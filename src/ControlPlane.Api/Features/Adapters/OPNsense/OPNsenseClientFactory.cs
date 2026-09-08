using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Security;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.OPNsense;

public class OPNsenseClientFactory : IOPNsenseClientFactory
{
    private readonly IAdapterConfigService _configService;
    private readonly ISecretEncryptionService _encryptionService;
    private readonly IOPNsenseClient _client;
    private readonly ILogger<OPNsenseClientFactory> _logger;

    public OPNsenseClientFactory(
        IAdapterConfigService configService,
        ISecretEncryptionService encryptionService,
        IOPNsenseClient client,
        ILogger<OPNsenseClientFactory> logger)
    {
        _configService = configService;
        _encryptionService = encryptionService;
        _client = client;
        _logger = logger;
    }

    public async Task<(IOPNsenseClient Client, OPNsenseStoredInstance Config, string ApiSecret)> ResolveAsync(string instanceId, CancellationToken ct = default)
    {
        var raw = await _configService.GetRawOPNsenseInstanceAsync(instanceId, ct);
        if (raw == null)
        {
            throw new KeyNotFoundException($"OPNsense instance '{instanceId}' not found.");
        }

        var secret = string.IsNullOrWhiteSpace(raw.EncryptedApiSecret)
            ? string.Empty
            : _encryptionService.Decrypt(raw.EncryptedApiSecret);

        return (_client, raw, secret);
    }

    public async Task<List<(OPNsenseStoredInstance Config, string ApiSecret)>> ResolveAllAsync(CancellationToken ct = default)
    {
        var list = await _configService.GetOPNsenseInstancesAsync(ct);
        var result = new List<(OPNsenseStoredInstance Config, string ApiSecret)>();

        foreach (var item in list)
        {
            var raw = await _configService.GetRawOPNsenseInstanceAsync(item.Id, ct);
            if (raw != null)
            {
                var secret = string.IsNullOrWhiteSpace(raw.EncryptedApiSecret)
                    ? string.Empty
                    : _encryptionService.Decrypt(raw.EncryptedApiSecret);

                result.Add((raw, secret));
            }
        }

        return result;
    }

    public IOPNsenseClient GetClient()
    {
        return _client;
    }
}
