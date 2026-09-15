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

    [Fact]
    public async Task AgentHub_Reconnection_DoesNotUnregisterNewSocket()
    {
        using var factory = new AgentTestAppFactory();

        var hostId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            db.Hosts.Add(new HostEntity
            {
                Id = hostId,
                Hostname = "race-test-node",
                IpAddress = "192.168.1.181",
                OsFamily = "linux_debian",
                TargetType = "baremetal"
            });
            await db.SaveChangesAsync();
        }

        var wsClient = factory.Server.CreateWebSocketClient();
        var uri = new Uri(factory.Server.BaseAddress, $"/agent-hub?token=dev-secret-key-123&nodeId={hostId}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Connect socket 1
        var socket1 = await wsClient.ConnectAsync(uri, cts.Token);
        Assert.Equal(WebSocketState.Open, socket1.State);

        var connManager = factory.Services.GetRequiredService<AgentConnectionManager>();
        Assert.True(connManager.IsOnline(hostId));

        // Connect socket 2 (reconnection)
        var wsClient2 = factory.Server.CreateWebSocketClient();
        var socket2 = await wsClient2.ConnectAsync(uri, cts.Token);
        Assert.Equal(WebSocketState.Open, socket2.State);
        for (int i = 0; i < 50 && !connManager.IsOnline(hostId); i++)
        {
            await Task.Delay(10);
        }
        Assert.True(connManager.IsOnline(hostId));

        // Close socket 1 (its finally block executes Unregister)
        await socket1.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed socket 1", cts.Token);

        // Host must STILL be online because socket 2 is active!
        var isOnlineAfterSocket1Close = false;
        string debugInfo = "";
        for (int i = 0; i < 30; i++)
        {
            var s = connManager.GetSession(hostId);
            debugInfo = $"Sess null={s == null}, socket State={s?.Socket.State}, clientSocket2 State={socket2.State}";
            if (connManager.IsOnline(hostId))
            {
                isOnlineAfterSocket1Close = true;
                break;
            }
            await Task.Delay(50);
        }
        Assert.True(isOnlineAfterSocket1Close);

        // Now close socket 2
        await socket2.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed socket 2", cts.Token);

        // Now host is offline
        var isOfflineAfterSocket2Close = false;
        for (int i = 0; i < 30; i++)
        {
            if (!connManager.IsOnline(hostId))
            {
                isOfflineAfterSocket2Close = true;
                break;
            }
            await Task.Delay(50);
        }
        Assert.True(isOfflineAfterSocket2Close);
    }

    [Fact]
    public async Task AgentHub_UnrecognizedNodeId_DoesNotHijackFirstHost()
    {
        using var factory = new AgentTestAppFactory();

        var firstHostId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            db.Hosts.Add(new HostEntity
            {
                Id = firstHostId,
                Hostname = "legitimate-first-host",
                IpAddress = "192.168.1.182",
                OsFamily = "linux_debian",
                TargetType = "baremetal"
            });
            await db.SaveChangesAsync();
        }

        var wsClient = factory.Server.CreateWebSocketClient();
        var unknownNodeId = Guid.NewGuid();
        var uri = new Uri(factory.Server.BaseAddress, $"/agent-hub?token=dev-secret-key-123&nodeId={unknownNodeId}");

        // Attempting to connect with unrecognized node ID must fail / be rejected
        var ex = await Record.ExceptionAsync(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await wsClient.ConnectAsync(uri, cts.Token);
        });

        Assert.NotNull(ex);

        // Ensure firstHost was NOT registered/hijacked
        var connManager = factory.Services.GetRequiredService<AgentConnectionManager>();
        Assert.False(connManager.IsOnline(firstHostId));
    }
}
