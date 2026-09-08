using ControlPlane.Api.Features.Adapters.Config;

namespace ControlPlane.Api.Features.Adapters.UniFi;

public interface IUniFiClient
{
    Task<bool> LoginAsync(string controllerUrl, string username, string password, CancellationToken ct = default);
    Task<UniFiTestResultDto> TestConnectionAsync(string controllerUrl, string? username, string? password, string site = "default", string? apiKey = null, CancellationToken ct = default);
    Task<List<UniFiDeviceDto>> GetDevicesAsync(string controllerUrl, string? username, string? password, string site = "default", string? apiKey = null, CancellationToken ct = default);
    Task<bool> RestartDeviceAsync(string controllerUrl, string? username, string? password, string deviceMac, string site = "default", string? apiKey = null, CancellationToken ct = default);
    Task<bool> UpgradeDeviceAsync(string controllerUrl, string? username, string? password, string deviceMac, string site = "default", string? apiKey = null, CancellationToken ct = default);
    Task<UniFiBounceResult> CyclePoEPortAsync(string controllerUrl, string? username, string? password, string switchMac, int portNumber, string site = "default", int delaySeconds = 5, string? apiKey = null, CancellationToken ct = default);
    Task<List<UniFiMacLease>> GetActiveClientsAsync(string controllerUrl, string? username, string? password, string site = "default", string? apiKey = null, CancellationToken ct = default);
}
