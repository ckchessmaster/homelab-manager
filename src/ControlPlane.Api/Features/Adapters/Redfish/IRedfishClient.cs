namespace ControlPlane.Api.Features.Adapters.Redfish;

public interface IRedfishClient
{
    Task<RedfishSystemInfo> GetSystemInfoAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default);
    Task<RedfishThermalVitals> GetThermalVitalsAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default);
    Task<RedfishResetResponse> ResetSystemAsync(string hostOrIp, string username, string password, string resetType, bool insecureTls = true, CancellationToken ct = default);
    Task<List<RedfishStorageControllerInfo>> GetStorageControllersAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default);
    Task<List<RedfishDriveInfo>> GetDrivesAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default);
    Task<RedfishPowerVitals> GetPowerVitalsAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default);
    Task<List<RedfishMemoryInfo>> GetMemoryModulesAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default);
}
