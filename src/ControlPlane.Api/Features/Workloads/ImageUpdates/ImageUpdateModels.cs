namespace ControlPlane.Api.Features.Workloads.ImageUpdates;

public record ImageUpdateInfoDto(
    string Image,
    string CurrentTag,
    string? LatestTag,
    bool IsOutdated,
    string? UpdateType = null, // "major" | "minor" | "patch" | "floating"
    string? LatestDigest = null,
    string? Message = null,
    DateTimeOffset? CheckedAt = null,
    List<string>? AvailableTags = null
);

public record ImageCheckRequestDto(
    List<string>? Images = null,
    bool Force = false
);

public record UpdateWorkloadImageRequestDto(
    string Image,
    string? Kind = "Deployment",
    string? ContainerName = null
);
