namespace ControlPlane.Api.Features.Adapters.Redfish;

public interface IRedfishClient
{
    Task<RedfishSystemInfo> GetSystemInfoAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default);
    Task<RedfishThermalVitals> GetThermalVitalsAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default);
    Task<RedfishResetResponse> ResetSystemAsync(string hostOrIp, string username, string password, string resetType, bool insecureTls = true, CancellationToken ct = default);
}
