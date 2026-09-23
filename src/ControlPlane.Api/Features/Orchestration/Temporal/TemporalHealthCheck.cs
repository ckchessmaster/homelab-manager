using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Temporalio.Api.WorkflowService.V1;
using Temporalio.Client;

namespace ControlPlane.Api.Features.Orchestration.Temporal;

/// <summary>
/// Health check that verifies gRPC connectivity to the configured Temporal endpoint and confirms
/// that the target namespace (e.g. "homelab-manager") exists and is active.
/// </summary>
public class TemporalHealthCheck : IHealthCheck
{
    private readonly ITemporalClient? _client;
    private readonly IOptions<TemporalOptions> _options;
    private readonly ILogger<TemporalHealthCheck> _logger;

    public TemporalHealthCheck(
        IOptions<TemporalOptions> options,
        ILogger<TemporalHealthCheck> logger,
        ITemporalClient? client = null)
    {
        _options = options;
        _logger = logger;
        _client = client;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = _options.Value;

        if (!options.Enabled)
        {
            return HealthCheckResult.Healthy("Temporal orchestration is disabled.");
        }

        if (_client == null)
        {
            return HealthCheckResult.Unhealthy("Temporal client is not registered or initialized in the service container.");
        }

        var data = new Dictionary<string, object>
        {
            ["endpoint"] = options.Endpoint,
            ["namespace"] = options.Namespace,
            ["taskQueue"] = options.TaskQueue,
            ["authEnabled"] = options.Auth.Enabled
        };

        try
        {
            var req = new DescribeNamespaceRequest
            {
                Namespace = options.Namespace
            };

            var resp = await _client.WorkflowService.DescribeNamespaceAsync(
                req,
                new RpcOptions { CancellationToken = cancellationToken });

            var state = resp.NamespaceInfo?.State.ToString() ?? "Registered";
            data["namespaceState"] = state;

            return HealthCheckResult.Healthy(
                $"Temporal cluster reachable at {options.Endpoint}; namespace '{options.Namespace}' is {state}.",
                data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Temporal health probe failed for endpoint '{Endpoint}' and namespace '{Namespace}'",
                options.Endpoint, options.Namespace);

            return HealthCheckResult.Unhealthy(
                $"Temporal connection or namespace validation failed for '{options.Endpoint}' (namespace: '{options.Namespace}'): {ex.Message}",
                ex,
                data);
        }
    }
}
