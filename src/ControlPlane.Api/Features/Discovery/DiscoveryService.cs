using System.Net;
using System.Text.RegularExpressions;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Discovery;

public class DiscoveryService : IDiscoveryService
{
    private readonly ControlPlaneDbContext _db;
    private readonly IProxmoxClient _proxmoxClient;
    private readonly IProxmoxClientFactory? _proxmoxClientFactory;
    private readonly IKubernetesAdapter _kubernetesAdapter;
    private readonly IKubernetesClientFactory? _kubernetesClientFactory;
    private readonly HostService _hostService;
    private readonly ProxmoxOptions _fallbackProxmoxOptions;
    private readonly Features.Adapters.Config.IAdapterConfigService? _adapterConfigService;
    private readonly Features.Adapters.UniFi.IUniFiClientFactory? _unifiClientFactory;
    private readonly Features.Adapters.OPNsense.IOPNsenseClientFactory? _opnsenseClientFactory;
    private readonly IHostCorrelationService? _hostCorrelationService;
    private readonly ILogger<DiscoveryService> _logger;

    public DiscoveryService(
        ControlPlaneDbContext db,
        IProxmoxClient proxmoxClient,
        IKubernetesAdapter kubernetesAdapter,
        HostService hostService,
        IOptions<ProxmoxOptions> proxmoxOptions,
        ILogger<DiscoveryService> logger,
        Features.Adapters.Config.IAdapterConfigService? adapterConfigService = null,
        IProxmoxClientFactory? proxmoxClientFactory = null,
        IKubernetesClientFactory? kubernetesClientFactory = null,
        Features.Adapters.UniFi.IUniFiClientFactory? unifiClientFactory = null,
        Features.Adapters.OPNsense.IOPNsenseClientFactory? opnsenseClientFactory = null,
        IHostCorrelationService? hostCorrelationService = null)
    {
        _db = db;
        _proxmoxClient = proxmoxClient;
        _kubernetesAdapter = kubernetesAdapter;
        _kubernetesClientFactory = kubernetesClientFactory;
        _hostService = hostService;
        _fallbackProxmoxOptions = proxmoxOptions.Value;
        _logger = logger;
        _adapterConfigService = adapterConfigService;
        _proxmoxClientFactory = proxmoxClientFactory;
        _unifiClientFactory = unifiClientFactory;
        _opnsenseClientFactory = opnsenseClientFactory;
        _hostCorrelationService = hostCorrelationService;
    }

    public async Task<DiscoveryScanResult> ScanAsync(
        bool includeProxmox = true,
        bool includeKubernetes = true,
        bool includeUniFi = true,
        bool includeOPNsense = true,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Beginning infrastructure service discovery (Proxmox: {Proxmox}, Kubernetes: {K8s}, UniFi: {UniFi}, OPNsense: {OPNsense})...",
            includeProxmox, includeKubernetes, includeUniFi, includeOPNsense);

        var candidates = new List<DiscoveredCandidateDto>();
        var errors = new List<string>();

        var existingHosts = await _db.Hosts.AsNoTracking().ToListAsync(ct);

        // 1. Scan Proxmox VE
        if (includeProxmox)
        {
            List<Features.Adapters.Config.ProxmoxInstanceDto> instances = new();
            if (_adapterConfigService != null)
            {
                try
                {
                    instances = await _adapterConfigService.GetProxmoxInstancesAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load Proxmox instances for discovery.");
                }
            }

            if (instances.Count > 0)
            {
                foreach (var inst in instances)
                {
                    var opts = await _adapterConfigService!.GetActiveProxmoxOptionsAsync(inst.Id, ct);
                    var client = _proxmoxClientFactory != null
                        ? await _proxmoxClientFactory.GetClientAsync(inst.Id, ct)
                        : _proxmoxClient;
                    await ScanSingleProxmoxInstanceAsync(inst.Id, opts, client, existingHosts, candidates, errors, ct);
                }
            }
            else
            {
                var pveOpts = _adapterConfigService != null
                    ? await _adapterConfigService.GetActiveProxmoxOptionsAsync(ct)
                    : _fallbackProxmoxOptions;

                if (string.IsNullOrWhiteSpace(pveOpts.BaseUrl) || string.IsNullOrWhiteSpace(pveOpts.ApiTokenId))
                {
                    errors.Add("Proxmox adapter is not configured (missing BaseUrl or ApiToken).");
                }
                else
                {
                    await ScanSingleProxmoxInstanceAsync("default", pveOpts, _proxmoxClient, existingHosts, candidates, errors, ct);
                }
            }
        }

        // 2. Scan Kubernetes Nodes
        if (includeKubernetes)
        {
            List<Features.Adapters.Config.KubernetesClusterDto> clusters = new();
            if (_adapterConfigService != null)
            {
                try
                {
                    clusters = await _adapterConfigService.GetKubernetesClustersAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to retrieve Kubernetes cluster configurations");
                }
            }

            if (clusters.Count > 0 && _kubernetesClientFactory != null)
            {
                foreach (var cluster in clusters)
                {
                    try
                    {
                        var adapter = await _kubernetesClientFactory.CreateAdapterAsync(cluster.Id, ct);
                        await ScanSingleKubernetesClusterAsync(cluster.Id, cluster.Name, adapter, existingHosts, candidates, errors, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error scanning Kubernetes cluster '{ClusterName}' ({ClusterId})", cluster.Name, cluster.Id);
                        errors.Add($"Kubernetes cluster '{cluster.Name}' scan failed: {ex.Message}");
                    }
                }
            }
            else
            {
                await ScanSingleKubernetesClusterAsync("default", "Default Cluster", _kubernetesAdapter, existingHosts, candidates, errors, ct);
            }
        }

        // 3. Scan UniFi Network (Devices & Clients)
        if (includeUniFi && _unifiClientFactory != null)
        {
            try
            {
                var unifiInstances = await _unifiClientFactory.ResolveAllAsync(ct);
                foreach (var (config, password, apiKey) in unifiInstances)
                {
                    await ScanSingleUniFiInstanceAsync(config, password, apiKey, existingHosts, candidates, errors, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve UniFi instances for discovery.");
            }
        }

        // 4. Scan OPNsense (Firewall Host & DHCP leases)
        if (includeOPNsense && _opnsenseClientFactory != null)
        {
            try
            {
                var opnsenseInstances = await _opnsenseClientFactory.ResolveAllAsync(ct);
                foreach (var (config, secret) in opnsenseInstances)
                {
                    await ScanSingleOPNsenseInstanceAsync(config, secret, existingHosts, candidates, errors, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve OPNsense instances for discovery.");
            }
        }

        var total = candidates.Count;
        var managed = candidates.Count(c => c.IsManaged);
        var unmanaged = total - managed;

        // Automatically synchronize correlations for existing managed hosts
        if (_hostCorrelationService != null)
        {
            try
            {
                var syncRes = await _hostCorrelationService.SyncHostCorrelationsAsync(ct);
                _logger.LogInformation("Discovery auto-synced correlations: {K8s} K8s nodes, {Pve} Proxmox hosts.",
                    syncRes.CorrelatedKubernetesNodes, syncRes.CorrelatedProxmoxHosts);
            }
            catch (Exception syncEx)
            {
                _logger.LogWarning(syncEx, "Failed to auto-sync correlations during discovery scan.");
            }
        }

        return new DiscoveryScanResult(
            Candidates: candidates,
            TotalDiscovered: total,
            AlreadyManaged: managed,
            UnmanagedCount: unmanaged,
            ScannedAt: DateTimeOffset.UtcNow,
            Errors: errors
        );
    }

    private async Task ScanSingleProxmoxInstanceAsync(
        string instanceId,
        ProxmoxOptions pveOpts,
        IProxmoxClient client,
        List<HostEntity> existingHosts,
        List<DiscoveredCandidateDto> candidates,
        List<string> errors,
        CancellationToken ct)
    {
        try
        {
            // 1a. Discover Proxmox hypervisor nodes
            try
            {
                var pveNodes = await client.ListNodesAsync(ct);
                string? defaultNodeIp = null;
                if (Uri.TryCreate(pveOpts.BaseUrl, UriKind.Absolute, out var pveUri))
                {
                    defaultNodeIp = pveUri.Host;
                    if (!IPAddress.TryParse(defaultNodeIp, out _))
                    {
                        try
                        {
                            var addresses = await Dns.GetHostAddressesAsync(defaultNodeIp, ct);
                            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                    ?? addresses.FirstOrDefault();
                            if (ipv4 != null)
                            {
                                defaultNodeIp = ipv4.ToString();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Could not resolve IP address for Proxmox base URL host {Host}", pveUri.Host);
                        }
                    }
                }

                foreach (var n in pveNodes)
                {
                    var matchedHost = existingHosts.FirstOrDefault(h =>
                        string.Equals(h.Hostname, n.Node, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrWhiteSpace(defaultNodeIp) && string.Equals(h.IpAddress, defaultNodeIp, StringComparison.OrdinalIgnoreCase))
                        || (h.Proxmox != null
                            && (string.IsNullOrEmpty(h.Proxmox.InstanceId) || string.Equals(h.Proxmox.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                            && string.Equals(h.Proxmox.Node, n.Node, StringComparison.OrdinalIgnoreCase)
                            && h.Proxmox.Vmid <= 0));

                    candidates.Add(new DiscoveredCandidateDto(
                        Id: $"pve:{instanceId}:node:{n.Node}",
                        Source: "Proxmox",
                        Name: n.Node,
                        IpAddress: matchedHost?.IpAddress ?? defaultNodeIp,
                        TargetType: "baremetal",
                        OsFamily: matchedHost?.OsFamily ?? "linux_debian",
                        Status: n.Status,
                        ProxmoxNode: n.Node,
                        ProxmoxVmid: null,
                        ProxmoxInstanceId: instanceId,
                        Roles: new List<string> { "hypervisor", "pve-host" },
                        IsManaged: matchedHost != null,
                        ExistingHostId: matchedHost?.Id,
                        ExistingHostname: matchedHost?.Hostname
                    ));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enumerate Proxmox nodes for instance '{InstanceId}'.", instanceId);
            }

            // 1b. Discover VMs and LXC containers
            var resources = await client.DiscoverClusterResourcesAsync(ct);
            _logger.LogInformation("Discovered {Count} VM/LXC resources from Proxmox instance '{InstanceId}'.", resources.Count, instanceId);

            if (resources.Count == 0)
            {
                var hasVmAudit = await client.HasVmAuditPermissionAsync(ct);
                if (!hasVmAudit)
                {
                    errors.Add($"Proxmox instance '{instanceId}' API token is connected, but lacks VM.Audit permissions (Proxmox returned 0 VMs).");
                }
            }

            foreach (var res in resources)
            {
                try
                {
                    var isLxc = string.Equals(res.Type, "lxc", StringComparison.OrdinalIgnoreCase);
                    var targetType = isLxc ? "proxmox_lxc" : "proxmox_vm";
                    var vmid = res.Vmid ?? 0;
                    var name = res.Name ?? $"vm-{vmid}";
                    var status = res.Status ?? "unknown";

                    string? ip = null;
                    string? osRaw = null;
                    if (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase) && vmid > 0)
                    {
                        try
                        {
                            ip = await client.TryGetGuestIpAddressAsync(res.Node, vmid, isLxc, ct);
                        }
                        catch (Exception ipEx)
                        {
                            _logger.LogDebug(ipEx, "Could not resolve IP address for guest {Node}/{Vmid} on instance '{InstanceId}'", res.Node, vmid, instanceId);
                        }

                        try
                        {
                            osRaw = await client.TryGetGuestOsTypeAsync(res.Node, vmid, isLxc, ct);
                        }
                        catch (Exception osEx)
                        {
                            _logger.LogDebug(osEx, "Could not resolve OS info for guest {Node}/{Vmid} on instance '{InstanceId}'", res.Node, vmid, instanceId);
                        }
                    }

                    var detectedOs = DetectOsFamily(osRaw, name);

                    // Match against existing hosts
                    var matchedHost = existingHosts.FirstOrDefault(h =>
                        (h.Proxmox != null
                            && (string.IsNullOrEmpty(h.Proxmox.InstanceId) || string.Equals(h.Proxmox.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                            && string.Equals(h.Proxmox.Node, res.Node, StringComparison.OrdinalIgnoreCase)
                            && h.Proxmox.Vmid == vmid)
                        || (!string.IsNullOrWhiteSpace(ip) && string.Equals(h.IpAddress, ip, StringComparison.OrdinalIgnoreCase))
                        || string.Equals(h.Hostname, name, StringComparison.OrdinalIgnoreCase));

                    var roles = new List<string> { isLxc ? "container" : "virtual-machine" };
                    if (detectedOs == "haos" || name.Contains("hass", StringComparison.OrdinalIgnoreCase) || name.Contains("homeassistant", StringComparison.OrdinalIgnoreCase))
                    {
                        roles.Add("home-assistant");
                    }

                    candidates.Add(new DiscoveredCandidateDto(
                        Id: $"pve:{instanceId}:{res.Node}:{vmid}",
                        Source: "Proxmox",
                        Name: name,
                        IpAddress: ip ?? matchedHost?.IpAddress,
                        TargetType: targetType,
                        OsFamily: matchedHost?.OsFamily ?? detectedOs,
                        Status: status,
                        ProxmoxNode: res.Node,
                        ProxmoxVmid: vmid > 0 ? vmid : null,
                        ProxmoxInstanceId: instanceId,
                        Roles: roles,
                        IsManaged: matchedHost != null,
                        ExistingHostId: matchedHost?.Id,
                        ExistingHostname: matchedHost?.Hostname
                    ));
                }
                catch (Exception candEx)
                {
                    _logger.LogWarning(candEx, "Error processing candidate resource {Id} ({Type}) on instance '{InstanceId}'", res.Id, res.Type, instanceId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning Proxmox instance '{InstanceId}' resources", instanceId);
            errors.Add($"Proxmox instance '{instanceId}' scan failed: {ex.Message}");
        }
    }

    private async Task ScanSingleKubernetesClusterAsync(
        string clusterId,
        string clusterName,
        IKubernetesAdapter adapter,
        List<HostEntity> existingHosts,
        List<DiscoveredCandidateDto> candidates,
        List<string> errors,
        CancellationToken ct)
    {
        try
        {
            var k8sNodes = await adapter.ListNodesAsync(ct);
            _logger.LogInformation("Discovered {Count} nodes from Kubernetes cluster '{ClusterName}' ({ClusterId}).", k8sNodes.Count, clusterName, clusterId);

            foreach (var kNode in k8sNodes)
            {
                // Match against existing hosts
                var matchedHost = existingHosts.FirstOrDefault(h =>
                    string.Equals(h.Hostname, kNode.Name, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(kNode.InternalIp) && string.Equals(h.IpAddress, kNode.InternalIp, StringComparison.OrdinalIgnoreCase)));

                // Also check if already in candidate list from Proxmox or another adapter
                var existingCandidate = candidates.FirstOrDefault(c =>
                    string.Equals(c.Name, kNode.Name, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(kNode.InternalIp) && string.Equals(c.IpAddress, kNode.InternalIp, StringComparison.OrdinalIgnoreCase)));

                if (existingCandidate != null)
                {
                    // Merge Kubernetes metadata into existing candidate
                    var index = candidates.IndexOf(existingCandidate);
                    var mergedRoles = new HashSet<string>(existingCandidate.Roles ?? new List<string>());
                    foreach (var r in kNode.Roles) mergedRoles.Add($"k8s-{r}");

                    candidates[index] = existingCandidate with
                    {
                        K8sClusterId = clusterId,
                        K8sNodeName = kNode.Name,
                        Roles = mergedRoles.ToList(),
                        IpAddress = existingCandidate.IpAddress ?? kNode.InternalIp
                    };
                }
                else
                {
                    var roles = kNode.Roles.Select(r => $"k8s-{r}").ToList();
                    candidates.Add(new DiscoveredCandidateDto(
                        Id: $"k8s:{clusterId}:{kNode.Name}",
                        Source: "Kubernetes",
                        Name: kNode.Name,
                        IpAddress: kNode.InternalIp ?? matchedHost?.IpAddress,
                        TargetType: matchedHost?.TargetType ?? "kubernetes_node",
                        OsFamily: matchedHost?.OsFamily ?? DetectOsFamily(kNode.OsImage, kNode.Name),
                        Status: kNode.IsReady ? "Ready" : "NotReady",
                        ProxmoxNode: null,
                        ProxmoxVmid: null,
                        ProxmoxInstanceId: null,
                        K8sClusterId: clusterId,
                        K8sNodeName: kNode.Name,
                        Roles: roles,
                        IsManaged: matchedHost != null,
                        ExistingHostId: matchedHost?.Id,
                        ExistingHostname: matchedHost?.Hostname
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning Kubernetes cluster '{ClusterName}' ({ClusterId}) nodes", clusterName, clusterId);
            errors.Add($"Kubernetes cluster '{clusterName}' scan failed: {ex.Message}");
        }
    }

    private async Task ScanSingleUniFiInstanceAsync(
        Features.Adapters.Config.UniFiStoredInstance config,
        string? password,
        string? apiKey,
        List<HostEntity> existingHosts,
        List<DiscoveredCandidateDto> candidates,
        List<string> errors,
        CancellationToken ct)
    {
        var client = _unifiClientFactory!.GetClient();

        // 3a. Discover UniFi infrastructure devices (switches, access points, gateways, dream machines)
        try
        {
            var devices = await client.GetDevicesAsync(
                config.ControllerUrl, config.Username, password, config.Site, apiKey, ct);

            foreach (var dev in devices)
            {
                if (string.IsNullOrWhiteSpace(dev.Ip) || dev.Ip == "0.0.0.0") continue;

                var cleanMac = (dev.Mac ?? string.Empty).Replace(":", "").Replace("-", "").ToLowerInvariant();

                var matchedHost = existingHosts.FirstOrDefault(h =>
                    string.Equals(h.IpAddress, dev.Ip, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(dev.Name) && string.Equals(h.Hostname, dev.Name, StringComparison.OrdinalIgnoreCase)) ||
                    (h.NetworkPort != null && !string.IsNullOrWhiteSpace(dev.Mac) &&
                     string.Equals(h.NetworkPort.SwitchMac, dev.Mac, StringComparison.OrdinalIgnoreCase)));

                if (candidates.Any(c => c.IpAddress == dev.Ip)) continue;

                var devType = (dev.Type ?? string.Empty).ToLowerInvariant();
                var targetType = devType switch
                {
                    "usw" => "switch",
                    "uap" => "access_point",
                    "ugw" or "udm" or "uxg" => "gateway",
                    _ => "network_device"
                };

                var roles = new List<string> { "network-device" };
                if (!string.IsNullOrWhiteSpace(dev.Type))
                {
                    roles.Add($"unifi-{dev.Type.ToLowerInvariant()}");
                }
                if (!string.IsNullOrWhiteSpace(dev.Model))
                {
                    roles.Add(dev.Model);
                }

                var candidateName = !string.IsNullOrWhiteSpace(dev.Name)
                    ? dev.Name
                    : (!string.IsNullOrWhiteSpace(dev.Model)
                        ? $"{dev.Model.ToLowerInvariant().Replace(' ', '-')}-{cleanMac[..Math.Min(4, cleanMac.Length)]}"
                        : $"unifi-{cleanMac[..Math.Min(6, cleanMac.Length)]}");

                candidates.Add(new DiscoveredCandidateDto(
                    Id: $"unifi:dev:{config.Id}:{cleanMac}",
                    Source: "UniFi",
                    Name: candidateName,
                    IpAddress: dev.Ip,
                    TargetType: targetType,
                    OsFamily: "unifi_os",
                    Status: dev.State ?? "online",
                    ProxmoxNode: null,
                    ProxmoxVmid: null,
                    ProxmoxInstanceId: null,
                    K8sClusterId: null,
                    K8sNodeName: null,
                    UnifiSwitchMac: devType == "usw" ? dev.Mac : null,
                    UnifiSwitchPort: null,
                    Roles: roles,
                    IsManaged: matchedHost != null,
                    ExistingHostId: matchedHost?.Id,
                    ExistingHostname: matchedHost?.Hostname
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scan devices from UniFi instance '{InstanceId}'", config.Id);
            errors.Add($"Failed to scan UniFi devices from '{config.Name}': {ex.Message}");
        }

        // 3b. Discover UniFi connected clients
        try
        {
            var clients = await client.GetActiveClientsAsync(
                config.ControllerUrl, config.Username, password, config.Site, apiKey, ct);

            foreach (var clientLease in clients)
            {
                if (string.IsNullOrWhiteSpace(clientLease.Ip) || clientLease.Ip == "0.0.0.0") continue;

                var matchedHost = existingHosts.FirstOrDefault(h =>
                    string.Equals(h.IpAddress, clientLease.Ip, StringComparison.OrdinalIgnoreCase) ||
                    (h.NetworkPort != null && string.Equals(h.NetworkPort.SwitchMac, clientLease.Mac, StringComparison.OrdinalIgnoreCase)));

                if (candidates.Any(c => c.IpAddress == clientLease.Ip)) continue;

                var cleanMac = clientLease.Mac.Replace(":", "").Replace("-", "").ToLowerInvariant();
                candidates.Add(new DiscoveredCandidateDto(
                    Id: $"unifi:{config.Id}:{cleanMac}",
                    Source: "UniFi",
                    Name: string.IsNullOrWhiteSpace(clientLease.Hostname) ? $"client-{cleanMac[..Math.Min(6, cleanMac.Length)]}" : clientLease.Hostname,
                    IpAddress: clientLease.Ip,
                    TargetType: "baremetal",
                    OsFamily: "linux_debian",
                    Status: "online",
                    ProxmoxNode: null,
                    ProxmoxVmid: null,
                    ProxmoxInstanceId: null,
                    K8sClusterId: null,
                    K8sNodeName: null,
                    UnifiSwitchMac: null,
                    UnifiSwitchPort: null,
                    Roles: new List<string> { "network-client" },
                    IsManaged: matchedHost != null,
                    ExistingHostId: matchedHost?.Id,
                    ExistingHostname: matchedHost?.Hostname
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scan clients from UniFi instance '{InstanceId}'", config.Id);
            errors.Add($"Failed to scan UniFi instance '{config.Name}': {ex.Message}");
        }
    }

    private async Task ScanSingleOPNsenseInstanceAsync(
        Features.Adapters.Config.OPNsenseStoredInstance config,
        string secret,
        List<HostEntity> existingHosts,
        List<DiscoveredCandidateDto> candidates,
        List<string> errors,
        CancellationToken ct)
    {
        var client = _opnsenseClientFactory!.GetClient();

        // 4a. Discover OPNsense firewall machine itself
        try
        {
            string? opnsenseIp = null;
            if (Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var opnUri))
            {
                opnsenseIp = opnUri.Host;
                if (!IPAddress.TryParse(opnsenseIp, out _))
                {
                    try
                    {
                        var addresses = await Dns.GetHostAddressesAsync(opnsenseIp, ct);
                        var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                ?? addresses.FirstOrDefault();
                        if (ipv4 != null)
                        {
                            opnsenseIp = ipv4.ToString();
                        }
                    }
                    catch (Exception dnsEx)
                    {
                        _logger.LogDebug(dnsEx, "Could not resolve IP address for OPNsense base URL host {Host}", opnUri.Host);
                    }
                }
            }

            string? firewallHostname = null;
            string firewallStatus = "online";
            string? firmwareVersion = null;

            try
            {
                var telemetry = await client.GetTelemetryAsync(
                    config.BaseUrl, config.ApiKey, secret, config.AllowSelfSignedCert, ct);
                if (telemetry != null)
                {
                    firewallHostname = telemetry.Hostname;
                    if (!string.IsNullOrWhiteSpace(telemetry.Status))
                    {
                        firewallStatus = telemetry.Status;
                    }
                    firmwareVersion = telemetry.Version;
                }
            }
            catch (Exception telemEx)
            {
                _logger.LogDebug(telemEx, "Could not retrieve telemetry for OPNsense instance '{InstanceId}', falling back to test connection", config.Id);
                try
                {
                    var testRes = await client.TestConnectionAsync(
                        config.BaseUrl, config.ApiKey, secret, config.AllowSelfSignedCert, ct);
                    if (testRes != null)
                    {
                        firewallHostname = testRes.Hostname;
                        firmwareVersion = testRes.Version;
                        if (!string.IsNullOrWhiteSpace(testRes.Status))
                        {
                            firewallStatus = testRes.Status;
                        }
                    }
                }
                catch
                {
                    // Fallback failed, continue with basic info
                }
            }

            var candidateName = !string.IsNullOrWhiteSpace(firewallHostname)
                ? firewallHostname
                : (!string.IsNullOrWhiteSpace(config.Name)
                    ? config.Name.ToLowerInvariant().Replace(' ', '-')
                    : $"opnsense-{config.Id}");

            var matchedHost = existingHosts.FirstOrDefault(h =>
                (!string.IsNullOrWhiteSpace(opnsenseIp) && string.Equals(h.IpAddress, opnsenseIp, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(h.Hostname, candidateName, StringComparison.OrdinalIgnoreCase));

            var roles = new List<string> { "firewall", "gateway", "opnsense" };
            if (!string.IsNullOrWhiteSpace(firmwareVersion))
            {
                roles.Add($"opnsense-{firmwareVersion}");
            }

            if (!candidates.Any(c => c.Id == $"opnsense:host:{config.Id}" || (!string.IsNullOrWhiteSpace(opnsenseIp) && c.IpAddress == opnsenseIp)))
            {
                candidates.Add(new DiscoveredCandidateDto(
                    Id: $"opnsense:host:{config.Id}",
                    Source: "OPNsense",
                    Name: candidateName,
                    IpAddress: opnsenseIp,
                    TargetType: "firewall",
                    OsFamily: "freebsd",
                    Status: firewallStatus,
                    ProxmoxNode: null,
                    ProxmoxVmid: null,
                    ProxmoxInstanceId: null,
                    K8sClusterId: null,
                    K8sNodeName: null,
                    UnifiSwitchMac: null,
                    UnifiSwitchPort: null,
                    Roles: roles,
                    IsManaged: matchedHost != null,
                    ExistingHostId: matchedHost?.Id,
                    ExistingHostname: matchedHost?.Hostname
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover firewall host for OPNsense instance '{InstanceId}'", config.Id);
            errors.Add($"Failed to discover OPNsense firewall host '{config.Name}': {ex.Message}");
        }

        // 4b. Discover OPNsense DHCP leases
        try
        {
            var leases = await client.GetDhcpLeasesAsync(
                config.BaseUrl, config.ApiKey, secret, config.AllowSelfSignedCert, ct);

            foreach (var lease in leases)
            {
                if (string.IsNullOrWhiteSpace(lease.Ip) || lease.Ip == "0.0.0.0") continue;

                var matchedHost = existingHosts.FirstOrDefault(h =>
                    string.Equals(h.IpAddress, lease.Ip, StringComparison.OrdinalIgnoreCase) ||
                    (h.NetworkPort != null && !string.IsNullOrWhiteSpace(lease.Mac) &&
                     string.Equals(h.NetworkPort.SwitchMac, lease.Mac, StringComparison.OrdinalIgnoreCase)));

                if (candidates.Any(c => c.IpAddress == lease.Ip)) continue;

                var cleanMac = (lease.Mac ?? string.Empty).Replace(":", "").Replace("-", "").ToLowerInvariant();
                var candidateName = !string.IsNullOrWhiteSpace(lease.Hostname)
                    ? lease.Hostname
                    : (!string.IsNullOrWhiteSpace(cleanMac) ? $"dhcp-{cleanMac[..Math.Min(6, cleanMac.Length)]}" : $"host-{lease.Ip.Replace('.', '-')}");

                candidates.Add(new DiscoveredCandidateDto(
                    Id: $"opnsense:{config.Id}:{cleanMac}",
                    Source: "OPNsense",
                    Name: candidateName,
                    IpAddress: lease.Ip,
                    TargetType: "baremetal",
                    OsFamily: "linux_debian",
                    Status: lease.Status ?? "active",
                    ProxmoxNode: null,
                    ProxmoxVmid: null,
                    ProxmoxInstanceId: null,
                    K8sClusterId: null,
                    K8sNodeName: null,
                    UnifiSwitchMac: null,
                    UnifiSwitchPort: null,
                    Roles: new List<string> { "dhcp-lease" },
                    IsManaged: matchedHost != null,
                    ExistingHostId: matchedHost?.Id,
                    ExistingHostname: matchedHost?.Hostname
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scan DHCP leases from OPNsense instance '{InstanceId}'", config.Id);
            errors.Add($"Failed to scan OPNsense instance '{config.Name}': {ex.Message}");
        }
    }

    public async Task<ImportCandidateResponse> ImportCandidateAsync(ImportCandidateRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new ImportCandidateResponse(false, null, null, "Candidate hostname/name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.IpAddress))
        {
            return new ImportCandidateResponse(false, null, null, "A valid IP address is required to import into host inventory.");
        }

        var cleanIp = request.IpAddress.Trim();
        if (!IPAddress.TryParse(cleanIp, out _))
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(cleanIp, ct);
                var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        ?? addresses.FirstOrDefault();
                if (ipv4 != null)
                {
                    cleanIp = ipv4.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not resolve hostname {Hostname} to an IP address", cleanIp);
            }
        }

        var hostname = request.Name.Trim();
        var friendlyName = string.IsNullOrWhiteSpace(request.FriendlyName) ? null : request.FriendlyName.Trim();

        // If hostname is not a valid DNS hostname (e.g. contains spaces, uppercase or unsupported characters),
        // save the original candidate name as FriendlyName (if not already set) and slugify the hostname.
        if (!HostValidators.IsValidHostname(hostname))
        {
            friendlyName ??= hostname;
            var sanitized = Regex.Replace(hostname.ToLowerInvariant().Replace(' ', '-').Replace('_', '-'), @"[^a-z0-9\-.]", "");
            sanitized = Regex.Replace(sanitized, @"\-+", "-").Trim('-');
            if (HostValidators.IsValidHostname(sanitized))
            {
                hostname = sanitized;
            }
        }

        var createRequest = new CreateHostRequest(
            Hostname: hostname,
            FriendlyName: friendlyName,
            IpAddress: cleanIp,
            OsFamily: string.IsNullOrWhiteSpace(request.OsFamily) ? "linux_debian" : request.OsFamily.Trim(),
            TargetType: string.IsNullOrWhiteSpace(request.TargetType) ? "baremetal" : request.TargetType.Trim(),
            ProxmoxNode: string.IsNullOrWhiteSpace(request.ProxmoxNode) ? null : request.ProxmoxNode.Trim(),
            ProxmoxVmid: request.ProxmoxVmid,
            ProxmoxInstanceId: string.IsNullOrWhiteSpace(request.ProxmoxInstanceId) ? null : request.ProxmoxInstanceId.Trim(),
            K8sClusterId: string.IsNullOrWhiteSpace(request.K8sClusterId) ? null : request.K8sClusterId.Trim(),
            K8sNodeName: string.IsNullOrWhiteSpace(request.K8sNodeName) ? null : request.K8sNodeName.Trim(),
            UnifiSwitchMac: string.IsNullOrWhiteSpace(request.UnifiSwitchMac) ? null : request.UnifiSwitchMac.Trim(),
            UnifiSwitchPort: request.UnifiSwitchPort
        );

        var (createdHost, errors, conflict) = await _hostService.CreateHostAsync(createRequest, ct);

        if (conflict)
        {
            var firstError = errors?.Values.FirstOrDefault()?.FirstOrDefault() ?? "A host with this hostname or IP already exists.";
            return new ImportCandidateResponse(false, null, null, firstError);
        }

        if (errors != null && errors.Count > 0)
        {
            var firstError = errors.Values.FirstOrDefault()?.FirstOrDefault() ?? "Validation failed.";
            return new ImportCandidateResponse(false, null, null, firstError);
        }

        if (createdHost == null)
        {
            return new ImportCandidateResponse(false, null, null, "Failed to create host record.");
        }

        _logger.LogInformation("Successfully imported discovered candidate '{Name}' as host {HostId}", request.Name, createdHost.Id);

        return new ImportCandidateResponse(true, createdHost.Id, createdHost.Hostname, null);
    }

    public async Task<BatchImportCandidatesResponse> ImportCandidatesBatchAsync(BatchImportCandidatesRequest request, CancellationToken ct = default)
    {
        if (request.Candidates == null || request.Candidates.Count == 0)
        {
            return new BatchImportCandidatesResponse(0, 0, 0, new List<BatchImportItemResult>());
        }

        var results = new List<BatchImportItemResult>();
        var succeeded = 0;
        var failed = 0;

        foreach (var candidateReq in request.Candidates)
        {
            var targetType = !string.IsNullOrWhiteSpace(request.CommonTargetType)
                ? request.CommonTargetType
                : candidateReq.TargetType;

            var osFamily = !string.IsNullOrWhiteSpace(request.CommonOsFamily)
                ? request.CommonOsFamily
                : candidateReq.OsFamily;

            var effectiveReq = candidateReq with
            {
                TargetType = targetType,
                OsFamily = osFamily
            };

            var res = await ImportCandidateAsync(effectiveReq, ct);
            if (res.Success)
            {
                succeeded++;
                results.Add(new BatchImportItemResult(candidateReq.Name, true, res.HostId, res.Hostname, null));
            }
            else
            {
                failed++;
                results.Add(new BatchImportItemResult(candidateReq.Name, false, null, null, res.ErrorMessage));
            }
        }

        _logger.LogInformation("Batch import completed: {Succeeded}/{Total} candidates successfully imported into host inventory", succeeded, request.Candidates.Count);

        return new BatchImportCandidatesResponse(request.Candidates.Count, succeeded, failed, results);
    }

    public static string DetectOsFamily(string? osInfo, string? fallbackHint = null)

    {
        var combined = $"{osInfo} {fallbackHint}".Trim();
        if (string.IsNullOrWhiteSpace(combined))
        {
            return "linux_debian";
        }

        var lower = combined.ToLowerInvariant();
        if (lower.Contains("ubuntu")) return "linux_ubuntu";
        if (lower.Contains("debian")) return "linux_debian";
        if (lower.Contains("rocky")) return "linux_rocky";
        if (lower.Contains("fedora")) return "linux_fedora";
        if (lower.Contains("rhel") || lower.Contains("red hat") || lower.Contains("redhat") || lower.Contains("centos") || lower.Contains("alma")) return "linux_rhel";
        if (lower.Contains("alpine")) return "linux_alpine";
        if (lower.Contains("arch")) return "linux_arch";
        if (lower.Contains("suse")) return "linux_suse";
        if (lower.Contains("win")) return "windows";
        if (lower.Contains("bsd") || lower.Contains("opnsense") || lower.Contains("pfsense")) return "freebsd";
        if (lower.Contains("haos") || lower.Contains("hass") || lower.Contains("home assistant") || lower.Contains("homeassistant")) return "haos";
        if (lower.Contains("unifi")) return "unifi_os";

        return "linux_debian";
    }
}
