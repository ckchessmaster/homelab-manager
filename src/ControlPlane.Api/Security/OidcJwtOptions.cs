namespace ControlPlane.Api.Security;

/// <summary>
/// Configuration options for validating OpenID Connect JWT tokens issued by generic OIDC providers (e.g., Authentik, Dex, Keycloak).
/// </summary>
public class OidcJwtOptions
{
    public const string SectionName = "Oidc";

    private string? _authority;
    private string? _audience;

    /// <summary>
    /// The base Issuer URL / Authority of the OIDC identity provider (e.g. "https://auth.homelab.local/application/o/controlplane/").
    /// In Authentik, this is the Issuer URL shown under Provider -> OpenID Configuration.
    /// </summary>
    public string? Authority
    {
        get => _authority;
        set => _authority = value;
    }

    /// <summary>
    /// Direct alias for Authority (commonly referred to as Issuer or IssuerUrl in app configurations).
    /// </summary>
    public string? IssuerUrl
    {
        get => _authority;
        set => _authority = value;
    }

    /// <summary>
    /// The intended audience for the token.
    /// In Authentik, this defaults to the ClientId unless customized under Advanced Settings -> Audience.
    /// </summary>
    public string Audience
    {
        get => _audience ?? ClientId ?? "controlplane";
        set => _audience = value;
    }

    /// <summary>
    /// User-friendly display name of the identity provider for UI buttons (e.g. "Authentik", "Single Sign-On").
    /// </summary>
    public string DisplayName { get; set; } = "Single Sign-On";

    /// <summary>
    /// Explicit valid issuer if different from Authority.
    /// </summary>
    public string? ValidIssuer { get; set; }

    /// <summary>
    /// Explicit valid audience list if multiple audiences are accepted.
    /// </summary>
    public List<string>? ValidAudiences { get; set; }

    /// <summary>
    /// Whether HTTPS is required for OIDC metadata discovery. Defaults to true in production.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Explicit OIDC discovery endpoint if custom path is needed.
    /// </summary>
    public string? MetadataAddress { get; set; }

    /// <summary>
    /// Explicit OIDC Client ID.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// The claim name containing user group memberships or roles (defaults to "groups" for Authentik / standard OIDC).
    /// </summary>
    public string RoleClaimType { get; set; } = AuthConstants.DefaultRoleClaimType;

    /// <summary>
    /// The claim name containing the username / user identity (defaults to "preferred_username").
    /// </summary>
    public string NameClaimType { get; set; } = AuthConstants.DefaultNameClaimType;

    /// <summary>
    /// OIDC scopes requested by the frontend and client credentials (defaults to "openid profile email groups").
    /// </summary>
    public string Scope { get; set; } = "openid profile email groups";

    /// <summary>
    /// Role name mappings for mapping IdP groups/roles to ControlPlane canonical roles.
    /// </summary>
    public OidcRoleMappingOptions Roles { get; set; } = new();

    /// <summary>
    /// Indicates whether OIDC JWT authentication is configured and enabled.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Authority);
}

/// <summary>
/// Configurable lists of role/group names mapped to canonical ControlPlane roles (Admin, Operator, Viewer).
/// </summary>
public class OidcRoleMappingOptions
{
    public List<string> Admin { get; set; } = ["admin", "authentik Admins"];
    public List<string> Operator { get; set; } = ["operator"];
    public List<string> Viewer { get; set; } = ["viewer"];
}
