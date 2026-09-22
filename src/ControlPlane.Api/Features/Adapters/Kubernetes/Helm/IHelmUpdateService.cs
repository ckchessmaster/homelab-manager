namespace ControlPlane.Api.Features.Adapters.Kubernetes.Helm;

public interface IHelmUpdateService
{
    Task<HelmChartUpdateInfoDto> CheckChartUpdateAsync(
        string chartName,
        string currentVersion,
        string? currentAppVersion = null,
        string? repoUrl = null,
        bool forceRefresh = false,
        CancellationToken ct = default);

    Task<Dictionary<string, HelmChartUpdateInfoDto>> CheckReleasesAsync(
        IEnumerable<HelmReleaseSummaryDto> releases,
        bool forceRefresh = false,
        CancellationToken ct = default);

    HelmChartUpdateInfoDto? GetCached(string chartName, string currentVersion);

    IReadOnlyDictionary<string, HelmChartUpdateInfoDto> GetAllCached();

    Task<List<string>> GetChartVersionsAsync(string chartName, string? repoUrl = null, CancellationToken ct = default);
}
