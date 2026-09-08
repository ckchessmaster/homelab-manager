namespace ControlPlane.Api.Features.Discovery;

public interface IDiscoveryService
{
    Task<DiscoveryScanResult> ScanAsync(bool includeProxmox = true, bool includeKubernetes = true, bool includeUniFi = true, CancellationToken ct = default);
    Task<ImportCandidateResponse> ImportCandidateAsync(ImportCandidateRequest request, CancellationToken ct = default);
    Task<BatchImportCandidatesResponse> ImportCandidatesBatchAsync(BatchImportCandidatesRequest request, CancellationToken ct = default);
}

