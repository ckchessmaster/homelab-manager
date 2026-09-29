using ControlPlane.Api.Features.Adapters.Kubernetes.Helm;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockHelmClient : IHelmClient
{
    private readonly List<HelmReleaseSummaryDto> _releases = new()
    {
        new(
            Name: "ingress-nginx",
            Namespace: "ingress-nginx",
            Revision: 2,
            Updated: DateTimeOffset.UtcNow.AddDays(-14),
            Status: "deployed",
            Chart: "ingress-nginx-4.10.0",
            ChartName: "ingress-nginx",
            ChartVersion: "4.10.0",
            AppVersion: "1.10.0",
            Description: "Install complete",
            Notes: "The ingress-nginx controller has been installed."
        ),
        new(
            Name: "kube-prometheus-stack",
            Namespace: "monitoring",
            Revision: 1,
            Updated: DateTimeOffset.UtcNow.AddDays(-30),
            Status: "deployed",
            Chart: "kube-prometheus-stack-58.2.0",
            ChartName: "kube-prometheus-stack",
            ChartVersion: "58.2.0",
            AppVersion: "v0.73.1",
            Description: "Install complete",
            Notes: "Prometheus stack is healthy."
        ),
        new(
            Name: "cert-manager",
            Namespace: "cert-manager",
            Revision: 1,
            Updated: DateTimeOffset.UtcNow.AddDays(-20),
            Status: "deployed",
            Chart: "cert-manager-v1.14.4",
            ChartName: "cert-manager",
            ChartVersion: "v1.14.4",
            AppVersion: "v1.14.4",
            Description: "Install complete",
            Notes: "cert-manager has been installed."
        )
    };

    public Task<List<HelmReleaseSummaryDto>> ListReleasesAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string? namespaceName = null,
        CancellationToken ct = default)
    {
        var result = _releases.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(namespaceName))
        {
            result = result.Where(r => r.Namespace.Equals(namespaceName, StringComparison.OrdinalIgnoreCase));
        }
        return Task.FromResult(result.ToList());
    }

    public Task<HelmReleaseDetailDto?> GetReleaseDetailAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        CancellationToken ct = default)
    {
        var rel = _releases.FirstOrDefault(r => r.Name.Equals(releaseName, StringComparison.OrdinalIgnoreCase) && r.Namespace.Equals(namespaceName, StringComparison.OrdinalIgnoreCase));
        if (rel == null) return Task.FromResult<HelmReleaseDetailDto?>(null);

        return Task.FromResult<HelmReleaseDetailDto?>(new HelmReleaseDetailDto(
            Name: rel.Name,
            Namespace: rel.Namespace,
            Revision: rel.Revision,
            Updated: rel.Updated,
            Status: rel.Status,
            Chart: rel.Chart,
            ChartName: rel.ChartName,
            ChartVersion: rel.ChartVersion,
            AppVersion: rel.AppVersion,
            Description: rel.Description,
            Notes: rel.Notes,
            ValuesYaml: "controller:\n  replicaCount: 2\n",
            Manifest: "# Simulated Helm Manifest for " + rel.Name,
            RepoUrl: "https://kubernetes.github.io/ingress-nginx"
        ));
    }

    public Task<List<HelmReleaseRevisionDto>> GetReleaseHistoryAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        CancellationToken ct = default)
    {
        return Task.FromResult(new List<HelmReleaseRevisionDto>
        {
            new(1, DateTimeOffset.UtcNow.AddDays(-30), "superseded", $"{releaseName}-1.0.0", "1.0.0", "Initial install"),
            new(2, DateTimeOffset.UtcNow.AddDays(-14), "deployed", $"{releaseName}-1.1.0", "1.1.0", "Upgrade release")
        });
    }

    public Task<HelmOperationResultDto> InstallOrUpgradeReleaseAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        InstallHelmReleaseRequestDto request,
        CancellationToken ct = default)
    {
        var existing = _releases.FirstOrDefault(r => r.Name == request.ReleaseName && r.Namespace == request.Namespace);
        if (existing != null)
        {
            _releases.Remove(existing);
            _releases.Add(existing with { Revision = existing.Revision + 1, Updated = DateTimeOffset.UtcNow });
        }
        else
        {
            _releases.Add(new HelmReleaseSummaryDto(
                Name: request.ReleaseName,
                Namespace: request.Namespace,
                Revision: 1,
                Updated: DateTimeOffset.UtcNow,
                Status: "deployed",
                Chart: $"{request.ChartName}-{request.Version ?? "1.0.0"}",
                ChartName: request.ChartName,
                ChartVersion: request.Version ?? "1.0.0",
                AppVersion: "1.0.0",
                Description: "Install complete (Demo Mode)"
            ));
        }

        return Task.FromResult(new HelmOperationResultDto(true, $"Release {request.ReleaseName} deployed successfully (Demo Mode)", request.ReleaseName, 1, "STATUS: deployed"));
    }

    public Task<HelmOperationResultDto> RollbackReleaseAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        int revision,
        CancellationToken ct = default)
    {
        return Task.FromResult(new HelmOperationResultDto(true, $"Rollback to revision {revision} complete (Demo Mode)", releaseName, revision));
    }

    public Task<HelmOperationResultDto> UninstallReleaseAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        CancellationToken ct = default)
    {
        _releases.RemoveAll(r => r.Name == releaseName && r.Namespace == namespaceName);
        return Task.FromResult(new HelmOperationResultDto(true, $"Release {releaseName} uninstalled successfully (Demo Mode)", releaseName));
    }
}
