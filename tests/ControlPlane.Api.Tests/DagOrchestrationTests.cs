using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
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

public class DagOrchestrationTests
{
    private class DagTestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-dag-{Guid.NewGuid():N}.db");

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

    [Fact]
    public async Task PreflightHeartbeat_Fails_When_AgentOffline()
    {
        using var factory = new DagTestAppFactory();
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
            Hostname = "offline-node",
            IpAddress = "192.168.1.99",
            OsFamily = "linux_debian",
            TargetType = "baremetal",
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

        var context = new JobExecutionContext(
            job,
            host,
            scopeFactory,
            hubContext,
            new MockCommandExecutor(),
            connMgr,
            NullLogger.Instance
        );

        var step = new PreflightHeartbeatCheckStep();
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("offline", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreflightHeartbeat_Fails_When_HeartbeatStale()
    {
        using var factory = new DagTestAppFactory();
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
            Hostname = "stale-node",
            IpAddress = "192.168.1.11",
            OsFamily = "linux_debian",
            TargetType = "baremetal",
            Agent = new AgentState
            {
                Installed = true,
                LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-5) // Stale > 15s
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

        // Register active websocket connection
        var wsClient = factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            new Uri(factory.Server.BaseAddress, $"/agent-hub?token=dev-secret-key-123&hostId={hostId}"),
            CancellationToken.None
        );
        for (var i = 0; i < 100 && !connMgr.IsOnline(hostId); i++)
        {
            await Task.Delay(10);
        }

        var context = new JobExecutionContext(
            job,
            host,
            scopeFactory,
            hubContext,
            new MockCommandExecutor(),
            connMgr,
            NullLogger.Instance
        );

        var step = new PreflightHeartbeatCheckStep(TimeSpan.FromSeconds(15));
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("stale", result.Message, StringComparison.OrdinalIgnoreCase);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task PreflightDiskHeadroom_Fails_When_FreeSpaceLessThan20Pct()
    {
        using var factory = new DagTestAppFactory();
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
            Hostname = "low-disk-node",
            IpAddress = "192.168.1.12",
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

        // Simulate cached metrics with only 12% free disk space
        connMgr.Register(hostId, "low-disk-node", new ClientWebSocket());
        connMgr.UpdateMetrics(hostId, new AgentMetrics
        {
            CpuUsagePct = 10,
            MemoryUsagePct = 40,
            DiskFreePct = 12.0
        });

        var context = new JobExecutionContext(
            job,
            host,
            scopeFactory,
            hubContext,
            new MockCommandExecutor(),
            connMgr,
            NullLogger.Instance
        );

        var step = new PreflightDiskHeadroomCheckStep(minFreePct: 20.0);
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Insufficient root filesystem headroom", result.Message);
        Assert.Contains("12.0%", result.Message);
    }

    [Fact]
    public async Task PreflightPackageLock_Fails_When_LockDetected()
    {
        using var factory = new DagTestAppFactory();
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
            Hostname = "locked-node",
            IpAddress = "192.168.1.13",
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

        var mockExecutor = new MockCommandExecutor
        {
            OnExecute = (hId, jId, cmd, args) => new AgentCommandResult(false, 1, "Locked: /var/lib/dpkg/lock-frontend")
        };

        var context = new JobExecutionContext(
            job,
            host,
            scopeFactory,
            hubContext,
            mockExecutor,
            connMgr,
            NullLogger.Instance
        );

        var step = new PreflightPackageLockCheckStep();
        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("lock detected", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DagExecutionPipeline_Executes_AllSteps_And_MarksJobCompleted()
    {
        using var factory = new DagTestAppFactory();
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
            Hostname = "healthy-upgrade-node",
            IpAddress = "192.168.1.14",
            OsFamily = "linux_debian",
            TargetType = "baremetal",
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

        // Register online session
        var wsClient = factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            new Uri(factory.Server.BaseAddress, $"/agent-hub?token=dev-secret-key-123&hostId={hostId}"),
            CancellationToken.None
        );
        for (var i = 0; i < 100 && !connMgr.IsOnline(hostId); i++)
        {
            await Task.Delay(10);
        }

        connMgr.UpdateMetrics(hostId, new AgentMetrics { DiskFreePct = 45.0 });

        var mockExecutor = new MockCommandExecutor
        {
            OnExecute = (hId, jId, cmd, args) => new AgentCommandResult(true, 0, null)
        };

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
            new PackageUpgradeStep()
        });

        var success = await pipeline.ExecuteAsync(context, CancellationToken.None);
        Assert.True(success);

        // Verify job state in database
        var updatedJob = await db.UpdateJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        Assert.NotNull(updatedJob);
        Assert.Equal(UpdateJobState.Completed, updatedJob.Status);
        Assert.Null(updatedJob.ActiveStep);
        Assert.NotNull(updatedJob.CompletedAt);

        // Verify logs persisted
        var logs = await db.StepLogs.Where(l => l.JobId == jobId).ToListAsync();
        Assert.NotEmpty(logs);
        Assert.Contains(logs, l => l.LogLine.Contains("Preflight: Heartbeat Freshness"));
        Assert.Contains(logs, l => l.LogLine.Contains("Package upgrade completed successfully"));

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task DagExecutionPipeline_RollsBack_When_StepFails()
    {
        using var factory = new DagTestAppFactory();
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
            Hostname = "rollback-node",
            IpAddress = "192.168.1.15",
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

        var rolledBack = false;
        var step1 = new MockStep("Step 1", () => JobStepResult.Succeeded("Step 1 done"), () => { rolledBack = true; return Task.CompletedTask; });
        var step2 = new MockStep("Step 2", () => JobStepResult.Failed("Step 2 failed on purpose"), () => Task.CompletedTask);

        var context = new JobExecutionContext(
            job,
            host,
            scopeFactory,
            hubContext,
            new MockCommandExecutor(),
            connMgr,
            NullLogger.Instance
        );

        var pipeline = new DagExecutionPipeline(new IJobStep[] { step1, step2 });
        var success = await pipeline.ExecuteAsync(context, CancellationToken.None);

        Assert.False(success);
        Assert.True(rolledBack, "Step 1 should have been rolled back after Step 2 failed");

        var updatedJob = await db.UpdateJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        Assert.NotNull(updatedJob);
        Assert.Equal(UpdateJobState.Failed, updatedJob.Status);
        Assert.Equal("Step 2 failed on purpose", updatedJob.FailureReason);
    }

    [Fact]
    public async Task JobEndpoints_CreateJob_And_GetJobById()
    {
        using var factory = new DagTestAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");

        var hostId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            db.Hosts.Add(new HostEntity
            {
                Id = hostId,
                Hostname = "api-job-node",
                IpAddress = "192.168.1.16",
                OsFamily = "linux_debian",
                TargetType = "baremetal"
            });
            await db.SaveChangesAsync();
        }

        // 1. Create Job via POST /api/v1/jobs
        var postResponse = await client.PostAsJsonAsync("/api/v1/jobs", new CreateJobRequest(hostId));
        Assert.Equal(HttpStatusCode.Accepted, postResponse.StatusCode);

        var jobSummary = await postResponse.Content.ReadFromJsonAsync<JobSummaryDto>();
        Assert.NotNull(jobSummary);
        Assert.Equal(hostId, jobSummary.TargetHostId);
        Assert.Equal(UpdateJobState.Pending, jobSummary.Status);

        // 2. Query Job via GET /api/v1/jobs/{id}
        var getResponse = await client.GetAsync($"/api/v1/jobs/{jobSummary.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var fetchedJob = await getResponse.Content.ReadFromJsonAsync<JobSummaryDto>();
        Assert.NotNull(fetchedJob);
        Assert.Equal(jobSummary.Id, fetchedJob.Id);

        // 3. Query Jobs list via GET /api/v1/jobs?hostId={hostId}
        var listResponse = await client.GetFromJsonAsync<List<JobSummaryDto>>($"/api/v1/jobs?hostId={hostId}");
        Assert.NotNull(listResponse);
        Assert.Contains(listResponse, j => j.Id == jobSummary.Id);
    }

    private class MockStep : IJobStep
    {
        private readonly Func<JobStepResult> _exec;
        private readonly Func<Task> _rollback;

        public string StepName { get; }

        public MockStep(string name, Func<JobStepResult> exec, Func<Task> rollback)
        {
            StepName = name;
            _exec = exec;
            _rollback = rollback;
        }

        public Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct) =>
            Task.FromResult(_exec());

        public Task RollbackAsync(JobExecutionContext context, CancellationToken ct) =>
            _rollback();
    }
}
