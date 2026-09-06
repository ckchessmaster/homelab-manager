using System.Net;
using System.Net.Http.Json;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Orchestration.Temporal;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using ControlPlane.Api.Features.Orchestration.Temporal.Workflows;
using ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;
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
using Xunit;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class TemporalWorkflowTests : IDisposable
{
    private readonly string _tempDbFile;
    private readonly DbContextOptions<ControlPlaneDbContext> _dbOptions;

    public TemporalWorkflowTests()
    {
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-temporal-test-{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseSqlite($"Data Source={_tempDbFile}")
            .UseSnakeCaseNamingConvention()
            .Options;

        using var db = new ControlPlaneDbContext(_dbOptions);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbFile))
        {
            try { File.Delete(_tempDbFile); } catch { }
        }
    }

    private ControlPlaneDbContext CreateDbContext() => new(_dbOptions);

    private class TestWorkflowLogEmitter : IWorkflowLogEmitter
    {
        public List<(Guid JobId, string Stream, string Line)> Logs { get; } = new();
        public List<(Guid JobId, string Status, string? ActiveStep)> StatusChanges { get; } = new();
        public string? LastSnapshotIdentifier { get; private set; }

        public Task EmitLogAsync(Guid jobId, string streamType, string logLine, CancellationToken ct = default)
        {
            Logs.Add((jobId, streamType, logLine));
            return Task.CompletedTask;
        }

        public Task UpdateJobStatusAsync(Guid jobId, string status, string? activeStep, string? failureReason = null, CancellationToken ct = default)
        {
            StatusChanges.Add((jobId, status, activeStep));
            return Task.CompletedTask;
        }

        public Task SetSnapshotIdentifierAsync(Guid jobId, string? snapshotIdentifier, CancellationToken ct = default)
        {
            LastSnapshotIdentifier = snapshotIdentifier;
            return Task.CompletedTask;
        }
    }

    private class TestCommandExecutor : IAgentCommandExecutor
    {
        public Func<string, string[], AgentCommandResult>? Handler { get; set; }

        public Task<AgentCommandResult> ExecuteCommandAsync(
            Guid hostId,
            Guid jobId,
            string command,
            string[] args,
            CancellationToken cancellationToken = default)
        {
            if (Handler != null)
            {
                return Task.FromResult(Handler(command, args));
            }
            return Task.FromResult(new AgentCommandResult(true, 0, null));
        }

        public void NotifyFrame(Guid hostId, AgentFrameData frame) { }
    }

    private class TestProxmoxClient : IProxmoxClient
    {
        public bool HasSnapshotSupport { get; set; } = true;
        public string CreatedSnapshotName { get; set; } = string.Empty;
        public bool RollbackInvoked { get; set; }

        public Task<bool> HasSnapshotFeatureAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult(HasSnapshotSupport);

        public Task<string> CreateVmSnapshotAsync(string node, int vmid, string snapname, string? description = null, bool isLxc = false, CancellationToken ct = default)
        {
            CreatedSnapshotName = snapname;
            return Task.FromResult("UPID:node:1234");
        }

        public Task<ProxmoxTaskStatus> PollTaskCompletionAsync(string node, string upid, TimeSpan? timeout = null, CancellationToken ct = default) =>
            Task.FromResult(new ProxmoxTaskStatus("stopped", "OK", "100", node, "qmsnapshot"));

        public Task<string> RollbackVmSnapshotAsync(string node, int vmid, string snapname, bool isLxc = false, CancellationToken ct = default)
        {
            RollbackInvoked = true;
            return Task.FromResult("UPID:node:5678");
        }

        public Task<string> DeleteVmSnapshotAsync(string node, int vmid, string snapname, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult("UPID:node:9999");

        public Task<ProxmoxTaskStatus> GetTaskStatusAsync(string node, string upid, CancellationToken ct = default) =>
            Task.FromResult(new ProxmoxTaskStatus("stopped", "OK", "100", node, "qmsnapshot"));

        public Task<List<ProxmoxClusterResourceDto>> DiscoverClusterResourcesAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<ProxmoxClusterResourceDto>());

        public Task<List<ProxmoxNodeDto>> ListNodesAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<ProxmoxNodeDto>());

        public Task<string?> TryGetGuestIpAddressAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<List<ProxmoxSnapshotItem>> ListVmSnapshotsAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult(new List<ProxmoxSnapshotItem>());

        public Task<bool> HasVmAuditPermissionAsync(CancellationToken ct = default) =>
            Task.FromResult(true);
    }

    private class TestKubernetesAdapter : IKubernetesAdapter
    {
        public bool IsRegistered { get; set; } = true;
        public bool Cordoned { get; set; }
        public bool Drained { get; set; }
        public bool Uncordoned { get; set; }

        public Task<K8sNodeStatus?> GetNodeStatusAsync(string nodeName, CancellationToken ct = default)
        {
            if (!IsRegistered) return Task.FromResult<K8sNodeStatus?>(null);
            return Task.FromResult<K8sNodeStatus?>(new K8sNodeStatus(nodeName, true, false, "192.168.1.10", 5));
        }

        public Task<bool> CordonNodeAsync(string nodeName, CancellationToken ct = default)
        {
            Cordoned = true;
            return Task.FromResult(true);
        }

        public Task<bool> UncordonNodeAsync(string nodeName, CancellationToken ct = default)
        {
            Uncordoned = true;
            return Task.FromResult(true);
        }

        public Task<K8sDrainResult> DrainNodeAsync(string nodeName, TimeSpan timeout, bool ignoreDaemonSets = true, bool deleteEmptyDirData = true, CancellationToken ct = default)
        {
            Drained = true;
            return Task.FromResult(new K8sDrainResult(nodeName, true, 3, 0, null));
        }

        public Task<List<K8sDiscoveredNodeDto>> ListNodesAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<K8sDiscoveredNodeDto>());
    }

    private class TestWebSocket : System.Net.WebSockets.WebSocket
    {
        public override System.Net.WebSockets.WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override System.Net.WebSockets.WebSocketState State => System.Net.WebSockets.WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(System.Net.WebSockets.WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task<System.Net.WebSockets.WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            Task.FromResult(new System.Net.WebSockets.WebSocketReceiveResult(0, System.Net.WebSockets.WebSocketMessageType.Text, true));
        public override Task SendAsync(ArraySegment<byte> buffer, System.Net.WebSockets.WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task PreflightActivities_CheckHeartbeat_WhenOnline_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateDbContext());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var connectionManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        // Seed host
        using (var db = CreateDbContext())
        {
            var host = new HostEntity
            {
                Id = hostId,
                Hostname = "node-01",
                TargetType = "bare_metal",
                IpAddress = "192.168.1.50",
                Agent = new AgentState
                {
                    Installed = true,
                    LastSeenAt = DateTimeOffset.UtcNow.AddSeconds(-2)
                }
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();
        }

        var logEmitter = new TestWorkflowLogEmitter();
        var cmdExec = new TestCommandExecutor();

        var activities = new PreflightActivities(
            connectionManager,
            cmdExec,
            scopeFactory,
            logEmitter,
            NullLogger<PreflightActivities>.Instance
        );

        // Offline initially
        var offlineResult = await activities.CheckHeartbeatAsync(new PreflightHeartbeatInput(jobId, hostId, "node-01"));
        Assert.False(offlineResult.Success);
        Assert.Contains("offline", offlineResult.Message, StringComparison.OrdinalIgnoreCase);

        // Simulate agent online session
        connectionManager.Register(hostId, "node-01", new TestWebSocket());

        var onlineResult = await activities.CheckHeartbeatAsync(new PreflightHeartbeatInput(jobId, hostId, "node-01"));
        Assert.True(onlineResult.Success);
        Assert.Contains("verified", onlineResult.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreflightActivities_CheckDiskHeadroom_ReturnsHeadroomStatus()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateDbContext());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var connectionManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var logEmitter = new TestWorkflowLogEmitter();
        var cmdExec = new TestCommandExecutor();

        var activities = new PreflightActivities(
            connectionManager,
            cmdExec,
            scopeFactory,
            logEmitter,
            NullLogger<PreflightActivities>.Instance
        );

        // Register session
        connectionManager.Register(hostId, "node-01", new TestWebSocket());

        // Update metrics with 25% free (above 20% limit)
        connectionManager.UpdateMetrics(hostId, new AgentMetrics { DiskFreePct = 25.0 });

        var successResult = await activities.CheckDiskHeadroomAsync(new PreflightDiskHeadroomInput(jobId, hostId, "node-01", MinFreePct: 20.0));
        Assert.True(successResult.Success);
        Assert.Contains("25.0% available", successResult.Message);

        // Update metrics with 10% free (below 20% limit)
        connectionManager.UpdateMetrics(hostId, new AgentMetrics { DiskFreePct = 10.0 });
        var failResult = await activities.CheckDiskHeadroomAsync(new PreflightDiskHeadroomInput(jobId, hostId, "node-01", MinFreePct: 20.0));
        Assert.False(failResult.Success);
        Assert.Contains("Insufficient root filesystem headroom", failResult.Message);
    }

    [Fact]
    public async Task PreflightActivities_CheckPackageLock_DetectsLockStatus()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateDbContext());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var connectionManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var logEmitter = new TestWorkflowLogEmitter();
        var cmdExec = new TestCommandExecutor
        {
            Handler = (cmd, args) => new AgentCommandResult(true, 0, null)
        };

        var activities = new PreflightActivities(
            connectionManager,
            cmdExec,
            scopeFactory,
            logEmitter,
            NullLogger<PreflightActivities>.Instance
        );

        var result = await activities.CheckPackageLockAsync(new PreflightPackageLockInput(jobId, hostId, "node-01", "debian"));
        Assert.True(result.Success);
        Assert.Contains("No active package manager locks", result.Message);

        // Simulate lock failure
        cmdExec.Handler = (cmd, args) => new AgentCommandResult(false, 1, "Lock file /var/lib/dpkg/lock held");
        var lockFailResult = await activities.CheckPackageLockAsync(new PreflightPackageLockInput(jobId, hostId, "node-01", "debian"));
        Assert.False(lockFailResult.Success);
        Assert.Contains("Package manager lock detected", lockFailResult.Message);
    }

    [Fact]
    public async Task ProxmoxActivities_SnapshotAndRollback_OperateCorrectly()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateDbContext());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using (var db = CreateDbContext())
        {
            var host = new HostEntity
            {
                Id = hostId,
                Hostname = "pve-vm-01",
                TargetType = "proxmox_vm",
                IpAddress = "192.168.1.100",
                Proxmox = new ProxmoxTarget
                {
                    Node = "pve1",
                    Vmid = 101
                }
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();
        }

        var proxmoxClient = new TestProxmoxClient();
        var logEmitter = new TestWorkflowLogEmitter();

        var activities = new ProxmoxActivities(
            scopeFactory,
            logEmitter,
            NullLogger<ProxmoxActivities>.Instance,
            proxmoxClient
        );

        // 1. Create snapshot
        var snapResult = await activities.CreateSnapshotAsync(
            new ProxmoxSnapshotInput(jobId, hostId, "pve-vm-01", "proxmox_vm", "test-snap-01")
        );

        Assert.True(snapResult.Success);
        Assert.True(snapResult.Created);
        Assert.Equal("test-snap-01", snapResult.SnapshotName);
        Assert.Equal("test-snap-01", proxmoxClient.CreatedSnapshotName);

        // 2. Rollback snapshot
        var rollbackResult = await activities.RollbackSnapshotAsync(
            new ProxmoxRollbackInput(jobId, hostId, "test-snap-01", "proxmox_vm")
        );

        Assert.True(rollbackResult.Success);
        Assert.True(proxmoxClient.RollbackInvoked);
    }

    [Fact]
    public async Task KubernetesActivities_CordonDrainUncordon_OperateCorrectly()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateDbContext());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var k8sAdapter = new TestKubernetesAdapter();
        var logEmitter = new TestWorkflowLogEmitter();

        var activities = new KubernetesActivities(
            scopeFactory,
            logEmitter,
            NullLogger<KubernetesActivities>.Instance,
            k8sAdapter
        );

        // Cordon
        var cordonResult = await activities.CordonNodeAsync(new KubernetesNodeInput(jobId, hostId, "k8s-node-01"));
        Assert.True(cordonResult.Success);
        Assert.True(cordonResult.Cordoned);
        Assert.True(k8sAdapter.Cordoned);

        // Drain
        var drainResult = await activities.DrainNodeAsync(new KubernetesDrainInput(jobId, hostId, "k8s-node-01"));
        Assert.True(drainResult.Success);
        Assert.True(drainResult.Drained);
        Assert.Equal(3, drainResult.EvictedPodCount);

        // Uncordon
        var uncordonResult = await activities.UncordonNodeAsync(new KubernetesNodeInput(jobId, hostId, "k8s-node-01"));
        Assert.True(uncordonResult.Success);
        Assert.True(k8sAdapter.Uncordoned);
    }

    [Fact]
    public async Task AgentActivities_UpgradeAndRebootHandshake_OperateCorrectly()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateDbContext());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var connectionManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using (var db = CreateDbContext())
        {
            var host = new HostEntity
            {
                Id = hostId,
                Hostname = "worker-01",
                TargetType = "bare_metal",
                IpAddress = "192.168.1.10",
                Agent = new AgentState
                {
                    Installed = true,
                    PendingReboot = false
                }
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();
        }

        var cmdExec = new TestCommandExecutor
        {
            Handler = (cmd, args) => new AgentCommandResult(true, 0, null)
        };
        var logEmitter = new TestWorkflowLogEmitter();

        var activities = new AgentActivities(
            connectionManager,
            cmdExec,
            scopeFactory,
            logEmitter,
            NullLogger<AgentActivities>.Instance
        );

        // Upgrade packages
        var upgradeResult = await activities.UpgradePackagesAsync(new AgentUpgradeInput(jobId, hostId, "worker-01", "ubuntu"));
        Assert.True(upgradeResult.Success);
        Assert.Equal(0, upgradeResult.ExitCode);

        // Reboot skipped if not required
        var rebootResult = await activities.InitiateRebootAsync(new AgentRebootInput(jobId, hostId, "worker-01", AlwaysReboot: false));
        Assert.True(rebootResult.Success);
        Assert.True(rebootResult.Skipped);

        // Await reconnect skipped if reboot was skipped
        var reconnectResult = await activities.AwaitReconnectionAsync(new AgentReconnectInput(jobId, hostId, "worker-01", RebootSkipped: true));
        Assert.True(reconnectResult.Success);
    }

    [Fact]
    public async Task UpdateJobActivities_UpdatesJobStatus_AndRecordsCompletion()
    {
        var logEmitter = new TestWorkflowLogEmitter();
        var activities = new UpdateJobActivities(logEmitter, NullLogger<UpdateJobActivities>.Instance);
        var jobId = Guid.NewGuid();

        await activities.UpdateJobStatusAsync(new UpdateJobStatusInput(jobId, "Running", "Step 1"));
        Assert.Contains(logEmitter.StatusChanges, s => s.JobId == jobId && s.Status == "Running" && s.ActiveStep == "Step 1");

        await activities.RecordJobCompletionAsync(new RecordJobCompletionInput(jobId, "Completed", null));
        Assert.Contains(logEmitter.StatusChanges, s => s.JobId == jobId && s.Status == "Completed" && s.ActiveStep == null);
    }

    [Fact]
    public async Task HostUpgradeWorkflow_SignalsAndQuery_TrackStateCorrectly()
    {
        var workflow = new HostUpgradeWorkflow();

        // Initial state query
        var state = workflow.GetWorkflowState();
        Assert.Equal("Pending", state.Status);
        Assert.False(state.RebootApproved);
        Assert.False(state.Cancelled);

        // Approve reboot signal
        await workflow.ApproveRebootAsync();
        state = workflow.GetWorkflowState();
        Assert.True(state.RebootApproved);

        // Cancel signal
        await workflow.CancelAsync("Operator requested cancellation");
        state = workflow.GetWorkflowState();
        Assert.True(state.Cancelled);
        Assert.Equal("Operator requested cancellation", state.CancelReason);
    }

    [Fact]
    public async Task WorkflowEndpoints_WhenTemporalUnavailable_ReturnsServiceUnavailable()
    {
        // Custom factory with STANDBY_MODE=true so Temporal is disabled
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("STANDBY_MODE", "true");
            builder.UseSetting("Temporal:Enabled", "false");
            builder.UseSetting("ControlPlane:ApiKey", "test-key");
            builder.UseSetting("ConnectionStrings:PostgresDatabase", "");
            builder.UseEnvironment("Development");
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "test-key");

        var response = await client.PostAsJsonAsync("/api/v1/orchestration/temporal/workflows/start", new
        {
            HostId = Guid.NewGuid(),
            RequireApprovalBeforeReboot = false
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
