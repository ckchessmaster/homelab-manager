using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Idrac;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockIdracClientFactory : IIdracClientFactory
{
    private readonly IIdracClient _client;

    public MockIdracClientFactory(IIdracClient client)
    {
        _client = client;
    }

    public Task<(IIdracClient Client, IdracStoredInstance Config, string Password)> ResolveAsync(string instanceId, CancellationToken ct = default)
    {
        var config = new IdracStoredInstance
        {
            Id = instanceId,
            Name = "pve-node-01 iDRAC9",
            BmcUrl = "https://192.168.1.25",
            Username = "root",
            AllowSelfSignedCert = true
        };
        return Task.FromResult((_client, config, "calvin"));
    }

    public Task<List<(IdracStoredInstance Config, string Password)>> ResolveAllAsync(CancellationToken ct = default)
    {
        var config = new IdracStoredInstance
        {
            Id = "idrac-default",
            Name = "pve-node-01 iDRAC9",
            BmcUrl = "https://192.168.1.25",
            Username = "root",
            AllowSelfSignedCert = true
        };
        return Task.FromResult(new List<(IdracStoredInstance Config, string Password)>
        {
            (config, "calvin")
        });
    }

    public Task<(IIdracClient Client, string BmcUrl, string Username, string Password, bool AllowSelfSigned)?> ResolveByHostBmcIpAsync(string bmcIp, CancellationToken ct = default)
    {
        return Task.FromResult<(IIdracClient Client, string BmcUrl, string Username, string Password, bool AllowSelfSigned)?>((_client, $"https://{bmcIp}", "root", "calvin", true));
    }

    public Task<(IIdracClient Client, IdracStoredInstance Config, string Password)?> ResolveByHostIdAsync(Guid hostId, CancellationToken ct = default)
    {
        var config = new IdracStoredInstance
        {
            Id = "idrac-" + hostId.ToString()[..8],
            Name = "Host iDRAC9",
            BmcUrl = "https://192.168.1.25",
            Username = "root",
            AllowSelfSignedCert = true
        };
        return Task.FromResult<(IIdracClient Client, IdracStoredInstance Config, string Password)?>((_client, config, "calvin"));
    }

    public IIdracClient GetClient() => _client;
}
