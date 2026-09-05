using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Config;
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
using Microsoft.Extensions.Options;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class ProxmoxSnapshotAndRollbackTests
{
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
        public List<string> DeletedSnapshots { get; } = new();

        public Func<string, int, string, string?, bool, string>? OnCreateSnapshot { get; set; }
        public Func<string, int, string, bool, string>? OnRollbackSnapshot { get; set; }
        public Func<string, string, ProxmoxTaskStatus>? OnGetTaskStatus { get; set; }

        public Task<string> CreateVmSnapshotAsync(string node, int vmid, string snapName, string? description = null, bool isLxc = false, CancellationToken ct = default)
        {
            CreatedSnapshots.Add(snapName);
            var upid = OnCreateSnapshot?.Invoke(node, vmid, snapName, description, isLxc) ?? $"UPID:{node}:0001:snap:{snapName}";
            return Task.FromResult(upid);
        }

        public Task<string> RollbackVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default)
        {
            RolledBackSnapshots.Add(snapName);
            var upid = OnRollbackSnapshot?.Invoke(node, vmid, snapName, isLxc) ?? $"UPID:{node}:0002:rollback:{snapName}";
            return Task.FromResult(upid);
        }

        public Task<string> DeleteVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default)
        {
            DeletedSnapshots.Add(snapName);
            return Task.FromResult($"UPID:{node}:0003:delete:{snapName}");
        }

        public Task<ProxmoxTaskStatus> GetTaskStatusAsync(string node, string upid, CancellationToken ct = default)
        {
            var status = OnGetTaskStatus?.Invoke(node, upid) ?? new ProxmoxTaskStatus("stopped", "OK", "100", node, "qmsnapshot");
            return Task.FromResult(status);
        }

        public Task<ProxmoxTaskStatus> PollTaskCompletionAsync(string node, string upid, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            var status = OnGetTaskStatus?.Invoke(node, upid) ?? new ProxmoxTaskStatus("stopped", "OK", "100", node, "qmsnapshot");
            if (!status.IsSuccess)
            {
                throw new InvalidOperationException($"Proxmox task '{upid}' failed with exit status: {status.ExitStatus ?? "unknown"}");
            }
            return Task.FromResult(status);
        }

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

        public Func<string, int, bool, bool>? OnHasSnapshotFeature { get; set; }
        public Task<bool> HasSnapshotFeatureAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) =>
            Task.FromResult(OnHasSnapshotFeature?.Invoke(node, vmid, isLxc) ?? true);
    }

    private class ProxmoxTestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-proxmox-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("STANDBY_MODE", "true");
            builder.UseSetting("ControlPlane:ApiKey", "dev-secret-key-123");
            builder.UseSetting("ConnectionStrings:PostgresDatabase", "");
            builder.UseSetting("Proxmox:BaseUrl", "https://pve.homelab.local:8006");
            builder.UseSetting("Proxmox:ApiTokenId", "root@pam!testtoken");
            builder.UseSetting("Proxmox:ApiTokenSecret", "test-secret-uuid");
            builder.UseSetting("Proxmox:AllowSelfSignedCert", "true");
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
    // 1. ProxmoxClient & ProxmoxTaskPoller Tests
    // =========================================================================

    [Fact]
    [Trait("Category", "ProxmoxIntegration")]
    public async Task ProxmoxClient_CreateVmSnapshotAsync_SendsCorrectRequestAndReturnsUpid()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "data": "UPID:pve-01:00001:snap-01" }""")
            };
        });

        var client = new HttpClient(mockHandler);
        var factory = new MockHttpClientFactory(client);
        var options = Options.Create(new ProxmoxOptions
        {
            BaseUrl = "https://pve.homelab.local:8006",
            ApiTokenId = "root@pam!token1",
            ApiTokenSecret = "secret-123",
            AllowSelfSignedCert = true
        });

        var poller = new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance);
        var proxmoxClient = new ProxmoxClient(factory, options, poller, NullLogger<ProxmoxClient>.Instance);

        var upid = await proxmoxClient.CreateVmSnapshotAsync(
            node: "pve-01",
            vmid: 100,
            snapName: "cp-pre-update-20260904",
            description: "Safety snapshot",
            isLxc: false
        );

        Assert.Equal("UPID:pve-01:00001:snap-01", upid);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://pve.homelab.local:8006/api2/json/nodes/pve-01/qemu/100/snapshot", capturedRequest.RequestUri!.ToString());
        Assert.True(capturedRequest.Headers.Contains("Authorization"));
        Assert.Equal("PVEAPIToken=root@pam!token1=secret-123", capturedRequest.Headers.GetValues("Authorization").First());

        Assert.NotNull(capturedBody);
        Assert.Contains("cp-pre-update-20260904", capturedBody);
        Assert.Contains("Safety snapshot", capturedBody);
    }

    [Fact]
    [Trait("Category", "ProxmoxIntegration")]
    public async Task ProxmoxClient_CreateLxcSnapshotAsync_TargetsLxcEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "data": "UPID:pve-01:00002:lxc-snap" }""")
            };
        });

        var client = new HttpClient(mockHandler);
        var factory = new MockHttpClientFactory(client);
        var options = Options.Create(new ProxmoxOptions
        {
            BaseUrl = "https://pve.homelab.local:8006",
            ApiTokenId = "root@pam!token1",
            ApiTokenSecret = "secret-123"
        });

        var poller = new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance);
        var proxmoxClient = new ProxmoxClient(factory, options, poller, NullLogger<ProxmoxClient>.Instance);

        var upid = await proxmoxClient.CreateVmSnapshotAsync(
            node: "pve-01",
            vmid: 200,
            snapName: "cp-lxc-snap",
            isLxc: true
        );

        Assert.Equal("UPID:pve-01:00002:lxc-snap", upid);
        Assert.NotNull(capturedRequest);
        Assert.Equal("https://pve.homelab.local:8006/api2/json/nodes/pve-01/lxc/200/snapshot", capturedRequest.RequestUri!.ToString());
    }

    [Fact]
    [Trait("Category", "ProxmoxIntegration")]
    public async Task ProxmoxClient_RollbackVmSnapshotAsync_CallsRollbackEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "data": "UPID:pve-01:00003:rollback" }""")
            };
        });

        var client = new HttpClient(mockHandler);
        var factory = new MockHttpClientFactory(client);
        var options = Options.Create(new ProxmoxOptions
        {
            BaseUrl = "https://pve.homelab.local:8006",
            ApiTokenId = "root@pam!token1",
            ApiTokenSecret = "secret-123"
        });

        var poller = new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance);
        var proxmoxClient = new ProxmoxClient(factory, options, poller, NullLogger<ProxmoxClient>.Instance);

        var upid = await proxmoxClient.RollbackVmSnapshotAsync("pve-01", 100, "cp-pre-update-123");

        Assert.Equal("UPID:pve-01:00003:rollback", upid);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://pve.homelab.local:8006/api2/json/nodes/pve-01/qemu/100/snapshot/cp-pre-update-123/rollback", capturedRequest.RequestUri!.ToString());
    }

    [Fact]
    [Trait("Category", "ProxmoxIntegration")]
    public async Task ProxmoxTaskPoller_PollUntilStopped_ReturnsOnSuccess()
    {
        var poller = new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance);
        var attempts = 0;

        var result = await poller.PollUntilStoppedAsync(
            "pve-01",
            "UPID:pve-01:1234",
            (node, upid, ct) =>
            {
                attempts++;
                if (attempts < 2)
                {
                    return Task.FromResult(new ProxmoxTaskStatus("running"));
                }
                return Task.FromResult(new ProxmoxTaskStatus("stopped", "OK"));
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(10)
        );

        Assert.True(result.IsStopped);
        Assert.True(result.IsSuccess);
        Assert.Equal("OK", result.ExitStatus);
        Assert.True(attempts >= 2);
    }

    [Fact]
    [Trait("Category", "ProxmoxIntegration")]
    public async Task ProxmoxTaskPoller_PollUntilStopped_ThrowsOnTaskError()
    {
        var poller = new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await poller.PollUntilStoppedAsync(
                "pve-01",
                "UPID:pve-01:error",
                (node, upid, ct) => Task.FromResult(new ProxmoxTaskStatus("stopped", "ERROR: snapshot failed")),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromMilliseconds(10)
            );
        });
    }

    [Fact]
    [Trait("Category", "ProxmoxIntegration")]
    public async Task ProxmoxTaskPoller_PollUntilStopped_ThrowsOnTimeout()
    {
        var poller = new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await poller.PollUntilStoppedAsync(
                "pve-01",
                "UPID:pve-01:hang",
                (node, upid, ct) => Task.FromResult(new ProxmoxTaskStatus("running")),
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(20)
            );
        });
    }

    // =========================================================================
    // 2. ProxmoxSnapshotStep & ProxmoxRollbackStep Tests
    // =========================================================================

    [Fact]
    [Trait("Category", "ProxmoxIntegration")]
    public async Task ProxmoxSnapshotStep_Skips_When_HostIsBaremetal()
    {
        using var factory = new ProxmoxTestAppFactory();
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
            Hostname = "baremetal-node",
            IpAddress = "192.168.1.100",
            OsFamily = "linux_debian",
            TargetType = "baremetal"
        };
        var job = new UpdateJob
        {
            Id = jobId,
            TargetHostId = hostId,
            Status = UpdateJobState.Pending
        };
        db.Hosts.Add(host);
        db.UpdateJobs.Add(job);
        await db.SaveChangesAsync();

        var mockProxmox = new MockProxmoxClient();
        var context = new JobExecutionContext(
            job,
            host,
            scopeFactory,
            hubContext,
            new MockCommandExecutor(),
            connMgr,
            NullLogger.Instance
        );

        var step = new ProxmoxSnapshotStep(mockProxmox);
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Skipped", result.Message);
        Assert.Empty(mockProxmox.CreatedSnapshots);
        Assert.Null(job.SnapshotIdentifier);
    }

    [Fact]
    [Trait("Category", "ProxmoxIntegration")]
    public async Task ProxmoxSnapshotStep_CreatesSnapshot_And_PersistsSnapshotIdentifier_For_ProxmoxVm()
    {
        using var factory = new ProxmoxTestAppFactory();
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
            Hostname = "vm-worker-01",
            IpAddress = "192.168.1.101",
            OsFamily = "linux_debian",
            TargetType = "proxmox_vm",
            Proxmox = new ProxmoxTarget
            {
                Node = "pve-01",
                Vmid = 105
            }
        };
        var job = new UpdateJob
        {
            Id = jobId,
            TargetHostId = hostId,
            Status = UpdateJobState.Running
        };
        db.Hosts.Add(host);
        db.UpdateJobs.Add(job);
        await db.SaveChangesAsync();

        var mockProxmox = new MockProxmoxClient();
        var context = new JobExecutionContext(
            job,
            host,
            scopeFactory,
            hubContext,
            new MockCommandExecutor(),
            connMgr,
            NullLogger.Instance
        );

        var step = new ProxmoxSnapshotStep(mockProxmox);
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(mockProxmox.CreatedSnapshots);
        var snapName = mockProxmox.CreatedSnapshots[0];
        Assert.StartsWith("cp-pre-update-", snapName);
        Assert.Equal(snapName, job.SnapshotIdentifier);

        // Verify snapshot identifier is persisted in the database update_jobs table
        var dbJob = await db.UpdateJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        Assert.NotNull(dbJob);
        Assert.Equal(snapName, dbJob.SnapshotIdentifier);
    }

    [Fact]
    [Trait("Category", "ProxmoxIntegration")]
    public async Task ProxmoxRollbackStep_ExecutesRollback_WhenSnapshotIdentifierExists()
    {
        using var factory = new ProxmoxTestAppFactory();
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
            Hostname = "vm-worker-02",
            IpAddress = "192.168.1.102",
            OsFamily = "linux_debian",
            TargetType = "proxmox_vm",
            Proxmox = new ProxmoxTarget
            {
                Node = "pve-01",
                Vmid = 106
            }
        };
        var job = new UpdateJob
        {
            Id = jobId,
            TargetHostId = hostId,
            Status = UpdateJobState.Running,
            SnapshotIdentifier = "cp-pre-update-20260904120000"
        };
        db.Hosts.Add(host);
        db.UpdateJobs.Add(job);
        await db.SaveChangesAsync();

        var mockProxmox = new MockProxmoxClient();
        var context = new JobExecutionContext(
            job,
            host,
            scopeFactory,
            hubContext,
            new MockCommandExecutor(),
            connMgr,
            NullLogger.Instance
        );

        var rollbackStep = new ProxmoxRollbackStep(mockProxmox);
        var result = await rollbackStep.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(mockProxmox.RolledBackSnapshots);
        Assert.Equal("cp-pre-update-20260904120000", mockProxmox.RolledBackSnapshots[0]);
        Assert.Equal(UpdateJobState.RolledBack, job.Status);

        var dbJob = await db.UpdateJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        Assert.NotNull(dbJob);
        Assert.Equal(UpdateJobState.RolledBack, dbJob.Status);
    }

    // =========================================================================
    // 3. End-to-End DAG Pipeline Failure & Automated Rollback
    // =========================================================================

    [Fact]
    [Trait("Category", "ProxmoxIntegration")]
    public async Task DagExecutionPipeline_TriggersAutomatedRollback_When_PackageUpgradeFails()
    {
        using var factory = new ProxmoxTestAppFactory();
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
            Hostname = "vm-rollback-node",
            IpAddress = "192.168.1.103",
            OsFamily = "linux_debian",
            TargetType = "proxmox_vm",
            Proxmox = new ProxmoxTarget
            {
                Node = "pve-cluster",
                Vmid = 110
            },
            Agent = new AgentState
            {
                Installed = true,
                LastSeenAt = DateTimeOffset.UtcNow
            }
        };
        var job = new UpdateJob
        {
            Id = jobId,
            TargetHostId = hostId,
            Status = UpdateJobState.Pending
        };
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

        connMgr.UpdateMetrics(hostId, new AgentMetrics { DiskFreePct = 50.0 });

        // Package upgrade will fail with exit code 100
        var mockExecutor = new MockCommandExecutor
        {
            OnExecute = (hId, jId, cmd, args) =>
            {
                if (args.Any(a => a.Contains("dist-upgrade") || a.Contains("upgrade")))
                {
                    return new AgentCommandResult(false, 100, "dpkg: error processing package linux-image (corrupted)");
                }
                return new AgentCommandResult(true, 0, null);
            }
        };

        var mockProxmox = new MockProxmoxClient();

        var context = new JobExecutionContext(
            job,
            host,
            scopeFactory,
            hubContext,
            mockExecutor,
            connMgr,
            NullLogger.Instance
        );

        // DAG with preflights, snapshot step, and package upgrade step
        var pipeline = new DagExecutionPipeline(new IJobStep[]
        {
            new PreflightHeartbeatCheckStep(),
            new PreflightDiskHeadroomCheckStep(),
            new PreflightPackageLockCheckStep(),
            new ProxmoxSnapshotStep(mockProxmox),
            new PackageUpgradeStep()
        });

        var pipelineSuccess = await pipeline.ExecuteAsync(context, CancellationToken.None);

        Assert.False(pipelineSuccess);

        // 1. Verify snapshot was created prior to upgrade
        Assert.Single(mockProxmox.CreatedSnapshots);
        var createdSnap = mockProxmox.CreatedSnapshots[0];
        Assert.StartsWith("cp-pre-update-", createdSnap);

        // 2. Verify snapshot rollback was triggered
        Assert.Single(mockProxmox.RolledBackSnapshots);
        Assert.Equal(createdSnap, mockProxmox.RolledBackSnapshots[0]);

        // 3. Verify job state in database is RolledBack
        var dbJob = await db.UpdateJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        Assert.NotNull(dbJob);
        Assert.Equal(UpdateJobState.RolledBack, dbJob.Status);
        Assert.Equal(createdSnap, dbJob.SnapshotIdentifier);
        Assert.NotNull(dbJob.CompletedAt);

        // 4. Verify logs contain the critical rollback alert
        var stepLogs = await db.StepLogs.Where(l => l.JobId == jobId).OrderBy(l => l.SequenceId).ToListAsync();
        Assert.NotEmpty(stepLogs);
        Assert.Contains(stepLogs, l => l.LogLine.Contains($"[ROLLBACK] Initiating automated hypervisor rollback to snapshot {createdSnap}"));
        Assert.Contains(stepLogs, l => l.LogLine.Contains("Automated rollback to snapshot"));

        await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "ProxmoxIntegration")]
    public async Task DagExecutionPipeline_Succeeds_And_RetainsSnapshotIdentifier_When_UpgradeSucceeds()
    {
        using var factory = new ProxmoxTestAppFactory();
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
            Hostname = "vm-success-node",
            IpAddress = "192.168.1.104",
            OsFamily = "linux_debian",
            TargetType = "proxmox_vm",
            Proxmox = new ProxmoxTarget
            {
                Node = "pve-01",
                Vmid = 112
            },
            Agent = new AgentState
            {
                Installed = true,
                LastSeenAt = DateTimeOffset.UtcNow
            }
        };
        var job = new UpdateJob
        {
            Id = jobId,
            TargetHostId = hostId,
            Status = UpdateJobState.Pending
        };
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

        connMgr.UpdateMetrics(hostId, new AgentMetrics { DiskFreePct = 50.0 });

        var mockExecutor = new MockCommandExecutor
        {
            OnExecute = (hId, jId, cmd, args) => new AgentCommandResult(true, 0, null)
        };

        var mockProxmox = new MockProxmoxClient();

        var context = new JobExecutionContext(
            job,
            host,
            scopeFactory,
            hubContext,
            mockExecutor,
            connMgr,
            NullLogger.Instance
        );

        var pipeline = new DagExecutionPipeline(new IJobStep[]
        {
            new PreflightHeartbeatCheckStep(),
            new PreflightDiskHeadroomCheckStep(),
            new PreflightPackageLockCheckStep(),
            new ProxmoxSnapshotStep(mockProxmox),
            new PackageUpgradeStep()
        });

        var pipelineSuccess = await pipeline.ExecuteAsync(context, CancellationToken.None);

        Assert.True(pipelineSuccess);

        // Snapshot created, but no rollback triggered
        Assert.Single(mockProxmox.CreatedSnapshots);
        Assert.Empty(mockProxmox.RolledBackSnapshots);

        // Job status Completed, snapshot identifier recorded in DB for 24h retention
        var dbJob = await db.UpdateJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        Assert.NotNull(dbJob);
        Assert.Equal(UpdateJobState.Completed, dbJob.Status);
        Assert.Equal(mockProxmox.CreatedSnapshots[0], dbJob.SnapshotIdentifier);

        await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "ProxmoxIntegration")]
    public async Task ProxmoxSnapshotStep_Parameterless_ResolvesFromScopeFactory_WithoutDisposingDbContext()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"cp-test-scope-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ControlPlaneDbContext>(options =>
            {
                options.UseSqlite($"Data Source={tempDb}")
                    .UseSnakeCaseNamingConvention();
            });

            // Configure default options with EMPTY BaseUrl (just like production appsettings)
            services.Configure<ProxmoxOptions>(opts =>
            {
                opts.BaseUrl = "";
                opts.ApiTokenId = "";
                opts.ApiTokenSecret = "";
            });

            var handler = new MockHttpMessageHandler(req =>
            {
                if (req.RequestUri!.PathAndQuery.Contains("/feature"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new { data = new { hasFeature = 1 } })
                    };
                }
                if (req.RequestUri.PathAndQuery.Contains("/snapshot"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new ProxmoxTaskResponse("UPID:pve-01:0001:snap:test"))
                    };
                }
                if (req.RequestUri.PathAndQuery.Contains("/tasks/"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new { data = new { status = "stopped", exitstatus = "OK" } })
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var client = new HttpClient(handler);
            var mockFactory = new MockHttpClientFactory(client);
            services.AddSingleton<IHttpClientFactory>(mockFactory);
            services.AddScoped<IAdapterConfigService, AdapterConfigService>();
            services.AddScoped<ProxmoxTaskPoller>();
            services.AddScoped<IProxmoxClient, ProxmoxClient>();
            services.AddSignalR();
            services.AddSingleton<AgentConnectionManager>();

            var sp = services.BuildServiceProvider();
            using (var initScope = sp.CreateScope())
            {
                var dbInit = initScope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
                await dbInit.Database.EnsureCreatedAsync();

                // Save Proxmox configuration into SystemSettings table in DB
                var configJson = JsonSerializer.Serialize(new
                {
                    baseUrl = "https://proxmox.local.chriskingdon.com",
                    apiTokenId = "user@pam!token",
                    apiTokenSecret = "secret-key",
                    allowSelfSignedCert = true,
                    taskPollTimeoutSeconds = 300,
                    taskPollIntervalMilliseconds = 1000
                });
                dbInit.SystemSettings.Add(new SystemSetting
                {
                    Key = "adapter:proxmox",
                    ValueJson = configJson,
                    UpdatedAt = DateTimeOffset.UtcNow
                });

                var host = new HostEntity
                {
                    Id = Guid.NewGuid(),
                    Hostname = "game-host-linux",
                    IpAddress = "192.168.1.150",
                    OsFamily = "linux_debian",
                    TargetType = "proxmox_vm",
                    Proxmox = new ProxmoxTarget
                    {
                        Node = "proxmox",
                        Vmid = 109
                    }
                };
                var job = new UpdateJob
                {
                    Id = Guid.NewGuid(),
                    TargetHostId = host.Id,
                    TargetHost = host,
                    PipelineId = "standard-os-upgrade",
                    Status = UpdateJobState.Running,
                    ActiveStep = "Proxmox Safety Snapshot"
                };
                dbInit.Hosts.Add(host);
                dbInit.UpdateJobs.Add(job);
                await dbInit.SaveChangesAsync();

                var scopeFactory = initScope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
                var hubContext = initScope.ServiceProvider.GetRequiredService<IHubContext<JobLogHub, IJobClient>>();
                var connMgr = initScope.ServiceProvider.GetRequiredService<AgentConnectionManager>();

                var context = new JobExecutionContext(
                    job,
                    host,
                    scopeFactory,
                    hubContext,
                    new MockCommandExecutor(),
                    connMgr,
                    NullLogger.Instance
                );

                // Instantiate parameterless ProxmoxSnapshotStep (relying on ScopeFactory dynamic resolution)
                var step = new ProxmoxSnapshotStep();
                var result = await step.ExecuteAsync(context, CancellationToken.None);

                Assert.True(result.Success, $"Expected step to succeed, but failed: {result.Message}");
                Assert.NotNull(job.SnapshotIdentifier);
                Assert.StartsWith("cp-pre-update-", job.SnapshotIdentifier);

                // Also test PipelineCatalog builds pipeline with injected scoped ProxmoxClient
                var catalog = new ControlPlane.Api.Features.Orchestration.Pipelines.PipelineCatalog();
                var pipeline = catalog.BuildPipeline("standard-os-upgrade", initScope.ServiceProvider);
                Assert.NotNull(pipeline);
                var snapStep = pipeline.Steps.OfType<ProxmoxSnapshotStep>().FirstOrDefault();
                Assert.NotNull(snapStep);
            }
        }
        finally
        {
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }

    [Fact]
    public async Task ProxmoxClient_HasSnapshotFeatureAsync_ReturnsTrue_WhenFeatureIs1()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "data": { "hasFeature": 1 } }""")
            };
        });

        var client = new HttpClient(mockHandler);
        var factory = new MockHttpClientFactory(client);
        var options = Options.Create(new ProxmoxOptions
        {
            BaseUrl = "https://pve.homelab.local:8006",
            ApiTokenId = "root@pam!token1",
            ApiTokenSecret = "secret-123"
        });

        var poller = new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance);
        var proxmoxClient = new ProxmoxClient(factory, options, poller, NullLogger<ProxmoxClient>.Instance);

        var result = await proxmoxClient.HasSnapshotFeatureAsync("pve-01", 100, isLxc: false);
        Assert.True(result);
    }

    [Fact]
    public async Task ProxmoxClient_HasSnapshotFeatureAsync_ReturnsFalse_WhenFeatureIs0()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "data": { "hasFeature": 0 } }""")
            };
        });

        var client = new HttpClient(mockHandler);
        var factory = new MockHttpClientFactory(client);
        var options = Options.Create(new ProxmoxOptions
        {
            BaseUrl = "https://pve.homelab.local:8006",
            ApiTokenId = "root@pam!token1",
            ApiTokenSecret = "secret-123"
        });

        var poller = new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance);
        var proxmoxClient = new ProxmoxClient(factory, options, poller, NullLogger<ProxmoxClient>.Instance);

        var result = await proxmoxClient.HasSnapshotFeatureAsync("pve-01", 100, isLxc: false);
        Assert.False(result);
    }

    [Fact]
    public async Task ProxmoxSnapshotStep_SkipsGracefully_WhenFeatureIsUnsupported()
    {
        var mockClient = new MockProxmoxClient
        {
            OnHasSnapshotFeature = (_, _, _) => false
        };

        var host = new HostEntity
        {
            Id = Guid.NewGuid(),
            Hostname = "game-host-linux",
            IpAddress = "192.168.1.150",
            OsFamily = "linux_debian",
            TargetType = "proxmox_vm",
            Proxmox = new ProxmoxTarget { Node = "proxmox", Vmid = 109 }
        };
        var job = new UpdateJob
        {
            Id = Guid.NewGuid(),
            TargetHostId = host.Id,
            TargetHost = host,
            Status = UpdateJobState.Running
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var hubContext = sp.GetRequiredService<IHubContext<JobLogHub, IJobClient>>();

        var context = new JobExecutionContext(
            job,
            host,
            scopeFactory,
            hubContext,
            new MockCommandExecutor(),
            new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance),
            NullLogger.Instance
        );

        var step = new ProxmoxSnapshotStep(mockClient);
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Skipped", result.Message);
        Assert.Null(job.SnapshotIdentifier);
        Assert.Empty(mockClient.CreatedSnapshots);
    }
}
