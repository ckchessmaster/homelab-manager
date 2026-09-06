using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Security;

/// <summary>
/// Hosted startup service that scans stored adapter configurations in SystemSettings
/// and transparently migrates any unencrypted legacy secrets to AES-256-GCM authenticated encryption.
/// </summary>
public class SecretsMigrationWorker : IHostedService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SecretsMigrationWorker> _logger;

    public SecretsMigrationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<SecretsMigrationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var encryptionService = scope.ServiceProvider.GetRequiredService<ISecretEncryptionService>();

            var adapterSettings = await db.SystemSettings
                .Where(s => s.Key.StartsWith("adapter:"))
                .ToListAsync(cancellationToken);

            var migratedCount = 0;

            foreach (var setting in adapterSettings)
            {
                if (string.IsNullOrWhiteSpace(setting.ValueJson))
                {
                    continue;
                }

                if (setting.Key == AdapterConfigService.ProxmoxSettingKey)
                {
                    try
                    {
                        var config = JsonSerializer.Deserialize<ProxmoxStoredConfig>(setting.ValueJson, SerializerOptions);
                        if (config != null && !string.IsNullOrWhiteSpace(config.ApiTokenSecret) && !encryptionService.IsEncrypted(config.ApiTokenSecret))
                        {
                            config.ApiTokenSecret = encryptionService.Encrypt(config.ApiTokenSecret);
                            setting.ValueJson = JsonSerializer.Serialize(config, SerializerOptions);
                            setting.UpdatedAt = DateTimeOffset.UtcNow;
                            migratedCount++;
                            _logger.LogInformation("Migrated legacy plaintext adapter secret '{SettingKey}' to AES-256-GCM encryption.", setting.Key);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse or migrate adapter setting '{SettingKey}'.", setting.Key);
                    }
                }
            }

            if (migratedCount > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Successfully migrated {Count} adapter secrets to AES-256-GCM encryption.", migratedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during startup secrets migration.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
