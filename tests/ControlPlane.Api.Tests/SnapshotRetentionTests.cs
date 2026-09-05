using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Tests;

public class SnapshotRetentionTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly ControlPlaneDbContext _db;

    public SnapshotRetentionTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        var dbOptions = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseSqlite(_sqliteConnection)
            .UseSnakeCaseNamingConvention()
            .Options;

        _db = new ControlPlaneDbContext(dbOptions);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private class MockHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public MockHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    [Fact]
    public async Task ListVmSnapshotsAsync_FiltersOutCurrentPseudoSnapshot()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains("/nodes/pve1/qemu/101/snapshot"))
            {
                var payload = new ProxmoxSnapshotListResponse(new List<ProxmoxSnapshotItem>
                {
                    new("current", null, "Current state", null, null),
                    new("cp-pre-update-20260904000000", 1788480000, "Safety snapshot", 0, null),
                    new("manual-backup", 1788470000, "Operator manual backup", 0, null)
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(payload)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new ProxmoxClient(
            new MockHttpClientFactory(new HttpClient(handler)),
            Options.Create(new ProxmoxOptions
            {
                BaseUrl = "https://pve.local:8006",
                ApiTokenId = "root@pam!token",
                ApiTokenSecret = "secret"
            }),
            new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance),
            NullLogger<ProxmoxClient>.Instance
        );

        var snapshots = await client.ListVmSnapshotsAsync("pve1", 101);

        Assert.Equal(2, snapshots.Count);
        Assert.DoesNotContain(snapshots, s => s.Name == "current");
        Assert.Contains(snapshots, s => s.Name == "cp-pre-update-20260904000000");
        Assert.Contains(snapshots, s => s.Name == "manual-backup");
    }

    [Fact]
    public async Task GetSnapshotsAsync_CalculatesAge_AndProtectsActiveRunningJob()
    {
        var host = new Host
        {
            Id = Guid.NewGuid(),
            Hostname = "k8s-worker-01",
            FriendlyName = "K8s Worker 01",
            IpAddress = "192.168.1.150",
            TargetType = "proxmox_vm",
            Proxmox = new ProxmoxTarget { Node = "pve1", Vmid = 101 }
        };
        _db.Hosts.Add(host);

        // Add active job running referencing the snapshot
        var activeJob = new UpdateJob
        {
            Id = Guid.NewGuid(),
            TargetHostId = host.Id,
            InitiatedBy = "Operator",
            Status = "Running",
            SnapshotIdentifier = "cp-pre-update-20260901000000"
        };
        _db.UpdateJobs.Add(activeJob);
        await _db.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains("/nodes/pve1/qemu/101/snapshot"))
            {
                var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var payload = new ProxmoxSnapshotListResponse(new List<ProxmoxSnapshotItem>
                {
                    // 3 days old, but protected by active running job!
                    new("cp-pre-update-20260901000000", nowSec - (72 * 3600), "Old safety snapshot", 0, null),
                    // 2 hours old, unexpired
                    new("cp-pre-update-recent", nowSec - (2 * 3600), "Recent safety snapshot", 0, null),
                    // 5 days old, but manual snapshot (not CP)
                    new("user-golden-image", nowSec - (120 * 3600), "Manual image", 0, null)
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var proxmoxClient = new ProxmoxClient(
            new MockHttpClientFactory(new HttpClient(handler)),
            Options.Create(new ProxmoxOptions
            {
                BaseUrl = "https://pve.local:8006",
                ApiTokenId = "root@pam!token",
                ApiTokenSecret = "secret"
            }),
            new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance),
            NullLogger<ProxmoxClient>.Instance
        );

        var service = new SnapshotRetentionService(
            _db,
            proxmoxClient,
            Options.Create(new SnapshotRetentionOptions { RetentionHours = 24 }),
            NullLogger<SnapshotRetentionService>.Instance
        );

        var snapshots = await service.GetSnapshotsAsync();

        Assert.Equal(3, snapshots.Count);

        var protectedSnap = snapshots.First(s => s.Name == "cp-pre-update-20260901000000");
        Assert.True(protectedSnap.IsProtectedByActiveJob);
        Assert.True(protectedSnap.IsExpired);
        Assert.False(protectedSnap.CanPrune); // MUST NOT prune because active job is running!

        var recentSnap = snapshots.First(s => s.Name == "cp-pre-update-recent");
        Assert.False(recentSnap.IsProtectedByActiveJob);
        Assert.False(recentSnap.IsExpired);
        Assert.False(recentSnap.CanPrune);

        var manualSnap = snapshots.First(s => s.Name == "user-golden-image");
        Assert.False(manualSnap.IsControlPlaneSnapshot);
        Assert.False(manualSnap.IsExpired);
        Assert.False(manualSnap.CanPrune);
    }

    [Fact]
    public async Task PruneExpiredSnapshotsAsync_DeletesExpiredCompletedJobSnapshot()
    {
        var host = new Host
        {
            Id = Guid.NewGuid(),
            Hostname = "db-vm-01",
            FriendlyName = "Database VM",
            IpAddress = "192.168.1.160",
            TargetType = "proxmox_vm",
            Proxmox = new ProxmoxTarget { Node = "pve2", Vmid = 202 }
        };
        _db.Hosts.Add(host);

        // Job has completed
        var completedJob = new UpdateJob
        {
            Id = Guid.NewGuid(),
            TargetHostId = host.Id,
            InitiatedBy = "Operator",
            Status = "Completed",
            SnapshotIdentifier = "cp-pre-update-20260901120000"
        };
        _db.UpdateJobs.Add(completedJob);
        await _db.SaveChangesAsync();

        bool deleteCalled = false;
        var deletePath = "";

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;

            if (req.Method == HttpMethod.Get && path.Contains("/nodes/pve2/qemu/202/snapshot"))
            {
                var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var payload = new ProxmoxSnapshotListResponse(new List<ProxmoxSnapshotItem>
                {
                    new("cp-pre-update-20260901120000", nowSec - (30 * 3600), "Expired snapshot", 0, null)
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
            }

            if (req.Method == HttpMethod.Delete && path.Contains("/nodes/pve2/qemu/202/snapshot/cp-pre-update-20260901120000"))
            {
                deleteCalled = true;
                deletePath = path;
                var upid = "UPID:pve2:00012345:00000000:60000000:qmdelsnap:202:root@pam:";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ProxmoxTaskResponse(upid))
                };
            }

            if (req.Method == HttpMethod.Get && path.Contains("/tasks/") && path.EndsWith("/status"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ProxmoxTaskStatusResponse(new ProxmoxTaskStatus("stopped", "OK")))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var proxmoxClient = new ProxmoxClient(
            new MockHttpClientFactory(new HttpClient(handler)),
            Options.Create(new ProxmoxOptions
            {
                BaseUrl = "https://pve.local:8006",
                ApiTokenId = "root@pam!token",
                ApiTokenSecret = "secret"
            }),
            new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance),
            NullLogger<ProxmoxClient>.Instance
        );

        var service = new SnapshotRetentionService(
            _db,
            proxmoxClient,
            Options.Create(new SnapshotRetentionOptions { RetentionHours = 24 }),
            NullLogger<SnapshotRetentionService>.Instance
        );

        var result = await service.PruneExpiredSnapshotsAsync(dryRun: false);

        Assert.Equal(1, result.TotalScanned);
        Assert.Equal(1, result.ExpiredCount);
        Assert.Equal(1, result.PrunedCount);
        Assert.Empty(result.Errors);
        Assert.True(deleteCalled);
        Assert.Contains("cp-pre-update-20260901120000", deletePath);
    }

    [Fact]
    public async Task PruneExpiredSnapshotsAsync_DryRunDoesNotInvokeDelete()
    {
        var host = new Host
        {
            Id = Guid.NewGuid(),
            Hostname = "web-vm-01",
            FriendlyName = "Web VM",
            IpAddress = "192.168.1.170",
            TargetType = "proxmox_vm",
            Proxmox = new ProxmoxTarget { Node = "pve1", Vmid = 105 }
        };
        _db.Hosts.Add(host);
        await _db.SaveChangesAsync();

        bool deleteCalled = false;

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;

            if (req.Method == HttpMethod.Get && path.Contains("/nodes/pve1/qemu/105/snapshot"))
            {
                var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var payload = new ProxmoxSnapshotListResponse(new List<ProxmoxSnapshotItem>
                {
                    new("cp-pre-update-20260901120000", nowSec - (50 * 3600), "Expired snapshot", 0, null)
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
            }

            if (req.Method == HttpMethod.Delete)
            {
                deleteCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var proxmoxClient = new ProxmoxClient(
            new MockHttpClientFactory(new HttpClient(handler)),
            Options.Create(new ProxmoxOptions
            {
                BaseUrl = "https://pve.local:8006",
                ApiTokenId = "root@pam!token",
                ApiTokenSecret = "secret"
            }),
            new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance),
            NullLogger<ProxmoxClient>.Instance
        );

        var service = new SnapshotRetentionService(
            _db,
            proxmoxClient,
            Options.Create(new SnapshotRetentionOptions { RetentionHours = 24 }),
            NullLogger<SnapshotRetentionService>.Instance
        );

        var result = await service.PruneExpiredSnapshotsAsync(dryRun: true);

        Assert.Equal(1, result.TotalScanned);
        Assert.Equal(1, result.ExpiredCount);
        Assert.Equal(1, result.PrunedCount);
        Assert.False(deleteCalled, "Dry-run mode must never issue DELETE requests.");
        Assert.Contains("[DRY-RUN]", result.Items[0].Message);
    }

    [Fact]
    public async Task DeleteSnapshotAsync_DeletesSpecificSnapshotSuccessfully()
    {
        var host = new Host
        {
            Id = Guid.NewGuid(),
            Hostname = "manual-vm",
            FriendlyName = "Manual VM",
            IpAddress = "192.168.1.180",
            TargetType = "proxmox_vm",
            Proxmox = new ProxmoxTarget { Node = "pve1", Vmid = 300 }
        };
        _db.Hosts.Add(host);
        await _db.SaveChangesAsync();

        bool deleteCalled = false;

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;

            if (req.Method == HttpMethod.Delete && path.Contains("/nodes/pve1/qemu/300/snapshot/operator-backup"))
            {
                deleteCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ProxmoxTaskResponse("UPID:pve1:0001:qmdelsnap:300:root@pam:"))
                };
            }

            if (req.Method == HttpMethod.Get && path.Contains("/tasks/") && path.EndsWith("/status"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ProxmoxTaskStatusResponse(new ProxmoxTaskStatus("stopped", "OK")))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var proxmoxClient = new ProxmoxClient(
            new MockHttpClientFactory(new HttpClient(handler)),
            Options.Create(new ProxmoxOptions
            {
                BaseUrl = "https://pve.local:8006",
                ApiTokenId = "root@pam!token",
                ApiTokenSecret = "secret"
            }),
            new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance),
            NullLogger<ProxmoxClient>.Instance
        );

        var service = new SnapshotRetentionService(
            _db,
            proxmoxClient,
            Options.Create(new SnapshotRetentionOptions { RetentionHours = 24 }),
            NullLogger<SnapshotRetentionService>.Instance
        );

        var success = await service.DeleteSnapshotAsync(host.Id, "operator-backup");

        Assert.True(success);
        Assert.True(deleteCalled);
    }
}

