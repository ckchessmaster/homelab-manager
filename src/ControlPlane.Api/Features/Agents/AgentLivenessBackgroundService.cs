using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Features.Agents;

public class AgentLivenessBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentConnectionManager _connectionManager;
    private readonly ILogger<AgentLivenessBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(15);
    private readonly TimeSpan _offlineThreshold = TimeSpan.FromSeconds(30);

    public AgentLivenessBackgroundService(
        IServiceScopeFactory scopeFactory,
        AgentConnectionManager connectionManager,
        ILogger<AgentLivenessBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent Liveness background service started");

        using var timer = new PeriodicTimer(_checkInterval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckLivenessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking agent liveness");
            }
        }

        _logger.LogInformation("Agent Liveness background service stopped");
    }

    private async Task CheckLivenessAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var cutoff = DateTimeOffset.UtcNow - _offlineThreshold;

        var offlineCandidateHosts = await db.Hosts
            .Where(h => h.Agent.Installed && h.Agent.LastSeenAt != null && h.Agent.LastSeenAt < cutoff)
            .ToListAsync(cancellationToken);

        foreach (var host in offlineCandidateHosts)
        {
            if (_connectionManager.IsOnline(host.Id))
            {
                // Active socket still registered, do not mark offline
                continue;
            }

            _logger.LogDebug(
                "Host {Hostname} ({HostId}) is offline (last seen: {LastSeenAt})",
                host.Hostname,
                host.Id,
                host.Agent.LastSeenAt
            );
        }
    }
}
