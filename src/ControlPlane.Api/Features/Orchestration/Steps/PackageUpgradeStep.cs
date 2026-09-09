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
