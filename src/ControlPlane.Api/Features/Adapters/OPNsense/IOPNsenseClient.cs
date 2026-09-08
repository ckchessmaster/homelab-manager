using ControlPlane.Api.Features.Adapters.Config;

namespace ControlPlane.Api.Features.Adapters.OPNsense;

public interface IOPNsenseClient
{
    Task<OPNsenseTestResultDto> TestConnectionAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<OPNsenseTelemetryResponse> GetTelemetryAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<List<OPNsenseGatewayStatus>> GetGatewaysAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<List<OPNsenseInterfaceInfo>> GetInterfacesAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<List<OPNsenseServiceItem>> GetServicesAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<OPNsenseServiceActionResult> RestartServiceAsync(string baseUrl, string apiKey, string apiSecret, string serviceName, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<List<OPNsenseDhcpLease>> GetDhcpLeasesAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default);
    Task<OPNsenseFirmwareInfo> GetFirmwareStatusAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default);
}
