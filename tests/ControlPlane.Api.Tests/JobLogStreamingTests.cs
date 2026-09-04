using System.Net.Http.Json;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Jobs;
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
}
