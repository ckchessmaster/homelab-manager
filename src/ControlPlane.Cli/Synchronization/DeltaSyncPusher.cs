using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlPlane.Api.Features.Cluster;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Cli.Synchronization;

public class DeltaSyncPusher
{
    private readonly HttpClient _httpClient;
    private readonly LeaseManager _leaseManager;
    private readonly ILogger<DeltaSyncPusher> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public DeltaSyncPusher(HttpClient httpClient, LeaseManager leaseManager, ILogger<DeltaSyncPusher> logger)
    {
        _httpClient = httpClient;
        _leaseManager = leaseManager;
        _logger = logger;
    }

    public async Task<bool> ProbeClusterAvailableAsync(string clusterUrl, string apiKey, CancellationToken ct = default)
    {
        try
        {
            var requestUri = $"{clusterUrl.TrimEnd('/')}/api/v1/cluster/status";
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Add(ApiKeyAuthenticationOptions.DefaultHeaderName, apiKey);

            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cluster probe failed: cluster not yet reachable.");
            return false;
        }
    }

    public async Task<bool> PushDeltaAsync(string clusterUrl, string apiKey, ControlPlaneDbContext localDb, DateTimeOffset? sinceTimestamp = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Gathering execution deltas from local SQLite database...");

        var hosts = await localDb.Hosts.AsNoTracking().ToListAsync(ct);
        var jobsQuery = localDb.UpdateJobs.AsNoTracking().AsQueryable();
        var logsQuery = localDb.StepLogs.AsNoTracking().AsQueryable();

        if (sinceTimestamp.HasValue)
        {
            jobsQuery = jobsQuery.Where(j => (j.StartedAt != null && j.StartedAt >= sinceTimestamp.Value) ||
                                             (j.CompletedAt != null && j.CompletedAt >= sinceTimestamp.Value));
            logsQuery = logsQuery.Where(l => l.Timestamp >= sinceTimestamp.Value);
        }

        var jobs = await jobsQuery.ToListAsync(ct);
        var logs = await logsQuery.ToListAsync(ct);

        _logger.LogInformation("Found {JobCount} jobs and {LogCount} step logs to reconcile to cluster.", jobs.Count, logs.Count);

        var payload = new DeltaSyncPayload(
            Hosts: hosts.Select(HostSnapshotDto.FromEntity).ToList(),
            UpdateJobs: jobs.Select(JobSnapshotDto.FromEntity).ToList(),
            StepLogs: logs.Select(StepLogSnapshotDto.FromEntity).ToList()
        );

        var requestUri = $"{clusterUrl.TrimEnd('/')}/api/v1/cluster/reconcile-delta";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add(ApiKeyAuthenticationOptions.DefaultHeaderName, apiKey);
        request.Content = JsonContent.Create(payload, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Delta synchronization failed with status {StatusCode}: {Error}", response.StatusCode, errorBody);
            return false;
        }

        _logger.LogInformation("Delta reconciliation completed successfully.");
        return true;
    }

    public async Task<bool> ReconcileAndReleaseAsync(
        string clusterUrl,
        string apiKey,
        string holderIdentifier,
        ControlPlaneDbContext localDb,
        DateTimeOffset? sinceTimestamp = null,
        CancellationToken ct = default)
    {
        var pushed = await PushDeltaAsync(clusterUrl, apiKey, localDb, sinceTimestamp, ct);
        if (!pushed)
        {
            _logger.LogError("Failed to push deltas; retaining lease '{LeaseKey}'.", ClusterEndpoints.GlobalMaintenanceLockKey);
            return false;
        }

        var released = await _leaseManager.ReleaseLeaseAsync(clusterUrl, apiKey, holderIdentifier, ct);
        return released;
    }
}
