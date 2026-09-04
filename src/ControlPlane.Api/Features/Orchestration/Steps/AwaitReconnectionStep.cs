namespace ControlPlane.Api.Features.Orchestration;

/// <summary>
/// Awaits agent reconnection over WebSocket following an operating system reboot.
/// Verifies the greeting and compares post-boot kernel progression against pre-reboot state.
/// </summary>
public class AwaitReconnectionStep : IJobStep
{
    private readonly TimeSpan _timeout;

    public string StepName => "Await Agent Reconnection";

    public AwaitReconnectionStep(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(300);
    }

    public async Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        if (context.State.ContainsKey("RebootSkipped"))
        {
            await context.EmitLogAsync("system", "[REBOOT] Host reboot was skipped; skipping await reconnection step.", ct);
            return JobStepResult.Succeeded("Skipped: Host was not rebooted.");
        }

        await context.EmitLogAsync(
            "system",
            $"[REBOOT] Waiting up to {_timeout.TotalSeconds}s for host '{context.TargetHost.Hostname}' to restart and re-establish agent connection...",
            ct
        );

        try
        {
            var session = await context.ConnectionManager.WaitForReconnectAsync(context.HostId, _timeout, ct);

            // Allow initial heartbeat to populate running kernel details
            string? postRebootKernel = null;
            for (var i = 0; i < 20; i++)
            {
                postRebootKernel = context.ConnectionManager.GetKernelVersion(context.HostId);
                if (!string.IsNullOrEmpty(postRebootKernel))
                {
                    break;
                }
                await Task.Delay(250, ct);
            }

            var preRebootKernel = context.State.TryGetValue("PreRebootKernel", out var preVal) ? preVal as string : null;

            if (!string.IsNullOrWhiteSpace(postRebootKernel))
            {
                if (!string.IsNullOrWhiteSpace(preRebootKernel) && !string.Equals(preRebootKernel, postRebootKernel, StringComparison.OrdinalIgnoreCase))
                {
                    await context.EmitLogAsync("system", $"[REBOOT] Node back online. Kernel updated to: {postRebootKernel} (previous: {preRebootKernel})", ct);
                }
                else
                {
                    await context.EmitLogAsync("system", $"[REBOOT] Node back online. Running kernel: {postRebootKernel}", ct);
                }
            }
            else
            {
                await context.EmitLogAsync("system", "[REBOOT] Node back online. WebSocket connection re-established.", ct);
            }

            return JobStepResult.Succeeded("Agent reconnected successfully.", targetState: UpdateJobState.Verifying);
        }
        catch (TimeoutException ex)
        {
            var msg = $"Reboot timeout: Host '{context.TargetHost.Hostname}' failed to reconnect within {_timeout.TotalSeconds} seconds.";
            await context.EmitLogAsync("system", $"[REBOOT] Error: {msg}", ct);
            return JobStepResult.Failed(msg, ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            var msg = $"Reboot timeout: Host '{context.TargetHost.Hostname}' failed to reconnect within {_timeout.TotalSeconds} seconds.";
            await context.EmitLogAsync("system", $"[REBOOT] Error: {msg}", ct);
            return JobStepResult.Failed(msg, ex);
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error awaiting reconnection for host {HostId}", context.HostId);
            await context.EmitLogAsync("system", $"[REBOOT] Error awaiting reconnection: {ex.Message}", ct);
            return JobStepResult.Failed($"Reconnection error: {ex.Message}", ex);
        }
    }

    public Task RollbackAsync(JobExecutionContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
