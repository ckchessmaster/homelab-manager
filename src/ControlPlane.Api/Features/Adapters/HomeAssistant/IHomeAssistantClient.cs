using ControlPlane.Api.Features.Adapters.Config;

namespace ControlPlane.Api.Features.Adapters.HomeAssistant;

public interface IHomeAssistantClient
{
    Task<HomeAssistantTestResultDto> TestConnectionAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default);

    Task<HomeAssistantHostInfoDto?> GetHostInfoAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default);

    Task<HomeAssistantOsInfoDto?> GetOsInfoAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default);

    Task<HomeAssistantCoreInfoDto?> GetCoreInfoAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default);

    Task<HomeAssistantSupervisorInfoDto?> GetSupervisorInfoAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default);

    Task<List<HomeAssistantBackupDto>> ListBackupsAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default);

    Task<HomeAssistantConfigCheckResultDto> CheckCoreConfigAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default);

    Task<bool> RebootHostAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default);

    Task<bool> RestartCoreAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default);

    Task<bool> UpdateOsAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default);

    Task<CreateHomeAssistantBackupResponse> CreateBackupAsync(
        string baseUrl,
        string token,
        string? name,
        string? password,
        bool allowSelfSignedCert,
        CancellationToken ct = default);
}
