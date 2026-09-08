using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Security;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.Idrac;

public class IdracClientFactory : IIdracClientFactory
{
    private readonly IAdapterConfigService _configService;
    private readonly ISecretEncryptionService _encryptionService;
    private readonly IIdracClient _client;
    private readonly ILogger<IdracClientFactory> _logger;

    public IdracClientFactory(
        IAdapterConfigService configService,
        ISecretEncryptionService encryptionService,
        IIdracClient client,
        ILogger<IdracClientFactory> logger)
    {
        _configService = configService;
        _encryptionService = encryptionService;
        _client = client;
        _logger = logger;
    }

    public async Task<(IIdracClient Client, IdracStoredInstance Config, string Password)> ResolveAsync(string instanceId, CancellationToken ct = default)
    {
        var raw = await _configService.GetRawIdracInstanceAsync(instanceId, ct);
        if (raw == null)
        {
            throw new KeyNotFoundException($"iDRAC instance '{instanceId}' not found.");
        }

        var password = string.IsNullOrWhiteSpace(raw.EncryptedPassword)
            ? string.Empty
            : _encryptionService.Decrypt(raw.EncryptedPassword);

        return (_client, raw, password);
    }

    public async Task<List<(IdracStoredInstance Config, string Password)>> ResolveAllAsync(CancellationToken ct = default)
    {
        var list = await _configService.GetIdracInstancesAsync(ct);
        var result = new List<(IdracStoredInstance Config, string Password)>();

        foreach (var item in list)
        {
            var raw = await _configService.GetRawIdracInstanceAsync(item.Id, ct);
            if (raw != null)
            {
                var password = string.IsNullOrWhiteSpace(raw.EncryptedPassword)
                    ? string.Empty
                    : _encryptionService.Decrypt(raw.EncryptedPassword);

                result.Add((raw, password));
            }
        }

        return result;
    }

    public async Task<(IIdracClient Client, string BmcUrl, string Username, string Password, bool AllowSelfSigned)?> ResolveByHostBmcIpAsync(string bmcIp, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bmcIp)) return null;

        var cleanTarget = bmcIp.Trim();
        var all = await ResolveAllAsync(ct);

        // 1. Try to find instance matching HostnameOrIp or BmcUrl host
        foreach (var (config, password) in all)
        {
            if (!string.IsNullOrWhiteSpace(config.HostnameOrIp) &&
                string.Equals(config.HostnameOrIp.Trim(), cleanTarget, StringComparison.OrdinalIgnoreCase))
            {
                return (_client, config.BmcUrl, config.Username, password, config.AllowSelfSignedCert);
            }

            if (Uri.TryCreate(config.BmcUrl, UriKind.Absolute, out var uri) &&
                string.Equals(uri.Host, cleanTarget, StringComparison.OrdinalIgnoreCase))
            {
                return (_client, config.BmcUrl, config.Username, password, config.AllowSelfSignedCert);
            }
        }

        // 2. If single iDRAC instance configured with same subnet or default credentials, allow using its credentials for direct BMC IP
        var first = all.FirstOrDefault();
        if (first.Config != null)
        {
            var bmcUrl = cleanTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         cleanTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                         ? cleanTarget
                         : $"https://{cleanTarget}";

            return (_client, bmcUrl, first.Config.Username, first.Password, first.Config.AllowSelfSignedCert);
        }

        return null;
    }

    public IIdracClient GetClient()
    {
        return _client;
    }
}
