using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.HomeAssistant;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockHomeAssistantClient : IHomeAssistantClient
{
    public Task<HomeAssistantTestResultDto> TestConnectionAsync(string baseUrl, string token, bool allowSelfSignedCert, CancellationToken ct = default)
    {
        return Task.FromResult(new HomeAssistantTestResultDto(true, "2026.3.2", "12.1", "2026.03.0", "homeassistant", false, 18, null));
    }

    public Task<HomeAssistantHostInfoDto?> GetHostInfoAsync(string baseUrl, string token, bool allowSelfSignedCert, CancellationToken ct = default)
    {
        return Task.FromResult<HomeAssistantHostInfoDto?>(new HomeAssistantHostInfoDto(
            Chassis: "qemu",
            Hostname: "homeassistant",
            Kernel: "6.6.21-haos",
            OperatingSystem: "Home Assistant OS 12.1",
            RebootRequired: false,
            DiskFreeGb: 28.5,
            DiskTotalGb: 32.0,
            DiskUsedGb: 3.5
        ));
    }

    public Task<HomeAssistantOsInfoDto?> GetOsInfoAsync(string baseUrl, string token, bool allowSelfSignedCert, CancellationToken ct = default)
    {
        return Task.FromResult<HomeAssistantOsInfoDto?>(new HomeAssistantOsInfoDto(
            Version: "12.1",
            VersionLatest: "12.1",
            UpdateAvailable: false,
            Board: "ova",
            BootSlot: "A"
        ));
    }

    public Task<HomeAssistantCoreInfoDto?> GetCoreInfoAsync(string baseUrl, string token, bool allowSelfSignedCert, CancellationToken ct = default)
    {
        return Task.FromResult<HomeAssistantCoreInfoDto?>(new HomeAssistantCoreInfoDto(
            Version: "2026.3.2",
            VersionLatest: "2026.3.2",
            UpdateAvailable: false,
            Arch: "x86_64",
            State: "RUNNING"
        ));
    }

    public Task<HomeAssistantSupervisorInfoDto?> GetSupervisorInfoAsync(string baseUrl, string token, bool allowSelfSignedCert, CancellationToken ct = default)
    {
        return Task.FromResult<HomeAssistantSupervisorInfoDto?>(new HomeAssistantSupervisorInfoDto(
            Version: "2026.03.0",
            VersionLatest: "2026.03.0",
            UpdateAvailable: false,
            Channel: "stable",
            Healthy: true,
            Supported: true
        ));
    }

    public Task<List<HomeAssistantBackupDto>> ListBackupsAsync(string baseUrl, string token, bool allowSelfSignedCert, CancellationToken ct = default)
    {
        return Task.FromResult(new List<HomeAssistantBackupDto>
        {
            new("core_2026_03_28", "Core Weekly Backup", DateTimeOffset.UtcNow.AddDays(-1), "full", 450.5, false),
            new("core_2026_03_21", "Core Weekly Backup", DateTimeOffset.UtcNow.AddDays(-8), "full", 442.1, false)
        });
    }

    public Task<HomeAssistantConfigCheckResultDto> CheckCoreConfigAsync(string baseUrl, string token, bool allowSelfSignedCert, CancellationToken ct = default)
    {
        return Task.FromResult(new HomeAssistantConfigCheckResultDto(true, null, DateTimeOffset.UtcNow));
    }

    public Task<bool> RebootHostAsync(string baseUrl, string token, bool allowSelfSignedCert, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> RestartCoreAsync(string baseUrl, string token, bool allowSelfSignedCert, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> UpdateOsAsync(string baseUrl, string token, bool allowSelfSignedCert, CancellationToken ct = default) => Task.FromResult(true);

    public Task<CreateHomeAssistantBackupResponse> CreateBackupAsync(string baseUrl, string token, string? name, string? password, bool allowSelfSignedCert, CancellationToken ct = default)
    {
        return Task.FromResult(new CreateHomeAssistantBackupResponse(true, "demo-job-123", "backup_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "Backup created successfully (Demo Mode)"));
    }
}
