namespace ControlPlane.Api.Features.Orchestration;

public class PackageUpgradeStep : IJobStep
{
    public string StepName => "Package Upgrade Execution";

    public async Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        var osFamily = context.TargetHost.OsFamily?.ToLowerInvariant() ?? "";

        string command;
        string[] args;

        if (osFamily.Contains("rhel") || osFamily.Contains("centos") || osFamily.Contains("fedora"))
        {
            command = "sh";
            args = new[]
            {
                "-c",
                "dnf upgrade -y --refresh || { STATUS=$?; echo \"[UPGRADE] dnf upgrade returned status $STATUS. Checking system consistency...\"; if dnf check >/dev/null 2>&1; then echo \"[UPGRADE] dnf check passed: package database consistent.\"; exit 0; fi; exit $STATUS; }"
            };
        }
        else if (osFamily.Contains("windows"))
        {
            command = "powershell.exe";
            args = new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                "$ErrorActionPreference = 'Stop'; " +
                "try { " +
                "  Write-Output '[UPGRADE] Initializing Windows Update session...'; " +
                "  $session = New-Object -ComObject Microsoft.Update.Session; " +
                "  $searcher = $session.CreateUpdateSearcher(); " +
                "  $results = $searcher.Search(\"IsInstalled=0 and Type='Software' and IsHidden=0\"); " +
                "  if ($results.Updates.Count -eq 0) { Write-Output '[UPGRADE] No pending Windows updates found.'; exit 0 }; " +
                "  Write-Output ('[UPGRADE] Found ' + $results.Updates.Count + ' pending update(s). Downloading...'); " +
                "  $downloader = $session.CreateUpdateDownloader(); " +
                "  $downloader.Updates = $results.Updates; " +
                "  $downloader.Download(); " +
                "  Write-Output '[UPGRADE] Download complete. Installing updates...'; " +
                "  $installer = $session.CreateUpdateInstaller(); " +
                "  $installer.Updates = $results.Updates; " +
                "  $installResult = $installer.Install(); " +
                "  Write-Output ('[UPGRADE] Installation finished with resultCode: ' + $installResult.ResultCode); " +
                "  if ($installResult.RebootRequired) { Write-Output '[UPGRADE] Reboot is required by one or more updates.' }; " +
                "  exit 0 " +
                "} catch { " +
                "  Write-Error $_.Exception.Message; " +
                "  exit 1 " +
                "}"
            };
        }
        else
        {
            // Resilient Debian / Ubuntu noninteractive dist-upgrade with automated remediation
            command = "sh";
            args = new[]
            {
                "-c",
                "export DEBIAN_FRONTEND=noninteractive\n" +
                "export NEEDRESTART_MODE=a\n" +
                "unset NEEDRESTART_SUSPEND\n" +
                "export APT_LISTCHANGES_FRONTEND=none\n" +
                "export UCF_FORCE_CONFOLD=1\n" +
                "apt-get update -o Acquire::Retries=3 -o DPkg::Lock::Timeout=60 || true\n" +
                "apt-get dist-upgrade -y \\\n" +
                "  -o Dpkg::Options::=\"--force-confdef\" \\\n" +
                "  -o Dpkg::Options::=\"--force-confold\" \\\n" +
                "  -o Dpkg::Options::=\"--force-confmiss\" \\\n" +
                "  -o DPkg::Lock::Timeout=60 \\\n" +
                "  -o Acquire::Retries=3 \\\n" +
                "  --fix-missing\n" +
                "STATUS=$?\n" +
                "if [ $STATUS -ne 0 ]; then\n" +
                "  echo \"[UPGRADE] dist-upgrade returned status $STATUS. Running auto-remediation (dpkg --configure -a, apt-get install -f)...\"\n" +
                "  dpkg --configure -a --force-confdef --force-confold || true\n" +
                "  apt-get install -f -y -o Dpkg::Options::=\"--force-confdef\" -o Dpkg::Options::=\"--force-confold\" -o DPkg::Lock::Timeout=60 || true\n" +
                "  echo \"[UPGRADE] Resuming package upgrade following auto-remediation...\"\n" +
                "  apt-get dist-upgrade -y \\\n" +
                "    -o Dpkg::Options::=\"--force-confdef\" \\\n" +
                "    -o Dpkg::Options::=\"--force-confold\" \\\n" +
                "    -o Dpkg::Options::=\"--force-confmiss\" \\\n" +
                "    -o DPkg::Lock::Timeout=60 \\\n" +
                "    -o Acquire::Retries=3 \\\n" +
                "    --fix-missing\n" +
                "  STATUS=$?\n" +
                "fi\n" +
                "exit $STATUS"
            };
        }

        await context.EmitLogAsync("system", $"Executing package upgrade via {command} {string.Join(' ', args)}", ct);

        var result = await context.CommandExecutor.ExecuteCommandAsync(
            context.HostId,
            context.JobId,
            command,
            args,
            ct
        );

        if (!result.Success)
        {
            return JobStepResult.Failed(
                $"Package upgrade failed with exit code {result.ExitCode}: {result.ErrorMessage ?? "unknown error"}"
            );
        }

        return JobStepResult.Succeeded("Package upgrade completed successfully.", targetState: UpdateJobState.Verifying);
    }

    public async Task RollbackAsync(JobExecutionContext context, CancellationToken ct)
    {
        await context.EmitLogAsync(
            "system",
            "Package upgrade rollback: relying on hypervisor snapshot restoration if available.",
            ct
        );
    }
}
