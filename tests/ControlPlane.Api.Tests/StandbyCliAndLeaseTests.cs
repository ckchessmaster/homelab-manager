using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Api.Features.Cluster;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using ControlPlane.Cli.Synchronization;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class StandbyCliAndLeaseTests
{
    private class ClusterTestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-cluster-{Guid.NewGuid():N}.db");

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

    private static HttpClient CreateAuthClient(ClusterTestAppFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");
        return client;
    }

    [Fact]
    public async Task ExportSnapshot_ReturnsAllHostsJobsAndLogs()
    {
        using var factory = new ClusterTestAppFactory();
        var client = CreateAuthClient(factory);

        // Seed some data into DB
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            await db.Database.EnsureCreatedAsync();

            var host = new HostEntity
            {
                Id = Guid.NewGuid(),
                Hostname = "k8s-node-01",
                IpAddress = "192.168.1.101",
                OsFamily = "linux_debian",
                TargetType = "proxmox_vm",
                Proxmox = new ProxmoxTarget { Node = "pve-01", Vmid = 101 }
            };
            db.Hosts.Add(host);

            var job = new UpdateJob
            {
                Id = Guid.NewGuid(),
                TargetHostId = host.Id,
                InitiatedBy = "Operator",
                Status = "Completed",
                ActiveStep = "Health Probes",
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                CompletedAt = DateTimeOffset.UtcNow
            };
            db.UpdateJobs.Add(job);

            var log = new StepLog
            {
                JobId = job.Id,
                SequenceId = 1,
                StreamType = "stdout",
                LogLine = "Kernel updated successfully",
                Timestamp = DateTimeOffset.UtcNow
            };
            db.StepLogs.Add(log);

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/v1/cluster/export-snapshot");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var snapshot = await response.Content.ReadFromJsonAsync<ClusterSnapshot>();
        Assert.NotNull(snapshot);
        Assert.NotEmpty(snapshot.Hosts);
        Assert.Contains(snapshot.Hosts, h => h.Hostname == "k8s-node-01" && h.ProxmoxVmid == 101);
        Assert.NotEmpty(snapshot.UpdateJobs);
        Assert.NotEmpty(snapshot.StepLogs);
        Assert.Contains(snapshot.StepLogs, l => l.LogLine == "Kernel updated successfully");
    }

    [Fact]
    public async Task LeaseAcquisition_And_Release_TransitionsSuspension()
    {
        using var factory = new ClusterTestAppFactory();
        var client = CreateAuthClient(factory);
        var clusterState = factory.Services.GetRequiredService<ClusterState>();

        // Clear any pre-seeded standby lease to test fresh acquisition
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.ClusterLeases.RemoveRange(db.ClusterLeases);
            await db.SaveChangesAsync();
        }

        // 1. Initial status
        clusterState.IsSuspended = false;
        clusterState.CurrentLeaseHolder = null;
        Assert.False(clusterState.IsSuspended);

        // 2. Acquire lease
        var acquireReq = new LeaseAcquireRequest("StandbyCli-MacBook", 60);
        var acquireResp = await client.PostAsJsonAsync("/api/v1/cluster/lease-acquire", acquireReq);
        Assert.Equal(HttpStatusCode.OK, acquireResp.StatusCode);
        Assert.True(clusterState.IsSuspended);
        Assert.Equal("StandbyCli-MacBook", clusterState.CurrentLeaseHolder);

        // 3. Competing acquisition conflict
        var competingReq = new LeaseAcquireRequest("StandbyCli-OtherWorkstation", 60);
        var conflictResp = await client.PostAsJsonAsync("/api/v1/cluster/lease-acquire", competingReq);
        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);

        // 4. Release lease
        var releaseReq = new LeaseReleaseRequest("StandbyCli-MacBook");
        var releaseResp = await client.PostAsJsonAsync("/api/v1/cluster/lease-release", releaseReq);
        Assert.Equal(HttpStatusCode.OK, releaseResp.StatusCode);
        Assert.False(clusterState.IsSuspended);
        Assert.Null(clusterState.CurrentLeaseHolder);
    }

    [Fact]
    public async Task SnapshotPuller_SeedsLocalSqliteDatabase_Idempotently()
    {
        var tempSqlitePath = Path.Combine(Path.GetTempPath(), $"standby-puller-test-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseSqlite($"Data Source={tempSqlitePath}")
            .UseSnakeCaseNamingConvention()
            .Options;

        try
        {
            var hostId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            var testSnapshot = new ClusterSnapshot(
                ExportedAt: DateTimeOffset.UtcNow,
                Hosts: new List<HostSnapshotDto>
                {
                    new(hostId, "standby-worker-1", "Standby 1", "192.168.1.55", "linux_debian", "baremetal",
                        null, null, "192.168.1.155", null, null, true, DateTimeOffset.UtcNow, false, 3, "1.0.0",
                        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                },
                UpdateJobs: new List<JobSnapshotDto>
                {
                    new(jobId, hostId, "AutoTester", "Completed", "Done", null, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow, null)
                },
                StepLogs: new List<StepLogSnapshotDto>
                {
                    new(1, jobId, 1, "stdout", "Test log output", DateTimeOffset.UtcNow)
                }
            );

            using var httpClient = new HttpClient();
            var puller = new SnapshotPuller(httpClient, NullLogger<SnapshotPuller>.Instance);

            using (var db = new ControlPlaneDbContext(options))
            {
                await puller.SeedLocalDatabaseAsync(testSnapshot, db);

                var seededHost = await db.Hosts.FindAsync(hostId);
                Assert.NotNull(seededHost);
                Assert.Equal("standby-worker-1", seededHost.Hostname);
                Assert.Equal("192.168.1.155", seededHost.Idrac?.IpAddress);

                var seededJob = await db.UpdateJobs.FindAsync(jobId);
                Assert.NotNull(seededJob);
                Assert.Equal("Completed", seededJob.Status);

                var seededLogs = await db.StepLogs.Where(l => l.JobId == jobId).ToListAsync();
                Assert.Single(seededLogs);
                Assert.Equal("Test log output", seededLogs[0].LogLine);
            }
        }
        finally
        {
            if (File.Exists(tempSqlitePath))
            {
                try { File.Delete(tempSqlitePath); } catch { }
            }
        }
    }

    [Fact]
    public async Task ReconcileDelta_IngestsStandbyJobsAndLogs()
    {
        using var factory = new ClusterTestAppFactory();
        var client = CreateAuthClient(factory);

        var hostId = Guid.NewGuid();
        // Seed host into primary first
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Hosts.Add(new HostEntity
            {
                Id = hostId,
                Hostname = "node-for-delta",
                IpAddress = "192.168.1.199",
                OsFamily = "linux_debian",
                TargetType = "baremetal"
            });
            await db.SaveChangesAsync();
        }

        // Prepare delta with a new job and log lines executed on standby
        var standbyJobId = Guid.NewGuid();
        var deltaPayload = new DeltaSyncPayload(
            Hosts: new List<HostSnapshotDto>(),
            UpdateJobs: new List<JobSnapshotDto>
            {
                new(standbyJobId, hostId, "StandbyOperator", "Completed", "Health Probes", "snap-01",
                    DateTimeOffset.UtcNow.AddMinutes(-3), DateTimeOffset.UtcNow, null)
            },
            StepLogs: new List<StepLogSnapshotDto>
            {
                new(0, standbyJobId, 1, "stdout", "Standby execution log line 1", DateTimeOffset.UtcNow.AddMinutes(-2)),
                new(0, standbyJobId, 2, "stdout", "Standby execution log line 2", DateTimeOffset.UtcNow.AddMinutes(-1))
            }
        );

        var response = await client.PostAsJsonAsync("/api/v1/cluster/reconcile-delta", deltaPayload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify primary database now has the standby job and logs
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

            var job = await db.UpdateJobs.FindAsync(standbyJobId);
            Assert.NotNull(job);
            Assert.Equal("StandbyOperator", job.InitiatedBy);
            Assert.Equal("Completed", job.Status);

            var logs = await db.StepLogs.Where(l => l.JobId == standbyJobId).OrderBy(l => l.SequenceId).ToListAsync();
            Assert.Equal(2, logs.Count);
            Assert.Equal("Standby execution log line 1", logs[0].LogLine);
            Assert.Equal("Standby execution log line 2", logs[1].LogLine);
        }
    }
}
