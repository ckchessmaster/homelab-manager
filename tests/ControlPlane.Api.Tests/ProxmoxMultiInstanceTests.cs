using System.Net;
using System.Net.Http.Json;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Features.Security;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Tests;

public class ProxmoxMultiInstanceTests
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
    public async Task AdapterConfigService_SaveAndRetrieveMultipleInstances_EncryptsSecrets()
    {
        var (db, conn) = CreateTestDbContext();
        using var _ = conn;
        using var __ = db;
        var encryptionService = CreateEncryptionService();
        var defaultOptions = Options.Create(new ProxmoxOptions());
        var logger = NullLogger<AdapterConfigService>.Instance;

        var service = new AdapterConfigService(db, defaultOptions, encryptionService, logger);

        // 1. Save instance 1: Prod
        var prodRequest = new SaveProxmoxInstanceRequest(
            Id: "pve-prod",
            Name: "Production Cluster",
            BaseUrl: "https://pve-prod.lab.local:8006",
            ApiTokenId: "root@pam!token1",
            ApiTokenSecret: "super-secret-token-1"
        );
        var prodDto = await service.SaveProxmoxInstanceAsync(prodRequest);

        // 2. Save instance 2: Lab
        var labRequest = new SaveProxmoxInstanceRequest(
            Id: "pve-lab",
            Name: "Lab Standalone",
            BaseUrl: "https://pve-lab.lab.local:8006",
            ApiTokenId: "root@pam!token2",
            ApiTokenSecret: "super-secret-token-2"
        );
        var labDto = await service.SaveProxmoxInstanceAsync(labRequest);

        // 3. List instances
        var allInstances = await service.GetProxmoxInstancesAsync();
        Assert.Equal(2, allInstances.Count);
        Assert.Contains(allInstances, i => i.Id == "pve-prod" && i.Name == "Production Cluster");
        Assert.Contains(allInstances, i => i.Id == "pve-lab" && i.Name == "Lab Standalone");

        // 4. Verify secret is masked in DTOs
        var prodRetrieved = await service.GetProxmoxInstanceAsync("pve-prod");
        Assert.NotNull(prodRetrieved);
        Assert.True(prodRetrieved.HasSecret);
        Assert.Equal(AdapterConfigService.MaskedPlaceholder, prodRetrieved.ApiTokenSecretMasked);

        // 5. Verify GetActiveProxmoxOptionsAsync resolves decrypted secret per instance
        var prodOptions = await service.GetActiveProxmoxOptionsAsync("pve-prod");
        Assert.Equal("https://pve-prod.lab.local:8006", prodOptions.BaseUrl);
        Assert.Equal("super-secret-token-1", prodOptions.ApiTokenSecret);

        var labOptions = await service.GetActiveProxmoxOptionsAsync("pve-lab");
        Assert.Equal("https://pve-lab.lab.local:8006", labOptions.BaseUrl);
        Assert.Equal("super-secret-token-2", labOptions.ApiTokenSecret);
    }

    [Fact]
    public async Task AdapterConfigService_DeleteInstance_RemovesCorrectInstance()
    {
        var (db, conn) = CreateTestDbContext();
        using var _ = conn;
        using var __ = db;
        var encryptionService = CreateEncryptionService();
        var service = new AdapterConfigService(db, Options.Create(new ProxmoxOptions()), encryptionService, NullLogger<AdapterConfigService>.Instance);

        await service.SaveProxmoxInstanceAsync(new SaveProxmoxInstanceRequest("pve-1", "PVE 1", "https://pve1:8006", "tok1", "sec1"));
        await service.SaveProxmoxInstanceAsync(new SaveProxmoxInstanceRequest("pve-2", "PVE 2", "https://pve2:8006", "tok2", "sec2"));

        var deleted = await service.DeleteProxmoxInstanceAsync("pve-1");
        Assert.True(deleted);

        var remaining = await service.GetProxmoxInstancesAsync();
        Assert.Single(remaining);
        Assert.Equal("pve-2", remaining[0].Id);
    }

    [Fact]
    public async Task HostService_CreateHost_PersistsProxmoxInstanceId()
    {
        var (db, conn) = CreateTestDbContext();
        using var _ = conn;
        using var __ = db;
        var hostService = new HostService(db, NullLogger<HostService>.Instance);

        var request = new CreateHostRequest(
            Hostname: "worker-vm-01",
            FriendlyName: "Worker VM 01",
            IpAddress: "192.168.1.55",
            OsFamily: "linux_debian",
            TargetType: "proxmox_vm",
            ProxmoxNode: "pve-node-02",
            ProxmoxVmid: 204,
            ProxmoxInstanceId: "pve-prod"
        );

        var (created, errors, conflict) = await hostService.CreateHostAsync(request);
        Assert.False(conflict);
        Assert.Null(errors);
        Assert.NotNull(created);
        Assert.NotNull(created.Proxmox);
        Assert.Equal("pve-prod", created.Proxmox.InstanceId);
        Assert.Equal("pve-node-02", created.Proxmox.Node);
        Assert.Equal(204, created.Proxmox.Vmid);

        // Update host instance ID
        var updated = await hostService.UpdateHostAsync(created.Id, new UpdateHostRequest(ProxmoxInstanceId: "pve-lab"));
        Assert.NotNull(updated.Host?.Proxmox);
        Assert.Equal("pve-lab", updated.Host.Proxmox.InstanceId);
    }
}
