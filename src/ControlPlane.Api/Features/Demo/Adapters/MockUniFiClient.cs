using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.UniFi;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockUniFiClient : IUniFiClient
{
    public Task<bool> LoginAsync(string controllerUrl, string username, string password, CancellationToken ct = default) => Task.FromResult(true);

    public Task<UniFiTestResultDto> TestConnectionAsync(string controllerUrl, string? username, string? password, string site = "default", string? apiKey = null, CancellationToken ct = default)
    {
        return Task.FromResult(new UniFiTestResultDto(true, "8.0.28", 2, 18, new List<string> { "default" }, 12, "Connected successfully (Simulated)"));
    }

    public Task<List<UniFiDeviceDto>> GetDevicesAsync(string controllerUrl, string? username, string? password, string site = "default", string? apiKey = null, CancellationToken ct = default)
    {
        var ports = new List<UniFiPortDto>();
        for (int i = 1; i <= 24; i++)
        {
            var isPoE = i <= 16;
            var isUp = i is 1 or 2 or 3 or 4 or 8 or 10 or 24;
            ports.Add(new UniFiPortDto(
                PortIdx: i,
                Name: i switch
                {
                    1 => "k8s-cp-01",
                    2 => "k8s-worker-01",
                    3 => "k8s-worker-02",
                    4 => "pve-node-01 (iDRAC)",
                    8 => "U6-Pro-AP",
                    10 => "Storage-NAS-01",
                    24 => "Uplink-UDM-Pro",
                    _ => $"Port {i}"
                },
                Up: isUp,
                SpeedMbps: isUp ? 1000 : null,
                PoeMode: isPoE ? "auto" : "off",
                PoePowerWatts: isPoE && isUp ? (i == 8 ? 8.4 : 4.2) : 0,
                PoeVoltage: isPoE && isUp ? 53.5 : 0,
                PoeCurrent: isPoE && isUp ? (i == 8 ? 0.16 : 0.08) : 0
            ));
        }

        var devices = new List<UniFiDeviceDto>
        {
            new(
                Mac: "74:ac:b9:11:22:33",
                Name: "UDM-Pro",
                Model: "UDMPRO",
                Type: "udm",
                Ip: "192.168.1.1",
                State: "connected",
                Version: "3.2.12",
                UpgradeAvailable: false,
                UptimeSeconds: 2592000,
                Temperature: 45.2,
                Ports: new List<UniFiPortDto>()
            ),
            new(
                Mac: "74:ac:b9:44:55:66",
                Name: "USW-Pro-24-PoE",
                Model: "US24P250",
                Type: "usw",
                Ip: "192.168.1.2",
                State: "connected",
                Version: "6.6.61",
                UpgradeAvailable: false,
                UptimeSeconds: 1840000,
                Temperature: 51.0,
                Ports: ports
            )
        };

        return Task.FromResult(devices);
    }

    public Task<bool> RestartDeviceAsync(string controllerUrl, string? username, string? password, string deviceMac, string site = "default", string? apiKey = null, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> UpgradeDeviceAsync(string controllerUrl, string? username, string? password, string deviceMac, string site = "default", string? apiKey = null, CancellationToken ct = default) => Task.FromResult(true);

    public Task<UniFiBounceResult> CyclePoEPortAsync(string controllerUrl, string? username, string? password, string switchMac, int portNumber, string site = "default", int delaySeconds = 5, string? apiKey = null, CancellationToken ct = default)
    {
        return Task.FromResult(new UniFiBounceResult(true, $"Power-cycled PoE port {portNumber} on switch {switchMac} successfully (Demo Mode)", switchMac, portNumber));
    }

    public Task<List<UniFiMacLease>> GetActiveClientsAsync(string controllerUrl, string? username, string? password, string site = "default", string? apiKey = null, CancellationToken ct = default)
    {
        return Task.FromResult(new List<UniFiMacLease>
        {
            new("00:11:22:33:44:55", "192.168.1.10", "k8s-cp-01", DateTimeOffset.UtcNow),
            new("00:11:22:33:44:56", "192.168.1.11", "k8s-worker-01", DateTimeOffset.UtcNow),
            new("00:11:22:33:44:57", "192.168.1.12", "k8s-worker-02", DateTimeOffset.UtcNow),
            new("00:11:22:33:44:58", "192.168.1.20", "pve-node-01", DateTimeOffset.UtcNow),
            new("00:11:22:33:44:59", "192.168.1.25", "idrac-pve-01", DateTimeOffset.UtcNow),
            new("aa:bb:cc:11:22:33", "192.168.80.235", "roborock-vacuum-a27", DateTimeOffset.UtcNow)
        });
    }
}
