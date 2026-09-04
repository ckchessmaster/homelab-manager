using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class SqliteStorageTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ControlPlaneDbContext> _options;

    public SqliteStorageTests()
    {
        // Use in-memory SQLite connection for isolated tests
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private ControlPlaneDbContext CreateContext() => new(_options);

    [Fact]
    public async Task EnsureCreated_CreatesSchemaSuccessfully()
    {
        using var context = CreateContext();
        var created = await context.Database.EnsureCreatedAsync();

        Assert.True(created);
    }

    [Fact]
    public async Task CanInsertAndQueryHost_WithOwnedEntities()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var host = new HostEntity
        {
            Id = Guid.NewGuid(),
            Hostname = "srv-compute-01",
            FriendlyName = "Compute Server 1",
            IpAddress = "10.0.0.15",
            OsFamily = "linux_debian",
            TargetType = "proxmox_vm",
            Proxmox = new ProxmoxTarget
            {
                Node = "pve-cluster-01",
                Vmid = 500
            },
            Agent = new AgentState
            {
                Installed = true,
                Version = "1.2.0",
                LastSeenAt = DateTimeOffset.UtcNow,
                PendingReboot = false,
                UpgradablePackagesCount = 3
            }
        };

        context.Hosts.Add(host);
        await context.SaveChangesAsync();

        var queried = await context.Hosts.FirstOrDefaultAsync(h => h.Hostname == "srv-compute-01");
        Assert.NotNull(queried);
        Assert.Equal("10.0.0.15", queried.IpAddress);
        Assert.Equal("proxmox_vm", queried.TargetType);
        Assert.NotNull(queried.Proxmox);
        Assert.Equal("pve-cluster-01", queried.Proxmox.Node);
        Assert.Equal(500, queried.Proxmox.Vmid);
        Assert.True(queried.Agent.Installed);
        Assert.Equal(3, queried.Agent.UpgradablePackagesCount);
    }

    [Fact]
    public async Task UniqueConstraint_Hostname_ThrowsOnDuplicate()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var host1 = new HostEntity
        {
            Id = Guid.NewGuid(),
            Hostname = "duplicate-host",
            IpAddress = "10.0.0.21",
            OsFamily = "linux_debian",
            TargetType = "baremetal"
        };
        var host2 = new HostEntity
        {
            Id = Guid.NewGuid(),
            Hostname = "duplicate-host",
            IpAddress = "10.0.0.22",
            OsFamily = "linux_debian",
            TargetType = "baremetal"
        };

        context.Hosts.Add(host1);
        await context.SaveChangesAsync();

        context.Hosts.Add(host2);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task JobAndStepLogs_CascadeDelete_WorksProperly()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var host = new HostEntity
        {
            Id = Guid.NewGuid(),
            Hostname = "k8s-worker-10",
            IpAddress = "10.0.0.30",
            OsFamily = "linux_debian",
            TargetType = "proxmox_vm"
        };
        context.Hosts.Add(host);
        await context.SaveChangesAsync();

        var job = new UpdateJob
        {
            Id = Guid.NewGuid(),
            TargetHostId = host.Id,
            InitiatedBy = "admin",
            Status = "Running",
            ActiveStep = "apt_upgrade",
            StartedAt = DateTimeOffset.UtcNow
        };
        context.UpdateJobs.Add(job);
        await context.SaveChangesAsync();

        var log1 = new StepLog
        {
            JobId = job.Id,
            SequenceId = 1,
            StreamType = "stdout",
            LogLine = "Reading package lists... Done",
            Timestamp = DateTimeOffset.UtcNow
        };
        var log2 = new StepLog
        {
            JobId = job.Id,
            SequenceId = 2,
            StreamType = "stdout",
            LogLine = "Building dependency tree... Done",
            Timestamp = DateTimeOffset.UtcNow
        };
        context.StepLogs.AddRange(log1, log2);
        await context.SaveChangesAsync();

        var savedLogs = await context.StepLogs.Where(l => l.JobId == job.Id).OrderBy(l => l.SequenceId).ToListAsync();
        Assert.Equal(2, savedLogs.Count);

        // Delete job and verify cascade delete on step_logs
        context.UpdateJobs.Remove(job);
        await context.SaveChangesAsync();

        var remainingLogs = await context.StepLogs.Where(l => l.JobId == job.Id).ToListAsync();
        Assert.Empty(remainingLogs);
    }

    [Fact]
    public async Task ClusterLease_CanAcquireAndQuery()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var lease = new ClusterLease
        {
            LeaseKey = "GLOBAL_MAINTENANCE_LOCK",
            HolderIdentifier = "standby-cli-ws01",
            AcquiredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        context.ClusterLeases.Add(lease);
        await context.SaveChangesAsync();

        var retrieved = await context.ClusterLeases.FindAsync("GLOBAL_MAINTENANCE_LOCK");
        Assert.NotNull(retrieved);
        Assert.Equal("standby-cli-ws01", retrieved.HolderIdentifier);
    }

    [Fact]
    public async Task DbSeeder_SeedsStandbyStateWhenEmpty()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        await DbSeeder.SeedStandbyAsync(context);

        var hostCount = await context.Hosts.CountAsync();
        var lease = await context.ClusterLeases.FindAsync("GLOBAL_MAINTENANCE_LOCK");

        Assert.Equal(2, hostCount);
        Assert.NotNull(lease);
        Assert.Equal("controlplane-standby", lease.HolderIdentifier);
    }
}
