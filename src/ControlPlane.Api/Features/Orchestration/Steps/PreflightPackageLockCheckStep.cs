namespace ControlPlane.Api.Features.Orchestration;

public class PreflightPackageLockCheckStep : IJobStep
{
    public string StepName => "Preflight: Package Lock Check";

    public async Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        var osFamily = context.TargetHost.OsFamily?.ToLowerInvariant() ?? "";

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
