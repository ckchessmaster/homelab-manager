using System.Text.Json;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Features.Hosts.WindowsLicense;
using ControlPlane.Api.Features.Orchestration.Temporal;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using EFCore.NamingConventions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ControlPlane.Api.Tests;

public class WindowsLicenseTests : IDisposable
{
    private class FakeAgentCommandExecutor : IAgentCommandExecutor
    {
        public Task<AgentCommandResult> ExecuteCommandAsync(
            Guid hostId,
            Guid jobId,
            string command,
            string[] args,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentCommandResult(true, 0, null, "Command completed successfully."));
        }

        public void NotifyFrame(Guid hostId, AgentFrameData frame) { }
    }

    private class FakeWorkflowLogEmitter : IWorkflowLogEmitter
    {
        public List<(Guid JobId, string StreamType, string LogLine)> Logs { get; } = new();

        public Task EmitLogAsync(Guid jobId, string streamType, string logLine, CancellationToken ct = default)
        {
            Logs.Add((jobId, streamType, logLine));
            return Task.CompletedTask;
        }

        public Task UpdateJobStatusAsync(Guid jobId, string status, string? activeStep, string? failureReason = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpdateJobStatusAsync(Guid jobId, string status, string? activeStep, string? failureReason, Guid? hostId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetSnapshotIdentifierAsync(Guid jobId, string? snapshotIdentifier, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private readonly SqliteConnection _conn;
    private readonly ControlPlaneDbContext _db;
    private readonly AgentConnectionManager _connectionManager;
    private readonly FakeAgentCommandExecutor _commandExecutor;
    private readonly FakeWorkflowLogEmitter _logEmitter;
    private readonly IServiceScopeFactory _scopeFactory;

    public WindowsLicenseTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseSqlite(_conn)
            .UseSnakeCaseNamingConvention()
            .Options;

        _db = new ControlPlaneDbContext(options);
        _db.Database.EnsureCreated();
        _connectionManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        _commandExecutor = new FakeAgentCommandExecutor();
        _logEmitter = new FakeWorkflowLogEmitter();

        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddSingleton(_connectionManager);
        services.AddSingleton<IAgentCommandExecutor>(_commandExecutor);
        services.AddSingleton<IWorkflowLogEmitter>(_logEmitter);
        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task GetWindowsLicenseAsync_ReturnsCorrectLicense_WhenPresent()
    {
        var hostId = Guid.NewGuid();
        var lic = new WindowsLicenseSummaryDto(
            IsEvaluation: true,
            Edition: "Windows Server 2022 Standard Evaluation",
            Description: "Windows Operating System, TIMEBASED_EVAL channel",
            LicenseStatus: "OOBGrace",
            GracePeriodMinutes: 10 * 24 * 60,
            DaysRemaining: 10,
            HoursRemaining: 240,
            ExpirationDate: DateTimeOffset.UtcNow.AddDays(10).ToString("o"),
            RearmsRemaining: 3,
            PartialProductKey: "ABCDE",
            IsExpiringSoon: true,
            IsCritical: false,
            CollectedAt: DateTimeOffset.UtcNow.ToString("o")
        );

        _db.Hosts.Add(new Host
        {
            Id = hostId,
            Hostname = "win-test-01",
            IpAddress = "192.168.1.150",
            OsFamily = "windows",
            TargetType = "proxmox_vm",
            WindowsLicenseJson = JsonSerializer.Serialize(lic)
        });
        await _db.SaveChangesAsync();

        var service = new WindowsLicenseService(
            _db,
            _connectionManager,
            Options.Create(new TemporalOptions()),
            _scopeFactory,
            NullLogger<WindowsLicenseService>.Instance);

        var result = await service.GetWindowsLicenseAsync(hostId);

        Assert.NotNull(result);
        Assert.True(result.IsEvaluation);
        Assert.Equal("Windows Server 2022 Standard Evaluation", result.Edition);
        Assert.Equal(10, result.DaysRemaining);
        Assert.Equal(3, result.RearmsRemaining);
        Assert.True(result.IsExpiringSoon);
        Assert.False(result.IsCritical);
    }

    [Fact]
    public async Task ProcessAgentWindowsLicenseHeartbeatAsync_SavesLicenseJson()
    {
        var hostId = Guid.NewGuid();
        _db.Hosts.Add(new Host
        {
            Id = hostId,
            Hostname = "win-test-02",
            IpAddress = "192.168.1.151",
            OsFamily = "windows",
            TargetType = "proxmox_vm"
        });
        await _db.SaveChangesAsync();

        var service = new WindowsLicenseService(
            _db,
            _connectionManager,
            Options.Create(new TemporalOptions()),
            _scopeFactory,
            NullLogger<WindowsLicenseService>.Instance);

        var heartbeatLic = new AgentWindowsLicenseSummary
        {
            IsEvaluation = true,
            Edition = "Windows Server 2022 Datacenter Evaluation",
            LicenseStatus = "OOBGrace",
            GracePeriodMinutes = 4320,
            DaysRemaining = 3,
            HoursRemaining = 72,
            ExpirationDate = DateTimeOffset.UtcNow.AddDays(3).ToString("o"),
            RearmsRemaining = 1,
            PartialProductKey = "DATAC",
            IsExpiringSoon = true,
            IsCritical = true,
            CollectedAt = DateTimeOffset.UtcNow.ToString("o")
        };

        await service.ProcessAgentWindowsLicenseHeartbeatAsync(hostId, heartbeatLic);

        var updatedHost = await _db.Hosts.FindAsync(hostId);
        Assert.NotNull(updatedHost?.WindowsLicenseJson);

        var parsed = JsonSerializer.Deserialize<WindowsLicenseSummaryDto>(
            updatedHost.WindowsLicenseJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(parsed);
        Assert.Equal("Windows Server 2022 Datacenter Evaluation", parsed.Edition);
        Assert.Equal(3, parsed.DaysRemaining);
        Assert.Equal(1, parsed.RearmsRemaining);
        Assert.True(parsed.IsCritical);
    }

    [Fact]
    public async Task RearmWindowsLicenseAsync_Rejects_WhenZeroRearmsRemaining()
    {
        var hostId = Guid.NewGuid();
        var zeroRearmLic = new WindowsLicenseSummaryDto(
            IsEvaluation: true,
            Edition: "Windows Server 2022 Standard Evaluation",
            Description: null,
            LicenseStatus: "Notification",
            GracePeriodMinutes: 0,
            DaysRemaining: 0,
            HoursRemaining: 0,
            ExpirationDate: null,
            RearmsRemaining: 0,
            PartialProductKey: "KEY00",
            IsExpiringSoon: true,
            IsCritical: true,
            CollectedAt: DateTimeOffset.UtcNow.ToString("o")
        );

        _db.Hosts.Add(new Host
        {
            Id = hostId,
            Hostname = "win-expired-01",
            IpAddress = "192.168.1.152",
            OsFamily = "windows",
            TargetType = "proxmox_vm",
            WindowsLicenseJson = JsonSerializer.Serialize(zeroRearmLic)
        });
        await _db.SaveChangesAsync();

        _connectionManager.RegisterSimulated(hostId, "win-expired-01");

        var service = new WindowsLicenseService(
            _db,
            _connectionManager,
            Options.Create(new TemporalOptions()),
            _scopeFactory,
            NullLogger<WindowsLicenseService>.Instance);

        var response = await service.RearmWindowsLicenseAsync(hostId);

        Assert.False(response.Success);
        Assert.Contains("0 rearms remaining", response.Message);
    }

    [Fact]
    public async Task WindowsLicenseActivities_PreflightAndRefresh_WorkSeamlessly()
    {
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var initialLic = new WindowsLicenseSummaryDto(
            IsEvaluation: true,
            Edition: "Windows Server 2022 Standard Evaluation",
            Description: "Windows Operating System, TIMEBASED_EVAL channel",
            LicenseStatus: "OOBGrace",
            GracePeriodMinutes: 1440,
            DaysRemaining: 1,
            HoursRemaining: 24,
            ExpirationDate: DateTimeOffset.UtcNow.AddDays(1).ToString("o"),
            RearmsRemaining: 2,
            PartialProductKey: "TEST1",
            IsExpiringSoon: true,
            IsCritical: true,
            CollectedAt: DateTimeOffset.UtcNow.ToString("o")
        );

        _db.Hosts.Add(new Host
        {
            Id = hostId,
            Hostname = "win-rearm-01",
            IpAddress = "192.168.1.153",
            OsFamily = "windows",
            TargetType = "proxmox_vm",
            WindowsLicenseJson = JsonSerializer.Serialize(initialLic)
        });
        await _db.SaveChangesAsync();

        _connectionManager.RegisterSimulated(hostId, "win-rearm-01");

        var activities = new WindowsLicenseActivities(
            _db,
            _connectionManager,
            _commandExecutor,
            _logEmitter,
            NullLogger<WindowsLicenseActivities>.Instance);

        var preflight = await activities.PreflightCheckAsync(new WindowsLicensePreflightInput(jobId, hostId, "win-rearm-01"));
        Assert.True(preflight.Success);
        Assert.Equal(2, preflight.RearmsRemaining);

        var rearmExec = await activities.ExecuteRearmAsync(new WindowsLicenseRearmExecutionInput(jobId, hostId, "win-rearm-01"));
        Assert.True(rearmExec.Success);

        var refresh = await activities.RefreshLicenseStatusAsync(new WindowsLicenseRefreshInput(jobId, hostId, "win-rearm-01"));
        Assert.True(refresh.Success);
        Assert.Equal(1, refresh.RearmsRemaining); // decremented from 2 to 1
        Assert.Equal(180, refresh.DaysRemaining); // reset to 180 days evaluation

        var updatedHost = await _db.Hosts.FindAsync(hostId);
        Assert.NotNull(updatedHost?.WindowsLicenseJson);
        var parsed = JsonSerializer.Deserialize<WindowsLicenseSummaryDto>(
            updatedHost.WindowsLicenseJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal(1, parsed?.RearmsRemaining);
        Assert.Equal(180, parsed?.DaysRemaining);
        Assert.False(parsed?.IsCritical);
    }
}
