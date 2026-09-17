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
        var isWindows = input.OsFamily?.ToLowerInvariant().Contains("windows") == true;

        // 1. Check actual storage requirements directly on the node if online
        if (_connectionManager.IsOnline(input.HostId))
        {
            string command;
            string[] args;

            if (isWindows)
            {
                command = "powershell.exe";
                var winScript =
                    "$drive = Get-PSDrive -Name ($env:SystemDrive.TrimEnd(':')) -PSProvider FileSystem; " +
                    "$availMb = [math]::Round($drive.Free / 1MB); " +
                    "$totalMb = [math]::Round(($drive.Used + $drive.Free) / 1MB); " +
                    "$usedPct = [math]::Round(($drive.Used / ($drive.Used + $drive.Free)) * 100); " +
                    "$freePct = 100 - $usedPct; " +
                    "$reqMb = 2048; " +
                    "Write-Output \"STORAGE_CHECK|avail_mb=$availMb|req_mb=$reqMb|total_mb=$totalMb|free_pct=$freePct\"; " +
                    "if ($availMb -ge $reqMb) { exit 0 } else { exit 1 }";
                args = new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", winScript };
            }
            else
            {
                command = "sh";
                var probeScript =
                    "avail_mb=$(df -B1M --output=avail / 2>/dev/null | tail -n 1 | tr -dc '0-9'); " +
                    "total_mb=$(df -B1M --output=size / 2>/dev/null | tail -n 1 | tr -dc '0-9'); " +
                    "used_pct=$(df --output=pcent / 2>/dev/null | tail -n 1 | tr -dc '0-9'); " +
                    "free_pct=$((100 - used_pct)); " +
                    "req_mb=1024; " +
                    "if command -v apt-get >/dev/null 2>&1; then " +
                    "  sim=$(apt-get -s dist-upgrade 2>/dev/null); " +
                    "  dl_kb=$(echo \"$sim\" | awk '/Need to get/ { val = $4; gsub(/,/, \"\", val); unit = tolower($5); if (unit ~ /^g/) val = val * 1048576; else if (unit ~ /^m/) val = val * 1024; else if (unit ~ /^b/) val = val / 1024; print int(val); }'); " +
                    "  inst_kb=$(echo \"$sim\" | awk '/additional disk space will be used/ { val = $4; gsub(/,/, \"\", val); unit = tolower($5); if (unit ~ /^g/) val = val * 1048576; else if (unit ~ /^m/) val = val * 1024; else if (unit ~ /^b/) val = val / 1024; print int(val); }'); " +
                    "  dl_kb=${dl_kb:-0}; " +
                    "  inst_kb=${inst_kb:-0}; " +
                    "  tot_kb=$((dl_kb + inst_kb)); " +
                    "  if [ \"$tot_kb\" -gt 0 ]; then " +
                    "    req_kb=$(( (tot_kb * 15 / 10) + 524288 )); " +
                    "    req_mb=$((req_kb / 1024)); " +
                    "  fi; " +
                    "fi; " +
                    "if [ \"$req_mb\" -lt 1024 ]; then req_mb=1024; fi; " +
                    "echo \"STORAGE_CHECK|avail_mb=$avail_mb|req_mb=$req_mb|total_mb=$total_mb|free_pct=$free_pct\"; " +
                    "if [ \"$avail_mb\" -ge \"$req_mb\" ]; then exit 0; else exit 1; fi";
                args = new[] { "-c", probeScript };
            }

            var cmdResult = await _commandExecutor.ExecuteCommandAsync(
                input.HostId,
                input.JobId,
                command,
                args
            );

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var lastLog = await db.StepLogs
                .Where(l => l.JobId == input.JobId && l.StreamType == "stdout" && l.LogLine.Contains("STORAGE_CHECK|"))
                .OrderByDescending(l => l.SequenceId)
                .Select(l => l.LogLine)
                .FirstOrDefaultAsync();

            var rawLog = lastLog ?? (cmdResult.ErrorMessage?.Contains("STORAGE_CHECK|") == true ? cmdResult.ErrorMessage : null);

            if (!string.IsNullOrWhiteSpace(rawLog))
            {
                var idx = rawLog.IndexOf("STORAGE_CHECK|");
                var checkPart = idx >= 0 ? rawLog[idx..] : rawLog;
                var parts = checkPart.Split('|');
                var avail = parts.FirstOrDefault(p => p.StartsWith("avail_mb="))?["avail_mb=".Length..];
                var req = parts.FirstOrDefault(p => p.StartsWith("req_mb="))?["req_mb=".Length..];
                var free = parts.FirstOrDefault(p => p.StartsWith("free_pct="))?["free_pct=".Length..];

                if (cmdResult.Success)
                {
                    var okMsg = $"Root filesystem storage verified: {avail ?? "unknown"} MB available, ~{req ?? "1024"} MB required for upgrade (with 50% buffer). Free: {free ?? "unknown"}%.";
                    await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] {okMsg}");
                    return new PreflightCheckResult(true, okMsg);
                }
                else
                {
                    var failMsg = $"Insufficient root filesystem space: {avail} MB available, but upgrade requires at least {req} MB (including 50% safety buffer).";
                    await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] Error: {failMsg}");
                    return new PreflightCheckResult(false, failMsg);
                }
            }
        }

        // 2. Fallback: check cached metrics if live probe was unavailable or not logged
        var metrics = _connectionManager.GetLatestMetrics(input.HostId);
        var diskFreePct = metrics?.DiskFreePct ?? -1;

        if (diskFreePct >= 0)
        {
            if (diskFreePct < input.MinFreePct)
            {
                var msg = $"Insufficient root filesystem headroom: {diskFreePct:F1}% free, minimum {input.MinFreePct:F1}% required.";
                await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] Error: {msg}");
                return new PreflightCheckResult(false, msg);
            }

            var fallbackMsg = $"Root filesystem headroom verified: {diskFreePct:F1}% available (threshold: {input.MinFreePct:F1}%).";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] {fallbackMsg}");
            return new PreflightCheckResult(true, fallbackMsg);
        }

        var errMsg = "Unable to determine root filesystem free space on target host.";
        await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] Error: {errMsg}");
        return new PreflightCheckResult(false, errMsg);
    }

    [Activity]
    public async Task<PreflightCheckResult> CheckPackageLockAsync(PreflightPackageLockInput input)
    {
        var osFamily = input.OsFamily?.ToLowerInvariant() ?? "";

        if (osFamily.Contains("windows"))
        {
            var winScript =
                "$timeout = 60; " +
                "while ($timeout -gt 0) { " +
                "  $procs = Get-Process -Name 'msiexec', 'TiWorker' -ErrorAction SilentlyContinue; " +
                "  if (-not $procs) { Write-Output 'No locks'; exit 0 }; " +
                "  $c1 = ($procs | Measure-Object -Property CPU -Sum).Sum; " +
                "  Start-Sleep -Seconds 1; " +
                "  $procs2 = Get-Process -Name 'msiexec', 'TiWorker' -ErrorAction SilentlyContinue; " +
                "  if (-not $procs2) { Write-Output 'No locks'; exit 0 }; " +
                "  $c2 = ($procs2 | Measure-Object -Property CPU -Sum).Sum; " +
                "  $delta = $c2 - $c1; " +
                "  if ($delta -le 0.1) { Write-Output 'No locks'; exit 0 }; " +
                "  $activeNames = ($procs2 | Select-Object -ExpandProperty Name -Unique) -join ', '; " +
                "  Write-Output ('[PREFLIGHT] Waiting for active Windows update/installer process (' + $activeNames + ') to complete...'); " +
                "  Start-Sleep -Seconds 5; " +
                "  $timeout -= 6; " +
                "}; " +
                "$procs = Get-Process -Name 'msiexec', 'TiWorker' -ErrorAction SilentlyContinue; " +
                "if ($procs) { " +
                "  $c1 = ($procs | Measure-Object -Property CPU -Sum).Sum; " +
                "  Start-Sleep -Seconds 1; " +
                "  $procs2 = Get-Process -Name 'msiexec', 'TiWorker' -ErrorAction SilentlyContinue; " +
                "  if ($procs2) { " +
                "    $c2 = ($procs2 | Measure-Object -Property CPU -Sum).Sum; " +
                "    if (($c2 - $c1) -gt 0.1) { " +
                "      $activeNames = ($procs2 | Select-Object -ExpandProperty Name -Unique) -join ', '; " +
                "      Write-Output ('Locked: ' + $activeNames + ' active'); " +
                "      exit 1; " +
                "    } " +
                "  } " +
                "}; " +
                "Write-Output 'No locks'; exit 0";
            var winResult = await _commandExecutor.ExecuteCommandAsync(
                input.HostId,
                input.JobId,
                "powershell.exe",
                new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", winScript }
            );

            if (!winResult.Success)
            {
                var msg = $"Windows installer lock detected on host '{input.Hostname}': active Windows Update or MSI installer process is currently running.";
                await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] Error: {msg}");
                return new PreflightCheckResult(false, msg);
            }

            var winSuccess = "No active Windows installer locks detected.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PREFLIGHT] {winSuccess}");
            return new PreflightCheckResult(true, winSuccess);
        }

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
