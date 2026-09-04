using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Features.Hosts;

public class HostService
{
    private readonly ControlPlaneDbContext _db;
    private readonly ILogger<HostService> _logger;

    public HostService(ControlPlaneDbContext db, ILogger<HostService> logger)
    {
        _db = db;
        _logger = logger;
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
        return list.Select(MapToResponse).ToList();
    }

    public async Task<HostResponse?> GetHostByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var host = await _db.Hosts.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == id, cancellationToken);

        return host == null ? null : MapToResponse(host);
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

        if (!string.IsNullOrWhiteSpace(request.ProxmoxNode) || request.ProxmoxVmid.HasValue)
        {
            host.Proxmox = new ProxmoxTarget
            {
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

        await _db.Hosts.AddAsync(host, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Registered host '{Hostname}' ({Id}) with IP {IpAddress}", host.Hostname, host.Id, host.IpAddress);

        return (MapToResponse(host), null, false);
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

        if (request.ProxmoxNode != null || request.ProxmoxVmid.HasValue)
        {
            host.Proxmox ??= new ProxmoxTarget();
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

        if (request.PendingReboot.HasValue)
        {
            host.Agent.PendingReboot = request.PendingReboot.Value;
        }

        host.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated host '{Hostname}' ({Id})", host.Hostname, host.Id);

        return (MapToResponse(host), null, false, false);
    }

    public async Task<(bool Success, bool NotFound, string? ErrorMessage)> DeleteHostAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var host = await _db.Hosts
            .Include(h => h.UpdateJobs)
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

        _db.Hosts.Remove(host);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted host '{Hostname}' ({Id})", host.Hostname, host.Id);

        return (true, false, null);
    }

    public static HostResponse MapToResponse(HostEntity host)
    {
        return new HostResponse(
            Id: host.Id,
            Hostname: host.Hostname,
            FriendlyName: host.FriendlyName,
            IpAddress: host.IpAddress,
            OsFamily: host.OsFamily,
            TargetType: host.TargetType,
            Proxmox: host.Proxmox == null ? null : new ProxmoxTargetDto(host.Proxmox.Node, host.Proxmox.Vmid),
            Idrac: host.Idrac == null ? null : new IdracTargetDto(host.Idrac.IpAddress),
            NetworkPort: host.NetworkPort == null ? null : new UnifiPortTargetDto(host.NetworkPort.SwitchMac, host.NetworkPort.PortNumber),
            Agent: new AgentStateDto(
                Installed: host.Agent.Installed,
                Version: host.Agent.Version,
                LastSeenAt: host.Agent.LastSeenAt,
                PendingReboot: host.Agent.PendingReboot,
                UpgradablePackagesCount: host.Agent.UpgradablePackagesCount
            ),
            CreatedAt: host.CreatedAt,
            UpdatedAt: host.UpdatedAt
        );
    }
}
