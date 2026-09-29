using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.OPNsense;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockOpnsenseClient : IOPNsenseClient
{
    public Task<OPNsenseTestResultDto> TestConnectionAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new OPNsenseTestResultDto(true, "opnsense.homelab.local", "24.7_1", "online", 15, null));
    }

    public Task<OPNsenseTelemetryResponse> GetTelemetryAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new OPNsenseTelemetryResponse(
            Hostname: "opnsense.homelab.local",
            Version: "24.7_1",
            Status: "online",
            Gateways: GetGatewaysAsync(baseUrl, apiKey, apiSecret, allowSelfSigned, ct).Result,
            Interfaces: GetInterfacesAsync(baseUrl, apiKey, apiSecret, allowSelfSigned, ct).Result,
            Services: GetServicesAsync(baseUrl, apiKey, apiSecret, allowSelfSigned, ct).Result,
            Timestamp: DateTimeOffset.UtcNow
        ));
    }

    public Task<List<OPNsenseGatewayStatus>> GetGatewaysAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new List<OPNsenseGatewayStatus>
        {
            new("WAN_DHCP", "igb0", "online", 8.4, 0.0, "203.0.113.1")
        });
    }

    public Task<List<OPNsenseInterfaceInfo>> GetInterfacesAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new List<OPNsenseInterfaceInfo>
        {
            new("WAN", "igb0", "198.51.100.1/24", "up", "1000baseT <full-duplex>", "WAN", "a0:36:9f:11:22:33", 1500, true),
            new("WAN2", "igb1", "192.168.12.100/24", "up", "1000baseT <full-duplex>", "WAN2", "a0:36:9f:11:22:34", 1500, true),
            new("LAN", "igb2", "192.168.1.1/24", "up", "1000baseT <full-duplex>", "LAN", "a0:36:9f:11:22:35", 1500, true),
            new("TRUSTED", "vlan0.10", "10.10.10.1/24", "up", "10Gbase-T", "TRUSTED", "a0:36:9f:11:22:36", 1500, true),
            new("SERVERS", "vlan0.20", "10.10.20.1/24", "up", "10Gbase-T", "SERVERS", "a0:36:9f:11:22:37", 1500, true),
            new("KUBERNETES", "vlan0.30", "10.10.30.1/24", "up", "10Gbase-T", "KUBERNETES", "a0:36:9f:11:22:38", 1500, true),
            new("IOT", "vlan0.40", "10.10.40.1/24", "up", "1000baseT <full-duplex>", "IOT", "a0:36:9f:11:22:39", 1500, true),
            new("CAMERA", "vlan0.50", "10.10.50.1/24", "up", "1000baseT <full-duplex>", "CAMERA", "a0:36:9f:11:22:3a", 1500, true),
            new("GUEST", "vlan0.60", "10.10.60.1/24", "up", "1000baseT <full-duplex>", "GUEST", "a0:36:9f:11:22:3b", 1500, true)
        });
    }

    public Task<List<OPNsenseServiceItem>> GetServicesAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new List<OPNsenseServiceItem>
        {
            new("unbound", "unbound", "Unbound DNS Resolver", true, true),
            new("dhcpd", "dhcpd", "ISC DHCP Daemon", true, true),
            new("sshd", "sshd", "OpenSSH Server", true, true),
            new("wireguard", "wireguard", "WireGuard VPN", true, true),
            new("suricata", "suricata", "Suricata Intrusion Detection System", true, true),
            new("haproxy", "haproxy", "HAProxy Load Balancer & Ingress", true, true),
            new("acme", "acme", "ACME Certificate Client", true, true)
        });
    }

    public Task<OPNsenseServiceActionResult> RestartServiceAsync(string baseUrl, string apiKey, string apiSecret, string serviceName, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new OPNsenseServiceActionResult(true, $"Service '{serviceName}' restarted successfully (Demo Mode)"));
    }

    public Task<List<OPNsenseDhcpLease>> GetDhcpLeasesAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new List<OPNsenseDhcpLease>
        {
            new("10.10.40.120", "dd:ee:ff:44:55:66", "smart-plug-01", "2026/09/28 00:00:00", "2026/09/29 00:00:00", "active"),
            new("10.10.40.121", "dd:ee:ff:44:55:67", "hue-bridge", "2026/09/28 00:00:00", "2026/09/29 00:00:00", "active"),
            new("10.10.50.122", "dd:ee:ff:44:55:68", "living-room-camera", "2026/09/28 00:00:00", "2026/09/29 00:00:00", "active"),
            new("192.168.1.150", "00:11:22:99:88:77", "epson-printer", "2026/09/28 00:00:00", "2026/09/29 00:00:00", "active")
        });
    }

    public Task<OPNsenseFirmwareInfo> GetFirmwareStatusAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new OPNsenseFirmwareInfo("26.7.4_1-amd64", "updates-available", 3, ["os-haproxy", "os-acme-client", "curl"], DateTime.UtcNow.AddHours(-2).ToString("o"), false, "3 package updates available.", "upgrade"));
    }

    public Task<OPNsenseFirmwareInfo> CheckFirmwareUpdatesAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new OPNsenseFirmwareInfo("26.7.4_1-amd64", "updates-available", 3, ["os-haproxy", "os-acme-client", "curl"], DateTime.UtcNow.ToString("o"), false, "3 package updates available.", "upgrade"));
    }

    public Task<OPNsenseVitalsInfo> GetVitalsAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new OPNsenseVitalsInfo(
            CpuLoadAverage: [0.17, 0.19, 0.20],
            MemoryTotalBytes: 6092L * 1024 * 1024,
            MemoryUsedBytes: 1598L * 1024 * 1024,
            MemoryUsagePercent: 26.23,
            DiskTotalBytes: 204L * 1024 * 1024 * 1024,
            DiskUsedBytes: (long)(204L * 1024 * 1024 * 1024 * 0.0118),
            DiskUsagePercent: 1.18,
            UptimeSeconds: 1151570,
            UptimeFormatted: "13 days, 07:52:50",
            Temperatures: new Dictionary<string, double> { { "CPU Core 0", 38.5 }, { "CPU Core 1", 39.0 } },
            LastConfigChange: "Fri Sep 25 21:21:34 EDT 2026"
        ));
    }

    public Task<OPNsenseHAProxyStatus> GetHAProxyStatusAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new OPNsenseHAProxyStatus(
            IsInstalled: true,
            Running: true,
            Frontends: ["HTTP_FrontEnd (80)", "HTTPS_FrontEnd (443)"],
            Backends:
            [
                new("k8s_ingress", "10.10.30.50", 443, "UP", 24, 2),
                new("plex_media", "10.10.20.15", 32400, "UP", 3, 1),
                new("home_assistant", "10.10.20.25", 8123, "UP", 8, 3),
                new("vaultwarden", "10.10.20.30", 8080, "UP", 1, 1)
            ]
        ));
    }

    public Task<OPNsenseAcmeStatus> GetAcmeStatusAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new OPNsenseAcmeStatus(
            IsInstalled: true,
            Certificates:
            [
                new("cert-wildcard", "*.internal.homelab.local", "Homelab Wildcard SSL", ["*.internal.homelab.local", "internal.homelab.local"], "valid", DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(60), 60, DateTimeOffset.UtcNow.AddDays(-30)),
                new("cert-gateway", "opnsense.internal.homelab.local", "OPNsense Management WebGUI", ["opnsense.internal.homelab.local"], "valid", DateTimeOffset.UtcNow.AddDays(-45), DateTimeOffset.UtcNow.AddDays(45), 45, DateTimeOffset.UtcNow.AddDays(-45))
            ]
        ));
    }

    public Task<OPNsenseSecurityStatus> GetSecurityAlertsAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new OPNsenseSecurityStatus(
            IsInstalled: true,
            Running: true,
            Model: "Suricata IPS Engine",
            ThreatCount24h: 14,
            Alerts:
            [
                new(DateTimeOffset.UtcNow.AddMinutes(-12).ToString("yyyy-MM-dd HH:mm:ss"), "ET SCAN Suspicious Inbound Port 22 Scan", "Network Scan", "Medium", "198.51.100.44", 51234, "198.51.100.1", 22, "TCP", "drop"),
                new(DateTimeOffset.UtcNow.AddMinutes(-34).ToString("yyyy-MM-dd HH:mm:ss"), "GPL ATTACK_RESPONSE id check returned root", "Attempted Admin Privilege Gain", "High", "203.0.113.89", 443, "10.10.30.50", 54321, "TCP", "alert"),
                new(DateTimeOffset.UtcNow.AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss"), "ET POLICY DNS Query to suspicious .top TLD", "Policy Violation", "Low", "10.10.40.120", 53211, "1.1.1.1", 53, "UDP", "alert"),
                new(DateTimeOffset.UtcNow.AddHours(-3).ToString("yyyy-MM-dd HH:mm:ss"), "ET DOS Possible NTP DDoS Amplification Attempt", "Denial of Service", "High", "198.51.100.99", 123, "198.51.100.1", 123, "UDP", "drop")
            ]
        ));
    }

    public Task<OPNsenseFirewallStats> GetFirewallStatsAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new OPNsenseFirewallStats(
            TotalFilterRules: 42,
            TotalAliases: 18,
            PfStatesCurrent: 1420,
            PfStatesMax: 600000,
            PfStatesPercent: 0.24,
            RecentBlockedPacketsCount: 52
        ));
    }

    public Task<List<OPNsenseArpEntry>> GetArpTableAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new List<OPNsenseArpEntry>
        {
            new("192.168.1.1", "a0:36:9f:11:22:35", "igb2", "opnsense.internal.homelab.local", "Deciso B.V.", false),
            new("192.168.1.10", "bc:24:11:88:99:aa", "igb2", "unifi-switch-core", "Ubiquiti Inc.", false),
            new("10.10.20.15", "00:0c:29:ab:cd:ef", "vlan0.20", "storage-nas", "VMware, Inc.", false),
            new("10.10.30.50", "52:54:00:12:34:56", "vlan0.30", "k8s-node-01", "QEMU Virtual Machine", false),
            new("10.10.40.120", "dd:ee:ff:44:55:66", "vlan0.40", "smart-plug-01", "Espressif Inc.", false),
            new("10.10.40.121", "dd:ee:ff:44:55:67", "vlan0.40", "hue-bridge", "Signify Netherlands B.V.", false)
        });
    }
}
