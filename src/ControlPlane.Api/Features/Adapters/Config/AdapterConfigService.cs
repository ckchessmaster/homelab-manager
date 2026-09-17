using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Kubernetes;
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
    public const string ProxmoxInstancesSettingKey = "adapters:proxmox:instances";
    public const string KubernetesClustersKey = "adapters:kubernetes:clusters";
    public const string UniFiInstancesKey = "adapters:unifi:instances";
    public const string OPNsenseInstancesKey = "adapters:opnsense:instances";
    public const string IdracInstancesKey = "adapters:idrac:instances";
    public const string MaskedPlaceholder = "••••••••";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ControlPlaneDbContext _dbContext;
    private readonly IOptions<ProxmoxOptions> _defaultOptions;
    private readonly IOptions<KubernetesConfigOptions>? _defaultK8sOptions;
    private readonly ISecretEncryptionService _encryptionService;
    private readonly ILogger<AdapterConfigService> _logger;

    public AdapterConfigService(
        ControlPlaneDbContext dbContext,
        IOptions<ProxmoxOptions> defaultOptions,
        ISecretEncryptionService encryptionService,
        ILogger<AdapterConfigService> logger,
        IOptions<KubernetesConfigOptions>? defaultK8sOptions = null)
    {
        _dbContext = dbContext;
        _defaultOptions = defaultOptions;
        _defaultK8sOptions = defaultK8sOptions;
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

    public Task<ProxmoxOptions> GetActiveProxmoxOptionsAsync(CancellationToken ct = default)
    {
        return GetActiveProxmoxOptionsAsync(null, ct);
    }

    public async Task<ProxmoxOptions> GetActiveProxmoxOptionsAsync(string? instanceId, CancellationToken ct = default)
    {
        try
        {
            var instances = await LoadStoredInstancesAsync(ct);
            ProxmoxStoredInstance? target = null;

            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                target = instances.FirstOrDefault(i => string.Equals(i.Id, instanceId, StringComparison.OrdinalIgnoreCase));
            }

            target ??= instances.FirstOrDefault();

            if (target != null && !string.IsNullOrWhiteSpace(target.BaseUrl))
            {
                return new ProxmoxOptions
                {
                    BaseUrl = target.BaseUrl,
                    ApiTokenId = target.ApiTokenId,
                    ApiTokenSecret = _encryptionService.Decrypt(target.ApiTokenSecret),
                    AllowSelfSignedCert = target.AllowSelfSignedCert,
                    TaskPollTimeoutSeconds = target.TaskPollTimeoutSeconds > 0 ? target.TaskPollTimeoutSeconds : 300,
                    TaskPollIntervalMilliseconds = target.TaskPollIntervalMilliseconds > 0 ? target.TaskPollIntervalMilliseconds : 1000
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Proxmox instance configuration for '{InstanceId}'; falling back.", instanceId ?? "default");
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

    public async Task<List<ProxmoxInstanceDto>> GetProxmoxInstancesAsync(CancellationToken ct = default)
    {
        var stored = await LoadStoredInstancesAsync(ct);
        return stored.Select(MapToDto).ToList();
    }

    public async Task<ProxmoxInstanceDto?> GetProxmoxInstanceAsync(string id, CancellationToken ct = default)
    {
        var stored = await LoadStoredInstancesAsync(ct);
        var match = stored.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        return match != null ? MapToDto(match) : null;
    }

    public async Task<ProxmoxInstanceDto> SaveProxmoxInstanceAsync(SaveProxmoxInstanceRequest request, CancellationToken ct = default)
    {
        var instances = await LoadStoredInstancesAsync(ct);

        var id = string.IsNullOrWhiteSpace(request.Id)
            ? GenerateInstanceId(request.Name)
            : request.Id.Trim();

        var existing = instances.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        var target = existing ?? new ProxmoxStoredInstance { Id = id };

        target.Name = string.IsNullOrWhiteSpace(request.Name) ? id : request.Name.Trim();
        target.BaseUrl = (request.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        target.ApiTokenId = (request.ApiTokenId ?? string.Empty).Trim();
        target.AllowSelfSignedCert = request.AllowSelfSignedCert;

        if (request.TaskPollTimeoutSeconds.HasValue && request.TaskPollTimeoutSeconds.Value > 0)
        {
            target.TaskPollTimeoutSeconds = request.TaskPollTimeoutSeconds.Value;
        }
        if (request.TaskPollIntervalMilliseconds.HasValue && request.TaskPollIntervalMilliseconds.Value > 0)
        {
            target.TaskPollIntervalMilliseconds = request.TaskPollIntervalMilliseconds.Value;
        }

        var newSecret = request.ApiTokenSecret;
        if (!string.IsNullOrWhiteSpace(newSecret) && newSecret != MaskedPlaceholder)
        {
            target.ApiTokenSecret = _encryptionService.Encrypt(newSecret.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(target.ApiTokenSecret) && !_encryptionService.IsEncrypted(target.ApiTokenSecret))
        {
            target.ApiTokenSecret = _encryptionService.Encrypt(target.ApiTokenSecret);
        }

        target.UpdatedAt = DateTimeOffset.UtcNow;

        if (existing == null)
        {
            instances.Add(target);
        }

        await PersistInstancesAsync(instances, ct);
        _logger.LogInformation("Saved Proxmox instance '{Id}' ({Name}) at {BaseUrl}", target.Id, target.Name, target.BaseUrl);

        return MapToDto(target);
    }

    public async Task<bool> DeleteProxmoxInstanceAsync(string id, CancellationToken ct = default)
    {
        var instances = await LoadStoredInstancesAsync(ct);
        var countRemoved = instances.RemoveAll(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (countRemoved > 0)
        {
            await PersistInstancesAsync(instances, ct);
            _logger.LogInformation("Deleted Proxmox instance '{Id}'", id);
            return true;
        }
        return false;
    }

    private async Task<List<ProxmoxStoredInstance>> LoadStoredInstancesAsync(CancellationToken ct = default)
    {
        try
        {
            var setting = await _dbContext.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == ProxmoxInstancesSettingKey, ct);

            if (setting != null && !string.IsNullOrWhiteSpace(setting.ValueJson))
            {
                var list = JsonSerializer.Deserialize<List<ProxmoxStoredInstance>>(setting.ValueJson, SerializerOptions);
                if (list != null && list.Count > 0)
                {
                    return list;
                }
            }

            // Fallback: check legacy single setting "adapter:proxmox"
            var legacy = await _dbContext.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == ProxmoxSettingKey, ct);

            if (legacy != null && !string.IsNullOrWhiteSpace(legacy.ValueJson))
            {
                var single = JsonSerializer.Deserialize<ProxmoxStoredConfig>(legacy.ValueJson, SerializerOptions);
                if (single != null && !string.IsNullOrWhiteSpace(single.BaseUrl))
                {
                    var migrated = new List<ProxmoxStoredInstance>
                    {
                        new()
                        {
                            Id = "default",
                            Name = "Primary Proxmox VE",
                            BaseUrl = single.BaseUrl,
                            ApiTokenId = single.ApiTokenId,
                            ApiTokenSecret = single.ApiTokenSecret,
                            AllowSelfSignedCert = single.AllowSelfSignedCert,
                            TaskPollTimeoutSeconds = single.TaskPollTimeoutSeconds,
                            TaskPollIntervalMilliseconds = single.TaskPollIntervalMilliseconds,
                            UpdatedAt = legacy.UpdatedAt
                        }
                    };
                    return migrated;
                }
            }

            // Fallback: appsettings options
            var def = _defaultOptions.Value;
            if (!string.IsNullOrWhiteSpace(def.BaseUrl))
            {
                return new List<ProxmoxStoredInstance>
                {
                    new()
                    {
                        Id = "default",
                        Name = "Default Proxmox",
                        BaseUrl = def.BaseUrl,
                        ApiTokenId = def.ApiTokenId ?? string.Empty,
                        ApiTokenSecret = def.ApiTokenSecret ?? string.Empty,
                        AllowSelfSignedCert = def.AllowSelfSignedCert,
                        TaskPollTimeoutSeconds = def.TaskPollTimeoutSeconds,
                        TaskPollIntervalMilliseconds = def.TaskPollIntervalMilliseconds,
                        UpdatedAt = null
                    }
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Proxmox instances setting; returning empty list.");
        }

        return new List<ProxmoxStoredInstance>();
    }

    private async Task PersistInstancesAsync(List<ProxmoxStoredInstance> instances, CancellationToken ct)
    {
        var setting = await _dbContext.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == ProxmoxInstancesSettingKey, ct);

        var now = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(instances, SerializerOptions);

        if (setting == null)
        {
            setting = new SystemSetting
            {
                Key = ProxmoxInstancesSettingKey,
                ValueJson = json,
                UpdatedAt = now
            };
            _dbContext.SystemSettings.Add(setting);
        }
        else
        {
            setting.ValueJson = json;
            setting.UpdatedAt = now;
        }

        // Also sync first instance to legacy setting for backward compatibility
        var first = instances.FirstOrDefault();
        if (first != null)
        {
            var legacy = await _dbContext.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == ProxmoxSettingKey, ct);

            var legacyConfig = new ProxmoxStoredConfig
            {
                BaseUrl = first.BaseUrl,
                ApiTokenId = first.ApiTokenId,
                ApiTokenSecret = first.ApiTokenSecret,
                AllowSelfSignedCert = first.AllowSelfSignedCert,
                TaskPollTimeoutSeconds = first.TaskPollTimeoutSeconds,
                TaskPollIntervalMilliseconds = first.TaskPollIntervalMilliseconds
            };
            var legacyJson = JsonSerializer.Serialize(legacyConfig, SerializerOptions);

            if (legacy == null)
            {
                _dbContext.SystemSettings.Add(new SystemSetting
                {
                    Key = ProxmoxSettingKey,
                    ValueJson = legacyJson,
                    UpdatedAt = now
                });
            }
            else
            {
                legacy.ValueJson = legacyJson;
                legacy.UpdatedAt = now;
            }
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    private static ProxmoxInstanceDto MapToDto(ProxmoxStoredInstance i)
    {
        var hasSecret = !string.IsNullOrWhiteSpace(i.ApiTokenSecret);
        return new ProxmoxInstanceDto(
            Id: i.Id,
            Name: i.Name,
            BaseUrl: i.BaseUrl,
            ApiTokenId: i.ApiTokenId,
            ApiTokenSecretMasked: hasSecret ? MaskedPlaceholder : string.Empty,
            HasSecret: hasSecret,
            AllowSelfSignedCert: i.AllowSelfSignedCert,
            TaskPollTimeoutSeconds: i.TaskPollTimeoutSeconds,
            TaskPollIntervalMilliseconds: i.TaskPollIntervalMilliseconds,
            UpdatedAt: i.UpdatedAt
        );
    }

    private static string GenerateInstanceId(string name)
    {
        var clean = new string(name.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray()).Trim('-');

        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = "pve";
        }
        return $"{clean}-{Guid.NewGuid():N}"[..Math.Min(16, clean.Length + 9)];
    }

    public async Task<List<KubernetesClusterDto>> GetKubernetesClustersAsync(CancellationToken ct = default)
    {
        var stored = await LoadStoredClustersAsync(ct);
        return stored.Select(MapToClusterDto).ToList();
    }

    public async Task<KubernetesClusterDto?> GetKubernetesClusterAsync(string id, CancellationToken ct = default)
    {
        var stored = await LoadStoredClustersAsync(ct);
        var match = stored.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(c.Name, id, StringComparison.OrdinalIgnoreCase));
        return match != null ? MapToClusterDto(match) : null;
    }

    public async Task<KubernetesStoredCluster?> GetRawKubernetesClusterAsync(string id, CancellationToken ct = default)
    {
        var stored = await LoadStoredClustersAsync(ct);
        var match = stored.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(c.Name, id, StringComparison.OrdinalIgnoreCase));
        if (match == null) return null;

        return new KubernetesStoredCluster
        {
            Id = match.Id,
            Name = match.Name,
            ApiServerUrl = match.ApiServerUrl,
            EncryptedKubeConfig = !string.IsNullOrWhiteSpace(match.EncryptedKubeConfig)
                ? _encryptionService.Decrypt(match.EncryptedKubeConfig)
                : null,
            EncryptedToken = !string.IsNullOrWhiteSpace(match.EncryptedToken)
                ? _encryptionService.Decrypt(match.EncryptedToken)
                : null,
            ContextName = match.ContextName,
            SkipTlsVerify = match.SkipTlsVerify,
            UpdatedAt = match.UpdatedAt
        };
    }

    public async Task<KubernetesClusterDto> SaveKubernetesClusterAsync(SaveKubernetesClusterRequest request, CancellationToken ct = default)
    {
        var stored = await LoadStoredClustersAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var id = string.IsNullOrWhiteSpace(request.Id) ? GenerateClusterId(request.Name) : request.Id.Trim();

        var existing = stored.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

        string? encryptedKubeConfig = existing?.EncryptedKubeConfig;
        if (!string.IsNullOrWhiteSpace(request.KubeConfigRaw))
        {
            encryptedKubeConfig = _encryptionService.Encrypt(request.KubeConfigRaw.Trim());
        }

        string? encryptedToken = existing?.EncryptedToken;
        if (!string.IsNullOrWhiteSpace(request.Token))
        {
            encryptedToken = _encryptionService.Encrypt(request.Token.Trim());
        }

        var entry = new KubernetesStoredCluster
        {
            Id = id,
            Name = request.Name.Trim(),
            ApiServerUrl = request.ApiServerUrl?.Trim(),
            EncryptedKubeConfig = encryptedKubeConfig,
            EncryptedToken = encryptedToken,
            ContextName = request.ContextName?.Trim(),
            SkipTlsVerify = request.SkipTlsVerify,
            UpdatedAt = now
        };

        if (existing != null)
        {
            var idx = stored.IndexOf(existing);
            stored[idx] = entry;
        }
        else
        {
            stored.Add(entry);
        }

        await SaveStoredClustersAsync(stored, ct);
        return MapToClusterDto(entry);
    }

    public async Task<bool> DeleteKubernetesClusterAsync(string id, CancellationToken ct = default)
    {
        var stored = await LoadStoredClustersAsync(ct);
        var existing = stored.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)
                                               || string.Equals(c.Name, id, StringComparison.OrdinalIgnoreCase));
        if (existing == null) return false;

        stored.Remove(existing);
        await SaveStoredClustersAsync(stored, ct);
        return true;
    }

    private async Task<List<KubernetesStoredCluster>> LoadStoredClustersAsync(CancellationToken ct)
    {
        try
        {
            var setting = await _dbContext.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == KubernetesClustersKey, ct);

            if (setting != null && !string.IsNullOrWhiteSpace(setting.ValueJson))
            {
                var clusters = JsonSerializer.Deserialize<List<KubernetesStoredCluster>>(setting.ValueJson, SerializerOptions);
                if (clusters != null && clusters.Count > 0)
                {
                    return clusters;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new List<KubernetesStoredCluster>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Kubernetes cluster configurations");
        }

        // Fallback to appsettings.json if present
        if (_defaultK8sOptions?.Value != null)
        {
            var opts = _defaultK8sOptions.Value;
            if (opts.InClusterConfig || !string.IsNullOrWhiteSpace(opts.KubeConfigPath) || !string.IsNullOrWhiteSpace(opts.MasterUri))
            {
                return new List<KubernetesStoredCluster>
                {
                    new()
                    {
                        Id = "default",
                        Name = "Default Cluster",
                        ApiServerUrl = opts.MasterUri,
                        SkipTlsVerify = true,
                        UpdatedAt = DateTimeOffset.UtcNow
                    }
                };
            }
        }

        return new List<KubernetesStoredCluster>();
    }

    private async Task SaveStoredClustersAsync(List<KubernetesStoredCluster> clusters, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(clusters, SerializerOptions);

        var setting = await _dbContext.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == KubernetesClustersKey, ct);

        if (setting == null)
        {
            _dbContext.SystemSettings.Add(new SystemSetting
            {
                Key = KubernetesClustersKey,
                ValueJson = json,
                UpdatedAt = now
            });
        }
        else
        {
            setting.ValueJson = json;
            setting.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    private static KubernetesClusterDto MapToClusterDto(KubernetesStoredCluster c)
    {
        return new KubernetesClusterDto(
            Id: c.Id,
            Name: c.Name,
            ApiServerUrl: c.ApiServerUrl,
            ContextName: c.ContextName,
            HasKubeConfig: !string.IsNullOrWhiteSpace(c.EncryptedKubeConfig),
            HasToken: !string.IsNullOrWhiteSpace(c.EncryptedToken),
            SkipTlsVerify: c.SkipTlsVerify,
            UpdatedAt: c.UpdatedAt
        );
    }

    private static string GenerateClusterId(string name)
    {
        var clean = new string(name.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray()).Trim('-');

        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = "k8s";
        }
        return $"{clean}-{Guid.NewGuid():N}"[..Math.Min(16, clean.Length + 9)];
    }

    public async Task<List<UniFiInstanceDto>> GetUniFiInstancesAsync(CancellationToken ct = default)
    {
        var stored = await LoadStoredUniFiInstancesAsync(ct);
        return stored.Select(MapToUniFiDto).ToList();
    }

    public async Task<UniFiInstanceDto?> GetUniFiInstanceAsync(string id, CancellationToken ct = default)
    {
        var stored = await LoadStoredUniFiInstancesAsync(ct);
        var match = stored.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        return match != null ? MapToUniFiDto(match) : null;
    }

    public async Task<UniFiStoredInstance?> GetRawUniFiInstanceAsync(string id, CancellationToken ct = default)
    {
        var stored = await LoadStoredUniFiInstancesAsync(ct);
        var match = stored.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (match == null) return null;

        return new UniFiStoredInstance
        {
            Id = match.Id,
            Name = match.Name,
            ControllerUrl = match.ControllerUrl,
            Username = match.Username,
            EncryptedPassword = match.EncryptedPassword,
            EncryptedApiKey = match.EncryptedApiKey,
            AuthType = match.AuthType ?? "api_key",
            Site = match.Site,
            AllowSelfSignedCert = match.AllowSelfSignedCert,
            UpdatedAt = match.UpdatedAt
        };
    }

    public async Task<UniFiInstanceDto> SaveUniFiInstanceAsync(SaveUniFiInstanceRequest request, CancellationToken ct = default)
    {
        var stored = await LoadStoredUniFiInstancesAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var id = string.IsNullOrWhiteSpace(request.Id) ? GenerateUniFiInstanceId(request.Name) : request.Id.Trim();

        var existing = stored.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

        var authType = string.IsNullOrWhiteSpace(request.AuthType)
            ? (existing?.AuthType ?? (!string.IsNullOrWhiteSpace(request.ApiKey) ? "api_key" : "credentials"))
            : request.AuthType.Trim().ToLowerInvariant();

        string encryptedPassword = existing?.EncryptedPassword ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(request.Password) && request.Password != MaskedPlaceholder)
        {
            encryptedPassword = _encryptionService.Encrypt(request.Password.Trim());
        }

        string? encryptedApiKey = existing?.EncryptedApiKey;
        if (!string.IsNullOrWhiteSpace(request.ApiKey) && request.ApiKey != MaskedPlaceholder)
        {
            encryptedApiKey = _encryptionService.Encrypt(request.ApiKey.Trim());
        }

        var username = (request.Username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username) && authType == "api_key")
        {
            username = "api-key";
        }

        var entry = new UniFiStoredInstance
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(request.Name) ? id : request.Name.Trim(),
            ControllerUrl = (request.ControllerUrl ?? string.Empty).Trim().TrimEnd('/'),
            Username = username,
            EncryptedPassword = encryptedPassword,
            EncryptedApiKey = encryptedApiKey,
            AuthType = authType,
            Site = string.IsNullOrWhiteSpace(request.Site) ? "default" : request.Site.Trim(),
            AllowSelfSignedCert = request.AllowSelfSignedCert,
            UpdatedAt = now
        };

        if (existing != null)
        {
            var idx = stored.IndexOf(existing);
            stored[idx] = entry;
        }
        else
        {
            stored.Add(entry);
        }

        await SaveStoredUniFiInstancesAsync(stored, ct);
        _logger.LogInformation("Saved UniFi controller instance '{Id}' ({Name}) at {Url} (Auth: {AuthType})", entry.Id, entry.Name, entry.ControllerUrl, entry.AuthType);
        return MapToUniFiDto(entry);
    }

    public async Task<bool> DeleteUniFiInstanceAsync(string id, CancellationToken ct = default)
    {
        var stored = await LoadStoredUniFiInstancesAsync(ct);
        var existing = stored.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing == null) return false;

        stored.Remove(existing);
        await SaveStoredUniFiInstancesAsync(stored, ct);
        _logger.LogInformation("Deleted UniFi controller instance '{Id}'", id);
        return true;
    }

    private async Task<List<UniFiStoredInstance>> LoadStoredUniFiInstancesAsync(CancellationToken ct)
    {
        try
        {
            var setting = await _dbContext.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == UniFiInstancesKey, ct);

            if (setting != null && !string.IsNullOrWhiteSpace(setting.ValueJson))
            {
                var instances = JsonSerializer.Deserialize<List<UniFiStoredInstance>>(setting.ValueJson, SerializerOptions);
                if (instances != null && instances.Count > 0)
                {
                    return instances;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new List<UniFiStoredInstance>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load UniFi controller instances");
        }

        return new List<UniFiStoredInstance>();
    }

    private async Task SaveStoredUniFiInstancesAsync(List<UniFiStoredInstance> instances, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(instances, SerializerOptions);

        var setting = await _dbContext.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == UniFiInstancesKey, ct);

        if (setting == null)
        {
            _dbContext.SystemSettings.Add(new SystemSetting
            {
                Key = UniFiInstancesKey,
                ValueJson = json,
                UpdatedAt = now
            });
        }
        else
        {
            setting.ValueJson = json;
            setting.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    private static UniFiInstanceDto MapToUniFiDto(UniFiStoredInstance inst)
    {
        return new UniFiInstanceDto(
            Id: inst.Id,
            Name: inst.Name,
            ControllerUrl: inst.ControllerUrl,
            Username: inst.Username,
            PasswordMasked: string.IsNullOrWhiteSpace(inst.EncryptedPassword) ? string.Empty : MaskedPlaceholder,
            HasPassword: !string.IsNullOrWhiteSpace(inst.EncryptedPassword),
            Site: string.IsNullOrWhiteSpace(inst.Site) ? "default" : inst.Site,
            AllowSelfSignedCert: inst.AllowSelfSignedCert,
            UpdatedAt: inst.UpdatedAt,
            AuthType: string.IsNullOrWhiteSpace(inst.AuthType) ? (!string.IsNullOrWhiteSpace(inst.EncryptedApiKey) ? "api_key" : "credentials") : inst.AuthType,
            ApiKeyMasked: string.IsNullOrWhiteSpace(inst.EncryptedApiKey) ? null : MaskedPlaceholder,
            HasApiKey: !string.IsNullOrWhiteSpace(inst.EncryptedApiKey)
        );
    }

    private static string GenerateUniFiInstanceId(string name)
    {
        var clean = new string(name.ToLowerInvariant()
            .Replace(' ', '-')
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray()).Trim('-');

        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = "unifi";
        }
        return $"{clean}-{Guid.NewGuid():N}"[..Math.Min(16, clean.Length + 9)];
    }

    public async Task<List<OPNsenseInstanceDto>> GetOPNsenseInstancesAsync(CancellationToken ct = default)
    {
        var stored = await LoadStoredOPNsenseInstancesAsync(ct);
        return stored.Select(MapToOPNsenseDto).ToList();
    }

    public async Task<OPNsenseInstanceDto?> GetOPNsenseInstanceAsync(string id, CancellationToken ct = default)
    {
        var stored = await LoadStoredOPNsenseInstancesAsync(ct);
        var match = stored.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        return match != null ? MapToOPNsenseDto(match) : null;
    }

    public async Task<OPNsenseStoredInstance?> GetRawOPNsenseInstanceAsync(string id, CancellationToken ct = default)
    {
        var stored = await LoadStoredOPNsenseInstancesAsync(ct);
        var match = stored.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (match == null) return null;

        return new OPNsenseStoredInstance
        {
            Id = match.Id,
            Name = match.Name,
            BaseUrl = match.BaseUrl,
            ApiKey = match.ApiKey,
            EncryptedApiSecret = match.EncryptedApiSecret,
            AllowSelfSignedCert = match.AllowSelfSignedCert,
            UpdatedAt = match.UpdatedAt
        };
    }

    public async Task<OPNsenseInstanceDto> SaveOPNsenseInstanceAsync(SaveOPNsenseInstanceRequest request, CancellationToken ct = default)
    {
        var stored = await LoadStoredOPNsenseInstancesAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var id = string.IsNullOrWhiteSpace(request.Id) ? GenerateOPNsenseInstanceId(request.Name) : request.Id.Trim();

        var existing = stored.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

        string encryptedSecret = existing?.EncryptedApiSecret ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(request.ApiSecret) && request.ApiSecret != MaskedPlaceholder)
        {
            encryptedSecret = _encryptionService.Encrypt(request.ApiSecret.Trim());
        }

        var entry = new OPNsenseStoredInstance
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(request.Name) ? id : request.Name.Trim(),
            BaseUrl = (request.BaseUrl ?? string.Empty).Trim().TrimEnd('/'),
            ApiKey = (request.ApiKey ?? string.Empty).Trim(),
            EncryptedApiSecret = encryptedSecret,
            AllowSelfSignedCert = request.AllowSelfSignedCert,
            UpdatedAt = now
        };

        if (existing != null)
        {
            var idx = stored.IndexOf(existing);
            stored[idx] = entry;
        }
        else
        {
            stored.Add(entry);
        }

        await PersistOPNsenseInstancesAsync(stored, ct);
        _logger.LogInformation("Saved OPNsense instance '{InstanceId}' ({Name})", entry.Id, entry.Name);
        return MapToOPNsenseDto(entry);
    }

    public async Task<bool> DeleteOPNsenseInstanceAsync(string id, CancellationToken ct = default)
    {
        var stored = await LoadStoredOPNsenseInstancesAsync(ct);
        var existing = stored.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing == null) return false;

        stored.Remove(existing);
        await PersistOPNsenseInstancesAsync(stored, ct);
        _logger.LogInformation("Deleted OPNsense instance '{InstanceId}'", id);
        return true;
    }

    private async Task<List<OPNsenseStoredInstance>> LoadStoredOPNsenseInstancesAsync(CancellationToken ct)
    {
        try
        {
            var setting = await _dbContext.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == OPNsenseInstancesKey, ct);

            if (setting != null && !string.IsNullOrWhiteSpace(setting.ValueJson))
            {
                return JsonSerializer.Deserialize<List<OPNsenseStoredInstance>>(setting.ValueJson, SerializerOptions)
                    ?? new List<OPNsenseStoredInstance>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load OPNsense instances from system settings.");
        }

        return new List<OPNsenseStoredInstance>();
    }

    private async Task PersistOPNsenseInstancesAsync(List<OPNsenseStoredInstance> instances, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(instances, SerializerOptions);

        var setting = await _dbContext.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == OPNsenseInstancesKey, ct);

        if (setting == null)
        {
            _dbContext.SystemSettings.Add(new SystemSetting
            {
                Key = OPNsenseInstancesKey,
                ValueJson = json,
                UpdatedAt = now
            });
        }
        else
        {
            setting.ValueJson = json;
            setting.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    private static OPNsenseInstanceDto MapToOPNsenseDto(OPNsenseStoredInstance inst)
    {
        return new OPNsenseInstanceDto(
            Id: inst.Id,
            Name: inst.Name,
            BaseUrl: inst.BaseUrl,
            ApiKey: inst.ApiKey,
            ApiSecretMasked: string.IsNullOrWhiteSpace(inst.EncryptedApiSecret) ? string.Empty : MaskedPlaceholder,
            HasSecret: !string.IsNullOrWhiteSpace(inst.EncryptedApiSecret),
            AllowSelfSignedCert: inst.AllowSelfSignedCert,
            UpdatedAt: inst.UpdatedAt
        );
    }

    private static string GenerateOPNsenseInstanceId(string name)
    {
        var clean = new string(name.ToLowerInvariant()
            .Replace(' ', '-')
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray()).Trim('-');

        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = "opnsense";
        }
        return $"{clean}-{Guid.NewGuid():N}"[..Math.Min(16, clean.Length + 9)];
    }

    public async Task<List<IdracInstanceDto>> GetIdracInstancesAsync(CancellationToken ct = default)
    {
        var stored = await LoadStoredIdracInstancesAsync(ct);
        Dictionary<Guid, string>? hostMap = null;
        try
        {
            hostMap = await _dbContext.Hosts.AsNoTracking()
                .Select(h => new { h.Id, Name = h.FriendlyName ?? h.Hostname })
                .ToDictionaryAsync(h => h.Id, h => h.Name, ct);
        }
        catch
        {
            // If hosts table not queried or DbContext unavailable in test
        }

        return stored.Select(inst =>
        {
            string? hostName = null;
            if (inst.HostId.HasValue && hostMap != null && hostMap.TryGetValue(inst.HostId.Value, out var name))
            {
                hostName = name;
            }
            return MapToIdracDto(inst, hostName);
        }).ToList();
    }

    public async Task<IdracInstanceDto?> GetIdracInstanceAsync(string id, CancellationToken ct = default)
    {
        var stored = await LoadStoredIdracInstancesAsync(ct);
        var match = stored.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (match == null) return null;

        string? hostName = null;
        if (match.HostId.HasValue)
        {
            try
            {
                var host = await _dbContext.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == match.HostId.Value, ct);
                hostName = host?.FriendlyName ?? host?.Hostname;
            }
            catch { }
        }

        return MapToIdracDto(match, hostName);
    }

    public async Task<IdracStoredInstance?> GetRawIdracInstanceAsync(string id, CancellationToken ct = default)
    {
        var stored = await LoadStoredIdracInstancesAsync(ct);
        var match = stored.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (match == null) return null;

        return new IdracStoredInstance
        {
            Id = match.Id,
            Name = match.Name,
            BmcUrl = match.BmcUrl,
            Username = match.Username,
            EncryptedPassword = match.EncryptedPassword,
            HostnameOrIp = match.HostnameOrIp,
            AllowSelfSignedCert = match.AllowSelfSignedCert,
            ConnectionMode = match.ConnectionMode ?? "network",
            HostId = match.HostId,
            UpdatedAt = match.UpdatedAt
        };
    }

    public async Task<IdracInstanceDto> SaveIdracInstanceAsync(SaveIdracInstanceRequest request, CancellationToken ct = default)
    {
        var stored = await LoadStoredIdracInstancesAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var id = string.IsNullOrWhiteSpace(request.Id) ? GenerateIdracInstanceId(request.Name) : request.Id.Trim();

        var existing = stored.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

        string encryptedPassword = existing?.EncryptedPassword ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(request.Password) && request.Password != MaskedPlaceholder)
        {
            encryptedPassword = _encryptionService.Encrypt(request.Password.Trim());
        }

        var connectionMode = string.Equals(request.ConnectionMode, "agent", StringComparison.OrdinalIgnoreCase)
            ? "agent"
            : "network";

        var bmcUrl = (request.BmcUrl ?? string.Empty).Trim().TrimEnd('/');
        if (connectionMode == "agent" && string.IsNullOrWhiteSpace(bmcUrl))
        {
            bmcUrl = request.HostId.HasValue ? $"agent://{request.HostId.Value}" : "agent://host";
        }

        var username = (request.Username ?? (connectionMode == "agent" ? "agent" : string.Empty)).Trim();

        var entry = new IdracStoredInstance
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(request.Name) ? id : request.Name.Trim(),
            BmcUrl = bmcUrl,
            Username = username,
            EncryptedPassword = encryptedPassword,
            HostnameOrIp = string.IsNullOrWhiteSpace(request.HostnameOrIp) ? null : request.HostnameOrIp.Trim(),
            AllowSelfSignedCert = request.AllowSelfSignedCert,
            ConnectionMode = connectionMode,
            HostId = request.HostId,
            UpdatedAt = now
        };

        if (existing != null)
        {
            var idx = stored.IndexOf(existing);
            stored[idx] = entry;
        }
        else
        {
            stored.Add(entry);
        }

        await PersistIdracInstancesAsync(stored, ct);
        _logger.LogInformation("Saved iDRAC instance '{InstanceId}' ({Name}) mode '{ConnectionMode}'", entry.Id, entry.Name, entry.ConnectionMode);

        string? hostName = null;
        if (entry.HostId.HasValue)
        {
            try
            {
                var host = await _dbContext.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == entry.HostId.Value, ct);
                hostName = host?.FriendlyName ?? host?.Hostname;
            }
            catch { }
        }

        return MapToIdracDto(entry, hostName);
    }

    public async Task<bool> DeleteIdracInstanceAsync(string id, CancellationToken ct = default)
    {
        var stored = await LoadStoredIdracInstancesAsync(ct);
        var existing = stored.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing == null) return false;

        stored.Remove(existing);
        await PersistIdracInstancesAsync(stored, ct);
        _logger.LogInformation("Deleted iDRAC instance '{InstanceId}'", id);
        return true;
    }

    private async Task<List<IdracStoredInstance>> LoadStoredIdracInstancesAsync(CancellationToken ct)
    {
        try
        {
            var setting = await _dbContext.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == IdracInstancesKey, ct);

            if (setting != null && !string.IsNullOrWhiteSpace(setting.ValueJson))
            {
                return JsonSerializer.Deserialize<List<IdracStoredInstance>>(setting.ValueJson, SerializerOptions)
                    ?? new List<IdracStoredInstance>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load iDRAC instances from system settings.");
        }

        return new List<IdracStoredInstance>();
    }

    private async Task PersistIdracInstancesAsync(List<IdracStoredInstance> instances, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(instances, SerializerOptions);

        var setting = await _dbContext.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == IdracInstancesKey, ct);

        if (setting == null)
        {
            _dbContext.SystemSettings.Add(new SystemSetting
            {
                Key = IdracInstancesKey,
                ValueJson = json,
                UpdatedAt = now
            });
        }
        else
        {
            setting.ValueJson = json;
            setting.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    private static IdracInstanceDto MapToIdracDto(IdracStoredInstance inst, string? hostName = null)
    {
        return new IdracInstanceDto(
            Id: inst.Id,
            Name: inst.Name,
            BmcUrl: inst.BmcUrl,
            Username: inst.Username,
            PasswordMasked: string.IsNullOrWhiteSpace(inst.EncryptedPassword) ? string.Empty : MaskedPlaceholder,
            HasPassword: !string.IsNullOrWhiteSpace(inst.EncryptedPassword),
            HostnameOrIp: inst.HostnameOrIp,
            AllowSelfSignedCert: inst.AllowSelfSignedCert,
            UpdatedAt: inst.UpdatedAt,
            ConnectionMode: inst.ConnectionMode ?? "network",
            HostId: inst.HostId,
            HostName: hostName
        );
    }

    private static string GenerateIdracInstanceId(string name)
    {
        var clean = new string(name.ToLowerInvariant()
            .Replace(' ', '-')
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray()).Trim('-');

        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = "idrac";
        }
        return $"{clean}-{Guid.NewGuid():N}"[..Math.Min(16, clean.Length + 9)];
    }
}

