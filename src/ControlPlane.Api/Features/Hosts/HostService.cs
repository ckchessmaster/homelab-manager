using System.Net;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Features.Hosts;

public class HostService
{
    private readonly ControlPlaneDbContext _db;
    private readonly ILogger<HostService> _logger;
    private readonly AgentConnectionManager? _connectionManager;

    public HostService(ControlPlaneDbContext db, ILogger<HostService> logger, AgentConnectionManager? connectionManager = null)
    {
        _db = db;
        _logger = logger;
        _connectionManager = connectionManager;
    }

    public async Task<List<HostResponse>> ListHostsAsync(HostFilterQuery query, CancellationToken cancellationToken = default)
    {
        var q = _db.Hosts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.OsFamily))
        {
            var os = query.OsFamily.Trim().ToLowerInvariant();
            q = q.Where(h => h.OsFamily.ToLower() == os);
        }

        if (!string.IsNullOrWhiteSpace(query.TargetType))
        {
            var target = query.TargetType.Trim().ToLowerInvariant();
            q = q.Where(h => h.TargetType.ToLower() == target);
        }

        if (query.PendingReboot.HasValue)
        {
            var pending = query.PendingReboot.Value;
            q = q.Where(h => h.Agent.PendingReboot == pending);
        }

        if (query.HasUpdates.HasValue)
        {
            if (query.HasUpdates.Value)
            {
                q = q.Where(h => h.Agent.UpgradablePackagesCount > 0);
            }
            else
            {
                q = q.Where(h => h.Agent.UpgradablePackagesCount == 0);
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            q = q.Where(h => h.Hostname.ToLower().Contains(term)
                || (h.FriendlyName != null && h.FriendlyName.ToLower().Contains(term))
                || h.IpAddress.ToLower().Contains(term));
        }

        var list = await q.OrderBy(h => h.Hostname).ToListAsync(cancellationToken);
        return list.Select(h =>
        {
            var isOnline = _connectionManager?.IsOnline(h.Id) ?? false;
            HypervisorHostSummaryDto? hyp = null;
            if (h.Proxmox != null && h.Proxmox.Vmid > 0 && !string.IsNullOrWhiteSpace(h.Proxmox.Node))
            {
                var parent = list.FirstOrDefault(p => p.Id != h.Id && (p.Proxmox == null || p.Proxmox.Vmid <= 0) && (
                    (p.Proxmox != null && string.Equals(p.Proxmox.Node, h.Proxmox.Node, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(p.Hostname, h.Proxmox.Node, StringComparison.OrdinalIgnoreCase)));
                if (parent != null)
                {
                    hyp = new HypervisorHostSummaryDto(parent.Id, parent.Hostname, parent.FriendlyName, h.Proxmox.Node);
                }
            }

            var isHypervisor = (h.Proxmox == null || h.Proxmox.Vmid <= 0) &&
                (string.Equals(h.TargetType, "proxmox_node", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(h.TargetType, "hypervisor", StringComparison.OrdinalIgnoreCase) ||
                 (h.Proxmox != null && h.Proxmox.Vmid <= 0 && !string.IsNullOrWhiteSpace(h.Proxmox.Node)) ||
                 list.Any(other => other.Id != h.Id && other.Proxmox != null && other.Proxmox.Vmid > 0 && string.Equals(other.Proxmox.Node, h.Hostname, StringComparison.OrdinalIgnoreCase)));

            List<HostedVmSummaryDto>? hosted = null;
            if (isHypervisor)
            {
                var hostedList = list.Where(vm => vm.Id != h.Id && vm.Proxmox != null && vm.Proxmox.Vmid > 0 && (
                    string.Equals(vm.Proxmox.Node, h.Hostname, StringComparison.OrdinalIgnoreCase) ||
                    (h.Proxmox != null && h.Proxmox.Vmid <= 0 && string.Equals(vm.Proxmox.Node, h.Proxmox.Node, StringComparison.OrdinalIgnoreCase))
                )).Select(vm => new HostedVmSummaryDto(
                    vm.Id,
                    vm.Hostname,
                    vm.FriendlyName,
                    vm.Proxmox!.Vmid,
                    vm.TargetType,
                    _connectionManager?.IsOnline(vm.Id) ?? false
                )).ToList();

                if (hostedList.Count > 0) hosted = hostedList;
            }

            return MapToResponse(h, isOnline, hyp, hosted);
        }).ToList();
    }

    public async Task<HostResponse?> GetHostByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var host = await _db.Hosts.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == id, cancellationToken);

        if (host == null) return null;

        var isOnline = _connectionManager?.IsOnline(host.Id) ?? false;
        HypervisorHostSummaryDto? hyp = null;
        if (host.Proxmox != null && host.Proxmox.Vmid > 0 && !string.IsNullOrWhiteSpace(host.Proxmox.Node))
        {
            var parent = await _db.Hosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id != host.Id && (p.Proxmox == null || p.Proxmox.Vmid <= 0) && (
                (p.Proxmox != null && p.Proxmox.Node.ToLower() == host.Proxmox.Node.ToLower()) ||
                p.Hostname.ToLower() == host.Proxmox.Node.ToLower()), cancellationToken);
            if (parent != null)
            {
                hyp = new HypervisorHostSummaryDto(parent.Id, parent.Hostname, parent.FriendlyName, host.Proxmox.Node);
            }
        }

        List<HostedVmSummaryDto>? hosted = null;
        if (host.Proxmox == null || host.Proxmox.Vmid <= 0)
        {
            var hostedEntities = await _db.Hosts.AsNoTracking().Where(vm => vm.Id != host.Id && vm.Proxmox != null && vm.Proxmox.Vmid > 0 && (
                vm.Proxmox.Node.ToLower() == host.Hostname.ToLower() ||
                (host.Proxmox != null && host.Proxmox.Vmid <= 0 && vm.Proxmox.Node.ToLower() == host.Proxmox.Node.ToLower())
            )).ToListAsync(cancellationToken);

            if (hostedEntities.Count > 0)
            {
                hosted = hostedEntities.Select(vm => new HostedVmSummaryDto(
                    vm.Id,
                    vm.Hostname,
                    vm.FriendlyName,
                    vm.Proxmox!.Vmid,
                    vm.TargetType,
                    _connectionManager?.IsOnline(vm.Id) ?? false
                )).ToList();
            }
        }

        return MapToResponse(host, isOnline, hyp, hosted);
    }

    public async Task<(HostResponse? Host, IDictionary<string, string[]>? Errors, bool Conflict)> CreateHostAsync(
        CreateHostRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = HostValidators.ValidateCreate(request);
        if (validationErrors.Count > 0)
        {
            return (null, validationErrors, false);
        }

        var cleanHostname = request.Hostname.Trim();
        var cleanIp = request.IpAddress.Trim();

        if (!IPAddress.TryParse(cleanIp, out _))
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(cleanIp, cancellationToken);
                var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        ?? addresses.FirstOrDefault();
                if (ipv4 != null)
                {
                    cleanIp = ipv4.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not resolve DNS for host address '{CleanIp}'", cleanIp);
            }
        }

        var duplicateHostname = await _db.Hosts
            .AnyAsync(h => h.Hostname.ToLower() == cleanHostname.ToLower(), cancellationToken);

        if (duplicateHostname)
        {
            var err = new Dictionary<string, string[]>
            {
                [nameof(request.Hostname)] = new[] { $"A host with hostname '{cleanHostname}' already exists." }
            };
            return (null, err, true);
        }

        var duplicateIp = await _db.Hosts
            .AnyAsync(h => h.IpAddress.ToLower() == cleanIp.ToLower(), cancellationToken);

        if (duplicateIp)
        {
            var err = new Dictionary<string, string[]>
            {
                [nameof(request.IpAddress)] = new[] { $"A host with IP address '{cleanIp}' already exists." }
            };
            return (null, err, true);
        }

        var host = new HostEntity
        {
            Id = Guid.NewGuid(),
            Hostname = cleanHostname,
            FriendlyName = string.IsNullOrWhiteSpace(request.FriendlyName) ? null : request.FriendlyName.Trim(),
            IpAddress = cleanIp,
            OsFamily = request.OsFamily.Trim().ToLowerInvariant(),
            TargetType = request.TargetType.Trim().ToLowerInvariant(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Agent = new AgentState
            {
                Installed = false,
                PendingReboot = false,
                UpgradablePackagesCount = 0
            }
        };

        if (!string.IsNullOrWhiteSpace(request.ProxmoxNode) || request.ProxmoxVmid.HasValue || !string.IsNullOrWhiteSpace(request.ProxmoxInstanceId))
        {
            host.Proxmox = new ProxmoxTarget
            {
                InstanceId = string.IsNullOrWhiteSpace(request.ProxmoxInstanceId) ? null : request.ProxmoxInstanceId.Trim(),
                Node = request.ProxmoxNode?.Trim() ?? string.Empty,
                Vmid = request.ProxmoxVmid ?? 0
            };
        }

        if (!string.IsNullOrWhiteSpace(request.IdracIp))
        {
            host.Idrac = new IdracTarget
            {
                IpAddress = request.IdracIp.Trim()
            };
        }

        if (!string.IsNullOrWhiteSpace(request.UnifiSwitchMac) || request.UnifiSwitchPort.HasValue)
        {
            host.NetworkPort = new UnifiPortTarget
            {
                SwitchMac = request.UnifiSwitchMac?.Trim() ?? string.Empty,
                PortNumber = request.UnifiSwitchPort ?? 0
            };
        }

        if (!string.IsNullOrWhiteSpace(request.K8sClusterId) || !string.IsNullOrWhiteSpace(request.K8sNodeName))
        {
            host.Kubernetes = new KubernetesTarget
            {
                ClusterId = string.IsNullOrWhiteSpace(request.K8sClusterId) ? null : request.K8sClusterId.Trim(),
                NodeName = string.IsNullOrWhiteSpace(request.K8sNodeName) ? null : request.K8sNodeName.Trim()
            };
        }

        await _db.Hosts.AddAsync(host, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Registered host '{Hostname}' ({Id}) with IP {IpAddress}", host.Hostname, host.Id, host.IpAddress);

        return (MapToResponse(host, _connectionManager?.IsOnline(host.Id) ?? false), null, false);
    }

    public async Task<(HostResponse? Host, IDictionary<string, string[]>? Errors, bool Conflict, bool NotFound)> UpdateHostAsync(
        Guid id,
        UpdateHostRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = HostValidators.ValidateUpdate(request);
        if (validationErrors.Count > 0)
        {
            return (null, validationErrors, false, false);
        }

        var host = await _db.Hosts.FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (host == null)
        {
            return (null, null, false, true);
        }

        if (request.Hostname != null)
        {
            var cleanHostname = request.Hostname.Trim();
            var duplicate = await _db.Hosts
                .AnyAsync(h => h.Id != id && h.Hostname.ToLower() == cleanHostname.ToLower(), cancellationToken);

            if (duplicate)
            {
                var err = new Dictionary<string, string[]>
                {
                    [nameof(request.Hostname)] = new[] { $"A host with hostname '{cleanHostname}' already exists." }
                };
                return (null, err, true, false);
            }
            host.Hostname = cleanHostname;
        }

        if (request.IpAddress != null)
        {
            var cleanIp = request.IpAddress.Trim();
            if (!IPAddress.TryParse(cleanIp, out _))
            {
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(cleanIp, cancellationToken);
                    var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            ?? addresses.FirstOrDefault();
                    if (ipv4 != null)
                    {
                        cleanIp = ipv4.ToString();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not resolve DNS for host address '{CleanIp}'", cleanIp);
                }
            }
            var duplicate = await _db.Hosts
                .AnyAsync(h => h.Id != id && h.IpAddress.ToLower() == cleanIp.ToLower(), cancellationToken);

            if (duplicate)
            {
                var err = new Dictionary<string, string[]>
                {
                    [nameof(request.IpAddress)] = new[] { $"A host with IP address '{cleanIp}' already exists." }
                };
                return (null, err, true, false);
            }
            host.IpAddress = cleanIp;
        }

        if (request.FriendlyName != null)
        {
            host.FriendlyName = string.IsNullOrWhiteSpace(request.FriendlyName) ? null : request.FriendlyName.Trim();
        }

        if (request.OsFamily != null)
        {
            host.OsFamily = request.OsFamily.Trim().ToLowerInvariant();
        }

        if (request.TargetType != null)
        {
            host.TargetType = request.TargetType.Trim().ToLowerInvariant();
        }

        if (request.ProxmoxNode != null || request.ProxmoxVmid.HasValue || request.ProxmoxInstanceId != null)
        {
            host.Proxmox ??= new ProxmoxTarget();
            if (request.ProxmoxInstanceId != null)
            {
                host.Proxmox.InstanceId = string.IsNullOrWhiteSpace(request.ProxmoxInstanceId) ? null : request.ProxmoxInstanceId.Trim();
            }
            if (request.ProxmoxNode != null)
            {
                host.Proxmox.Node = request.ProxmoxNode.Trim();
            }
            if (request.ProxmoxVmid.HasValue)
            {
                host.Proxmox.Vmid = request.ProxmoxVmid.Value;
            }
        }

        if (request.IdracIp != null)
        {
            if (string.IsNullOrWhiteSpace(request.IdracIp))
            {
                host.Idrac = null;
            }
            else
            {
                host.Idrac ??= new IdracTarget();
                host.Idrac.IpAddress = request.IdracIp.Trim();
            }
        }

        if (request.UnifiSwitchMac != null || request.UnifiSwitchPort.HasValue)
        {
            host.NetworkPort ??= new UnifiPortTarget();
            if (request.UnifiSwitchMac != null)
            {
                host.NetworkPort.SwitchMac = request.UnifiSwitchMac.Trim();
            }
            if (request.UnifiSwitchPort.HasValue)
            {
                host.NetworkPort.PortNumber = request.UnifiSwitchPort.Value;
            }
        }

        if (request.K8sClusterId != null || request.K8sNodeName != null)
        {
            if (string.IsNullOrWhiteSpace(request.K8sClusterId) && string.IsNullOrWhiteSpace(request.K8sNodeName))
            {
                host.Kubernetes = null;
            }
            else
            {
                host.Kubernetes ??= new KubernetesTarget();
                if (request.K8sClusterId != null) host.Kubernetes.ClusterId = string.IsNullOrWhiteSpace(request.K8sClusterId) ? null : request.K8sClusterId.Trim();
                if (request.K8sNodeName != null) host.Kubernetes.NodeName = string.IsNullOrWhiteSpace(request.K8sNodeName) ? null : request.K8sNodeName.Trim();
            }
        }

        if (request.PendingReboot.HasValue)
        {
            host.Agent.PendingReboot = request.PendingReboot.Value;
        }

        host.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated host '{Hostname}' ({Id})", host.Hostname, host.Id);

        return (MapToResponse(host, _connectionManager?.IsOnline(host.Id) ?? false), null, false, false);
    }

    public async Task<(bool Success, bool NotFound, string? ErrorMessage)> DeleteHostAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var host = await _db.Hosts
            .Include(h => h.UpdateJobs)
                .ThenInclude(j => j.StepLogs)
            .FirstOrDefaultAsync(h => h.Id == id, cancellationToken);

        if (host == null)
        {
            return (false, true, "Host not found.");
        }

        var activeJob = host.UpdateJobs.FirstOrDefault(j =>
            j.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase) ||
            j.Status.Equals("Running", StringComparison.OrdinalIgnoreCase) ||
            j.Status.Equals("Verifying", StringComparison.OrdinalIgnoreCase));

        if (activeJob != null)
        {
            return (false, false, $"Cannot delete host while update job '{activeJob.Id}' is {activeJob.Status}.");
        }

        if (host.UpdateJobs.Count > 0)
        {
            _db.UpdateJobs.RemoveRange(host.UpdateJobs);
        }

        _db.Hosts.Remove(host);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted host '{Hostname}' ({Id})", host.Hostname, host.Id);

        return (true, false, null);
    }

    public static HostResponse MapToResponse(
        HostEntity host,
        bool isOnline = false,
        HypervisorHostSummaryDto? hypervisor = null,
        List<HostedVmSummaryDto>? hostedVms = null)
    {
        return new HostResponse(
            Id: host.Id,
            Hostname: host.Hostname,
            FriendlyName: host.FriendlyName,
            IpAddress: host.IpAddress,
            OsFamily: host.OsFamily,
            TargetType: host.TargetType,
            Proxmox: host.Proxmox == null ? null : new ProxmoxTargetDto(host.Proxmox.Node, host.Proxmox.Vmid, host.Proxmox.InstanceId),
            Kubernetes: host.Kubernetes == null ? null : new KubernetesTargetDto(host.Kubernetes.ClusterId, host.Kubernetes.NodeName),
            Idrac: host.Idrac == null ? null : new IdracTargetDto(host.Idrac.IpAddress),
            NetworkPort: host.NetworkPort == null ? null : new UnifiPortTargetDto(host.NetworkPort.SwitchMac, host.NetworkPort.PortNumber),
            Agent: new AgentStateDto(
                Installed: host.Agent.Installed,
                Version: host.Agent.Version,
                LastSeenAt: host.Agent.LastSeenAt,
                PendingReboot: host.Agent.PendingReboot,
                UpgradablePackagesCount: host.Agent.UpgradablePackagesCount,
                IsOnline: isOnline
            ),
            CreatedAt: host.CreatedAt,
            UpdatedAt: host.UpdatedAt,
            Hypervisor: hypervisor,
            HostedVms: hostedVms
        );
    }
}
