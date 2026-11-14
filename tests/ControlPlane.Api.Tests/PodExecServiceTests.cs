using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Demo;
using ControlPlane.Api.Features.Demo.Adapters;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlPlane.Api.Tests;

public class PodExecServiceTests
{
    private sealed class MockDelegatingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = handler(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    private sealed class MultiClusterTestClientFactory(IReadOnlyDictionary<string, IKubernetes> clusters, IKubernetes defaultClient)
        : IKubernetesClientFactory
    {
        public List<string?> RequestedClusterIds { get; } = [];

        public Task<IKubernetes> CreateClientAsync(string? clusterId = null, CancellationToken ct = default)
        {
            RequestedClusterIds.Add(clusterId);
            if (!string.IsNullOrWhiteSpace(clusterId) && clusters.TryGetValue(clusterId, out var matched))
            {
                return Task.FromResult(matched);
            }

            return Task.FromResult(defaultClient);
        }

        public Task<IKubernetesAdapter> CreateAdapterAsync(string? clusterId = null, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// In-memory WebSocket speaking the Kubernetes multiplexed exec framing ([channelIndex, ...payload])
    /// so tests exercise real <see cref="StreamDemuxer"/> and <see cref="ChannelIndex"/> channels.
    /// </summary>
    private sealed class FakeK8sExecWebSocket : WebSocket
    {
        private readonly Channel<byte[]?> _inboundFrames = Channel.CreateUnbounded<byte[]?>();
        private readonly ConcurrentQueue<(ChannelIndex Channel, string Payload)> _outboundFrames = new();
        private readonly TaskCompletionSource<(ChannelIndex Channel, string Payload)> _nextStdInTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WebSocketState _state = WebSocketState.Open;
        private ArraySegment<byte> _pendingSegment;

        public IReadOnlyCollection<(ChannelIndex Channel, string Payload)> SentFrames => _outboundFrames.ToArray();

        public event Action<ChannelIndex, string>? FrameSent;

        public void EnqueueServerChannelText(ChannelIndex channel, string text)
        {
            var textBytes = Encoding.UTF8.GetBytes(text);
            var frame = new byte[textBytes.Length + 1];
            frame[0] = (byte)channel;
            Buffer.BlockCopy(textBytes, 0, frame, 1, textBytes.Length);
            _inboundFrames.Writer.TryWrite(frame);
        }

        public void CompleteServerStream()
        {
            _state = WebSocketState.CloseReceived;
            _inboundFrames.Writer.TryWrite(null);
            _inboundFrames.Writer.TryComplete();
        }

        public override WebSocketCloseStatus? CloseStatus => _state == WebSocketState.Closed ? WebSocketCloseStatus.NormalClosure : null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => "v4.channel.k8s.io";

        public override void Abort()
        {
            _state = WebSocketState.Aborted;
            _inboundFrames.Writer.TryWrite(null);
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            _inboundFrames.Writer.TryWrite(null);
            _inboundFrames.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => CloseAsync(closeStatus, statusDescription, cancellationToken);

        public override void Dispose()
        {
            _state = WebSocketState.Closed;
            _inboundFrames.Writer.TryWrite(null);
            _inboundFrames.Writer.TryComplete();
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_pendingSegment.Count == 0)
            {
                while (await _inboundFrames.Reader.WaitToReadAsync(cancellationToken))
                {
                    if (_inboundFrames.Reader.TryRead(out var frame))
                    {
                        if (frame is null)
                        {
                            _state = WebSocketState.Closed;
                            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "EOF");
                        }

                        _pendingSegment = new ArraySegment<byte>(frame);
                        break;
                    }
                }

                if (_pendingSegment.Count == 0)
                {
                    _state = WebSocketState.Closed;
                    return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "EOF");
                }
            }

            var toCopy = Math.Min(buffer.Count, _pendingSegment.Count);
            Buffer.BlockCopy(_pendingSegment.Array!, _pendingSegment.Offset, buffer.Array!, buffer.Offset, toCopy);
            var endOfMessage = toCopy == _pendingSegment.Count;
            _pendingSegment = endOfMessage
                ? default
                : new ArraySegment<byte>(_pendingSegment.Array!, _pendingSegment.Offset + toCopy, _pendingSegment.Count - toCopy);

            return new WebSocketReceiveResult(toCopy, WebSocketMessageType.Binary, endOfMessage);
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            if (buffer.Count > 0)
            {
                var channel = (ChannelIndex)buffer.Array![buffer.Offset];
                var payload = buffer.Count > 1
                    ? Encoding.UTF8.GetString(buffer.Array!, buffer.Offset + 1, buffer.Count - 1)
                    : string.Empty;
                _outboundFrames.Enqueue((channel, payload));
                if (channel == ChannelIndex.StdIn)
                {
                    _nextStdInTcs.TrySetResult((channel, payload));
                }
                FrameSent?.Invoke(channel, payload);
            }

            return Task.CompletedTask;
        }
    }

    private static IKubernetes CreatePodMockClient(V1Pod? podToReturn)
    {
        var handler = new MockDelegatingHandler(req =>
        {
            if (podToReturn != null &&
                req.Method == HttpMethod.Get &&
                req.RequestUri!.AbsolutePath.Equals(
                    $"/api/v1/namespaces/{podToReturn.Metadata.NamespaceProperty}/pods/{podToReturn.Metadata.Name}",
                    StringComparison.Ordinal))
            {
                var json = KubernetesJson.Serialize(podToReturn);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"kind\":\"Status\",\"status\":\"Failure\",\"reason\":\"NotFound\",\"code\":404}", Encoding.UTF8, "application/json")
            };
        });

        return new Kubernetes(new KubernetesClientConfiguration { Host = "http://localhost:8080" }, handler);
    }

    private static V1Pod BuildPod(
        string name = "grafana-67b8d-12345",
        string namespaceName = "monitoring",
        string phase = "Running",
        string[]? containers = null,
        string? defaultContainerAnnotation = null,
        DateTime? deletionTimestamp = null)
    {
        var containerList = (containers ?? ["grafana", "istio-proxy"])
            .Select(c => new V1Container { Name = c })
            .ToList();

        var annotations = defaultContainerAnnotation != null
            ? new Dictionary<string, string> { [KubernetesPodExecService.DefaultContainerAnnotation] = defaultContainerAnnotation }
            : null;

        return new V1Pod
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = namespaceName,
                Annotations = annotations,
                DeletionTimestamp = deletionTimestamp
            },
            Spec = new V1PodSpec
            {
                Containers = containerList
            },
            Status = new V1PodStatus
            {
                Phase = phase
            }
        };
    }

    [Fact]
    public async Task KubernetesPodExecService_RoutesToCluster_StreamsStdInStdOutResizeAndExitCode()
    {
        var edgePod = BuildPod(
            name: "edge-agent-abc12",
            namespaceName: "observability",
            containers: ["sidecar", "agent"],
            defaultContainerAnnotation: "agent");

        var defaultClient = CreatePodMockClient(null);
        var edgeClient = CreatePodMockClient(edgePod);
        var factory = new MultiClusterTestClientFactory(
            new Dictionary<string, IKubernetes> { ["edge-k3s"] = edgeClient },
            defaultClient);

        var ws = new FakeK8sExecWebSocket();
        IKubernetes? connectedClient = null;
        string? connectedContainer = null;
        IReadOnlyList<string>? connectedCommand = null;

        var stdInReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResizeReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        ws.FrameSent += (ch, payload) =>
        {
            if (ch == ChannelIndex.StdIn)
            {
                stdInReceived.TrySetResult(payload);
            }
            else if (ch == ChannelIndex.Resize && payload.Contains("140", StringComparison.Ordinal))
            {
                secondResizeReceived.TrySetResult(payload);
            }
        };

        var service = new KubernetesPodExecService(
            factory,
            NullLogger<KubernetesPodExecService>.Instance,
            (client, pod, ns, cmd, container, _) =>
            {
                connectedClient = client;
                connectedContainer = container;
                connectedCommand = cmd;
                ws.EnqueueServerChannelText(ChannelIndex.StdOut, "root@edge-agent:/# ");
                return Task.FromResult<WebSocket>(ws);
            },
            startupProbeDelay: TimeSpan.FromMilliseconds(20));

        var outputBuilder = new StringBuilder();
        var secondOutputTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closedTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var session = await service.AttachAsync(
            new PodExecRequest(
                Namespace: "observability",
                PodName: "edge-agent-abc12",
                ClusterId: "edge-k3s",
                Cols: 120,
                Rows: 36),
            onOutput: chunk =>
            {
                lock (outputBuilder)
                {
                    outputBuilder.Append(chunk);
                    if (outputBuilder.ToString().Contains("uid=0(root)", StringComparison.Ordinal))
                    {
                        secondOutputTcs.TrySetResult(outputBuilder.ToString());
                    }
                }
                return Task.CompletedTask;
            },
            onClosed: code =>
            {
                closedTcs.TrySetResult(code);
                return Task.CompletedTask;
            });

        Assert.Same(edgeClient, connectedClient);
        Assert.Contains("edge-k3s", factory.RequestedClusterIds);
        Assert.Equal("agent", connectedContainer);
        Assert.Equal("agent", session.Container);
        Assert.Equal("/bin/bash", session.ResolvedShell);
        Assert.Equal(["/bin/bash"], connectedCommand);

        // Verify initial resize frame sent on ChannelIndex.Resize
        Assert.Contains(ws.SentFrames, f => f.Channel == ChannelIndex.Resize && f.Payload == "{\"Width\":120,\"Height\":36}");

        // Verify StdIn write and dynamic Resize frame
        await session.SendInputAsync("id\n");
        var sentStdIn = await stdInReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("id\n", sentStdIn);

        await session.ResizeAsync(140, 48);
        var sentResize = await secondResizeReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("{\"Width\":140,\"Height\":48}", sentResize);

        // Verify StdOut/StdErr streaming
        ws.EnqueueServerChannelText(ChannelIndex.StdOut, "uid=0(root) gid=0(root)\r\n");
        var combinedOut = await secondOutputTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("root@edge-agent:/# ", combinedOut);
        Assert.Contains("uid=0(root)", combinedOut);

        // Verify ChannelIndex.Error exit code propagation on process termination
        ws.EnqueueServerChannelText(
            ChannelIndex.Error,
            "{\"metadata\":{},\"status\":\"Failure\",\"reason\":\"NonZeroExitCode\",\"details\":{\"causes\":[{\"reason\":\"ExitCode\",\"message\":\"130\"}]}}");
        ws.CompleteServerStream();

        var exitCode = await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(130, exitCode);
    }

    [Fact]
    public async Task KubernetesPodExecService_AutoFallbacksFromBashAndAshToSh_OnOciMissingShellFailure()
    {
        var alpinePod = BuildPod(name: "ingress-nginx-748596-abcde", namespaceName: "ingress-nginx", containers: ["controller"]);
        var client = CreatePodMockClient(alpinePod);
        var factory = new MultiClusterTestClientFactory(new Dictionary<string, IKubernetes>(), client);

        var attemptedShells = new List<string>();
        var shSocket = new FakeK8sExecWebSocket();

        var service = new KubernetesPodExecService(
            factory,
            NullLogger<KubernetesPodExecService>.Instance,
            (_, _, _, cmd, _, _) =>
            {
                var shell = cmd[0];
                attemptedShells.Add(shell);

                if (shell is "/bin/bash" or "/bin/ash")
                {
                    // Simulate containerd OCI runtime exec failure over ChannelIndex.Error + immediate stream close
                    var failedSocket = new FakeK8sExecWebSocket();
                    failedSocket.EnqueueServerChannelText(
                        ChannelIndex.StdOut,
                        $"OCI runtime exec failed: exec: \"{shell}\": stat {shell}: no such file or directory\r\n");
                    failedSocket.EnqueueServerChannelText(
                        ChannelIndex.Error,
                        "{\"status\":\"Failure\",\"message\":\"executable file not found\",\"details\":{\"causes\":[{\"reason\":\"ExitCode\",\"message\":\"126\"}]}}");
                    failedSocket.CompleteServerStream();
                    return Task.FromResult<WebSocket>(failedSocket);
                }

                shSocket.EnqueueServerChannelText(ChannelIndex.StdOut, "/ $ ");
                return Task.FromResult<WebSocket>(shSocket);
            },
            startupProbeDelay: TimeSpan.FromMilliseconds(25));

        var receivedOutput = new StringBuilder();
        var closedCalled = false;

        await using var session = await service.AttachAsync(
            new PodExecRequest(Namespace: "ingress-nginx", PodName: "ingress-nginx-748596-abcde"),
            onOutput: text =>
            {
                receivedOutput.Append(text);
                return Task.CompletedTask;
            },
            onClosed: _ =>
            {
                closedCalled = true;
                return Task.CompletedTask;
            });

        Assert.Equal(["/bin/bash", "/bin/ash", "/bin/sh"], attemptedShells);
        Assert.Equal("/bin/sh", session.ResolvedShell);
        Assert.Equal("controller", session.Container);
        Assert.Equal("/ $ ", receivedOutput.ToString());
        Assert.DoesNotContain("OCI runtime exec failed", receivedOutput.ToString());
        Assert.False(closedCalled);
    }

    [Fact]
    public async Task KubernetesPodExecService_RejectsMissingNonRunningPodInvalidContainerAndUnsafeShell()
    {
        var pendingPod = BuildPod(name: "pending-pod", namespaceName: "default", phase: "Pending", containers: ["main"]);
        var runningPod = BuildPod(name: "running-pod", namespaceName: "default", phase: "Running", containers: ["main", "crashing-sidecar"]);
        runningPod.Status.ContainerStatuses =
        [
            new V1ContainerStatus
            {
                Name = "main",
                State = new V1ContainerState { Running = new V1ContainerStateRunning() }
            },
            new V1ContainerStatus
            {
                Name = "crashing-sidecar",
                State = new V1ContainerState
                {
                    Waiting = new V1ContainerStateWaiting { Reason = "CrashLoopBackOff" }
                }
            }
        ];

        var handler = new MockDelegatingHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/pods/pending-pod", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(KubernetesJson.Serialize(pendingPod), Encoding.UTF8, "application/json")
                };
            }
            if (req.RequestUri.AbsolutePath.EndsWith("/pods/running-pod", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(KubernetesJson.Serialize(runningPod), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var k8sClient = new Kubernetes(new KubernetesClientConfiguration { Host = "http://localhost:8080" }, handler);
        var factory = new MultiClusterTestClientFactory(new Dictionary<string, IKubernetes>(), k8sClient);

        var service = new KubernetesPodExecService(
            factory,
            NullLogger<KubernetesPodExecService>.Instance,
            (_, _, _, _, _, _) => throw new InvalidOperationException("Should not connect WebSocket"),
            startupProbeDelay: TimeSpan.Zero);

        // 1. Missing pod -> InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AttachAsync(new PodExecRequest("default", "missing-pod"), _ => Task.CompletedTask, _ => Task.CompletedTask));

        // 2. Non-running pod -> InvalidOperationException
        var pendingEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AttachAsync(new PodExecRequest("default", "pending-pod"), _ => Task.CompletedTask, _ => Task.CompletedTask));
        Assert.Contains("Pending", pendingEx.Message);

        // 3. Non-existent container -> InvalidOperationException
        var containerEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AttachAsync(new PodExecRequest("default", "running-pod", Container: "unknown-sidecar"), _ => Task.CompletedTask, _ => Task.CompletedTask));
        Assert.Contains("unknown-sidecar", containerEx.Message);

        // 4. Waiting/crashing container in an otherwise Running pod -> InvalidOperationException
        var crashLoopEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AttachAsync(new PodExecRequest("default", "running-pod", Container: "crashing-sidecar"), _ => Task.CompletedTask, _ => Task.CompletedTask));
        Assert.Contains("CrashLoopBackOff", crashLoopEx.Message);

        // 5. Unsafe shell command injection attempt -> ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AttachAsync(new PodExecRequest("default", "running-pod", Shell: "/bin/sh -c id"), _ => Task.CompletedTask, _ => Task.CompletedTask));
    }

    [Fact]
    public async Task DemoPodExecService_SimulatesInteractiveShellResizeAndCleanExit_AndRegistersInDemoMode()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DEMO_MODE"] = "true" })
            .Build();

        var services = new ServiceCollection();
        services.AddControlPlaneDemoMode(config);
        await using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var execService = scope.ServiceProvider.GetRequiredService<IPodExecService>();
        Assert.IsType<DemoPodExecService>(execService);

        var output = new StringBuilder();
        int? closedExitCode = null;

        await using var session = await execService.AttachAsync(
            new PodExecRequest(
                Namespace: "monitoring",
                PodName: "monitoring-grafana-67b8d-12345",
                ClusterId: "homelab-demo",
                Cols: 100,
                Rows: 30),
            onOutput: chunk =>
            {
                output.Append(chunk);
                return Task.CompletedTask;
            },
            onClosed: code =>
            {
                closedExitCode = code;
                return Task.CompletedTask;
            });

        Assert.Equal("grafana", session.Container);
        Assert.Equal("/bin/bash", session.ResolvedShell);
        Assert.Contains("[DEMO MODE]", output.ToString());

        output.Clear();
        await session.ResizeAsync(132, 42);
        await session.SendInputAsync("stty size\r");
        Assert.Contains("42 132", output.ToString());

        output.Clear();
        await session.SendInputAsync("whoamix\b\r");
        Assert.Contains("root\r\n", output.ToString());

        await session.SendInputAsync("exit\r");
        Assert.Equal(0, closedExitCode);
    }
}
