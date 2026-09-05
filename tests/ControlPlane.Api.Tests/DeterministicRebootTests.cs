using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Orchestration;
using ControlPlane.Api.Hubs;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class DeterministicRebootTests
{
    private class MockCommandExecutor : IAgentCommandExecutor
    {
        public Func<Guid, Guid, string, string[], AgentCommandResult>? OnExecute { get; set; }

        public Task<AgentCommandResult> ExecuteCommandAsync(
            Guid hostId,
            Guid jobId,
            string command,
            string[] args,
            CancellationToken cancellationToken = default)
        {
            var res = OnExecute?.Invoke(hostId, jobId, command, args)
                      ?? new AgentCommandResult(true, 0, null);
            return Task.FromResult(res);
        }

        public void NotifyFrame(Guid hostId, AgentFrameData frame) { }
    }

    private class MockProxmoxClient : IProxmoxClient
    {
        public List<string> CreatedSnapshots { get; } = new();
        public List<string> RolledBackSnapshots { get; } = new();

        public Task<string> CreateVmSnapshotAsync(string node, int vmid, string snapName, string? description = null, bool isLxc = false, CancellationToken ct = default)
        {
            CreatedSnapshots.Add(snapName);
            return Task.FromResult($"UPID:{node}:snap:{snapName}");
        }

        public Task<string> RollbackVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default)
        {
            RolledBackSnapshots.Add(snapName);
            return Task.FromResult($"UPID:{node}:rollback:{snapName}");
        }

        public Task<string> DeleteVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult($"UPID:{node}:del:{snapName}");

        public Task<List<ProxmoxSnapshotItem>> ListVmSnapshotsAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult(new List<ProxmoxSnapshotItem>());

        public Task<ProxmoxTaskStatus> GetTaskStatusAsync(string node, string upid, CancellationToken ct = default) =>
            Task.FromResult(new ProxmoxTaskStatus("stopped", "OK", "100", node, "qmsnapshot"));

        public Task<ProxmoxTaskStatus> PollTaskCompletionAsync(string node, string upid, TimeSpan? timeout = null, CancellationToken ct = default) =>
            Task.FromResult(new ProxmoxTaskStatus("stopped", "OK", "100", node, "qmsnapshot"));

        public Task<List<ProxmoxClusterResourceDto>> DiscoverClusterResourcesAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<ProxmoxClusterResourceDto>());

        public Task<List<ProxmoxNodeDto>> ListNodesAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<ProxmoxNodeDto>());

        public Task<string?> TryGetGuestIpAddressAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<bool> HasVmAuditPermissionAsync(CancellationToken ct = default) =>
            Task.FromResult(true);
    }

    private class RebootAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-det-reboot-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("STANDBY_MODE", "true");
            builder.UseSetting("ControlPlane:ApiKey", "dev-secret-key-123");
            builder.UseSetting("ConnectionStrings:PostgresDatabase", "");
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ControlPlaneDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ControlPlaneDbContext>(options =>
                {
                    options.UseSqlite($"Data Source={_tempDbFile}")
                        .UseSnakeCaseNamingConvention();
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (File.Exists(_tempDbFile))
            {
                try { File.Delete(_tempDbFile); } catch { }
            }
        }
    }

    // =========================================================================
    // 1. DeterministicRebootStep Tests
    // =========================================================================

    [Fact]
    [Trait("Category", "RebootIntegration")]
    public async Task DeterministicRebootStep_Skips_WhenHostDoesNotNeedReboot()
    {
        using var factory = new RebootAppFactory();
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<JobLogHub, IJobClient>>();
        var connMgr = scope.ServiceProvider.GetRequiredService<AgentConnectionManager>();
        var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

        var host = new HostEntity
        {
            Id = hostId,
            Hostname = "no-reboot-node",
            IpAddress = "192.168.1.130",
            OsFamily = "linux_debian",
            TargetType = "baremetal",
            Agent = new AgentState { Installed = true, PendingReboot = false }
        };
        var job = new UpdateJob { Id = jobId, TargetHostId = hostId, Status = UpdateJobState.Running };
        db.Hosts.Add(host);
        db.UpdateJobs.Add(job);
        await db.SaveChangesAsync();

        var context = new JobExecutionContext(
            job, host, scopeFactory, hubContext, new MockCommandExecutor(), connMgr, NullLogger.Instance
        );

        var step = new DeterministicRebootStep(alwaysReboot: false);
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Skipped", result.Message);
        Assert.True(context.State.ContainsKey("RebootSkipped"));
    }

    [Fact]
    [Trait("Category", "RebootIntegration")]
    public async Task DeterministicRebootStep_Fails_WhenAgentIsOffline()
    {
        using var factory = new RebootAppFactory();
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<JobLogHub, IJobClient>>();
        var connMgr = scope.ServiceProvider.GetRequiredService<AgentConnectionManager>();
        var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

        var host = new HostEntity
        {
            Id = hostId,
            Hostname = "offline-reboot-node",
            IpAddress = "192.168.1.131",
            OsFamily = "linux_debian",
            TargetType = "baremetal",
            Agent = new AgentState { Installed = true, PendingReboot = true }
        };
        var job = new UpdateJob { Id = jobId, TargetHostId = hostId, Status = UpdateJobState.Running };
        db.Hosts.Add(host);
        db.UpdateJobs.Add(job);
        await db.SaveChangesAsync();

        var context = new JobExecutionContext(
            job, host, scopeFactory, hubContext, new MockCommandExecutor(), connMgr, NullLogger.Instance
        );

        var step = new DeterministicRebootStep(alwaysReboot: true);
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("offline", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "RebootIntegration")]
    public async Task DeterministicRebootStep_DispatchesReboot_AndSets_AwaitingReconnect()
    {
        using var factory = new RebootAppFactory();
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<JobLogHub, IJobClient>>();
        var connMgr = scope.ServiceProvider.GetRequiredService<AgentConnectionManager>();
        var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

        var host = new HostEntity
        {
            Id = hostId,
            Hostname = "online-reboot-node",
            IpAddress = "192.168.1.132",
            OsFamily = "linux_debian",
            TargetType = "baremetal",
            Agent = new AgentState { Installed = true, PendingReboot = true, LastSeenAt = DateTimeOffset.UtcNow }
        };
        var job = new UpdateJob { Id = jobId, TargetHostId = hostId, Status = UpdateJobState.Running };
        db.Hosts.Add(host);
        db.UpdateJobs.Add(job);
        await db.SaveChangesAsync();

        // Connect online agent websocket
        var wsClient = factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            new Uri(factory.Server.BaseAddress, $"/agent-hub?token=dev-secret-key-123&hostId={hostId}"),
            CancellationToken.None
        );
        for (var i = 0; i < 100 && !connMgr.IsOnline(hostId); i++)
        {
            await Task.Delay(10);
        }

        connMgr.UpdateHeartbeat(hostId, new AgentHeartbeatMessage
        {
            Hostname = host.Hostname,
            KernelVersion = "6.1.0-20-amd64",
            PendingReboot = true
        });

        // Simulate agent acknowledging REBOOT_COMMENCING in background
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            connMgr.NotifyRebootCommencing(hostId, jobId.ToString());
        });

        var context = new JobExecutionContext(
            job, host, scopeFactory, hubContext, new MockCommandExecutor(), connMgr, NullLogger.Instance
        );

        var step = new DeterministicRebootStep(alwaysReboot: true, handshakeTimeout: TimeSpan.FromSeconds(2));
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(UpdateJobState.AwaitingReconnect, result.TargetState);
        Assert.Equal(UpdateJobState.AwaitingReconnect, job.Status);
        Assert.Equal("6.1.0-20-amd64", context.State["PreRebootKernel"]);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    // =========================================================================
    // 2. AwaitReconnectionStep Tests
    // =========================================================================

    [Fact]
    [Trait("Category", "RebootIntegration")]
    public async Task AwaitReconnectionStep_Skips_WhenRebootWasSkipped()
    {
        using var factory = new RebootAppFactory();
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<JobLogHub, IJobClient>>();
        var connMgr = scope.ServiceProvider.GetRequiredService<AgentConnectionManager>();
        var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

        var host = new HostEntity { Id = hostId, Hostname = "skip-wait-node", IpAddress = "192.168.1.133", OsFamily = "linux_debian", TargetType = "baremetal" };
        var job = new UpdateJob { Id = jobId, TargetHostId = hostId, Status = UpdateJobState.AwaitingReconnect };

        var context = new JobExecutionContext(job, host, scopeFactory, hubContext, new MockCommandExecutor(), connMgr, NullLogger.Instance);
        context.State["RebootSkipped"] = true;

        var step = new AwaitReconnectionStep(TimeSpan.FromSeconds(5));
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Skipped", result.Message);
    }

    [Fact]
    [Trait("Category", "RebootIntegration")]
    public async Task AwaitReconnectionStep_AwaitsReconnect_ComparesKernel_AndTransitionsToVerifying()
    {
        using var factory = new RebootAppFactory();
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<JobLogHub, IJobClient>>();
        var connMgr = scope.ServiceProvider.GetRequiredService<AgentConnectionManager>();
        var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

        var host = new HostEntity
        {
            Id = hostId,
            Hostname = "reconnected-node",
            IpAddress = "192.168.1.134",
            OsFamily = "linux_debian",
            TargetType = "baremetal"
        };
        var job = new UpdateJob { Id = jobId, TargetHostId = hostId, Status = UpdateJobState.AwaitingReconnect };
        db.Hosts.Add(host);
        db.UpdateJobs.Add(job);
        await db.SaveChangesAsync();

        var context = new JobExecutionContext(job, host, scopeFactory, hubContext, new MockCommandExecutor(), connMgr, NullLogger.Instance);
        context.State["PreRebootKernel"] = "6.1.0-20-amd64";

        var step = new AwaitReconnectionStep(TimeSpan.FromSeconds(5));

        // Background task reconnecting after 100ms with updated kernel
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            var wsClient = factory.Server.CreateWebSocketClient();
            var ws = await wsClient.ConnectAsync(
                new Uri(factory.Server.BaseAddress, $"/agent-hub?token=dev-secret-key-123&hostId={hostId}"),
                CancellationToken.None
            );

            // Send heartbeat with updated kernel
            connMgr.UpdateHeartbeat(hostId, new AgentHeartbeatMessage
            {
                Hostname = host.Hostname,
                KernelVersion = "6.1.0-25-amd64",
                PendingReboot = false
            });
        });

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(UpdateJobState.Verifying, result.TargetState);

        // Verify logs recorded kernel progression
        var logs = await db.StepLogs.Where(l => l.JobId == jobId).ToListAsync();
        Assert.Contains(logs, l => l.LogLine.Contains("Kernel updated to: 6.1.0-25-amd64"));
    }

    [Fact]
    [Trait("Category", "RebootIntegration")]
    public async Task AwaitReconnectionStep_Fails_WhenReconnectionTimesOut()
    {
        using var factory = new RebootAppFactory();
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<JobLogHub, IJobClient>>();
        var connMgr = scope.ServiceProvider.GetRequiredService<AgentConnectionManager>();
        var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

        var host = new HostEntity { Id = hostId, Hostname = "hung-reboot-node", IpAddress = "192.168.1.135", OsFamily = "linux_debian", TargetType = "baremetal" };
        var job = new UpdateJob { Id = jobId, TargetHostId = hostId, Status = UpdateJobState.AwaitingReconnect };

        var context = new JobExecutionContext(job, host, scopeFactory, hubContext, new MockCommandExecutor(), connMgr, NullLogger.Instance);

        var step = new AwaitReconnectionStep(TimeSpan.FromMilliseconds(150));
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("timeout", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 3. PostFlightHealthProbeStep Tests
    // =========================================================================

    [Fact]
    [Trait("Category", "RebootIntegration")]
    public async Task PostFlightHealthProbeStep_Succeeds_WhenZeroFailedUnits()
    {
        using var factory = new RebootAppFactory();
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<JobLogHub, IJobClient>>();
        var connMgr = scope.ServiceProvider.GetRequiredService<AgentConnectionManager>();
        var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

        var host = new HostEntity { Id = hostId, Hostname = "healthy-probes-node", IpAddress = "192.168.1.136", OsFamily = "linux_debian", TargetType = "baremetal" };
        var job = new UpdateJob { Id = jobId, TargetHostId = hostId, Status = UpdateJobState.Verifying };

        var mockExecutor = new MockCommandExecutor
        {
            OnExecute = (hId, jId, cmd, args) => new AgentCommandResult(true, 0, null)
        };

        var context = new JobExecutionContext(job, host, scopeFactory, hubContext, mockExecutor, connMgr, NullLogger.Instance);

        var step = new PostFlightHealthProbeStep();
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(UpdateJobState.Completed, result.TargetState);
    }

    [Fact]
    [Trait("Category", "RebootIntegration")]
    public async Task PostFlightHealthProbeStep_Fails_WhenFailedUnitsDetected()
    {
        using var factory = new RebootAppFactory();
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<JobLogHub, IJobClient>>();
        var connMgr = scope.ServiceProvider.GetRequiredService<AgentConnectionManager>();
        var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

        var host = new HostEntity { Id = hostId, Hostname = "failing-units-node", IpAddress = "192.168.1.137", OsFamily = "linux_debian", TargetType = "baremetal" };
        var job = new UpdateJob { Id = jobId, TargetHostId = hostId, Status = UpdateJobState.Verifying };

        var mockExecutor = new MockCommandExecutor
        {
            OnExecute = (hId, jId, cmd, args) => new AgentCommandResult(false, 1, "Failed units: containerd.service")
        };

        var context = new JobExecutionContext(job, host, scopeFactory, hubContext, mockExecutor, connMgr, NullLogger.Instance);

        var step = new PostFlightHealthProbeStep(failOnFailedServices: true);
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Failed units: containerd.service", result.Message);
    }

    // =========================================================================
    // 4. Debug Endpoint & Full DAG Reboot / Rollback Integration
    // =========================================================================

    [Fact]
    [Trait("Category", "RebootIntegration")]
    public async Task DebugRebootEndpoint_ReturnsAccepted_WhenAgentIsOnline()
    {
        using var factory = new RebootAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");

        var hostId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            db.Hosts.Add(new HostEntity
            {
                Id = hostId,
                Hostname = "debug-reboot-node",
                IpAddress = "192.168.1.138",
                OsFamily = "linux_debian",
                TargetType = "baremetal",
                Agent = new AgentState { Installed = true, PendingReboot = true, LastSeenAt = DateTimeOffset.UtcNow }
            });
            await db.SaveChangesAsync();
        }

        // Connect agent WebSocket
        var wsClient = factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            new Uri(factory.Server.BaseAddress, $"/agent-hub?token=dev-secret-key-123&hostId={hostId}"),
            CancellationToken.None
        );

        var connMgr = factory.Services.GetRequiredService<AgentConnectionManager>();
        for (var i = 0; i < 50 && !connMgr.IsOnline(hostId); i++)
        {
            await Task.Delay(20);
        }

        var response = await client.PostAsJsonAsync("/api/v1/debug/test-reboot", new DebugRebootRequest(hostId));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(UpdateJobState.AwaitingReconnect, doc.RootElement.GetProperty("status").GetString());

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "RebootIntegration")]
    public async Task DagExecutionPipeline_RebootTimeout_TriggersProxmoxRollback()
    {
        using var factory = new RebootAppFactory();
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<JobLogHub, IJobClient>>();
        var connMgr = scope.ServiceProvider.GetRequiredService<AgentConnectionManager>();
        var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

        var host = new HostEntity
        {
            Id = hostId,
            Hostname = "vm-timeout-node",
            IpAddress = "192.168.1.139",
            OsFamily = "linux_debian",
            TargetType = "proxmox_vm",
            Proxmox = new ProxmoxTarget { Node = "pve-01", Vmid = 220 },
            Agent = new AgentState { Installed = true, PendingReboot = true, LastSeenAt = DateTimeOffset.UtcNow }
        };
        var job = new UpdateJob { Id = jobId, TargetHostId = hostId, Status = UpdateJobState.Pending };
        db.Hosts.Add(host);
        db.UpdateJobs.Add(job);
        await db.SaveChangesAsync();

        var wsClient = factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            new Uri(factory.Server.BaseAddress, $"/agent-hub?token=dev-secret-key-123&hostId={hostId}"),
            CancellationToken.None
        );
        for (var i = 0; i < 50 && !connMgr.IsOnline(hostId); i++)
        {
            await Task.Delay(20);
        }

        connMgr.UpdateMetrics(hostId, new AgentMetrics { DiskFreePct = 40.0 });

        var mockExecutor = new MockCommandExecutor
        {
            OnExecute = (hId, jId, cmd, args) =>
            {
                if (cmd.Contains("reboot"))
                {
                    _ = Task.Run(async () =>
                    {
                        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "rebooting", CancellationToken.None); } catch { }
                    });
                }
                return new AgentCommandResult(true, 0, null);
            }
        };

        var mockProxmox = new MockProxmoxClient();

        var context = new JobExecutionContext(job, host, scopeFactory, hubContext, mockExecutor, connMgr, NullLogger.Instance);

        // Simulated pipeline with short 100ms reboot reconnect timeout (host fails to come back up)
        var pipeline = new DagExecutionPipeline(new IJobStep[]
        {
            new PreflightHeartbeatCheckStep(),
            new PreflightDiskHeadroomCheckStep(),
            new PreflightPackageLockCheckStep(),
            new ProxmoxSnapshotStep(mockProxmox),
            new PackageUpgradeStep(),
            new DeterministicRebootStep(alwaysReboot: true, handshakeTimeout: TimeSpan.FromMilliseconds(50)),
            new AwaitReconnectionStep(timeout: TimeSpan.FromMilliseconds(100)),
            new PostFlightHealthProbeStep()
        });

        var success = await pipeline.ExecuteAsync(context, CancellationToken.None);

        // Pipeline should fail due to reconnection timeout
        Assert.False(success);

        // Snapshot was taken
        Assert.Single(mockProxmox.CreatedSnapshots);
        var createdSnap = mockProxmox.CreatedSnapshots[0];

        // Hypervisor rollback was triggered!
        Assert.Single(mockProxmox.RolledBackSnapshots);
        Assert.Equal(createdSnap, mockProxmox.RolledBackSnapshots[0]);

        // Job status in database is RolledBack
        var updatedJob = await db.UpdateJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        Assert.NotNull(updatedJob);
        Assert.Equal(UpdateJobState.RolledBack, updatedJob.Status);
    }
}
