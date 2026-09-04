using System.Net;
using System.Text;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Orchestration;
using ControlPlane.Api.Hubs;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class KubernetesAdapterTests
{
    private class MockDelegatingHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockDelegatingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _handler(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    private static (IKubernetes Client, List<HttpRequestMessage> Requests) CreateMockK8s(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var requests = new List<HttpRequestMessage>();
        var delegatingHandler = new MockDelegatingHandler(req =>
        {
            requests.Add(req);
            return responseFactory(req);
        });

        var config = new KubernetesClientConfiguration { Host = "http://localhost:8080" };
        var client = new Kubernetes(config, delegatingHandler);
        return (client, requests);
    }

    [Fact]
    public async Task CordonNodeAsync_PatchesNode_ToUnschedulableTrue()
    {
        string? patchBody = null;
        var (client, requests) = CreateMockK8s(req =>
        {
            Console.WriteLine($"[TEST INTERCEPT] {req.Method} {req.RequestUri}");
            if (req.RequestUri!.ToString().Contains("/api/v1/nodes/k8s-worker-01"))
            {
                patchBody = req.Content?.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"metadata\":{\"name\":\"k8s-worker-01\"},\"spec\":{\"unschedulable\":true}}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var adapter = new KubernetesAdapter(client, loggerFactory.CreateLogger<KubernetesAdapter>());
        var result = await adapter.CordonNodeAsync("k8s-worker-01");

        if (!result)
        {
            foreach (var r in requests)
            {
                Console.WriteLine($"Intercepted request: {r.Method} {r.RequestUri}");
            }
        }

        Assert.True(result);
        Assert.NotNull(patchBody);
        Assert.Contains("\"unschedulable\": true", patchBody);
    }

    [Fact]
    public async Task UncordonNodeAsync_PatchesNode_ToUnschedulableFalse()
    {
        string? patchBody = null;
        var (client, requests) = CreateMockK8s(req =>
        {
            if (req.Method.Method == "PATCH" && req.RequestUri!.ToString().Contains("/api/v1/nodes/k8s-worker-01"))
            {
                patchBody = req.Content!.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"metadata\":{\"name\":\"k8s-worker-01\"},\"spec\":{\"unschedulable\":false}}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);
        var result = await adapter.UncordonNodeAsync("k8s-worker-01");

        Assert.True(result);
        Assert.NotNull(patchBody);
        Assert.Contains("\"unschedulable\": false", patchBody);
    }

    [Fact]
    public async Task DrainNodeAsync_EvictsNonDaemonSetPods_AndFiltersMirrorPods()
    {
        var podListJson = """
        {
            "items": [
                {
                    "metadata": {
                        "name": "web-api-pod-1",
                        "namespace": "default"
                    },
                    "spec": { "nodeName": "k8s-worker-01" },
                    "status": { "phase": "Running" }
                },
                {
                    "metadata": {
                        "name": "node-exporter-ds-1",
                        "namespace": "monitoring",
                        "ownerReferences": [
                            { "kind": "DaemonSet", "name": "node-exporter" }
                        ]
                    },
                    "spec": { "nodeName": "k8s-worker-01" },
                    "status": { "phase": "Running" }
                },
                {
                    "metadata": {
                        "name": "kube-proxy-mirror",
                        "namespace": "kube-system",
                        "annotations": {
                            "kubernetes.io/config.mirror": "abc1234"
                        }
                    },
                    "spec": { "nodeName": "k8s-worker-01" },
                    "status": { "phase": "Running" }
                },
                {
                    "metadata": {
                        "name": "completed-job-pod",
                        "namespace": "default"
                    },
                    "spec": { "nodeName": "k8s-worker-01" },
                    "status": { "phase": "Succeeded" }
                }
            ]
        }
        """;

        var emptyPodListJson = """{ "items": [] }""";
        var evictionRequests = new List<string>();
        var listCallCount = 0;

        var (client, requests) = CreateMockK8s(req =>
        {
            var uri = req.RequestUri!.ToString();

            // Patch cordon
            if (req.Method.Method == "PATCH")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            }

            // Pod listing
            if (req.Method == HttpMethod.Get && uri.Contains("/api/v1/pods"))
            {
                listCallCount++;
                var responseContent = listCallCount == 1 ? podListJson : emptyPodListJson;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
                };
            }

            // Pod eviction
            if (req.Method == HttpMethod.Post && uri.Contains("/eviction"))
            {
                evictionRequests.Add(uri);
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);
        var result = await adapter.DrainNodeAsync("k8s-worker-01", TimeSpan.FromSeconds(5));

        Assert.True(result.Success);
        Assert.Equal(1, result.EvictedPodCount);
        Assert.Single(evictionRequests);
        Assert.Contains("/namespaces/default/pods/web-api-pod-1/eviction", evictionRequests[0]);
    }

    [Fact]
    public async Task DrainNodeAsync_RetriesOnHttp429_WhenPdbConflictOccurs()
    {
        var podListJson = """
        {
            "items": [
                {
                    "metadata": {
                        "name": "pdb-protected-pod",
                        "namespace": "production"
                    },
                    "spec": { "nodeName": "k8s-worker-02" },
                    "status": { "phase": "Running" }
                }
            ]
        }
        """;

        var emptyPodListJson = """{ "items": [] }""";
        var evictionAttempts = 0;
        var listCallCount = 0;

        var (client, _) = CreateMockK8s(req =>
        {
            var uri = req.RequestUri!.ToString();

            if (req.Method.Method == "PATCH")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            }

            if (req.Method == HttpMethod.Get && uri.Contains("/api/v1/pods"))
            {
                listCallCount++;
                var responseContent = listCallCount == 1 ? podListJson : emptyPodListJson;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
                };
            }

            if (req.Method == HttpMethod.Post && uri.Contains("/eviction"))
            {
                evictionAttempts++;
                if (evictionAttempts == 1)
                {
                    // First attempt: PDB violation 429 Too Many Requests
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Content = new StringContent("{\"message\":\"Cannot evict pod as it would violate pod disruption budget\"}", Encoding.UTF8, "application/json")
                    };
                }

                // Second attempt: Success
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);
        var result = await adapter.DrainNodeAsync("k8s-worker-02", TimeSpan.FromSeconds(5));

        Assert.True(result.Success);
        Assert.Equal(1, result.EvictedPodCount);
        Assert.True(evictionAttempts >= 2);
    }

    [Fact]
    public async Task KubernetesCordonStep_Rollback_UncordonsNode()
    {
        var isCordoned = false;

        var (client, _) = CreateMockK8s(req =>
        {
            var uri = req.RequestUri!.ToString();
            if (req.Method == HttpMethod.Get && uri.Contains("/api/v1/nodes/k8s-worker-01"))
            {
                var nodeJson = """
                {
                    "metadata": { "name": "k8s-worker-01" },
                    "spec": { "unschedulable": false },
                    "status": {
                        "conditions": [{ "type": "Ready", "status": "True" }]
                    }
                }
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(nodeJson, Encoding.UTF8, "application/json")
                };
            }

            if (req.Method == HttpMethod.Get && uri.Contains("/api/v1/pods"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"items\":[]}", Encoding.UTF8, "application/json")
                };
            }

            if (req.Method.Method == "PATCH" && uri.Contains("/api/v1/nodes/k8s-worker-01"))
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                isCordoned = body.Contains("\"unschedulable\": true");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);

        var host = new HostEntity
        {
            Id = Guid.NewGuid(),
            Hostname = "k8s-worker-01",
            IpAddress = "192.168.1.105",
            OsFamily = "linux_debian",
            TargetType = "baremetal"
        };

        var job = new UpdateJob
        {
            Id = Guid.NewGuid(),
            TargetHostId = host.Id,
            InitiatedBy = "Tester",
            Status = "Running"
        };

        var mockHub = new MockHubContext();
        var mockCmd = new MockCommandExecutor();
        var connManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);

        var context = new JobExecutionContext(
            job,
            host,
            scopeFactory: null!,
            hubContext: mockHub,
            commandExecutor: mockCmd,
            connectionManager: connManager,
            logger: NullLogger.Instance
        );

        var cordonStep = new KubernetesCordonStep(adapter);

        // 1. Execute cordon
        var execResult = await cordonStep.ExecuteAsync(context, CancellationToken.None);
        Assert.True(execResult.Success);
        Assert.True(isCordoned);

        // 2. Rollback uncordons
        await cordonStep.RollbackAsync(context, CancellationToken.None);
        Assert.False(isCordoned);
    }

    private class MockHubContext : IHubContext<JobLogHub, IJobClient>
    {
        public IHubClients<IJobClient> Clients { get; } = new MockHubClients();
        public IGroupManager Groups { get; } = null!;
    }

    private class MockHubClients : IHubClients<IJobClient>
    {
        public IJobClient All => new MockJobClient();
        public IJobClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => new MockJobClient();
        public IJobClient Client(string connectionId) => new MockJobClient();
        public IJobClient Clients(IReadOnlyList<string> connectionIds) => new MockJobClient();
        public IJobClient Group(string groupName) => new MockJobClient();
        public IJobClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new MockJobClient();
        public IJobClient Groups(IReadOnlyList<string> groupNames) => new MockJobClient();
        public IJobClient User(string userId) => new MockJobClient();
        public IJobClient Users(IReadOnlyList<string> userIds) => new MockJobClient();
    }

    private class MockJobClient : IJobClient
    {
        public Task ReceiveLogLine(Guid jobId, long sequenceId, string streamType, string logLine, DateTimeOffset timestamp) => Task.CompletedTask;
        public Task JobStatusChanged(Guid jobId, string status, string? activeStep) => Task.CompletedTask;
    }

    private class MockCommandExecutor : IAgentCommandExecutor
    {
        public Task<AgentCommandResult> ExecuteCommandAsync(Guid hostId, Guid jobId, string command, string[] args, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentCommandResult(true, 0, null));
        }

        public void NotifyFrame(Guid hostId, AgentFrameData frame) { }
    }
}
