using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ControlPlane.Api.Features.Agents.Models;

namespace ControlPlane.Api.Features.Agents;

public class AgentSession
{
    public Guid HostId { get; init; }
    public string NodeId { get; init; } = string.Empty;
    public WebSocket Socket { get; init; } = null!;
    public DateTimeOffset ConnectedAt { get; init; } = DateTimeOffset.UtcNow;
    public SemaphoreSlim SendLock { get; } = new(1, 1);
    public AgentMetrics? LatestMetrics { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
}

public class AgentConnectionManager
{
    private readonly ConcurrentDictionary<Guid, AgentSession> _sessions = new();
    private readonly ILogger<AgentConnectionManager> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AgentConnectionManager(ILogger<AgentConnectionManager> logger)
    {
        _logger = logger;
    }

    public AgentSession Register(Guid hostId, string nodeId, WebSocket socket)
    {
        var session = new AgentSession
        {
            HostId = hostId,
            NodeId = nodeId,
            Socket = socket,
            ConnectedAt = DateTimeOffset.UtcNow
        };

        _sessions.AddOrUpdate(hostId, session, (key, oldSession) =>
        {
            try
            {
                if (oldSession.Socket.State == WebSocketState.Open)
                {
                    _ = oldSession.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Replaced by new connection", CancellationToken.None);
                }
            }
            catch
            {
                // ignore
            }
            return session;
        });

        _logger.LogInformation("Registered agent session for host {HostId} (Node: {NodeId})", hostId, nodeId);
        return session;
    }

    public bool Unregister(Guid hostId)
    {
        if (_sessions.TryRemove(hostId, out var session))
        {
            _logger.LogInformation("Unregistered agent session for host {HostId}", hostId);
            return true;
        }
        return false;
    }

    public bool IsOnline(Guid hostId)
    {
        if (_sessions.TryGetValue(hostId, out var session))
        {
            return session.Socket.State == WebSocketState.Open;
        }
        return false;
    }

    public IReadOnlyCollection<Guid> GetOnlineHostIds()
    {
        return _sessions
            .Where(s => s.Value.Socket.State == WebSocketState.Open)
            .Select(s => s.Key)
            .ToList();
    }

    public void UpdateMetrics(Guid hostId, AgentMetrics? metrics)
    {
        if (_sessions.TryGetValue(hostId, out var session))
        {
            session.LatestMetrics = metrics;
            session.LastHeartbeatAt = DateTimeOffset.UtcNow;
        }
    }

    public AgentMetrics? GetLatestMetrics(Guid hostId)
    {
        if (_sessions.TryGetValue(hostId, out var session))
        {
            return session.LatestMetrics;
        }
        return null;
    }

    public async Task<bool> SendCommandAsync(Guid hostId, AgentCommandEnvelope command, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(hostId, out var session) || session.Socket.State != WebSocketState.Open)
        {
            _logger.LogWarning("Cannot send command to host {HostId}: agent is not connected", hostId);
            return false;
        }

        var json = JsonSerializer.Serialize(command, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        await session.SendLock.WaitAsync(ct);
        try
        {
            if (session.Socket.State != WebSocketState.Open)
            {
                return false;
            }

            await session.Socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                ct
            );
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command to host {HostId}", hostId);
            return false;
        }
        finally
        {
            session.SendLock.Release();
        }
    }
}
