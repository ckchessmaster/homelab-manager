using ControlPlane.Api.Features.Agents.Models;

namespace ControlPlane.Api.Features.Orchestration;

/// <summary>
/// Executes a deterministic reboot handshake with the compute node agent.
/// Caches the running kernel version, sends CMD_REBOOT, awaits acknowledgment, and transitions the job to AwaitingReconnect.
/// </summary>
public class DeterministicRebootStep : IJobStep
{
    private readonly bool _alwaysReboot;
    private readonly TimeSpan _handshakeTimeout;

    public string StepName => "Deterministic Host Reboot";

    public DeterministicRebootStep(bool alwaysReboot = false, TimeSpan? handshakeTimeout = null)
    {
        _alwaysReboot = alwaysReboot;
        _handshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        var forceReboot = context.State.ContainsKey("ForceReboot");
        var needsReboot = _alwaysReboot || forceReboot || context.TargetHost.Agent.PendingReboot;

        if (!needsReboot)
        {
            context.State["RebootSkipped"] = true;
            await context.EmitLogAsync("system", "[REBOOT] No pending reboot required for host; skipping host reboot.", ct);
            return JobStepResult.Succeeded("Skipped: Host does not require a reboot.");
        }

        if (!context.ConnectionManager.IsOnline(context.HostId))
        {
            var offlineMsg = $"Cannot initiate reboot: Agent for host '{context.TargetHost.Hostname}' is currently offline.";
            await context.EmitLogAsync("system", $"[REBOOT] Error: {offlineMsg}", ct);
            return JobStepResult.Failed(offlineMsg);
        }

        // Cache pre-reboot kernel version for progression comparison
        var preRebootKernel = context.ConnectionManager.GetKernelVersion(context.HostId);
        if (!string.IsNullOrWhiteSpace(preRebootKernel))
        {
            context.State["PreRebootKernel"] = preRebootKernel;
            await context.EmitLogAsync("system", $"[REBOOT] Pre-reboot kernel recorded: {preRebootKernel}", ct);
        }

        var rebootEnvelope = new AgentCommandEnvelope
        {
            Type = "CMD_REBOOT",
            JobId = context.JobId,
            Command = "systemctl",
            Args = new[] { "reboot" }
        };

        await context.EmitLogAsync(
            "system",
            $"[REBOOT] Dispatching CMD_REBOOT handshake to agent on host '{context.TargetHost.Hostname}'...",
            ct
        );

        var dispatched = await context.ConnectionManager.SendCommandAsync(context.HostId, rebootEnvelope, ct);
        if (!dispatched)
        {
            var dispatchError = "Failed to dispatch CMD_REBOOT envelope to agent over WebSocket.";
            await context.EmitLogAsync("system", $"[REBOOT] Error: {dispatchError}", ct);
            return JobStepResult.Failed(dispatchError);
        }

        // Wait for REBOOT_COMMENCING acknowledgment
        var acknowledged = await context.ConnectionManager.WaitForRebootCommencingAsync(context.HostId, _handshakeTimeout, ct);
        if (acknowledged)
        {
            await context.EmitLogAsync("system", "[REBOOT] Agent acknowledged REBOOT_COMMENCING. System restart initiated.", ct);
        }
        else
        {
            await context.EmitLogAsync("system", "[REBOOT] Warning: No immediate REBOOT_COMMENCING acknowledgment received. Proceeding with disconnect watch.", ct);
        }

        await context.UpdateJobStatusAsync(UpdateJobState.AwaitingReconnect, StepName, ct: ct);
        return JobStepResult.Succeeded("Reboot handshake completed. Awaiting node restart.", targetState: UpdateJobState.AwaitingReconnect);
    }

    public async Task RollbackAsync(JobExecutionContext context, CancellationToken ct)
    {
        await context.EmitLogAsync(
            "system",
            "[REBOOT] Host reboot cannot be reversed directly; relying on hypervisor snapshot rollback if configured.",
            ct
        );
    }
}
