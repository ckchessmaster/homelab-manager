namespace ControlPlane.Api.Features.Orchestration.Temporal;

public class TemporalOptions
{
    public const string SectionName = "Temporal";

    /// <summary>
    /// Whether Temporal workflow orchestration is active.
    /// Defaults to true; can be disabled in Standby mode or unit tests.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Temporal server address host and port (e.g. "localhost:7233" or "temporal-frontend.temporal.svc.cluster.local:7233").
    /// Takes precedence over ServerUrl if specified.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Temporal server host and port (e.g. "localhost:7233").
    /// Preserved for backward compatibility.
    /// </summary>
    public string ServerUrl { get; set; } = "localhost:7233";

    /// <summary>
    /// Effective Temporal server endpoint host and port.
    /// </summary>
    public string Endpoint => !string.IsNullOrWhiteSpace(Address) ? Address : ServerUrl;

    /// <summary>
    /// Temporal target namespace.
    /// </summary>
    public string Namespace { get; set; } = "homelab-manager";

    /// <summary>
    /// Default task queue name for host update workflows.
    /// </summary>
    public string TaskQueue { get; set; } = "homelab-manager-tasks";

    /// <summary>
    /// Authentication options for connecting to authenticated Temporal clusters.
    /// </summary>
    public TemporalAuthOptions Auth { get; set; } = new();
}

public class TemporalAuthOptions
{
    /// <summary>
    /// Whether M2M token authentication is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// OAuth2 / OIDC token endpoint URL (e.g. "https://auth.chriskingdon.com/oauth/v2/token").
    /// </summary>
    public string TokenUrl { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 Client ID for M2M credentials grant.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 Client Secret for M2M credentials grant.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 Scopes requested for the access token.
    /// </summary>
    public string Scopes { get; set; } = "openid urn:zitadel:iam:org:projects:roles";
}
