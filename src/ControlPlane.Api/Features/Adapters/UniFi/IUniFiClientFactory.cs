using ControlPlane.Api.Features.Adapters.Config;

namespace ControlPlane.Api.Features.Adapters.UniFi;

public interface IUniFiClientFactory
{
    Task<(IUniFiClient Client, UniFiStoredInstance Config, string Password, string? ApiKey)> ResolveAsync(string instanceId, CancellationToken ct = default);
    Task<List<(UniFiStoredInstance Config, string Password, string? ApiKey)>> ResolveAllAsync(CancellationToken ct = default);
    IUniFiClient GetClient();
}
