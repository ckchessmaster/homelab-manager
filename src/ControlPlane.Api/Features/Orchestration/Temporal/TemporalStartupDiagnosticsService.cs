using ControlPlane.Api.Features.Orchestration.Temporal.Auth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Temporalio.Client;

namespace ControlPlane.Api.Features.Orchestration.Temporal;

/// <summary>
/// Hosted service that outputs diagnostic information during application startup regarding the
/// active Temporal server endpoint, target namespace, task queue, and Zitadel authentication status,
/// and primes the active Temporal client with an initial Zitadel M2M token if authentication is enabled.
/// </summary>
public class TemporalStartupDiagnosticsService : IHostedService
{
    private readonly IOptions<TemporalOptions> _options;
    private readonly ILogger<TemporalStartupDiagnosticsService> _logger;
    private readonly IZitadelTokenProvider? _tokenProvider;
    private readonly ITemporalClient? _client;

    public TemporalStartupDiagnosticsService(
        IOptions<TemporalOptions> options,
        ILogger<TemporalStartupDiagnosticsService> logger,
        IZitadelTokenProvider? tokenProvider = null,
        ITemporalClient? client = null)
    {
        _options = options;
        _logger = logger;
        _tokenProvider = tokenProvider;
        _client = client;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            _logger.LogInformation("[Temporal Engine] Temporal orchestration is DISABLED.");
            return;
        }

        if (opts.Auth.Enabled && _tokenProvider != null && _client != null)
        {
            try
            {
                var initialToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(initialToken))
                {
                    _client.Connection.ApiKey = initialToken;
                    _logger.LogInformation("[Temporal Engine] Primed active Temporal connection with initial Zitadel M2M token.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Temporal Engine] Could not prime initial Zitadel M2M token. Background refresh will retry.");
            }
        }

        var authStatus = opts.Auth.Enabled
            ? $"Enabled (Zitadel M2M via {opts.Auth.TokenUrl}, ClientId: {opts.Auth.ClientId})"
            : "Disabled (Insecure / Dev)";

        _logger.LogInformation(
            "\n===============================================================\n" +
            "[Temporal Engine] Configuration Diagnostics:\n" +
            "  * Target Endpoint : {Endpoint}\n" +
            "  * Namespace       : {Namespace}\n" +
            "  * Task Queue      : {TaskQueue}\n" +
            "  * Authentication  : {AuthStatus}\n" +
            "===============================================================",
            opts.Endpoint, opts.Namespace, opts.TaskQueue, authStatus);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
