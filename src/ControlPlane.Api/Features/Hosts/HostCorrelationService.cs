using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Features.Hosts;

public class HostCorrelationService : IHostCorrelationService
{
    private readonly ControlPlaneDbContext _db;
    private readonly AgentConnectionManager? _connectionManager;
    private readonly IProxmoxClientFactory? _proxmoxClientFactory;
    private readonly IProxmoxClient? _fallbackProxmoxClient;
    private readonly IKubernetesClientFactory? _kubernetesClientFactory;
    private readonly IKubernetesAdapter? _fallbackKubernetesAdapter;
    private readonly IAdapterConfigService? _adapterConfigService;
    private readonly ILogger<HostCorrelationService> _logger;

    public HostCorrelationService(
        ControlPlaneDbContext db,
        ILogger<HostCorrelationService> logger,
        AgentConnectionManager? connectionManager = null,
        IProxmoxClientFactory? proxmoxClientFactory = null,
        IProxmoxClient? fallbackProxmoxClient = null,
        IKubernetesClientFactory? kubernetesClientFactory = null,
        IKubernetesAdapter? fallbackKubernetesAdapter = null,
        IAdapterConfigService? adapterConfigService = null)
    {
        _db = db;
        _logger = logger;
        _connectionManager = connectionManager;
        _proxmoxClientFactory = proxmoxClientFactory;
        _fallbackProxmoxClient = fallbackProxmoxClient;
        _kubernetesClientFactory = kubernetesClientFactory;
        _fallbackKubernetesAdapter = fallbackKubernetesAdapter;
        _adapterConfigService = adapterConfigService;
    }

    public async Task<HostRebootImpactDto> GetRebootImpactAsync(Guid hostId, CancellationToken ct = default)
    {
        var host = await _db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == hostId, ct);
        if (host == null)
        {
            throw new KeyNotFoundException($"Host with ID '{hostId}' was not found.");
        }

        var affectedRunningVms = new List<CorrelatedVmDto>();
        var warningMessages = new List<string>();
        bool hasWarnings = false;
        bool requiresConfirmation = false;

        // 1. Proxmox Hypervisor Assessment
        var isHypervisor = await CheckIsHypervisorAsync(host, ct);
        string? hypervisorNode = null;

        if (isHypervisor)
        {
            hypervisorNode = !string.IsNullOrWhiteSpace(host.Proxmox?.Node)
                ? host.Proxmox.Node
                : host.Hostname;

            var instanceId = host.Proxmox?.InstanceId ?? "default";

            // Find all inventory VMs correlated to this hypervisor node
            var inventoryVms = await _db.Hosts.AsNoTracking()
                .Where(h => h.Id != host.Id && h.Proxmox != null && h.Proxmox.Vmid > 0 &&
                    (h.Proxmox.Node.ToLower() == hypervisorNode.ToLower() || h.Proxmox.Node.ToLower() == host.Hostname.ToLower()))
                .ToListAsync(ct);

            // Attempt to query live Proxmox cluster resources for accurate runtime state
            IProxmoxClient? pveClient = null;
            if (!string.IsNullOrWhiteSpace(instanceId) && !string.Equals(instanceId, "default", StringComparison.OrdinalIgnoreCase) && _proxmoxClientFactory != null)
            {
                try
                {
                    pveClient = await _proxmoxClientFactory.GetClientAsync(instanceId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not resolve Proxmox client for instance '{InstanceId}'", instanceId);
                }
            }
            pveClient ??= _fallbackProxmoxClient;
            if (pveClient == null && _proxmoxClientFactory != null)
            {
                try
                {
                    pveClient = await _proxmoxClientFactory.GetClientAsync(instanceId, ct);
                }
                catch { }
            }

            List<ProxmoxClusterResourceDto> pveResources = new();
            if (pveClient != null)
            {
                try
                {
                    pveResources = await pveClient.DiscoverClusterResourcesAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to discover live Proxmox cluster resources for hypervisor '{Node}'", hypervisorNode);
                }
            }

            var nodeResources = pveResources
                .Where(r => string.Equals(r.Node, hypervisorNode, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(r.Type, "qemu", StringComparison.OrdinalIgnoreCase) || string.Equals(r.Type, "lxc", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (nodeResources.Count > 0)
            {
                // Correlate live resources with inventory
                foreach (var res in nodeResources)
                {
                    var isRunning = string.Equals(res.Status, "running", StringComparison.OrdinalIgnoreCase);
                    if (!isRunning) continue;

                    var vmid = res.Vmid ?? 0;
                    var matchedHost = inventoryVms.FirstOrDefault(h => h.Proxmox?.Vmid == vmid);

                    var vm = new CorrelatedVmDto(
                        Vmid: vmid,
                        Name: res.Name ?? matchedHost?.Hostname ?? $"vm-{vmid}",
                        Type: res.Type,
                        Status: res.Status ?? "running",
                        HostId: matchedHost?.Id,
                        Hostname: matchedHost?.Hostname ?? res.Name,
                        IpAddress: matchedHost?.IpAddress,
                        IsAgentOnline: matchedHost != null && (_connectionManager?.IsOnline(matchedHost.Id) ?? false),
                        K8sClusterId: matchedHost?.Kubernetes?.ClusterId,
                        K8sNodeName: matchedHost?.Kubernetes?.NodeName
                    );

                    affectedRunningVms.Add(vm);
                }
            }
            else
            {
                // If Proxmox API is offline or unconfigured, inspect inventory VMs
                foreach (var invVm in inventoryVms)
                {
                    var isOnline = _connectionManager?.IsOnline(invVm.Id) ?? false;
                    affectedRunningVms.Add(new CorrelatedVmDto(
                        Vmid: invVm.Proxmox!.Vmid,
                        Name: invVm.Hostname,
                        Type: invVm.TargetType,
                        Status: isOnline ? "running (agent online)" : "configured",
                        HostId: invVm.Id,
                        Hostname: invVm.Hostname,
                        IpAddress: invVm.IpAddress,
                        IsAgentOnline: isOnline,
                        K8sClusterId: invVm.Kubernetes?.ClusterId,
                        K8sNodeName: invVm.Kubernetes?.NodeName
                    ));
                }
            }

            if (affectedRunningVms.Count > 0)
            {
                hasWarnings = true;
                requiresConfirmation = true;
                warningMessages.Add(
                    $"Hypervisor node '{host.Hostname}' currently runs {affectedRunningVms.Count} virtual machine(s)/container(s) that will be terminated or interrupted upon reboot.");

                var k8sVms = affectedRunningVms.Where(v => !string.IsNullOrWhiteSpace(v.K8sClusterId) || !string.IsNullOrWhiteSpace(v.K8sNodeName)).ToList();
                foreach (var kvm in k8sVms)
                {
                    var clusterDesc = !string.IsNullOrWhiteSpace(kvm.K8sClusterId) ? $"cluster '{kvm.K8sClusterId}'" : "Kubernetes cluster";
                    warningMessages.Add(
                        $"Running VM '{kvm.Name}' (VMID {kvm.Vmid}) is a member node of {clusterDesc}. Rebooting this hypervisor will degrade or disrupt the Kubernetes cluster.");
                }
            }
        }

        // 2. Kubernetes Node Assessment
        var isK8sNode = CheckIsKubernetesNode(host);
        KubernetesClusterImpactDto? k8sImpact = null;

        if (isK8sNode)
        {
            var clusterId = host.Kubernetes?.ClusterId ?? "default";
            var nodeName = !string.IsNullOrWhiteSpace(host.Kubernetes?.NodeName)
                ? host.Kubernetes.NodeName
                : host.Hostname;

            IKubernetesAdapter? k8sAdapter = null;
            if (!string.IsNullOrWhiteSpace(clusterId) && !string.Equals(clusterId, "default", StringComparison.OrdinalIgnoreCase) && _kubernetesClientFactory != null)
            {
                try
                {
                    k8sAdapter = await _kubernetesClientFactory.CreateAdapterAsync(clusterId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not resolve Kubernetes adapter for cluster '{ClusterId}'", clusterId);
                }
            }
            k8sAdapter ??= _fallbackKubernetesAdapter;
            if (k8sAdapter == null && _kubernetesClientFactory != null)
            {
                try
                {
                    k8sAdapter = await _kubernetesClientFactory.CreateAdapterAsync(clusterId, ct);
                }
                catch { }
            }

            if (k8sAdapter != null)
            {
                try
                {
                    var allNodes = await k8sAdapter.ListNodesAsync(ct);

                    var matchedNode = allNodes.FirstOrDefault(n =>
                        string.Equals(n.Name, nodeName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(n.Name, host.Hostname, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(n.InternalIp) && string.Equals(n.InternalIp, host.IpAddress, StringComparison.OrdinalIgnoreCase)));

                    var effectiveNodeName = matchedNode?.Name ?? nodeName;
                    var nodePods = await k8sAdapter.ListPodsAsync(nodeName: effectiveNodeName, ct: ct);
                    var totalNodes = allNodes.Count;

                    if (matchedNode != null && totalNodes > 0)
                    {
                        var roles = matchedNode.Roles ?? new List<string> { "worker" };
                        var isControlPlane = roles.Any(r =>
                            r.Contains("control-plane", StringComparison.OrdinalIgnoreCase) ||
                            r.Contains("master", StringComparison.OrdinalIgnoreCase));

                        var readyNodes = allNodes.Count(n => n.IsReady);
                        var controlPlaneCount = allNodes.Count(n => n.Roles.Any(r =>
                            r.Contains("control-plane", StringComparison.OrdinalIgnoreCase) ||
                            r.Contains("master", StringComparison.OrdinalIgnoreCase)));

                        var isOnlyControlPlane = isControlPlane && controlPlaneCount <= 1;
                        var quorumAtRisk = (isControlPlane && controlPlaneCount <= 2) || totalNodes == 1;
                        var runningPods = nodePods.Count(p => string.Equals(p.Phase, "Running", StringComparison.OrdinalIgnoreCase));

                        string summary;
                        if (isOnlyControlPlane)
                        {
                            summary = $"Node '{effectiveNodeName}' is the sole control plane node for cluster '{clusterId}'. Rebooting this host will cause complete Kubernetes API server outage and disrupt all {runningPods} scheduled workload(s).";
                        }
                        else if (quorumAtRisk)
                        {
                            summary = $"Node '{effectiveNodeName}' is a control plane node in cluster '{clusterId}' with only {controlPlaneCount} control plane node(s). Control plane quorum may be degraded or lost.";
                        }
                        else if (isControlPlane)
                        {
                            summary = $"Node '{effectiveNodeName}' is an active control plane node in high-availability cluster '{clusterId}' ({readyNodes}/{totalNodes} nodes ready, {controlPlaneCount} control plane nodes). DAG reboot will safely cordon and drain workloads to sibling nodes.";
                        }
                        else
                        {
                            summary = $"Node '{effectiveNodeName}' is a worker node in cluster '{clusterId}' ({readyNodes}/{totalNodes} nodes ready) running {runningPods} workload pods. DAG reboot will cordon and drain workloads to other nodes.";
                        }

                        k8sImpact = new KubernetesClusterImpactDto(
                            ClusterId: clusterId,
                            NodeName: effectiveNodeName,
                            IsControlPlane: isControlPlane,
                            IsOnlyControlPlane: isOnlyControlPlane,
                            TotalNodes: totalNodes,
                            ReadyNodes: readyNodes,
                            RunningPodsCount: runningPods,
                            QuorumAtRisk: quorumAtRisk,
                            Summary: summary
                        );

                        hasWarnings = true;
                        if (quorumAtRisk || isOnlyControlPlane || runningPods > 0)
                        {
                            requiresConfirmation = true;
                        }
                        warningMessages.Add(summary);
                    }
                    else
                    {
                        hasWarnings = true;
                        warningMessages.Add($"Host is registered as a Kubernetes node ('{effectiveNodeName}') in cluster '{clusterId}'.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to evaluate Kubernetes cluster impact for node '{NodeName}' in cluster '{ClusterId}'", nodeName, clusterId);
                    hasWarnings = true;
                    warningMessages.Add(
                        $"Host is registered as a Kubernetes node ('{nodeName}') in cluster '{clusterId}'. Cluster status could not be verified in real-time.");
                }
            }
            else
            {
                hasWarnings = true;
                warningMessages.Add(
                    $"Host is registered as a Kubernetes node in cluster '{clusterId}'. Workloads may be affected during reboot.");
            }
        }

        return new HostRebootImpactDto(
            HostId: host.Id,
            Hostname: host.Hostname,
            IsHypervisor: isHypervisor,
            HypervisorNode: hypervisorNode,
            AffectedRunningVms: affectedRunningVms,
            IsKubernetesNode: isK8sNode,
            KubernetesImpact: k8sImpact,
            HasWarnings: hasWarnings,
            RequiresConfirmation: requiresConfirmation,
            WarningMessages: warningMessages
        );
    }

    public async Task<HostCorrelationDto> GetHostCorrelationAsync(Guid hostId, CancellationToken ct = default)
    {
        var host = await _db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == hostId, ct);
        if (host == null)
        {
            throw new KeyNotFoundException($"Host with ID '{hostId}' was not found.");
        }

        var isHypervisor = await CheckIsHypervisorAsync(host, ct);
        string? hypervisorNode = null;
        var hostedVms = new List<CorrelatedVmDto>();

        if (isHypervisor)
        {
            hypervisorNode = !string.IsNullOrWhiteSpace(host.Proxmox?.Node)
                ? host.Proxmox.Node
                : host.Hostname;

            var inventoryVms = await _db.Hosts.AsNoTracking()
                .Where(h => h.Id != host.Id && h.Proxmox != null && h.Proxmox.Vmid > 0 &&
                    (h.Proxmox.Node.ToLower() == hypervisorNode.ToLower() || h.Proxmox.Node.ToLower() == host.Hostname.ToLower()))
                .ToListAsync(ct);

            foreach (var vm in inventoryVms)
            {
                var isOnline = _connectionManager?.IsOnline(vm.Id) ?? false;
                hostedVms.Add(new CorrelatedVmDto(
                    Vmid: vm.Proxmox!.Vmid,
                    Name: vm.Hostname,
                    Type: vm.TargetType,
                    Status: isOnline ? "online" : "offline",
                    HostId: vm.Id,
                    Hostname: vm.Hostname,
                    IpAddress: vm.IpAddress,
                    IsAgentOnline: isOnline,
                    K8sClusterId: vm.Kubernetes?.ClusterId,
                    K8sNodeName: vm.Kubernetes?.NodeName
                ));
            }
        }

        // Check if host is a VM hosted on another host
        bool isVm = host.Proxmox != null && host.Proxmox.Vmid > 0;
        CorrelatedHypervisorDto? parentHypervisor = null;

        if (isVm)
        {
            var pveNode = host.Proxmox!.Node;
            var parentHost = await _db.Hosts.AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id != host.Id && (
                    (h.Proxmox != null && h.Proxmox.Vmid <= 0 && h.Proxmox.Node.ToLower() == pveNode.ToLower()) ||
                    h.Hostname.ToLower() == pveNode.ToLower() ||
                    (h.FriendlyName != null && h.FriendlyName.ToLower() == pveNode.ToLower())), ct);

            parentHypervisor = new CorrelatedHypervisorDto(
                HostId: parentHost?.Id,
                Hostname: parentHost?.Hostname ?? pveNode,
                FriendlyName: parentHost?.FriendlyName,
                ProxmoxNode: pveNode,
                InstanceId: host.Proxmox.InstanceId,
                IsOnline: parentHost != null && (_connectionManager?.IsOnline(parentHost.Id) ?? false)
            );
        }

        // Check if Kubernetes node
        var isK8s = CheckIsKubernetesNode(host);
        CorrelatedKubernetesDto? k8sDto = null;

        if (isK8s)
        {
            var clusterId = host.Kubernetes?.ClusterId ?? "default";
            var nodeName = !string.IsNullOrWhiteSpace(host.Kubernetes?.NodeName)
                ? host.Kubernetes.NodeName
                : host.Hostname;

            k8sDto = new CorrelatedKubernetesDto(
                ClusterId: clusterId,
                NodeName: nodeName,
                Roles: new List<string> { host.TargetType.Contains("control", StringComparison.OrdinalIgnoreCase) ? "control-plane" : "worker" },
                IsReady: true,
                RunningPodsCount: 0
            );
        }

        return new HostCorrelationDto(
            HostId: host.Id,
            Hostname: host.Hostname,
            IsHypervisor: isHypervisor,
            HypervisorNode: hypervisorNode,
            HostedVms: hostedVms,
            IsVm: isVm,
            Hypervisor: parentHypervisor,
            IsKubernetesNode: isK8s,
            Kubernetes: k8sDto
        );
    }

    private async Task<bool> CheckIsHypervisorAsync(HostEntity host, CancellationToken ct)
    {
        // 1. If it's a guest VM/container (Vmid > 0), it is DEFINITELY not a hypervisor
        if (host.Proxmox != null && host.Proxmox.Vmid > 0)
        {
            return false;
        }

        if (string.Equals(host.TargetType, "proxmox_vm", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host.TargetType, "proxmox_lxc", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(host.TargetType, "proxmox_node", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host.TargetType, "hypervisor", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (host.Proxmox != null && host.Proxmox.Vmid <= 0 && !string.IsNullOrWhiteSpace(host.Proxmox.Node))
        {
            return true;
        }

        // Check if other hosts refer to this host's hostname or this host's hypervisor node as their hypervisor
        var pveNode = host.Proxmox?.Node;
        var hasVms = await _db.Hosts.AsNoTracking().AnyAsync(h =>
            h.Id != host.Id && h.Proxmox != null && h.Proxmox.Vmid > 0 &&
            (h.Proxmox.Node.ToLower() == host.Hostname.ToLower() ||
             (!string.IsNullOrWhiteSpace(pveNode) && (host.Proxmox == null || host.Proxmox.Vmid <= 0) && h.Proxmox.Node.ToLower() == pveNode.ToLower())), ct);

        return hasVms;
    }

    private static bool CheckIsKubernetesNode(HostEntity host)
    {
        return (host.Kubernetes != null && (!string.IsNullOrWhiteSpace(host.Kubernetes.ClusterId) || !string.IsNullOrWhiteSpace(host.Kubernetes.NodeName))) ||
            string.Equals(host.TargetType, "kubernetes_node", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host.TargetType, "k8s_node", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SyncCorrelationResultDto> SyncHostCorrelationsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting host correlation synchronization across Proxmox and Kubernetes adapters...");
        var hosts = await _db.Hosts.ToListAsync(ct);
        var updatedHostIds = new HashSet<Guid>();
        var messages = new List<string>();
        int k8sCount = 0;
        int pveCount = 0;

        // 1. Synchronize Kubernetes Nodes
        List<KubernetesClusterDto> clusters = new();
        if (_adapterConfigService != null)
        {
            try
            {
                clusters = await _adapterConfigService.GetKubernetesClustersAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Kubernetes cluster configurations for correlation sync");
            }
        }

        if (clusters.Count == 0)
        {
            clusters.Add(new KubernetesClusterDto("default", "Default Cluster", null, null, false, false, true, DateTimeOffset.UtcNow));
        }

        foreach (var cluster in clusters)
        {
            IKubernetesAdapter? adapter = null;
            if (!string.IsNullOrWhiteSpace(cluster.Id) && !string.Equals(cluster.Id, "default", StringComparison.OrdinalIgnoreCase) && _kubernetesClientFactory != null)
            {
                try
                {
                    adapter = await _kubernetesClientFactory.CreateAdapterAsync(cluster.Id, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not resolve Kubernetes adapter for cluster '{ClusterId}'", cluster.Id);
                }
            }
            adapter ??= _fallbackKubernetesAdapter;
            if (adapter == null && _kubernetesClientFactory != null)
            {
                try
                {
                    adapter = await _kubernetesClientFactory.CreateAdapterAsync(cluster.Id, ct);
                }
                catch { }
            }

            if (adapter == null) continue;

            try
            {
                var k8sNodes = await adapter.ListNodesAsync(ct);
                _logger.LogInformation("Found {Count} node(s) in Kubernetes cluster '{ClusterId}' for correlation sync.", k8sNodes.Count, cluster.Id);

                foreach (var node in k8sNodes)
                {
                    var matchedHost = hosts.FirstOrDefault(h =>
                        (!string.IsNullOrWhiteSpace(node.InternalIp) && string.Equals(h.IpAddress, node.InternalIp, StringComparison.OrdinalIgnoreCase)) ||
                        string.Equals(h.Hostname, node.Name, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(h.FriendlyName) && string.Equals(h.FriendlyName, node.Name, StringComparison.OrdinalIgnoreCase)));

                    if (matchedHost != null)
                    {
                        bool modified = false;
                        if (matchedHost.Kubernetes == null)
                        {
                            matchedHost.Kubernetes = new KubernetesTarget
                            {
                                ClusterId = cluster.Id,
                                NodeName = node.Name
                            };
                            modified = true;
                        }
                        else
                        {
                            if (string.IsNullOrWhiteSpace(matchedHost.Kubernetes.ClusterId))
                            {
                                matchedHost.Kubernetes.ClusterId = cluster.Id;
                                modified = true;
                            }
                            if (string.IsNullOrWhiteSpace(matchedHost.Kubernetes.NodeName) || matchedHost.Kubernetes.NodeName != node.Name)
                            {
                                matchedHost.Kubernetes.NodeName = node.Name;
                                modified = true;
                            }
                        }

                        if (modified)
                        {
                            matchedHost.UpdatedAt = DateTimeOffset.UtcNow;
                            k8sCount++;
                            updatedHostIds.Add(matchedHost.Id);
                            messages.Add($"Correlated host '{matchedHost.Hostname}' to Kubernetes cluster '{cluster.Id}' as node '{node.Name}'.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error scanning Kubernetes cluster '{ClusterId}' during correlation sync", cluster.Id);
            }
        }

        // 2. Synchronize Proxmox Hypervisors & VMs
        List<ProxmoxInstanceDto> pveInstances = new();
        if (_adapterConfigService != null)
        {
            try
            {
                pveInstances = await _adapterConfigService.GetProxmoxInstancesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Proxmox instance configurations for correlation sync");
            }
        }

        if (pveInstances.Count == 0)
        {
            pveInstances.Add(new ProxmoxInstanceDto("default", "Default Proxmox", "", "", "", false, true, 300, 1000, null));
        }

        foreach (var inst in pveInstances)
        {
            IProxmoxClient? pveClient = null;
            if (!string.IsNullOrWhiteSpace(inst.Id) && !string.Equals(inst.Id, "default", StringComparison.OrdinalIgnoreCase) && _proxmoxClientFactory != null)
            {
                try
                {
                    pveClient = await _proxmoxClientFactory.GetClientAsync(inst.Id, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not resolve Proxmox client for instance '{InstanceId}'", inst.Id);
                }
            }
            pveClient ??= _fallbackProxmoxClient;
            if (pveClient == null && _proxmoxClientFactory != null)
            {
                try
                {
                    pveClient = await _proxmoxClientFactory.GetClientAsync(inst.Id, ct);
                }
                catch { }
            }

            if (pveClient == null) continue;

            try
            {
                // Hypervisor nodes
                var pveNodes = await pveClient.ListNodesAsync(ct);
                foreach (var n in pveNodes)
                {
                    var matchedHyp = hosts.FirstOrDefault(h =>
                        string.Equals(h.Hostname, n.Node, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(h.FriendlyName) && string.Equals(h.FriendlyName, n.Node, StringComparison.OrdinalIgnoreCase)) ||
                        (h.Proxmox != null && string.Equals(h.Proxmox.Node, n.Node, StringComparison.OrdinalIgnoreCase) && h.Proxmox.Vmid <= 0));

                    if (matchedHyp != null)
                    {
                        bool modified = false;
                        if (matchedHyp.Proxmox == null)
                        {
                            matchedHyp.Proxmox = new ProxmoxTarget { InstanceId = inst.Id, Node = n.Node, Vmid = 0 };
                            modified = true;
                        }
                        else if (string.IsNullOrWhiteSpace(matchedHyp.Proxmox.InstanceId))
                        {
                            matchedHyp.Proxmox.InstanceId = inst.Id;
                            modified = true;
                        }
                        if (string.Equals(matchedHyp.TargetType, "baremetal", StringComparison.OrdinalIgnoreCase))
                        {
                            matchedHyp.TargetType = "proxmox_node";
                            modified = true;
                        }
                        if (modified)
                        {
                            matchedHyp.UpdatedAt = DateTimeOffset.UtcNow;
                            pveCount++;
                            updatedHostIds.Add(matchedHyp.Id);
                            messages.Add($"Correlated Proxmox hypervisor '{matchedHyp.Hostname}' (Node: {n.Node}).");
                        }
                    }
                }

                // Cluster VM/LXC resources
                var resources = await pveClient.DiscoverClusterResourcesAsync(ct);
                foreach (var res in resources)
                {
                    if (!string.Equals(res.Type, "qemu", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(res.Type, "lxc", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var vmid = res.Vmid ?? 0;
                    if (vmid <= 0) continue;

                    var matchedVm = hosts.FirstOrDefault(h =>
                        (h.Proxmox != null && h.Proxmox.Vmid == vmid && string.Equals(h.Proxmox.Node, res.Node, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(res.Name) && string.Equals(h.Hostname, res.Name, StringComparison.OrdinalIgnoreCase)));

                    if (matchedVm != null)
                    {
                        bool modified = false;
                        if (matchedVm.Proxmox == null)
                        {
                            matchedVm.Proxmox = new ProxmoxTarget { InstanceId = inst.Id, Node = res.Node, Vmid = vmid };
                            modified = true;
                        }
                        else
                        {
                            if (matchedVm.Proxmox.Vmid <= 0) { matchedVm.Proxmox.Vmid = vmid; modified = true; }
                            if (string.IsNullOrWhiteSpace(matchedVm.Proxmox.Node)) { matchedVm.Proxmox.Node = res.Node; modified = true; }
                            if (string.IsNullOrWhiteSpace(matchedVm.Proxmox.InstanceId)) { matchedVm.Proxmox.InstanceId = inst.Id; modified = true; }
                        }

                        var expectedType = string.Equals(res.Type, "lxc", StringComparison.OrdinalIgnoreCase) ? "proxmox_lxc" : "proxmox_vm";
                        if (!string.Equals(matchedVm.TargetType, expectedType, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(matchedVm.TargetType, "kubernetes_node", StringComparison.OrdinalIgnoreCase))
                        {
                            matchedVm.TargetType = expectedType;
                            modified = true;
                        }

                        if (modified)
                        {
                            matchedVm.UpdatedAt = DateTimeOffset.UtcNow;
                            pveCount++;
                            updatedHostIds.Add(matchedVm.Id);
                            messages.Add($"Correlated VM '{matchedVm.Hostname}' to Proxmox node '{res.Node}' (VMID: {vmid}).");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error scanning Proxmox instance '{InstanceId}' during correlation sync", inst.Id);
            }
        }

        // 3. Fix any hosts where Vmid > 0 that might have been incorrectly marked as proxmox_node
        foreach (var h in hosts)
        {
            if (h.Proxmox != null && h.Proxmox.Vmid > 0 && string.Equals(h.TargetType, "proxmox_node", StringComparison.OrdinalIgnoreCase))
            {
                h.TargetType = "proxmox_vm";
                h.UpdatedAt = DateTimeOffset.UtcNow;
                updatedHostIds.Add(h.Id);
            }
        }

        // 4. Mark baremetal hosts as proxmox_node ONLY if Vmid <= 0 and other VMs point to them
        foreach (var h in hosts)
        {
            if ((h.Proxmox == null || h.Proxmox.Vmid <= 0) &&
                string.Equals(h.TargetType, "baremetal", StringComparison.OrdinalIgnoreCase) &&
                hosts.Any(other => other.Id != h.Id && other.Proxmox != null && other.Proxmox.Vmid > 0 &&
                    (string.Equals(other.Proxmox.Node, h.Hostname, StringComparison.OrdinalIgnoreCase) ||
                     (h.Proxmox != null && !string.IsNullOrWhiteSpace(h.Proxmox.Node) && string.Equals(other.Proxmox.Node, h.Proxmox.Node, StringComparison.OrdinalIgnoreCase)))))
            {
                h.TargetType = "proxmox_node";
                h.UpdatedAt = DateTimeOffset.UtcNow;
                updatedHostIds.Add(h.Id);
                pveCount++;
            }
        }

        if (updatedHostIds.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Saved correlation updates for {Count} hosts to database.", updatedHostIds.Count);
        }

        return new SyncCorrelationResultDto(
            Success: true,
            CorrelatedKubernetesNodes: k8sCount,
            CorrelatedProxmoxHosts: pveCount,
            TotalHostsUpdated: updatedHostIds.Count,
            Messages: messages
        );
    }
}
