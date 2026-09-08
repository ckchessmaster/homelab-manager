using System.Net;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Idrac;
using ControlPlane.Api.Features.Security;
using ControlPlane.Api.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Tests;

public class IdracMultiInstanceTests
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
    public async Task AdapterConfigService_SaveAndRetrieveMultipleIdracInstances_EncryptsPasswords()
    {
        var (db, conn) = CreateTestDbContext();
        using var _ = conn;
        var enc = CreateEncryptionService();
        var service = new AdapterConfigService(
            db,
            Options.Create(new Features.Adapters.Proxmox.ProxmoxOptions()),
            enc,
            NullLogger<AdapterConfigService>.Instance);

        // 1. Save iDRAC 1
        var inst1 = await service.SaveIdracInstanceAsync(new SaveIdracInstanceRequest(
            Id: "r730-bmc",
            Name: "Dell R730xd BMC",
            BmcUrl: "https://192.168.1.120",
            Username: "root",
            Password: "CalvinPassword1!",
            HostnameOrIp: "192.168.1.120"
        ));

        Assert.Equal("r730-bmc", inst1.Id);
        Assert.Equal("Dell R730xd BMC", inst1.Name);
        Assert.Equal(AdapterConfigService.MaskedPlaceholder, inst1.PasswordMasked);
        Assert.True(inst1.HasPassword);

        // Verify stored in DB encrypted
        var raw = await service.GetRawIdracInstanceAsync("r730-bmc");
        Assert.NotNull(raw);
        Assert.NotEqual("CalvinPassword1!", raw.EncryptedPassword);
        Assert.Equal("CalvinPassword1!", enc.Decrypt(raw.EncryptedPassword));

        // 2. Save iDRAC 2
        var inst2 = await service.SaveIdracInstanceAsync(new SaveIdracInstanceRequest(
            Id: null,
            Name: "Dell R640 Worker BMC",
            BmcUrl: "https://192.168.1.121",
            Username: "root",
            Password: "CalvinPassword2!"
        ));

        Assert.StartsWith("dell-r640", inst2.Id);

        // 3. List all
        var all = await service.GetIdracInstancesAsync();
        Assert.Equal(2, all.Count);

        // 4. Test factory resolve by BMC IP
        var client = new IdracClient(new MockHttpClientFactory(new HttpClient()), NullLogger<IdracClient>.Instance);
        var factory = new IdracClientFactory(service, enc, client, NullLogger<IdracClientFactory>.Instance);

        var resolved = await factory.ResolveByHostBmcIpAsync("192.168.1.120");
        Assert.NotNull(resolved);
        Assert.Equal("CalvinPassword1!", resolved.Value.Password);
        Assert.Equal("root", resolved.Value.Username);
    }

    [Fact]
    public async Task IdracClient_GetVitalsAsync_ParsesSystemInfoThermalsAndPowerWatts()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var uri = req.RequestUri!.ToString();

            if (uri.Contains("/redfish/v1/Systems/System.Embedded.1") && !uri.Contains("Actions"))
            {
                var json = JsonSerializer.Serialize(new
                {
                    PowerState = "On",
                    Model = "PowerEdge R730xd",
                    BiosVersion = "2.12.1",
                    SerialNumber = "8GHY912",
                    Status = new { Health = "OK" }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (uri.Contains("/redfish/v1/Chassis/System.Embedded.1/Thermal"))
            {
                var json = JsonSerializer.Serialize(new
                {
                    Temperatures = new[]
                    {
                        new { Name = "System Board Inlet Temp", ReadingCelsius = 22.0, UpperThresholdCritical = 42.0, Status = new { Health = "OK" } },
                        new { Name = "CPU1 Temp", ReadingCelsius = 48.0, UpperThresholdCritical = 88.0, Status = new { Health = "OK" } }
                    },
                    Fans = new[]
                    {
                        new { FanName = "System Fan 1", Reading = 4800, Status = new { Health = "OK" } },
                        new { FanName = "System Fan 2", Reading = 4920, Status = new { Health = "OK" } }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (uri.Contains("/redfish/v1/Chassis/System.Embedded.1/Power"))
            {
                var json = JsonSerializer.Serialize(new
                {
                    PowerControl = new[]
                    {
                        new { PowerConsumedWatts = 168.5 }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factory = new MockHttpClientFactory(new HttpClient(handler));
        var client = new IdracClient(factory, NullLogger<IdracClient>.Instance);

        var vitals = await client.GetVitalsAsync("https://192.168.1.120", "root", "calvin");

        Assert.Equal("On", vitals.PowerState);
        Assert.Equal("PowerEdge R730xd", vitals.Model);
        Assert.Equal("2.12.1", vitals.BiosVersion);
        Assert.Equal("8GHY912", vitals.SerialNumber);
        Assert.Equal(168.5, vitals.PowerConsumptionWatts);
        Assert.Equal(2, vitals.Temperatures.Count);
        Assert.Equal(22.0, vitals.Temperatures[0].CurrentReadingCelsius);
        Assert.Equal(2, vitals.Fans.Count);
        Assert.Equal(4800, vitals.Fans[0].ReadingRpm);
    }

    [Fact]
    public async Task IdracClient_ResetSystemAsync_DispatchesNormalizedPowerAction()
    {
        string? capturedBody = null;

        var handler = new MockHttpMessageHandler(req =>
        {
            var uri = req.RequestUri!.ToString();
            if (req.Method == HttpMethod.Post && uri.Contains("/redfish/v1/Systems/System.Embedded.1/Actions/ComputerSystem.Reset"))
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factory = new MockHttpClientFactory(new HttpClient(handler));
        var client = new IdracClient(factory, NullLogger<IdracClient>.Instance);

        var result = await client.ResetSystemAsync("https://192.168.1.120", "root", "calvin", "GracefulShutdown");

        Assert.True(result.Success);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"ResetType\":\"GracefulShutdown\"", capturedBody);
    }
}
