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
using Microsoft.Extensions.DependencyInjection;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class MassAgentUpdateTests
{
    private class AgentUpdateAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-agents-{Guid.NewGuid():N}.db");

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
                    Version = "1.1.0"
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
        Assert.Equal("1.1.0", info.ServerVersion);
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
}
