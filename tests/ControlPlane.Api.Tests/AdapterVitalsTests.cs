using System.Net;
using System.Text;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Security;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ControlPlane.Api.Tests;

public class AdapterVitalsTests
{
    private static (ControlPlaneDbContext Db, SqliteConnection Conn) CreateTestDbContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseSqlite(conn)
            .UseSnakeCaseNamingConvention()
            .Options;
        var db = new ControlPlaneDbContext(options);
        db.Database.EnsureCreated();
        return (db, conn);
    }

    private static ISecretEncryptionService CreateEncryptionService()
    {
        var key = Encoding.UTF8.GetBytes("12345678901234567890123456789012");
        return new SecretEncryptionService(new TestKeyProvider(key));
    }

    private class TestKeyProvider : ISecurityKeyProvider
    {
        private readonly byte[] _key;
        public TestKeyProvider(byte[] key) => _key = key;
        public byte[] GetMasterKey() => (byte[])_key.Clone();
        public string KeySource => "Test";
        public string? KeyFilePath => null;
    }

    private class MockProxmoxClient : IProxmoxClient
    {
        public List<ProxmoxNodeDto> NodesToReturn { get; set; } = new();

        public Task<List<ProxmoxNodeDto>> ListNodesAsync(CancellationToken ct = default) =>
            Task.FromResult(NodesToReturn);

        public Task<List<ProxmoxClusterResourceDto>> DiscoverClusterResourcesAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<ProxmoxClusterResourceDto>());
        public Task<string?> TryGetGuestIpAddressAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
        public Task<string> CreateVmSnapshotAsync(string node, int vmid, string snapName, string? description = null, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult("UPID:1");
        public Task<string> RollbackVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult("UPID:2");
        public Task<string> DeleteVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult("UPID:3");
        public Task<List<ProxmoxSnapshotItem>> ListVmSnapshotsAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult(new List<ProxmoxSnapshotItem>());
        public Task<ProxmoxTaskStatus> GetTaskStatusAsync(string node, string upid, CancellationToken ct = default) =>
            Task.FromResult(new ProxmoxTaskStatus("stopped", "OK"));
        public Task<ProxmoxTaskStatus> PollTaskCompletionAsync(string node, string upid, TimeSpan? timeout = null, CancellationToken ct = default) =>
            Task.FromResult(new ProxmoxTaskStatus("stopped", "OK"));
        public Task<bool> HasSnapshotFeatureAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult(true);
        public Task<bool> HasVmAuditPermissionAsync(CancellationToken ct = default) =>
            Task.FromResult(true);
    }

    private class MockProxmoxClientFactory : IProxmoxClientFactory
    {
        private readonly IProxmoxClient _client;
        public MockProxmoxClientFactory(IProxmoxClient client) => _client = client;
        public Task<IProxmoxClient> GetClientAsync(string? instanceId = null, CancellationToken ct = default) =>
            Task.FromResult(_client);
        public IProxmoxClient CreateClient(ProxmoxOptions options) => _client;
    }

    [Fact]
    public void ProxmoxVitalsDto_CalculatesCpuAndMemoryCorrectly()
    {
        var rawNodes = new List<ProxmoxNodeDto>
        {
            new ProxmoxNodeDto("pve-01", "online", 0.155, 16, 17179869184, 34359738368, 86400),
            new ProxmoxNodeDto("pve-02", "offline", null, 8, null, null, null)
        };

        var nodeVitals = rawNodes.Select(n =>
        {
            double? cpuPct = n.Cpu.HasValue
                ? Math.Round(n.Cpu.Value <= 1.0 ? n.Cpu.Value * 100 : n.Cpu.Value, 1)
                : null;
            double? memPct = n.Memory.HasValue && n.MaxMemory.HasValue && n.MaxMemory.Value > 0
                ? Math.Round((double)n.Memory.Value / n.MaxMemory.Value * 100, 1)
                : null;

            return new ProxmoxNodeVitalsDto(
                Node: n.Node,
                Status: n.Status,
                CpuUsagePct: cpuPct,
                MaxCpu: n.MaxCpu,
                MemoryUsedBytes: n.Memory,
                MemoryMaxBytes: n.MaxMemory,
                MemoryUsagePct: memPct,
                UptimeSeconds: n.Uptime
            );
        }).ToList();

        var vitals = new ProxmoxVitalsDto(
            InstanceId: "test-pve",
            InstanceName: "Test Cluster",
            Version: "8.2",
            TotalNodes: nodeVitals.Count,
            OnlineNodes: nodeVitals.Count(n => string.Equals(n.Status, "online", StringComparison.OrdinalIgnoreCase)),
            Nodes: nodeVitals,
            FetchedAt: DateTimeOffset.UtcNow
        );

        Assert.Equal(2, vitals.TotalNodes);
        Assert.Equal(1, vitals.OnlineNodes);
        Assert.Equal(15.5, vitals.Nodes[0].CpuUsagePct);
        Assert.Equal(50.0, vitals.Nodes[0].MemoryUsagePct);
        Assert.Equal(86400, vitals.Nodes[0].UptimeSeconds);
        Assert.Null(vitals.Nodes[1].CpuUsagePct);
    }

    [Fact]
    public void KubernetesClusterVitalsDto_CalculatesReadinessAndCounts()
    {
        var nodeVitals = new List<K8sNodeVitalsDto>
        {
            new K8sNodeVitalsDto("k8s-master", true, false, new List<string> { "control-plane" }, "Ubuntu 24.04", "6.8.0", 12),
            new K8sNodeVitalsDto("k8s-worker-01", true, false, new List<string> { "worker" }, "Ubuntu 24.04", "6.8.0", 8),
            new K8sNodeVitalsDto("k8s-worker-02", false, true, new List<string> { "worker" }, "Ubuntu 24.04", "6.8.0", 0)
        };

        var vitals = new KubernetesClusterVitalsDto(
            ClusterId: "k8s-prod",
            ClusterName: "Prod Cluster",
            TotalNodes: nodeVitals.Count,
            ReadyNodes: nodeVitals.Count(n => n.IsReady),
            TotalPods: 20,
            RunningPods: 18,
            TotalNamespaces: 6,
            LatencyMs: 12,
            Nodes: nodeVitals,
            FetchedAt: DateTimeOffset.UtcNow
        );

        Assert.Equal(3, vitals.TotalNodes);
        Assert.Equal(2, vitals.ReadyNodes);
        Assert.Equal(20, vitals.TotalPods);
        Assert.Equal(18, vitals.RunningPods);
        Assert.Equal(6, vitals.TotalNamespaces);
        Assert.Equal(12, vitals.LatencyMs);
        Assert.Equal("k8s-prod", vitals.ClusterId);
    }
}
