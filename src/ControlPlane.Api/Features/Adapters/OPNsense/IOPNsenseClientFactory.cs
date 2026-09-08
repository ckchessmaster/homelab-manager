using ControlPlane.Api.Features.Adapters.Config;

namespace ControlPlane.Api.Features.Adapters.OPNsense;

public interface IOPNsenseClientFactory
{
    Task<(IOPNsenseClient Client, OPNsenseStoredInstance Config, string ApiSecret)> ResolveAsync(string instanceId, CancellationToken ct = default);
    Task<List<(OPNsenseStoredInstance Config, string ApiSecret)>> ResolveAllAsync(CancellationToken ct = default);
    IOPNsenseClient GetClient();
}
