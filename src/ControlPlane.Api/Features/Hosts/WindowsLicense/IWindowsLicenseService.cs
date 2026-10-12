using ControlPlane.Api.Features.Agents.Models;

namespace ControlPlane.Api.Features.Hosts.WindowsLicense;

public interface IWindowsLicenseService
{
    Task<WindowsLicenseSummaryDto?> GetWindowsLicenseAsync(Guid hostId, bool refresh = false, CancellationToken ct = default);
    Task ProcessAgentWindowsLicenseHeartbeatAsync(Guid hostId, AgentWindowsLicenseSummary license, CancellationToken ct = default);
    Task<RearmWindowsLicenseResponse> RearmWindowsLicenseAsync(Guid hostId, bool rebootAfter = false, string initiatedBy = "Operator", CancellationToken ct = default);
}
