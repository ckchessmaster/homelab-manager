using ControlPlane.Api.Features.Adapters.Proxmox;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockProxmoxClientFactory : IProxmoxClientFactory
{
    private readonly IProxmoxClient _client;

    public MockProxmoxClientFactory(IProxmoxClient client)
    {
        _client = client;
    }

    public Task<IProxmoxClient> GetClientAsync(string? instanceId = null, CancellationToken ct = default)
    {
        return Task.FromResult(_client);
    }

    public IProxmoxClient CreateClient(ProxmoxOptions options)
    {
        return _client;
    }
}
