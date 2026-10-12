namespace ControlPlane.Api.Features.Hosts.WindowsLicense;

public record RearmWindowsLicenseRequest(bool RebootAfter = false);

public record RearmWindowsLicenseResponse(
    bool Success,
    Guid JobId,
    string? WorkflowId,
    string Status,
    string Message
);
