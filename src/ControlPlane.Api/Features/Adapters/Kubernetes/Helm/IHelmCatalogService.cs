namespace ControlPlane.Api.Features.Adapters.Kubernetes.Helm;

public interface IHelmCatalogService
{
    IReadOnlyList<HelmCatalogItemDto> GetCatalog();
    HelmCatalogItemDto? GetCatalogItem(string id);
}
