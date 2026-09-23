using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Temporalio.Client;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Auth;

/// <summary>
/// Background service that proactively rotates the Zitadel M2M Bearer token on the active ITemporalClient
/// connection prior to token expiration.
/// </summary>
public class TemporalTokenRefreshService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<TemporalOptions> _options;
    private readonly ILogger<TemporalTokenRefreshService> _logger;
    private readonly TimeSpan _checkInterval;

    public TemporalTokenRefreshService(
        IServiceProvider serviceProvider,
        IOptions<TemporalOptions> options,
        ILogger<TemporalTokenRefreshService> logger,
        TimeSpan? checkInterval = null)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
        _checkInterval = checkInterval ?? TimeSpan.FromMinutes(2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Auth.Enabled)
        {
            _logger.LogDebug("Temporal Zitadel M2M authentication is disabled; skipping token refresh service.");
            return;
        }

        _logger.LogInformation("Starting Temporal Zitadel M2M token refresh service (interval: {Interval}).", _checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);

                using var scope = _serviceProvider.CreateScope();
                var tokenProvider = scope.ServiceProvider.GetService<IZitadelTokenProvider>();
                var client = scope.ServiceProvider.GetService<ITemporalClient>();

                if (tokenProvider != null && client != null)
                {
                    var token = await tokenProvider.GetAccessTokenAsync(stoppingToken);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        client.Connection.ApiKey = token;
                        _logger.LogDebug("Refreshed Zitadel M2M token on active Temporal connection.");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while executing Temporal Zitadel M2M token refresh.");
            }
        }
    }
}
