using ControlPlane.Api.Features.Adapters.Proxmox;

namespace ControlPlane.Api.Features.Adapters.Config;

public interface IAdapterConfigService
{
    Task<ProxmoxConfigDto> GetProxmoxConfigAsync(CancellationToken ct = default);
    Task<ProxmoxConfigDto> SaveProxmoxConfigAsync(SaveProxmoxConfigRequest request, CancellationToken ct = default);
    Task<ProxmoxOptions> GetActiveProxmoxOptionsAsync(CancellationToken ct = default);
    Task<ProxmoxOptions> GetActiveProxmoxOptionsAsync(string? instanceId, CancellationToken ct = default);

    Task<List<ProxmoxInstanceDto>> GetProxmoxInstancesAsync(CancellationToken ct = default);
    Task<ProxmoxInstanceDto?> GetProxmoxInstanceAsync(string id, CancellationToken ct = default);
    Task<ProxmoxInstanceDto> SaveProxmoxInstanceAsync(SaveProxmoxInstanceRequest request, CancellationToken ct = default);
    Task<bool> DeleteProxmoxInstanceAsync(string id, CancellationToken ct = default);

    Task<List<KubernetesClusterDto>> GetKubernetesClustersAsync(CancellationToken ct = default);
    Task<KubernetesClusterDto?> GetKubernetesClusterAsync(string id, CancellationToken ct = default);
    Task<KubernetesStoredCluster?> GetRawKubernetesClusterAsync(string id, CancellationToken ct = default);
    Task<KubernetesClusterDto> SaveKubernetesClusterAsync(SaveKubernetesClusterRequest request, CancellationToken ct = default);
    Task<bool> DeleteKubernetesClusterAsync(string id, CancellationToken ct = default);

    Task<List<UniFiInstanceDto>> GetUniFiInstancesAsync(CancellationToken ct = default);
    Task<UniFiInstanceDto?> GetUniFiInstanceAsync(string id, CancellationToken ct = default);
    Task<UniFiStoredInstance?> GetRawUniFiInstanceAsync(string id, CancellationToken ct = default);
    Task<UniFiInstanceDto> SaveUniFiInstanceAsync(SaveUniFiInstanceRequest request, CancellationToken ct = default);
    Task<bool> DeleteUniFiInstanceAsync(string id, CancellationToken ct = default);

    Task<List<OPNsenseInstanceDto>> GetOPNsenseInstancesAsync(CancellationToken ct = default);
    Task<OPNsenseInstanceDto?> GetOPNsenseInstanceAsync(string id, CancellationToken ct = default);
    Task<OPNsenseStoredInstance?> GetRawOPNsenseInstanceAsync(string id, CancellationToken ct = default);
    Task<OPNsenseInstanceDto> SaveOPNsenseInstanceAsync(SaveOPNsenseInstanceRequest request, CancellationToken ct = default);
    Task<bool> DeleteOPNsenseInstanceAsync(string id, CancellationToken ct = default);

    Task<List<IdracInstanceDto>> GetIdracInstancesAsync(CancellationToken ct = default);
    Task<IdracInstanceDto?> GetIdracInstanceAsync(string id, CancellationToken ct = default);
    Task<IdracStoredInstance?> GetRawIdracInstanceAsync(string id, CancellationToken ct = default);
    Task<IdracInstanceDto> SaveIdracInstanceAsync(SaveIdracInstanceRequest request, CancellationToken ct = default);
    Task<bool> DeleteIdracInstanceAsync(string id, CancellationToken ct = default);
}

