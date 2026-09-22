using ControlPlane.Api.Features.Adapters.Config;

namespace ControlPlane.Api.Features.Adapters.HomeAssistant;

public interface IHomeAssistantClientFactory
{
    IHomeAssistantClient GetClient();
    Task<(IHomeAssistantClient Client, HomeAssistantStoredInstance Config, string Token)> ResolveAsync(string instanceId, CancellationToken ct = default);
    Task<List<(HomeAssistantStoredInstance Config, string Token)>> ResolveAllAsync(CancellationToken ct = default);
}
