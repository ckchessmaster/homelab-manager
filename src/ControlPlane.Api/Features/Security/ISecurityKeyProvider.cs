namespace ControlPlane.Api.Features.Security;

/// <summary>
/// Provides access to the 256-bit (32-byte) master encryption key used for application-layer encryption at rest.
/// </summary>
public interface ISecurityKeyProvider
{
    /// <summary>
    /// Retrieves the 32-byte master encryption key.
    /// Throws CryptographicException if the key cannot be resolved or is invalid.
    /// </summary>
    byte[] GetMasterKey();

    /// <summary>
    /// The origin from which the key was resolved: "Environment", "Configuration", "File", or "Generated".
    /// </summary>
    string KeySource { get; }

    /// <summary>
    /// File path where the key is stored, if file-backed.
    /// </summary>
    string? KeyFilePath { get; }
}
