using ControlPlane.Api.Features.Adapters.Config;

namespace ControlPlane.Api.Features.Adapters.Idrac;

public interface IIdracClient
{
    Task<IdracTestResultDto> TestConnectionAsync(string bmcUrl, string username, string password, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<IdracVitalsDto> GetVitalsAsync(string bmcUrl, string username, string password, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<IdracPowerControlResponse> ResetSystemAsync(string bmcUrl, string username, string password, string resetType, bool allowSelfSigned = true, CancellationToken ct = default);
}
