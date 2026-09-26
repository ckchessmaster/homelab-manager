using System.Collections.Concurrent;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Agents;

public class MassAgentUpdateService
{
    private readonly ControlPlaneDbContext _db;
    private readonly AgentConnectionManager _connectionManager;
    private readonly AgentBinaryService _binaryService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MassAgentUpdateService> _logger;

    private static readonly ConcurrentDictionary<Guid, MassUpdateBatchResult> ActiveBatches = new();

    public MassAgentUpdateService(
        ControlPlaneDbContext db,
        AgentConnectionManager connectionManager,
        AgentBinaryService binaryService,
        IConfiguration configuration,
        ILogger<MassAgentUpdateService> logger)
    {
        _db = db;
        _connectionManager = connectionManager;
        _binaryService = binaryService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AgentVersionInfoDto> GetVersionInfoAsync(CancellationToken ct = default)
    {
        var targetVersion = _binaryService.GetTargetVersion();
        var hosts = await _db.Hosts.AsNoTracking().Where(h => h.Agent.Installed).ToListAsync(ct);

        var outdatedList = new List<OutdatedHostSummaryDto>();
        var onlineOutdatedCount = 0;

        foreach (var host in hosts)
        {
            var isOutdated = string.IsNullOrWhiteSpace(host.Agent.Version) ||
                             !string.Equals(host.Agent.Version.TrimStart('v'), targetVersion.TrimStart('v'), StringComparison.OrdinalIgnoreCase);

            if (isOutdated)
            {
                var isOnline = _connectionManager.IsOnline(host.Id);
                if (isOnline) onlineOutdatedCount++;

                outdatedList.Add(new OutdatedHostSummaryDto(
                    host.Id,
                    host.Hostname,
                    host.Agent.Version ?? "unknown",
                    isOnline
                ));
            }
        }

        return new AgentVersionInfoDto(
            ServerVersion: targetVersion,
            SupportedArchitectures: _binaryService.GetAvailableArchitectures(),
            TotalInstalledAgents: hosts.Count,
            OutdatedAgentsCount: outdatedList.Count,
            OnlineOutdatedCount: onlineOutdatedCount,
            OutdatedHosts: outdatedList
        );
    }

    public async Task<MassUpdateBatchResult> TriggerMassUpdateAsync(
        MassUpdateRequest request,
        string serverBaseUrl,
        CancellationToken ct = default)
    {
        var targetVersion = _binaryService.GetTargetVersion();
        var batchId = Guid.NewGuid();

        _logger.LogInformation("Initiating mass agent update batch {BatchId} to version {TargetVersion}...", batchId, targetVersion);

        var query = _db.Hosts.AsNoTracking().Where(h => h.Agent.Installed);

        if (request.HostIds != null && request.HostIds.Count > 0)
        {
            query = query.Where(h => request.HostIds.Contains(h.Id));
        }
        else if (request.AllOutdated)
        {
            query = query.Where(h => h.Agent.Version == null || h.Agent.Version != targetVersion);
        }

        var targets = await query.ToListAsync(ct);
        var details = new List<HostUpdateStatusDto>();
        var dispatched = 0;
        var skippedOffline = 0;

        foreach (var host in targets)
        {
            var isOnline = _connectionManager.IsOnline(host.Id);
            if (!isOnline)
            {
                skippedOffline++;
                details.Add(new HostUpdateStatusDto(
                    host.Id,
                    host.Hostname,
                    host.Agent.Version ?? "unknown",
                    targetVersion,
                    "SkippedOffline",
                    "Agent is currently offline; update cannot be dispatched in-band."
                ));
                continue;
            }

            // Determine architecture
            var arch = (host.OsFamily?.Contains("arm", StringComparison.OrdinalIgnoreCase) ?? false)
                ? "linux-arm64"
                : (host.OsFamily?.Contains("windows", StringComparison.OrdinalIgnoreCase) ?? false)
                    ? "windows-amd64"
                    : "linux-amd64";

            var session = _connectionManager.GetSession(host.Id);
            string effectiveBaseUrl;
            if (!string.IsNullOrWhiteSpace(session?.InboundHost))
            {
                var scheme = !string.IsNullOrWhiteSpace(session.InboundScheme) ? session.InboundScheme : "http";
                effectiveBaseUrl = $"{scheme}://{session.InboundHost}";
            }
            else
            {
                effectiveBaseUrl = ResolveLanAddress(serverBaseUrl, _configuration);
            }
            var downloadUrl = $"{effectiveBaseUrl.TrimEnd('/')}/api/v1/agents/binaries/{arch}";
            var jobId = Guid.NewGuid();

            var envelope = new AgentCommandEnvelope
            {
                Type = "CMD_SELF_UPDATE",
                JobId = jobId,
                Command = downloadUrl,
                Args = new[] { targetVersion },
                DownloadUrl = downloadUrl,
                TargetVersion = targetVersion
            };

            var sent = await _connectionManager.SendCommandAsync(host.Id, envelope, ct);
            if (sent)
            {
                dispatched++;
                details.Add(new HostUpdateStatusDto(
                    host.Id,
                    host.Hostname,
                    host.Agent.Version ?? "unknown",
                    targetVersion,
                    "Dispatched",
                    $"Self-update command dispatched to agent (target {targetVersion})."
                ));
            }
            else
            {
                details.Add(new HostUpdateStatusDto(
                    host.Id,
                    host.Hostname,
                    host.Agent.Version ?? "unknown",
                    targetVersion,
                    "Failed",
                    "Failed to transmit self-update envelope over active WebSocket."
                ));
            }
        }

        var batchResult = new MassUpdateBatchResult(
            BatchId: batchId,
            TotalTargeted: targets.Count,
            DispatchedCount: dispatched,
            SkippedOfflineCount: skippedOffline,
            Details: details
        );

        ActiveBatches[batchId] = batchResult;
        return batchResult;
    }

    public MassUpdateBatchResult? GetBatchStatus(Guid batchId)
    {
        ActiveBatches.TryGetValue(batchId, out var result);
        return result;
    }

    public static string ResolveLanAddress(string defaultUrl, IConfiguration? config = null)
    {
        var hubUrl = config?["ControlPlane:HubUrl"];
        if (!string.IsNullOrWhiteSpace(hubUrl) && Uri.TryCreate(hubUrl, UriKind.Absolute, out var hubUri))
        {
            var scheme = hubUri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
            return $"{scheme}://{hubUri.Authority}";
        }

        if (!defaultUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) && !defaultUrl.Contains("127.0.0.1"))
        {
            return defaultUrl;
        }

        try
        {
            using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is System.Net.IPEndPoint endPoint)
            {
                var uri = new Uri(defaultUrl);
                return $"{uri.Scheme}://{endPoint.Address}:{uri.Port}";
            }
        }
        catch
        {
            // fallback
        }
        return defaultUrl;
    }
}

public record MassUpdateRequest(
    List<Guid>? HostIds = null,
    bool AllOutdated = true
);

public record HostUpdateStatusDto(
    Guid HostId,
    string Hostname,
    string CurrentVersion,
    string TargetVersion,
    string Status,
    string? Message
);

public record MassUpdateBatchResult(
    Guid BatchId,
    int TotalTargeted,
    int DispatchedCount,
    int SkippedOfflineCount,
    List<HostUpdateStatusDto> Details
);

public record AgentVersionInfoDto(
    string ServerVersion,
    IReadOnlyList<string> SupportedArchitectures,
    int TotalInstalledAgents,
    int OutdatedAgentsCount,
    int OnlineOutdatedCount,
    List<OutdatedHostSummaryDto> OutdatedHosts
);

public record OutdatedHostSummaryDto(
    Guid HostId,
    string Hostname,
    string CurrentVersion,
    bool IsOnline
);
