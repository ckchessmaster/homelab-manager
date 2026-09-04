using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Api.Features.Cluster;
using ControlPlane.Api.Security;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Cli.Synchronization;

public class LeaseManager
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LeaseManager> _logger;

    public LeaseManager(HttpClient httpClient, ILogger<LeaseManager> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> AcquireLeaseAsync(string clusterUrl, string apiKey, string holderIdentifier, int durationMinutes = 60, CancellationToken ct = default)
    {
        _logger.LogInformation("Acquiring distributed lease '{LeaseKey}' on cluster {ClusterUrl} for holder '{Holder}' ({DurationMinutes}m)...",
            ClusterEndpoints.GlobalMaintenanceLockKey, clusterUrl, holderIdentifier, durationMinutes);

        var requestUri = $"{clusterUrl.TrimEnd('/')}/api/v1/cluster/lease-acquire";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add(ApiKeyAuthenticationOptions.DefaultHeaderName, apiKey);
        request.Content = JsonContent.Create(new LeaseAcquireRequest(holderIdentifier, durationMinutes));

        using var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var conflictBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Failed to acquire maintenance lock: Lease conflict. Details: {Conflict}", conflictBody);
            return false;
        }

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Successfully acquired '{LeaseKey}' lease. Cluster pod has entered suspended state.", ClusterEndpoints.GlobalMaintenanceLockKey);
        return true;
    }

    public async Task<bool> ReleaseLeaseAsync(string clusterUrl, string apiKey, string holderIdentifier, CancellationToken ct = default)
    {
        _logger.LogInformation("Releasing distributed lease '{LeaseKey}' on cluster {ClusterUrl} for holder '{Holder}'...",
            ClusterEndpoints.GlobalMaintenanceLockKey, clusterUrl, holderIdentifier);

        var requestUri = $"{clusterUrl.TrimEnd('/')}/api/v1/cluster/lease-release";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add(ApiKeyAuthenticationOptions.DefaultHeaderName, apiKey);
        request.Content = JsonContent.Create(new LeaseReleaseRequest(holderIdentifier));

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Lease release returned status {StatusCode}.", response.StatusCode);
            return false;
        }

        _logger.LogInformation("Successfully released '{LeaseKey}' lease. Cluster pod restored to active primary mode.", ClusterEndpoints.GlobalMaintenanceLockKey);
        return true;
    }
}
