using System.Text.Json;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public class WindowsLicenseActivities : IWindowsLicenseActivities
{
    private readonly ControlPlaneDbContext _db;
    private readonly AgentConnectionManager _connectionManager;
    private readonly IAgentCommandExecutor _commandExecutor;
    private readonly IWorkflowLogEmitter _logEmitter;
    private readonly ILogger<WindowsLicenseActivities> _logger;

    public WindowsLicenseActivities(
        ControlPlaneDbContext db,
        AgentConnectionManager connectionManager,
        IAgentCommandExecutor commandExecutor,
        IWorkflowLogEmitter logEmitter,
        ILogger<WindowsLicenseActivities> logger)
    {
        _db = db;
        _connectionManager = connectionManager;
        _commandExecutor = commandExecutor;
        _logEmitter = logEmitter;
        _logger = logger;
    }

    [Activity]
    public async Task<WindowsLicensePreflightResult> PreflightCheckAsync(WindowsLicensePreflightInput input)
    {
        await _logEmitter.EmitLogAsync(input.JobId, "stdout", $"[PREFLIGHT] Validating Windows node state for {input.Hostname}...");

        var host = await _db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == input.HostId);
        if (host == null)
        {
            var msg = $"Host '{input.HostId}' not found.";
            await _logEmitter.EmitLogAsync(input.JobId, "stderr", $"[PREFLIGHT] [ERROR] {msg}");
            return new WindowsLicensePreflightResult(false, 0, "", msg);
        }

        if (!host.OsFamily.ToLowerInvariant().Contains("windows"))
        {
            var msg = $"Host '{host.Hostname}' OS family is '{host.OsFamily}', which is not Windows.";
            await _logEmitter.EmitLogAsync(input.JobId, "stderr", $"[PREFLIGHT] [ERROR] {msg}");
            return new WindowsLicensePreflightResult(false, 0, "", msg);
        }

        if (!_connectionManager.IsOnline(host.Id))
        {
            var msg = $"Agent for Windows host '{host.Hostname}' is currently offline.";
            await _logEmitter.EmitLogAsync(input.JobId, "stderr", $"[PREFLIGHT] [ERROR] {msg}");
            return new WindowsLicensePreflightResult(false, 0, "", msg);
        }

        int rearms = 1;
        string edition = "Windows Server";
        if (!string.IsNullOrWhiteSpace(host.WindowsLicenseJson))
        {
            try
            {
                var lic = JsonSerializer.Deserialize<WindowsLicenseSummaryDto>(
                    host.WindowsLicenseJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (lic != null)
                {
                    rearms = lic.RearmsRemaining;
                    edition = lic.Edition;
                }
            }
            catch
            {
                // ignore
            }
        }

        if (rearms <= 0)
        {
            var msg = $"Windows Server license on {host.Hostname} has 0 rearms remaining. System cannot be rearmed.";
            await _logEmitter.EmitLogAsync(input.JobId, "stderr", $"[PREFLIGHT] [ERROR] {msg}");
            return new WindowsLicensePreflightResult(false, 0, edition, msg);
        }

        await _logEmitter.EmitLogAsync(input.JobId, "stdout", $"[PREFLIGHT] Verified: Agent is online. Edition: '{edition}'. Remaining rearms: {rearms}.");
        return new WindowsLicensePreflightResult(true, rearms, edition, "Preflight check passed.");
    }

    [Activity]
    public async Task<WindowsLicenseRearmExecutionResult> ExecuteRearmAsync(WindowsLicenseRearmExecutionInput input)
    {
        await _logEmitter.EmitLogAsync(input.JobId, "stdout", $"[REARM] Dispatching slmgr.vbs /rearm to {input.Hostname}...");

        var session = _connectionManager.GetSession(input.HostId);
        if (session != null && session.IsSimulated)
        {
            await Task.Delay(1000);
            await _logEmitter.EmitLogAsync(input.JobId, "stdout", "[REARM] Executing: cscript.exe //nologo C:\\Windows\\System32\\slmgr.vbs /rearm");
            await _logEmitter.EmitLogAsync(input.JobId, "stdout", "[REARM] Command completed successfully.");
            await _logEmitter.EmitLogAsync(input.JobId, "stdout", "[REARM] Please restart the system for the changes to take effect.");
            return new WindowsLicenseRearmExecutionResult(true, "Simulated rearm succeeded.");
        }

        var command = "powershell.exe";
        var args = new[]
        {
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-Command",
            "& { cscript.exe //nologo C:\\Windows\\System32\\slmgr.vbs /rearm }"
        };

        var result = await _commandExecutor.ExecuteCommandAsync(input.HostId, input.JobId, command, args);
        if (!result.Success)
        {
            var errMsg = $"Rearm command failed with exit code {result.ExitCode}: {result.ErrorMessage}";
            await _logEmitter.EmitLogAsync(input.JobId, "stderr", $"[REARM] [ERROR] {errMsg}");
            return new WindowsLicenseRearmExecutionResult(false, errMsg);
        }

        await _logEmitter.EmitLogAsync(input.JobId, "stdout", "[REARM] License rearm executed successfully.");
        return new WindowsLicenseRearmExecutionResult(true, "License rearm succeeded.");
    }

    [Activity]
    public async Task<WindowsLicenseRefreshResult> RefreshLicenseStatusAsync(WindowsLicenseRefreshInput input)
    {
        await _logEmitter.EmitLogAsync(input.JobId, "stdout", $"[REFRESH] Updating license telemetry for {input.Hostname}...");

        var host = await _db.Hosts.FirstOrDefaultAsync(h => h.Id == input.HostId);
        if (host == null)
        {
            return new WindowsLicenseRefreshResult(false, 0, 0, "Host not found.");
        }

        var session = _connectionManager.GetSession(input.HostId);
        if (session != null && session.IsSimulated)
        {
            int oldRearms = 2;
            string edition = "Windows Server 2022 Standard Evaluation";
            string key = "ABCDE";
            if (!string.IsNullOrWhiteSpace(host.WindowsLicenseJson))
            {
                try
                {
                    var lic = JsonSerializer.Deserialize<WindowsLicenseSummaryDto>(
                        host.WindowsLicenseJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (lic != null)
                    {
                        oldRearms = lic.RearmsRemaining;
                        edition = lic.Edition;
                        key = lic.PartialProductKey ?? "ABCDE";
                    }
                }
                catch { }
            }

            var newRearms = Math.Max(0, oldRearms - 1);
            var newDays = 180;
            var newExp = DateTimeOffset.UtcNow.AddDays(newDays).ToString("o");

            var updatedLic = new WindowsLicenseSummaryDto(
                IsEvaluation: true,
                Edition: edition,
                Description: "Windows Operating System, TIMEBASED_EVAL channel",
                LicenseStatus: "Licensed",
                GracePeriodMinutes: newDays * 24 * 60,
                DaysRemaining: newDays,
                HoursRemaining: newDays * 24,
                ExpirationDate: newExp,
                RearmsRemaining: newRearms,
                PartialProductKey: key,
                IsExpiringSoon: false,
                IsCritical: newRearms == 0,
                CollectedAt: DateTimeOffset.UtcNow.ToString("o")
            );

            host.WindowsLicenseJson = JsonSerializer.Serialize(updatedLic, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            host.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();

            await _logEmitter.EmitLogAsync(input.JobId, "stdout", $"[REFRESH] Updated license state: Evaluation reset to {newDays} days. Remaining rearms: {newRearms}.");
            return new WindowsLicenseRefreshResult(true, newRearms, newDays, "License state refreshed.");
        }

        // On-demand scan for connected physical/virtual node
        var scanEnv = new AgentCommandEnvelope { Type = "CMD_LICENSE_SCAN" };
        await _connectionManager.SendCommandAsync(host.Id, scanEnv);

        await _logEmitter.EmitLogAsync(input.JobId, "stdout", "[REFRESH] Dispatched CMD_LICENSE_SCAN to agent.");
        return new WindowsLicenseRefreshResult(true, 1, 180, "License scan requested.");
    }
}
