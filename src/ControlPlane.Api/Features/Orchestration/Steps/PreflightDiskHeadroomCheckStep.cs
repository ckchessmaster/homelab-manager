using ControlPlane.Api.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Api.Features.Orchestration;

public class PreflightDiskHeadroomCheckStep : IJobStep
{
    private readonly double _minFreePct;

    public string StepName => "Preflight: Disk Headroom";

    public PreflightDiskHeadroomCheckStep(double minFreePct = 20.0)
    {
        _minFreePct = minFreePct;
    }

    public async Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        double diskFreePct = -1;

        // 1. Check cached agent metrics from heartbeat
        var metrics = context.ConnectionManager.GetLatestMetrics(context.HostId);
        if (metrics != null && metrics.DiskFreePct > 0)
        {
            diskFreePct = metrics.DiskFreePct;
        }
        else
        {
            // 2. Query dynamically via df command
            var cmdResult = await context.CommandExecutor.ExecuteCommandAsync(
                context.HostId,
                context.JobId,
                "sh",
                new[] { "-c", "df --output=pcent / | tail -n 1" },
                ct
            );

            if (cmdResult.Success)
            {
                // In StepLogs, find the stdout line or parse from last log
                using var scope = context.ScopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Storage.ControlPlaneDbContext>();
                var lastLog = db.StepLogs
                    .Where(l => l.JobId == context.JobId && l.StreamType == "stdout")
                    .OrderByDescending(l => l.SequenceId)
                    .Select(l => l.LogLine)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(lastLog))
                {
                    var cleaned = lastLog.Trim().TrimEnd('%');
                    if (double.TryParse(cleaned, out var usedPct))
                    {
                        diskFreePct = 100.0 - usedPct;
                    }
                }
            }
        }

        if (diskFreePct < 0)
        {
            return JobStepResult.Failed("Unable to determine root filesystem free space on target host.");
        }

        if (diskFreePct < _minFreePct)
        {
            return JobStepResult.Failed(
                $"Insufficient root filesystem headroom: {diskFreePct:F1}% free, minimum {_minFreePct:F1}% required."
            );
        }

        return JobStepResult.Succeeded(
            $"Root filesystem headroom verified: {diskFreePct:F1}% available (threshold: {_minFreePct:F1}%)."
        );
    }

    public Task RollbackAsync(JobExecutionContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
