using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace ControlPlane.Api.Features.Adapters.Kubernetes;

/// <summary>
/// Delegate used to establish the underlying Kubernetes pod exec WebSocket stream.
/// Defaults to <see cref="IKubernetes.WebSocketNamespacedPodExecAsync"/> in production.
/// </summary>
public delegate Task<WebSocket> PodExecWebSocketConnector(
    IKubernetes client,
    string podName,
    string namespaceName,
    IReadOnlyList<string> command,
    string? container,
    CancellationToken ct);

/// <summary>
/// Production <see cref="IPodExecService"/> backed by <see cref="IKubernetesClientFactory"/>,
/// <c>WebSocketNamespacedPodExecAsync</c>, <see cref="StreamDemuxer"/>, <see cref="ChannelIndex.Resize"/>,
/// and automatic shell fallback (<c>/bin/bash</c> -&gt; <c>/bin/ash</c> -&gt; <c>/bin/sh</c>).
/// </summary>
public sealed class KubernetesPodExecService : IPodExecService
{
    public const string DefaultClusterKey = "default";
    public const string DefaultContainerAnnotation = "kubectl.kubernetes.io/default-container";

    public static readonly IReadOnlyList<string> DefaultShellFallbackOrder =
    [
        "/bin/bash",
        "/bin/ash",
        "/bin/sh"
    ];

    private static readonly HashSet<string> AllowedExplicitShells = new(StringComparer.Ordinal)
    {
        "/bin/bash",
        "/usr/bin/bash",
        "/bin/ash",
        "/bin/sh",
        "/usr/bin/sh",
        "/bin/zsh",
        "/usr/bin/zsh",
        "pwsh",
        "powershell",
        "cmd",
        "cmd.exe"
    };

    private static readonly Regex K8sNameRegex = new(
        "^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IKubernetesClientFactory _clientFactory;
    private readonly ILogger<KubernetesPodExecService> _logger;
    private readonly PodExecWebSocketConnector _connector;
    private readonly TimeSpan _startupProbeDelay;

    public KubernetesPodExecService(
        IKubernetesClientFactory clientFactory,
        ILogger<KubernetesPodExecService> logger)
        : this(clientFactory, logger, DefaultConnectWebSocketAsync, TimeSpan.FromMilliseconds(100))
    {
    }

    public KubernetesPodExecService(
        IKubernetesClientFactory clientFactory,
        ILogger<KubernetesPodExecService> logger,
        PodExecWebSocketConnector connector,
        TimeSpan? startupProbeDelay = null)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(connector);

        _clientFactory = clientFactory;
        _logger = logger;
        _connector = connector;
        _startupProbeDelay = startupProbeDelay ?? TimeSpan.FromMilliseconds(100);
    }

    private static Task<WebSocket> DefaultConnectWebSocketAsync(
        IKubernetes client,
        string podName,
        string namespaceName,
        IReadOnlyList<string> command,
        string? container,
        CancellationToken ct)
    {
        return client.WebSocketNamespacedPodExecAsync(
            name: podName,
            @namespace: namespaceName,
            command: command,
            container: container,
            stdin: true,
            stdout: true,
            stderr: true,
            tty: true,
            cancellationToken: ct);
    }

    public async Task<IPodExecSession> AttachAsync(
        PodExecRequest request,
        Func<string, Task> onOutput,
        Func<int, Task> onClosed,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onOutput);
        ArgumentNullException.ThrowIfNull(onClosed);

        var namespaceName = ValidateKubernetesIdentifier(request.Namespace, nameof(request.Namespace), maxLength: 63);
        var podName = ValidateKubernetesIdentifier(request.PodName, nameof(request.PodName), maxLength: 253);
        var clusterKey = string.IsNullOrWhiteSpace(request.ClusterId) ? null : request.ClusterId.Trim();
        var effectiveClusterLabel = clusterKey ?? DefaultClusterKey;

        var cols = Math.Clamp(request.Cols > 0 ? request.Cols : 80, 10, 500);
        var rows = Math.Clamp(request.Rows > 0 ? request.Rows : 24, 2, 200);
        var shellCandidates = ResolveShellCandidates(request.Shell);

        var client = await _clientFactory.CreateClientAsync(clusterKey, ct);
        var ownsClient = _clientFactory is KubernetesClientFactory;

        V1Pod pod;
        string? targetContainer;
        try
        {
            pod = await ReadRunningPodOrThrowAsync(client, namespaceName, podName, ct);
            targetContainer = ResolveTargetContainerOrThrow(pod, namespaceName, podName, request.Container);
        }
        catch
        {
            if (ownsClient)
            {
                try { client.Dispose(); } catch { }
            }
            throw;
        }

        Exception? lastFailure = null;
        var failureReasons = new List<string>();

        for (var i = 0; i < shellCandidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var candidateShell = shellCandidates[i];
            var hasMoreCandidates = i < shellCandidates.Count - 1;

            WebSocket? webSocket = null;
            StreamDemuxer? demuxer = null;
            SemaphoreSlim? writeLock = null;
            SessionStartupGate? startupGate = null;

            try
            {
                webSocket = await _connector(
                    client,
                    podName,
                    namespaceName,
                    [candidateShell],
                    targetContainer,
                    ct);

                demuxer = new StreamDemuxer(webSocket);
                var stdOut = demuxer.GetStream(ChannelIndex.StdOut, null);
                var stdErr = demuxer.GetStream(ChannelIndex.StdErr, null);
                var errorStream = demuxer.GetStream(ChannelIndex.Error, null);
                demuxer.Start();

                writeLock = new SemaphoreSlim(1, 1);
                startupGate = new SessionStartupGate();

                var session = new KubernetesPodExecSession(
                    effectiveClusterLabel,
                    namespaceName,
                    podName,
                    targetContainer,
                    candidateShell,
                    webSocket,
                    demuxer,
                    writeLock,
                    startupGate,
                    ownsClient ? client : null,
                    onClosed,
                    _logger);

                // Send initial PTY dimensions immediately so the shell initializes COLUMNS/LINES accurately.
                await session.ResizeAsync(cols, rows, ct);

                var errorChannelTask = Task.Factory.StartNew(
                    () => ReadErrorChannelAsync(errorStream, startupGate),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap();
                var stdOutTask = Task.Factory.StartNew(
                    () => PumpOutputChannelAsync(stdOut, webSocket, startupGate, onOutput, _logger, notifyGateOnEnd: true),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap();
                var stdErrTask = Task.Factory.StartNew(
                    () => PumpOutputChannelAsync(stdErr, webSocket, startupGate, onOutput, _logger, notifyGateOnEnd: false),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap();

                _ = MonitorSessionLifetimeAsync(session, stdOutTask, stdErrTask, errorChannelTask);

                if (_startupProbeDelay > TimeSpan.Zero)
                {
                    await Task.WhenAny(
                        startupGate.StartupTerminated,
                        Task.Delay(_startupProbeDelay, ct));
                }

                ct.ThrowIfCancellationRequested();

                if (startupGate.HasFailedDuringProbe(webSocket))
                {
                    var detail = startupGate.GetFailureSummary();
                    failureReasons.Add($"{candidateShell}: {detail}");
                    startupGate.AbortProbe();
                    await session.DisposeSilentlyAsync(disposeClient: !hasMoreCandidates);

                    if (hasMoreCandidates)
                    {
                        _logger.LogDebug(
                            "Shell '{Shell}' unavailable in pod {Namespace}/{Pod} (container: {Container}): {Reason}. Falling back.",
                            candidateShell, namespaceName, podName, targetContainer ?? "<default>", detail);
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Failed to start shell in pod '{namespaceName}/{podName}' (tried: {string.Join(", ", shellCandidates)}). Last error: {detail}");
                }

                await startupGate.ConfirmAndFlushAsync(onOutput);

                _logger.LogInformation(
                    "Attached pod exec session to {Cluster}/{Namespace}/{Pod} (container: {Container}, shell: {Shell}, size: {Cols}x{Rows})",
                    effectiveClusterLabel, namespaceName, podName, targetContainer ?? "<default>", candidateShell, cols, rows);

                return session;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                startupGate?.AbortProbe();
                SafeDisposeAttempt(demuxer, webSocket, writeLock, ownsClient ? client : null);
                throw;
            }
            catch (HttpOperationException ex) when (ex.Response?.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                startupGate?.AbortProbe();
                SafeDisposeAttempt(demuxer, webSocket, writeLock, ownsClient ? client : null);
                throw new InvalidOperationException(
                    $"Kubernetes API rejected exec into pod '{namespaceName}/{podName}' (HTTP {(int)ex.Response.StatusCode}).",
                    ex);
            }
            catch (InvalidOperationException) when (!hasMoreCandidates)
            {
                if (ownsClient)
                {
                    try { client.Dispose(); } catch { }
                }
                throw;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                failureReasons.Add($"{candidateShell}: {ex.Message}");
                startupGate?.AbortProbe();
                SafeDisposeAttempt(demuxer, webSocket, writeLock, !hasMoreCandidates && ownsClient ? client : null);

                if (hasMoreCandidates)
                {
                    _logger.LogDebug(
                        ex,
                        "Pod exec attempt with shell '{Shell}' failed for {Namespace}/{Pod}; trying next fallback shell.",
                        candidateShell, namespaceName, podName);
                    continue;
                }
            }
        }

        if (ownsClient)
        {
            try { client.Dispose(); } catch { }
        }

        throw new InvalidOperationException(
            $"Unable to establish pod exec session for '{namespaceName}/{podName}' (tried: {string.Join(", ", shellCandidates)}). " +
            string.Join(" | ", failureReasons),
            lastFailure);
    }

    internal static IReadOnlyList<string> ResolveShellCandidates(string? requestedShell)
    {
        if (string.IsNullOrWhiteSpace(requestedShell) ||
            string.Equals(requestedShell.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultShellFallbackOrder;
        }

        var trimmed = requestedShell.Trim();
        if (!AllowedExplicitShells.Contains(trimmed))
        {
            throw new ArgumentException(
                $"Unsupported shell '{trimmed}'. Allowed shells: {string.Join(", ", AllowedExplicitShells)} (or 'auto').",
                nameof(requestedShell));
        }

        return [trimmed];
    }

    internal static (int ExitCode, bool IsFailure, string? Message) ParseKubernetesExecStatus(string? errorChannelPayload)
    {
        if (string.IsNullOrWhiteSpace(errorChannelPayload))
        {
            return (0, false, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(errorChannelPayload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (0, false, errorChannelPayload.Trim());
            }

            var status = root.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.String
                ? statusProp.GetString()
                : null;
            var message = root.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String
                ? msgProp.GetString()
                : null;

            if (string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
            {
                return (0, false, message);
            }

            var exitCode = 1;
            if (root.TryGetProperty("details", out var details) &&
                details.ValueKind == JsonValueKind.Object &&
                details.TryGetProperty("causes", out var causes) &&
                causes.ValueKind == JsonValueKind.Array)
            {
                foreach (var cause in causes.EnumerateArray())
                {
                    if (cause.ValueKind != JsonValueKind.Object) continue;
                    var reason = cause.TryGetProperty("reason", out var rProp) ? rProp.GetString() : null;
                    var causeMsg = cause.TryGetProperty("message", out var mProp) ? mProp.GetString() : null;
                    if (string.Equals(reason, "ExitCode", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(causeMsg, out var parsedCode))
                    {
                        exitCode = parsedCode;
                        break;
                    }
                }
            }

            return (exitCode, true, message ?? errorChannelPayload.Trim());
        }
        catch
        {
            var trimmed = errorChannelPayload.Trim();
            var looksLikeFailure = trimmed.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                   trimmed.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                                   trimmed.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                                   trimmed.Contains("no such file", StringComparison.OrdinalIgnoreCase);
            return (looksLikeFailure ? 126 : 0, looksLikeFailure, trimmed);
        }
    }

    private static string ValidateKubernetesIdentifier(string value, string paramName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength || !K8sNameRegex.IsMatch(trimmed))
        {
            throw new ArgumentException(
                $"Invalid {paramName} '{trimmed}'. Must be 1-{maxLength} lowercase alphanumeric characters, '-' or '.'.",
                paramName);
        }

        return trimmed;
    }

    private static async Task<V1Pod> ReadRunningPodOrThrowAsync(
        IKubernetes client,
        string namespaceName,
        string podName,
        CancellationToken ct)
    {
        V1Pod pod;
        try
        {
            pod = await client.CoreV1.ReadNamespacedPodAsync(podName, namespaceName, cancellationToken: ct);
        }
        catch (HttpOperationException ex) when (ex.Response?.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Pod '{namespaceName}/{podName}' was not found.", ex);
        }

        if (pod.Metadata?.DeletionTimestamp != null)
        {
            throw new InvalidOperationException($"Pod '{namespaceName}/{podName}' is currently terminating.");
        }

        var phase = pod.Status?.Phase;
        if (!string.Equals(phase, "Running", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Pod '{namespaceName}/{podName}' is not running (current phase: {phase ?? "Unknown"}).");
        }

        return pod;
    }

    private static string? ResolveTargetContainerOrThrow(
        V1Pod pod,
        string namespaceName,
        string podName,
        string? requestedContainer)
    {
        var containers = pod.Spec?.Containers?.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                         ?? [];
        var ephemeralContainers = pod.Spec?.EphemeralContainers?.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                                  ?? [];
        var initContainers = pod.Spec?.InitContainers?.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                             ?? [];
        var allContainers = containers.Concat(ephemeralContainers).Concat(initContainers).Distinct(StringComparer.Ordinal).ToList();

        string? resolved;
        if (!string.IsNullOrWhiteSpace(requestedContainer))
        {
            var trimmed = requestedContainer.Trim();
            if (allContainers.Count > 0 && !allContainers.Contains(trimmed, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Container '{trimmed}' was not found in pod '{namespaceName}/{podName}'. Available containers: {string.Join(", ", allContainers)}.");
            }
            resolved = trimmed;
        }
        else if (pod.Metadata?.Annotations != null &&
                 pod.Metadata.Annotations.TryGetValue(DefaultContainerAnnotation, out var annotatedDefault) &&
                 !string.IsNullOrWhiteSpace(annotatedDefault) &&
                 (allContainers.Count == 0 || allContainers.Contains(annotatedDefault.Trim(), StringComparer.Ordinal)))
        {
            resolved = annotatedDefault.Trim();
        }
        else
        {
            resolved = containers.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(resolved))
        {
            var status = pod.Status?.ContainerStatuses?.FirstOrDefault(s => string.Equals(s.Name, resolved, StringComparison.Ordinal))
                         ?? pod.Status?.EphemeralContainerStatuses?.FirstOrDefault(s => string.Equals(s.Name, resolved, StringComparison.Ordinal))
                         ?? pod.Status?.InitContainerStatuses?.FirstOrDefault(s => string.Equals(s.Name, resolved, StringComparison.Ordinal));

            if (status?.State != null && status.State.Running == null)
            {
                if (status.State.Waiting != null)
                {
                    var reason = status.State.Waiting.Reason ?? "Waiting";
                    throw new InvalidOperationException(
                        $"Container '{resolved}' in pod '{namespaceName}/{podName}' is not running ({reason}).");
                }

                if (status.State.Terminated != null)
                {
                    var reason = status.State.Terminated.Reason ?? $"ExitCode {status.State.Terminated.ExitCode}";
                    throw new InvalidOperationException(
                        $"Container '{resolved}' in pod '{namespaceName}/{podName}' has terminated ({reason}).");
                }
            }
        }

        return resolved;
    }

    private static async Task PumpOutputChannelAsync(
        Stream channelStream,
        WebSocket webSocket,
        SessionStartupGate gate,
        Func<string, Task> onOutput,
        ILogger logger,
        bool notifyGateOnEnd)
    {
        var buffer = new byte[4096];
        var decoder = Encoding.UTF8.GetDecoder();
        var charBuffer = new char[4096];

        try
        {
            while (!gate.IsAborted)
            {
                // k8s MuxedStream only implements synchronous Read() over ByteBuffer Monitor.Wait;
                // read directly on our LongRunning thread so Stream.ReadAsync does not starve the ThreadPool.
                var read = channelStream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    var flushed = decoder.GetChars(Array.Empty<byte>(), 0, 0, charBuffer, 0, flush: true);
                    if (flushed > 0)
                    {
                        await gate.DeliverOrBufferOutputAsync(new string(charBuffer, 0, flushed), onOutput);
                    }
                    break;
                }

                var charsDecoded = decoder.GetChars(buffer, 0, read, charBuffer, 0, flush: false);
                if (charsDecoded > 0)
                {
                    var chunk = new string(charBuffer, 0, charsDecoded);
                    await gate.DeliverOrBufferOutputAsync(chunk, onOutput);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Pod exec output channel ended (WebSocket state: {State})", webSocket.State);
        }
        finally
        {
            if (notifyGateOnEnd)
            {
                gate.NotifyStreamEnded();
            }
        }
    }

    private static Task<(int ExitCode, bool IsFailure, string? Message)> ReadErrorChannelAsync(
        Stream errorStream,
        SessionStartupGate gate)
    {
        try
        {
            using var reader = new StreamReader(errorStream, Encoding.UTF8);
            var payload = reader.ReadToEnd();
            var parsed = ParseKubernetesExecStatus(payload);
            gate.RecordErrorChannelStatus(parsed.ExitCode, parsed.IsFailure, parsed.Message);
            return Task.FromResult(parsed);
        }
        catch (Exception ex)
        {
            return Task.FromResult<(int ExitCode, bool IsFailure, string? Message)>((0, false, ex.Message));
        }
    }

    private static async Task MonitorSessionLifetimeAsync(
        KubernetesPodExecSession session,
        Task stdOutTask,
        Task stdErrTask,
        Task<(int ExitCode, bool IsFailure, string? Message)> errorChannelTask)
    {
        try
        {
            await Task.WhenAll(stdOutTask, stdErrTask);

            var completedError = await Task.WhenAny(errorChannelTask, Task.Delay(150));
            var exitCode = completedError == errorChannelTask
                ? (await errorChannelTask).ExitCode
                : session.ExitCode;

            await session.NotifyClosedOnceAsync(exitCode);
        }
        catch
        {
            await session.NotifyClosedOnceAsync(session.ExitCode);
        }
    }

    private static void SafeDisposeAttempt(StreamDemuxer? demuxer, WebSocket? webSocket, SemaphoreSlim? writeLock, IKubernetes? ownedClient = null)
    {
        try { demuxer?.Dispose(); } catch { }
        try { webSocket?.Dispose(); } catch { }
        try { writeLock?.Dispose(); } catch { }
        try { ownedClient?.Dispose(); } catch { }
    }

    private sealed class SessionStartupGate
    {
        private readonly object _sync = new();
        private readonly SemaphoreSlim _outputLock = new(1, 1);
        private readonly List<string> _bufferedChunks = [];
        private readonly TaskCompletionSource _terminatedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _isConfirmed;
        private bool _isAborted;
        private bool _streamEnded;
        private bool _errorFailure;
        private int _exitCode;
        private string? _failureMessage;

        public Task StartupTerminated => _terminatedTcs.Task;
        public bool IsAborted { get { lock (_sync) return _isAborted; } }
        public bool IsConfirmed { get { lock (_sync) return _isConfirmed; } }
        public int ExitCode { get { lock (_sync) return _exitCode; } }

        public async Task DeliverOrBufferOutputAsync(string chunk, Func<string, Task> onOutput)
        {
            if (IsAborted) return;

            await _outputLock.WaitAsync();
            try
            {
                lock (_sync)
                {
                    if (_isAborted) return;
                    if (!_isConfirmed)
                    {
                        _bufferedChunks.Add(chunk);
                        return;
                    }
                }

                await onOutput(chunk);
            }
            finally
            {
                _outputLock.Release();
            }
        }

        public void RecordErrorChannelStatus(int exitCode, bool isFailure, string? message)
        {
            lock (_sync)
            {
                _exitCode = exitCode;
                if (isFailure)
                {
                    _errorFailure = true;
                    _failureMessage = message;
                    _terminatedTcs.TrySetResult();
                }
            }
        }

        public void NotifyStreamEnded()
        {
            lock (_sync)
            {
                _streamEnded = true;
                _terminatedTcs.TrySetResult();
            }
        }

        public bool HasFailedDuringProbe(WebSocket webSocket)
        {
            lock (_sync)
            {
                if (_errorFailure) return true;
                if (_streamEnded || webSocket.State != WebSocketState.Open)
                {
                    return true;
                }
                return false;
            }
        }

        public string GetFailureSummary()
        {
            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(_failureMessage))
                {
                    return _failureMessage!;
                }

                var buffered = string.Concat(_bufferedChunks).Trim();
                if (!string.IsNullOrWhiteSpace(buffered))
                {
                    return buffered;
                }

                return _exitCode != 0
                    ? $"Process exited immediately with code {_exitCode}"
                    : "Exec stream closed immediately during shell startup";
            }
        }

        public void AbortProbe()
        {
            lock (_sync)
            {
                _isAborted = true;
                _bufferedChunks.Clear();
                _terminatedTcs.TrySetResult();
            }
        }

        public async Task ConfirmAndFlushAsync(Func<string, Task> onOutput)
        {
            await _outputLock.WaitAsync();
            try
            {
                List<string> pending;
                lock (_sync)
                {
                    if (_isAborted) return;
                    pending = [.. _bufferedChunks];
                    _bufferedChunks.Clear();
                    _isConfirmed = true;
                }

                foreach (var chunk in pending)
                {
                    if (IsAborted) return;
                    await onOutput(chunk);
                }
            }
            finally
            {
                _outputLock.Release();
            }
        }
    }

    private sealed class KubernetesPodExecSession(
        string clusterId,
        string namespaceName,
        string podName,
        string? container,
        string resolvedShell,
        WebSocket webSocket,
        StreamDemuxer demuxer,
        SemaphoreSlim writeLock,
        SessionStartupGate startupGate,
        IKubernetes? ownedClient,
        Func<int, Task> onClosed,
        ILogger logger) : IPodExecSession
    {
        private int _closedSignaled;
        private int _disposed;
        private IKubernetes? _ownedClient = ownedClient;

        public string ClusterId { get; } = clusterId;
        public string Namespace { get; } = namespaceName;
        public string PodName { get; } = podName;
        public string? Container { get; } = container;
        public string ResolvedShell { get; } = resolvedShell;
        public int ExitCode => startupGate.ExitCode;

        public async ValueTask SendInputAsync(string data, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(data) || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    var bytes = Encoding.UTF8.GetBytes(data);
                    await writeLock.WaitAsync(ct);
                    try
                    {
                        if (Volatile.Read(ref _disposed) == 0 && webSocket.State == WebSocketState.Open)
                        {
                            await demuxer.Write(ChannelIndex.StdIn, bytes, 0, bytes.Length, ct);
                        }
                    }
                    finally
                    {
                        writeLock.Release();
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Failed to send stdin data to pod exec session {Namespace}/{Pod}", Namespace, PodName);
            }
        }

        public async ValueTask ResizeAsync(int cols, int rows, CancellationToken ct = default)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            var clampedCols = Math.Clamp(cols > 0 ? cols : 80, 10, 500);
            var clampedRows = Math.Clamp(rows > 0 ? rows : 24, 2, 200);

            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    var resizePayload = $"{{\"Width\":{clampedCols},\"Height\":{clampedRows}}}";
                    var bytes = Encoding.UTF8.GetBytes(resizePayload);
                    await writeLock.WaitAsync(ct);
                    try
                    {
                        if (Volatile.Read(ref _disposed) == 0 && webSocket.State == WebSocketState.Open)
                        {
                            await demuxer.Write(ChannelIndex.Resize, bytes, 0, bytes.Length, ct);
                        }
                    }
                    finally
                    {
                        writeLock.Release();
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Failed to send resize event to pod exec session {Namespace}/{Pod}", Namespace, PodName);
            }
        }

        public async Task NotifyClosedOnceAsync(int exitCode)
        {
            if (!startupGate.IsConfirmed || startupGate.IsAborted)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _closedSignaled, 1, 0) == 0)
            {
                try
                {
                    await onClosed(exitCode);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Pod exec onClosed callback faulted for {Namespace}/{Pod}", Namespace, PodName);
                }
            }
        }

        public async ValueTask DisposeSilentlyAsync(bool disposeClient = true)
        {
            Interlocked.Exchange(ref _closedSignaled, 1);
            if (!disposeClient)
            {
                Interlocked.Exchange(ref _ownedClient, null);
            }
            await DisposeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session disposed", closeCts.Token);
                }
            }
            catch { }

            try { demuxer.Dispose(); } catch { }
            try { webSocket.Dispose(); } catch { }
            try { writeLock.Dispose(); } catch { }

            var clientToDispose = Interlocked.Exchange(ref _ownedClient, null);
            try { clientToDispose?.Dispose(); } catch { }
        }
    }
}
