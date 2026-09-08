using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.UniFi;
using ControlPlane.Api.Features.Security;
using ControlPlane.Api.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Tests;

public class UniFiMultiInstanceTests
{
    private static (ControlPlaneDbContext Db, Microsoft.Data.Sqlite.SqliteConnection Conn) CreateTestDbContext()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
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

    [Fact]
    public async Task AdapterConfigService_SaveAndRetrieveMultipleUniFiInstances_EncryptsSecrets()
    {
        var (db, conn) = CreateTestDbContext();
        using var _ = conn;
        var enc = CreateEncryptionService();
        var service = new AdapterConfigService(
            db,
            Options.Create(new Features.Adapters.Proxmox.ProxmoxOptions()),
            enc,
            NullLogger<AdapterConfigService>.Instance);

        // 1. Save instance 1 (legacy container)
        var inst1 = await service.SaveUniFiInstanceAsync(new SaveUniFiInstanceRequest(
            Id: "unifi-main",
            Name: "Main Network Container",
            ControllerUrl: "https://unifi.lan:8443",
            Username: "admin",
            Password: "SuperSecretPassword1!",
            AuthType: "credentials",
            Site: "default",
            AllowSelfSignedCert: true
        ));

        Assert.Equal("unifi-main", inst1.Id);
        Assert.Equal("Main Network Container", inst1.Name);
        Assert.Equal(AdapterConfigService.MaskedPlaceholder, inst1.PasswordMasked);
        Assert.True(inst1.HasPassword);
        Assert.Equal("credentials", inst1.AuthType);

        // 2. Save instance 2 (UniFi OS with API Key)
        var inst2 = await service.SaveUniFiInstanceAsync(new SaveUniFiInstanceRequest(
            Id: null, // auto-generated
            Name: "UniFi OS Server",
            ControllerUrl: "https://192.168.1.1",
            ApiKey: "unifi-os-api-key-9999",
            AuthType: "api_key",
            Site: "default",
            AllowSelfSignedCert: true
        ));

        Assert.StartsWith("unifi-os-server", inst2.Id);
        Assert.Equal("api_key", inst2.AuthType);
        Assert.True(inst2.HasApiKey);
        Assert.Equal(AdapterConfigService.MaskedPlaceholder, inst2.ApiKeyMasked);
        Assert.Equal("api-key", inst2.Username);

        // 3. Retrieve all instances
        var all = await service.GetUniFiInstancesAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, i => i.Id == "unifi-main");
        Assert.Contains(all, i => i.Id == inst2.Id);

        // 4. Verify password and API key are encrypted in database
        var raw1 = await service.GetRawUniFiInstanceAsync("unifi-main");
        Assert.NotNull(raw1);
        Assert.NotEqual("SuperSecretPassword1!", raw1.EncryptedPassword);
        Assert.Equal("SuperSecretPassword1!", enc.Decrypt(raw1.EncryptedPassword));

        var raw2 = await service.GetRawUniFiInstanceAsync(inst2.Id);
        Assert.NotNull(raw2);
        Assert.NotNull(raw2.EncryptedApiKey);
        Assert.NotEqual("unifi-os-api-key-9999", raw2.EncryptedApiKey);
        Assert.Equal("unifi-os-api-key-9999", enc.Decrypt(raw2.EncryptedApiKey));
    }

    [Fact]
    public async Task AdapterConfigService_DeleteUniFiInstance_RemovesTarget()
    {
        var (db, conn) = CreateTestDbContext();
        using var _ = conn;
        var enc = CreateEncryptionService();
        var service = new AdapterConfigService(
            db,
            Options.Create(new Features.Adapters.Proxmox.ProxmoxOptions()),
            enc,
            NullLogger<AdapterConfigService>.Instance);

        await service.SaveUniFiInstanceAsync(new SaveUniFiInstanceRequest(
            Id: "unifi-temp",
            Name: "Temp Controller",
            ControllerUrl: "https://temp.lan:8443",
            Username: "admin",
            Password: "TempPassword123!"
        ));

        var deleted = await service.DeleteUniFiInstanceAsync("unifi-temp");
        Assert.True(deleted);

        var retrieved = await service.GetUniFiInstanceAsync("unifi-temp");
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task UniFiClientFactory_ResolveAsync_ReturnsDecryptedPassword()
    {
        var (db, conn) = CreateTestDbContext();
        using var _ = conn;
        var enc = CreateEncryptionService();
        var configService = new AdapterConfigService(
            db,
            Options.Create(new Features.Adapters.Proxmox.ProxmoxOptions()),
            enc,
            NullLogger<AdapterConfigService>.Instance);

        await configService.SaveUniFiInstanceAsync(new SaveUniFiInstanceRequest(
            Id: "unifi-resolved",
            Name: "Resolved Controller",
            ControllerUrl: "https://resolved.lan:8443",
            Username: "admin",
            Password: "PlainPasswordXYZ",
            AuthType: "credentials"
        ));

        var mockClient = new MockUniFiClient();
        var factory = new UniFiClientFactory(
            configService,
            enc,
            mockClient,
            NullLogger<UniFiClientFactory>.Instance);

        var (client, config, password, apiKey) = await factory.ResolveAsync("unifi-resolved");
        Assert.Same(mockClient, client);
        Assert.Equal("https://resolved.lan:8443", config.ControllerUrl);
        Assert.Equal("PlainPasswordXYZ", password);
        Assert.Null(apiKey);
    }

    [Fact]
    public async Task UniFiClientFactory_ResolveAsync_UniFiOS_ReturnsDecryptedApiKey()
    {
        var (db, conn) = CreateTestDbContext();
        using var _ = conn;
        var enc = CreateEncryptionService();
        var configService = new AdapterConfigService(
            db,
            Options.Create(new Features.Adapters.Proxmox.ProxmoxOptions()),
            enc,
            NullLogger<AdapterConfigService>.Instance);

        await configService.SaveUniFiInstanceAsync(new SaveUniFiInstanceRequest(
            Id: "unifi-os-resolved",
            Name: "UniFi OS Machine",
            ControllerUrl: "https://192.168.1.1",
            ApiKey: "SecretUniFiOSApiKey123",
            AuthType: "api_key"
        ));

        var mockClient = new MockUniFiClient();
        var factory = new UniFiClientFactory(
            configService,
            enc,
            mockClient,
            NullLogger<UniFiClientFactory>.Instance);

        var (client, config, password, apiKey) = await factory.ResolveAsync("unifi-os-resolved");
        Assert.Same(mockClient, client);
        Assert.Equal("https://192.168.1.1", config.ControllerUrl);
        Assert.Equal("SecretUniFiOSApiKey123", apiKey);
        Assert.Equal("api_key", config.AuthType);
    }

    [Fact]
    public async Task UniFiClient_WithApiKey_SendsXApiKeyHeaderAndBypassesLogin()
    {
        var capturedRequests = new List<HttpRequestMessage>();
        var handler = new DelegatingHandlerStub((req, ct) =>
        {
            capturedRequests.Add(req);

            if (req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/proxy/network/api/s/default/stat/sysinfo"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[{\"version\":\"8.1.113\"}]}")
                };
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[]}")
            });
        });

        var httpClient = new HttpClient(handler);
        var mockFactory = new TestHttpClientFactory(httpClient);
        var client = new UniFiClient(mockFactory, NullLogger<UniFiClient>.Instance);

        var testResult = await client.TestConnectionAsync(
            controllerUrl: "https://192.168.1.1",
            username: null,
            password: null,
            site: "default",
            apiKey: "my-test-api-key");

        Assert.True(testResult.Success);
        Assert.Equal("8.1.113", testResult.ControllerVersion);

        // Verify that NO login requests were sent to /api/auth/login or /api/login
        Assert.DoesNotContain(capturedRequests, r => r.RequestUri?.PathAndQuery.Contains("/login") == true);

        // Verify that requests had X-API-KEY header
        Assert.Contains(capturedRequests, r =>
            r.Headers.TryGetValues("X-API-KEY", out var vals) &&
            vals.Contains("my-test-api-key"));
    }

    private class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public TestHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private class DelegatingHandlerStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public DelegatingHandlerStub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }

    private class MockUniFiClient : IUniFiClient
    {
        public Task<bool> LoginAsync(string controllerUrl, string username, string password, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<UniFiTestResultDto> TestConnectionAsync(string controllerUrl, string? username, string? password, string site = "default", string? apiKey = null, CancellationToken ct = default)
            => Task.FromResult(new UniFiTestResultDto(true, "8.0.28", 5, 20, new List<string> { "default" }, 15, null));

        public Task<List<UniFiDeviceDto>> GetDevicesAsync(string controllerUrl, string? username, string? password, string site = "default", string? apiKey = null, CancellationToken ct = default)
            => Task.FromResult(new List<UniFiDeviceDto>
            {
                new("00:11:22:33:44:55", "Core Switch", "USW-24-PoE", "usw", "192.168.1.2", "Connected", "6.5.59", false, 3600, 45.0, new List<UniFiPortDto>
                {
                    new(1, "Port 1", true, 1000, "auto", 7.5, 54.0, 0.14)
                })
            });

        public Task<bool> RestartDeviceAsync(string controllerUrl, string? username, string? password, string deviceMac, string site = "default", string? apiKey = null, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> UpgradeDeviceAsync(string controllerUrl, string? username, string? password, string deviceMac, string site = "default", string? apiKey = null, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<UniFiBounceResult> CyclePoEPortAsync(string controllerUrl, string? username, string? password, string switchMac, int portNumber, string site = "default", int delaySeconds = 5, string? apiKey = null, CancellationToken ct = default)
            => Task.FromResult(new UniFiBounceResult(true, "Bounced", switchMac, portNumber));

        public Task<List<UniFiMacLease>> GetActiveClientsAsync(string controllerUrl, string? username, string? password, string site = "default", string? apiKey = null, CancellationToken ct = default)
            => Task.FromResult(new List<UniFiMacLease>
            {
                new("aa:bb:cc:dd:ee:ff", "192.168.1.100", "pi-worker-1", DateTimeOffset.UtcNow)
            });
    }
}
