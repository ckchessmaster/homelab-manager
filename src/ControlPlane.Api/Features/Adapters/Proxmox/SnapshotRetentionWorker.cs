using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Adapters.Proxmox;

/// <summary>
/// Background service that periodically sweeps and prunes expired Proxmox safety snapshots.
/// </summary>
public class SnapshotRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<SnapshotRetentionOptions> _options;
    private readonly ILogger<SnapshotRetentionWorker> _logger;

    public SnapshotRetentionWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<SnapshotRetentionOptions> options,
        ILogger<SnapshotRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = _options.Value;
        if (!config.Enabled)
        {
            _logger.LogInformation("Proxmox snapshot retention background worker is disabled via configuration.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, config.ScanIntervalMinutes));
        _logger.LogInformation("Proxmox snapshot retention background worker started (ScanInterval: {Interval}m, RetentionWindow: {RetentionHours}h)",
            config.ScanIntervalMinutes, config.RetentionHours);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var retentionService = scope.ServiceProvider.GetRequiredService<ISnapshotRetentionService>();

                var result = await retentionService.PruneExpiredSnapshotsAsync(hostId: null, dryRun: false, stoppingToken);

                if (result.PrunedCount > 0 || result.Errors.Count > 0)
                {
                    _logger.LogInformation("Snapshot retention sweep completed: {Pruned} pruned, {Skipped} skipped, {Errors} errors.",
                        result.PrunedCount, result.SkippedCount, result.Errors.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during periodic Proxmox snapshot retention sweep");
            }
        }

        _logger.LogInformation("Proxmox snapshot retention background worker stopped.");
    }
}
