namespace ControlPlane.Api.Features.Security;

/// <summary>
/// Service providing authenticated envelope encryption (AES-256-GCM) for sensitive credentials stored at rest.
/// </summary>
public interface ISecretEncryptionService
{
    /// <summary>
    /// Encrypts plaintext using AES-256-GCM authenticated envelope encryption.
    /// Format: enc:v1:&lt;base64-iv&gt;:&lt;base64-ciphertext&gt;:&lt;base64-tag&gt;
    /// </summary>
    string Encrypt(string? plainText);

    /// <summary>
    /// Decrypts ciphertext envelope. If the input is not prefixed with enc:v1:,
    /// it is treated as legacy plaintext and returned as-is for backwards compatibility.
    /// Throws CryptographicException if the ciphertext or authentication tag has been tampered with.
    /// </summary>
    string Decrypt(string? cipherTextOrPlain);

    /// <summary>
    /// Returns true if the string is formatted in the encrypted envelope format (enc:v1:...).
    /// </summary>
    bool IsEncrypted(string? value);
}
