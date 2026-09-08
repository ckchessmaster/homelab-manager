using System.Net;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.OPNsense;
using ControlPlane.Api.Features.Security;
using ControlPlane.Api.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Tests;

public class OPNsenseMultiInstanceTests
{
    private static (ControlPlaneDbContext Db, SqliteConnection Conn) CreateTestDbContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseSqlite(conn)
            .UseSnakeCaseNamingConvention()
            .Options;
        var db = new ControlPlaneDbContext(options);
        db.Database.EnsureCreated();
        return (db, conn);
    }

    private static ISecretEncryptionService CreateEncryptionService()
    {
        var key = System.Text.Encoding.UTF8.GetBytes("12345678901234567890123456789012");
        var mockKeyProvider = new TestKeyProvider(key);
        return new SecretEncryptionService(mockKeyProvider);
    }

    private class TestKeyProvider : ISecurityKeyProvider
    {
        private readonly byte[] _key;
        public TestKeyProvider(byte[] key) => _key = key;
        public byte[] GetMasterKey() => (byte[])_key.Clone();
        public string KeySource => "Test";
        public string? KeyFilePath => null;
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
        public MockHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    [Fact]
    public async Task AdapterConfigService_SaveAndRetrieveMultipleOPNsenseInstances_EncryptsSecrets()
    {
        var (db, conn) = CreateTestDbContext();
        using var _ = conn;
        var enc = CreateEncryptionService();
        var service = new AdapterConfigService(
            db,
            Options.Create(new Features.Adapters.Proxmox.ProxmoxOptions()),
            enc,
            NullLogger<AdapterConfigService>.Instance);

        // 1. Save instance 1
        var inst1 = await service.SaveOPNsenseInstanceAsync(new SaveOPNsenseInstanceRequest(
            Id: "opnsense-core",
            Name: "Core Perimeter Firewall",
            BaseUrl: "https://192.168.1.1:8443",
            ApiKey: "my-api-key-1",
            ApiSecret: "my-api-secret-1",
            AllowSelfSignedCert: true
        ));

        Assert.Equal("opnsense-core", inst1.Id);
        Assert.Equal("Core Perimeter Firewall", inst1.Name);
        Assert.Equal(AdapterConfigService.MaskedPlaceholder, inst1.ApiSecretMasked);
        Assert.True(inst1.HasSecret);

        // Verify stored in DB encrypted
        var raw = await service.GetRawOPNsenseInstanceAsync("opnsense-core");
        Assert.NotNull(raw);
        Assert.NotEqual("my-api-secret-1", raw.EncryptedApiSecret);
        Assert.Equal("my-api-secret-1", enc.Decrypt(raw.EncryptedApiSecret));

        // 2. Save instance 2 (auto-generated ID)
        var inst2 = await service.SaveOPNsenseInstanceAsync(new SaveOPNsenseInstanceRequest(
            Id: null,
            Name: "Lab Edge Firewall",
            BaseUrl: "https://10.0.0.1",
            ApiKey: "key-2",
            ApiSecret: "secret-2"
        ));

        Assert.StartsWith("lab-edge", inst2.Id);

        // 3. List all
        var all = await service.GetOPNsenseInstancesAsync();
        Assert.Equal(2, all.Count);

        // 4. Delete instance 1
        var deleted = await service.DeleteOPNsenseInstanceAsync("opnsense-core");
        Assert.True(deleted);

        var remaining = await service.GetOPNsenseInstancesAsync();
        Assert.Single(remaining);
        Assert.Equal(inst2.Id, remaining[0].Id);
    }

    [Fact]
    public async Task OPNsenseClient_TestConnectionAsync_ParsesHostnameAndVersion()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var auth = req.Headers.Authorization;
            Assert.NotNull(auth);
            Assert.Equal("Basic", auth.Scheme);

            var json = JsonSerializer.Serialize(new
            {
                hostname = "opnsense.homelab.local",
                product_version = "24.7.1",
                status = "Online"
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var factory = new MockHttpClientFactory(new HttpClient(handler));
        var client = new OPNsenseClient(factory, NullLogger<OPNsenseClient>.Instance);

        var result = await client.TestConnectionAsync("https://192.168.1.1", "key", "secret");

        Assert.True(result.Success);
        Assert.Equal("opnsense.homelab.local", result.Hostname);
        Assert.Equal("24.7.1", result.Version);
        Assert.Equal("Online", result.Status);
    }

    [Fact]
    public async Task OPNsenseClient_GetGatewaysAsync_ParsesGatewaysCorrectly()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Contains("/api/routes/gateway/status", req.RequestUri!.ToString());

            var json = JsonSerializer.Serialize(new
            {
                items = new[]
                {
                    new
                    {
                        name = "WAN_DHCP",
                        @interface = "igb0",
                        status = "online",
                        delay = 14.2,
                        loss = 0.0,
                        address = "100.64.0.1"
                    },
                    new
                    {
                        name = "VPN_GW",
                        @interface = "wg0",
                        status = "online",
                        delay = 28.5,
                        loss = 0.5,
                        address = "10.10.0.1"
                    }
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var factory = new MockHttpClientFactory(new HttpClient(handler));
        var client = new OPNsenseClient(factory, NullLogger<OPNsenseClient>.Instance);

        var gateways = await client.GetGatewaysAsync("https://192.168.1.1", "key", "secret");

        Assert.Equal(2, gateways.Count);
        Assert.Equal("WAN_DHCP", gateways[0].Name);
        Assert.Equal("igb0", gateways[0].Interface);
        Assert.Equal(14.2, gateways[0].LatencyMs);
        Assert.Equal("100.64.0.1", gateways[0].Address);
    }

    [Fact]
    public async Task OPNsenseClient_GetServicesAndRestartAsync_ExecutesSuccessfully()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains("/api/core/service/search"))
            {
                var json = JsonSerializer.Serialize(new
                {
                    rows = new[]
                    {
                        new { id = "svc-1", name = "unbound", description = "Unbound DNS Resolver", running = 1, enabled = 1 },
                        new { id = "svc-2", name = "wireguard", description = "WireGuard VPN", running = 1, enabled = 1 },
                        new { id = "svc-3", name = "suricata", description = "Suricata Intrusion Detection", running = 0, enabled = 1 }
                    }
                });

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("/api/core/service/restart/unbound"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"result\":\"ok\"}", System.Text.Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factory = new MockHttpClientFactory(new HttpClient(handler));
        var client = new OPNsenseClient(factory, NullLogger<OPNsenseClient>.Instance);

        var services = await client.GetServicesAsync("https://192.168.1.1", "key", "secret");
        Assert.Equal(3, services.Count);
        Assert.True(services[0].Running);
        Assert.False(services[2].Running);

        var restartResult = await client.RestartServiceAsync("https://192.168.1.1", "key", "secret", "unbound");
        Assert.True(restartResult.Success);
        Assert.Contains("restarted successfully", restartResult.Message);
    }

    [Fact]
    public async Task OPNsenseClient_GetDhcpLeasesAsync_ParsesActiveLeases()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Contains("/api/diagnostics/dhcp/searchLeases", req.RequestUri!.ToString());

            var json = JsonSerializer.Serialize(new
            {
                rows = new[]
                {
                    new
                    {
                        ip = "192.168.1.50",
                        mac = "00:11:22:33:44:55",
                        hostname = "truenas-core",
                        starts = "2026/09/08 10:00:00",
                        ends = "2026/09/08 22:00:00",
                        status = "active"
                    },
                    new
                    {
                        ip = "192.168.1.51",
                        mac = "aa:bb:cc:dd:ee:ff",
                        hostname = "k8s-worker-01",
                        starts = "2026/09/08 10:05:00",
                        ends = "2026/09/08 22:05:00",
                        status = "active"
                    }
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var factory = new MockHttpClientFactory(new HttpClient(handler));
        var client = new OPNsenseClient(factory, NullLogger<OPNsenseClient>.Instance);

        var leases = await client.GetDhcpLeasesAsync("https://192.168.1.1", "key", "secret");

        Assert.Equal(2, leases.Count);
        Assert.Equal("192.168.1.50", leases[0].Ip);
        Assert.Equal("00:11:22:33:44:55", leases[0].Mac);
        Assert.Equal("truenas-core", leases[0].Hostname);
    }
}
