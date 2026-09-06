using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public class PreflightActivities : IPreflightActivities
{
    private readonly AgentConnectionManager _connectionManager;
    private readonly IAgentCommandExecutor _commandExecutor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkflowLogEmitter _logEmitter;
    private readonly ILogger<PreflightActivities> _logger;

    public PreflightActivities(
        AgentConnectionManager connectionManager,
        IAgentCommandExecutor commandExecutor,
        IServiceScopeFactory scopeFactory,
        IWorkflowLogEmitter logEmitter,
        ILogger<PreflightActivities> logger)
    {
        _connectionManager = connectionManager;
        _commandExecutor = commandExecutor;
        _scopeFactory = scopeFactory;
        _logEmitter = logEmitter;
        _logger = logger;
    }

    [Activity]
    public async Task<PreflightCheckResult> CheckHeartbeatAsync(PreflightHeartbeatInput input)
    {
        var isOnline = _connectionManager.IsOnline(input.HostId);
        DateTimeOffset? lastSeen = null;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var host = await db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == input.HostId);
            lastSeen = host?.Agent.LastSeenAt;
        }

        if (!isOnline)
        {
            var msg = $"Target host '{input.Hostname}' agent is offline or WebSocket connection is inactive.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] Error: {msg}");
            return new PreflightCheckResult(false, msg);
        }

        if (!lastSeen.HasValue)
        {
            var msg = $"No heartbeat received yet from host '{input.Hostname}'.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] Error: {msg}");
            return new PreflightCheckResult(false, msg);
        }

        var age = DateTimeOffset.UtcNow - lastSeen.Value;
        var limit = TimeSpan.FromSeconds(input.MaxHeartbeatAgeSeconds);
        if (age > limit)
        {
            var msg = $"Agent heartbeat is stale ({age.TotalSeconds:F1}s old > limit of {limit.TotalSeconds:F1}s).";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] Error: {msg}");
            return new PreflightCheckResult(false, msg);
        }

        var successMsg = $"Heartbeat verified: agent is online and healthy (last seen {age.TotalSeconds:F1}s ago).";
        await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] {successMsg}");
        return new PreflightCheckResult(true, successMsg);
    }

    [Activity]
    public async Task<PreflightCheckResult> CheckDiskHeadroomAsync(PreflightDiskHeadroomInput input)
    {
        double diskFreePct = -1;

        // 1. Check cached agent metrics
        var metrics = _connectionManager.GetLatestMetrics(input.HostId);
        if (metrics != null && metrics.DiskFreePct > 0)
        {
            diskFreePct = metrics.DiskFreePct;
        }
        else
        {
            // 2. Query dynamically via df command
            var cmdResult = await _commandExecutor.ExecuteCommandAsync(
                input.HostId,
                input.JobId,
                "sh",
                new[] { "-c", "df --output=pcent / | tail -n 1" }
            );

            if (cmdResult.Success)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
                var lastLog = await db.StepLogs
                    .Where(l => l.JobId == input.JobId && l.StreamType == "stdout")
                    .OrderByDescending(l => l.SequenceId)
                    .Select(l => l.LogLine)
                    .FirstOrDefaultAsync();

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
            var msg = "Unable to determine root filesystem free space on target host.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] Error: {msg}");
            return new PreflightCheckResult(false, msg);
        }

        if (diskFreePct < input.MinFreePct)
        {
            var msg = $"Insufficient root filesystem headroom: {diskFreePct:F1}% free, minimum {input.MinFreePct:F1}% required.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] Error: {msg}");
            return new PreflightCheckResult(false, msg);
        }

        var successMsg = $"Root filesystem headroom verified: {diskFreePct:F1}% available (threshold: {input.MinFreePct:F1}%).";
        await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] {successMsg}");
        return new PreflightCheckResult(true, successMsg);
    }

    [Activity]
    public async Task<PreflightCheckResult> CheckPackageLockAsync(PreflightPackageLockInput input)
    {
        var osFamily = input.OsFamily?.ToLowerInvariant() ?? "";

        string checkScript;
        if (osFamily.Contains("rhel") || osFamily.Contains("centos") || osFamily.Contains("fedora"))
        {
            checkScript = "for f in /var/lib/rpm/.rpm.lock; do if [ -e \"$f\" ] && fuser \"$f\" >/dev/null 2>&1; then echo \"Locked: $f\"; exit 1; fi; done; echo \"No locks\"; exit 0";
        }
        else
        {
            // Default Debian/Ubuntu
            checkScript = "for f in /var/lib/dpkg/lock-frontend /var/lib/dpkg/lock /var/lib/apt/lists/lock; do if [ -e \"$f\" ] && fuser \"$f\" >/dev/null 2>&1; then echo \"Locked: $f\"; exit 1; fi; done; echo \"No locks\"; exit 0";
        }

        var result = await _commandExecutor.ExecuteCommandAsync(
            input.HostId,
            input.JobId,
            "sh",
            new[] { "-c", checkScript }
        );

        if (!result.Success)
        {
            var msg = $"Package manager lock detected on host '{input.Hostname}': {result.ErrorMessage ?? "lock file is held by another process"}";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] Error: {msg}");
            return new PreflightCheckResult(false, msg);
        }

        var successMsg = "No active package manager locks detected.";
        await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] {successMsg}");
        return new PreflightCheckResult(true, successMsg);
    }
}
