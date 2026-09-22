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
    public async Task DrainNodeAsync_IgnoresLonghornInstanceManagerPods()
    {
        var podListJson = """
        {
            "items": [
                {
                    "metadata": {
                        "name": "instance-manager-9d58420e8bd90c1998c578e53322d833",
                        "namespace": "longhorn-system"
                    },
                    "spec": { "nodeName": "k8s-worker-01" },
                    "status": { "phase": "Running" }
                },
                {
                    "metadata": {
                        "name": "minio-pool-0-0",
                        "namespace": "minio-tenant"
                    },
                    "spec": { "nodeName": "k8s-worker-01" },
                    "status": { "phase": "Running" }
                }
            ]
        }
        """;

        var emptyPodListJson = """{ "items": [] }""";
        var evictionRequests = new List<string>();
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
        Assert.Contains("minio-pool-0-0", evictionRequests[0]);
        Assert.DoesNotContain("instance-manager", evictionRequests[0]);
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

    [Fact]
    public async Task ApplyManifestYamlAsync_AppliesSecretAndConfigMap()
    {
        var (client, requests) = CreateMockK8s(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);
        var yaml = @"
apiVersion: v1
kind: Secret
metadata:
  name: my-secret
  namespace: test-ns
stringData:
  PASSWORD: password123
---
apiVersion: v1
kind: ConfigMap
metadata:
  name: my-config
  namespace: test-ns
data:
  APP_ENV: dev
";

        var result = await adapter.ApplyManifestYamlAsync(yaml, false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.AffectedResources, r => r.Contains("Secret/my-secret"));
        Assert.Contains(result.AffectedResources, r => r.Contains("ConfigMap/my-config"));
        Assert.Contains(requests, r => r.RequestUri?.AbsolutePath.Contains("/api/v1/namespaces/test-ns/secrets") == true);
        Assert.Contains(requests, r => r.RequestUri?.AbsolutePath.Contains("/api/v1/namespaces/test-ns/configmaps") == true);
    }

    [Fact]
    public async Task ApplyManifestYamlAsync_AppliesExistingService_WhenConflict()
    {
        var (client, requests) = CreateMockK8s(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("/services"))
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("{\"kind\":\"Status\",\"status\":\"Failure\",\"message\":\"services already exists\",\"reason\":\"AlreadyExists\",\"code\":409}", Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"metadata\":{\"name\":\"my-svc\"}}", Encoding.UTF8, "application/json")
            };
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);
        var yaml = @"
apiVersion: v1
kind: Service
metadata:
  name: my-svc
  namespace: test-ns
spec:
  ports:
  - port: 80
";

        var result = await adapter.ApplyManifestYamlAsync(yaml, false, CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Contains(result.AffectedResources, r => r.Contains("Service/my-svc (patched)") || r.Contains("Service/my-svc (updated)"));
    }

    [Fact]
    public async Task ApplyManifestYamlAsync_AppliesExistingDeployment_WithStaleResourceVersionAndStatus()
    {
        var (client, requests) = CreateMockK8s(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("/deployments"))
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("{\"kind\":\"Status\",\"status\":\"Failure\",\"message\":\"deployments already exists\",\"reason\":\"AlreadyExists\",\"code\":409}", Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("/deployments/my-deployment"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"metadata\":{\"name\":\"my-deployment\",\"resourceVersion\":\"current-rv-12345\"}}", Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath.Contains("/deployments/my-deployment"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"metadata\":{\"name\":\"my-deployment\",\"resourceVersion\":\"current-rv-12345\"}}", Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);
        var yaml = @"
apiVersion: apps/v1
kind: Deployment
metadata:
  labels:
    app.kubernetes.io/name: web-app
  name: my-deployment
  namespace: prod
  resourceVersion: 'stale-rv-999'
spec:
  replicas: 3
  template:
    spec:
      containers:
      - name: nginx
        image: nginx:latest
status:
  availableReplicas: 2
";

        var result = await adapter.ApplyManifestYamlAsync(yaml, false, CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Contains(result.AffectedResources, r => r.Contains("Deployment/my-deployment (updated)"));

        var putRequest = requests.FirstOrDefault(r => r.Method == HttpMethod.Put && r.RequestUri!.AbsolutePath.Contains("/deployments/my-deployment"));
        Assert.NotNull(putRequest);
    }

    [Fact]
    public async Task ApplyManifestYamlAsync_AppliesExistingService_WithStaleResourceVersionAndStatusAndPreservesClusterIP()
    {
        var (client, requests) = CreateMockK8s(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("/services"))
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("{\"kind\":\"Status\",\"status\":\"Failure\",\"message\":\"services already exists\",\"reason\":\"AlreadyExists\",\"code\":409}", Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("/services/my-api-svc"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"metadata\":{\"name\":\"my-api-svc\",\"resourceVersion\":\"current-rv-555\"},\"spec\":{\"clusterIP\":\"10.43.0.100\",\"clusterIPs\":[\"10.43.0.100\"]}}", Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"metadata\":{\"name\":\"my-api-svc\"}}", Encoding.UTF8, "application/json")
            };
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);
        var yaml = @"
apiVersion: v1
kind: Service
metadata:
  labels:
    app.kubernetes.io/name: api
  name: my-api-svc
  namespace: prod
  resourceVersion: 'stale-rv-111'
spec:
  ports:
  - port: 8080
    targetPort: 8080
status:
  loadBalancer: {}
";

        var result = await adapter.ApplyManifestYamlAsync(yaml, false, CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Contains(result.AffectedResources, r => r.Contains("Service/my-api-svc"));
    }

    [Fact]
    public async Task GetServiceAsync_ReturnsServiceDetailWithCleanRawYaml()
    {
        var (client, requests) = CreateMockK8s(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/endpoints/web-svc"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"subsets\":[{\"addresses\":[{\"ip\":\"10.244.0.5\"},{\"ip\":\"10.244.0.6\"}]}]}", Encoding.UTF8, "application/json")
                };
            }
            if (req.RequestUri!.AbsolutePath.Contains("/services/web-svc"))
            {
                var json = @"{
                    ""metadata"": {
                        ""name"": ""web-svc"",
                        ""namespace"": ""prod"",
                        ""resourceVersion"": ""12345"",
                        ""uid"": ""some-uid-abc"",
                        ""generation"": 1,
                        ""creationTimestamp"": ""2026-01-01T00:00:00Z"",
                        ""managedFields"": [{""manager"": ""k8s""}]
                    },
                    ""spec"": {
                        ""type"": ""ClusterIP"",
                        ""clusterIP"": ""10.43.0.50"",
                        ""clusterIPs"": [""10.43.0.50""],
                        ""ports"": [{""name"": ""http"", ""port"": 80, ""targetPort"": 8080, ""protocol"": ""TCP""}],
                        ""selector"": {""app"": ""web""}
                    },
                    ""status"": {
                        ""loadBalancer"": {}
                    }
                }";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);
        var detail = await adapter.GetServiceAsync("prod", "web-svc", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("web-svc", detail.Name);
        Assert.Equal("prod", detail.Namespace);
        Assert.Equal("10.43.0.50", detail.ClusterIp);
        Assert.Equal(2, detail.EndpointsCount);
        Assert.NotNull(detail.RawYaml);
        Assert.DoesNotContain("resourceVersion", detail.RawYaml);
        Assert.DoesNotContain("uid", detail.RawYaml);
        Assert.DoesNotContain("managedFields", detail.RawYaml);
        Assert.DoesNotContain("status:", detail.RawYaml);
    }

    [Fact]
    public async Task UpdateServiceAsync_UpdatesServiceSuccessfully()
    {
        var (client, requests) = CreateMockK8s(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("/services/web-svc"))
            {
                var json = @"{
                    ""metadata"": {""name"": ""web-svc"", ""namespace"": ""prod"", ""resourceVersion"": ""100""},
                    ""spec"": {""type"": ""ClusterIP"", ""clusterIP"": ""10.43.0.50""}
                }";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath.Contains("/services/web-svc"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"metadata\":{\"name\":\"web-svc\"}}", Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);
        var reqDto = new K8sUpdateServiceRequestDto(
            Type: "NodePort",
            Ports: new List<K8sServicePortDto> { new("http", 80, "8080", "TCP", 30080) }
        );

        var result = await adapter.UpdateServiceAsync("prod", "web-svc", reqDto, CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Contains(requests, r => r.Method == HttpMethod.Put && r.RequestUri!.AbsolutePath.Contains("/services/web-svc"));
    }

    [Fact]
    public async Task ListServicesAsync_ReturnsServicesWithEndpointsAndPorts()
    {
        var (client, requests) = CreateMockK8s(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/services"))
            {
                var json = @"{
                    ""items"": [
                        {
                            ""metadata"": { ""name"": ""web-svc"", ""namespace"": ""prod"", ""creationTimestamp"": ""2026-01-01T00:00:00Z"" },
                            ""spec"": {
                                ""type"": ""LoadBalancer"",
                                ""clusterIP"": ""10.43.10.20"",
                                ""externalIPs"": [""192.168.1.50""],
                                ""selector"": { ""app"": ""web"" },
                                ""ports"": [
                                    { ""name"": ""http"", ""port"": 80, ""targetPort"": 8080, ""protocol"": ""TCP"", ""nodePort"": 30080 }
                                ]
                            },
                            ""status"": {
                                ""loadBalancer"": {
                                    ""ingress"": [ { ""ip"": ""192.168.1.50"" } ]
                                }
                            }
                        }
                    ]
                }";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            if (req.RequestUri.AbsolutePath.Contains("/endpoints"))
            {
                var json = @"{
                    ""items"": [
                        {
                            ""metadata"": { ""name"": ""web-svc"", ""namespace"": ""prod"" },
                            ""subsets"": [
                                { ""addresses"": [ { ""ip"": ""10.42.0.1"" }, { ""ip"": ""10.42.0.2"" } ] }
                            ]
                        }
                    ]
                }";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);
        var services = await adapter.ListServicesAsync("prod", CancellationToken.None);

        Assert.Single(services);
        var svc = services[0];
        Assert.Equal("web-svc", svc.Name);
        Assert.Equal("prod", svc.Namespace);
        Assert.Equal("LoadBalancer", svc.Type);
        Assert.Equal("10.43.10.20", svc.ClusterIp);
        Assert.Contains("192.168.1.50", svc.ExternalIps!);
        Assert.Equal(2, svc.EndpointsCount);
        Assert.Single(svc.Ports);
        Assert.Equal(80, svc.Ports[0].Port);
        Assert.Equal("8080", svc.Ports[0].TargetPort);
        Assert.Equal(30080, svc.Ports[0].NodePort);
        Assert.Equal("web", svc.Selector!["app"]);
    }

    [Fact]
    public async Task CreateSecretAsync_WithInvalidName_FailsValidation()
    {
        var (client, _) = CreateMockK8s(req => new HttpResponseMessage(HttpStatusCode.OK));
        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);

        var req = new K8sCreateSecretRequestDto("INVALID_UPPERCASE", "default", "Opaque");
        var result = await adapter.CreateSecretAsync("default", req, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lowercase", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSecretAsync_ValidRequest_CreatesSecretSuccessfully()
    {
        var (client, requests) = CreateMockK8s(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("/secrets"))
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"metadata\":{\"name\":\"valid-secret\"}}", Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);
        var req = new K8sCreateSecretRequestDto("valid-secret", "default", "Opaque", new Dictionary<string, string> { ["password"] = "secret123" });
        var result = await adapter.CreateSecretAsync("default", req, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/namespaces/default/secrets"));
    }

    [Fact]
    public async Task GetAppBundleAsync_WithSecretAndConfigMapAndLiteralEnvVars_ParsesCorrectly()
    {
        var deploymentJson = """
        {
            "metadata": {
                "name": "my-app",
                "namespace": "default"
            },
            "spec": {
                "replicas": 2,
                "template": {
                    "spec": {
                        "containers": [
                            {
                                "name": "web",
                                "image": "nginx:latest",
                                "ports": [{ "name": "http", "containerPort": 80, "protocol": "TCP" }],
                                "env": [
                                    { "name": "PORT", "value": "8080" },
                                    {
                                        "name": "DB_PASS",
                                        "valueFrom": {
                                            "secretKeyRef": {
                                                "name": "db-secret",
                                                "key": "password"
                                            }
                                        }
                                    },
                                    {
                                        "name": "APP_ENV",
                                        "valueFrom": {
                                            "configMapKeyRef": {
                                                "name": "app-config",
                                                "key": "env"
                                            }
                                        }
                                    }
                                ],
                                "envFrom": [
                                    {
                                        "secretRef": { "name": "app-secrets" },
                                        "prefix": "SEC_"
                                    },
                                    {
                                        "configMapRef": { "name": "shared-config" }
                                    }
                                ]
                            }
                        ]
                    }
                }
            }
        }
        """;

        var (client, _) = CreateMockK8s(req =>
        {
            var uri = req.RequestUri!.ToString();
            if (req.Method == HttpMethod.Get && uri.Contains("/apis/apps/v1/namespaces/default/deployments/my-app"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(deploymentJson, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);
        var bundle = await adapter.GetAppBundleAsync("default", "my-app", CancellationToken.None);

        Assert.NotNull(bundle);
        Assert.Equal("my-app", bundle.Name);
        Assert.Equal("Deployment", bundle.Kind);
        Assert.Equal(2, bundle.Replicas);
        Assert.Equal(3, bundle.EnvironmentVariables.Count);

        var portVar = bundle.EnvironmentVariables.First(e => e.Key == "PORT");
        Assert.Equal("8080", portVar.Value);
        Assert.False(portVar.IsSecret);
        Assert.Null(portVar.SecretName);
        Assert.Equal("web", portVar.ContainerName);

        var dbPassVar = bundle.EnvironmentVariables.First(e => e.Key == "DB_PASS");
        Assert.True(dbPassVar.IsSecret);
        Assert.Equal("db-secret", dbPassVar.SecretName);
        Assert.Equal("password", dbPassVar.SecretKey);
        Assert.Equal("web", dbPassVar.ContainerName);

        var appEnvVar = bundle.EnvironmentVariables.First(e => e.Key == "APP_ENV");
        Assert.False(appEnvVar.IsSecret);
        Assert.Equal("app-config", appEnvVar.ConfigMapName);
        Assert.Equal("env", appEnvVar.ConfigMapKey);
        Assert.Equal("web", appEnvVar.ContainerName);

        Assert.NotNull(bundle.EnvFrom);
        Assert.Equal(2, bundle.EnvFrom.Count);
        Assert.Equal("app-secrets", bundle.EnvFrom[0].SecretRef);
        Assert.Equal("SEC_", bundle.EnvFrom[0].Prefix);
        Assert.Equal("shared-config", bundle.EnvFrom[1].ConfigMapRef);
    }

    [Fact]
    public async Task GetAppBundleAsync_ForDaemonSet_ReturnsDaemonSetKind()
    {
        var daemonSetJson = """
        {
            "metadata": {
                "name": "node-exporter",
                "namespace": "monitoring"
            },
            "spec": {
                "template": {
                    "spec": {
                        "containers": [
                            {
                                "name": "exporter",
                                "image": "prom/node-exporter:latest",
                                "env": [
                                    { "name": "NODE_NAME", "value": "node-1" }
                                ]
                            }
                        ]
                    }
                }
            },
            "status": {
                "desiredNumberScheduled": 3
            }
        }
        """;

        var (client, _) = CreateMockK8s(req =>
        {
            var uri = req.RequestUri!.ToString();
            if (req.Method == HttpMethod.Get && uri.Contains("/apis/apps/v1/namespaces/monitoring/daemonsets/node-exporter"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(daemonSetJson, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);
        var bundle = await adapter.GetAppBundleAsync("monitoring", "node-exporter", CancellationToken.None);

        Assert.NotNull(bundle);
        Assert.Equal("node-exporter", bundle.Name);
        Assert.Equal("DaemonSet", bundle.Kind);
        Assert.Equal(3, bundle.Replicas);
        Assert.Single(bundle.EnvironmentVariables);
        Assert.Equal("NODE_NAME", bundle.EnvironmentVariables[0].Key);
        Assert.Equal("node-1", bundle.EnvironmentVariables[0].Value);
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
