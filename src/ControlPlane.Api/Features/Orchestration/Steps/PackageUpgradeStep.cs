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
            command = "dnf";
            args = new[] { "upgrade", "-y" };
        }
        else
        {
            // Default Debian / Ubuntu noninteractive dist-upgrade
            command = "sh";
            args = new[]
            {
                "-c",
                "DEBIAN_FRONTEND=noninteractive apt-get dist-upgrade -y -o Dpkg::Options::=\"--force-confdef\" -o Dpkg::Options::=\"--force-confold\""
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
