using System.Net;
using System.Net.WebSockets;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Idrac;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Security;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Tests;

public class AgentIpmiIntegrationTests
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

    private class MockCommandExecutor : IAgentCommandExecutor
    {
        public Func<Guid, string, string[], AgentCommandResult>? Handler { get; set; }
        public List<(Guid HostId, string Command, string[] Args)> DispatchedCommands { get; } = new();

        public Task<AgentCommandResult> ExecuteCommandAsync(
            Guid hostId,
            Guid jobId,
            string command,
            string[] args,
            CancellationToken cancellationToken = default)
        {
            DispatchedCommands.Add((hostId, command, args));
            if (Handler != null)
            {
                return Task.FromResult(Handler(hostId, command, args));
            }
            return Task.FromResult(new AgentCommandResult(true, 0, null, "", ""));
        }

        public void NotifyFrame(Guid hostId, AgentFrameData frame) { }
    }

    [Fact]
    public void AgentIpmiExecutor_Parsers_ParseCorrectly()
    {
        // 1. Power status parsing
        Assert.Equal("On", AgentIpmiExecutor.ParsePowerStatus("Chassis Power is on"));
        Assert.Equal("Off", AgentIpmiExecutor.ParsePowerStatus("Chassis Power is off"));
        Assert.Equal("Unknown", AgentIpmiExecutor.ParsePowerStatus("Some error"));
        Assert.Equal("Unknown", AgentIpmiExecutor.ParsePowerStatus(""));

        // 2. MapResetTypeToArgs
        Assert.Equal(new[] { "chassis", "power", "on" }, AgentIpmiExecutor.MapResetTypeToArgs("On"));
        Assert.Equal(new[] { "chassis", "power", "soft" }, AgentIpmiExecutor.MapResetTypeToArgs("GracefulShutdown"));
        Assert.Equal(new[] { "chassis", "power", "cycle" }, AgentIpmiExecutor.MapResetTypeToArgs("PowerCycle"));
        Assert.Equal(new[] { "chassis", "power", "reset" }, AgentIpmiExecutor.MapResetTypeToArgs("ForceRestart"));
        Assert.Equal(new[] { "chassis", "power", "off" }, AgentIpmiExecutor.MapResetTypeToArgs("ForceOff"));

        // 3. BMC & FRU parsing
        var bmcOutput = @"
Device ID                 : 32
Device Revision           : 1
Firmware Revision         : 2.84
IPMI Version              : 2.0
Manufacturer ID           : 674 (Dell Inc.)
Manufacturer Name         : Dell Inc.
Product ID                : 256 (0x0100)
Product Name              : PowerEdge R730xd
Device Available          : yes";

        var fruOutput = @"
FRU Device Description : Builtin FRU Device (ID 0)
 Chassis Type          : Rack Mount Chassis
 Chassis Serial        : 7ABCD12
 Product Manufacturer  : DELL
 Product Name          : PowerEdge R730xd
 Product Serial        : 7ABCD12";

        var (model, bios, serial, health, bmcFw) = AgentIpmiExecutor.ParseBmcAndFruInfo(bmcOutput, fruOutput);
        Assert.Equal("Dell Inc. PowerEdge R730xd", model);
        Assert.Equal("7ABCD12", serial);
        Assert.Equal("OK", health);
        Assert.Equal("2.84", bmcFw);

        // 3b. Dell 12G Unknown (0x100) parsing fix (R720xd test case)
        var dell12gBmcOutput = @"
Device ID                 : 32
Device Revision           : 1
Firmware Revision         : 2.65
IPMI Version              : 2.0
Manufacturer ID           : 674 (Dell Inc.)
Manufacturer Name         : Dell Inc.
Product ID                : 256 (0x0100)
Product Name              : Unknown (0x100)
Device Available          : yes";

        var dell12gFruOutput = @"
FRU Device Description : Builtin FRU Device (ID 0)
 Chassis Type          : Rack Mount Chassis
 Chassis Serial        : CN7016342L0099
 Board Mfg             : DELL
 Board Product         : PowerEdge R720xd
 Board Serial          : CN7016342L0099
 Product Manufacturer  : DELL
 Product Name          : PowerEdge R720xd
 Product Serial        : CN7016342L0099";

        var (r720Model, _, r720Serial, _, r720Fw) = AgentIpmiExecutor.ParseBmcAndFruInfo(dell12gBmcOutput, dell12gFruOutput);
        Assert.Equal("Dell Inc. PowerEdge R720xd", r720Model);
        Assert.DoesNotContain("Unknown", r720Model);
        Assert.Equal("CN7016342L0099", r720Serial);
        Assert.Equal("2.65", r720Fw);

        // 4. Sensor parsing
        var sensorOutput = @"
Inlet Temp       | 21.000     | degrees C  | ok    | na        | na        | na        | 42.000    | 47.000    | na
Exhaust Temp     | 36.000     | degrees C  | ok    | na        | na        | na        | 70.000    | 75.000    | na
CPU1 Temp        | 42.000     | degrees C  | ok    | na        | na        | na        | 86.000    | 91.000    | na
Fan1A RPM        | 3600.000   | RPM        | ok    | na        | na        | na        | na        | na        | na
Fan2A RPM        | 3720.000   | RPM        | ok    | na        | na        | na        | na        | na        | na
Pwr Consumption  | 142.000    | Watts      | ok    | na        | na        | na        | 896.000   | 960.000   | na";

        var (temps, fans, pwr, sHealth) = AgentIpmiExecutor.ParseSensors(sensorOutput);
        Assert.Equal(3, temps.Count);
        Assert.Equal("Inlet Temp", temps[0].Name);
        Assert.Equal(21.0, temps[0].CurrentReadingCelsius);
        Assert.Equal(42.0, temps[0].CriticalThresholdCelsius);

        Assert.Equal(2, fans.Count);
        Assert.Equal("Fan1A RPM", fans[0].Name);
        Assert.Equal(3600, fans[0].ReadingRpm);

        Assert.Equal(142.0, pwr);
        Assert.Equal("OK", sHealth);

        // 5. Chassis identify and boot override helpers
        Assert.Equal(new[] { "chassis", "identify", "15" }, AgentIpmiExecutor.MapIdentifyStateToArgs("Blink", 15));
        Assert.Equal(new[] { "chassis", "identify", "0" }, AgentIpmiExecutor.MapIdentifyStateToArgs("Off", 15));
        Assert.Equal("bios", AgentIpmiExecutor.NormalizeBootTarget("BiosSetup"));
        Assert.Equal("pxe", AgentIpmiExecutor.NormalizeBootTarget("pxe"));
        Assert.Equal("disk", AgentIpmiExecutor.NormalizeBootTarget("hdd"));
    }

    [Fact]
    public async Task AgentIpmiExecutor_TestConnectionAsync_OfflineAgent_ReturnsOfflineResult()
    {
        var connManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var cmdExecutor = new MockCommandExecutor();
        var executor = new AgentIpmiExecutor(cmdExecutor, connManager, NullLogger<AgentIpmiExecutor>.Instance);

        var hostId = Guid.NewGuid();
        var result = await executor.TestConnectionAsync(hostId);

        Assert.False(result.Success);
        Assert.Equal("Offline", result.PowerState);
        Assert.Contains("offline", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentIpmiExecutor_TestConnectionAsync_MissingIpmitool_ReturnsActionableError()
    {
        var connManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var hostId = Guid.NewGuid();
        // Simulate online agent
        connManager.Register(hostId, "pve-node", new MockWebSocket(), "localhost", "https");

        var cmdExecutor = new MockCommandExecutor
        {
            Handler = (h, cmd, args) => new AgentCommandResult(
                false,
                -1,
                "Failed to start process: exec: \"ipmitool\": executable file not found in $PATH",
                "",
                "")
        };

        var executor = new AgentIpmiExecutor(cmdExecutor, connManager, NullLogger<AgentIpmiExecutor>.Instance);
        var result = await executor.TestConnectionAsync(hostId);

        Assert.False(result.Success);
        Assert.Contains("apt install ipmitool", result.Message);
    }

    [Fact]
    public async Task AgentIpmiExecutor_GetVitalsAndResetSystem_ExecuteViaAgent()
    {
        var connManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var hostId = Guid.NewGuid();
        connManager.Register(hostId, "pve-node", new MockWebSocket(), "localhost", "https");

        var cmdExecutor = new MockCommandExecutor
        {
            Handler = (h, cmd, args) =>
            {
                if (args.SequenceEqual(new[] { "chassis", "power", "status" }))
                {
                    return new AgentCommandResult(true, 0, null, "Chassis Power is on", "");
                }
                if (args.SequenceEqual(new[] { "bmc", "info" }))
                {
                    return new AgentCommandResult(true, 0, null, "Product Name : PowerEdge R730xd\nFirmware Revision : 2.84\nDevice Available : yes", "");
                }
                if (args.SequenceEqual(new[] { "fru" }))
                {
                    return new AgentCommandResult(true, 0, null, "Product Serial : 7ABCD12", "");
                }
                if (args.SequenceEqual(new[] { "sensor" }))
                {
                    return new AgentCommandResult(true, 0, null, "CPU1 Temp | 42.0 | degrees C | ok\nFan1 | 3500 | RPM | ok\nPwr | 120 | Watts | ok", "");
                }
                if (args.SequenceEqual(new[] { "chassis", "power", "cycle" }))
                {
                    return new AgentCommandResult(true, 0, null, "Chassis Power Control: Cycle", "");
                }
                return new AgentCommandResult(true, 0, null, "", "");
            }
        };

        var executor = new AgentIpmiExecutor(cmdExecutor, connManager, NullLogger<AgentIpmiExecutor>.Instance);

        // 1. Get vitals
        var vitals = await executor.GetVitalsAsync(hostId);
        Assert.Equal("On", vitals.PowerState);
        Assert.Equal("PowerEdge R730xd", vitals.Model);
        Assert.Equal("2.84", vitals.BiosVersion);
        Assert.Equal("7ABCD12", vitals.SerialNumber);
        Assert.Equal(120.0, vitals.PowerConsumptionWatts);
        Assert.Single(vitals.Temperatures);
        Assert.Single(vitals.Fans);

        // 2. Power cycle
        var resetRes = await executor.ResetSystemAsync(hostId, "PowerCycle");
        Assert.True(resetRes.Success);
        Assert.Contains("Cycle", resetRes.Message);
    }

    [Fact]
    public async Task AdapterConfigService_SaveAndRetrieveAgentModeIdracInstance_LinksHost()
    {
        var (db, conn) = CreateTestDbContext();
        using var _ = conn;
        var enc = CreateEncryptionService();

        var hostId = Guid.NewGuid();
        var host = new Host
        {
            Id = hostId,
            Hostname = "pve-node-01.homelab.local",
            FriendlyName = "PVE Hypervisor Node 1",
            IpAddress = "192.168.1.10",
            OsFamily = "linux_debian",
            TargetType = "proxmox"
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();

        var service = new AdapterConfigService(
            db,
            Options.Create(new Features.Adapters.Proxmox.ProxmoxOptions()),
            enc,
            NullLogger<AdapterConfigService>.Instance);

        // 1. Save instance in Agent mode
        var saved = await service.SaveIdracInstanceAsync(new SaveIdracInstanceRequest(
            Id: "pve1-ipmi",
            Name: "PVE 1 Local IPMI",
            ConnectionMode: "agent",
            HostId: hostId
        ));

        Assert.Equal("pve1-ipmi", saved.Id);
        Assert.Equal("agent", saved.ConnectionMode);
        Assert.Equal(hostId, saved.HostId);
        Assert.Equal("PVE Hypervisor Node 1", saved.HostName);
        Assert.StartsWith("agent://", saved.BmcUrl);

        // 2. Retrieve all
        var all = await service.GetIdracInstancesAsync();
        Assert.Single(all);
        Assert.Equal("agent", all[0].ConnectionMode);
        Assert.Equal(hostId, all[0].HostId);
        Assert.Equal("PVE Hypervisor Node 1", all[0].HostName);

        // 3. Test factory resolution by host ID
        var connManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var cmdExecutor = new MockCommandExecutor();
        var ipmiExecutor = new AgentIpmiExecutor(cmdExecutor, connManager, NullLogger<AgentIpmiExecutor>.Instance);
        var client = new IdracClient(new MockHttpClientFactory(new HttpClient()), NullLogger<IdracClient>.Instance, ipmiExecutor);
        var factory = new IdracClientFactory(service, enc, client, NullLogger<IdracClientFactory>.Instance);

        var resolvedByHost = await factory.ResolveByHostIdAsync(hostId);
        Assert.NotNull(resolvedByHost);
        Assert.Equal("agent", resolvedByHost.Value.Config.ConnectionMode);
        Assert.Equal(hostId, resolvedByHost.Value.Config.HostId);

        // 4. Test ResolveByHostBmcIpAsync with Guid string
        var resolvedByGuidStr = await factory.ResolveByHostBmcIpAsync(hostId.ToString());
        Assert.NotNull(resolvedByGuidStr);
        Assert.StartsWith("agent://", resolvedByGuidStr.Value.BmcUrl);
    }

    [Fact]
    public async Task IdracClient_UnifiedInstanceDispatch_RoutesToAgentCorrectly()
    {
        var connManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var hostId = Guid.NewGuid();
        connManager.Register(hostId, "pve-node", new MockWebSocket(), "localhost", "https");

        var cmdExecutor = new MockCommandExecutor
        {
            Handler = (h, cmd, args) =>
            {
                if (args.SequenceEqual(new[] { "chassis", "power", "status" }))
                    return new AgentCommandResult(true, 0, null, "Chassis Power is on", "");
                if (args.SequenceEqual(new[] { "bmc", "info" }))
                    return new AgentCommandResult(true, 0, null, "Product Name : Dell R730xd\nDevice Available : yes", "");
                if (args.SequenceEqual(new[] { "sensor" }))
                    return new AgentCommandResult(true, 0, null, "Temp | 35.0 | degrees C | ok", "");
                if (args.SequenceEqual(new[] { "chassis", "power", "off" }))
                    return new AgentCommandResult(true, 0, null, "Chassis Power Control: Down/Off", "");
                return new AgentCommandResult(true, 0, null, "", "");
            }
        };

        var ipmiExecutor = new AgentIpmiExecutor(cmdExecutor, connManager, NullLogger<AgentIpmiExecutor>.Instance);
        var client = new IdracClient(new MockHttpClientFactory(new HttpClient()), NullLogger<IdracClient>.Instance, ipmiExecutor);

        var agentConfig = new IdracStoredInstance
        {
            Id = "test-agent-bmc",
            Name = "Test Host Agent BMC",
            ConnectionMode = "agent",
            HostId = hostId
        };

        // Test preflight
        var testRes = await client.TestInstanceAsync(agentConfig, "");
        Assert.True(testRes.Success);
        Assert.Equal("On", testRes.PowerState);

        // Get vitals
        var vitals = await client.GetInstanceVitalsAsync(agentConfig, "");
        Assert.Equal("On", vitals.PowerState);
        Assert.Single(vitals.Temperatures);

        // Power off
        var resetRes = await client.ResetInstanceSystemAsync(agentConfig, "", "ForceOff");
        Assert.True(resetRes.Success);
        Assert.Contains("Down/Off", resetRes.Message);

        // Test agent:// URL delegation in legacy GetVitalsAsync
        var legacyVitals = await client.GetVitalsAsync($"agent://{hostId}", "agent", "");
        Assert.Equal("On", legacyVitals.PowerState);
    }

    [Fact]
    public async Task AgentIpmiExecutor_InstallIpmiToolAsync_ExecutesAptInstall()
    {
        var connManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var hostId = Guid.NewGuid();
        connManager.Register(hostId, "pve-node", new MockWebSocket(), "localhost", "https");

        string executedCommand = "";
        string[]? executedArgs = null;

        var cmdExecutor = new MockCommandExecutor
        {
            Handler = (h, cmd, args) =>
            {
                executedCommand = cmd;
                executedArgs = args;
                return new AgentCommandResult(true, 0, null, "Setting up ipmitool (1.8.19-7) ...", "");
            }
        };

        var executor = new AgentIpmiExecutor(cmdExecutor, connManager, NullLogger<AgentIpmiExecutor>.Instance);
        var (success, msg) = await executor.InstallIpmiToolAsync(hostId);

        Assert.True(success);
        Assert.Contains("Successfully installed", msg);
        Assert.Equal("sh", executedCommand);
        Assert.NotNull(executedArgs);
        Assert.Contains("apt-get install -y ipmitool", executedArgs![1]);
    }

    [Fact]
    public async Task IdracClient_Redfish404_FallsBackToLanIpmi()
    {
        var connManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var cmdExecutor = new MockCommandExecutor();

        // Create a custom executor that overrides LAN execution to simulate Dell R720xd response
        var ipmiExecutor = new MockLanIpmiExecutor(cmdExecutor, connManager);

        // Mock HTTP client that returns 404 on Redfish endpoints (simulating Dell iDRAC 7 where Redfish is disabled)
        var httpHandler = new MockHttpMessageHandler((req) => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"error\": \"RedFish attribute is disabled\"}")
        });
        var httpClient = new HttpClient(httpHandler);
        var client = new IdracClient(new MockHttpClientFactory(httpClient), NullLogger<IdracClient>.Instance, ipmiExecutor);

        // Preflight test should fallback to LAN IPMI and succeed
        var testRes = await client.TestConnectionAsync("https://192.168.1.9", "admin", "calvin");
        Assert.True(testRes.Success);
        Assert.Equal("On", testRes.PowerState);
        Assert.Contains("IPMI-over-LAN", testRes.Message);

        // Vitals should fallback to LAN IPMI
        var vitals = await client.GetVitalsAsync("https://192.168.1.9", "admin", "calvin");
        Assert.Equal("On", vitals.PowerState);
        Assert.Equal("PowerEdge R720xd", vitals.Model);

        // Power reset should fallback to LAN IPMI
        var resetRes = await client.ResetSystemAsync("https://192.168.1.9", "admin", "calvin", "PowerCycle");
        Assert.True(resetRes.Success);
        Assert.Contains("Cycle", resetRes.Message);
    }

    [Fact]
    public async Task AgentIpmiExecutor_FanControl_ManualAndAuto_DispatchesCorrectRawCommands()
    {
        var hostId = Guid.NewGuid();
        var connManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var mockSocket = new MockWebSocket();
        connManager.Register(hostId, "pve-node", mockSocket, "localhost", "https");

        var cmdExecutor = new MockCommandExecutor();
        var executor = new AgentIpmiExecutor(cmdExecutor, connManager, NullLogger<AgentIpmiExecutor>.Instance);

        // 1. Manual Fan Control with 37% default (0x25)
        var manualRes = await executor.SetFanControlAsync(hostId, "Manual", 37);
        Assert.True(manualRes.Success);
        Assert.Equal("Manual", manualRes.Mode);
        Assert.Equal(37, manualRes.Percentage);

        // Verify two commands dispatched: raw 0x30 0x30 0x01 0x00, then raw 0x30 0x30 0x02 0xff 0x25
        Assert.Equal(2, cmdExecutor.DispatchedCommands.Count);
        Assert.Equal(new[] { "raw", "0x30", "0x30", "0x01", "0x00" }, cmdExecutor.DispatchedCommands[0].Args);
        Assert.Equal(new[] { "raw", "0x30", "0x30", "0x02", "0xff", "0x25" }, cmdExecutor.DispatchedCommands[1].Args);

        // 2. Restore Automatic Fan Control
        cmdExecutor.DispatchedCommands.Clear();
        var autoRes = await executor.SetFanControlAsync(hostId, "Auto", null);
        Assert.True(autoRes.Success);
        Assert.Equal("Auto", autoRes.Mode);

        // Verify single command dispatched: raw 0x30 0x30 0x01 0x01
        Assert.Single(cmdExecutor.DispatchedCommands);
        Assert.Equal(new[] { "raw", "0x30", "0x30", "0x01", "0x01" }, cmdExecutor.DispatchedCommands[0].Args);
    }

    [Fact]
    public async Task AgentIpmiExecutor_ChassisIdentify_And_BootOverride_DispatchesCorrectCommands()
    {
        var hostId = Guid.NewGuid();
        var connManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var mockSocket = new MockWebSocket();
        connManager.Register(hostId, "pve-node", mockSocket, "localhost", "https");

        var cmdExecutor = new MockCommandExecutor();
        var executor = new AgentIpmiExecutor(cmdExecutor, connManager, NullLogger<AgentIpmiExecutor>.Instance);

        // Chassis Identify Blink
        var idRes = await executor.SetChassisIdentifyAsync(hostId, "Blink", 15);
        Assert.True(idRes.Success);
        Assert.Equal("Blink", idRes.State);
        Assert.Equal(new[] { "chassis", "identify", "15" }, cmdExecutor.DispatchedCommands[0].Args);

        // Chassis Identify Off
        cmdExecutor.DispatchedCommands.Clear();
        var idOffRes = await executor.SetChassisIdentifyAsync(hostId, "Off", 0);
        Assert.True(idOffRes.Success);
        Assert.Equal(new[] { "chassis", "identify", "0" }, cmdExecutor.DispatchedCommands[0].Args);

        // Boot Override BIOS
        cmdExecutor.DispatchedCommands.Clear();
        var bootRes = await executor.SetBootOverrideAsync(hostId, "BiosSetup");
        Assert.True(bootRes.Success);
        Assert.Equal(new[] { "chassis", "bootdev", "bios" }, cmdExecutor.DispatchedCommands[0].Args);
    }

    private class MockLanIpmiExecutor : AgentIpmiExecutor
    {
        public MockLanIpmiExecutor(IAgentCommandExecutor cmdExecutor, AgentConnectionManager connManager)
            : base(cmdExecutor, connManager, NullLogger<AgentIpmiExecutor>.Instance) { }

        public override Task<(bool Success, string StandardOutput, string StandardError, string? ErrorMessage)> ExecuteLanplusCommandAsync(
            string hostOrIp, string username, string password, string[] ipmiArgs, CancellationToken ct = default)
        {
            if (ipmiArgs.SequenceEqual(new[] { "chassis", "power", "status" }))
                return Task.FromResult((true, "Chassis Power is on", "", (string?)null));
            if (ipmiArgs.SequenceEqual(new[] { "bmc", "info" }))
                return Task.FromResult((true, "Product Name : PowerEdge R720xd\nDevice Available : yes", "", (string?)null));
            if (ipmiArgs.SequenceEqual(new[] { "fru" }))
                return Task.FromResult((true, "Product Serial : 9ABCD88", "", (string?)null));
            if (ipmiArgs.SequenceEqual(new[] { "sensor" }))
                return Task.FromResult((true, "Temp | 38.0 | degrees C | ok", "", (string?)null));
            if (ipmiArgs.SequenceEqual(new[] { "chassis", "power", "cycle" }))
                return Task.FromResult((true, "Chassis Power Control: Cycle", "", (string?)null));

            return Task.FromResult((true, "", "", (string?)null));
        }
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    private class MockWebSocket : WebSocket
    {
        private WebSocketState _state;
        public MockWebSocket(WebSocketState state = WebSocketState.Open) => _state = state;
        public override WebSocketState State => _state;
        public override void Abort() => _state = WebSocketState.Aborted;
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketCloseStatus.NormalClosure == closeStatus ? WebSocketState.Closed : WebSocketState.Aborted;
            return Task.CompletedTask;
        }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }
        public override void Dispose() { }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            => Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public override string? CloseStatusDescription => null;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? SubProtocol => null;
    }

    private class MockHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public MockHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }
}
