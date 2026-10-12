using System.Text.Json;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Orchestration.Temporal;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using ControlPlane.Api.Features.Orchestration.Temporal.Workflows;
using ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Temporalio.Client;

namespace ControlPlane.Api.Features.Hosts.WindowsLicense;

public class WindowsLicenseService : IWindowsLicenseService
{
    private readonly ControlPlaneDbContext _db;
    private readonly AgentConnectionManager _connectionManager;
    private readonly ITemporalClient? _temporalClient;
    private readonly IOptions<TemporalOptions> _temporalOptions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WindowsLicenseService> _logger;

    public WindowsLicenseService(
        ControlPlaneDbContext db,
        AgentConnectionManager connectionManager,
        IOptions<TemporalOptions> temporalOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<WindowsLicenseService> logger,
        ITemporalClient? temporalClient = null)
    {
        _db = db;
        _connectionManager = connectionManager;
        _temporalOptions = temporalOptions;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _temporalClient = temporalClient;
    }

    public async Task<WindowsLicenseSummaryDto?> GetWindowsLicenseAsync(Guid hostId, bool refresh = false, CancellationToken ct = default)
    {
        var host = await _db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == hostId, ct);
        if (host == null) return null;

        if (refresh && _connectionManager.IsOnline(host.Id))
        {
            var session = _connectionManager.GetSession(host.Id);
            if (session != null && session.IsSimulated)
            {
                // In demo/simulated mode, ensure valid mock state is present
                if (string.IsNullOrWhiteSpace(host.WindowsLicenseJson))
                {
                    var mock = new WindowsLicenseSummaryDto(
                        IsEvaluation: true,
                        Edition: "Windows Server 2022 Standard Evaluation",
                        Description: "Windows Operating System, TIMEBASED_EVAL channel",
                        LicenseStatus: "OOBGrace",
                        GracePeriodMinutes: 5 * 24 * 60,
                        DaysRemaining: 5,
                        HoursRemaining: 120,
                        ExpirationDate: DateTimeOffset.UtcNow.AddDays(5).ToString("o"),
                        RearmsRemaining: 2,
                        PartialProductKey: "ABCDE",
                        IsExpiringSoon: true,
                        IsCritical: false,
                        CollectedAt: DateTimeOffset.UtcNow.ToString("o")
                    );
                    var entry = await _db.Hosts.FirstOrDefaultAsync(h => h.Id == hostId, ct);
                    if (entry != null)
                    {
                        entry.WindowsLicenseJson = JsonSerializer.Serialize(mock, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        await _db.SaveChangesAsync(ct);
                        return mock;
                    }
                }
            }
            else
            {
                await _connectionManager.SendCommandAsync(host.Id, new AgentCommandEnvelope { Type = "CMD_LICENSE_SCAN" }, ct);
            }
        }

        if (string.IsNullOrWhiteSpace(host.WindowsLicenseJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WindowsLicenseSummaryDto>(
                host.WindowsLicenseJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize WindowsLicenseJson for host {HostId}", hostId);
            return null;
        }
    }

    public async Task ProcessAgentWindowsLicenseHeartbeatAsync(Guid hostId, AgentWindowsLicenseSummary license, CancellationToken ct = default)
    {
        var host = await _db.Hosts.FirstOrDefaultAsync(h => h.Id == hostId, ct);
        if (host == null) return;

        var dto = new WindowsLicenseSummaryDto(
            IsEvaluation: license.IsEvaluation,
            Edition: license.Edition,
            Description: license.Description,
            LicenseStatus: license.LicenseStatus,
            GracePeriodMinutes: license.GracePeriodMinutes,
            DaysRemaining: license.DaysRemaining,
            HoursRemaining: license.HoursRemaining,
            ExpirationDate: license.ExpirationDate,
            RearmsRemaining: license.RearmsRemaining,
            PartialProductKey: license.PartialProductKey,
            IsExpiringSoon: license.IsExpiringSoon,
            IsCritical: license.IsCritical,
            CollectedAt: license.CollectedAt,
            Error: license.Error
        );

        host.WindowsLicenseJson = JsonSerializer.Serialize(dto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        host.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug("Saved Windows license heartbeat telemetry for {Hostname} ({HostId}): {Days} days, {Rearms} rearms",
            host.Hostname, host.Id, dto.DaysRemaining, dto.RearmsRemaining);
    }

    public async Task<RearmWindowsLicenseResponse> RearmWindowsLicenseAsync(
        Guid hostId,
        bool rebootAfter = false,
        string initiatedBy = "Operator",
        CancellationToken ct = default)
    {
        var host = await _db.Hosts.FirstOrDefaultAsync(h => h.Id == hostId, ct);
        if (host == null)
        {
            return new RearmWindowsLicenseResponse(false, Guid.Empty, null, "Failed", $"Host '{hostId}' not found.");
        }

        if (!host.OsFamily.ToLowerInvariant().Contains("windows"))
        {
            return new RearmWindowsLicenseResponse(false, Guid.Empty, null, "Failed", $"Host '{host.Hostname}' is not a Windows machine.");
        }

        if (!_connectionManager.IsOnline(host.Id))
        {
            return new RearmWindowsLicenseResponse(false, Guid.Empty, null, "Failed", $"Agent for Windows host '{host.Hostname}' is offline.");
        }

        // Verify remaining rearms
        if (!string.IsNullOrWhiteSpace(host.WindowsLicenseJson))
        {
            try
            {
                var lic = JsonSerializer.Deserialize<WindowsLicenseSummaryDto>(
                    host.WindowsLicenseJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (lic != null && lic.RearmsRemaining <= 0)
                {
                    return new RearmWindowsLicenseResponse(false, Guid.Empty, null, "Failed",
                        $"Host '{host.Hostname}' has 0 rearms remaining. System cannot be rearmed.");
                }
            }
            catch { }
        }

        var jobId = Guid.NewGuid();
        var job = new UpdateJob
        {
            Id = jobId,
            TargetHostId = host.Id,
            PipelineId = "windows-license-rearm",
            InitiatedBy = initiatedBy,
            Status = "Running",
            ActiveStep = "PreflightCheck",
            StartedAt = DateTimeOffset.UtcNow
        };

        _db.UpdateJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        if (_temporalClient != null)
        {
            var workflowId = $"windows-license-rearm-{jobId}";
            try
            {
                await _temporalClient.StartWorkflowAsync<IWindowsLicenseRearmWorkflow>(
                    wf => wf.RunAsync(new WindowsLicenseRearmWorkflowInput(jobId, host.Id, host.Hostname, rebootAfter, initiatedBy)),
                    new WorkflowOptions
                    {
                        Id = workflowId,
                        TaskQueue = _temporalOptions.Value.TaskQueue
                    });

                return new RearmWindowsLicenseResponse(true, jobId, workflowId, "Running",
                    $"Windows Server license rearm Temporal workflow started for {host.Hostname}.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Windows license rearm Temporal workflow for {Hostname}", host.Hostname);
                job.Status = "Failed";
                job.FailureReason = $"Failed to start Temporal workflow: {ex.Message}";
                job.CompletedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);

                return new RearmWindowsLicenseResponse(false, jobId, null, "Failed", ex.Message);
            }
        }

        // Fallback execution when Temporal is unavailable (Standby mode or local SQLite)
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var licActivities = scope.ServiceProvider.GetRequiredService<IWindowsLicenseActivities>();
            var jobActivities = scope.ServiceProvider.GetRequiredService<IUpdateJobActivities>();
            var agentActivities = scope.ServiceProvider.GetRequiredService<IAgentActivities>();

            try
            {
                await jobActivities.UpdateJobStatusAsync(new UpdateJobStatusInput(jobId, "Running", "PreflightCheck", null, host.Id));
                var preflight = await licActivities.PreflightCheckAsync(new WindowsLicensePreflightInput(jobId, host.Id, host.Hostname));
                if (!preflight.Success)
                {
                    await jobActivities.RecordJobCompletionAsync(new RecordJobCompletionInput(jobId, "Failed", preflight.Message));
                    return;
                }

                await jobActivities.UpdateJobStatusAsync(new UpdateJobStatusInput(jobId, "Running", "ExecuteRearm", null, host.Id));
                var rearm = await licActivities.ExecuteRearmAsync(new WindowsLicenseRearmExecutionInput(jobId, host.Id, host.Hostname));
                if (!rearm.Success)
                {
                    await jobActivities.RecordJobCompletionAsync(new RecordJobCompletionInput(jobId, "Failed", rearm.Message));
                    return;
                }

                if (rebootAfter)
                {
                    await jobActivities.UpdateJobStatusAsync(new UpdateJobStatusInput(jobId, "Running", "RebootHost", null, host.Id));
                    var reboot = await agentActivities.InitiateRebootAsync(new AgentRebootInput(jobId, host.Id, host.Hostname, true, 5, "windows"));

                    await jobActivities.UpdateJobStatusAsync(new UpdateJobStatusInput(jobId, "AwaitingReconnect", "WaitForReconnect", null, host.Id));
                    var reconnect = await agentActivities.AwaitReconnectionAsync(new AgentReconnectInput(jobId, host.Id, host.Hostname, reboot.Skipped, reboot.PreRebootKernel, 300));
                    if (!reconnect.Success)
                    {
                        await jobActivities.RecordJobCompletionAsync(new RecordJobCompletionInput(jobId, "Failed", reconnect.Message));
                        return;
                    }
                }

                await jobActivities.UpdateJobStatusAsync(new UpdateJobStatusInput(jobId, "Running", "RefreshLicenseStatus", null, host.Id));
                await licActivities.RefreshLicenseStatusAsync(new WindowsLicenseRefreshInput(jobId, host.Id, host.Hostname));

                await jobActivities.RecordJobCompletionAsync(new RecordJobCompletionInput(jobId, "Completed", null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "In-process fallback license rearm failed for {Hostname}", host.Hostname);
                await jobActivities.RecordJobCompletionAsync(new RecordJobCompletionInput(jobId, "Failed", ex.Message));
            }
        });

        return new RearmWindowsLicenseResponse(true, jobId, null, "Running",
            $"Windows Server license rearm started for {host.Hostname}.");
    }
}
