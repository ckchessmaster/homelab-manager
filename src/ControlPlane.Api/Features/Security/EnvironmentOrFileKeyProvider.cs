using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Security;

/// <summary>
/// Resolves the 256-bit master encryption key from environment variable, configuration,
/// local key file, or secure auto-generation.
/// </summary>
public class EnvironmentOrFileKeyProvider : ISecurityKeyProvider
{
    public const string EnvironmentVariableName = "CONTROLPLANE_MASTER_KEY";
    public const int RequiredKeyLengthBytes = 32; // 256-bit key

    private readonly SecurityOptions _options;
    private readonly ILogger<EnvironmentOrFileKeyProvider> _logger;
    private readonly byte[] _masterKey;
    private readonly string _keySource;
    private readonly string? _keyFilePath;

    public EnvironmentOrFileKeyProvider(
        IOptions<SecurityOptions> options,
        ILogger<EnvironmentOrFileKeyProvider> logger)
    {
        _options = options.Value;
        _logger = logger;

        (_masterKey, _keySource, _keyFilePath) = ResolveKey();
    }

    public byte[] GetMasterKey() => (byte[])_masterKey.Clone();

    public string KeySource => _keySource;

    public string? KeyFilePath => _keyFilePath;

    private (byte[] Key, string Source, string? Path) ResolveKey()
    {
        // 1. Check environment variable
        var envKey = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            var keyBytes = ParseAndValidateKey(envKey.Trim(), "Environment Variable (" + EnvironmentVariableName + ")");
            _logger.LogInformation("Master encryption key loaded from environment variable {EnvVar}.", EnvironmentVariableName);
            return (keyBytes, "Environment", null);
        }

        // 2. Check configuration option
        if (!string.IsNullOrWhiteSpace(_options.MasterKey))
        {
            var keyBytes = ParseAndValidateKey(_options.MasterKey.Trim(), "Configuration (SecurityOptions:MasterKey)");
            _logger.LogInformation("Master encryption key loaded from application configuration.");
            return (keyBytes, "Configuration", null);
        }

        // 3. Resolve key file path
        var keyPath = ResolveKeyFilePath(_options.KeyFilePath);

        if (File.Exists(keyPath))
        {
            try
            {
                var content = File.ReadAllText(keyPath).Trim();
                var keyBytes = ParseAndValidateKey(content, $"Key File ({keyPath})");
                _logger.LogInformation("Master encryption key loaded from persistent key file {KeyPath}.", keyPath);
                return (keyBytes, "File", keyPath);
            }
            catch (Exception ex) when (ex is not CryptographicException)
            {
                throw new CryptographicException($"Failed to read master key from file '{keyPath}'.", ex);
            }
        }

        // 4. Auto-generate if permitted
        if (_options.AutoGenerateKeyIfMissing)
        {
            var generatedKey = RandomNumberGenerator.GetBytes(RequiredKeyLengthBytes);
            var base64Key = Convert.ToBase64String(generatedKey);

            try
            {
                var directory = Path.GetDirectoryName(keyPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(keyPath, base64Key);

                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not set 0600 POSIX file mode on generated key file {KeyPath}.", keyPath);
                    }
                }

                _logger.LogWarning("Generated new 256-bit master encryption key and saved to {KeyPath} with restricted permissions (0600).", keyPath);
                return (generatedKey, "Generated", keyPath);
            }
            catch (Exception ex)
            {
                throw new CryptographicException($"Failed to write auto-generated master key to '{keyPath}'.", ex);
            }
        }

        throw new CryptographicException(
            $"No master encryption key was found (Environment: {EnvironmentVariableName}, File: {keyPath}), " +
            "and auto-generation is disabled (AutoGenerateKeyIfMissing=false).");
    }

    private static byte[] ParseAndValidateKey(string rawValue, string sourceDescription)
    {
        try
        {
            var bytes = Convert.FromBase64String(rawValue);
            if (bytes.Length != RequiredKeyLengthBytes)
            {
                throw new CryptographicException(
                    $"Master encryption key from {sourceDescription} is invalid: expected {RequiredKeyLengthBytes} bytes (256-bit), but got {bytes.Length} bytes.");
            }
            return bytes;
        }
        catch (FormatException ex)
        {
            throw new CryptographicException(
                $"Master encryption key from {sourceDescription} is not a valid Base64 string.", ex);
        }
    }

    public static string ResolveKeyFilePath(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            if (customPath.StartsWith("~/", StringComparison.Ordinal) || customPath.StartsWith("~\\", StringComparison.Ordinal))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, customPath[2..]);
            }
            return Path.GetFullPath(customPath);
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".controlplane", "master.key");
    }
}
