namespace ControlPlane.Api.Features.Security;

/// <summary>
/// Configuration options for application-layer secret encryption and master key management.
/// </summary>
public class SecurityOptions
{
    public const string SectionName = "ControlPlane:Security";

    /// <summary>
    /// Base64-encoded 256-bit (32-byte) master encryption key.
    /// Can also be provided via the CONTROLPLANE_MASTER_KEY environment variable.
    /// </summary>
    public string? MasterKey { get; set; }

    /// <summary>
    /// Custom path to the master key file. Defaults to ~/.controlplane/master.key if not specified.
    /// </summary>
    public string? KeyFilePath { get; set; }

    /// <summary>
    /// If true, automatically generates and persists a random 256-bit key if no key is configured or found.
    /// Default is true for zero-friction local development and standby CLI deployments.
    /// </summary>
    public bool AutoGenerateKeyIfMissing { get; set; } = true;
}
