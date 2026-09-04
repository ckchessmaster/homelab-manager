namespace ControlPlane.Api.Features.Adapters.UniFi;

public interface IUniFiClient
{
    Task<bool> LoginAsync(string controllerUrl, string username, string password, CancellationToken ct = default);
    Task<UniFiBounceResult> CyclePoEPortAsync(string controllerUrl, string username, string password, string switchMac, int portNumber, string site = "default", int delaySeconds = 5, CancellationToken ct = default);
    Task<List<UniFiMacLease>> GetActiveClientsAsync(string controllerUrl, string username, string password, string site = "default", CancellationToken ct = default);
}
