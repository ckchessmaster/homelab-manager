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
        var osFamily = context.TargetHost.OsFamily?.ToLowerInvariant() ?? "";
        var isWindows = osFamily.Contains("windows");

        // 1. Check actual storage requirements directly on the node if online
        if (context.ConnectionManager.IsOnline(context.HostId))
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

            var cmdResult = await context.CommandExecutor.ExecuteCommandAsync(
                context.HostId,
                context.JobId,
                command,
                args,
                ct
            );

            using var scope = context.ScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Storage.ControlPlaneDbContext>();
            var lastLog = db.StepLogs
                .Where(l => l.JobId == context.JobId && l.StreamType == "stdout" && l.LogLine.Contains("STORAGE_CHECK|"))
                .OrderByDescending(l => l.SequenceId)
                .Select(l => l.LogLine)
                .FirstOrDefault();

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
                    return JobStepResult.Succeeded(
                        $"Root filesystem storage verified: {avail ?? "unknown"} MB available, ~{req ?? "1024"} MB required for upgrade (with 50% buffer). Free: {free ?? "unknown"}%."
                    );
                }
                else
                {
                    return JobStepResult.Failed(
                        $"Insufficient root filesystem space: {avail} MB available, but upgrade requires at least {req} MB (including 50% safety buffer)."
                    );
                }
            }
        }

        // 2. Fallback: Check cached agent metrics from heartbeat
        var metrics = context.ConnectionManager.GetLatestMetrics(context.HostId);
        var diskFreePct = metrics?.DiskFreePct ?? -1;

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
