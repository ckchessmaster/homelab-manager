using System.Security.Cryptography;
using System.Text;

namespace ControlPlane.Api.Features.Security;

/// <summary>
/// Authenticated envelope encryption service implementing AES-256-GCM.
/// Formats ciphertext as enc:v1:&lt;base64-iv&gt;:&lt;base64-ciphertext&gt;:&lt;base64-tag&gt;.
/// </summary>
public class SecretEncryptionService : ISecretEncryptionService
{
    public const string EnvelopePrefix = "enc:v1:";
    public const int NonceByteSize = 12; // 96-bit IV
    public const int TagByteSize = 16;   // 128-bit authentication tag

    private readonly ISecurityKeyProvider _keyProvider;

    public SecretEncryptionService(ISecurityKeyProvider keyProvider)
    {
        _keyProvider = keyProvider;
    }

    public string Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return plainText ?? string.Empty;
        }

        var key = _keyProvider.GetMasterKey();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceByteSize);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagByteSize];

        using (var aes = new AesGcm(key, TagByteSize))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        var ivB64 = Convert.ToBase64String(nonce);
        var cipherB64 = Convert.ToBase64String(cipherBytes);
        var tagB64 = Convert.ToBase64String(tag);

        return $"{EnvelopePrefix}{ivB64}:{cipherB64}:{tagB64}";
    }

    public string Decrypt(string? cipherTextOrPlain)
    {
        if (string.IsNullOrEmpty(cipherTextOrPlain))
        {
            return cipherTextOrPlain ?? string.Empty;
        }

        if (!IsEncrypted(cipherTextOrPlain))
        {
            // Legacy plaintext fallback
            return cipherTextOrPlain;
        }

        var body = cipherTextOrPlain[EnvelopePrefix.Length..];
        var parts = body.Split(':');
        if (parts.Length != 3)
        {
            throw new CryptographicException("Invalid encrypted envelope format. Expected 3 components: iv, ciphertext, tag.");
        }

        byte[] nonce;
        byte[] cipherBytes;
        byte[] tag;

        try
        {
            nonce = Convert.FromBase64String(parts[0]);
            cipherBytes = Convert.FromBase64String(parts[1]);
            tag = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Encrypted envelope contains invalid Base64 data.", ex);
        }

        if (nonce.Length != NonceByteSize)
        {
            throw new CryptographicException($"Invalid IV length: expected {NonceByteSize} bytes, got {nonce.Length}.");
        }

        if (tag.Length != TagByteSize)
        {
            throw new CryptographicException($"Invalid authentication tag length: expected {TagByteSize} bytes, got {tag.Length}.");
        }

        var key = _keyProvider.GetMasterKey();
        var plainBytes = new byte[cipherBytes.Length];

        using (var aes = new AesGcm(key, TagByteSize))
        {
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    public bool IsEncrypted(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.StartsWith(EnvelopePrefix, StringComparison.Ordinal);
    }
}
