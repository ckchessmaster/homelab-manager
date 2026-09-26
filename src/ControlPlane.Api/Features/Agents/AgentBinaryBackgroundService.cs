using ControlPlane.Api.Features.Agents.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Agents;

public class AgentBinaryBackgroundService : BackgroundService
{
    private readonly IAgentBinarySyncService _syncService;
    private readonly IOptions<AgentBinarySyncOptions> _options;
    private readonly ILogger<AgentBinaryBackgroundService> _logger;

    public AgentBinaryBackgroundService(
        IAgentBinarySyncService syncService,
        IOptions<AgentBinarySyncOptions> options,
        ILogger<AgentBinaryBackgroundService> logger)
    {
        _syncService = syncService;
        _options = options;
        _logger = logger;
    }

    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Agent binary background synchronization is disabled via configuration.");
            return;
        }

        if (_options.Value.CheckOnStartup)
        {
            try
            {
                // Wait briefly for app startup to stabilize
                await Task.Delay(StartupDelay, stoppingToken);
                _logger.LogInformation("Executing startup agent binary synchronization check...");
                await _syncService.SyncBinariesAsync(force: false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to complete initial agent binary startup check.");
            }
        }

        var intervalHours = Math.Max(1, _options.Value.SyncIntervalHours);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));
        _logger.LogInformation("Agent binary periodic synchronization scheduled every {Hours} hours.", intervalHours);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation("Executing periodic scheduled agent binary synchronization...");
                await _syncService.SyncBinariesAsync(force: false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Periodic agent binary synchronization check encountered an error.");
            }
        }
    }
}
