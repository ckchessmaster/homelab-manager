using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Security;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using EFCore.NamingConventions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Tests;

public class SecretEncryptionTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly ControlPlaneDbContext _db;
    private readonly byte[] _staticTestKey;
    private readonly ISecurityKeyProvider _staticKeyProvider;
    private readonly ISecretEncryptionService _encryptionService;

    public SecretEncryptionTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        var dbOptions = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseSqlite(_sqliteConnection)
            .UseSnakeCaseNamingConvention()
            .Options;

        _db = new ControlPlaneDbContext(dbOptions);
        _db.Database.EnsureCreated();

        // 32-byte (256-bit) static test key
        _staticTestKey = Encoding.UTF8.GetBytes("12345678901234567890123456789012");
        _staticKeyProvider = new TestKeyProvider(_staticTestKey);
        _encryptionService = new SecretEncryptionService(_staticKeyProvider);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private sealed class TestKeyProvider : ISecurityKeyProvider
    {
        private readonly byte[] _key;

        public TestKeyProvider(byte[] key)
        {
            _key = key;
        }

        public byte[] GetMasterKey() => (byte[])_key.Clone();
        public string KeySource => "Test";
        public string? KeyFilePath => null;
    }

    [Fact]
    public void EncryptAndDecrypt_Roundtrip_Succeeds()
    {
        var secrets = new[]
        {
            "super-secret-token",
            "pve-token!root@pam!abc1234-5678",
            "password-with-special-characters:!@#$%^&*()_+~`|}{[]:;?><,./-=",
            "unicode-secrets:🔑🔐🛡️-こんにちは-世界",
            "{\"token\":\"nested-json-secret\",\"id\":\"12345\"}",
            new string('a', 10000) // 10KB payload
        };

        foreach (var secret in secrets)
        {
            var encrypted = _encryptionService.Encrypt(secret);

            Assert.NotNull(encrypted);
            Assert.StartsWith(SecretEncryptionService.EnvelopePrefix, encrypted);

            var parts = encrypted[SecretEncryptionService.EnvelopePrefix.Length..].Split(':');
            Assert.Equal(3, parts.Length);

            var iv = Convert.FromBase64String(parts[0]);
            var cipher = Convert.FromBase64String(parts[1]);
            var tag = Convert.FromBase64String(parts[2]);

            Assert.Equal(12, iv.Length); // 96-bit nonce
            Assert.Equal(16, tag.Length); // 128-bit auth tag
            Assert.Equal(Encoding.UTF8.GetByteCount(secret), cipher.Length);

            var decrypted = _encryptionService.Decrypt(encrypted);
            Assert.Equal(secret, decrypted);
        }
    }

    [Fact]
    public void Encrypt_EmptyOrNull_ReturnsOriginal()
    {
        Assert.Equal(string.Empty, _encryptionService.Encrypt(string.Empty));
        Assert.Equal(string.Empty, _encryptionService.Encrypt(null));
        Assert.Equal(string.Empty, _encryptionService.Decrypt(string.Empty));
        Assert.Equal(string.Empty, _encryptionService.Decrypt(null));
    }

    [Fact]
    public void Encrypt_ProducesUniqueCiphertext_ForSamePlaintext()
    {
        var secret = "repeatable-secret";
        var c1 = _encryptionService.Encrypt(secret);
        var c2 = _encryptionService.Encrypt(secret);

        Assert.NotEqual(c1, c2); // Unique nonces ensure non-identical ciphertexts
        Assert.Equal(secret, _encryptionService.Decrypt(c1));
        Assert.Equal(secret, _encryptionService.Decrypt(c2));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
    {
        var encrypted = _encryptionService.Encrypt("secret-value-to-tamper");
        var parts = encrypted[SecretEncryptionService.EnvelopePrefix.Length..].Split(':');

        var cipherBytes = Convert.FromBase64String(parts[1]);
        cipherBytes[0] ^= 0xFF; // Flip bits in ciphertext
        var tamperedCipher = Convert.ToBase64String(cipherBytes);

        var tamperedEnvelope = $"{SecretEncryptionService.EnvelopePrefix}{parts[0]}:{tamperedCipher}:{parts[2]}";

        Assert.ThrowsAny<CryptographicException>(() => _encryptionService.Decrypt(tamperedEnvelope));
    }

    [Fact]
    public void Decrypt_TamperedTag_ThrowsCryptographicException()
    {
        var encrypted = _encryptionService.Encrypt("secret-value-to-tamper");
        var parts = encrypted[SecretEncryptionService.EnvelopePrefix.Length..].Split(':');

        var tagBytes = Convert.FromBase64String(parts[2]);
        tagBytes[0] ^= 0xFF; // Flip bits in authentication tag
        var tamperedTag = Convert.ToBase64String(tagBytes);

        var tamperedEnvelope = $"{SecretEncryptionService.EnvelopePrefix}{parts[0]}:{parts[1]}:{tamperedTag}";

        Assert.ThrowsAny<CryptographicException>(() => _encryptionService.Decrypt(tamperedEnvelope));
    }

    [Fact]
    public void Decrypt_TamperedIV_ThrowsCryptographicException()
    {
        var encrypted = _encryptionService.Encrypt("secret-value-to-tamper");
        var parts = encrypted[SecretEncryptionService.EnvelopePrefix.Length..].Split(':');

        var ivBytes = Convert.FromBase64String(parts[0]);
        ivBytes[0] ^= 0xFF; // Flip bits in IV
        var tamperedIv = Convert.ToBase64String(ivBytes);

        var tamperedEnvelope = $"{SecretEncryptionService.EnvelopePrefix}{tamperedIv}:{parts[1]}:{parts[2]}";

        Assert.ThrowsAny<CryptographicException>(() => _encryptionService.Decrypt(tamperedEnvelope));
    }

    [Fact]
    public void Decrypt_LegacyPlaintext_ReturnsUnmodified()
    {
        var legacySecrets = new[]
        {
            "my-raw-plaintext-token-1234",
            "root@pam!token-secret",
            "unencrypted:colon:separated:text"
        };

        foreach (var plain in legacySecrets)
        {
            var result = _encryptionService.Decrypt(plain);
            Assert.Equal(plain, result);
        }
    }

    [Fact]
    public void IsEncrypted_DetectsEnvelopeCorrectly()
    {
        Assert.False(_encryptionService.IsEncrypted(null));
        Assert.False(_encryptionService.IsEncrypted(""));
        Assert.False(_encryptionService.IsEncrypted("plaintext-secret"));
        Assert.False(_encryptionService.IsEncrypted("enc:v2:not-supported"));

        var encrypted = _encryptionService.Encrypt("some-secret");
        Assert.True(_encryptionService.IsEncrypted(encrypted));
    }

    [Fact]
    public void EnvironmentOrFileKeyProvider_ResolvesEnvironmentKey()
    {
        var testKeyBytes = RandomNumberGenerator.GetBytes(32);
        var base64Key = Convert.ToBase64String(testKeyBytes);

        var envVarName = EnvironmentOrFileKeyProvider.EnvironmentVariableName;
        var originalEnv = Environment.GetEnvironmentVariable(envVarName);

        try
        {
            Environment.SetEnvironmentVariable(envVarName, base64Key);

            var options = Options.Create(new SecurityOptions());
            var provider = new EnvironmentOrFileKeyProvider(options, NullLogger<EnvironmentOrFileKeyProvider>.Instance);

            Assert.Equal("Environment", provider.KeySource);
            Assert.Equal(testKeyBytes, provider.GetMasterKey());
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, originalEnv);
        }
    }

    [Fact]
    public void EnvironmentOrFileKeyProvider_ResolvesFileKey()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"master-key-test-{Guid.NewGuid():N}.key");
        var testKeyBytes = RandomNumberGenerator.GetBytes(32);
        var base64Key = Convert.ToBase64String(testKeyBytes);

        try
        {
            File.WriteAllText(tempFile, base64Key);

            var options = Options.Create(new SecurityOptions
            {
                KeyFilePath = tempFile,
                AutoGenerateKeyIfMissing = false
            });

            // Ensure env var is cleared for this test
            var envVarName = EnvironmentOrFileKeyProvider.EnvironmentVariableName;
            var originalEnv = Environment.GetEnvironmentVariable(envVarName);
            try
            {
                Environment.SetEnvironmentVariable(envVarName, null);

                var provider = new EnvironmentOrFileKeyProvider(options, NullLogger<EnvironmentOrFileKeyProvider>.Instance);

                Assert.Equal("File", provider.KeySource);
                Assert.Equal(tempFile, provider.KeyFilePath);
                Assert.Equal(testKeyBytes, provider.GetMasterKey());
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVarName, originalEnv);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void EnvironmentOrFileKeyProvider_AutoGeneratesAndSecuresFile_WhenMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"master-key-gen-{Guid.NewGuid():N}");
        var tempFile = Path.Combine(tempDir, "master.key");

        var envVarName = EnvironmentOrFileKeyProvider.EnvironmentVariableName;
        var originalEnv = Environment.GetEnvironmentVariable(envVarName);

        try
        {
            Environment.SetEnvironmentVariable(envVarName, null);

            var options = Options.Create(new SecurityOptions
            {
                KeyFilePath = tempFile,
                AutoGenerateKeyIfMissing = true
            });

            var provider = new EnvironmentOrFileKeyProvider(options, NullLogger<EnvironmentOrFileKeyProvider>.Instance);

            Assert.Equal("Generated", provider.KeySource);
            Assert.Equal(tempFile, provider.KeyFilePath);
            Assert.True(File.Exists(tempFile));

            var key = provider.GetMasterKey();
            Assert.Equal(32, key.Length);

            var fileContent = File.ReadAllText(tempFile).Trim();
            var parsedFileKey = Convert.FromBase64String(fileContent);
            Assert.Equal(key, parsedFileKey);

            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(tempFile);
                Assert.True(mode.HasFlag(UnixFileMode.UserRead));
                Assert.True(mode.HasFlag(UnixFileMode.UserWrite));
                Assert.False(mode.HasFlag(UnixFileMode.GroupRead));
                Assert.False(mode.HasFlag(UnixFileMode.OtherRead));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, originalEnv);
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AdapterConfigService_EncryptsSecretInDatabase_AndMasksInDto_AndDecryptsForActiveOptions()
    {
        var defaultOptions = Options.Create(new ProxmoxOptions());
        var service = new AdapterConfigService(_db, defaultOptions, _encryptionService, NullLogger<AdapterConfigService>.Instance);

        var plainToken = "my-secret-proxmox-api-token-xyz-123";

        // Save new config
        var saved = await service.SaveProxmoxConfigAsync(new SaveProxmoxConfigRequest(
            BaseUrl: "https://pve.homelab.local:8006",
            ApiTokenId: "root@pam!test",
            ApiTokenSecret: plainToken,
            AllowSelfSignedCert: true
        ));

        // 1. DTO returned from Save has masked placeholder
        Assert.True(saved.HasSecret);
        Assert.Equal(AdapterConfigService.MaskedPlaceholder, saved.ApiTokenSecretMasked);

        // 2. Querying database directly confirms secret is stored encrypted at rest
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == AdapterConfigService.ProxmoxSettingKey);
        Assert.NotNull(setting);
        Assert.DoesNotContain(plainToken, setting.ValueJson); // Raw secret is never in plain text
        Assert.Contains(SecretEncryptionService.EnvelopePrefix, setting.ValueJson);

        var stored = JsonSerializer.Deserialize<ProxmoxStoredConfig>(setting.ValueJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(stored);
        Assert.True(_encryptionService.IsEncrypted(stored.ApiTokenSecret));

        // 3. GetProxmoxConfigAsync masks the secret for client egress
        var retrievedDto = await service.GetProxmoxConfigAsync();
        Assert.True(retrievedDto.HasSecret);
        Assert.Equal(AdapterConfigService.MaskedPlaceholder, retrievedDto.ApiTokenSecretMasked);

        // 4. GetActiveProxmoxOptionsAsync decrypts the secret for server-side adapter calls
        var activeOptions = await service.GetActiveProxmoxOptionsAsync();
        Assert.Equal("https://pve.homelab.local:8006", activeOptions.BaseUrl);
        Assert.Equal("root@pam!test", activeOptions.ApiTokenId);
        Assert.Equal(plainToken, activeOptions.ApiTokenSecret);
    }

    [Fact]
    public async Task SecretsMigrationWorker_UpgradesLegacyPlaintextSecrets()
    {
        var legacyPlainToken = "legacy-unencrypted-secret-token";
        var legacyConfig = new ProxmoxStoredConfig
        {
            BaseUrl = "https://pve.homelab.local:8006",
            ApiTokenId = "root@pam!token",
            ApiTokenSecret = legacyPlainToken,
            AllowSelfSignedCert = true
        };

        var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        _db.SystemSettings.Add(new SystemSetting
        {
            Key = AdapterConfigService.ProxmoxSettingKey,
            ValueJson = JsonSerializer.Serialize(legacyConfig, serializerOptions),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await _db.SaveChangesAsync();

        // Verify initial state is plaintext
        var initialSetting = await _db.SystemSettings.FirstAsync(s => s.Key == AdapterConfigService.ProxmoxSettingKey);
        Assert.Contains(legacyPlainToken, initialSetting.ValueJson);

        // Setup DI for migration worker
        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddSingleton(_encryptionService);
        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var worker = new SecretsMigrationWorker(scopeFactory, NullLogger<SecretsMigrationWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);

        // Verify upgraded state is encrypted
        var migratedSetting = await _db.SystemSettings.FirstAsync(s => s.Key == AdapterConfigService.ProxmoxSettingKey);
        Assert.DoesNotContain(legacyPlainToken, migratedSetting.ValueJson);
        Assert.Contains(SecretEncryptionService.EnvelopePrefix, migratedSetting.ValueJson);

        var upgradedConfig = JsonSerializer.Deserialize<ProxmoxStoredConfig>(migratedSetting.ValueJson, serializerOptions);
        Assert.NotNull(upgradedConfig);
        Assert.True(_encryptionService.IsEncrypted(upgradedConfig.ApiTokenSecret));
        Assert.Equal(legacyPlainToken, _encryptionService.Decrypt(upgradedConfig.ApiTokenSecret));
    }
}
