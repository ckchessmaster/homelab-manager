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
    /// Explicit SPA Client ID if different from Audience.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Role name mappings for mapping custom IdP roles/groups to ControlPlane canonical roles.
    /// </summary>
    public ZitadelRoleMappingOptions Roles { get; set; } = new();

    /// <summary>
    /// Indicates whether Zitadel JWT authentication is configured and enabled.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Authority);
}

/// <summary>
/// Configurable lists of role names mapped to canonical ControlPlane roles (Admin, Operator, Viewer).
/// </summary>
public class ZitadelRoleMappingOptions
{
    public List<string> Admin { get; set; } = ["admin"];
    public List<string> Operator { get; set; } = ["operator"];
    public List<string> Viewer { get; set; } = ["viewer"];
}
