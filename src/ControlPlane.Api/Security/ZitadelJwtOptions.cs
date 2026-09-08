namespace ControlPlane.Api.Security;

/// <summary>
/// Configuration options for validating OpenID Connect JWT tokens issued by Zitadel.
/// </summary>
public class ZitadelJwtOptions
{
    public const string SectionName = "Zitadel";

    /// <summary>
    /// The base URL / authority of the Zitadel instance (e.g. "http://localhost:8085" or "https://auth.homelab.local").
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// The intended audience for the token (usually the project ID or client ID).
    /// </summary>
    public string Audience { get; set; } = "controlplane";

    /// <summary>
    /// Explicit valid issuer if different from Authority.
    /// </summary>
    public string? ValidIssuer { get; set; }

    /// <summary>
    /// Explicit valid audience list if multiple audiences are accepted.
    /// </summary>
    public List<string>? ValidAudiences { get; set; }

    /// <summary>
    /// Whether HTTPS is required for OIDC metadata discovery. Defaults to true in production, can be disabled for local/Aspire dev.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Explicit OIDC discovery endpoint if custom path is needed.
    /// </summary>
    public string? MetadataAddress { get; set; }

    /// <summary>
    /// Indicates whether Zitadel JWT authentication is configured and enabled.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Authority);
}
