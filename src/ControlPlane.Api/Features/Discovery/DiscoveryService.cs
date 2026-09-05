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
    private readonly IKubernetesAdapter _kubernetesAdapter;
    private readonly HostService _hostService;
    private readonly ProxmoxOptions _fallbackProxmoxOptions;
    private readonly Features.Adapters.Config.IAdapterConfigService? _adapterConfigService;
    private readonly ILogger<DiscoveryService> _logger;

    public DiscoveryService(
        ControlPlaneDbContext db,
        IProxmoxClient proxmoxClient,
        IKubernetesAdapter kubernetesAdapter,
        HostService hostService,
        IOptions<ProxmoxOptions> proxmoxOptions,
        ILogger<DiscoveryService> logger,
        Features.Adapters.Config.IAdapterConfigService? adapterConfigService = null)
    {
        _db = db;
        _proxmoxClient = proxmoxClient;
        _kubernetesAdapter = kubernetesAdapter;
        _hostService = hostService;
        _fallbackProxmoxOptions = proxmoxOptions.Value;
        _logger = logger;
        _adapterConfigService = adapterConfigService;
    }

    public async Task<DiscoveryScanResult> ScanAsync(
        bool includeProxmox = true,
        bool includeKubernetes = true,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Beginning infrastructure service discovery (Proxmox: {Proxmox}, Kubernetes: {K8s})...",
            includeProxmox, includeKubernetes);

        var candidates = new List<DiscoveredCandidateDto>();
        var errors = new List<string>();

        var existingHosts = await _db.Hosts.AsNoTracking().ToListAsync(ct);

        // 1. Scan Proxmox VE
        if (includeProxmox)
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
                try
                {
                    // 1a. Discover Proxmox hypervisor nodes
                    try
                    {
                        var pveNodes = await _proxmoxClient.ListNodesAsync(ct);
                        string? defaultNodeIp = null;
                        if (Uri.TryCreate(pveOpts.BaseUrl, UriKind.Absolute, out var pveUri))
                        {
                            defaultNodeIp = pveUri.Host;
                        }

                        foreach (var n in pveNodes)
                        {
                            var matchedHost = existingHosts.FirstOrDefault(h =>
                                string.Equals(h.Hostname, n.Node, StringComparison.OrdinalIgnoreCase)
                                || (!string.IsNullOrWhiteSpace(defaultNodeIp) && string.Equals(h.IpAddress, defaultNodeIp, StringComparison.OrdinalIgnoreCase))
                                || (h.Proxmox != null && string.Equals(h.Proxmox.Node, n.Node, StringComparison.OrdinalIgnoreCase) && h.Proxmox.Vmid <= 0));

                            candidates.Add(new DiscoveredCandidateDto(
                                Id: $"pve:node:{n.Node}",
                                Source: "Proxmox",
                                Name: n.Node,
                                IpAddress: matchedHost?.IpAddress ?? defaultNodeIp,
                                TargetType: "baremetal",
                                OsFamily: matchedHost?.OsFamily ?? "linux_debian",
                                Status: n.Status,
                                ProxmoxNode: n.Node,
                                ProxmoxVmid: null,
                                Roles: new List<string> { "hypervisor", "pve-host" },
                                IsManaged: matchedHost != null,
                                ExistingHostId: matchedHost?.Id,
                                ExistingHostname: matchedHost?.Hostname
                            ));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to enumerate Proxmox nodes for hypervisor discovery.");
                    }

                    // 1b. Discover VMs and LXC containers
                    var resources = await _proxmoxClient.DiscoverClusterResourcesAsync(ct);
                    _logger.LogInformation("Discovered {Count} VM/LXC resources from Proxmox cluster.", resources.Count);

                    if (resources.Count == 0)
                    {
                        var hasVmAudit = await _proxmoxClient.HasVmAuditPermissionAsync(ct);
                        if (!hasVmAudit)
                        {
                            errors.Add("Proxmox API token is connected, but lacks VM.Audit permissions (Proxmox returned 0 VMs). If 'Privilege Separation' is enabled on the token, grant 'PVEVMAdmin', 'PVEAuditor', or 'Administrator' on '/' or '/vms' in Proxmox Datacenter -> Permissions -> API Token, or recreate the token with 'Privilege Separation' unchecked.");
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
                            if (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase) && vmid > 0)
                            {
                                try
                                {
                                    ip = await _proxmoxClient.TryGetGuestIpAddressAsync(res.Node, vmid, isLxc, ct);
                                }
                                catch (Exception ipEx)
                                {
                                    _logger.LogDebug(ipEx, "Could not resolve IP address for guest {Node}/{Vmid}", res.Node, vmid);
                                }
                            }

                            // Match against existing hosts
                            var matchedHost = existingHosts.FirstOrDefault(h =>
                                (h.Proxmox != null && string.Equals(h.Proxmox.Node, res.Node, StringComparison.OrdinalIgnoreCase) && h.Proxmox.Vmid == vmid)
                                || (!string.IsNullOrWhiteSpace(ip) && string.Equals(h.IpAddress, ip, StringComparison.OrdinalIgnoreCase))
                                || string.Equals(h.Hostname, name, StringComparison.OrdinalIgnoreCase));

                            candidates.Add(new DiscoveredCandidateDto(
                                Id: $"pve:{res.Node}:{vmid}",
                                Source: "Proxmox",
                                Name: name,
                                IpAddress: ip ?? matchedHost?.IpAddress,
                                TargetType: targetType,
                                OsFamily: matchedHost?.OsFamily ?? "linux_debian",
                                Status: status,
                                ProxmoxNode: res.Node,
                                ProxmoxVmid: vmid > 0 ? vmid : null,
                                Roles: new List<string> { isLxc ? "container" : "virtual-machine" },
                                IsManaged: matchedHost != null,
                                ExistingHostId: matchedHost?.Id,
                                ExistingHostname: matchedHost?.Hostname
                            ));
                        }
                        catch (Exception candEx)
                        {
                            _logger.LogWarning(candEx, "Error processing candidate resource {Id} ({Type})", res.Id, res.Type);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scanning Proxmox cluster resources");
                    errors.Add($"Proxmox scan failed: {ex.Message}");
                }
            }
        }

        // 2. Scan Kubernetes Nodes
        if (includeKubernetes)
        {
            try
            {
                var k8sNodes = await _kubernetesAdapter.ListNodesAsync(ct);
                _logger.LogInformation("Discovered {Count} nodes from Kubernetes cluster.", k8sNodes.Count);

                foreach (var kNode in k8sNodes)
                {
                    // Match against existing hosts
                    var matchedHost = existingHosts.FirstOrDefault(h =>
                        string.Equals(h.Hostname, kNode.Name, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrWhiteSpace(kNode.InternalIp) && string.Equals(h.IpAddress, kNode.InternalIp, StringComparison.OrdinalIgnoreCase)));

                    // Also check if already in candidate list from Proxmox
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
                            K8sNodeName = kNode.Name,
                            Roles = mergedRoles.ToList(),
                            IpAddress = existingCandidate.IpAddress ?? kNode.InternalIp
                        };
                    }
                    else
                    {
                        var roles = kNode.Roles.Select(r => $"k8s-{r}").ToList();
                        candidates.Add(new DiscoveredCandidateDto(
                            Id: $"k8s:{kNode.Name}",
                            Source: "Kubernetes",
                            Name: kNode.Name,
                            IpAddress: kNode.InternalIp ?? matchedHost?.IpAddress,
                            TargetType: matchedHost?.TargetType ?? "baremetal",
                            OsFamily: matchedHost?.OsFamily ?? (kNode.OsImage?.Contains("ubuntu", StringComparison.OrdinalIgnoreCase) == true || kNode.OsImage?.Contains("debian", StringComparison.OrdinalIgnoreCase) == true ? "linux_debian" : "linux_rhel"),
                            Status: kNode.IsReady ? "Ready" : "NotReady",
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
                _logger.LogError(ex, "Error scanning Kubernetes cluster nodes");
                errors.Add($"Kubernetes scan failed: {ex.Message}");
            }
        }

        var total = candidates.Count;
        var managed = candidates.Count(c => c.IsManaged);
        var unmanaged = total - managed;

        return new DiscoveryScanResult(
            Candidates: candidates,
            TotalDiscovered: total,
            AlreadyManaged: managed,
            UnmanagedCount: unmanaged,
            ScannedAt: DateTimeOffset.UtcNow,
            Errors: errors
        );
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

        var createRequest = new CreateHostRequest(
            Hostname: request.Name.Trim(),
            FriendlyName: string.IsNullOrWhiteSpace(request.FriendlyName) ? null : request.FriendlyName.Trim(),
            IpAddress: request.IpAddress.Trim(),
            OsFamily: string.IsNullOrWhiteSpace(request.OsFamily) ? "linux_debian" : request.OsFamily.Trim(),
            TargetType: string.IsNullOrWhiteSpace(request.TargetType) ? "proxmox_vm" : request.TargetType.Trim(),
            ProxmoxNode: string.IsNullOrWhiteSpace(request.ProxmoxNode) ? null : request.ProxmoxNode.Trim(),
            ProxmoxVmid: request.ProxmoxVmid
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
}
