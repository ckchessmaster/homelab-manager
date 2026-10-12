using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ControlPlane.Api.Features.Agents.Messaging;
using ControlPlane.Api.Features.Agents.Models;

namespace ControlPlane.Api.Features.Agents;

public class AgentSession
{
    public Guid HostId { get; init; }
    public string NodeId { get; init; } = string.Empty;
    public WebSocket? Socket { get; init; }
    public bool IsSimulated { get; init; }
    public DateTimeOffset ConnectedAt { get; init; } = DateTimeOffset.UtcNow;
    public SemaphoreSlim SendLock { get; } = new(1, 1);
    public AgentMetrics? LatestMetrics { get; set; }
    public AgentHardwareSummary? LatestHardware { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public string? KernelVersion { get; set; }
    public string? InboundHost { get; init; }
    public string? InboundScheme { get; init; }
}

public class AgentConnectionManager
{
    private readonly ConcurrentDictionary<Guid, AgentSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, RemoteAgentSession> _remoteSessions = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<AgentSession>> _reconnectWaiters = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<string>> _rebootCommencingWaiters = new();
    private readonly IAgentMessageBus? _messageBus;
    private readonly ILogger<AgentConnectionManager> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public event Action<Guid, string?>? OnRebootCommencing;

    public AgentConnectionManager(
        ILogger<AgentConnectionManager> logger,
        IAgentMessageBus? messageBus = null)
    {
        _logger = logger;
        _messageBus = messageBus;

        if (_messageBus != null)
        {
            _messageBus.OnPresenceReceived += HandleRemotePresenceAsync;
            _messageBus.OnCommandReceived += HandleRemoteCommandAsync;
        }
    }

    public AgentSession? GetSession(Guid hostId)
    {
        _sessions.TryGetValue(hostId, out var session);
        return session;
    }

    public AgentSession Register(Guid hostId, string nodeId, WebSocket socket, string? inboundHost = null, string? inboundScheme = null)
    {
        var session = new AgentSession
        {
            HostId = hostId,
            NodeId = nodeId,
            Socket = socket,
            ConnectedAt = DateTimeOffset.UtcNow,
            InboundHost = inboundHost,
            InboundScheme = inboundScheme
        };

        _sessions.AddOrUpdate(hostId, session, (key, oldSession) =>
        {
            try
            {
                if (oldSession.Socket?.State == WebSocketState.Open)
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

        _remoteSessions.TryRemove(hostId, out _);

        _logger.LogInformation("Registered agent session for host {HostId} (Node: {NodeId})", hostId, nodeId);

        // Notify other pods of local registration
        if (_messageBus != null)
        {
            _ = _messageBus.PublishPresenceAsync(hostId, "Connected");
        }

        // Resolve any waiters awaiting reconnection for this host
        if (_reconnectWaiters.TryRemove(hostId, out var waiter))
        {
            waiter.TrySetResult(session);
        }

        return session;
    }

    public AgentSession RegisterSimulated(Guid hostId, string nodeId)
    {
        var session = new AgentSession
        {
            HostId = hostId,
            NodeId = nodeId,
            IsSimulated = true,
            ConnectedAt = DateTimeOffset.UtcNow
        };

        _sessions[hostId] = session;
        _logger.LogInformation("Registered simulated agent session for host {HostId} (Node: {NodeId})", hostId, nodeId);

        if (_messageBus != null)
        {
            _ = _messageBus.PublishPresenceAsync(hostId, "Connected");
        }

        if (_reconnectWaiters.TryRemove(hostId, out var waiter))
        {
            waiter.TrySetResult(session);
        }

        return session;
    }

    public bool Unregister(Guid hostId, WebSocket? socket = null)
    {
        if (_sessions.TryGetValue(hostId, out var currentSession))
        {
            if (socket != null && !ReferenceEquals(currentSession.Socket, socket))
            {
                _logger.LogDebug("Ignoring unregister for host {HostId} because active session belongs to a different socket", hostId);
                return false;
            }

            if (((ICollection<KeyValuePair<Guid, AgentSession>>)_sessions).Remove(new KeyValuePair<Guid, AgentSession>(hostId, currentSession)))
            {
                _logger.LogInformation("Unregistered agent session for host {HostId}", hostId);

                if (_messageBus != null)
                {
                    _ = _messageBus.PublishPresenceAsync(hostId, "Disconnected");
                }

                return true;
            }
        }
        return false;
    }

    public bool IsOnline(Guid hostId)
    {
        if (_sessions.TryGetValue(hostId, out var session))
        {
            if (session.IsSimulated) return true;
            return session.Socket != null && session.Socket.State == WebSocketState.Open;
        }

        if (_remoteSessions.TryGetValue(hostId, out var remote))
        {
            return DateTimeOffset.UtcNow - remote.LastSeenAt < TimeSpan.FromSeconds(45);
        }

        return false;
    }

    public IReadOnlyCollection<Guid> GetOnlineHostIds()
    {
        var localOnline = _sessions
            .Where(s => s.Value.IsSimulated || (s.Value.Socket != null && s.Value.Socket.State == WebSocketState.Open))
            .Select(s => s.Key);

        var remoteOnline = _remoteSessions
            .Where(r => DateTimeOffset.UtcNow - r.Value.LastSeenAt < TimeSpan.FromSeconds(45))
            .Select(r => r.Key);

        return localOnline.Union(remoteOnline).ToList();
    }

    public void UpdateMetrics(Guid hostId, AgentMetrics? metrics)
    {
        if (_sessions.TryGetValue(hostId, out var session))
        {
            session.LatestMetrics = metrics;
            session.LastHeartbeatAt = DateTimeOffset.UtcNow;
        }
    }

    public void UpdateHeartbeat(Guid hostId, AgentHeartbeatMessage heartbeat)
    {
        if (_sessions.TryGetValue(hostId, out var session))
        {
            session.LatestMetrics = heartbeat.Metrics;
            session.LastHeartbeatAt = DateTimeOffset.UtcNow;
            if (heartbeat.Hardware != null)
            {
                session.LatestHardware = heartbeat.Hardware;
            }
            if (!string.IsNullOrWhiteSpace(heartbeat.KernelVersion))
            {
                session.KernelVersion = heartbeat.KernelVersion;
            }

            if (_messageBus != null)
            {
                _ = _messageBus.PublishPresenceAsync(hostId, "Heartbeat");
            }
        }
    }

    public void SyncRemotePresence(IEnumerable<(Guid HostId, DateTimeOffset LastSeenAt)> activeHosts)
    {
        foreach (var (hostId, lastSeenAt) in activeHosts)
        {
            if (!_sessions.ContainsKey(hostId))
            {
                _remoteSessions.AddOrUpdate(
                    hostId,
                    _ => new RemoteAgentSession(hostId, "remote", lastSeenAt),
                    (_, existing) => lastSeenAt > existing.LastSeenAt ? new RemoteAgentSession(hostId, existing.PodId, lastSeenAt) : existing
                );
            }
        }
    }

    public string? GetKernelVersion(Guid hostId)
    {
        if (_sessions.TryGetValue(hostId, out var session))
        {
            return session.KernelVersion;
        }
        return null;
    }

    public AgentMetrics? GetLatestMetrics(Guid hostId)
    {
        if (_sessions.TryGetValue(hostId, out var session))
        {
            return session.LatestMetrics;
        }
        return null;
    }

    public AgentHardwareSummary? GetLatestHardware(Guid hostId)
    {
        if (_sessions.TryGetValue(hostId, out var session))
        {
            return session.LatestHardware;
        }
        return null;
    }

    public async Task<bool> SendCommandAsync(Guid hostId, AgentCommandEnvelope command, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(hostId, out var session))
        {
            return await SendLocalCommandAsync(session, hostId, command, ct);
        }

        // Host is not connected to this pod locally. Forward via message bus if available and host is online
        if (_messageBus != null && IsOnline(hostId))
        {
            _logger.LogInformation("Broadcasting command {CommandType} for host {HostId} across pods via message bus", command.Type, hostId);
            await _messageBus.PublishCommandAsync(hostId, command, ct);
            return true;
        }

        _logger.LogWarning("Cannot send command to host {HostId}: agent is not connected on any pod", hostId);
        return false;
    }

    private async Task<bool> SendLocalCommandAsync(AgentSession session, Guid hostId, AgentCommandEnvelope command, CancellationToken ct)
    {
        if (session.IsSimulated)
        {
            _logger.LogInformation("Dispatched simulated command {CommandType} to host {HostId}", command.Type, hostId);
            return true;
        }

        if (session.Socket == null || session.Socket.State != WebSocketState.Open)
        {
            _logger.LogWarning("Cannot send command to host {HostId}: agent socket is not open", hostId);
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

    private Task HandleRemotePresenceAsync(AgentPresencePayload payload)
    {
        if (payload.State is "Connected" or "Heartbeat")
        {
            if (!_sessions.ContainsKey(payload.HostId))
            {
                _remoteSessions[payload.HostId] = new RemoteAgentSession(payload.HostId, payload.PodId, payload.Timestamp);
            }

            if (_reconnectWaiters.TryRemove(payload.HostId, out var waiter))
            {
                waiter.TrySetResult(new AgentSession
                {
                    HostId = payload.HostId,
                    NodeId = payload.PodId,
                    IsSimulated = true
                });
            }
        }
        else if (payload.State == "Disconnected")
        {
            _remoteSessions.TryRemove(payload.HostId, out _);
        }
        else if (payload.State.StartsWith("RebootCommencing", StringComparison.OrdinalIgnoreCase))
        {
            var parts = payload.State.Split(':', 2);
            var jobId = parts.Length > 1 ? parts[1] : null;
            NotifyRebootCommencing(payload.HostId, jobId, broadcast: false);
        }

        return Task.CompletedTask;
    }

    private async Task HandleRemoteCommandAsync(AgentCommandPayload payload)
    {
        if (_sessions.TryGetValue(payload.TargetHostId, out var session))
        {
            _logger.LogInformation("Executing inter-pod forwarded command {CommandType} for host {HostId}", payload.Command.Type, payload.TargetHostId);
            await SendLocalCommandAsync(session, payload.TargetHostId, payload.Command, CancellationToken.None);
        }
    }

    public async Task<AgentSession> WaitForReconnectAsync(Guid hostId, TimeSpan timeout, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(hostId, out var existingSession) && existingSession.IsSimulated)
        {
            return existingSession;
        }

        var tcs = _reconnectWaiters.GetOrAdd(hostId, _ => new TaskCompletionSource<AgentSession>(TaskCreationOptions.RunContinuationsAsynchronously));

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);

        using var registration = linkedCts.Token.Register(() =>
        {
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetCanceled(ct);
            }
            else
            {
                tcs.TrySetException(new TimeoutException($"Agent for host {hostId} did not reconnect within {timeout.TotalSeconds} seconds."));
            }
            _reconnectWaiters.TryRemove(hostId, out _);
        });

        try
        {
            return await tcs.Task;
        }
        finally
        {
            _reconnectWaiters.TryRemove(hostId, out _);
        }
    }

    public void NotifyRebootCommencing(Guid hostId, string? jobId, bool broadcast = true)
    {
        _logger.LogInformation("Agent acknowledged reboot commencing for host {HostId} (Job: {JobId})", hostId, jobId);
        if (_rebootCommencingWaiters.TryRemove(hostId, out var waiter))
        {
            waiter.TrySetResult(jobId ?? string.Empty);
        }
        OnRebootCommencing?.Invoke(hostId, jobId);

        if (broadcast && _messageBus != null)
        {
            _ = _messageBus.PublishPresenceAsync(hostId, $"RebootCommencing:{jobId}");
        }
    }

    public async Task<bool> WaitForRebootCommencingAsync(Guid hostId, TimeSpan timeout, CancellationToken ct = default)
    {
        var tcs = _rebootCommencingWaiters.GetOrAdd(hostId, _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);

        using var registration = linkedCts.Token.Register(() =>
        {
            tcs.TrySetResult(string.Empty);
            _rebootCommencingWaiters.TryRemove(hostId, out _);
        });

        try
        {
            var result = await tcs.Task;
            return !string.IsNullOrEmpty(result) || tcs.Task.IsCompletedSuccessfully;
        }
        finally
        {
            _rebootCommencingWaiters.TryRemove(hostId, out _);
        }
    }
}
