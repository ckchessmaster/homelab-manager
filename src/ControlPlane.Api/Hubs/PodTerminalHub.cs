using System.Collections.Concurrent;
using System.Security.Claims;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ControlPlane.Api.Hubs;

public sealed record PodTerminalAttachRequest(
    string Namespace = "",
    string PodName = "",
    string? ClusterId = null,
    string? Container = null,
    string? Shell = null,
    int Cols = 80,
    int Rows = 24,
    string? NamespaceName = null);

public sealed record PodTerminalAttachedEvent(
    Guid SessionId,
    string ClusterId,
    string Namespace,
    string PodName,
    string? Container,
    string ResolvedShell,
    int Cols,
    int Rows,
    DateTimeOffset ConnectedAtUtc);

public sealed record PodTerminalAttachResult(
    bool Succeeded,
    Guid? SessionId = null,
    string? ClusterId = null,
    string? Namespace = null,
    string? PodName = null,
    string? Container = null,
    string? ResolvedShell = null,
    string? Error = null);

/// <summary>
/// Host-scoped registry of active SignalR pod terminal sessions keyed by SignalR <c>ConnectionId</c>.
/// Ensures transient <see cref="PodTerminalHub"/> invocations share session state safely and disposes
/// any remaining sessions cleanly on application shutdown.
/// </summary>
public sealed class PodTerminalSessionRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, TrackedPodTerminalSession> _sessions = new(StringComparer.Ordinal);

    public int ActiveSessionCount => _sessions.Count;

    internal TrackedPodTerminalSession? SetAndReplace(string connectionId, TrackedPodTerminalSession next)
    {
        TrackedPodTerminalSession? previous = null;
        _sessions.AddOrUpdate(
            connectionId,
            next,
            (_, existing) =>
            {
                previous = existing;
                return next;
            });
        return previous;
    }

    internal bool TryGet(string connectionId, out TrackedPodTerminalSession? session)
        => _sessions.TryGetValue(connectionId, out session);

    internal bool TryRemove(string connectionId, out TrackedPodTerminalSession? session)
        => _sessions.TryRemove(connectionId, out session);

    internal bool TryRemoveSpecific(string connectionId, Guid sessionId, out TrackedPodTerminalSession? removed)
    {
        while (_sessions.TryGetValue(connectionId, out var current))
        {
            if (current.SessionId != sessionId)
            {
                removed = null;
                return false;
            }

            if (_sessions.TryRemove(KeyValuePair.Create(connectionId, current)))
            {
                removed = current;
                return true;
            }
        }

        removed = null;
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var kvp in _sessions.ToArray())
        {
            if (_sessions.TryRemove(kvp.Key, out var entry))
            {
                entry.Lifecycle.TryClaimClose(markSuperseded: true);
                try
                {
                    await entry.Session.DisposeAsync();
                }
                catch
                {
                    // Best-effort cleanup on shutdown
                }
            }
        }
    }
}

internal sealed class PodTerminalSessionLifecycle
{
    private readonly TaskCompletionSource _registrationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _closed;
    private volatile bool _superseded;

    public PodTerminalSessionLifecycle(
        Guid sessionId,
        string connectionId,
        DateTimeOffset startedAtUtc,
        string principalName,
        IReadOnlyList<string> roles,
        string? authMethod,
        string? remoteIp,
        string clusterId,
        string namespaceName,
        string podName,
        string? container,
        string? shell)
    {
        SessionId = sessionId;
        ConnectionId = connectionId;
        StartedAtUtc = startedAtUtc;
        PrincipalName = principalName;
        Roles = roles;
        AuthMethod = authMethod;
        RemoteIp = remoteIp;
        ClusterId = clusterId;
        Namespace = namespaceName;
        PodName = podName;
        Container = container;
        Shell = shell;
    }

    public Guid SessionId { get; }
    public string ConnectionId { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public string PrincipalName { get; }
    public IReadOnlyList<string> Roles { get; }
    public string? AuthMethod { get; }
    public string? RemoteIp { get; }

    public string ClusterId { get; private set; }
    public string Namespace { get; private set; }
    public string PodName { get; private set; }
    public string? Container { get; private set; }
    public string? Shell { get; private set; }

    public bool IsSuperseded => _superseded;

    public void UpdateResolvedTarget(string clusterId, string namespaceName, string podName, string? container, string resolvedShell)
    {
        ClusterId = clusterId;
        Namespace = namespaceName;
        PodName = podName;
        Container = container;
        Shell = resolvedShell;
    }

    public bool TryClaimClose(bool markSuperseded = false)
    {
        if (markSuperseded)
        {
            _superseded = true;
        }

        return Interlocked.Exchange(ref _closed, 1) == 0;
    }

    public void CompleteRegistration() => _registrationCompleted.TrySetResult();

    public Task WaitForRegistrationAsync() => _registrationCompleted.Task;
}

internal sealed record TrackedPodTerminalSession(
    Guid SessionId,
    IPodExecSession Session,
    PodTerminalSessionLifecycle Lifecycle);

[Authorize(Policy = AuthConstants.RequirePodExec)]
public sealed class PodTerminalHub(
    IHubContext<PodTerminalHub> hubContext,
    IPodExecService podExecService,
    PodTerminalSessionRegistry sessionRegistry,
    IPodExecAuditLogger auditLogger,
    ILogger<PodTerminalHub> logger) : Hub
{
    public async Task<PodTerminalAttachResult> Attach(PodTerminalAttachRequest? request)
    {
        var connectionId = Context.ConnectionId;
        var (principalName, roles, authMethod, remoteIp) = ExtractCallerAuditContext(Context);
        var sessionId = Guid.NewGuid();
        var startedAtUtc = DateTimeOffset.UtcNow;

        var rawNamespace = !string.IsNullOrWhiteSpace(request?.Namespace)
            ? request.Namespace
            : request?.NamespaceName;
        var namespaceName = rawNamespace?.Trim() ?? string.Empty;
        var podName = request?.PodName?.Trim() ?? string.Empty;
        var requestedClusterId = string.IsNullOrWhiteSpace(request?.ClusterId) ? null : request.ClusterId.Trim();
        var effectiveClusterLabel = requestedClusterId ?? KubernetesPodExecService.DefaultClusterKey;
        var requestedContainer = string.IsNullOrWhiteSpace(request?.Container) ? null : request.Container.Trim();
        var requestedShell = string.IsNullOrWhiteSpace(request?.Shell) ? null : request.Shell.Trim();
        var cols = Math.Clamp(request is { Cols: > 0 } ? request.Cols : 80, 10, 500);
        var rows = Math.Clamp(request is { Rows: > 0 } ? request.Rows : 24, 2, 200);

        if (string.IsNullOrWhiteSpace(namespaceName) || string.IsNullOrWhiteSpace(podName))
        {
            const string validationError = "Both namespace and podName are required to attach a pod terminal.";
            auditLogger.LogAttachFailed(new PodExecAuditEntry(
                SessionId: sessionId,
                Action: PodExecAuditActions.AttachFailed,
                ConnectionId: connectionId,
                PrincipalName: principalName,
                Roles: roles,
                AuthMethod: authMethod,
                RemoteIp: remoteIp,
                ClusterId: effectiveClusterLabel,
                Namespace: string.IsNullOrWhiteSpace(namespaceName) ? "<missing>" : namespaceName,
                PodName: string.IsNullOrWhiteSpace(podName) ? "<missing>" : podName,
                Container: requestedContainer,
                Shell: requestedShell,
                TimestampUtc: startedAtUtc,
                Reason: validationError));

            await NotifyAttachErrorAsync(connectionId, validationError);
            return new PodTerminalAttachResult(Succeeded: false, Error: validationError);
        }

        if (sessionRegistry.TryRemove(connectionId, out var existing) && existing != null)
        {
            await CloseTrackedSessionAsync(existing, exitCode: null, reason: "Reattached");
        }

        var lifecycle = new PodTerminalSessionLifecycle(
            sessionId: sessionId,
            connectionId: connectionId,
            startedAtUtc: startedAtUtc,
            principalName: principalName,
            roles: roles,
            authMethod: authMethod,
            remoteIp: remoteIp,
            clusterId: effectiveClusterLabel,
            namespaceName: namespaceName,
            podName: podName,
            container: requestedContainer,
            shell: requestedShell);

        // Route asynchronous background output/close events through singleton IHubContext<PodTerminalHub>
        // so streaming remains safe after this ephemeral Hub instance is disposed when Attach returns.
        Func<string, Task> onOutput = async text =>
        {
            if (string.IsNullOrEmpty(text) || lifecycle.IsSuperseded)
            {
                return;
            }

            try
            {
                await hubContext.Clients.Client(connectionId).SendAsync("Output", text);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to stream pod terminal output to connection {ConnectionId}", connectionId);
            }
        };

        Func<int, Task> onClosed = async exitCode =>
        {
            await lifecycle.WaitForRegistrationAsync();
            var wasSuperseded = lifecycle.IsSuperseded;
            if (!lifecycle.TryClaimClose())
            {
                return;
            }

            sessionRegistry.TryRemoveSpecific(connectionId, sessionId, out _);

            var now = DateTimeOffset.UtcNow;
            auditLogger.LogSessionEnded(new PodExecAuditEntry(
                SessionId: sessionId,
                Action: PodExecAuditActions.SessionEnded,
                ConnectionId: connectionId,
                PrincipalName: lifecycle.PrincipalName,
                Roles: lifecycle.Roles,
                AuthMethod: lifecycle.AuthMethod,
                RemoteIp: lifecycle.RemoteIp,
                ClusterId: lifecycle.ClusterId,
                Namespace: lifecycle.Namespace,
                PodName: lifecycle.PodName,
                Container: lifecycle.Container,
                Shell: lifecycle.Shell,
                TimestampUtc: now,
                Duration: now - lifecycle.StartedAtUtc,
                ExitCode: exitCode,
                Reason: "RemoteExited"));

            if (!wasSuperseded)
            {
                try
                {
                    await hubContext.Clients.Client(connectionId).SendAsync("Closed", exitCode);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to send pod terminal Closed event to connection {ConnectionId}", connectionId);
                }
            }
        };

        try
        {
            var session = await podExecService.AttachAsync(
                new PodExecRequest(
                    Namespace: namespaceName,
                    PodName: podName,
                    ClusterId: requestedClusterId,
                    Container: requestedContainer,
                    Shell: requestedShell,
                    Cols: cols,
                    Rows: rows),
                onOutput: onOutput,
                onClosed: onClosed,
                ct: Context.ConnectionAborted);

            lifecycle.UpdateResolvedTarget(
                session.ClusterId,
                session.Namespace,
                session.PodName,
                session.Container,
                session.ResolvedShell);

            var tracked = new TrackedPodTerminalSession(sessionId, session, lifecycle);
            var concurrentReplaced = sessionRegistry.SetAndReplace(connectionId, tracked);
            if (concurrentReplaced != null)
            {
                await CloseTrackedSessionAsync(concurrentReplaced, exitCode: null, reason: "Reattached");
            }

            auditLogger.LogSessionStarted(new PodExecAuditEntry(
                SessionId: sessionId,
                Action: PodExecAuditActions.SessionStarted,
                ConnectionId: connectionId,
                PrincipalName: principalName,
                Roles: roles,
                AuthMethod: authMethod,
                RemoteIp: remoteIp,
                ClusterId: session.ClusterId,
                Namespace: session.Namespace,
                PodName: session.PodName,
                Container: session.Container,
                Shell: session.ResolvedShell,
                TimestampUtc: DateTimeOffset.UtcNow));

            lifecycle.CompleteRegistration();

            if (Context.ConnectionAborted.IsCancellationRequested)
            {
                if (sessionRegistry.TryRemoveSpecific(connectionId, sessionId, out var aborted))
                {
                    await CloseTrackedSessionAsync(aborted!, exitCode: null, reason: "ClientDisconnected");
                }

                return new PodTerminalAttachResult(Succeeded: false, Error: "Connection aborted during attach.");
            }

            var attachedEvent = new PodTerminalAttachedEvent(
                SessionId: sessionId,
                ClusterId: session.ClusterId,
                Namespace: session.Namespace,
                PodName: session.PodName,
                Container: session.Container,
                ResolvedShell: session.ResolvedShell,
                Cols: cols,
                Rows: rows,
                ConnectedAtUtc: startedAtUtc);

            await hubContext.Clients.Client(connectionId).SendAsync("Attached", attachedEvent);

            return new PodTerminalAttachResult(
                Succeeded: true,
                SessionId: sessionId,
                ClusterId: session.ClusterId,
                Namespace: session.Namespace,
                PodName: session.PodName,
                Container: session.Container,
                ResolvedShell: session.ResolvedShell);
        }
        catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
        {
            lifecycle.TryClaimClose(markSuperseded: true);
            lifecycle.CompleteRegistration();
            auditLogger.LogAttachFailed(new PodExecAuditEntry(
                SessionId: sessionId,
                Action: PodExecAuditActions.AttachFailed,
                ConnectionId: connectionId,
                PrincipalName: principalName,
                Roles: roles,
                AuthMethod: authMethod,
                RemoteIp: remoteIp,
                ClusterId: effectiveClusterLabel,
                Namespace: namespaceName,
                PodName: podName,
                Container: requestedContainer,
                Shell: requestedShell,
                TimestampUtc: DateTimeOffset.UtcNow,
                Reason: "Connection aborted during attach."));

            return new PodTerminalAttachResult(Succeeded: false, Error: "Connection aborted during attach.");
        }
        catch (Exception ex)
        {
            lifecycle.TryClaimClose(markSuperseded: true);
            lifecycle.CompleteRegistration();
            logger.LogWarning(
                ex,
                "Failed to attach pod terminal for {Cluster}/{Namespace}/{Pod} (user: {User})",
                effectiveClusterLabel,
                namespaceName,
                podName,
                principalName);

            auditLogger.LogAttachFailed(new PodExecAuditEntry(
                SessionId: sessionId,
                Action: PodExecAuditActions.AttachFailed,
                ConnectionId: connectionId,
                PrincipalName: principalName,
                Roles: roles,
                AuthMethod: authMethod,
                RemoteIp: remoteIp,
                ClusterId: effectiveClusterLabel,
                Namespace: namespaceName,
                PodName: podName,
                Container: requestedContainer,
                Shell: requestedShell,
                TimestampUtc: DateTimeOffset.UtcNow,
                Reason: ex.Message));

            await NotifyAttachErrorAsync(connectionId, ex.Message);

            return new PodTerminalAttachResult(
                Succeeded: false,
                ClusterId: effectiveClusterLabel,
                Namespace: namespaceName,
                PodName: podName,
                Container: requestedContainer,
                Error: ex.Message);
        }
    }

    public async Task Input(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        if (sessionRegistry.TryGet(Context.ConnectionId, out var active) && active != null)
        {
            try
            {
                await active.Session.SendInputAsync(data, Context.ConnectionAborted);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
            {
                logger.LogDebug(ex, "Ignored terminal input for closing connection {ConnectionId}", Context.ConnectionId);
            }
        }
    }

    public async Task Resize(int cols, int rows)
    {
        if (cols <= 0 || rows <= 0)
        {
            return;
        }

        if (sessionRegistry.TryGet(Context.ConnectionId, out var active) && active != null)
        {
            try
            {
                await active.Session.ResizeAsync(cols, rows, Context.ConnectionAborted);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
            {
                logger.LogDebug(ex, "Ignored terminal resize for closing connection {ConnectionId}", Context.ConnectionId);
            }
        }
    }

    public async Task Detach()
    {
        if (sessionRegistry.TryRemove(Context.ConnectionId, out var active) && active != null)
        {
            await CloseTrackedSessionAsync(active, exitCode: null, reason: "ClientDetached");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (sessionRegistry.TryRemove(Context.ConnectionId, out var active) && active != null)
        {
            var reason = exception != null
                ? $"ClientDisconnected ({exception.GetType().Name})"
                : "ClientDisconnected";
            await CloseTrackedSessionAsync(active, exitCode: null, reason: reason);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task CloseTrackedSessionAsync(TrackedPodTerminalSession tracked, int? exitCode, string reason)
    {
        if (tracked.Lifecycle.TryClaimClose(markSuperseded: true))
        {
            var now = DateTimeOffset.UtcNow;
            auditLogger.LogSessionEnded(new PodExecAuditEntry(
                SessionId: tracked.SessionId,
                Action: PodExecAuditActions.SessionEnded,
                ConnectionId: tracked.Lifecycle.ConnectionId,
                PrincipalName: tracked.Lifecycle.PrincipalName,
                Roles: tracked.Lifecycle.Roles,
                AuthMethod: tracked.Lifecycle.AuthMethod,
                RemoteIp: tracked.Lifecycle.RemoteIp,
                ClusterId: tracked.Lifecycle.ClusterId,
                Namespace: tracked.Lifecycle.Namespace,
                PodName: tracked.Lifecycle.PodName,
                Container: tracked.Lifecycle.Container,
                Shell: tracked.Lifecycle.Shell,
                TimestampUtc: now,
                Duration: now - tracked.Lifecycle.StartedAtUtc,
                ExitCode: exitCode,
                Reason: reason));
        }

        try
        {
            await tracked.Session.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error disposing pod terminal session {SessionId} for connection {ConnectionId}", tracked.SessionId, tracked.Lifecycle.ConnectionId);
        }
    }

    private async Task NotifyAttachErrorAsync(string connectionId, string errorMessage)
    {
        try
        {
            await hubContext.Clients.Client(connectionId).SendAsync("Error", errorMessage);
            await hubContext.Clients.Client(connectionId).SendAsync("Output", $"\r\n\x1b[31mError attaching pod terminal: {errorMessage}\x1b[0m\r\n");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to send attach error notification to connection {ConnectionId}", connectionId);
        }
    }

    private static (string PrincipalName, IReadOnlyList<string> Roles, string? AuthMethod, string? RemoteIp) ExtractCallerAuditContext(HubCallerContext context)
    {
        var user = context.User;
        var principalName = user?.Identity?.Name
                         ?? user?.FindFirst("preferred_username")?.Value
                         ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? user?.FindFirst("sub")?.Value
                         ?? "anonymous";

        var roles = user?.FindAll(ClaimTypes.Role)
                        .Select(c => c.Value)
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                    ?? [];

        var authMethod = user?.FindFirst("auth_method")?.Value
                      ?? user?.Identity?.AuthenticationType;

        var remoteIp = context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();

        return (principalName, roles, authMethod, remoteIp);
    }
}
