using ControlPlane.Api.Features.Adapters.Proxmox;

namespace ControlPlane.Api.Features.Adapters.Config;

public interface IAdapterConfigService
{
    Task<ProxmoxConfigDto> GetProxmoxConfigAsync(CancellationToken ct = default);
    Task<ProxmoxConfigDto> SaveProxmoxConfigAsync(SaveProxmoxConfigRequest request, CancellationToken ct = default);
    Task<ProxmoxOptions> GetActiveProxmoxOptionsAsync(CancellationToken ct = default);
}
