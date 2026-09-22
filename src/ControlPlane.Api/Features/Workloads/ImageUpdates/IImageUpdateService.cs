namespace ControlPlane.Api.Features.Workloads.ImageUpdates;

public interface IImageUpdateService
{
    Task<ImageUpdateInfoDto> CheckImageAsync(string imageRef, bool forceRefresh = false, CancellationToken ct = default);
    Task<Dictionary<string, ImageUpdateInfoDto>> CheckImagesAsync(IEnumerable<string> imageRefs, bool forceRefresh = false, CancellationToken ct = default);
    ImageUpdateInfoDto? GetCached(string imageRef);
    IReadOnlyDictionary<string, ImageUpdateInfoDto> GetAllCached();
    Task<List<string>> GetImageTagsAsync(string imageRef, CancellationToken ct = default);
}
