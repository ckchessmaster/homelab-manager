using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.HomeAssistant;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockHomeAssistantClientFactory : IHomeAssistantClientFactory
{
    private readonly IHomeAssistantClient _client;

    public MockHomeAssistantClientFactory(IHomeAssistantClient client)
    {
        _client = client;
    }

    public Task<(IHomeAssistantClient Client, HomeAssistantStoredInstance Config, string Token)> ResolveAsync(string instanceId, CancellationToken ct = default)
    {
        var config = new HomeAssistantStoredInstance
        {
            Id = instanceId,
            Name = "Homelab Home Assistant",
            BaseUrl = "http://192.168.1.180:8123",
            AllowSelfSignedCert = true
        };
        return Task.FromResult((_client, config, "demo-long-lived-access-token"));
    }

    public Task<List<(HomeAssistantStoredInstance Config, string Token)>> ResolveAllAsync(CancellationToken ct = default)
    {
        var config = new HomeAssistantStoredInstance
        {
            Id = "ha-default",
            Name = "Homelab Home Assistant",
            BaseUrl = "http://192.168.1.180:8123",
            AllowSelfSignedCert = true
        };
        return Task.FromResult(new List<(HomeAssistantStoredInstance Config, string Token)>
        {
            (config, "demo-long-lived-access-token")
        });
    }

    public IHomeAssistantClient GetClient() => _client;
}
