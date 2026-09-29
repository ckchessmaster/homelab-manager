using ControlPlane.Api.Features.Adapters.Kubernetes;
using k8s;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockKubernetesClientFactory : IKubernetesClientFactory
{
    private readonly IKubernetesAdapter _adapter;
    private readonly IKubernetes _client;

    public MockKubernetesClientFactory(IKubernetesAdapter adapter)
    {
        _adapter = adapter;
        var config = new KubernetesClientConfiguration { Host = "http://127.0.0.1:6443" };
        _client = new Kubernetes(config);
    }

    public Task<IKubernetes> CreateClientAsync(string? clusterId = null, CancellationToken ct = default)
    {
        return Task.FromResult(_client);
    }

    public Task<IKubernetesAdapter> CreateAdapterAsync(string? clusterId = null, CancellationToken ct = default)
    {
        return Task.FromResult(_adapter);
    }
}
