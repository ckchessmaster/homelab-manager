using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.UniFi;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockUniFiClientFactory : IUniFiClientFactory
{
    private readonly IUniFiClient _client;

    public MockUniFiClientFactory(IUniFiClient client)
    {
        _client = client;
    }

    public Task<(IUniFiClient Client, UniFiStoredInstance Config, string Password, string? ApiKey)> ResolveAsync(string instanceId, CancellationToken ct = default)
    {
        var config = new UniFiStoredInstance
        {
            Id = instanceId,
            Name = "Homelab UniFi Network",
            ControllerUrl = "https://192.168.1.1:8443",
            Username = "admin",
            Site = "default"
        };
        return Task.FromResult((_client, config, "demo-password", (string?)null));
    }

    public Task<List<(UniFiStoredInstance Config, string Password, string? ApiKey)>> ResolveAllAsync(CancellationToken ct = default)
    {
        var config = new UniFiStoredInstance
        {
            Id = "unifi-default",
            Name = "Homelab UniFi Network",
            ControllerUrl = "https://192.168.1.1:8443",
            Username = "admin",
            Site = "default"
        };
        return Task.FromResult(new List<(UniFiStoredInstance Config, string Password, string? ApiKey)>
        {
            (config, "demo-password", null)
        });
    }

    public IUniFiClient GetClient() => _client;
}
