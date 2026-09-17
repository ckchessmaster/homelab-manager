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

public record ProxmoxInstanceDto(
    string Id,
    string Name,
    string BaseUrl,
    string ApiTokenId,
    string ApiTokenSecretMasked,
    bool HasSecret,
    bool AllowSelfSignedCert,
    int TaskPollTimeoutSeconds,
    int TaskPollIntervalMilliseconds,
    DateTimeOffset? UpdatedAt
);

public record SaveProxmoxInstanceRequest(
    string? Id,
    string Name,
    string BaseUrl,
    string ApiTokenId,
    string? ApiTokenSecret,
    bool AllowSelfSignedCert = true,
    int? TaskPollTimeoutSeconds = null,
    int? TaskPollIntervalMilliseconds = null
);

public class ProxmoxStoredInstance
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiTokenId { get; set; } = string.Empty;
    public string ApiTokenSecret { get; set; } = string.Empty;
    public bool AllowSelfSignedCert { get; set; } = true;
    public int TaskPollTimeoutSeconds { get; set; } = 300;
    public int TaskPollIntervalMilliseconds { get; set; } = 1000;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public record KubernetesClusterDto(
    string Id,
    string Name,
    string? ApiServerUrl,
    string? ContextName,
    bool HasKubeConfig,
    bool HasToken,
    bool SkipTlsVerify,
    DateTimeOffset? UpdatedAt
);

public record SaveKubernetesClusterRequest(
    string? Id,
    string Name,
    string? ApiServerUrl,
    string? KubeConfigRaw,
    string? Token,
    string? ContextName,
    bool SkipTlsVerify = true
);

public class KubernetesStoredCluster
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ApiServerUrl { get; set; }
    public string? EncryptedKubeConfig { get; set; }
    public string? EncryptedToken { get; set; }
    public string? ContextName { get; set; }
    public bool SkipTlsVerify { get; set; } = true;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public record KubernetesClusterTestResultDto(
    bool Success,
    string? ServerVersion,
    int NodeCount,
    long LatencyMs,
    string? Message
);

public record UniFiInstanceDto(
    string Id,
    string Name,
    string ControllerUrl,
    string Username,
    string PasswordMasked,
    bool HasPassword,
    string Site,
    bool AllowSelfSignedCert,
    DateTimeOffset? UpdatedAt,
    string AuthType = "api_key",
    string? ApiKeyMasked = null,
    bool HasApiKey = false
);

public record SaveUniFiInstanceRequest(
    string? Id,
    string Name,
    string ControllerUrl,
    string? Username = null,
    string? Password = null,
    string? ApiKey = null,
    string AuthType = "api_key",
    string Site = "default",
    bool AllowSelfSignedCert = true
);

public class UniFiStoredInstance
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ControllerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string EncryptedPassword { get; set; } = string.Empty;
    public string? EncryptedApiKey { get; set; }
    public string AuthType { get; set; } = "api_key";
    public string Site { get; set; } = "default";
    public bool AllowSelfSignedCert { get; set; } = true;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public record UniFiTestResultDto(
    bool Success,
    string? ControllerVersion,
    int? DeviceCount,
    int? ClientCount,
    List<string>? Sites,
    long LatencyMs,
    string? Message
);

public record OPNsenseInstanceDto(
    string Id,
    string Name,
    string BaseUrl,
    string ApiKey,
    string ApiSecretMasked,
    bool HasSecret,
    bool AllowSelfSignedCert,
    DateTimeOffset? UpdatedAt
);

public record SaveOPNsenseInstanceRequest(
    string? Id,
    string Name,
    string BaseUrl,
    string ApiKey,
    string? ApiSecret,
    bool AllowSelfSignedCert = true
);

public class OPNsenseStoredInstance
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string EncryptedApiSecret { get; set; } = string.Empty;
    public bool AllowSelfSignedCert { get; set; } = true;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public record OPNsenseTestResultDto(
    bool Success,
    string? Hostname,
    string? Version,
    string? Status,
    long LatencyMs,
    string? Message
);

public record IdracInstanceDto(
    string Id,
    string Name,
    string BmcUrl,
    string Username,
    string PasswordMasked,
    bool HasPassword,
    string? HostnameOrIp,
    bool AllowSelfSignedCert,
    DateTimeOffset? UpdatedAt,
    string ConnectionMode = "network",
    Guid? HostId = null,
    string? HostName = null
);

public record SaveIdracInstanceRequest(
    string? Id,
    string Name,
    string? BmcUrl = null,
    string? Username = null,
    string? Password = null,
    string? HostnameOrIp = null,
    bool AllowSelfSignedCert = true,
    string ConnectionMode = "network",
    Guid? HostId = null
);

public class IdracStoredInstance
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BmcUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string EncryptedPassword { get; set; } = string.Empty;
    public string? HostnameOrIp { get; set; }
    public bool AllowSelfSignedCert { get; set; } = true;
    public string ConnectionMode { get; set; } = "network";
    public Guid? HostId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public record IdracTestResultDto(
    bool Success,
    string? PowerState,
    string? Model,
    string? BiosVersion,
    string? HealthStatus,
    string? SerialNumber,
    long LatencyMs,
    string? Message
);

public record HomeAssistantInstanceDto(
    string Id,
    string Name,
    string BaseUrl,
    string TokenMasked,
    bool HasToken,
    bool AllowSelfSignedCert,
    DateTimeOffset? UpdatedAt
);

public record SaveHomeAssistantInstanceRequest(
    string? Id,
    string Name,
    string BaseUrl,
    string? Token,
    bool AllowSelfSignedCert = true
);

public class HomeAssistantStoredInstance
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string EncryptedToken { get; set; } = string.Empty;
    public bool AllowSelfSignedCert { get; set; } = true;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public record HomeAssistantTestResultDto(
    bool Success,
    string? CoreVersion,
    string? OsVersion,
    string? SupervisorVersion,
    string? Hostname,
    bool? UpdateAvailable,
    long LatencyMs,
    string? Message
);

