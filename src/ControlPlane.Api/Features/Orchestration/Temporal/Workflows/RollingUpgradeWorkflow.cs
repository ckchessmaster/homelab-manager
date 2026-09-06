using ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Workflows;

[Workflow]
public class RollingUpgradeWorkflow : IRollingUpgradeWorkflow
{
    private readonly RollingUpgradeWorkflowState _state = new();
    private bool _isPaused;
    private bool _cancelled;
    private string? _cancelReason;

    [WorkflowSignal]
    public Task PauseAsync()
    {
        _isPaused = true;
        _state.IsPaused = true;
        if (_state.Status == "Running")
        {
            _state.Status = "Paused";
        }
        return Task.CompletedTask;
    }

    [WorkflowSignal]
    public Task ResumeAsync()
    {
        _isPaused = false;
        _state.IsPaused = false;
        if (_state.Status == "Paused")
        {
            _state.Status = "Running";
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
        _state.Status = "Cancelled";
        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public RollingUpgradeWorkflowState GetWorkflowState() => _state;

    [WorkflowRun]
    public async Task<RollingUpgradeWorkflowResult> RunAsync(RollingUpgradeWorkflowInput input)
    {
        _state.BatchId = input.BatchId;
        _state.Status = "Running";
        _state.TotalHosts = input.TargetHosts.Count;
        _state.CompletedHosts = 0;
        _state.FailedHosts = 0;

        foreach (var host in input.TargetHosts)
        {
            _state.HostProgresses[host.HostId.ToString()] = new RollingHostProgress
            {
                HostId = host.HostId,
                Hostname = host.Hostname,
                Status = "Pending"
            };
        }

        var maxParallelism = Math.Max(1, input.MaxParallelism);
        var targetQueue = new Queue<RollingHostTarget>(input.TargetHosts);
        bool firstHostProcessed = false;

        while (targetQueue.Count > 0)
        {
            // Check cancellation
            if (_cancelled)
            {
                MarkRemainingSkipped(targetQueue, "Workflow was cancelled by operator.");
                _state.Status = "Cancelled";
                return new RollingUpgradeWorkflowResult(
                    Success: false,
                    Status: "Cancelled",
                    TotalHosts: _state.TotalHosts,
                    CompletedHosts: _state.CompletedHosts,
                    FailedHosts: _state.FailedHosts,
                    ErrorMessage: _cancelReason ?? "Rolling upgrade cancelled."
                );
            }

            // Await pause or approval between hosts
            if (_isPaused || (input.RequireApprovalBetweenHosts && firstHostProcessed))
            {
                _state.Status = "Paused";
                _state.IsPaused = true;
                await Workflow.WaitConditionAsync(() => !_isPaused || _cancelled);

                if (_cancelled)
                {
                    continue; // Loop will handle cancel above
                }
                _state.Status = "Running";
                _state.IsPaused = false;
            }

            // Dequeue a batch of up to maxParallelism hosts
            var batch = new List<RollingHostTarget>();
            while (batch.Count < maxParallelism && targetQueue.Count > 0)
            {
                batch.Add(targetQueue.Dequeue());
            }

            // Execute batch child workflows in parallel
            var tasks = batch.Select(target => ProcessHostAsync(target, input)).ToList();
            var results = await Task.WhenAll(tasks);
            firstHostProcessed = true;

            // Evaluate failure strategy
            if (results.Any(r => !r.Success) && string.Equals(input.FailureStrategy, "StopOnFirstFailure", StringComparison.OrdinalIgnoreCase))
            {
                var failedResult = results.First(r => !r.Success);
                MarkRemainingSkipped(targetQueue, $"Rolling update halted after failure on '{failedResult.Hostname}'.");
                _state.Status = "Failed";
                _state.FailureReason = failedResult.ErrorMessage;

                return new RollingUpgradeWorkflowResult(
                    Success: false,
                    Status: "Failed",
                    TotalHosts: _state.TotalHosts,
                    CompletedHosts: _state.CompletedHosts,
                    FailedHosts: _state.FailedHosts,
                    ErrorMessage: _state.FailureReason
                );
            }
        }

        var allSuccessful = _state.FailedHosts == 0;
        _state.Status = allSuccessful ? "Completed" : "PartiallyFailed";
        _state.ActiveHostId = null;
        _state.ActiveHostname = null;

        return new RollingUpgradeWorkflowResult(
            Success: allSuccessful,
            Status: _state.Status,
            TotalHosts: _state.TotalHosts,
            CompletedHosts: _state.CompletedHosts,
            FailedHosts: _state.FailedHosts,
            ErrorMessage: allSuccessful ? null : $"{_state.FailedHosts} of {_state.TotalHosts} hosts failed."
        );
    }

    private async Task<(bool Success, string Hostname, string? ErrorMessage)> ProcessHostAsync(
        RollingHostTarget target,
        RollingUpgradeWorkflowInput input)
    {
        var hostKey = target.HostId.ToString();
        var progress = _state.HostProgresses[hostKey];

        progress.Status = "Running";
        progress.StartedAt = Workflow.UtcNow;
        _state.ActiveHostId = target.HostId;
        _state.ActiveHostname = target.Hostname;

        var childJobId = Workflow.NewGuid();
        var childWorkflowId = $"host-upgrade-{target.HostId}-{input.BatchId}";
        progress.ChildWorkflowId = childWorkflowId;

        var snapshotName = !string.IsNullOrWhiteSpace(input.SnapshotPrefix)
            ? $"{input.SnapshotPrefix}-{target.Hostname}"
            : null;

        var childInput = new HostUpgradeWorkflowInput(
            JobId: childJobId,
            HostId: target.HostId,
            Hostname: target.Hostname,
            TargetType: target.TargetType,
            OsFamily: target.OsFamily,
            RequireApprovalBeforeReboot: input.RequireApprovalBeforeReboot,
            AlwaysReboot: input.AlwaysReboot,
            ProbeUrls: input.ProbeUrls,
            SnapshotName: snapshotName,
            K8sNodeName: target.K8sNodeName ?? target.Hostname
        );

        try
        {
            var childOptions = new ChildWorkflowOptions
            {
                Id = childWorkflowId,
            };

            var childResult = await Workflow.ExecuteChildWorkflowAsync(
                (IHostUpgradeWorkflow w) => w.RunAsync(childInput),
                childOptions
            );

            if (childResult.Success)
            {
                progress.Status = "Completed";
                progress.CompletedAt = Workflow.UtcNow;
                _state.CompletedHosts++;
                return (true, target.Hostname, null);
            }
            else
            {
                progress.Status = "Failed";
                progress.CompletedAt = Workflow.UtcNow;
                progress.ErrorMessage = childResult.ErrorMessage ?? "Child workflow execution failed.";
                _state.FailedHosts++;
                return (false, target.Hostname, progress.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            progress.Status = "Failed";
            progress.CompletedAt = Workflow.UtcNow;
            progress.ErrorMessage = ex.Message;
            _state.FailedHosts++;
            return (false, target.Hostname, ex.Message);
        }
    }

    private void MarkRemainingSkipped(Queue<RollingHostTarget> queue, string reason)
    {
        while (queue.Count > 0)
        {
            var remaining = queue.Dequeue();
            var progress = _state.HostProgresses[remaining.HostId.ToString()];
            progress.Status = "Skipped";
            progress.ErrorMessage = reason;
            progress.CompletedAt = Workflow.UtcNow;
        }
    }
}
