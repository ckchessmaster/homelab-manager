namespace ControlPlane.Api.Features.Orchestration;

public class PreflightHeartbeatCheckStep : IJobStep
{
    private readonly TimeSpan _maxHeartbeatAge;

    public string StepName => "Preflight: Heartbeat Freshness";

    public PreflightHeartbeatCheckStep(TimeSpan? maxHeartbeatAge = null)
    {
        _maxHeartbeatAge = maxHeartbeatAge ?? TimeSpan.FromSeconds(15);
    }

    public Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        var isOnline = context.ConnectionManager.IsOnline(context.HostId);
        var lastSeen = context.TargetHost.Agent.LastSeenAt;

        if (!isOnline)
        {
            return Task.FromResult(JobStepResult.Failed(
                $"Target host '{context.TargetHost.Hostname}' agent is offline or WebSocket connection is inactive."
            ));
        }

        if (!lastSeen.HasValue)
        {
            return Task.FromResult(JobStepResult.Failed(
                $"No heartbeat received yet from host '{context.TargetHost.Hostname}'."
            ));
        }

        var age = DateTimeOffset.UtcNow - lastSeen.Value;
        if (age > _maxHeartbeatAge)
        {
            return Task.FromResult(JobStepResult.Failed(
                $"Agent heartbeat is stale ({age.TotalSeconds:F1}s old > limit of {_maxHeartbeatAge.TotalSeconds}s)."
            ));
        }

        return Task.FromResult(JobStepResult.Succeeded(
            $"Heartbeat verified: agent is online and healthy (last seen {age.TotalSeconds:F1}s ago)."
        ));
    }

    public Task RollbackAsync(JobExecutionContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
