using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class HostRebootTests
{
    private class RebootTestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-reboot-{Guid.NewGuid():N}.db");

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

    private static HttpClient CreateAuthClient(RebootTestAppFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");
        return client;
    }

    [Fact]
    public async Task RebootHost_ReturnsNotFound_WhenHostDoesNotExist()
    {
        using var factory = new RebootTestAppFactory();
        var client = CreateAuthClient(factory);

        var randomId = Guid.NewGuid();
        var response = await client.PostAsync($"/api/v1/hosts/{randomId}/reboot", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RebootHost_ReturnsBadRequest_WhenAgentIsOffline()
    {
        using var factory = new RebootTestAppFactory();
        var client = CreateAuthClient(factory);

        var hostId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Hosts.Add(new HostEntity
            {
                Id = hostId,
                Hostname = "offline-server",
                IpAddress = "192.168.1.188",
                OsFamily = "Debian",
                TargetType = "BareMetal",
                Agent = new AgentState
                {
                    Installed = true,
                    PendingReboot = true,
                    LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-10)
                }
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/v1/hosts/{hostId}/reboot", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("offline", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RebootHost_Accepts_WhenAgentIsOnline()
    {
        using var factory = new RebootTestAppFactory();
        var client = CreateAuthClient(factory);

        var hostId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Hosts.Add(new HostEntity
            {
                Id = hostId,
                Hostname = "reboot-target",
                IpAddress = "192.168.1.189",
                OsFamily = "Debian",
                TargetType = "BareMetal",
                Agent = new AgentState
                {
                    Installed = true,
                    PendingReboot = true,
                    LastSeenAt = DateTimeOffset.UtcNow
                }
            });
            await db.SaveChangesAsync();
        }

        // Connect a mock agent over WebSocket
        var wsClient = factory.Server.CreateWebSocketClient();
        var wsUri = new Uri(factory.Server.BaseAddress, $"/agent-hub?token=dev-secret-key-123&hostId={hostId}");
        using var ws = await wsClient.ConnectAsync(wsUri, CancellationToken.None);

        var connMgr = factory.Services.GetRequiredService<AgentConnectionManager>();
        for (int i = 0; i < 50 && !connMgr.IsOnline(hostId); i++)
        {
            await Task.Delay(50);
        }

        var response = await client.PostAsync($"/api/v1/hosts/{hostId}/reboot", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var responseDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = responseDoc.RootElement;
        Assert.True(root.TryGetProperty("jobId", out var jobIdProp));
        var returnedJobId = jobIdProp.GetGuid();
        Assert.NotEqual(Guid.Empty, returnedJobId);

        // Verify update_jobs has record
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var job = await db.UpdateJobs.FindAsync(returnedJobId);
            Assert.NotNull(job);
            Assert.Equal("Running", job.Status);
            Assert.Equal("Rebooting node", job.ActiveStep);
            Assert.Equal(hostId, job.TargetHostId);
        }
    }
}
