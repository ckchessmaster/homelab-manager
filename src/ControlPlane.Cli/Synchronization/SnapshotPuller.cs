using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlPlane.Api.Features.Cluster;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Cli.Synchronization;

public class SnapshotPuller
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SnapshotPuller> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SnapshotPuller(HttpClient httpClient, ILogger<SnapshotPuller> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ClusterSnapshot> PullSnapshotAsync(string clusterUrl, string apiKey, CancellationToken ct = default)
    {
        _logger.LogInformation("Pulling snapshot from cluster at {ClusterUrl}...", clusterUrl);

        var requestUri = $"{clusterUrl.TrimEnd('/')}/api/v1/cluster/export-snapshot";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add(ApiKeyAuthenticationOptions.DefaultHeaderName, apiKey);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var snapshot = await response.Content.ReadFromJsonAsync<ClusterSnapshot>(JsonOptions, ct);
        if (snapshot == null)
        {
            throw new InvalidOperationException("Cluster returned empty or invalid snapshot payload.");
        }

        _logger.LogInformation("Successfully pulled snapshot with {HostCount} hosts, {JobCount} jobs, and {LogCount} step logs.",
            snapshot.Hosts.Count, snapshot.UpdateJobs.Count, snapshot.StepLogs.Count);

        return snapshot;
    }

    public async Task SeedLocalDatabaseAsync(ClusterSnapshot snapshot, ControlPlaneDbContext localDb, CancellationToken ct = default)
    {
        _logger.LogInformation("Ensuring local SQLite database schema exists...");
        await localDb.Database.EnsureCreatedAsync(ct);

        // Seed hosts
        foreach (var hostDto in snapshot.Hosts)
        {
            var exists = await localDb.Hosts.AnyAsync(h => h.Id == hostDto.Id, ct);
            if (!exists)
            {
                localDb.Hosts.Add(hostDto.ToEntity());
            }
        }
        await localDb.SaveChangesAsync(ct);

        // Seed update jobs
        foreach (var jobDto in snapshot.UpdateJobs)
        {
            var exists = await localDb.UpdateJobs.AnyAsync(j => j.Id == jobDto.Id, ct);
            if (!exists)
            {
                localDb.UpdateJobs.Add(jobDto.ToEntity());
            }
        }
        await localDb.SaveChangesAsync(ct);

        // Seed step logs
        foreach (var logDto in snapshot.StepLogs)
        {
            var exists = await localDb.StepLogs.AnyAsync(l => l.JobId == logDto.JobId && l.SequenceId == logDto.SequenceId, ct);
            if (!exists)
            {
                localDb.StepLogs.Add(logDto.ToEntity());
            }
        }
        await localDb.SaveChangesAsync(ct);

        _logger.LogInformation("Local SQLite database seeded successfully.");
    }
}
