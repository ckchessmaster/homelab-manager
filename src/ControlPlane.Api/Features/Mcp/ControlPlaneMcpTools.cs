using System.ComponentModel;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Idrac;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.OPNsense;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Adapters.UniFi;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Discovery;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Features.Orchestration;
using ControlPlane.Api.Features.Orchestration.Pipelines;
using ControlPlane.Api.Features.Workloads;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace ControlPlane.Api.Features.Mcp;

/// <summary>
/// Tools exposed over the Model Context Protocol (MCP) enabling AI agents to query logs, interact with compute hosts, and orchestrate infrastructure adapters.
/// </summary>
[McpServerToolType]
public class ControlPlaneMcpTools
{
    private readonly ControlPlaneDbContext _db;
    private readonly HostService _hostService;
    private readonly IDiscoveryService _discoveryService;
    private readonly IPipelineCatalog _pipelineCatalog;
    private readonly JobOrchestratorService _jobOrchestrator;
    private readonly AgentConnectionManager _connectionManager;
    private readonly IAdapterConfigService? _adapterConfigService;
    private readonly IWorkloadService? _workloadService;
    private readonly IUniFiClientFactory? _unifiClientFactory;
    private readonly IOPNsenseClientFactory? _opnsenseClientFactory;
    private readonly IIdracClientFactory? _idracClientFactory;
    private readonly IKubernetesClientFactory? _kubernetesClientFactory;
    private readonly IProxmoxClientFactory? _proxmoxClientFactory;

    public ControlPlaneMcpTools(
        ControlPlaneDbContext db,
        HostService hostService,
        IDiscoveryService discoveryService,
        IPipelineCatalog pipelineCatalog,
        JobOrchestratorService jobOrchestrator,
        AgentConnectionManager connectionManager,
        IAdapterConfigService? adapterConfigService = null,
        IWorkloadService? workloadService = null,
        IUniFiClientFactory? unifiClientFactory = null,
        IOPNsenseClientFactory? opnsenseClientFactory = null,
        IIdracClientFactory? idracClientFactory = null,
        IKubernetesClientFactory? kubernetesClientFactory = null,
        IProxmoxClientFactory? proxmoxClientFactory = null)
    {
        _db = db;
        _hostService = hostService;
        _discoveryService = discoveryService;
        _pipelineCatalog = pipelineCatalog;
        _jobOrchestrator = jobOrchestrator;
        _connectionManager = connectionManager;
        _adapterConfigService = adapterConfigService;
        _workloadService = workloadService;
        _unifiClientFactory = unifiClientFactory;
        _opnsenseClientFactory = opnsenseClientFactory;
        _idracClientFactory = idracClientFactory;
        _kubernetesClientFactory = kubernetesClientFactory;
        _proxmoxClientFactory = proxmoxClientFactory;
    }

    [McpServerTool]
    [Description("Query sequence-ordered console and execution logs for an update or debug job.")]
    public async Task<object> QueryJobLogs(
        [Description("The GUID of the job whose logs to query.")] Guid jobId,
        [Description("Optional starting sequence ID (defaults to 0 for beginning of stream).")] long fromSequenceId = 0,
        [Description("Maximum number of log lines to return (default 100, max 1000).")] int limit = 100,
        CancellationToken ct = default)
    {
        var effectiveLimit = Math.Clamp(limit, 1, 1000);
        var logs = await _db.StepLogs
            .AsNoTracking()
            .Where(l => l.JobId == jobId && l.SequenceId >= fromSequenceId)
            .OrderBy(l => l.SequenceId)
            .Take(effectiveLimit)
            .Select(l => new
            {
                sequenceId = l.SequenceId,
                stream = l.StreamType,
                line = l.LogLine,
                timestamp = l.Timestamp
            })
            .ToListAsync(ct);

        var totalCount = await _db.StepLogs
            .AsNoTracking()
            .Where(l => l.JobId == jobId)
            .CountAsync(ct);

        return new
        {
            jobId,
            fromSequenceId,
            count = logs.Count,
            totalAvailable = totalCount,
            logs
        };
    }

    [McpServerTool]
    [Description("Get status, current step, and execution details for an update job.")]
    public async Task<object?> GetJob(
        [Description("The GUID of the job to inspect.")] Guid jobId,
        CancellationToken ct = default)
    {
        var job = await _db.UpdateJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job == null) return null;

        var host = await _db.Hosts
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == job.TargetHostId, ct);

        var logCount = await _db.StepLogs
            .AsNoTracking()
            .Where(l => l.JobId == jobId)
            .CountAsync(ct);

        return new
        {
            id = job.Id,
            targetHostId = job.TargetHostId,
            targetHostname = host?.Hostname,
            targetIp = host?.IpAddress,
            pipelineId = job.PipelineId,
            status = job.Status,
            activeStep = job.ActiveStep,
            initiatedBy = job.InitiatedBy,
            startedAt = job.StartedAt,
            completedAt = job.CompletedAt,
            failureReason = job.FailureReason,
            totalLogs = logCount
        };
    }

    [McpServerTool]
    [Description("List recent update jobs across the fleet with optional status or host filtering.")]
    public async Task<object> ListJobs(
        [Description("Maximum number of jobs to return (default 20, max 100).")] int limit = 20,
        [Description("Optional status filter (e.g. 'Running', 'Completed', 'Failed', 'RolledBack').")] string? status = null,
        [Description("Optional target host GUID filter.")] Guid? hostId = null,
        CancellationToken ct = default)
    {
        var effectiveLimit = Math.Clamp(limit, 1, 100);
        var query = _db.UpdateJobs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(j => j.Status.ToLower() == status.Trim().ToLower());
        }

        if (hostId.HasValue)
        {
            query = query.Where(j => j.TargetHostId == hostId.Value);
        }

        var jobs = await query
            .OrderByDescending(j => j.StartedAt)
            .Take(effectiveLimit)
            .Select(j => new
            {
                id = j.Id,
                targetHostId = j.TargetHostId,
                pipelineId = j.PipelineId,
                status = j.Status,
                activeStep = j.ActiveStep,
                initiatedBy = j.InitiatedBy,
                startedAt = j.StartedAt,
                completedAt = j.CompletedAt,
                failureReason = j.FailureReason
            })
            .ToListAsync(ct);

        return new
        {
            total = jobs.Count,
            jobs
        };
    }

    [McpServerTool]
    [Description("List managed compute hosts in the inventory with agent and package update status.")]
    public async Task<object> ListHosts(
        [Description("Optional OS family filter (e.g. 'linux_debian', 'linux_rhel', 'windows').")] string? osFamily = null,
        [Description("Optional target type filter (e.g. 'baremetal', 'proxmox_vm', 'proxmox_lxc').")] string? targetType = null,
        [Description("Optional filter for agent online state (true/false).")] bool? isOnline = null,
        CancellationToken ct = default)
    {
        var filter = new HostFilterQuery(
            OsFamily: osFamily,
            TargetType: targetType,
            PendingReboot: null,
            HasUpdates: null
        );

        var hosts = await _hostService.ListHostsAsync(filter, ct);

        var enriched = hosts.Select(h =>
        {
            var online = _connectionManager.IsOnline(h.Id);
            return new
            {
                id = h.Id,
                hostname = h.Hostname,
                friendlyName = h.FriendlyName,
                ipAddress = h.IpAddress,
                osFamily = h.OsFamily,
                targetType = h.TargetType,
                isOnline = online,
                agentInstalled = h.Agent.Installed,
                pendingReboot = h.Agent.PendingReboot,
                upgradablePackagesCount = h.Agent.UpgradablePackagesCount,
                agentVersion = h.Agent.Version,
                proxmoxNode = h.Proxmox?.Node,
                proxmoxVmid = h.Proxmox?.Vmid
            };
        }).Where(h => !isOnline.HasValue || h.isOnline == isOnline.Value)
          .ToList();

        return new
        {
            count = enriched.Count,
            hosts = enriched
        };
    }

    [McpServerTool]
    [Description("Get detailed information for a managed compute host including hardware targets and active jobs.")]
    public async Task<object?> GetHostDetails(
        [Description("The GUID of the host to inspect.")] Guid? hostId = null,
        CancellationToken ct = default)
    {
        if (!hostId.HasValue || hostId.Value == Guid.Empty)
        {
            return new { error = "The 'hostId' parameter is required." };
        }

        var host = await _hostService.GetHostByIdAsync(hostId.Value, ct);
        if (host == null) return null;

        var isOnline = _connectionManager.IsOnline(host.Id);
        var activeJobs = await _db.UpdateJobs
            .AsNoTracking()
            .Where(j => j.TargetHostId == host.Id && (j.Status == "Running" || j.Status == "Pending" || j.Status == "Verifying"))
            .OrderByDescending(j => j.StartedAt)
            .Select(j => new
            {
                id = j.Id,
                pipelineId = j.PipelineId,
                status = j.Status,
                activeStep = j.ActiveStep,
                startedAt = j.StartedAt
            })
            .ToListAsync(ct);

        return new
        {
            host,
            isOnline,
            activeJobs
        };
    }

    [McpServerTool]
    [Description("Scan infrastructure (Proxmox VE, Kubernetes, UniFi, and OPNsense) to discover unmanaged compute candidates.")]
    public async Task<object> ScanDiscovery(
        [Description("Whether to include Proxmox VE scan (default true).")] bool includeProxmox = true,
        [Description("Whether to include Kubernetes cluster scan (default true).")] bool includeKubernetes = true,
        [Description("Whether to include Ubiquiti UniFi network scan (default true).")] bool includeUniFi = true,
        [Description("Whether to include OPNsense firewall DHCP lease scan (default true).")] bool includeOPNsense = true,
        CancellationToken ct = default)
    {
        var result = await _discoveryService.ScanAsync(includeProxmox, includeKubernetes, includeUniFi, includeOPNsense, ct);
        return result;
    }

    [McpServerTool]
    [Description("Import a discovered compute candidate directly into managed host inventory.")]
    public async Task<object> ImportCandidateHost(
        [Description("Hostname for the new host.")] string name,
        [Description("IP address or resolvable FQDN for the host.")] string ipAddress,
        [Description("Target type: 'baremetal', 'proxmox_vm', or 'proxmox_lxc'.")] string targetType,
        [Description("OS family: 'linux_debian', 'linux_rhel', or 'windows'.")] string osFamily,
        [Description("Optional friendly display name.")] string? friendlyName = null,
        [Description("Optional Proxmox hypervisor node name.")] string? proxmoxNode = null,
        [Description("Optional Proxmox VMID.")] int? proxmoxVmid = null,
        CancellationToken ct = default)
    {
        var request = new ImportCandidateRequest(
            Name: name,
            IpAddress: ipAddress,
            TargetType: targetType,
            OsFamily: osFamily,
            FriendlyName: friendlyName,
            ProxmoxNode: proxmoxNode,
            ProxmoxVmid: proxmoxVmid
        );

        var result = await _discoveryService.ImportCandidateAsync(request, ct);
        return result;
    }

    [McpServerTool]
    [Description("List all modular maintenance and upgrade pipeline profiles available in the catalog.")]
    public object ListPipelines()
    {
        var profiles = _pipelineCatalog.GetProfiles().Select(p => new
        {
            id = p.Id,
            name = p.Name,
            description = p.Description,
            icon = p.Icon,
            compatibleTargetTypes = p.CompatibleTargetTypes,
            steps = p.Steps.Select(s => new { name = s.Name, description = s.Description }).ToList()
        });

        return new { pipelines = profiles };
    }

    [McpServerTool]
    [Description("Trigger an upgrade or maintenance job on a target host using a pipeline profile.")]
    public async Task<object> TriggerUpgradeJob(
        [Description("The GUID of the target host to upgrade.")] Guid hostId,
        [Description("Optional pipeline ID (e.g. 'linux-standard-upgrade', 'proxmox-pve-upgrade'). If omitted, catalog picks matching pipeline.")] string? pipelineId = null,
        [Description("Identifier of operator/agent initiating the job (default 'AI Agent via MCP').")] string initiatedBy = "AI Agent via MCP",
        CancellationToken ct = default)
    {
        var host = await _db.Hosts.FindAsync(new object[] { hostId }, ct);
        if (host == null)
        {
            return new { success = false, error = $"Host {hostId} not found." };
        }

        var activeJob = await _db.UpdateJobs
            .AnyAsync(j => j.TargetHostId == hostId && (j.Status == "Running" || j.Status == "Pending" || j.Status == "Verifying"), ct);

        if (activeJob)
        {
            return new { success = false, error = $"Host '{host.Hostname}' already has an active update job running." };
        }

        var (job, error) = await _jobOrchestrator.CreateAndStartJobAsync(hostId, pipelineId, initiatedBy, ct);
        if (job == null)
        {
            return new { success = false, error = error ?? "Failed to create and start job." };
        }

        return new
        {
            success = true,
            jobId = job.Id,
            pipelineId = job.PipelineId,
            status = job.Status,
            targetHostname = host.Hostname,
            targetIp = host.IpAddress
        };
    }

    [McpServerTool]
    [Description("Dispatch an ad-hoc shell command to an online node agent and stream output to job console.")]
    public async Task<object> ExecuteDebugCommand(
        [Description("The GUID of the target host.")] Guid hostId,
        [Description("Command to execute (e.g. 'uptime', 'df -h', 'systemctl status').")] string command,
        [Description("Optional argument list.")] string[]? args = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new { success = false, error = "Command cannot be empty." };
        }

        var host = await _db.Hosts.FindAsync(new object[] { hostId }, ct);
        if (host == null)
        {
            return new { success = false, error = $"Host {hostId} not found." };
        }

        if (!_connectionManager.IsOnline(host.Id))
        {
            return new { success = false, error = $"Agent for host '{host.Hostname}' is currently offline." };
        }

        var jobId = Guid.NewGuid();
        var job = new UpdateJob
        {
            Id = jobId,
            TargetHostId = host.Id,
            InitiatedBy = "AI Agent via MCP",
            Status = "Running",
            ActiveStep = $"{command} {string.Join(' ', args ?? Array.Empty<string>())}",
            StartedAt = DateTimeOffset.UtcNow
        };

        _db.UpdateJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        var cmdEnvelope = new AgentCommandEnvelope
        {
            Type = "EXECUTE_COMMAND",
            JobId = jobId,
            Command = command,
            Args = args ?? Array.Empty<string>()
        };

        var dispatched = await _connectionManager.SendCommandAsync(host.Id, cmdEnvelope, ct);
        if (!dispatched)
        {
            job.Status = "Failed";
            job.FailureReason = "Failed to dispatch command to connected agent.";
            job.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new { success = false, error = "Failed to dispatch command to agent." };
        }

        return new
        {
            success = true,
            jobId,
            hostId = host.Id,
            hostname = host.Hostname,
            command,
            status = "Running"
        };
    }

    [McpServerTool]
    [Description("List all configured infrastructure adapters (Proxmox, Kubernetes, UniFi, OPNsense, iDRAC) with target counts and connection status.")]
    public async Task<object> ListAdapters(CancellationToken ct = default)
    {
        if (_adapterConfigService == null)
        {
            return new { error = "Adapter configuration service is not available." };
        }

        var proxmox = await _adapterConfigService.GetProxmoxInstancesAsync(ct);
        var k8s = await _adapterConfigService.GetKubernetesClustersAsync(ct);
        var unifi = await _adapterConfigService.GetUniFiInstancesAsync(ct);
        var opnsense = await _adapterConfigService.GetOPNsenseInstancesAsync(ct);
        var idrac = await _adapterConfigService.GetIdracInstancesAsync(ct);

        return new
        {
            summary = new
            {
                totalAdapters = proxmox.Count + k8s.Count + unifi.Count + opnsense.Count + idrac.Count,
                proxmoxCount = proxmox.Count,
                k8sClusterCount = k8s.Count,
                unifiControllerCount = unifi.Count,
                opnsenseFirewallCount = opnsense.Count,
                idracBmcCount = idrac.Count
            },
            adapters = new
            {
                proxmox = proxmox.Select(p => new { p.Id, p.Name, p.BaseUrl, p.HasSecret }),
                kubernetes = k8s.Select(k => new { k.Id, k.Name, k.HasKubeConfig, k.HasToken }),
                unifi = unifi.Select(u => new { u.Id, u.Name, u.ControllerUrl, u.Site, u.AuthType, u.HasPassword, u.HasApiKey }),
                opnsense = opnsense.Select(o => new { o.Id, o.Name, o.BaseUrl, o.HasSecret }),
                idrac = idrac.Select(i => new { i.Id, i.Name, i.BmcUrl, i.Username, i.HasPassword })
            }
        };
    }

    [McpServerTool]
    [Description("Test connectivity and health for a specific infrastructure adapter instance.")]
    public async Task<object> TestAdapterConnection(
        [Description("Adapter category: 'proxmox', 'kubernetes' (or 'k8s'), 'unifi', 'opnsense', or 'idrac' (or 'redfish').")] string adapterType,
        [Description("ID of the instance to test. If omitted or 'default', tests the primary instance.")] string? instanceId = null,
        CancellationToken ct = default)
    {
        var type = adapterType.Trim().ToLowerInvariant();
        try
        {
            switch (type)
            {
                case "proxmox":
                    if (_proxmoxClientFactory == null) return new { success = false, error = "Proxmox factory not available." };
                    var pClient = await _proxmoxClientFactory.GetClientAsync(instanceId, ct);
                    var nodes = await pClient.ListNodesAsync(ct);
                    return new { success = true, adapterType = "proxmox", instanceId, nodeCount = nodes.Count, nodes = nodes.Select(n => n.Node) };

                case "kubernetes":
                case "k8s":
                    if (_kubernetesClientFactory == null) return new { success = false, error = "Kubernetes factory not available." };
                    var kAdapter = await _kubernetesClientFactory.CreateAdapterAsync(instanceId, ct);
                    var kRes = await kAdapter.TestConnectionAsync(ct);
                    return new { success = kRes.Success, adapterType = "kubernetes", instanceId, serverVersion = kRes.ServerVersion, nodeCount = kRes.NodeCount, latencyMs = kRes.LatencyMs, message = kRes.Message };

                case "unifi":
                    if (_unifiClientFactory == null) return new { success = false, error = "UniFi factory not available." };
                    var (uClient, uConfig, uPass, uApiKey) = await _unifiClientFactory.ResolveAsync(instanceId ?? "default", ct);
                    var uRes = await uClient.TestConnectionAsync(uConfig.ControllerUrl, uConfig.Username, uPass, uConfig.Site, uApiKey, ct);
                    return new { success = uRes.Success, adapterType = "unifi", instanceId = uConfig.Id, latencyMs = uRes.LatencyMs, message = uRes.Message, version = uRes.ControllerVersion };

                case "opnsense":
                    if (_opnsenseClientFactory == null) return new { success = false, error = "OPNsense factory not available." };
                    var (oClient, oConfig, oSecret) = await _opnsenseClientFactory.ResolveAsync(instanceId ?? "default", ct);
                    var oRes = await oClient.TestConnectionAsync(oConfig.BaseUrl, oConfig.ApiKey, oSecret, oConfig.AllowSelfSignedCert, ct);
                    return new { success = oRes.Success, adapterType = "opnsense", instanceId = oConfig.Id, latencyMs = oRes.LatencyMs, message = oRes.Message, version = oRes.Version };

                case "idrac":
                case "redfish":
                    if (_idracClientFactory == null) return new { success = false, error = "iDRAC factory not available." };
                    var (iClient, iConfig, iPass) = await _idracClientFactory.ResolveAsync(instanceId ?? "default", ct);
                    var iRes = await iClient.TestConnectionAsync(iConfig.BmcUrl, iConfig.Username, iPass, iConfig.AllowSelfSignedCert, ct);
                    return new { success = iRes.Success, adapterType = "idrac", instanceId = iConfig.Id, latencyMs = iRes.LatencyMs, message = iRes.Message, model = iRes.Model };

                default:
                    return new { success = false, error = $"Unknown adapter type '{adapterType}'. Valid types: 'proxmox', 'kubernetes', 'unifi', 'opnsense', 'idrac'." };
            }
        }
        catch (Exception ex)
        {
            return new { success = false, adapterType, instanceId, error = ex.Message };
        }
    }

    [McpServerTool]
    [Description("List aggregated Kubernetes workloads (Deployments, StatefulSets, DaemonSets) across clusters and namespaces with replica status.")]
    public async Task<object> ListWorkloads(
        [Description("Optional Kubernetes cluster ID filter.")] string? clusterId = null,
        [Description("Optional namespace filter (e.g. 'default', 'kube-system').")] string? namespaceName = null,
        CancellationToken ct = default)
    {
        if (_workloadService == null) return new { error = "Workload service not available." };
        var workloads = await _workloadService.GetAggregatedWorkloadsAsync(clusterId, namespaceName, ct);
        return new { count = workloads.Items.Count, totalDeployments = workloads.TotalDeployments, healthyDeployments = workloads.HealthyDeployments, workloads = workloads.Items };
    }

    [McpServerTool]
    [Description("Trigger a rolling rollout restart of a Kubernetes deployment.")]
    public async Task<object> RestartWorkload(
        [Description("Kubernetes cluster ID hosting the workload.")] string clusterId,
        [Description("Namespace where the workload resides.")] string namespaceName,
        [Description("Name of the deployment workload to restart.")] string name,
        CancellationToken ct = default)
    {
        if (_workloadService == null) return new { success = false, error = "Workload service not available." };
        var success = await _workloadService.RestartWorkloadAsync(clusterId, namespaceName, name, ct);
        return new { success, clusterId, namespaceName, name, message = success ? $"Workload '{name}' restart initiated." : "Failed to restart workload." };
    }

    [McpServerTool]
    [Description("Scale the replica count of a Kubernetes deployment workload.")]
    public async Task<object> ScaleWorkload(
        [Description("Kubernetes cluster ID hosting the workload.")] string clusterId,
        [Description("Namespace where the workload resides.")] string namespaceName,
        [Description("Name of the deployment workload to scale.")] string name,
        [Description("Desired number of replicas.")] int replicas,
        CancellationToken ct = default)
    {
        if (_workloadService == null) return new { success = false, error = "Workload service not available." };
        var success = await _workloadService.ScaleWorkloadAsync(clusterId, namespaceName, name, replicas, ct);
        return new { success, clusterId, namespaceName, name, replicas, message = success ? $"Workload '{name}' scaled to {replicas} replicas." : "Failed to scale workload." };
    }

    [McpServerTool]
    [Description("List UniFi network devices (switches, access points, gateways) including PoE power draw and port status.")]
    public async Task<object> ListUniFiDevices(
        [Description("Optional UniFi controller instance ID.")] string? instanceId = null,
        CancellationToken ct = default)
    {
        if (_unifiClientFactory == null) return new { error = "UniFi factory not available." };
        var (client, config, pass, apiKey) = await _unifiClientFactory.ResolveAsync(instanceId ?? "default", ct);
        var devices = await client.GetDevicesAsync(config.ControllerUrl, config.Username, pass, config.Site, apiKey, ct);
        return new { controllerId = config.Id, count = devices.Count, devices };
    }

    [McpServerTool]
    [Description("Power-cycle a PoE port on a UniFi switch to reboot a connected device (e.g. camera, access point, Pi).")]
    public async Task<object> PowerCycleUniFiPort(
        [Description("MAC address of the target UniFi switch.")] string switchMac,
        [Description("Port index/number on the switch (1-based).")] int portNumber,
        [Description("Optional UniFi controller instance ID.")] string? instanceId = null,
        [Description("Power bounce off duration in seconds (default 5).")] int delaySeconds = 5,
        CancellationToken ct = default)
    {
        if (_unifiClientFactory == null) return new { success = false, error = "UniFi factory not available." };
        var (client, config, pass, apiKey) = await _unifiClientFactory.ResolveAsync(instanceId ?? "default", ct);
        var result = await client.CyclePoEPortAsync(config.ControllerUrl, config.Username, pass, switchMac, portNumber, config.Site, delaySeconds, apiKey, ct);
        return new { success = result.Success, switchMac, portNumber, message = result.Message };
    }

    [McpServerTool]
    [Description("Query OPNsense firewall gateway status, WAN/LAN health, and active DHCP leases.")]
    public async Task<object> GetOPNsenseStatus(
        [Description("Optional OPNsense firewall instance ID.")] string? instanceId = null,
        CancellationToken ct = default)
    {
        if (_opnsenseClientFactory == null) return new { error = "OPNsense factory not available." };
        var (client, config, secret) = await _opnsenseClientFactory.ResolveAsync(instanceId ?? "default", ct);
        var gateways = await client.GetGatewaysAsync(config.BaseUrl, config.ApiKey, secret, config.AllowSelfSignedCert, ct);
        var leases = await client.GetDhcpLeasesAsync(config.BaseUrl, config.ApiKey, secret, config.AllowSelfSignedCert, ct);
        var firmware = await client.GetFirmwareStatusAsync(config.BaseUrl, config.ApiKey, secret, config.AllowSelfSignedCert, ct);
        return new
        {
            firewallId = config.Id,
            firewallName = config.Name,
            gateways,
            dhcpLeaseCount = leases.Count,
            dhcpLeases = leases.Take(50),
            firmwareStatus = firmware.Status,
            productVersion = firmware.Version
        };
    }

    [McpServerTool]
    [Description("Query out-of-band BMC (Redfish / Dell iDRAC) system health, power state, and thermal/fan vitals.")]
    public async Task<object> GetHardwareSensors(
        [Description("Optional iDRAC instance ID.")] string? instanceId = null,
        [Description("Optional host BMC IP address to look up instance.")] string? hostBmcIp = null,
        CancellationToken ct = default)
    {
        if (_idracClientFactory == null) return new { error = "iDRAC factory not available." };

        IIdracClient client;
        string bmcUrl, username, password;
        bool allowSelfSigned;

        if (!string.IsNullOrWhiteSpace(hostBmcIp))
        {
            var resolved = await _idracClientFactory.ResolveByHostBmcIpAsync(hostBmcIp, ct);
            if (resolved == null) return new { error = $"No iDRAC instance configured for BMC IP '{hostBmcIp}'." };
            (client, bmcUrl, username, password, allowSelfSigned) = resolved.Value;
        }
        else
        {
            var (c, config, pass) = await _idracClientFactory.ResolveAsync(instanceId ?? "default", ct);
            client = c;
            bmcUrl = config.BmcUrl;
            username = config.Username;
            password = pass;
            allowSelfSigned = config.AllowSelfSignedCert;
        }

        var vitals = await client.GetVitalsAsync(bmcUrl, username, password, allowSelfSigned, ct);
        return new { bmcUrl, vitals };
    }

    [McpServerTool]
    [Description("Dispatch an out-of-band power action to server hardware via BMC/Redfish/iDRAC.")]
    public async Task<object> ExecuteHardwarePowerAction(
        [Description("Power action: 'On', 'ForceOff', 'GracefulShutdown', 'GracefulRestart', or 'ForceRestart'.")] string resetType,
        [Description("Optional iDRAC instance ID.")] string? instanceId = null,
        [Description("Optional host BMC IP address.")] string? hostBmcIp = null,
        CancellationToken ct = default)
    {
        if (_idracClientFactory == null) return new { success = false, error = "iDRAC factory not available." };

        IIdracClient client;
        string bmcUrl, username, password;
        bool allowSelfSigned;

        if (!string.IsNullOrWhiteSpace(hostBmcIp))
        {
            var resolved = await _idracClientFactory.ResolveByHostBmcIpAsync(hostBmcIp, ct);
            if (resolved == null) return new { success = false, error = $"No iDRAC instance configured for BMC IP '{hostBmcIp}'." };
            (client, bmcUrl, username, password, allowSelfSigned) = resolved.Value;
        }
        else
        {
            var (c, config, pass) = await _idracClientFactory.ResolveAsync(instanceId ?? "default", ct);
            client = c;
            bmcUrl = config.BmcUrl;
            username = config.Username;
            password = pass;
            allowSelfSigned = config.AllowSelfSignedCert;
        }

        var result = await client.ResetSystemAsync(bmcUrl, username, password, resetType, allowSelfSigned, ct);
        return new { success = result.Success, bmcUrl, resetType, message = result.Message };
    }
}

