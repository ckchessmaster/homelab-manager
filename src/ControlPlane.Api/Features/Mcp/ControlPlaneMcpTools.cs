using System.ComponentModel;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Discovery;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Features.Orchestration;
using ControlPlane.Api.Features.Orchestration.Pipelines;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace ControlPlane.Api.Features.Mcp;

/// <summary>
/// Tools exposed over the Model Context Protocol (MCP) enabling AI agents to query logs and interact with the ControlPlane API.
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

    public ControlPlaneMcpTools(
        ControlPlaneDbContext db,
        HostService hostService,
        IDiscoveryService discoveryService,
        IPipelineCatalog pipelineCatalog,
        JobOrchestratorService jobOrchestrator,
        AgentConnectionManager connectionManager)
    {
        _db = db;
        _hostService = hostService;
        _discoveryService = discoveryService;
        _pipelineCatalog = pipelineCatalog;
        _jobOrchestrator = jobOrchestrator;
        _connectionManager = connectionManager;
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
        [Description("The GUID of the host to inspect.")] Guid hostId,
        CancellationToken ct = default)
    {
        var host = await _hostService.GetHostByIdAsync(hostId, ct);
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
    [Description("Scan infrastructure (Proxmox VE and Kubernetes) to discover unmanaged compute candidates.")]
    public async Task<object> ScanDiscovery(
        [Description("Whether to include Proxmox VE scan (default true).")] bool includeProxmox = true,
        [Description("Whether to include Kubernetes cluster scan (default true).")] bool includeKubernetes = true,
        CancellationToken ct = default)
    {
        var result = await _discoveryService.ScanAsync(includeProxmox, includeKubernetes, ct);
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
}
