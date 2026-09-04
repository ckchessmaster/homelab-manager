using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Features.Agents;

public class AgentHeartbeatHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentConnectionManager _connectionManager;
    private readonly ILogger<AgentHeartbeatHandler> _logger;

    public AgentHeartbeatHandler(
        IServiceScopeFactory scopeFactory,
        AgentConnectionManager connectionManager,
        ILogger<AgentHeartbeatHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task HandleHeartbeatAsync(Guid hostId, AgentHeartbeatMessage message, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == hostId, cancellationToken);
        if (host == null)
        {
            _logger.LogWarning("Received heartbeat for unknown host ID {HostId}", hostId);
            return;
        }

        host.Agent.Installed = true;
        host.Agent.LastSeenAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(message.AgentVersion))
        {
            host.Agent.Version = message.AgentVersion;
        }
        host.Agent.PendingReboot = message.PendingReboot;
        if (message.PackageSummary != null)
        {
            host.Agent.UpgradablePackagesCount = message.PackageSummary.UpgradableCount;
        }
        host.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        _connectionManager.UpdateMetrics(hostId, message.Metrics);

        _logger.LogDebug(
            "Processed heartbeat for {Hostname} ({HostId}): RebootNeeded={Reboot}, UpgradablePkgs={Pkgs}",
            host.Hostname,
            host.Id,
            host.Agent.PendingReboot,
            host.Agent.UpgradablePackagesCount
        );
    }
}
