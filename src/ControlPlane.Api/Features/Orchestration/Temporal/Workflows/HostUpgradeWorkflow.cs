using ControlPlane.Api.Features.Orchestration.Temporal.Activities;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;
using Microsoft.Extensions.Logging;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Workflows;

[Workflow]
public class HostUpgradeWorkflow : IHostUpgradeWorkflow
{
    private readonly HostUpgradeWorkflowState _state = new();
    private bool _rebootApproved;
    private bool _cancelled;
    private string? _cancelReason;

    [WorkflowSignal]
    public Task ApproveRebootAsync()
    {
        _rebootApproved = true;
        _state.RebootApproved = true;
        try
        {
            Workflow.Logger.LogInformation("Operator reboot approval signal received");
        }
        catch
        {
            // Outside workflow execution context
        }
        return Task.CompletedTask;
    }

    [WorkflowSignal]
    public Task CancelAsync(string? reason = null)
    {
        _cancelled = true;
        _cancelReason = reason;
        _state.Cancelled = true;
        _state.CancelReason = reason;
        try
        {
            Workflow.Logger.LogWarning("Workflow cancel signal received. Reason: {Reason}", reason ?? "None provided");
        }
        catch
        {
            // Outside workflow execution context
        }
        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public HostUpgradeWorkflowState GetWorkflowState() => _state;

    [WorkflowRun]
    public async Task<HostUpgradeWorkflowResult> RunAsync(HostUpgradeWorkflowInput input)
    {
        var defaultOptions = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(2) };
        var preflightOptions = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(2) };
        var snapshotOptions = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(10) };
        var k8sOptions = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(5) };
        var drainOptions = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(5) };
        var upgradeOptions = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(30),
            HeartbeatTimeout = TimeSpan.FromSeconds(60)
        };
        var rebootOptions = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(2) };
        var reconnectOptions = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(10),
            HeartbeatTimeout = TimeSpan.FromSeconds(30)
        };
        var healthOptions = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(5) };

        var compensations = new List<Func<Task>>();
        bool isK8sCordoned = false;

        try
        {
            _state.Status = "Running";
            _state.ActiveStep = "Preflight Checks";

            // Step 0: Set Running status in DB
            await Workflow.ExecuteActivityAsync(
                (IUpdateJobActivities a) => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "Running", _state.ActiveStep)),
                defaultOptions
            );

            // Step 1: Preflight Heartbeat
            _state.ActiveStep = "Preflight: Heartbeat Freshness";
            var hbResult = await Workflow.ExecuteActivityAsync(
                (IPreflightActivities a) => a.CheckHeartbeatAsync(new PreflightHeartbeatInput(input.JobId, input.HostId, input.Hostname)),
                preflightOptions
            );
            if (!hbResult.Success)
            {
                throw new ApplicationFailureException(hbResult.Message, nonRetryable: true);
            }
            _state.CompletedSteps.Add(_state.ActiveStep);

            // Step 2: Preflight Disk Headroom
            _state.ActiveStep = "Preflight: Disk Headroom";
            var diskResult = await Workflow.ExecuteActivityAsync(
                (IPreflightActivities a) => a.CheckDiskHeadroomAsync(new PreflightDiskHeadroomInput(input.JobId, input.HostId, input.Hostname)),
                preflightOptions
            );
            if (!diskResult.Success)
            {
                throw new ApplicationFailureException(diskResult.Message, nonRetryable: true);
            }
            _state.CompletedSteps.Add(_state.ActiveStep);

            // Step 3: Preflight Package Lock Check
            _state.ActiveStep = "Preflight: Package Lock Check";
            var lockResult = await Workflow.ExecuteActivityAsync(
                (IPreflightActivities a) => a.CheckPackageLockAsync(new PreflightPackageLockInput(input.JobId, input.HostId, input.Hostname, input.OsFamily)),
                preflightOptions
            );
            if (!lockResult.Success)
            {
                throw new ApplicationFailureException(lockResult.Message, nonRetryable: true);
            }
            _state.CompletedSteps.Add(_state.ActiveStep);

            // Step 4: Proxmox Safety Snapshot
            _state.ActiveStep = "Hypervisor Safety Snapshot";
            await Workflow.ExecuteActivityAsync(
                (IUpdateJobActivities a) => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "Running", _state.ActiveStep)),
                defaultOptions
            );
            var snapResult = await Workflow.ExecuteActivityAsync(
                (IProxmoxActivities a) => a.CreateSnapshotAsync(new ProxmoxSnapshotInput(input.JobId, input.HostId, input.Hostname, input.TargetType, input.SnapshotName)),
                snapshotOptions
            );
            if (!snapResult.Success)
            {
                throw new ApplicationFailureException(snapResult.Message, nonRetryable: true);
            }
            if (snapResult.Created && !string.IsNullOrEmpty(snapResult.SnapshotName))
            {
                _state.SnapshotIdentifier = snapResult.SnapshotName;
                // Register Saga Compensation: Rollback snapshot on failure
                compensations.Add(async () =>
                {
                    await Workflow.ExecuteActivityAsync(
                        (IProxmoxActivities a) => a.RollbackSnapshotAsync(new ProxmoxRollbackInput(input.JobId, input.HostId, snapResult.SnapshotName, input.TargetType)),
                        new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(10) }
                    );
                });
            }
            _state.CompletedSteps.Add(_state.ActiveStep);

            // Step 5: Kubernetes Cordon
            _state.ActiveStep = "Kubernetes Node Cordon";
            await Workflow.ExecuteActivityAsync(
                (IUpdateJobActivities a) => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "Running", _state.ActiveStep)),
                defaultOptions
            );
            var cordonResult = await Workflow.ExecuteActivityAsync(
                (IKubernetesActivities a) => a.CordonNodeAsync(new KubernetesNodeInput(input.JobId, input.HostId, input.K8sNodeName)),
                k8sOptions
            );
            if (!cordonResult.Success)
            {
                throw new ApplicationFailureException(cordonResult.Message, nonRetryable: true);
            }

            if (cordonResult.Cordoned)
            {
                isK8sCordoned = true;
                _state.K8sNodeName = cordonResult.NodeName;
                // Register Saga Compensation: Uncordon node on failure
                compensations.Add(async () =>
                {
                    if (isK8sCordoned && _state.K8sNodeName != null)
                    {
                        await Workflow.ExecuteActivityAsync(
                            (IKubernetesActivities a) => a.UncordonNodeAsync(new KubernetesNodeInput(input.JobId, input.HostId, _state.K8sNodeName)),
                            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(5) }
                        );
                    }
                });

                // Step 6: Kubernetes Drain
                _state.ActiveStep = "Kubernetes Workload Eviction";
                await Workflow.ExecuteActivityAsync(
                    (IUpdateJobActivities a) => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "Running", _state.ActiveStep)),
                    defaultOptions
                );
                var drainResult = await Workflow.ExecuteActivityAsync(
                    (IKubernetesActivities a) => a.DrainNodeAsync(new KubernetesDrainInput(input.JobId, input.HostId, cordonResult.NodeName!)),
                    drainOptions
                );
                if (!drainResult.Success)
                {
                    throw new ApplicationFailureException(drainResult.Message, nonRetryable: true);
                }
                _state.CompletedSteps.Add(_state.ActiveStep);
            }
            _state.CompletedSteps.Add("Kubernetes Node Cordon");

            // Step 7: Package Upgrade Execution
            _state.ActiveStep = "Package Upgrade Execution";
            await Workflow.ExecuteActivityAsync(
                (IUpdateJobActivities a) => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "Running", _state.ActiveStep)),
                defaultOptions
            );
            var upgradeResult = await Workflow.ExecuteActivityAsync(
                (IAgentActivities a) => a.UpgradePackagesAsync(new AgentUpgradeInput(input.JobId, input.HostId, input.Hostname, input.OsFamily)),
                upgradeOptions
            );
            if (!upgradeResult.Success)
            {
                throw new ApplicationFailureException(upgradeResult.Message, nonRetryable: true);
            }
            _state.CompletedSteps.Add(_state.ActiveStep);

            // Step 8: Approval Gate (Human-in-the-Loop)
            if (input.RequireApprovalBeforeReboot)
            {
                _state.ActiveStep = "Awaiting Operator Reboot Approval";
                _state.AwaitingApproval = true;
                await Workflow.ExecuteActivityAsync(
                    (IUpdateJobActivities a) => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "AwaitingApproval", _state.ActiveStep)),
                    defaultOptions
                );

                var timeout = input.ApprovalTimeout ?? TimeSpan.FromHours(4);
                var signaled = await Workflow.WaitConditionAsync(() => _rebootApproved || _cancelled, timeout);

                _state.AwaitingApproval = false;
                if (_cancelled)
                {
                    throw new ApplicationFailureException($"Workflow cancelled by operator: {_cancelReason ?? "No reason given"}", nonRetryable: true);
                }
                if (!signaled || !_rebootApproved)
                {
                    throw new ApplicationFailureException($"Operator reboot approval timed out after {timeout.TotalHours} hours", nonRetryable: true);
                }
                _state.CompletedSteps.Add(_state.ActiveStep);
            }

            // Step 9: Reboot Initiation
            _state.ActiveStep = "Deterministic Host Reboot";
            await Workflow.ExecuteActivityAsync(
                (IUpdateJobActivities a) => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "Running", _state.ActiveStep)),
                defaultOptions
            );
            var rebootResult = await Workflow.ExecuteActivityAsync(
                (IAgentActivities a) => a.InitiateRebootAsync(new AgentRebootInput(input.JobId, input.HostId, input.Hostname, input.AlwaysReboot)),
                rebootOptions
            );
            if (!rebootResult.Success)
            {
                throw new ApplicationFailureException(rebootResult.Message, nonRetryable: true);
            }
            _state.CompletedSteps.Add(_state.ActiveStep);

            // Step 10: Await Reconnection (if reboot performed)
            if (!rebootResult.Skipped)
            {
                _state.ActiveStep = "Await Agent Reconnection";
                await Workflow.ExecuteActivityAsync(
                    (IUpdateJobActivities a) => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "AwaitingReconnect", _state.ActiveStep)),
                    defaultOptions
                );
                var reconnectResult = await Workflow.ExecuteActivityAsync(
                    (IAgentActivities a) => a.AwaitReconnectionAsync(new AgentReconnectInput(input.JobId, input.HostId, input.Hostname, false, rebootResult.PreRebootKernel)),
                    reconnectOptions
                );
                if (!reconnectResult.Success)
                {
                    throw new ApplicationFailureException(reconnectResult.Message, nonRetryable: true);
                }
                _state.CompletedSteps.Add(_state.ActiveStep);
            }

            // Step 11: Post-Flight Health Probes
            _state.ActiveStep = "Post-Flight Health Probes";
            await Workflow.ExecuteActivityAsync(
                (IUpdateJobActivities a) => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "Verifying", _state.ActiveStep)),
                defaultOptions
            );
            var healthResult = await Workflow.ExecuteActivityAsync(
                (IHealthProbeActivities a) => a.RunHealthProbesAsync(new HealthProbeInput(input.JobId, input.HostId, input.Hostname, input.OsFamily, input.ProbeUrls)),
                healthOptions
            );
            if (!healthResult.Success)
            {
                throw new ApplicationFailureException(healthResult.Message, nonRetryable: true);
            }
            _state.CompletedSteps.Add(_state.ActiveStep);

            // Step 12: Kubernetes Uncordon (if cordoned earlier)
            if (isK8sCordoned && _state.K8sNodeName != null)
            {
                _state.ActiveStep = "Kubernetes Node Uncordon";
                await Workflow.ExecuteActivityAsync(
                    (IUpdateJobActivities a) => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "Verifying", _state.ActiveStep)),
                    defaultOptions
                );
                await Workflow.ExecuteActivityAsync(
                    (IKubernetesActivities a) => a.UncordonNodeAsync(new KubernetesNodeInput(input.JobId, input.HostId, _state.K8sNodeName)),
                    k8sOptions
                );
                isK8sCordoned = false; // Successfully uncordoned, no compensation needed
                _state.CompletedSteps.Add(_state.ActiveStep);
            }

            // Mark Completed
            _state.Status = "Completed";
            _state.ActiveStep = null;
            await Workflow.ExecuteActivityAsync(
                (IUpdateJobActivities a) => a.RecordJobCompletionAsync(new RecordJobCompletionInput(input.JobId, "Completed", null)),
                defaultOptions
            );

            return new HostUpgradeWorkflowResult(
                Success: true,
                Status: "Completed",
                CompletedSteps: _state.CompletedSteps,
                SnapshotIdentifier: _state.SnapshotIdentifier,
                ErrorMessage: null
            );
        }
        catch (Exception ex)
        {
            Workflow.Logger.LogError(ex, "Host upgrade workflow execution failed during step: {ActiveStep}", _state.ActiveStep);
            var failureMsg = ex.Message;
            _state.FailureReason = failureMsg;

            // Execute Saga compensations in reverse order
            bool hadRollback = false;
            for (int i = compensations.Count - 1; i >= 0; i--)
            {
                try
                {
                    await compensations[i]();
                    hadRollback = true;
                }
                catch (Exception compEx)
                {
                    Workflow.Logger.LogError(compEx, "Saga compensation execution failed");
                }
            }

            var finalStatus = hadRollback && !string.IsNullOrWhiteSpace(_state.SnapshotIdentifier) ? "RolledBack" : "Failed";
            _state.Status = finalStatus;

            await Workflow.ExecuteActivityAsync(
                (IUpdateJobActivities a) => a.RecordJobCompletionAsync(new RecordJobCompletionInput(input.JobId, finalStatus, failureMsg)),
                defaultOptions
            );

            return new HostUpgradeWorkflowResult(
                Success: false,
                Status: finalStatus,
                CompletedSteps: _state.CompletedSteps,
                SnapshotIdentifier: _state.SnapshotIdentifier,
                ErrorMessage: failureMsg
            );
        }
    }
}
