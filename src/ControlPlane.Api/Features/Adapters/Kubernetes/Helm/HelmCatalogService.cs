namespace ControlPlane.Api.Features.Adapters.Kubernetes.Helm;

public class HelmCatalogService : IHelmCatalogService
{
    public static readonly List<HelmCatalogItemDto> CuratedCatalog = new()
    {
        new(
            Id: "ingress-nginx",
            Name: "Ingress NGINX",
            Category: "Networking",
            Description: "Production-ready HTTP and reverse proxy ingress controller for Kubernetes using NGINX as reverse proxy and load balancer.",
            RepoUrl: "https://kubernetes.github.io/ingress-nginx",
            ChartName: "ingress-nginx",
            DefaultNamespace: "ingress-nginx",
            DefaultValuesYaml: """
            controller:
              service:
                type: LoadBalancer
              metrics:
                enabled: true
              admissionWebhooks:
                enabled: true
            """,
            Icon: "Globe",
            OfficialUrl: "https://kubernetes.github.io/ingress-nginx/"
        ),
        new(
            Id: "cert-manager",
            Name: "cert-manager",
            Category: "Certificates",
            Description: "Cloud-native certificate management with automatic TLS renewal from Let's Encrypt, HashiCorp Vault, and private PKI.",
            RepoUrl: "https://charts.jetstack.io",
            ChartName: "cert-manager",
            DefaultNamespace: "cert-manager",
            DefaultValuesYaml: """
            crds:
              enabled: true
            prometheus:
              enabled: true
            """,
            Icon: "ShieldCheck",
            OfficialUrl: "https://cert-manager.io/"
        ),
        new(
            Id: "longhorn",
            Name: "Longhorn Storage",
            Category: "Storage",
            Description: "Lightweight, reliable, and powerful distributed block storage system for Kubernetes homelabs and edge clusters.",
            RepoUrl: "https://charts.longhorn.io",
            ChartName: "longhorn",
            DefaultNamespace: "longhorn-system",
            DefaultValuesYaml: """
            persistence:
              defaultClassReplicaCount: 2
            defaultSettings:
              backupTarget: ""
              createDefaultDiskLabeledNodes: true
            """,
            Icon: "HardDrive",
            OfficialUrl: "https://longhorn.io/"
        ),
        new(
            Id: "kube-prometheus-stack",
            Name: "Prometheus & Grafana",
            Category: "Monitoring",
            Description: "Complete Prometheus, Grafana dashboards, and Alertmanager monitoring and observability stack for Kubernetes.",
            RepoUrl: "https://prometheus-community.github.io/helm-charts",
            ChartName: "kube-prometheus-stack",
            DefaultNamespace: "monitoring",
            DefaultValuesYaml: """
            grafana:
              enabled: true
              adminPassword: "prom-operator"
            prometheus:
              prometheusSpec:
                retention: 15d
                scrapeInterval: 30s
            """,
            Icon: "Activity",
            OfficialUrl: "https://github.com/prometheus-community/helm-charts"
        ),
        new(
            Id: "pihole",
            Name: "Pi-hole DNS",
            Category: "Networking",
            Description: "Network-wide ad blocking and local DNS sinkhole protecting your homelab devices without client-side software.",
            RepoUrl: "https://mojo2600.github.io/pihole-kubernetes",
            ChartName: "pihole",
            DefaultNamespace: "pihole",
            DefaultValuesYaml: """
            serviceWeb:
              type: LoadBalancer
              loadBalancerIP: ""
            serviceDns:
              type: LoadBalancer
              loadBalancerIP: ""
            persistentVolumeClaim:
              enabled: true
              size: 5Gi
            """,
            Icon: "Shield",
            OfficialUrl: "https://pi-hole.net/"
        ),
        new(
            Id: "metallb",
            Name: "MetalLB LoadBalancer",
            Category: "Networking",
            Description: "Bare-metal load-balancer implementation for Kubernetes clusters using standard routing protocols (ARP / BGP).",
            RepoUrl: "https://metallb.github.io/metallb",
            ChartName: "metallb",
            DefaultNamespace: "metallb-system",
            DefaultValuesYaml: """
            speaker:
              frr:
                enabled: false
            """,
            Icon: "Network",
            OfficialUrl: "https://metallb.universe.tf/"
        ),
        new(
            Id: "tailscale-operator",
            Name: "Tailscale Operator",
            Category: "Security",
            Description: "Connect Kubernetes workloads, ingresses, and services directly to your private Tailscale Mesh VPN with zero-trust security.",
            RepoUrl: "https://pkgs.tailscale.com/helmcharts",
            ChartName: "tailscale-operator",
            DefaultNamespace: "tailscale",
            DefaultValuesYaml: """
            operatorConfig:
              defaultTags:
                - "tag:k8s"
            """,
            Icon: "Lock",
            OfficialUrl: "https://tailscale.com/kb/1236/kubernetes-operator"
        ),
        new(
            Id: "vaultwarden",
            Name: "Vaultwarden",
            Category: "Security",
            Description: "Lightweight, self-hosted Bitwarden compatible password manager server written in Rust, ideal for homelab deployment.",
            RepoUrl: "https://guerzon.github.io/vaultwarden",
            ChartName: "vaultwarden",
            DefaultNamespace: "vaultwarden",
            DefaultValuesYaml: """
            ingress:
              enabled: false
            storage:
              size: 5Gi
            """,
            Icon: "Key",
            OfficialUrl: "https://github.com/dani-garcia/vaultwarden"
        ),
        new(
            Id: "jellyfin",
            Name: "Jellyfin Media",
            Category: "Media",
            Description: "The Free Software Media System that puts you in control of managing and streaming your media files with hardware transcoding.",
            RepoUrl: "https://geek-cookbook.github.io/charts/",
            ChartName: "jellyfin",
            DefaultNamespace: "media",
            DefaultValuesYaml: """
            service:
              type: ClusterIP
            persistence:
              config:
                enabled: true
                size: 10Gi
            """,
            Icon: "Film",
            OfficialUrl: "https://jellyfin.org/"
        ),
        new(
            Id: "home-assistant",
            Name: "Home Assistant",
            Category: "Smart Home",
            Description: "Open source home automation that puts local control and privacy first, powered by a worldwide community of tinkerers.",
            RepoUrl: "https://pknw1.github.io/charts/",
            ChartName: "home-assistant",
            DefaultNamespace: "home-assistant",
            DefaultValuesYaml: """
            hostNetwork: true
            persistence:
              enabled: true
              size: 10Gi
            """,
            Icon: "Home",
            OfficialUrl: "https://www.home-assistant.io/"
        )
    };

    public IReadOnlyList<HelmCatalogItemDto> GetCatalog() => CuratedCatalog;

    public HelmCatalogItemDto? GetCatalogItem(string id)
    {
        return CuratedCatalog.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
