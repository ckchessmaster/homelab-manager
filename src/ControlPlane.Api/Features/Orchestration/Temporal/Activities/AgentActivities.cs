using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public class AgentActivities : IAgentActivities
{
    private readonly AgentConnectionManager _connectionManager;
    private readonly IAgentCommandExecutor _commandExecutor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkflowLogEmitter _logEmitter;
    private readonly ILogger<AgentActivities> _logger;

    public AgentActivities(
        AgentConnectionManager connectionManager,
        IAgentCommandExecutor commandExecutor,
        IServiceScopeFactory scopeFactory,
        IWorkflowLogEmitter logEmitter,
        ILogger<AgentActivities> logger)
    {
        _connectionManager = connectionManager;
        _commandExecutor = commandExecutor;
        _scopeFactory = scopeFactory;
        _logEmitter = logEmitter;
        _logger = logger;
    }

    [Activity]
    public async Task<AgentUpgradeResult> UpgradePackagesAsync(AgentUpgradeInput input)
    {
        var osFamily = input.OsFamily?.ToLowerInvariant() ?? "";

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
            // Resilient Debian / Ubuntu noninteractive dist-upgrade with automated remediation and lock timeout
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
                "  echo \"[UPGRADE] apt-get dist-upgrade returned exit code $STATUS. Attempting auto-remediation (dpkg --configure -a, apt-get install -f)...\"\n" +
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

        await _logEmitter.EmitLogAsync(
            input.JobId,
            "system",
            $"[UPGRADE] Executing package upgrade via {command} {string.Join(' ', args)}"
        );

        using var heartbeatCts = new CancellationTokenSource();
        var heartbeatTask = Task.Run(async () =>
        {
            while (!heartbeatCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), heartbeatCts.Token);
                    ActivityExecutionContext.Current.Heartbeat("Package upgrade execution in progress");
                }
                catch
                {
                    break;
                }
            }
        });

        try
        {
            var result = await _commandExecutor.ExecuteCommandAsync(
                input.HostId,
                input.JobId,
                command,
                args
            );

            if (!result.Success)
            {
                var errorMsg = $"Package upgrade failed with exit code {result.ExitCode}: {result.ErrorMessage ?? "unknown error"}";
                await _logEmitter.EmitLogAsync(input.JobId, "system", $"[UPGRADE] Error: {errorMsg}");
                return new AgentUpgradeResult(false, result.ExitCode, errorMsg);
            }

            var successMsg = "Package upgrade completed successfully.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[UPGRADE] {successMsg}");
            return new AgentUpgradeResult(true, 0, successMsg);
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeatTask; } catch { }
        }
    }

    [Activity]
    public async Task<AgentRebootResult> InitiateRebootAsync(AgentRebootInput input)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var host = await db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == input.HostId);

        var pendingReboot = host?.Agent?.PendingReboot ?? false;
        var needsReboot = input.AlwaysReboot || pendingReboot;

        if (!needsReboot && _connectionManager.IsOnline(input.HostId))
        {
            var checkScript = "if [ -f /var/run/reboot-required ] || [ -f /run/reboot-required ]; then exit 0; fi; if command -v needrestart >/dev/null 2>&1 && needrestart -b 2>/dev/null | grep -Eq 'NEEDRESTART-KSTA: [23]'; then exit 0; fi; if command -v needs-restarting >/dev/null 2>&1 && ! needs-restarting -r >/dev/null 2>&1; then exit 0; fi; exit 1";
            var probeResult = await _commandExecutor.ExecuteCommandAsync(
                input.HostId,
                input.JobId,
                "sh",
                new[] { "-c", checkScript }
            );
            if (probeResult.Success)
            {
                needsReboot = true;
                if (host != null)
                {
                    host.Agent.PendingReboot = true;
                    await db.SaveChangesAsync();
                }
                await _logEmitter.EmitLogAsync(input.JobId, "system", "[REBOOT] Live probe detected pending reboot flag on node. Proceeding with reboot.");
            }
        }

        if (!needsReboot)
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", "[REBOOT] No pending reboot required for host; skipping host reboot.");
            return new AgentRebootResult(true, true, null, "Skipped: Host does not require a reboot.");
        }

        if (!_connectionManager.IsOnline(input.HostId))
        {
            var offlineMsg = $"Cannot initiate reboot: Agent for host '{input.Hostname}' is currently offline.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[REBOOT] Error: {offlineMsg}");
            return new AgentRebootResult(false, false, null, offlineMsg);
        }

        // Cache pre-reboot kernel version for progression comparison
        var preRebootKernel = _connectionManager.GetKernelVersion(input.HostId);
        if (!string.IsNullOrWhiteSpace(preRebootKernel))
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[REBOOT] Pre-reboot kernel recorded: {preRebootKernel}");
        }

        var rebootEnvelope = new AgentCommandEnvelope
        {
            Type = "CMD_REBOOT",
            JobId = input.JobId,
            Command = "systemctl",
            Args = new[] { "reboot" }
        };

        await _logEmitter.EmitLogAsync(
            input.JobId,
            "system",
            $"[REBOOT] Dispatching CMD_REBOOT handshake to agent on host '{input.Hostname}'..."
        );

        var dispatched = await _connectionManager.SendCommandAsync(input.HostId, rebootEnvelope);
        if (!dispatched)
        {
            var dispatchError = "Failed to dispatch CMD_REBOOT envelope to agent over WebSocket.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[REBOOT] Error: {dispatchError}");
            return new AgentRebootResult(false, false, null, dispatchError);
        }

        // Wait for REBOOT_COMMENCING acknowledgment
        var timeout = TimeSpan.FromSeconds(input.HandshakeTimeoutSeconds);
        var acknowledged = await _connectionManager.WaitForRebootCommencingAsync(input.HostId, timeout);
        if (acknowledged)
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", "[REBOOT] Agent acknowledged REBOOT_COMMENCING. System restart initiated.");
        }
        else
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", "[REBOOT] Warning: No immediate REBOOT_COMMENCING acknowledgment received. Proceeding with disconnect watch.");
        }

        return new AgentRebootResult(true, false, preRebootKernel, "Reboot handshake completed. Awaiting node restart.");
    }

    [Activity]
    public async Task<AgentReconnectResult> AwaitReconnectionAsync(AgentReconnectInput input)
    {
        if (input.RebootSkipped)
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", "[REBOOT] Host reboot was skipped; skipping await reconnection step.");
            return new AgentReconnectResult(true, null, "Skipped: Host was not rebooted.");
        }

        var timeout = TimeSpan.FromSeconds(input.TimeoutSeconds);
        await _logEmitter.EmitLogAsync(
            input.JobId,
            "system",
            $"[REBOOT] Waiting up to {timeout.TotalSeconds}s for host '{input.Hostname}' to restart and re-establish agent connection..."
        );

        using var heartbeatCts = new CancellationTokenSource();
        var heartbeatTask = Task.Run(async () =>
        {
            while (!heartbeatCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), heartbeatCts.Token);
                    ActivityExecutionContext.Current.Heartbeat("Waiting for agent WebSocket reconnection");
                }
                catch
                {
                    break;
                }
            }
        });

        try
        {
            var session = await _connectionManager.WaitForReconnectAsync(input.HostId, timeout);

            // Allow initial heartbeat to populate running kernel details
            string? postRebootKernel = null;
            for (var i = 0; i < 20; i++)
            {
                postRebootKernel = _connectionManager.GetKernelVersion(input.HostId);
                if (!string.IsNullOrEmpty(postRebootKernel))
                {
                    break;
                }
                await Task.Delay(250);
            }

            if (!string.IsNullOrWhiteSpace(postRebootKernel))
            {
                if (!string.IsNullOrWhiteSpace(input.PreRebootKernel) && !string.Equals(input.PreRebootKernel, postRebootKernel, StringComparison.OrdinalIgnoreCase))
                {
                    await _logEmitter.EmitLogAsync(input.JobId, "system", $"[REBOOT] Node back online. Kernel updated to: {postRebootKernel} (previous: {input.PreRebootKernel})");
                }
                else
                {
                    await _logEmitter.EmitLogAsync(input.JobId, "system", $"[REBOOT] Node back online. Running kernel: {postRebootKernel}");
                }
            }
            else
            {
                await _logEmitter.EmitLogAsync(input.JobId, "system", "[REBOOT] Node back online. WebSocket connection re-established.");
            }

            return new AgentReconnectResult(true, postRebootKernel, "Agent reconnected successfully.");
        }
        catch (TimeoutException)
        {
            var msg = $"Reboot timeout: Host '{input.Hostname}' failed to reconnect within {timeout.TotalSeconds} seconds.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[REBOOT] Error: {msg}");
            return new AgentReconnectResult(false, null, msg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error awaiting reconnection for host {HostId}", input.HostId);
            var msg = $"Reconnection error: {ex.Message}";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[REBOOT] Error: {msg}");
            return new AgentReconnectResult(false, null, msg);
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeatTask; } catch { }
        }
    }
}
