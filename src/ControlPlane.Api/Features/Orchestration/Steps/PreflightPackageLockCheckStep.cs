namespace ControlPlane.Api.Features.Orchestration;

public class PreflightPackageLockCheckStep : IJobStep
{
    public string StepName => "Preflight: Package Lock Check";

    public async Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        var osFamily = context.TargetHost.OsFamily?.ToLowerInvariant() ?? "";

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
            var winResult = await context.CommandExecutor.ExecuteCommandAsync(
                context.HostId,
                context.JobId,
                "powershell.exe",
                new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", winScript },
                ct
            );

            if (!winResult.Success)
            {
                return JobStepResult.Failed(
                    $"Windows installer lock detected on host '{context.TargetHost.Hostname}': active Windows Update or MSI installer process is currently running."
                );
            }

            return JobStepResult.Succeeded("No active Windows installer locks detected.");
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

        var result = await context.CommandExecutor.ExecuteCommandAsync(
            context.HostId,
            context.JobId,
            "sh",
            new[] { "-c", checkScript },
            ct
        );

        if (!result.Success)
        {
            return JobStepResult.Failed(
                $"Package manager lock detected on host '{context.TargetHost.Hostname}': {result.ErrorMessage ?? "lock file is held by another process"}"
            );
        }

        return JobStepResult.Succeeded("No active package manager locks detected.");
    }

    public Task RollbackAsync(JobExecutionContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
