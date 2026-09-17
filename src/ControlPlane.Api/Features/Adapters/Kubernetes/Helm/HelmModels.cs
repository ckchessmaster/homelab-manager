namespace ControlPlane.Api.Features.Adapters.Kubernetes.Helm;

public record HelmReleaseSummaryDto(
    string Name,
    string Namespace,
    int Revision,
    DateTimeOffset Updated,
    string Status,
    string Chart,
    string ChartName,
    string ChartVersion,
    string AppVersion,
    string? Description = null,
    string? Notes = null
);

public record HelmReleaseDetailDto(
    string Name,
    string Namespace,
    int Revision,
    DateTimeOffset Updated,
    string Status,
    string Chart,
    string ChartName,
    string ChartVersion,
    string AppVersion,
    string? Description = null,
    string? Notes = null,
    string? ValuesYaml = null,
    string? Manifest = null,
    string? RepoUrl = null
);

public record HelmReleaseRevisionDto(
    int Revision,
    DateTimeOffset Updated,
    string Status,
    string Chart,
    string AppVersion,
    string? Description = null
);

public record InstallHelmReleaseRequestDto(
    string ReleaseName,
    string Namespace,
    string ChartName,
    string? RepoUrl = null,
    string? Version = null,
    string? ValuesYaml = null,
    bool CreateNamespace = true,
    bool Wait = false,
    int TimeoutSeconds = 300
);

public record UpgradeHelmReleaseRequestDto(
    string? ChartName = null,
    string? RepoUrl = null,
    string? Version = null,
    string? ValuesYaml = null,
    bool ReuseValues = false,
    bool ResetValues = false,
    bool Wait = false,
    int TimeoutSeconds = 300
);

public record RollbackHelmReleaseRequestDto(
    int Revision,
    bool CleanupOnFail = false,
    bool Wait = false,
    int TimeoutSeconds = 300
);

public record HelmOperationResultDto(
    bool Success,
    string Message,
    string? ReleaseName = null,
    int? Revision = null,
    string? Output = null
);

public record HelmCatalogItemDto(
    string Id,
    string Name,
    string Category,
    string Description,
    string RepoUrl,
    string ChartName,
    string DefaultNamespace,
    string DefaultValuesYaml,
    string Icon,
    string? OfficialUrl = null
);
