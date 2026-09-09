using System.Net.Http.Json;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Jobs;
using ControlPlane.Api.Features.Orchestration;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class JobLogStreamingTests
{
    private class JobTestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-joblogs-{Guid.NewGuid():N}.db");

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

    [Fact]
    public async Task JobLogStreaming_FramesPersisted_And_BroadcastViaSignalR()
    {
        using var factory = new JobTestAppFactory();

        // 1. Seed host and job
        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            db.Hosts.Add(new HostEntity
            {
                Id = hostId,
                Hostname = "stream-test-node",
                IpAddress = "192.168.1.199",
                OsFamily = "linux_debian",
                TargetType = "baremetal"
            });
            db.UpdateJobs.Add(new UpdateJob
            {
                Id = jobId,
                TargetHostId = hostId,
                PipelineId = "adhoc-command",
                InitiatedBy = "Operator",
                Status = "Running",
                StartedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // 2. Connect SignalR client to /hubs/jobs
        var receivedLines = new List<string>();
        var logReceivedTcs = new TaskCompletionSource<bool>();

        var hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/jobs"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();

        hubConnection.On<Guid, long, string, string, DateTimeOffset>("ReceiveLogLine", (jId, seq, stream, line, ts) =>
        {
            if (jId == jobId)
            {
                receivedLines.Add(line);
                if (receivedLines.Count >= 2)
                {
                    logReceivedTcs.TrySetResult(true);
                }
            }
        });

        await hubConnection.StartAsync();
        await hubConnection.InvokeAsync("JoinJobGroup", jobId);

        // 3. Emit frames through StepLogStreamConsumer
        var consumer = factory.Services.GetRequiredService<IStepLogConsumer>();

        await consumer.ConsumeFrameAsync(hostId, new AgentFrameData
        {
            JobId = jobId,
            SequenceId = 1,
            StreamType = "stdout",
            LogLine = "Reading package lists...",
            Timestamp = DateTimeOffset.UtcNow
        });

        await consumer.ConsumeFrameAsync(hostId, new AgentFrameData
        {
            JobId = jobId,
            SequenceId = 2,
            StreamType = "stdout",
            LogLine = "Building dependency tree...",
            Timestamp = DateTimeOffset.UtcNow
        });

        await consumer.ConsumeFrameAsync(hostId, new AgentFrameData
        {
            JobId = jobId,
            SequenceId = 3,
            StreamType = "system",
            LogLine = "Process completed successfully (exit code 0)",
            Timestamp = DateTimeOffset.UtcNow
        });

        // 4. Await SignalR event receipt
        var completed = await Task.WhenAny(logReceivedTcs.Task, Task.Delay(5000));
        Assert.Equal(logReceivedTcs.Task, completed);
        Assert.Contains("Reading package lists...", receivedLines);
        Assert.Contains("Building dependency tree...", receivedLines);

        // 5. Query REST historical log replay endpoint
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");

        var logsResponse = await client.GetFromJsonAsync<List<StepLogDto>>($"/api/v1/jobs/{jobId}/logs?fromSequenceId=0");
        Assert.NotNull(logsResponse);
        Assert.Equal(3, logsResponse.Count);
        Assert.Equal(1, logsResponse[0].SequenceId);
        Assert.Equal(2, logsResponse[1].SequenceId);
        Assert.Equal(3, logsResponse[2].SequenceId);
        Assert.Equal("Reading package lists...", logsResponse[0].LogLine);

        // 6. Verify Job status updated to Completed
        var jobDetails = await client.GetFromJsonAsync<JobDetailsDto>($"/api/v1/jobs/{jobId}");
        Assert.NotNull(jobDetails);
        Assert.Equal("Completed", jobDetails.Status);

        await hubConnection.StopAsync();
    }

    [Fact]
    public async Task CancelJob_CancelsRunningJob_AndReturnsSuccess()
    {
        using var factory = new JobTestAppFactory();

        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            db.Hosts.Add(new HostEntity
            {
                Id = hostId,
                Hostname = "cancel-test-host",
                IpAddress = "192.168.1.199",
                OsFamily = "linux_debian",
                TargetType = "baremetal"
            });
            db.UpdateJobs.Add(new UpdateJob
            {
                Id = jobId,
                TargetHostId = hostId,
                PipelineId = "standard-os-upgrade",
                Status = "Running",
                ActiveStep = "Package Upgrade Execution",
                InitiatedBy = "Operator",
                StartedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");

        var response = await client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/cancel", new
        {
            Reason = "Stopped by operator"
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        // Verify status in DB changed to Cancelled
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var job = await db.UpdateJobs.FindAsync(jobId);
            Assert.NotNull(job);
            Assert.Equal(UpdateJobState.Cancelled, job.Status);
            Assert.Equal("Stopped by operator", job.FailureReason);
            Assert.NotNull(job.CompletedAt);
        }
    }

    [Fact]
    public async Task DeleteJob_DeletesFinishedJob_AndAssociatedLogs()
    {
        using var factory = new JobTestAppFactory();

        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            db.Hosts.Add(new HostEntity
            {
                Id = hostId,
                Hostname = "delete-test-host",
                IpAddress = "192.168.1.198",
                OsFamily = "linux_debian",
                TargetType = "baremetal"
            });
            db.UpdateJobs.Add(new UpdateJob
            {
                Id = jobId,
                TargetHostId = hostId,
                PipelineId = "standard-os-upgrade",
                Status = "Completed",
                InitiatedBy = "Operator",
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            });
            db.StepLogs.Add(new StepLog
            {
                JobId = jobId,
                SequenceId = 1,
                StreamType = "stdout",
                LogLine = "Log to delete",
                Timestamp = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");

        var response = await client.DeleteAsync($"/api/v1/jobs/{jobId}");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var job = await db.UpdateJobs.FindAsync(jobId);
            Assert.Null(job);
            var logs = await db.StepLogs.Where(l => l.JobId == jobId).ToListAsync();
            Assert.Empty(logs);
        }
    }

    [Fact]
    public async Task PurgeJobs_DeletesAllCompletedAndFailedJobs()
    {
        using var factory = new JobTestAppFactory();

        var hostId = Guid.NewGuid();
        var completedJobId = Guid.NewGuid();
        var failedJobId = Guid.NewGuid();
        var runningJobId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            db.Hosts.Add(new HostEntity
            {
                Id = hostId,
                Hostname = "purge-test-host",
                IpAddress = "192.168.1.197",
                OsFamily = "linux_debian",
                TargetType = "baremetal"
            });
            db.UpdateJobs.AddRange(
                new UpdateJob
                {
                    Id = completedJobId,
                    TargetHostId = hostId,
                    PipelineId = "p1",
                    Status = "Completed",
                    InitiatedBy = "Op",
                    StartedAt = DateTimeOffset.UtcNow
                },
                new UpdateJob
                {
                    Id = failedJobId,
                    TargetHostId = hostId,
                    PipelineId = "p2",
                    Status = "Failed",
                    InitiatedBy = "Op",
                    StartedAt = DateTimeOffset.UtcNow
                },
                new UpdateJob
                {
                    Id = runningJobId,
                    TargetHostId = hostId,
                    PipelineId = "p3",
                    Status = "Running",
                    InitiatedBy = "Op",
                    StartedAt = DateTimeOffset.UtcNow
                }
            );
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");

        var response = await client.PostAsync("/api/v1/jobs/purge", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            Assert.Null(await db.UpdateJobs.FindAsync(completedJobId));
            Assert.Null(await db.UpdateJobs.FindAsync(failedJobId));
            // Running job must NOT be purged
            Assert.NotNull(await db.UpdateJobs.FindAsync(runningJobId));
        }
    }
}
