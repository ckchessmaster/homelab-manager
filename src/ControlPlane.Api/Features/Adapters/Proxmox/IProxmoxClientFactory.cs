namespace ControlPlane.Api.Features.Adapters.Proxmox;

public interface IProxmoxClientFactory
{
    Task<IProxmoxClient> GetClientAsync(string? instanceId = null, CancellationToken ct = default);
    IProxmoxClient CreateClient(ProxmoxOptions options);
}
