using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace ControlPlane.Api.Features.Workloads.ImageUpdates;

public class ImageUpdateService : IImageUpdateService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ImageUpdateService> _logger;
    private readonly ConcurrentDictionary<string, ImageUpdateInfoDto> _latestResults = new(StringComparer.OrdinalIgnoreCase);

    public const string HttpClientName = "ImageRegistryClient";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);

    private static readonly Regex SemVerRegex = new(
        @"^v?(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?(?:\.(?<build>\d+))?(?<flavor>[-_].*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PreReleaseRegex = new(
        @"(?:[-._](?:rc|beta|alpha|preview|dev|nightly|canary|next|test))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ImageUpdateService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<ImageUpdateService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public ImageUpdateInfoDto? GetCached(string imageRef)
    {
        if (string.IsNullOrWhiteSpace(imageRef)) return null;
        var cleanRef = imageRef.Trim();
        if (_latestResults.TryGetValue(cleanRef, out var cached))
        {
            return cached;
        }

        var cacheKey = $"img-upd:{cleanRef}";
        if (_cache.TryGetValue<ImageUpdateInfoDto>(cacheKey, out var memoryCached) && memoryCached != null)
        {
            _latestResults[cleanRef] = memoryCached;
            return memoryCached;
        }

        return null;
    }

    public IReadOnlyDictionary<string, ImageUpdateInfoDto> GetAllCached()
    {
        return _latestResults;
    }

    public async Task<List<string>> GetImageTagsAsync(string imageRef, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageRef)) return new List<string>();
        var clean = imageRef.Trim();
        if (GetCached(clean) is { } cached && cached.AvailableTags != null && cached.AvailableTags.Count > 0)
        {
            return cached.AvailableTags;
        }

        var info = await CheckImageAsync(clean, false, ct);
        return info.AvailableTags ?? new List<string>();
    }

    public async Task<Dictionary<string, ImageUpdateInfoDto>> CheckImagesAsync(
        IEnumerable<string> imageRefs,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, ImageUpdateInfoDto>(StringComparer.OrdinalIgnoreCase);
        var distinctImages = imageRefs.Where(img => !string.IsNullOrWhiteSpace(img)).Distinct().ToList();

        // Run checks in parallel with bounded concurrency
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(distinctImages, parallelOptions, async (img, token) =>
        {
            try
            {
                var info = await CheckImageAsync(img, forceRefresh, token);
                lock (results)
                {
                    results[img] = info;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking image update for {Image}", img);
            }
        });

        return results;
    }

    public async Task<ImageUpdateInfoDto> CheckImageAsync(
        string imageRef,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageRef))
        {
            return new ImageUpdateInfoDto("", "", null, false, null, null, "Empty image reference", DateTimeOffset.UtcNow);
        }

        var cleanRef = imageRef.Trim();
        var cacheKey = $"img-upd:{cleanRef}";

        if (!forceRefresh && GetCached(cleanRef) is { } existing)
        {
            return existing;
        }

        try
        {
            var parsed = ParseImageRef(cleanRef);

            if (parsed.Tag.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                var floating = new ImageUpdateInfoDto(
                    Image: cleanRef,
                    CurrentTag: parsed.Tag,
                    LatestTag: null,
                    IsOutdated: false,
                    UpdateType: "floating",
                    LatestDigest: parsed.Digest,
                    Message: "Mutable floating tag (:latest)",
                    CheckedAt: DateTimeOffset.UtcNow
                );
                SaveToCache(cleanRef, cacheKey, floating);
                return floating;
            }

            var currentMatch = SemVerRegex.Match(parsed.Tag);
            if (!currentMatch.Success)
            {
                var nonSemver = new ImageUpdateInfoDto(
                    Image: cleanRef,
                    CurrentTag: parsed.Tag,
                    LatestTag: null,
                    IsOutdated: false,
                    UpdateType: null,
                    LatestDigest: parsed.Digest,
                    Message: $"Non-semver tag: '{parsed.Tag}'",
                    CheckedAt: DateTimeOffset.UtcNow
                );
                SaveToCache(cleanRef, cacheKey, nonSemver);
                return nonSemver;
            }

            var curMajor = int.Parse(currentMatch.Groups["major"].Value);
            var curMinor = currentMatch.Groups["minor"].Success ? int.Parse(currentMatch.Groups["minor"].Value) : 0;
            var curPatch = currentMatch.Groups["patch"].Success ? int.Parse(currentMatch.Groups["patch"].Value) : 0;
            var curBuild = currentMatch.Groups["build"].Success ? int.Parse(currentMatch.Groups["build"].Value) : 0;
            var curFlavor = currentMatch.Groups["flavor"].Value;

            var tags = await FetchTagsAsync(parsed, ct);
            if (tags.Count == 0)
            {
                var noTags = new ImageUpdateInfoDto(
                    Image: cleanRef,
                    CurrentTag: parsed.Tag,
                    LatestTag: null,
                    IsOutdated: false,
                    UpdateType: null,
                    LatestDigest: parsed.Digest,
                    Message: "Could not retrieve remote tags",
                    CheckedAt: DateTimeOffset.UtcNow
                );
                SaveToCache(cleanRef, cacheKey, noTags);
                return noTags;
            }

            string? bestTag = null;
            (int major, int minor, int patch, int build) highestVersion = (curMajor, curMinor, curPatch, curBuild);
            bool isPreReleaseCurrent = PreReleaseRegex.IsMatch(parsed.Tag);
            var compatibleCandidates = new List<(string Tag, (int major, int minor, int patch, int build) Version)>();

            foreach (var candTag in tags)
            {
                // Skip pre-release candidates unless current tag is already a pre-release
                if (!isPreReleaseCurrent && PreReleaseRegex.IsMatch(candTag)) continue;

                var candMatch = SemVerRegex.Match(candTag);
                if (!candMatch.Success) continue;

                var candFlavor = candMatch.Groups["flavor"].Value;
                if (!IsFlavorCompatible(curFlavor, candFlavor)) continue;

                var candMajor = int.Parse(candMatch.Groups["major"].Value);
                var candMinor = candMatch.Groups["minor"].Success ? int.Parse(candMatch.Groups["minor"].Value) : 0;
                var candPatch = candMatch.Groups["patch"].Success ? int.Parse(candMatch.Groups["patch"].Value) : 0;
                var candBuild = candMatch.Groups["build"].Success ? int.Parse(candMatch.Groups["build"].Value) : 0;

                var candVersion = (candMajor, candMinor, candPatch, candBuild);
                compatibleCandidates.Add((candTag, candVersion));

                if (!candTag.Equals(parsed.Tag, StringComparison.OrdinalIgnoreCase))
                {
                    if (CompareVersions(candVersion, highestVersion) > 0)
                    {
                        highestVersion = candVersion;
                        bestTag = candTag;
                    }
                }
            }

            var availableTags = compatibleCandidates
                .OrderByDescending(c => c.Version, Comparer<(int major, int minor, int patch, int build)>.Create(CompareVersions))
                .Select(c => c.Tag)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .ToList();

            if (!availableTags.Contains(parsed.Tag, StringComparer.OrdinalIgnoreCase))
            {
                availableTags.Add(parsed.Tag);
            }

            ImageUpdateInfoDto result;
            if (bestTag != null)
            {
                string updateType = highestVersion.major > curMajor
                    ? "major"
                    : highestVersion.minor > curMinor
                        ? "minor"
                        : "patch";

                result = new ImageUpdateInfoDto(
                    Image: cleanRef,
                    CurrentTag: parsed.Tag,
                    LatestTag: bestTag,
                    IsOutdated: true,
                    UpdateType: updateType,
                    LatestDigest: null,
                    Message: $"{char.ToUpper(updateType[0])}{updateType[1..]} update available: {parsed.Tag} -> {bestTag}",
                    CheckedAt: DateTimeOffset.UtcNow,
                    AvailableTags: availableTags
                );
            }
            else
            {
                result = new ImageUpdateInfoDto(
                    Image: cleanRef,
                    CurrentTag: parsed.Tag,
                    LatestTag: parsed.Tag,
                    IsOutdated: false,
                    UpdateType: null,
                    LatestDigest: parsed.Digest,
                    Message: "Image is up to date",
                    CheckedAt: DateTimeOffset.UtcNow,
                    AvailableTags: availableTags
                );
            }

            SaveToCache(cleanRef, cacheKey, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check image updates for {Image}", cleanRef);
            var errorInfo = new ImageUpdateInfoDto(
                Image: cleanRef,
                CurrentTag: ExtractTagOnly(cleanRef),
                LatestTag: null,
                IsOutdated: false,
                UpdateType: null,
                LatestDigest: null,
                Message: $"Registry check failed: {ex.Message}",
                CheckedAt: DateTimeOffset.UtcNow
            );
            SaveToCache(cleanRef, cacheKey, errorInfo);
            return errorInfo;
        }
    }

    private void SaveToCache(string cleanRef, string cacheKey, ImageUpdateInfoDto info)
    {
        _cache.Set(cacheKey, info, CacheDuration);
        _latestResults[cleanRef] = info;
    }

    public static bool IsFlavorCompatible(string curFlavor, string candFlavor)
    {
        // Both plain semver (e.g. 1.24.0 and 1.27.0)
        if (string.IsNullOrEmpty(curFlavor) && string.IsNullOrEmpty(candFlavor))
        {
            return true;
        }

        // One has a flavor suffix and the other does not
        if (string.IsNullOrEmpty(curFlavor) || string.IsNullOrEmpty(candFlavor))
        {
            return false;
        }

        // Exact match (e.g. -alpine and -alpine, or -slim and -slim)
        if (curFlavor.Equals(candFlavor, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Family match (e.g. -alpine3.18 and -alpine3.20, or -alpine and -alpine3.20)
        var curNorm = curFlavor.TrimStart('-', '_').ToLowerInvariant();
        var candNorm = candFlavor.TrimStart('-', '_').ToLowerInvariant();

        if (curNorm.StartsWith("alpine") && candNorm.StartsWith("alpine")) return true;
        if (curNorm.StartsWith("slim") && candNorm.StartsWith("slim")) return true;
        if (curNorm.StartsWith("debian") && candNorm.StartsWith("debian")) return true;
        if (curNorm.StartsWith("ubuntu") && candNorm.StartsWith("ubuntu")) return true;
        if (curNorm.StartsWith("bookworm") && candNorm.StartsWith("bookworm")) return true;
        if (curNorm.StartsWith("bullseye") && candNorm.StartsWith("bullseye")) return true;

        return false;
    }

    private static int CompareVersions(
        (int major, int minor, int patch, int build) a,
        (int major, int minor, int patch, int build) b)
    {
        if (a.major != b.major) return a.major.CompareTo(b.major);
        if (a.minor != b.minor) return a.minor.CompareTo(b.minor);
        if (a.patch != b.patch) return a.patch.CompareTo(b.patch);
        return a.build.CompareTo(b.build);
    }

    public static ParsedImageRef ParseImageRef(string raw)
    {
        var clean = raw.Trim();
        string? digest = null;

        if (clean.Contains('@'))
        {
            var parts = clean.Split('@', 2);
            clean = parts[0];
            digest = parts[1];
        }

        string registry;
        string pathWithTag;

        int firstSlash = clean.IndexOf('/');
        if (firstSlash != -1)
        {
            var firstSegment = clean[..firstSlash];
            if (firstSegment.Contains('.') || firstSegment.Contains(':') || firstSegment.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                registry = firstSegment;
                pathWithTag = clean[(firstSlash + 1)..];
            }
            else
            {
                registry = "docker.io";
                pathWithTag = clean;
            }
        }
        else
        {
            registry = "docker.io";
            pathWithTag = clean;
        }

        string repo;
        string tag;

        int lastColon = pathWithTag.LastIndexOf(':');
        if (lastColon != -1)
        {
            repo = pathWithTag[..lastColon];
            tag = pathWithTag[(lastColon + 1)..];
        }
        else
        {
            repo = pathWithTag;
            tag = "latest";
        }

        if (registry.Equals("docker.io", StringComparison.OrdinalIgnoreCase) && !repo.Contains('/'))
        {
            repo = $"library/{repo}";
        }

        return new ParsedImageRef(raw, registry, repo, tag, digest);
    }

    private static string ExtractTagOnly(string raw)
    {
        try
        {
            return ParseImageRef(raw).Tag;
        }
        catch
        {
            return "unknown";
        }
    }

    private async Task<List<string>> FetchTagsAsync(ParsedImageRef parsed, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);

        var client = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            if (parsed.Registry.Equals("docker.io", StringComparison.OrdinalIgnoreCase))
            {
                return await FetchDockerHubTagsAsync(client, parsed.Repository, cts.Token);
            }

            if (parsed.Registry.Equals("ghcr.io", StringComparison.OrdinalIgnoreCase))
            {
                return await FetchGhcrTagsAsync(client, parsed.Repository, cts.Token);
            }

            if (parsed.Registry.Equals("quay.io", StringComparison.OrdinalIgnoreCase))
            {
                return await FetchQuayTagsAsync(client, parsed.Repository, cts.Token);
            }

            // Fallback to standard OCI registry tags endpoint
            return await FetchGenericOciTagsAsync(client, parsed.Registry, parsed.Repository, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("Timeout querying registry {Registry} for {Repo}", parsed.Registry, parsed.Repository);
            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query tags from {Registry} for {Repo}", parsed.Registry, parsed.Repository);
            return new List<string>();
        }
    }

    private static async Task<List<string>> FetchDockerHubTagsAsync(HttpClient client, string repo, CancellationToken ct)
    {
        var url = $"https://hub.docker.com/v2/repositories/{repo}/tags?page_size=100&ordering=last_updated";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return new List<string>();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var tags = new List<string>();
        if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in results.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        tags.Add(name);
                    }
                }
            }
        }

        return tags;
    }

    private static async Task<List<string>> FetchGhcrTagsAsync(HttpClient client, string repo, CancellationToken ct)
    {
        // Try requesting token from GHCR token endpoint first
        string? token = null;
        try
        {
            var tokenUrl = $"https://ghcr.io/token?service=ghcr.io&scope=repository:{repo}:pull";
            var tokenResp = await client.GetAsync(tokenUrl, ct);
            if (tokenResp.IsSuccessStatusCode)
            {
                using var stream = await tokenResp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("token", out var tokenProp))
                {
                    token = tokenProp.GetString();
                }
            }
        }
        catch
        {
            // Token request may fail if no auth required
        }

        var tagsUrl = $"https://ghcr.io/v2/{repo}/tags/list";
        using var req = new HttpRequestMessage(HttpMethod.Get, tagsUrl);
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return new List<string>();

        using var contentStream = await resp.Content.ReadAsStreamAsync(ct);
        using var tagsDoc = await JsonDocument.ParseAsync(contentStream, cancellationToken: ct);

        var tags = new List<string>();
        if (tagsDoc.RootElement.TryGetProperty("tags", out var tagsArray) && tagsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tagsArray.EnumerateArray())
            {
                var name = item.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    tags.Add(name);
                }
            }
        }

        return tags;
    }

    private static async Task<List<string>> FetchQuayTagsAsync(HttpClient client, string repo, CancellationToken ct)
    {
        var url = $"https://quay.io/api/v1/repository/{repo}/tag/?limit=100&only_active_tags=true";
        var resp = await client.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return new List<string>();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var tags = new List<string>();
        if (doc.RootElement.TryGetProperty("tags", out var tagsArray) && tagsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tagsArray.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        tags.Add(name);
                    }
                }
            }
        }

        return tags;
    }

    private static async Task<List<string>> FetchGenericOciTagsAsync(HttpClient client, string registry, string repo, CancellationToken ct)
    {
        var url = $"https://{registry}/v2/{repo}/tags/list";
        var resp = await client.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return new List<string>();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var tags = new List<string>();
        if (doc.RootElement.TryGetProperty("tags", out var tagsArray) && tagsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tagsArray.EnumerateArray())
            {
                var name = item.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    tags.Add(name);
                }
            }
        }

        return tags;
    }
}

public record ParsedImageRef(
    string Original,
    string Registry,
    string Repository,
    string Tag,
    string? Digest
);
