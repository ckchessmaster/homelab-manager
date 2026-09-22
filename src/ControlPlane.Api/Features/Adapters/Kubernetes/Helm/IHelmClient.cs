namespace ControlPlane.Api.Features.Adapters.Kubernetes.Helm;

public interface IHelmClient
{
    Task<List<HelmReleaseSummaryDto>> ListReleasesAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string? namespaceName = null,
        CancellationToken ct = default);

    Task<HelmReleaseDetailDto?> GetReleaseDetailAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        CancellationToken ct = default);

    Task<HelmReleaseDetailDto?> GetReleaseDetailAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        int? revision,
        CancellationToken ct = default)
        => GetReleaseDetailAsync(kubeconfigYaml, apiServerUrl, token, skipTlsVerify, namespaceName, releaseName, ct);

    Task<List<HelmReleaseRevisionDto>> GetReleaseHistoryAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        CancellationToken ct = default);

    Task<HelmOperationResultDto> InstallOrUpgradeReleaseAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        InstallHelmReleaseRequestDto request,
        CancellationToken ct = default);

    Task<HelmOperationResultDto> RollbackReleaseAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        int revision,
        CancellationToken ct = default);

    Task<HelmOperationResultDto> UninstallReleaseAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        CancellationToken ct = default);
}
