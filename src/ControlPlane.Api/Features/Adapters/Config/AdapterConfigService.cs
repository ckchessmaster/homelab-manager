using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Security;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Adapters.Config;

public class AdapterConfigService : IAdapterConfigService
{
    public const string ProxmoxSettingKey = "adapter:proxmox";
    public const string MaskedPlaceholder = "••••••••";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ControlPlaneDbContext _dbContext;
    private readonly IOptions<ProxmoxOptions> _defaultOptions;
    private readonly ISecretEncryptionService _encryptionService;
    private readonly ILogger<AdapterConfigService> _logger;

    public AdapterConfigService(
        ControlPlaneDbContext dbContext,
        IOptions<ProxmoxOptions> defaultOptions,
        ISecretEncryptionService encryptionService,
        ILogger<AdapterConfigService> logger)
    {
        _dbContext = dbContext;
        _defaultOptions = defaultOptions;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<ProxmoxConfigDto> GetProxmoxConfigAsync(CancellationToken ct = default)
    {
        try
        {
            var setting = await _dbContext.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == ProxmoxSettingKey, ct);

            if (setting != null && !string.IsNullOrWhiteSpace(setting.ValueJson))
            {
                var stored = JsonSerializer.Deserialize<ProxmoxStoredConfig>(setting.ValueJson, SerializerOptions);
                if (stored != null)
                {
                    var hasSecret = !string.IsNullOrWhiteSpace(stored.ApiTokenSecret);
                    return new ProxmoxConfigDto(
                        BaseUrl: stored.BaseUrl,
                        ApiTokenId: stored.ApiTokenId,
                        ApiTokenSecretMasked: hasSecret ? MaskedPlaceholder : string.Empty,
                        HasSecret: hasSecret,
                        AllowSelfSignedCert: stored.AllowSelfSignedCert,
                        TaskPollTimeoutSeconds: stored.TaskPollTimeoutSeconds,
                        TaskPollIntervalMilliseconds: stored.TaskPollIntervalMilliseconds,
                        UpdatedAt: setting.UpdatedAt
                    );
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read system settings; falling back to configuration defaults.");
        }

        var defaults = _defaultOptions.Value;
        var defHasSecret = !string.IsNullOrWhiteSpace(defaults.ApiTokenSecret);
        return new ProxmoxConfigDto(
            BaseUrl: defaults.BaseUrl ?? string.Empty,
            ApiTokenId: defaults.ApiTokenId ?? string.Empty,
            ApiTokenSecretMasked: defHasSecret ? MaskedPlaceholder : string.Empty,
            HasSecret: defHasSecret,
            AllowSelfSignedCert: defaults.AllowSelfSignedCert,
            TaskPollTimeoutSeconds: defaults.TaskPollTimeoutSeconds,
            TaskPollIntervalMilliseconds: defaults.TaskPollIntervalMilliseconds,
            UpdatedAt: null
        );
    }

    public async Task<ProxmoxConfigDto> SaveProxmoxConfigAsync(SaveProxmoxConfigRequest request, CancellationToken ct = default)
    {
        SystemSetting? setting = null;
        try
        {
            setting = await _dbContext.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == ProxmoxSettingKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not query existing SystemSettings; attempting to continue save.");
        }

        ProxmoxStoredConfig current = new();
        if (setting != null && !string.IsNullOrWhiteSpace(setting.ValueJson))
        {
            try
            {
                current = JsonSerializer.Deserialize<ProxmoxStoredConfig>(setting.ValueJson, SerializerOptions) ?? new();
            }
            catch
            {
                current = new();
            }
        }
        else if (!string.IsNullOrWhiteSpace(_defaultOptions.Value.ApiTokenSecret))
        {
            current.ApiTokenSecret = _defaultOptions.Value.ApiTokenSecret;
        }

        var newSecret = request.ApiTokenSecret;
        if (!string.IsNullOrWhiteSpace(newSecret) && newSecret != MaskedPlaceholder)
        {
            current.ApiTokenSecret = _encryptionService.Encrypt(newSecret.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(current.ApiTokenSecret) && !_encryptionService.IsEncrypted(current.ApiTokenSecret))
        {
            current.ApiTokenSecret = _encryptionService.Encrypt(current.ApiTokenSecret);
        }

        current.BaseUrl = (request.BaseUrl ?? string.Empty).Trim();
        current.ApiTokenId = (request.ApiTokenId ?? string.Empty).Trim();
        current.AllowSelfSignedCert = request.AllowSelfSignedCert;

        if (request.TaskPollTimeoutSeconds.HasValue && request.TaskPollTimeoutSeconds.Value > 0)
        {
            current.TaskPollTimeoutSeconds = request.TaskPollTimeoutSeconds.Value;
        }
        if (request.TaskPollIntervalMilliseconds.HasValue && request.TaskPollIntervalMilliseconds.Value > 0)
        {
            current.TaskPollIntervalMilliseconds = request.TaskPollIntervalMilliseconds.Value;
        }

        var now = DateTimeOffset.UtcNow;
        if (setting == null)
        {
            setting = new SystemSetting
            {
                Key = ProxmoxSettingKey,
                ValueJson = JsonSerializer.Serialize(current, SerializerOptions),
                UpdatedAt = now
            };
            _dbContext.SystemSettings.Add(setting);
        }
        else
        {
            setting.ValueJson = JsonSerializer.Serialize(current, SerializerOptions);
            setting.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("Saved Proxmox adapter settings (BaseUrl: {BaseUrl}, ApiTokenId: {TokenId})", current.BaseUrl, current.ApiTokenId);

        var hasSecret = !string.IsNullOrWhiteSpace(current.ApiTokenSecret);
        return new ProxmoxConfigDto(
            BaseUrl: current.BaseUrl,
            ApiTokenId: current.ApiTokenId,
            ApiTokenSecretMasked: hasSecret ? MaskedPlaceholder : string.Empty,
            HasSecret: hasSecret,
            AllowSelfSignedCert: current.AllowSelfSignedCert,
            TaskPollTimeoutSeconds: current.TaskPollTimeoutSeconds,
            TaskPollIntervalMilliseconds: current.TaskPollIntervalMilliseconds,
            UpdatedAt: now
        );
    }

    public async Task<ProxmoxOptions> GetActiveProxmoxOptionsAsync(CancellationToken ct = default)
    {
        try
        {
            var setting = await _dbContext.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == ProxmoxSettingKey, ct);

            if (setting != null && !string.IsNullOrWhiteSpace(setting.ValueJson))
            {
                var stored = JsonSerializer.Deserialize<ProxmoxStoredConfig>(setting.ValueJson, SerializerOptions);
                if (stored != null && !string.IsNullOrWhiteSpace(stored.BaseUrl))
                {
                    return new ProxmoxOptions
                    {
                        BaseUrl = stored.BaseUrl,
                        ApiTokenId = stored.ApiTokenId,
                        ApiTokenSecret = _encryptionService.Decrypt(stored.ApiTokenSecret),
                        AllowSelfSignedCert = stored.AllowSelfSignedCert,
                        TaskPollTimeoutSeconds = stored.TaskPollTimeoutSeconds > 0 ? stored.TaskPollTimeoutSeconds : 300,
                        TaskPollIntervalMilliseconds = stored.TaskPollIntervalMilliseconds > 0 ? stored.TaskPollIntervalMilliseconds : 1000
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Proxmox adapter configuration from database; using application settings fallback.");
        }

        var def = _defaultOptions.Value;
        return new ProxmoxOptions
        {
            BaseUrl = def.BaseUrl,
            ApiTokenId = def.ApiTokenId,
            ApiTokenSecret = _encryptionService.Decrypt(def.ApiTokenSecret),
            AllowSelfSignedCert = def.AllowSelfSignedCert,
            TaskPollTimeoutSeconds = def.TaskPollTimeoutSeconds,
            TaskPollIntervalMilliseconds = def.TaskPollIntervalMilliseconds
        };
    }
}
