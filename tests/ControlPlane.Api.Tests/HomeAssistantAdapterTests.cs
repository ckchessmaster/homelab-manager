using System.Net;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.HomeAssistant;
using ControlPlane.Api.Features.Security;
using ControlPlane.Api.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Tests;

public class HomeAssistantAdapterTests
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
    public async Task AdapterConfigService_SaveAndRetrieveHomeAssistantInstances_EncryptsTokenAndMasks()
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
        var inst1 = await service.SaveHomeAssistantInstanceAsync(new SaveHomeAssistantInstanceRequest(
            Id: "ha-main",
            Name: "Home Assistant VM",
            BaseUrl: "http://192.168.1.50:8123",
            Token: "test-token-secret-xyz",
            AllowSelfSignedCert: true
        ));

        Assert.Equal("ha-main", inst1.Id);
        Assert.Equal("Home Assistant VM", inst1.Name);
        Assert.Equal("••••••••", inst1.TokenMasked);
        Assert.True(inst1.HasToken);

        // Verify encrypted in database
        var raw1 = await service.GetRawHomeAssistantInstanceAsync("ha-main");
        Assert.NotNull(raw1);
        Assert.NotEqual("test-token-secret-xyz", raw1.EncryptedToken);
        Assert.Equal("test-token-secret-xyz", enc.Decrypt(raw1.EncryptedToken));

        // 2. Save instance 2 (generated ID)
        var inst2 = await service.SaveHomeAssistantInstanceAsync(new SaveHomeAssistantInstanceRequest(
            Id: null,
            Name: "Home Assistant Lab",
            BaseUrl: "https://192.168.1.51:8123",
            Token: "lab-token-12345",
            AllowSelfSignedCert: false
        ));

        Assert.NotNull(inst2.Id);
        Assert.Contains("home-assistant-lab", inst2.Id);

        // 3. List instances
        var list = await service.GetHomeAssistantInstancesAsync();
        Assert.Equal(2, list.Count);
        Assert.All(list, i => Assert.Equal("••••••••", i.TokenMasked));

        // 4. Update instance 1 with masked placeholder (preserves encrypted token)
        var updated = await service.SaveHomeAssistantInstanceAsync(new SaveHomeAssistantInstanceRequest(
            Id: "ha-main",
            Name: "Home Assistant Production",
            BaseUrl: "http://192.168.1.50:8123",
            Token: "••••••••",
            AllowSelfSignedCert: true
        ));

        Assert.Equal("Home Assistant Production", updated.Name);
        var rawUpdated = await service.GetRawHomeAssistantInstanceAsync("ha-main");
        Assert.NotNull(rawUpdated);
        Assert.Equal("test-token-secret-xyz", enc.Decrypt(rawUpdated.EncryptedToken));

        // 5. Delete instance 2
        var deleted = await service.DeleteHomeAssistantInstanceAsync(inst2.Id);
        Assert.True(deleted);

        var listAfter = await service.GetHomeAssistantInstancesAsync();
        Assert.Single(listAfter);
    }

    [Fact]
    public async Task HomeAssistantClient_TestConnectionAsync_ParsesVitalsCorrectly()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/api/hassio/host/info"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        result = "ok",
                        data = new
                        {
                            chassis = "vm",
                            hostname = "homeassistant",
                            kernel = "6.6.31-haos",
                            operating_system = "Home Assistant OS 12.4",
                            reboot_required = false,
                            disk_free = 25.4,
                            disk_total = 32.0,
                            disk_used = 6.6
                        }
                    }))
                };
            }
            if (path.Contains("/api/hassio/core/info"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        result = "ok",
                        data = new
                        {
                            version = "2024.9.1",
                            version_latest = "2024.9.2",
                            update_available = true,
                            arch = "amd64",
                            state = "RUNNING"
                        }
                    }))
                };
            }
            if (path.Contains("/api/hassio/supervisor/info"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        result = "ok",
                        data = new
                        {
                            version = "2024.09.0",
                            channel = "stable",
                            healthy = true,
                            supported = true
                        }
                    }))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var factory = new MockHttpClientFactory(httpClient);
        var client = new HomeAssistantClient(factory, NullLogger<HomeAssistantClient>.Instance);

        var testRes = await client.TestConnectionAsync("http://ha.local:8123", "dummy-token", true);

        Assert.True(testRes.Success);
        Assert.Equal("2024.9.1", testRes.CoreVersion);
        Assert.Equal("Home Assistant OS 12.4", testRes.OsVersion);
        Assert.Equal("2024.09.0", testRes.SupervisorVersion);
        Assert.Equal("homeassistant", testRes.Hostname);
        Assert.True(testRes.UpdateAvailable);
    }

    [Fact]
    public async Task HomeAssistantClient_CheckCoreConfigAsync_ValidatesConfig()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath.Contains("/api/hassio/core/check") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        result = "ok",
                        data = new
                        {
                            result = "valid",
                            errors = (string?)null
                        }
                    }))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var factory = new MockHttpClientFactory(httpClient);
        var client = new HomeAssistantClient(factory, NullLogger<HomeAssistantClient>.Instance);

        var checkRes = await client.CheckCoreConfigAsync("http://ha.local:8123", "dummy-token", true);

        Assert.True(checkRes.IsValid);
        Assert.Null(checkRes.Errors);
    }

    [Fact]
    public async Task HomeAssistantClient_CreateBackupAsync_And_ListBackupsAsync_Succeeds()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/api/hassio/backups/new/full"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        result = "ok",
                        data = new
                        {
                            job_id = "job-12345",
                            slug = "backup-c1f3b8"
                        }
                    }))
                };
            }
            if (path.Contains("/api/hassio/backups"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        result = "ok",
                        data = new
                        {
                            backups = new[]
                            {
                                new
                                {
                                    slug = "backup-c1f3b8",
                                    name = "Full Backup 2024-09-17",
                                    date = "2024-09-17T12:00:00Z",
                                    type = "full",
                                    size = 120.5,
                                    @protected = false
                                }
                            }
                        }
                    }))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var factory = new MockHttpClientFactory(httpClient);
        var client = new HomeAssistantClient(factory, NullLogger<HomeAssistantClient>.Instance);

        // Create backup
        var createRes = await client.CreateBackupAsync("http://ha.local:8123", "dummy-token", "Full Backup", null, true);
        Assert.True(createRes.Success);
        Assert.Equal("job-12345", createRes.JobId);
        Assert.Equal("backup-c1f3b8", createRes.Slug);

        // List backups
        var backups = await client.ListBackupsAsync("http://ha.local:8123", "dummy-token", true);
        Assert.Single(backups);
        Assert.Equal("Full Backup 2024-09-17", backups[0].Name);
        Assert.Equal(120.5, backups[0].SizeMb);
    }

    [Fact]
    public async Task HomeAssistantClient_RebootHostAndRestartCore_ReturnsTrue()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/api/hassio/host/reboot") || path.Contains("/api/hassio/core/restart") || path.Contains("/api/hassio/os/update"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { result = "ok" }))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var factory = new MockHttpClientFactory(httpClient);
        var client = new HomeAssistantClient(factory, NullLogger<HomeAssistantClient>.Instance);

        var rebootRes = await client.RebootHostAsync("http://ha.local:8123", "dummy-token", true);
        Assert.True(rebootRes);

        var restartRes = await client.RestartCoreAsync("http://ha.local:8123", "dummy-token", true);
        Assert.True(restartRes);

        var updateRes = await client.UpdateOsAsync("http://ha.local:8123", "dummy-token", true);
        Assert.True(updateRes);
    }

    [Fact]
    public async Task HomeAssistantClientFactory_ResolveAsync_DecryptsTokenProperly()
    {
        var (db, conn) = CreateTestDbContext();
        using var _ = conn;
        var enc = CreateEncryptionService();
        var configService = new AdapterConfigService(
            db,
            Options.Create(new Features.Adapters.Proxmox.ProxmoxOptions()),
            enc,
            NullLogger<AdapterConfigService>.Instance);

        await configService.SaveHomeAssistantInstanceAsync(new SaveHomeAssistantInstanceRequest(
            Id: "ha-test",
            Name: "Home Assistant Test",
            BaseUrl: "http://192.168.1.60:8123",
            Token: "my-secret-access-token-12345"
        ));

        var client = new HomeAssistantClient(new MockHttpClientFactory(new HttpClient()), NullLogger<HomeAssistantClient>.Instance);
        var factory = new HomeAssistantClientFactory(configService, enc, client, NullLogger<HomeAssistantClientFactory>.Instance);

        var (resolvedClient, config, token) = await factory.ResolveAsync("ha-test");

        Assert.NotNull(resolvedClient);
        Assert.Equal("Home Assistant Test", config.Name);
        Assert.Equal("my-secret-access-token-12345", token);
    }
}
