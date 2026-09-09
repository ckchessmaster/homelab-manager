namespace ControlPlane.Api.Features.Hosts;

public interface IHostCorrelationService
{
    Task<HostRebootImpactDto> GetRebootImpactAsync(Guid hostId, CancellationToken ct = default);

    Task<HostCorrelationDto> GetHostCorrelationAsync(Guid hostId, CancellationToken ct = default);

    Task<SyncCorrelationResultDto> SyncHostCorrelationsAsync(CancellationToken ct = default);
}
