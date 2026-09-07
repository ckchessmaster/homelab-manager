using System.Net;
using System.Text.Json;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Discovery;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Features.Jobs;
using ControlPlane.Api.Features.Mcp;
using ControlPlane.Api.Features.Orchestration;
using ControlPlane.Api.Features.Orchestration.Pipelines;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class McpServerTests
{
    private (ControlPlaneDbContext Db, SqliteConnection Conn, IServiceProvider Sp) CreateTestServiceProvider()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var services = new ServiceCollection();
        services.AddDbContext<ControlPlaneDbContext>(options =>
        {
            options.UseSqlite(conn).UseSnakeCaseNamingConvention();
        });
        services.AddLogging();
        services.AddSignalR();
        services.AddSingleton<IPipelineCatalog, PipelineCatalog>();
        services.AddSingleton<IAgentCommandExecutor, AgentCommandExecutor>();
        services.AddSingleton<AgentConnectionManager>();
        services.AddSingleton<IStepLogConsumer, StepLogStreamConsumer>();
        services.AddSingleton<JobOrchestratorService>();
        services.AddScoped<HostService>();
        services.AddScoped<IDiscoveryService, DiscoveryService>();
        services.AddScoped<Features.Adapters.Proxmox.IProxmoxClient, FakeProxmoxClient>();
        services.AddScoped<Features.Adapters.Kubernetes.IKubernetesAdapter, FakeKubernetesAdapter>();
        services.Configure<Features.Adapters.Proxmox.ProxmoxOptions>(_ => { });
        services.AddScoped<ControlPlaneMcpTools>();

        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<ControlPlaneDbContext>();
        db.Database.EnsureCreated();
        return (db, conn, sp);
    }

    [Fact]
    public void IsMcpServerEnabled_ReturnsExpectedStatus()
    {
        var trueConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ENABLE_MCP_SERVER"] = "true" })
            .Build();
        Assert.True(McpServiceCollectionExtensions.IsMcpServerEnabled(trueConfig));

        var falseConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ENABLE_MCP_SERVER"] = "false" })
            .Build();
        Assert.False(McpServiceCollectionExtensions.IsMcpServerEnabled(falseConfig));

        var emptyConfig = new ConfigurationBuilder().Build();
        Assert.False(McpServiceCollectionExtensions.IsMcpServerEnabled(emptyConfig));
    }

    [Fact]
    public async Task QueryJobLogs_ReturnsSequenceOrderedLogs()
    {
        var (db, conn, sp) = CreateTestServiceProvider();
        using var _ = conn;
        using var __ = db;

        var jobId = Guid.NewGuid();
        var hostId = Guid.NewGuid();

        db.Hosts.Add(new HostEntity
        {
            Id = hostId,
            Hostname = "mcp-test-host",
            IpAddress = "192.168.1.10",
            OsFamily = "linux_debian",
            TargetType = "baremetal"
        });

        db.UpdateJobs.Add(new UpdateJob
        {
            Id = jobId,
            TargetHostId = hostId,
            InitiatedBy = "Tester",
            Status = "Running",
            ActiveStep = "apt update",
            StartedAt = DateTimeOffset.UtcNow
        });

        for (int i = 0; i < 5; i++)
        {
            db.StepLogs.Add(new StepLog
            {
                JobId = jobId,
                SequenceId = i,
                StreamType = "stdout",
                LogLine = $"Log line {i}",
                Timestamp = DateTimeOffset.UtcNow.AddSeconds(i)
            });
        }
        await db.SaveChangesAsync();

        using var scope = sp.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<ControlPlaneMcpTools>();

        var resultObj = await tools.QueryJobLogs(jobId, fromSequenceId: 1, limit: 3);
        var json = JsonSerializer.Serialize(resultObj);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(3, root.GetProperty("count").GetInt32());
        Assert.Equal(5, root.GetProperty("totalAvailable").GetInt32());
        var logs = root.GetProperty("logs");
        Assert.Equal(3, logs.GetArrayLength());
        Assert.Equal(1, logs[0].GetProperty("sequenceId").GetInt64());
        Assert.Equal("Log line 1", logs[0].GetProperty("line").GetString());
    }

    [Fact]
    public async Task ListHosts_ReturnsHostInventory()
    {
        var (db, conn, sp) = CreateTestServiceProvider();
        using var _ = conn;
        using var __ = db;

        var hostId = Guid.NewGuid();
        db.Hosts.Add(new HostEntity
        {
            Id = hostId,
            Hostname = "worker-01",
            FriendlyName = "Primary Worker",
            IpAddress = "192.168.1.50",
            OsFamily = "linux_debian",
            TargetType = "baremetal"
        });
        await db.SaveChangesAsync();

        using var scope = sp.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<ControlPlaneMcpTools>();

        var result = await tools.ListHosts();
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("count").GetInt32() >= 1);
        var hosts = root.GetProperty("hosts");
        Assert.True(hosts.GetArrayLength() >= 1);
        Assert.Equal("worker-01", hosts[0].GetProperty("hostname").GetString());
    }

    [Fact]
    public void ListPipelines_ReturnsCatalogProfiles()
    {
        var (db, conn, sp) = CreateTestServiceProvider();
        using var _ = conn;
        using var __ = db;

        using var scope = sp.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<ControlPlaneMcpTools>();
        var result = tools.ListPipelines();
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var pipelines = root.GetProperty("pipelines");
        Assert.True(pipelines.GetArrayLength() > 0);
    }

    private class FakeProxmoxClient : Features.Adapters.Proxmox.IProxmoxClient
    {
        public Task<List<Features.Adapters.Proxmox.ProxmoxClusterResourceDto>> DiscoverClusterResourcesAsync(CancellationToken ct = default) => Task.FromResult(new List<Features.Adapters.Proxmox.ProxmoxClusterResourceDto>());
        public Task<List<Features.Adapters.Proxmox.ProxmoxNodeDto>> ListNodesAsync(CancellationToken ct = default) => Task.FromResult(new List<Features.Adapters.Proxmox.ProxmoxNodeDto>());
        public Task<string?> TryGetGuestIpAddressAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string> CreateVmSnapshotAsync(string node, int vmid, string snapName, string? description = null, bool isLxc = false, CancellationToken ct = default) => Task.FromResult("UPID:1");
        public Task<string> RollbackVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default) => Task.FromResult("UPID:2");
        public Task<string> DeleteVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default) => Task.FromResult("UPID:3");
        public Task<List<Features.Adapters.Proxmox.ProxmoxSnapshotItem>> ListVmSnapshotsAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) => Task.FromResult(new List<Features.Adapters.Proxmox.ProxmoxSnapshotItem>());
        public Task<Features.Adapters.Proxmox.ProxmoxTaskStatus> GetTaskStatusAsync(string node, string upid, CancellationToken ct = default) => Task.FromResult(new Features.Adapters.Proxmox.ProxmoxTaskStatus("stopped", "OK"));
        public Task<Features.Adapters.Proxmox.ProxmoxTaskStatus> PollTaskCompletionAsync(string node, string upid, TimeSpan? timeout = null, CancellationToken ct = default) => Task.FromResult(new Features.Adapters.Proxmox.ProxmoxTaskStatus("stopped", "OK"));
        public Task<bool> HasSnapshotFeatureAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> HasVmAuditPermissionAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private class FakeKubernetesAdapter : Features.Adapters.Kubernetes.IKubernetesAdapter
    {
        public Task<List<Features.Adapters.Kubernetes.K8sDiscoveredNodeDto>> ListNodesAsync(CancellationToken ct = default) => Task.FromResult(new List<Features.Adapters.Kubernetes.K8sDiscoveredNodeDto>());
        public Task<bool> CordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UncordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<Features.Adapters.Kubernetes.K8sDrainResult> DrainNodeAsync(string nodeName, TimeSpan timeout, bool ignoreDaemonSets = true, bool deleteEmptyDirData = true, CancellationToken ct = default) => Task.FromResult(new Features.Adapters.Kubernetes.K8sDrainResult(nodeName, true, 0, 0, null));
        public Task<Features.Adapters.Kubernetes.K8sNodeStatus?> GetNodeStatusAsync(string nodeName, CancellationToken ct = default) => Task.FromResult<Features.Adapters.Kubernetes.K8sNodeStatus?>(null);
    }
}
