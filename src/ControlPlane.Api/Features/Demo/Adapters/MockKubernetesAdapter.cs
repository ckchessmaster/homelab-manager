using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Kubernetes.Helm;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockKubernetesAdapter : IKubernetesAdapter
{
    private readonly List<K8sDiscoveredNodeDto> _nodes = new()
    {
        new("k8s-cp-01", "192.168.1.10", new List<string> { "control-plane" }, true, false, "Ubuntu 24.04 LTS", "6.8.0-31-generic", "containerd://1.7.19", new Dictionary<string, string> { ["node-role.kubernetes.io/control-plane"] = "" }),
        new("k8s-worker-01", "192.168.1.11", new List<string> { "worker" }, true, false, "Ubuntu 24.04 LTS", "6.8.0-31-generic", "containerd://1.7.19", new Dictionary<string, string> { ["node-role.kubernetes.io/worker"] = "" }),
        new("k8s-worker-02", "192.168.1.12", new List<string> { "worker" }, true, false, "Ubuntu 24.04 LTS", "6.8.0-31-generic", "containerd://1.7.19", new Dictionary<string, string> { ["node-role.kubernetes.io/worker"] = "" })
    };

    public Task<bool> CordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> UncordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);

    public Task<K8sDrainResult> DrainNodeAsync(string nodeName, TimeSpan timeout, bool ignoreDaemonSets = true, bool deleteEmptyDirData = true, CancellationToken ct = default, Func<string, Task>? onProgress = null)
    {
        onProgress?.Invoke($"Evicting pods from node {nodeName}...");
        return Task.FromResult(new K8sDrainResult(nodeName, true, 4, 0, null));
    }

    public Task<K8sNodeStatus?> GetNodeStatusAsync(string nodeName, CancellationToken ct = default)
    {
        var node = _nodes.FirstOrDefault(n => n.Name.Equals(nodeName, StringComparison.OrdinalIgnoreCase));
        if (node == null) return Task.FromResult<K8sNodeStatus?>(null);

        return Task.FromResult<K8sNodeStatus?>(new K8sNodeStatus(
            NodeName: node.Name,
            IsReady: true,
            Unschedulable: false,
            InternalIp: node.InternalIp,
            PodCount: node.Roles.Contains("control-plane") ? 8 : 14
        ));
    }

    public Task<List<K8sDiscoveredNodeDto>> ListNodesAsync(CancellationToken ct = default) => Task.FromResult(new List<K8sDiscoveredNodeDto>(_nodes));

    public Task<KubernetesClusterTestResultDto> TestConnectionAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new KubernetesClusterTestResultDto(true, "v1.31.1", 3, 28, "Cluster reachable (Demo Mode)"));
    }

    public Task<List<string>> ListNamespacesAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new List<string> { "default", "kube-system", "monitoring", "ingress-nginx", "cert-manager" });
    }

    public Task<List<K8sDeploymentSummaryDto>> ListDeploymentsAsync(string? namespaceName = null, CancellationToken ct = default)
    {
        var all = new List<K8sDeploymentSummaryDto>
        {
            new("ingress-nginx-controller", "ingress-nginx", 2, 2, 2, new List<string> { "registry.k8s.io/ingress-nginx/controller:v1.10.1" }, DateTime.UtcNow.AddDays(-14)),
            new("coredns", "kube-system", 2, 2, 2, new List<string> { "registry.k8s.io/coredns/coredns:v1.11.1" }, DateTime.UtcNow.AddDays(-30)),
            new("monitoring-grafana", "monitoring", 1, 1, 1, new List<string> { "grafana/grafana:10.4.1" }, DateTime.UtcNow.AddDays(-7)),
            new("cert-manager", "cert-manager", 1, 1, 1, new List<string> { "quay.io/jetstack/cert-manager-controller:v1.14.4" }, DateTime.UtcNow.AddDays(-10))
        };

        if (!string.IsNullOrWhiteSpace(namespaceName))
        {
            all = all.Where(d => d.Namespace.Equals(namespaceName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return Task.FromResult(all);
    }

    public Task<List<K8sPodSummaryDto>> ListPodsAsync(string? namespaceName = null, string? nodeName = null, CancellationToken ct = default)
    {
        var pods = new List<K8sPodSummaryDto>
        {
            new("ingress-nginx-controller-748596-abcde", "ingress-nginx", "Running", "k8s-worker-01", "10.244.1.15", 0, true, DateTime.UtcNow.AddDays(-3), new List<string> { "controller" }),
            new("ingress-nginx-controller-748596-fghij", "ingress-nginx", "Running", "k8s-worker-02", "10.244.2.22", 0, true, DateTime.UtcNow.AddDays(-3), new List<string> { "controller" }),
            new("monitoring-grafana-67b8d-12345", "monitoring", "Running", "k8s-worker-01", "10.244.1.16", 0, true, DateTime.UtcNow.AddDays(-7), new List<string> { "grafana" }),
            new("cert-manager-58f9d-67890", "cert-manager", "Running", "k8s-worker-02", "10.244.2.23", 0, true, DateTime.UtcNow.AddDays(-10), new List<string> { "cert-manager" })
        };

        if (!string.IsNullOrWhiteSpace(namespaceName))
        {
            pods = pods.Where(p => p.Namespace.Equals(namespaceName, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(nodeName))
        {
            pods = pods.Where(p => p.NodeName != null && p.NodeName.Equals(nodeName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return Task.FromResult(pods);
    }

    public Task<List<K8sWorkloadItemDto>> ListAllWorkloadsAsync(string? namespaceName = null, CancellationToken ct = default)
    {
        var workloads = new List<K8sWorkloadItemDto>
        {
            new("ingress-nginx-controller", "ingress-nginx", "Deployment", 2, 2, 2, new List<string> { "registry.k8s.io/ingress-nginx/controller:v1.10.1" }, DateTime.UtcNow.AddDays(-14), null, null, null, false),
            new("coredns", "kube-system", "Deployment", 2, 2, 2, new List<string> { "registry.k8s.io/coredns/coredns:v1.11.1" }, DateTime.UtcNow.AddDays(-30), null, null, null, true),
            new("monitoring-grafana", "monitoring", "Deployment", 1, 1, 1, new List<string> { "grafana/grafana:10.4.1" }, DateTime.UtcNow.AddDays(-7), null, null, null, false),
            new("cert-manager", "cert-manager", "Deployment", 1, 1, 1, new List<string> { "quay.io/jetstack/cert-manager-controller:v1.14.4" }, DateTime.UtcNow.AddDays(-10), null, null, null, false),
            new("cilium", "kube-system", "DaemonSet", 3, 3, 3, new List<string> { "quay.io/cilium/cilium:v1.15.4" }, DateTime.UtcNow.AddDays(-30), null, null, null, true),
            new("postgres-ha", "default", "StatefulSet", 2, 2, 2, new List<string> { "bitnami/postgresql:16.2.0" }, DateTime.UtcNow.AddDays(-12), null, null, null, false)
        };

        if (!string.IsNullOrWhiteSpace(namespaceName))
        {
            workloads = workloads.Where(w => w.Namespace.Equals(namespaceName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return Task.FromResult(workloads);
    }

    public Task<K8sClusterVitalsDto> GetClusterVitalsAsync(CancellationToken ct = default)
    {
        var nodeVitals = _nodes.Select(n => new K8sNodeVitalDto(
            NodeName: n.Name,
            CpuUsageMillis: n.Roles.Contains("control-plane") ? 250 : 450,
            CpuAllocatableMillis: 4000,
            MemoryUsageBytes: n.Roles.Contains("control-plane") ? 2147483648 : 4294967296,
            MemoryAllocatableBytes: 8589934592,
            DiskPressure: false,
            MemoryPressure: false,
            PidPressure: false,
            Ready: true
        )).ToList();

        return Task.FromResult(new K8sClusterVitalsDto(
            MetricsServerAvailable: true,
            TotalCpuUsageMillis: 1150,
            TotalCpuAllocatableMillis: 12000,
            TotalMemoryUsageBytes: 10737418240,
            TotalMemoryAllocatableBytes: 25769803776,
            Nodes: nodeVitals,
            TopPods: new List<K8sPodVitalDto>()
        ));
    }

    public Task<List<K8sServiceSummaryDto>> ListServicesAsync(string? namespaceName = null, CancellationToken ct = default)
    {
        return Task.FromResult(new List<K8sServiceSummaryDto>
        {
            new("ingress-nginx-controller", "ingress-nginx", "LoadBalancer", "10.96.0.100", new List<string> { "192.168.1.50" }, new List<K8sServicePortDto> { new("http", 80, "80", "TCP", 30080), new("https", 443, "443", "TCP", 30443) }, new Dictionary<string, string> { ["app.kubernetes.io/name"] = "ingress-nginx" }, 2, DateTime.UtcNow.AddDays(-14)),
            new("monitoring-grafana", "monitoring", "ClusterIP", "10.96.45.12", null, new List<K8sServicePortDto> { new("service", 80, "3000", "TCP") }, new Dictionary<string, string> { ["app.kubernetes.io/name"] = "grafana" }, 1, DateTime.UtcNow.AddDays(-7)),
            new("kubernetes", "default", "ClusterIP", "10.96.0.1", null, new List<K8sServicePortDto> { new("https", 443, "6443", "TCP") }, null, 1, DateTime.UtcNow.AddDays(-30))
        });
    }

    public Task<List<K8sIngressSummaryDto>> ListIngressesAsync(string? namespaceName = null, CancellationToken ct = default)
    {
        return Task.FromResult(new List<K8sIngressSummaryDto>
        {
            new("grafana-ingress", "monitoring", "nginx", new List<string> { "grafana.homelab.local" }, new List<K8sIngressRulePathDto> { new("/", "Prefix", "monitoring-grafana", 80, 1) }, new List<string> { "grafana.homelab.local" }, new Dictionary<string, string>(), DateTime.UtcNow.AddDays(-7)),
            new("controlplane-ingress", "default", "nginx", new List<string> { "controlplane.homelab.local" }, new List<K8sIngressRulePathDto> { new("/", "Prefix", "controlplane-api", 5029, 1) }, new List<string> { "controlplane.homelab.local" }, new Dictionary<string, string>(), DateTime.UtcNow.AddDays(-14))
        });
    }
}
