namespace ControlPlane.Api.Features.Adoption;

public record AdoptNodeRequest(
    Guid? HostId,
    string? Hostname,
    string TargetHost,
    int Port = 22,
    string Username = "root",
    string? Password = null,
    string? PrivateKey = null,
    string? HubUrl = null
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
