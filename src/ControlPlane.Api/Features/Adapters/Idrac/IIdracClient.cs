using ControlPlane.Api.Features.Adapters.Config;

namespace ControlPlane.Api.Features.Adapters.Idrac;

public interface IIdracClient
{
    Task<IdracTestResultDto> TestConnectionAsync(string bmcUrl, string username, string password, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<IdracVitalsDto> GetVitalsAsync(string bmcUrl, string username, string password, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<IdracPowerControlResponse> ResetSystemAsync(string bmcUrl, string username, string password, string resetType, bool allowSelfSigned = true, CancellationToken ct = default);

    Task<IdracTestResultDto> TestAgentConnectionAsync(Guid hostId, CancellationToken ct = default);
    Task<IdracVitalsDto> GetAgentVitalsAsync(Guid hostId, CancellationToken ct = default);
    Task<IdracPowerControlResponse> ResetAgentSystemAsync(Guid hostId, string resetType, CancellationToken ct = default);

    Task<IdracTestResultDto> TestInstanceAsync(IdracStoredInstance config, string password, CancellationToken ct = default);
    Task<IdracVitalsDto> GetInstanceVitalsAsync(IdracStoredInstance config, string password, CancellationToken ct = default);
    Task<IdracPowerControlResponse> ResetInstanceSystemAsync(IdracStoredInstance config, string password, string resetType, CancellationToken ct = default);

    // --- Fan Control ---
    Task<BmcFanControlResponse> SetFanControlAsync(string bmcUrl, string username, string password, string mode, int? percentage, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<BmcFanControlResponse> SetAgentFanControlAsync(Guid hostId, string mode, int? percentage, CancellationToken ct = default);
    Task<BmcFanControlResponse> SetInstanceFanControlAsync(IdracStoredInstance config, string password, string mode, int? percentage, CancellationToken ct = default);

    // --- Chassis Identify (Locator LED / UID) ---
    Task<BmcChassisIdentifyResponse> SetChassisIdentifyAsync(string bmcUrl, string username, string password, string state, int durationSeconds = 15, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<BmcChassisIdentifyResponse> SetAgentChassisIdentifyAsync(Guid hostId, string state, int durationSeconds = 15, CancellationToken ct = default);
    Task<BmcChassisIdentifyResponse> SetInstanceChassisIdentifyAsync(IdracStoredInstance config, string password, string state, int durationSeconds = 15, CancellationToken ct = default);

    // --- Boot Device Override ---
    Task<BmcBootOverrideResponse> SetBootOverrideAsync(string bmcUrl, string username, string password, string target, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<BmcBootOverrideResponse> SetAgentBootOverrideAsync(Guid hostId, string target, CancellationToken ct = default);
    Task<BmcBootOverrideResponse> SetInstanceBootOverrideAsync(IdracStoredInstance config, string password, string target, CancellationToken ct = default);
}
