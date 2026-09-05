namespace ControlPlane.Api.Features.Adapters.Config;

public record ProxmoxConfigDto(
    string BaseUrl,
    string ApiTokenId,
    string ApiTokenSecretMasked,
    bool HasSecret,
    bool AllowSelfSignedCert,
    int TaskPollTimeoutSeconds,
    int TaskPollIntervalMilliseconds,
    DateTimeOffset? UpdatedAt
);

public record SaveProxmoxConfigRequest(
    string BaseUrl,
    string ApiTokenId,
    string? ApiTokenSecret,
    bool AllowSelfSignedCert = true,
    int? TaskPollTimeoutSeconds = null,
    int? TaskPollIntervalMilliseconds = null
);

public class ProxmoxStoredConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiTokenId { get; set; } = string.Empty;
    public string ApiTokenSecret { get; set; } = string.Empty;
    public bool AllowSelfSignedCert { get; set; } = true;
    public int TaskPollTimeoutSeconds { get; set; } = 300;
    public int TaskPollIntervalMilliseconds { get; set; } = 1000;
}
