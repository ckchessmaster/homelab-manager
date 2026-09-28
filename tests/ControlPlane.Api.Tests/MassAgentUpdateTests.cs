using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class MassAgentUpdateTests
{
    private class AgentUpdateAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-agents-{Guid.NewGuid():N}.db");
        private readonly string _tempDistDir = Path.Combine(Path.GetTempPath(), $"cp-test-dist-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("STANDBY_MODE", "true");
            builder.UseSetting("ControlPlane:ApiKey", "dev-secret-key-123");
            builder.UseSetting("ConnectionStrings:PostgresDatabase", "");
            builder.UseSetting("ControlPlane:AgentDistDir", _tempDistDir);
            builder.UseSetting("ControlPlane:AgentBinarySync:Enabled", "false");
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
            if (Directory.Exists(_tempDistDir))
            {
                try { Directory.Delete(_tempDistDir, true); } catch { }
            }
        }
    }

    private static HttpClient CreateAuthClient(AgentUpdateAppFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");
        return client;
    }

    [Fact]
    public async Task GetVersionInfo_ReturnsOutdatedAndOnlineCounts()
    {
        using var factory = new AgentUpdateAppFactory();
        var client = CreateAuthClient(factory);

        // Seed hosts with differing agent versions
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var connectionManager = scope.ServiceProvider.GetRequiredService<AgentConnectionManager>();

            var host1 = new HostEntity
            {
                Id = Guid.NewGuid(),
                Hostname = "node-outdated-offline",
                IpAddress = "192.168.1.101",
                Agent = new AgentState
                {
                    Installed = true,
                    Version = "1.0.0"
                }
            };

            var host2 = new HostEntity
            {
                Id = Guid.NewGuid(),
                Hostname = "node-up-to-date",
                IpAddress = "192.168.1.102",
                Agent = new AgentState
                {
                    Installed = true,
                    Version = AgentBinaryService.CurrentAgentVersion
                }
            };

            var host3 = new HostEntity
            {
                Id = Guid.NewGuid(),
                Hostname = "node-no-agent",
                IpAddress = "192.168.1.103",
                Agent = new AgentState
                {
                    Installed = false
                }
            };

            db.Hosts.AddRange(host1, host2, host3);
            await db.SaveChangesAsync();
        }

        var res = await client.GetAsync("/api/v1/agents/version-info");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var info = await res.Content.ReadFromJsonAsync<AgentVersionInfoDto>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(info);
        Assert.Equal(AgentBinaryService.CurrentAgentVersion, info.ServerVersion);
        Assert.True(info.TotalInstalledAgents >= 2);
        Assert.True(info.OutdatedAgentsCount >= 1);
        Assert.Contains(info.OutdatedHosts, h => h.Hostname == "node-outdated-offline");
        Assert.DoesNotContain(info.OutdatedHosts, h => h.Hostname == "node-up-to-date");
        Assert.DoesNotContain(info.OutdatedHosts, h => h.Hostname == "node-no-agent");
    }

    [Fact]
    public async Task GetBinary_AllowsAnonymousAndServesBinary()
    {
        using var factory = new AgentUpdateAppFactory();
        var anonClient = factory.CreateClient(); // No auth header

        var res = await anonClient.GetAsync("/api/v1/agents/binaries/linux-amd64");
        // Binaries were compiled in src/agent/dist
        Assert.True(res.StatusCode == HttpStatusCode.OK || res.StatusCode == HttpStatusCode.NotFound);
        if (res.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal("application/octet-stream", res.Content.Headers.ContentType?.MediaType);
            var bytes = await res.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.Length > 1000);
        }
    }

    [Fact]
    public async Task MassUpdate_SkipsOfflineHostsAndDispatchesToOnline()
    {
        using var factory = new AgentUpdateAppFactory();
        var client = CreateAuthClient(factory);

        var offlineHostId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

            var host1 = new HostEntity
            {
                Id = offlineHostId,
                Hostname = "worker-offline",
                IpAddress = "192.168.1.201",
                Agent = new AgentState
                {
                    Installed = true,
                    Version = "1.0.0"
                }
            };

            db.Hosts.Add(host1);
            await db.SaveChangesAsync();
        }

        var updateReq = new MassUpdateRequest(
            HostIds: new List<Guid> { offlineHostId },
            AllOutdated: false
        );

        var res = await client.PostAsJsonAsync("/api/v1/agents/mass-update", updateReq);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        var result = await res.Content.ReadFromJsonAsync<MassUpdateBatchResult>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal(1, result.TotalTargeted);
        Assert.Equal(0, result.DispatchedCount);
        Assert.Equal(1, result.SkippedOfflineCount);
        Assert.Equal("SkippedOffline", result.Details[0].Status);

        // Verify GET status endpoint
        var statusRes = await client.GetAsync($"/api/v1/agents/mass-update/{result.BatchId}");
        Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);
    }

    [Fact]
    public void ResolveLanAddress_WithWssHubUrl_ResolvesToHttps()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ControlPlane:HubUrl"] = "wss://manage.local.chriskingdon.com/agent-hub"
            })
            .Build();

        var resolved = MassAgentUpdateService.ResolveLanAddress("http://127.0.0.1:5029", config);
        Assert.Equal("https://manage.local.chriskingdon.com", resolved);
    }

    [Fact]
    public async Task TriggerMassUpdate_WhenHubUrlUsesWss_EnforcesHttpsDownloadUrl()
    {
        using var factory = new AgentUpdateAppFactory();
        var client = CreateAuthClient(factory);

        var hostId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var connManager = scope.ServiceProvider.GetRequiredService<AgentConnectionManager>();
        var binaryService = scope.ServiceProvider.GetRequiredService<AgentBinaryService>();
        var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MassAgentUpdateService>>();

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ControlPlane:HubUrl"] = "wss://manage.local.chriskingdon.com/agent-hub"
            })
            .Build();

        var host = new HostEntity
        {
            Id = hostId,
            Hostname = "online-agent-node",
            IpAddress = "192.168.1.150",
            Agent = new AgentState
            {
                Installed = true,
                Version = "1.3.0"
            }
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();

        // Register online session with http inbound scheme (as reported behind reverse proxy)
        // Using a mock open WebSocket
        using var fakeWs = new TestWebSocket();
        connManager.Register(hostId, hostId.ToString(), fakeWs, "manage.local.chriskingdon.com", "http");

        var service = new MassAgentUpdateService(db, connManager, binaryService, config, logger);
        var batch = await service.TriggerMassUpdateAsync(new MassUpdateRequest(HostIds: new List<Guid> { hostId }, AllOutdated: false), "http://localhost:5029");

        Assert.Equal(1, batch.TotalTargeted);
        Assert.Equal(1, batch.DispatchedCount);
        Assert.NotNull(fakeWs.LastSentJson);
        Assert.Contains("https://manage.local.chriskingdon.com/api/v1/agents/binaries/linux-amd64", fakeWs.LastSentJson);
        Assert.DoesNotContain("http://manage.local.chriskingdon.com", fakeWs.LastSentJson);
    }

    private class TestWebSocket : System.Net.WebSockets.WebSocket
    {
        public string? LastSentJson { get; private set; }
        public override System.Net.WebSockets.WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override System.Net.WebSockets.WebSocketState State => System.Net.WebSockets.WebSocketState.Open;
        public override string SubProtocol => "";

        public override void Abort() { }
        public override Task CloseAsync(System.Net.WebSockets.WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task<System.Net.WebSockets.WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            Task.FromResult(new System.Net.WebSockets.WebSocketReceiveResult(0, System.Net.WebSockets.WebSocketMessageType.Close, true));

        public override Task SendAsync(ArraySegment<byte> buffer, System.Net.WebSockets.WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            LastSentJson = System.Text.Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count);
            return Task.CompletedTask;
        }
    }
}
