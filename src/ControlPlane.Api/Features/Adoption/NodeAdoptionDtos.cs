namespace ControlPlane.Api.Features.Adoption;

public record AdoptNodeRequest(
    Guid? HostId,
    string? Hostname,
    string TargetHost,
    int Port = 22,
    string Username = "root",
    string? Password = null,
    string? PrivateKey = null,
    string? HubUrl = null,
    bool Insecure = false
);

public enum AdoptionStepStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

public record AdoptionStepEvent(
    string StepKey,
    string StepTitle,
    AdoptionStepStatus Status,
    string? Message = null,
    DateTimeOffset Timestamp = default
);

public record NodeAdoptionResponse(
    Guid HostId,
    bool Success,
    string Message,
    List<AdoptionStepEvent> Steps
);

public record BatchAdoptHostItem(
    Guid HostId,
    string TargetHost,
    string? Hostname = null
);

public record BatchAdoptNodesRequest(
    List<BatchAdoptHostItem> Hosts,
    int Port = 22,
    string Username = "root",
    string? Password = null,
    string? PrivateKey = null,
    string? HubUrl = null,
    bool Insecure = false
);

public record BatchAdoptItemResult(
    Guid HostId,
    string Hostname,
    bool Success,
    string Message
);

public record BatchAdoptNodesResponse(
    int TotalRequested,
    int SucceededCount,
    int FailedCount,
    List<BatchAdoptItemResult> Results
);

