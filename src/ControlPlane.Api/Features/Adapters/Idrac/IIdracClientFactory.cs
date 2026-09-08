using ControlPlane.Api.Features.Adapters.Config;

namespace ControlPlane.Api.Features.Adapters.Idrac;

public interface IIdracClientFactory
{
    Task<(IIdracClient Client, IdracStoredInstance Config, string Password)> ResolveAsync(string instanceId, CancellationToken ct = default);
    Task<List<(IdracStoredInstance Config, string Password)>> ResolveAllAsync(CancellationToken ct = default);
    Task<(IIdracClient Client, string BmcUrl, string Username, string Password, bool AllowSelfSigned)?> ResolveByHostBmcIpAsync(string bmcIp, CancellationToken ct = default);
    IIdracClient GetClient();
}
