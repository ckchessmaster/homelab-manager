using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Security;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.UniFi;

public class UniFiClientFactory : IUniFiClientFactory
{
    private readonly IAdapterConfigService _configService;
    private readonly ISecretEncryptionService _encryptionService;
    private readonly IUniFiClient _client;
    private readonly ILogger<UniFiClientFactory> _logger;

    public UniFiClientFactory(
        IAdapterConfigService configService,
        ISecretEncryptionService encryptionService,
        IUniFiClient client,
        ILogger<UniFiClientFactory> logger)
    {
        _configService = configService;
        _encryptionService = encryptionService;
        _client = client;
        _logger = logger;
    }

    public async Task<(IUniFiClient Client, UniFiStoredInstance Config, string Password, string? ApiKey)> ResolveAsync(string instanceId, CancellationToken ct = default)
    {
        var raw = await _configService.GetRawUniFiInstanceAsync(instanceId, ct);
        if (raw == null)
        {
            throw new KeyNotFoundException($"UniFi controller instance '{instanceId}' not found.");
        }

        var password = string.IsNullOrWhiteSpace(raw.EncryptedPassword)
            ? string.Empty
            : _encryptionService.Decrypt(raw.EncryptedPassword);

        var apiKey = string.IsNullOrWhiteSpace(raw.EncryptedApiKey)
            ? null
            : _encryptionService.Decrypt(raw.EncryptedApiKey);

        return (_client, raw, password, apiKey);
    }

    public async Task<List<(UniFiStoredInstance Config, string Password, string? ApiKey)>> ResolveAllAsync(CancellationToken ct = default)
    {
        var list = await _configService.GetUniFiInstancesAsync(ct);
        var result = new List<(UniFiStoredInstance Config, string Password, string? ApiKey)>();

        foreach (var item in list)
        {
            var raw = await _configService.GetRawUniFiInstanceAsync(item.Id, ct);
            if (raw != null)
            {
                var password = string.IsNullOrWhiteSpace(raw.EncryptedPassword)
                    ? string.Empty
                    : _encryptionService.Decrypt(raw.EncryptedPassword);

                var apiKey = string.IsNullOrWhiteSpace(raw.EncryptedApiKey)
                    ? null
                    : _encryptionService.Decrypt(raw.EncryptedApiKey);

                result.Add((raw, password, apiKey));
            }
        }

        return result;
    }

    public IUniFiClient GetClient()
    {
        return _client;
    }
}
