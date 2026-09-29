using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.OPNsense;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockOpnsenseClientFactory : IOPNsenseClientFactory
{
    private readonly IOPNsenseClient _client;

    public MockOpnsenseClientFactory(IOPNsenseClient client)
    {
        _client = client;
    }

    public Task<(IOPNsenseClient Client, OPNsenseStoredInstance Config, string ApiSecret)> ResolveAsync(string instanceId, CancellationToken ct = default)
    {
        var config = new OPNsenseStoredInstance
        {
            Id = instanceId,
            Name = "Homelab Core Gateway",
            BaseUrl = "https://192.168.1.1",
            ApiKey = "demo-api-key",
            AllowSelfSignedCert = true
        };
        return Task.FromResult((_client, config, "demo-api-secret"));
    }

    public Task<List<(OPNsenseStoredInstance Config, string ApiSecret)>> ResolveAllAsync(CancellationToken ct = default)
    {
        var config = new OPNsenseStoredInstance
        {
            Id = "opnsense-default",
            Name = "Homelab Core Gateway",
            BaseUrl = "https://192.168.1.1",
            ApiKey = "demo-api-key",
            AllowSelfSignedCert = true
        };
        return Task.FromResult(new List<(OPNsenseStoredInstance Config, string ApiSecret)>
        {
            (config, "demo-api-secret")
        });
    }

    public IOPNsenseClient GetClient() => _client;
}
