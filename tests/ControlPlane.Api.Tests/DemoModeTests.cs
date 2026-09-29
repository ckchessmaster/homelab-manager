using System.Net;
using System.Net.Http.Json;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Demo;
using ControlPlane.Api.Features.Demo.Adapters;
using ControlPlane.Api.Features.Security;
using ControlPlane.Api.Storage;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ControlPlane.Api.Tests;

public class DemoModeTests
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

    [Fact]
    public async Task DemoDataSeeder_SeedsHostsJobsAndRegistersSimulatedAgents()
    {
        var (db, conn) = CreateTestDbContext();
        using (conn)
        using (db)
        {
            var secretService = new MockSecretEncryptionService();
            var adapterConfigService = new AdapterConfigService(db, Options.Create(new ProxmoxOptions()), secretService, NullLogger<AdapterConfigService>.Instance);
            var connectionManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);

            var seeder = new DemoDataSeeder(db, adapterConfigService, connectionManager, NullLogger<DemoDataSeeder>.Instance);

            await seeder.SeedAsync(false, CancellationToken.None);

            // Verify 5 hosts created
            var hosts = await db.Hosts.ToListAsync();
            Assert.Equal(5, hosts.Count);
            Assert.Contains(hosts, h => h.Hostname == "pve-node-01");
            Assert.Contains(hosts, h => h.Hostname == "k8s-cp-01");
            Assert.Contains(hosts, h => h.Hostname == "storage-nas-01");

            // Verify simulated sessions registered
            var onlineHostIds = connectionManager.GetOnlineHostIds();
            Assert.Equal(5, onlineHostIds.Count);
            Assert.True(connectionManager.IsOnline(DemoDataSeeder.HostPveNode01Id));
            Assert.True(connectionManager.IsOnline(DemoDataSeeder.HostK8sCp01Id));

            // Verify jobs seeded
            var jobs = await db.UpdateJobs.ToListAsync();
            Assert.NotEmpty(jobs);

            var logs = await db.StepLogs.ToListAsync();
            Assert.NotEmpty(logs);
        }
    }

    [Fact]
    public async Task MockProxmoxClient_ReturnsSimulatedNodesAndVms()
    {
        var client = new MockProxmoxClient();
        var resources = await client.DiscoverClusterResourcesAsync();
        Assert.NotEmpty(resources);
        Assert.Contains(resources, r => r.Name == "k8s-cp-01");
    }

    [Fact]
    public async Task MockKubernetesAdapter_ReturnsSimulatedNodesAndWorkloads()
    {
        var client = new MockKubernetesAdapter();
        var nodes = await client.ListNodesAsync();
        Assert.Equal(3, nodes.Count);

        var testRes = await client.TestConnectionAsync();
        Assert.True(testRes.Success);
    }

    [Fact]
    public async Task MockUniFiClient_ReturnsSimulatedDevicesAndPorts()
    {
        var client = new MockUniFiClient();
        var devices = await client.GetDevicesAsync("https://192.168.1.1:8443", "admin", "pass");
        Assert.NotEmpty(devices);
        var usw = devices.FirstOrDefault(d => d.Ports.Count > 0);
        Assert.NotNull(usw);
        Assert.Equal(24, usw.Ports.Count);
    }

    [Fact]
    public async Task MockIdracClient_ReturnsSensorsAndPowerState()
    {
        var client = new MockIdracClient();
        var vitals = await client.GetVitalsAsync("https://192.168.1.25", "root", "calvin");
        Assert.Equal("On", vitals.PowerState);
        Assert.NotEmpty(vitals.Temperatures);
    }

    [Fact]
    public async Task DemoSimulationWorker_SimulatesRebootCycle()
    {
        var (db, conn) = CreateTestDbContext();
        using (conn)
        using (db)
        {
            var secretService = new MockSecretEncryptionService();
            var adapterConfigService = new AdapterConfigService(db, Options.Create(new ProxmoxOptions()), secretService, NullLogger<AdapterConfigService>.Instance);
            var connectionManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
            var seeder = new DemoDataSeeder(db, adapterConfigService, connectionManager, NullLogger<DemoDataSeeder>.Instance);

            await seeder.SeedAsync(false);

            var host = await db.Hosts.FindAsync(DemoDataSeeder.HostK8sCp01Id);
            Assert.NotNull(host);
            host.Agent.PendingReboot = true;
            await db.SaveChangesAsync();

            var services = new ServiceCollection();
            services.AddScoped<ControlPlaneDbContext>(_ =>
            {
                var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
                    .UseSqlite(conn)
                    .UseSnakeCaseNamingConvention()
                    .Options;
                return new ControlPlaneDbContext(options);
            });
            var sp = services.BuildServiceProvider();
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

            var options = Options.Create(new DemoOptions { Enabled = true, SimulationTickSeconds = 1 });
            var worker = new DemoSimulationWorker(
                scopeFactory,
                connectionManager,
                options,
                NullLogger<DemoSimulationWorker>.Instance
            );

            // Simulate reboot with very short duration
            await worker.SimulateRebootCycleAsync(DemoDataSeeder.HostK8sCp01Id, TimeSpan.FromMilliseconds(50));

            // Agent should be back online and PendingReboot cleared
            Assert.True(connectionManager.IsOnline(DemoDataSeeder.HostK8sCp01Id));
            using var verifyDb = new ControlPlaneDbContext(new DbContextOptionsBuilder<ControlPlaneDbContext>().UseSqlite(conn).UseSnakeCaseNamingConvention().Options);
            var reloadedHost = await verifyDb.Hosts.FirstOrDefaultAsync(h => h.Id == DemoDataSeeder.HostK8sCp01Id);
            Assert.NotNull(reloadedHost);
            Assert.False(reloadedHost.Agent.PendingReboot);
        }
    }

    private class MockSecretEncryptionService : ISecretEncryptionService
    {
        public string Encrypt(string? plainText) => $"enc:{plainText ?? string.Empty}";
        public string Decrypt(string? cipherTextOrPlain) => cipherTextOrPlain != null && cipherTextOrPlain.StartsWith("enc:") ? cipherTextOrPlain[4..] : cipherTextOrPlain ?? string.Empty;
        public bool IsEncrypted(string? value) => value != null && value.StartsWith("enc:");
    }
}
