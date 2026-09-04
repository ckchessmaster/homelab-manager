using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class HostEndpointsTests
{
    private class HostTestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-hosts-{Guid.NewGuid():N}.db");

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

    private static HttpClient CreateAuthClient(HostTestAppFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");
        return client;
    }

    [Fact]
    public async Task ListHosts_ReturnsSeededHosts()
    {
        using var factory = new HostTestAppFactory();
        var client = CreateAuthClient(factory);

        var response = await client.GetAsync("/api/v1/hosts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hosts = await response.Content.ReadFromJsonAsync<List<HostResponse>>();
        Assert.NotNull(hosts);
        Assert.True(hosts.Count >= 2);
        Assert.Contains(hosts, h => h.Hostname == "k8s-control-01");
        Assert.Contains(hosts, h => h.Hostname == "pve-node-01");
    }

    [Fact]
    public async Task ListHosts_FilterByOsFamily_FiltersAccurately()
    {
        using var factory = new HostTestAppFactory();
        var client = CreateAuthClient(factory);

        var response = await client.GetAsync("/api/v1/hosts?osFamily=linux_debian");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hosts = await response.Content.ReadFromJsonAsync<List<HostResponse>>();
        Assert.NotNull(hosts);
        Assert.All(hosts, h => Assert.Equal("linux_debian", h.OsFamily));
    }

    [Fact]
    public async Task ListHosts_FilterByPendingReboot_FiltersAccurately()
    {
        using var factory = new HostTestAppFactory();
        var client = CreateAuthClient(factory);

        var response = await client.GetAsync("/api/v1/hosts?pendingReboot=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hosts = await response.Content.ReadFromJsonAsync<List<HostResponse>>();
        Assert.NotNull(hosts);
        Assert.Contains(hosts, h => h.Hostname == "pve-node-01");
        Assert.DoesNotContain(hosts, h => h.Hostname == "k8s-control-01");
    }

    [Fact]
    public async Task GetHostById_ReturnsSingleHost()
    {
        using var factory = new HostTestAppFactory();
        var client = CreateAuthClient(factory);

        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var response = await client.GetAsync($"/api/v1/hosts/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var host = await response.Content.ReadFromJsonAsync<HostResponse>();
        Assert.NotNull(host);
        Assert.Equal("k8s-control-01", host.Hostname);
        Assert.Equal("192.168.1.10", host.IpAddress);
        Assert.NotNull(host.Proxmox);
        Assert.Equal("pve-node-01", host.Proxmox.Node);
        Assert.Equal(101, host.Proxmox.Vmid);
    }

    [Fact]
    public async Task GetHostById_NonExistent_Returns404()
    {
        using var factory = new HostTestAppFactory();
        var client = CreateAuthClient(factory);

        var response = await client.GetAsync($"/api/v1/hosts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateHost_ValidRequest_Returns201AndPersists()
    {
        using var factory = new HostTestAppFactory();
        var client = CreateAuthClient(factory);

        var request = new CreateHostRequest(
            Hostname: "test-compute-01",
            FriendlyName: "Test Compute Node",
            IpAddress: "192.168.1.75",
            OsFamily: "linux_ubuntu",
            TargetType: "proxmox_vm",
            ProxmoxNode: "pve-node-01",
            ProxmoxVmid: 200,
            IdracIp: "192.168.1.175",
            UnifiSwitchMac: "AA:BB:CC:DD:EE:FF",
            UnifiSwitchPort: 12
        );

        var response = await client.PostAsJsonAsync("/api/v1/hosts", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<HostResponse>();
        Assert.NotNull(created);
        Assert.Equal("test-compute-01", created.Hostname);
        Assert.Equal("192.168.1.75", created.IpAddress);
        Assert.Equal("linux_ubuntu", created.OsFamily);
        Assert.NotNull(created.Proxmox);
        Assert.Equal(200, created.Proxmox.Vmid);
        Assert.NotNull(created.Idrac);
        Assert.Equal("192.168.1.175", created.Idrac.IpAddress);
        Assert.NotNull(created.NetworkPort);
        Assert.Equal(12, created.NetworkPort.PortNumber);

        // Verify retrieval
        var getResponse = await client.GetAsync($"/api/v1/hosts/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task CreateHost_InvalidHostname_ReturnsValidationProblem()
    {
        using var factory = new HostTestAppFactory();
        var client = CreateAuthClient(factory);

        var request = new CreateHostRequest(
            Hostname: "invalid_hostname#_!",
            FriendlyName: "Bad Host",
            IpAddress: "192.168.1.99",
            OsFamily: "linux_debian",
            TargetType: "baremetal"
        );

        var response = await client.PostAsJsonAsync("/api/v1/hosts", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("Hostname", out _));
    }

    [Fact]
    public async Task CreateHost_InvalidIp_ReturnsValidationProblem()
    {
        using var factory = new HostTestAppFactory();
        var client = CreateAuthClient(factory);

        var request = new CreateHostRequest(
            Hostname: "good-host-name",
            FriendlyName: "Bad IP Host",
            IpAddress: "999.999.999.999",
            OsFamily: "linux_debian",
            TargetType: "baremetal"
        );

        var response = await client.PostAsJsonAsync("/api/v1/hosts", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("IpAddress", out _));
    }

    [Fact]
    public async Task CreateHost_DuplicateHostname_ReturnsConflict()
    {
        using var factory = new HostTestAppFactory();
        var client = CreateAuthClient(factory);

        var request = new CreateHostRequest(
            Hostname: "k8s-control-01", // already seeded
            FriendlyName: "Duplicate",
            IpAddress: "192.168.1.199",
            OsFamily: "linux_debian",
            TargetType: "baremetal"
        );

        var response = await client.PostAsJsonAsync("/api/v1/hosts", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateHost_DuplicateIp_ReturnsConflict()
    {
        using var factory = new HostTestAppFactory();
        var client = CreateAuthClient(factory);

        var request = new CreateHostRequest(
            Hostname: "unique-hostname",
            FriendlyName: "Duplicate IP",
            IpAddress: "192.168.1.10", // already seeded for k8s-control-01
            OsFamily: "linux_debian",
            TargetType: "baremetal"
        );

        var response = await client.PostAsJsonAsync("/api/v1/hosts", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateHost_UpdatesFieldsAndReflectsChanges()
    {
        using var factory = new HostTestAppFactory();
        var client = CreateAuthClient(factory);

        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var updateRequest = new UpdateHostRequest(
            FriendlyName: "Renamed K8s Master",
            PendingReboot: true
        );

        var response = await client.PutAsJsonAsync($"/api/v1/hosts/{id}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<HostResponse>();
        Assert.NotNull(updated);
        Assert.Equal("Renamed K8s Master", updated.FriendlyName);
        Assert.True(updated.Agent.PendingReboot);
    }

    [Fact]
    public async Task DeleteHost_DeletesSuccessfully()
    {
        using var factory = new HostTestAppFactory();
        var client = CreateAuthClient(factory);

        // Create a host to delete
        var req = new CreateHostRequest(
            Hostname: "host-to-delete",
            FriendlyName: "Deletable Host",
            IpAddress: "192.168.1.250",
            OsFamily: "linux_debian",
            TargetType: "baremetal"
        );
        var createResp = await client.PostAsJsonAsync("/api/v1/hosts", req);
        var created = await createResp.Content.ReadFromJsonAsync<HostResponse>();
        Assert.NotNull(created);

        // Delete it
        var delResp = await client.DeleteAsync($"/api/v1/hosts/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        // Verify gone
        var getResp = await client.GetAsync($"/api/v1/hosts/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task DeleteHost_WithActiveJob_ReturnsBadRequest()
    {
        using var factory = new HostTestAppFactory();
        var client = CreateAuthClient(factory);

        // Add an active update job to k8s-control-01
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var hostId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            db.UpdateJobs.Add(new UpdateJob
            {
                Id = Guid.NewGuid(),
                TargetHostId = hostId,
                InitiatedBy = "test-operator",
                Status = "Running",
                ActiveStep = "apt update",
                StartedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var delResp = await client.DeleteAsync("/api/v1/hosts/11111111-1111-1111-1111-111111111111");
        Assert.Equal(HttpStatusCode.BadRequest, delResp.StatusCode);
    }
}
