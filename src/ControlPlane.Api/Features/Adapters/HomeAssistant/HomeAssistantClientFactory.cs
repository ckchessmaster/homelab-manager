using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Security;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.HomeAssistant;

public class HomeAssistantClientFactory : IHomeAssistantClientFactory
{
    private readonly IAdapterConfigService _configService;
    private readonly ISecretEncryptionService _encryptionService;
    private readonly IHomeAssistantClient _client;
    private readonly ILogger<HomeAssistantClientFactory> _logger;

    public HomeAssistantClientFactory(
        IAdapterConfigService configService,
        ISecretEncryptionService encryptionService,
        IHomeAssistantClient client,
        ILogger<HomeAssistantClientFactory> logger)
    {
        _configService = configService;
        _encryptionService = encryptionService;
        _client = client;
        _logger = logger;
    }

    public IHomeAssistantClient GetClient() => _client;

    public async Task<(IHomeAssistantClient Client, HomeAssistantStoredInstance Config, string Token)> ResolveAsync(string instanceId, CancellationToken ct = default)
    {
        var raw = await _configService.GetRawHomeAssistantInstanceAsync(instanceId, ct);
        if (raw == null)
        {
            throw new KeyNotFoundException($"Home Assistant instance '{instanceId}' not found.");
        }

        var token = string.IsNullOrWhiteSpace(raw.EncryptedToken)
            ? string.Empty
            : _encryptionService.Decrypt(raw.EncryptedToken);

        return (_client, raw, token);
    }

    public async Task<List<(HomeAssistantStoredInstance Config, string Token)>> ResolveAllAsync(CancellationToken ct = default)
    {
        var list = await _configService.GetHomeAssistantInstancesAsync(ct);
        var result = new List<(HomeAssistantStoredInstance Config, string Token)>();

        foreach (var item in list)
        {
            var raw = await _configService.GetRawHomeAssistantInstanceAsync(item.Id, ct);
            if (raw != null)
            {
                var token = string.IsNullOrWhiteSpace(raw.EncryptedToken)
                    ? string.Empty
                    : _encryptionService.Decrypt(raw.EncryptedToken);

                result.Add((raw, token));
            }
        }

        return result;
    }
}
