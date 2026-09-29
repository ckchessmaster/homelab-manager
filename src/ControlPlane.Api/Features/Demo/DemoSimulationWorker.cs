using System.Security.Cryptography;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Demo;

/// <summary>
/// Background worker that simulates dynamic homelab telemetry (CPU/RAM jitter) and handles
/// state transitions during simulated reboots when Demo Mode is active.
/// </summary>
public class DemoSimulationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentConnectionManager _connectionManager;
    private readonly DemoOptions _options;
    private readonly ILogger<DemoSimulationWorker> _logger;

    public DemoSimulationWorker(
        IServiceScopeFactory scopeFactory,
        AgentConnectionManager connectionManager,
        IOptions<DemoOptions> options,
        ILogger<DemoSimulationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionManager = connectionManager;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DemoSimulationWorker started. Simulating homelab dynamics every {Interval}s...", _options.SimulationTickSeconds);

        // Initial delay to let DB migrations and initial seeding complete
        await Task.Delay(1000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ApplyTelemetryJitter();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error occurred during demo simulation tick.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.SimulationTickSeconds)), stoppingToken);
        }

        _logger.LogInformation("DemoSimulationWorker stopped.");
    }

    private void ApplyTelemetryJitter()
    {
        var hosts = new[]
        {
            (DemoDataSeeder.HostPveNode01Id, BaseCpu: 28.0, BaseRam: 45.0, BaseDisk: 72.0),
            (DemoDataSeeder.HostK8sCp01Id, BaseCpu: 18.0, BaseRam: 52.0, BaseDisk: 64.0),
            (DemoDataSeeder.HostK8sWorker01Id, BaseCpu: 34.0, BaseRam: 68.0, BaseDisk: 55.0),
            (DemoDataSeeder.HostK8sWorker02Id, BaseCpu: 41.0, BaseRam: 62.0, BaseDisk: 59.0),
            (DemoDataSeeder.HostStorageNas01Id, BaseCpu: 12.0, BaseRam: 82.0, BaseDisk: 42.0)
        };

        foreach (var (hostId, baseCpu, baseRam, baseDisk) in hosts)
        {
            var session = _connectionManager.GetSession(hostId);
            if (session == null || !session.IsSimulated)
            {
                continue;
            }

            // Generate slight realistic sinusoidal/jitter variation (+/- 3%)
            var jitterCpu = (RandomNumberGenerator.GetInt32(-30, 31) / 10.0);
            var jitterRam = (RandomNumberGenerator.GetInt32(-15, 16) / 10.0);

            var cpu = Math.Clamp(Math.Round(baseCpu + jitterCpu, 1), 1.0, 99.0);
            var ram = Math.Clamp(Math.Round(baseRam + jitterRam, 1), 1.0, 99.0);
            var disk = Math.Clamp(Math.Round(baseDisk, 1), 1.0, 99.0);

            _connectionManager.UpdateMetrics(hostId, new AgentMetrics { CpuUsagePct = cpu, MemoryUsagePct = ram, DiskFreePct = disk });
        }
    }

    /// <summary>
    /// Simulates a temporary node offline period during a reboot, then brings the node back online.
    /// </summary>
    public async Task SimulateRebootCycleAsync(Guid hostId, TimeSpan? offlineDuration = null, CancellationToken ct = default)
    {
        var duration = offlineDuration ?? TimeSpan.FromSeconds(4);
        _logger.LogInformation("Simulating host reboot for {HostId}: taking offline for {Duration}s...", hostId, duration.TotalSeconds);

        _connectionManager.Unregister(hostId);

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var host = await db.Hosts.FindAsync(new object[] { hostId }, ct);
            if (host != null)
            {
                host.Agent.PendingReboot = false;
                await db.SaveChangesAsync(ct);
            }
        }

        await Task.Delay(duration, ct);

        var nodeName = hostId == DemoDataSeeder.HostPveNode01Id ? "pve-node-01" :
                       hostId == DemoDataSeeder.HostK8sCp01Id ? "k8s-cp-01" :
                       hostId == DemoDataSeeder.HostK8sWorker01Id ? "k8s-worker-01" :
                       hostId == DemoDataSeeder.HostK8sWorker02Id ? "k8s-worker-02" : "storage-nas-01";

        _connectionManager.RegisterSimulated(hostId, nodeName);
        _connectionManager.UpdateMetrics(hostId, new AgentMetrics { CpuUsagePct = 15.0, MemoryUsagePct = 40.0, DiskFreePct = 60.0 });
        _logger.LogInformation("Host {HostId} reboot cycle completed; agent back online.", hostId);
    }
}
