using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Storage;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class AgentHubTests
{
    private class AgentTestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-agenthub-{Guid.NewGuid():N}.db");

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
    public async Task AgentHub_Rejects_UnauthorizedConnection()
    {
        using var factory = new AgentTestAppFactory();
        var wsClient = factory.Server.CreateWebSocketClient();

        var uri = new Uri(factory.Server.BaseAddress, "/agent-hub?token=invalid-secret-key");

        var ex = await Record.ExceptionAsync(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await wsClient.ConnectAsync(uri, cts.Token);
        });

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task AgentHub_Connects_And_IngestsHeartbeat()
    {
        using var factory = new AgentTestAppFactory();

        // 1. Seed a test host
        var hostId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            db.Hosts.Add(new HostEntity
            {
                Id = hostId,
                Hostname = "agent-node-01",
                IpAddress = "192.168.1.180",
                OsFamily = "linux_debian",
                TargetType = "baremetal"
            });
            await db.SaveChangesAsync();
        }

        // 2. Connect via WebSocket
        var wsClient = factory.Server.CreateWebSocketClient();
        var uri = new Uri(factory.Server.BaseAddress, $"/agent-hub?token=dev-secret-key-123&nodeId={hostId}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var webSocket = await wsClient.ConnectAsync(uri, cts.Token);
        Assert.Equal(WebSocketState.Open, webSocket.State);

        var connManager = factory.Services.GetRequiredService<AgentConnectionManager>();
        Assert.True(connManager.IsOnline(hostId));

        // 3. Send Heartbeat Message
        var heartbeat = new AgentHeartbeatMessage
        {
            Type = "HEARTBEAT",
            NodeId = hostId.ToString(),
            Hostname = "agent-node-01",
            AgentVersion = "1.0.0",
            KernelVersion = "6.8.0-generic",
            PendingReboot = true,
            PackageManager = "apt",
            Metrics = new AgentMetrics
            {
                CpuUsagePct = 12.5,
                MemoryUsagePct = 48.0,
                DiskFreePct = 75.2
            },
            PackageSummary = new AgentPackageSummary
            {
                PackageManager = "apt",
                UpgradableCount = 5,
                SecurityCount = 2
            }
        };

        var json = JsonSerializer.Serialize(heartbeat, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var bytes = Encoding.UTF8.GetBytes(json);
        await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);

        // Allow handler to process
        await Task.Delay(200);

        // 4. Verify DB was updated
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var updatedHost = await db.Hosts.FindAsync(hostId);
            Assert.NotNull(updatedHost);
            Assert.True(updatedHost.Agent.Installed);
            Assert.Equal("1.0.0", updatedHost.Agent.Version);
            Assert.True(updatedHost.Agent.PendingReboot);
            Assert.Equal(5, updatedHost.Agent.UpgradablePackagesCount);
            Assert.NotNull(updatedHost.Agent.LastSeenAt);
        }

        // 5. Close socket and verify session unregistered
        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cts.Token);
        await Task.Delay(100);
        Assert.False(connManager.IsOnline(hostId));
    }
}
