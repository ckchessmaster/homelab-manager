using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.Kubernetes.Helm;

public class HelmUpdateService : IHelmUpdateService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HelmUpdateService> _logger;
    private readonly ConcurrentDictionary<string, HelmChartUpdateInfoDto> _latestResults = new(StringComparer.OrdinalIgnoreCase);

    public const string HttpClientName = "HelmRegistryClient";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);

    private static readonly Regex SemVerRegex = new(
        @"^v?(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?(?:\.(?<build>\d+))?(?<flavor>[-_].*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PreReleaseRegex = new(
        @"(?:[-._](?:rc|beta|alpha|preview|dev|nightly|canary|next|test))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public HelmUpdateService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<HelmUpdateService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public HelmChartUpdateInfoDto? GetCached(string chartName, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(chartName) || string.IsNullOrWhiteSpace(currentVersion)) return null;
        var key = BuildKey(chartName, currentVersion);

        if (_latestResults.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var cacheKey = $"helm-upd:{key}";
        if (_cache.TryGetValue<HelmChartUpdateInfoDto>(cacheKey, out var memCached) && memCached != null)
        {
            _latestResults[key] = memCached;
            return memCached;
        }

        return null;
    }

    public IReadOnlyDictionary<string, HelmChartUpdateInfoDto> GetAllCached()
    {
        return _latestResults;
    }

    public async Task<List<string>> GetChartVersionsAsync(
        string chartName,
        string? repoUrl = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chartName)) return new List<string>();
        var clean = chartName.Trim();
        var effectiveRepo = repoUrl;
        if (string.IsNullOrWhiteSpace(effectiveRepo))
        {
            var catalogItem = HelmCatalogService.CuratedCatalog.FirstOrDefault(c =>
                c.ChartName.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                c.Id.Equals(clean, StringComparison.OrdinalIgnoreCase));
            if (catalogItem != null) effectiveRepo = catalogItem.RepoUrl;
        }

        var cacheKey = $"helm-versions:{clean.ToLowerInvariant()}:{effectiveRepo ?? ""}";
        if (_cache.TryGetValue<List<string>>(cacheKey, out var memCached) && memCached != null && memCached.Count > 0)
        {
            return memCached;
        }

        var versions = new List<string>();

        // 1. Try Artifact Hub package detail
        try
        {
            var (hubVersion, _, _, repoName) = await QueryArtifactHubAsync(clean, effectiveRepo, ct);
            if (!string.IsNullOrWhiteSpace(repoName))
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                client.Timeout = RequestTimeout;
                var detailUrl = $"https://artifacthub.io/api/v1/packages/helm/{Uri.EscapeDataString(repoName)}/{Uri.EscapeDataString(clean)}";
                using var detailReq = new HttpRequestMessage(HttpMethod.Get, detailUrl);
                detailReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                detailReq.Headers.UserAgent.ParseAdd("ControlPlane-HomelabManager/1.0");

                using var detailResp = await client.SendAsync(detailReq, ct);
                if (detailResp.IsSuccessStatusCode)
                {
                    var detailJson = await detailResp.Content.ReadAsStringAsync(ct);
                    using var detailDoc = JsonDocument.Parse(detailJson);
                    if (detailDoc.RootElement.TryGetProperty("available_versions", out var avProp) && avProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var vObj in avProp.EnumerateArray())
                        {
                            if (vObj.TryGetProperty("version", out var vVal))
                            {
                                var verStr = vVal.GetString();
                                if (!string.IsNullOrWhiteSpace(verStr))
                                {
                                    versions.Add(verStr.Trim());
                                }
                            }
                        }
                    }
                }
            }
            if (versions.Count == 0 && !string.IsNullOrWhiteSpace(hubVersion))
            {
                versions.Add(hubVersion);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get Artifact Hub versions for chart {Chart}", clean);
        }

        // 2. If still empty, try index.yaml
        if (versions.Count == 0 && !string.IsNullOrWhiteSpace(effectiveRepo))
        {
            var (_, _, yamlVersions) = await QueryIndexYamlAsync(clean, effectiveRepo, ct);
            versions.AddRange(yamlVersions);
        }

        var sorted = versions
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(v =>
            {
                var m = SemVerRegex.Match(v);
                return m.Success ? ParseSemVer(m) : (-1, -1, -1, -1);
            })
            .Take(50)
            .ToList();

        if (sorted.Count > 0)
        {
            _cache.Set(cacheKey, sorted, CacheDuration);
        }

        return sorted;
    }

    public async Task<Dictionary<string, HelmChartUpdateInfoDto>> CheckReleasesAsync(
        IEnumerable<HelmReleaseSummaryDto> releases,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        var releaseList = releases.ToList();
        var results = new Dictionary<string, HelmChartUpdateInfoDto>(StringComparer.OrdinalIgnoreCase);

        // Group by distinct ChartName + ChartVersion to avoid redundant upstream requests
        var distinctCharts = releaseList
            .Where(r => !string.IsNullOrWhiteSpace(r.ChartName) && !string.IsNullOrWhiteSpace(r.ChartVersion))
            .GroupBy(r => (ChartName: r.ChartName.Trim(), Version: r.ChartVersion.Trim()),
                     r => r,
                     (key, group) => (key.ChartName, key.Version, Sample: group.First()))
            .ToList();

        var chartUpdates = new ConcurrentDictionary<(string ChartName, string Version), HelmChartUpdateInfoDto>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(distinctCharts, parallelOptions, async (item, token) =>
        {
            try
            {
                var update = await CheckChartUpdateAsync(
                    item.ChartName,
                    item.Version,
                    item.Sample.AppVersion,
                    null,
                    forceRefresh,
                    token
                );
                chartUpdates[(item.ChartName, item.Version)] = update;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking Helm update for chart {Chart}:{Version}", item.ChartName, item.Version);
            }
        });

        foreach (var r in releaseList)
        {
            var relKey = $"{r.Namespace}/{r.Name}";
            if (chartUpdates.TryGetValue((r.ChartName.Trim(), r.ChartVersion.Trim()), out var update))
            {
                results[relKey] = update;
            }
            else
            {
                // Fallback to cache lookup
                var cached = GetCached(r.ChartName, r.ChartVersion);
                if (cached != null)
                {
                    results[relKey] = cached;
                }
            }
        }

        return results;
    }

    public async Task<HelmChartUpdateInfoDto> CheckChartUpdateAsync(
        string chartName,
        string currentVersion,
        string? currentAppVersion = null,
        string? repoUrl = null,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chartName))
        {
            return new HelmChartUpdateInfoDto("", currentVersion ?? "", null, null, null, false, null, null, "Empty chart name", DateTimeOffset.UtcNow);
        }

        var cleanChart = chartName.Trim();
        var cleanVer = (currentVersion ?? "").Trim();
        var key = BuildKey(cleanChart, cleanVer);
        var cacheKey = $"helm-upd:{key}";

        if (!forceRefresh && GetCached(cleanChart, cleanVer) is { } existing)
        {
            return existing;
        }

        try
        {
            // Resolve repository URL from catalog if not provided
            var effectiveRepoUrl = repoUrl;
            if (string.IsNullOrWhiteSpace(effectiveRepoUrl))
            {
                var catalogItem = HelmCatalogService.CuratedCatalog.FirstOrDefault(c =>
                    c.ChartName.Equals(cleanChart, StringComparison.OrdinalIgnoreCase) ||
                    c.Id.Equals(cleanChart, StringComparison.OrdinalIgnoreCase));
                if (catalogItem != null)
                {
                    effectiveRepoUrl = catalogItem.RepoUrl;
                }
            }

            // Step 1: Query Artifact Hub
            var (hubVersion, hubAppVersion, hubRepoUrl, _) = await QueryArtifactHubAsync(cleanChart, effectiveRepoUrl, ct);

            string? candidateVersion = hubVersion;
            string? candidateAppVersion = hubAppVersion;
            var resolvedRepoUrl = hubRepoUrl ?? effectiveRepoUrl;

            // Step 2: Fallback to direct index.yaml ONLY if Artifact Hub yielded nothing and repoUrl is known
            if (string.IsNullOrWhiteSpace(candidateVersion) && !string.IsNullOrWhiteSpace(resolvedRepoUrl))
            {
                var (yamlVersion, yamlAppVersion, _) = await QueryIndexYamlAsync(cleanChart, resolvedRepoUrl, ct);
                if (!string.IsNullOrWhiteSpace(yamlVersion))
                {
                    candidateVersion = yamlVersion;
                    candidateAppVersion = yamlAppVersion;
                }
            }

            var availableVersionsCacheKey = $"helm-versions:{cleanChart.ToLowerInvariant()}:{resolvedRepoUrl ?? ""}";
            List<string> availableVersions;
            if (_cache.TryGetValue<List<string>>(availableVersionsCacheKey, out var cv) && cv != null && cv.Count > 0)
            {
                availableVersions = cv;
            }
            else
            {
                availableVersions = new List<string>();
                if (!string.IsNullOrWhiteSpace(candidateVersion)) availableVersions.Add(candidateVersion);
                if (!string.IsNullOrWhiteSpace(cleanVer) && !availableVersions.Contains(cleanVer, StringComparer.OrdinalIgnoreCase))
                {
                    availableVersions.Add(cleanVer);
                }
            }

            if (string.IsNullOrWhiteSpace(candidateVersion))
            {
                var notFound = new HelmChartUpdateInfoDto(
                    ChartName: cleanChart,
                    CurrentVersion: cleanVer,
                    LatestVersion: null,
                    CurrentAppVersion: currentAppVersion,
                    LatestAppVersion: null,
                    IsOutdated: false,
                    UpdateType: null,
                    RepoUrl: resolvedRepoUrl,
                    Message: "No remote repository or version found for chart",
                    CheckedAt: DateTimeOffset.UtcNow,
                    AvailableVersions: availableVersions
                );
                SaveToCache(key, cacheKey, notFound);
                return notFound;
            }

            // Compare versions
            var curMatch = SemVerRegex.Match(cleanVer);
            var candMatch = SemVerRegex.Match(candidateVersion);

            if (!curMatch.Success || !candMatch.Success)
            {
                // Simple string equality if not standard SemVer
                bool isDiff = !cleanVer.Equals(candidateVersion, StringComparison.OrdinalIgnoreCase);
                var fallbackResult = new HelmChartUpdateInfoDto(
                    ChartName: cleanChart,
                    CurrentVersion: cleanVer,
                    LatestVersion: candidateVersion,
                    CurrentAppVersion: currentAppVersion,
                    LatestAppVersion: candidateAppVersion,
                    IsOutdated: isDiff,
                    UpdateType: isDiff ? "patch" : null,
                    RepoUrl: resolvedRepoUrl,
                    Message: isDiff ? $"Newer version available: {cleanVer} -> {candidateVersion}" : "Chart is up to date",
                    CheckedAt: DateTimeOffset.UtcNow,
                    AvailableVersions: availableVersions
                );
                SaveToCache(key, cacheKey, fallbackResult);
                return fallbackResult;
            }

            // Don't flag as outdated if candidate is pre-release but current is stable
            bool isCurrentPreRelease = PreReleaseRegex.IsMatch(cleanVer);
            bool isCandidatePreRelease = PreReleaseRegex.IsMatch(candidateVersion);
            if (!isCurrentPreRelease && isCandidatePreRelease)
            {
                var stableResult = new HelmChartUpdateInfoDto(
                    ChartName: cleanChart,
                    CurrentVersion: cleanVer,
                    LatestVersion: cleanVer,
                    CurrentAppVersion: currentAppVersion,
                    LatestAppVersion: candidateAppVersion,
                    IsOutdated: false,
                    UpdateType: null,
                    RepoUrl: resolvedRepoUrl,
                    Message: "Chart is up to date (candidate is pre-release)",
                    CheckedAt: DateTimeOffset.UtcNow,
                    AvailableVersions: availableVersions
                );
                SaveToCache(key, cacheKey, stableResult);
                return stableResult;
            }

            var curV = ParseSemVer(curMatch);
            var candV = ParseSemVer(candMatch);

            int cmp = CompareVersions(candV, curV);
            if (cmp > 0)
            {
                string updateType = candV.Major > curV.Major
                    ? "major"
                    : candV.Minor > curV.Minor
                        ? "minor"
                        : "patch";

                var outdated = new HelmChartUpdateInfoDto(
                    ChartName: cleanChart,
                    CurrentVersion: cleanVer,
                    LatestVersion: candidateVersion,
                    CurrentAppVersion: currentAppVersion,
                    LatestAppVersion: candidateAppVersion,
                    IsOutdated: true,
                    UpdateType: updateType,
                    RepoUrl: resolvedRepoUrl,
                    Message: $"{char.ToUpper(updateType[0])}{updateType[1..]} update available: {cleanVer} -> {candidateVersion}",
                    CheckedAt: DateTimeOffset.UtcNow,
                    AvailableVersions: availableVersions
                );
                SaveToCache(key, cacheKey, outdated);
                return outdated;
            }
            else
            {
                var upToDate = new HelmChartUpdateInfoDto(
                    ChartName: cleanChart,
                    CurrentVersion: cleanVer,
                    LatestVersion: candidateVersion,
                    CurrentAppVersion: currentAppVersion,
                    LatestAppVersion: candidateAppVersion,
                    IsOutdated: false,
                    UpdateType: null,
                    RepoUrl: resolvedRepoUrl,
                    Message: "Chart is up to date",
                    CheckedAt: DateTimeOffset.UtcNow,
                    AvailableVersions: availableVersions
                );
                SaveToCache(key, cacheKey, upToDate);
                return upToDate;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check Helm chart update for {Chart}:{Version}", cleanChart, cleanVer);
            var err = new HelmChartUpdateInfoDto(
                ChartName: cleanChart,
                CurrentVersion: cleanVer,
                LatestVersion: null,
                CurrentAppVersion: currentAppVersion,
                LatestAppVersion: null,
                IsOutdated: false,
                UpdateType: null,
                RepoUrl: repoUrl,
                Message: $"Check failed: {ex.Message}",
                CheckedAt: DateTimeOffset.UtcNow,
                AvailableVersions: new List<string> { cleanVer }
            );
            SaveToCache(key, cacheKey, err);
            return err;
        }
    }

    private async Task<(string? Version, string? AppVersion, string? RepoUrl, string? RepoName)> QueryArtifactHubAsync(
        string chartName,
        string? preferredRepoUrl,
        CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = RequestTimeout;

            var searchUrl = $"https://artifacthub.io/api/v1/packages/search?kind=0&name={Uri.EscapeDataString(chartName)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("ControlPlane-HomelabManager/1.0");

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return (null, null, null, null);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("packages", out var packagesProp) ||
                packagesProp.ValueKind != JsonValueKind.Array)
            {
                return (null, null, null, null);
            }

            var matchingPackages = new List<ArtifactHubCandidate>();

            foreach (var item in packagesProp.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (!string.Equals(name, chartName, StringComparison.OrdinalIgnoreCase)) continue;

                var version = item.TryGetProperty("version", out var v) ? v.GetString() : null;
                var appVersion = item.TryGetProperty("app_version", out var av) ? av.GetString() : null;
                var official = item.TryGetProperty("official", out var off) && off.GetBoolean();
                var stars = item.TryGetProperty("stars", out var st) && st.TryGetInt32(out var s) ? s : 0;

                string? repoUrl = null;
                string? repoName = null;
                if (item.TryGetProperty("repository", out var repoProp) && repoProp.ValueKind == JsonValueKind.Object)
                {
                    repoName = repoProp.TryGetProperty("name", out var rn) ? rn.GetString() : null;
                    repoUrl = repoProp.TryGetProperty("url", out var ru) ? ru.GetString() : null;
                }

                bool verifiedPublisher = item.TryGetProperty("repository", out var vpProp) &&
                    vpProp.TryGetProperty("verified_publisher", out var vp) && vp.GetBoolean();

                if (!string.IsNullOrWhiteSpace(version))
                {
                    matchingPackages.Add(new ArtifactHubCandidate(
                        Name: name!,
                        Version: version,
                        AppVersion: appVersion,
                        RepoUrl: repoUrl,
                        RepoName: repoName,
                        Official: official,
                        VerifiedPublisher: verifiedPublisher,
                        Stars: stars
                    ));
                }
            }

            if (matchingPackages.Count == 0) return (null, null, null, null);

            ArtifactHubCandidate? chosen = null;

            // Prioritize package matching preferredRepoUrl if known
            if (!string.IsNullOrWhiteSpace(preferredRepoUrl))
            {
                var cleanPref = preferredRepoUrl.TrimEnd('/').ToLowerInvariant();
                chosen = matchingPackages.FirstOrDefault(p =>
                    !string.IsNullOrWhiteSpace(p.RepoUrl) &&
                    cleanPref.Contains(p.RepoUrl.TrimEnd('/').ToLowerInvariant()));
            }

            // Otherwise pick highest score: verified publisher > official > stars
            chosen ??= matchingPackages
                .OrderByDescending(p => p.VerifiedPublisher)
                .ThenByDescending(p => p.Official)
                .ThenByDescending(p => p.Stars)
                .First();

            return (chosen.Version, chosen.AppVersion, chosen.RepoUrl, chosen.RepoName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Artifact Hub search query failed for chart {Chart}", chartName);
            return (null, null, null, null);
        }
    }

    private async Task<(string? Version, string? AppVersion, List<string> Versions)> QueryIndexYamlAsync(
        string chartName,
        string repoUrl,
        CancellationToken ct)
    {
        var versions = new List<string>();
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = RequestTimeout;

            var indexUrl = $"{repoUrl.TrimEnd('/')}/index.yaml";
            using var request = new HttpRequestMessage(HttpMethod.Get, indexUrl);
            request.Headers.UserAgent.ParseAdd("ControlPlane-HomelabManager/1.0");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return (null, null, versions);

            // Read up to 256KB to avoid huge index downloads
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            string? line;
            bool inChartBlock = false;
            string? foundVersion = null;
            string? foundAppVersion = null;
            int linesRead = 0;

            var chartHeaderRegex = new Regex($@"^\s*{Regex.Escape(chartName)}:\s*$", RegexOptions.IgnoreCase);
            var versionRegex = new Regex(@"^\s*-\s*version:\s*[""']?([^""'\s]+)[""']?", RegexOptions.IgnoreCase);
            var subVersionRegex = new Regex(@"^\s*version:\s*[""']?([^""'\s]+)[""']?", RegexOptions.IgnoreCase);
            var appVersionRegex = new Regex(@"^\s*appVersion:\s*[""']?([^""'\s]+)[""']?", RegexOptions.IgnoreCase);
            var topLevelKeyRegex = new Regex(@"^[a-zA-Z0-9_\-]+:\s*");

            while ((line = await reader.ReadLineAsync(ct)) != null && linesRead++ < 2000)
            {
                if (!inChartBlock)
                {
                    if (chartHeaderRegex.IsMatch(line))
                    {
                        inChartBlock = true;
                    }
                }
                else
                {
                    // If we encounter another top-level or same-level unindented key, stop
                    if (topLevelKeyRegex.IsMatch(line) && !line.StartsWith(" ") && !line.StartsWith("\t"))
                    {
                        break;
                    }

                    var vm = versionRegex.Match(line);
                    if (!vm.Success) vm = subVersionRegex.Match(line);
                    if (vm.Success)
                    {
                        var v = vm.Groups[1].Value.Trim();
                        if (foundVersion == null)
                        {
                            foundVersion = v;
                        }
                        versions.Add(v);
                    }

                    if (foundAppVersion == null)
                    {
                        var avm = appVersionRegex.Match(line);
                        if (avm.Success)
                        {
                            foundAppVersion = avm.Groups[1].Value.Trim();
                        }
                    }
                }
            }

            return (foundVersion, foundAppVersion, versions);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query index.yaml from {RepoUrl} for chart {Chart}", repoUrl, chartName);
            return (null, null, versions);
        }
    }

    private void SaveToCache(string key, string cacheKey, HelmChartUpdateInfoDto result)
    {
        _latestResults[key] = result;
        _cache.Set(cacheKey, result, CacheDuration);
    }

    private static string BuildKey(string chartName, string version) =>
        $"{chartName.Trim().ToLowerInvariant()}:{version.Trim().ToLowerInvariant()}";

    private static (int Major, int Minor, int Patch, int Build) ParseSemVer(Match m)
    {
        var major = int.Parse(m.Groups["major"].Value);
        var minor = m.Groups["minor"].Success ? int.Parse(m.Groups["minor"].Value) : 0;
        var patch = m.Groups["patch"].Success ? int.Parse(m.Groups["patch"].Value) : 0;
        var build = m.Groups["build"].Success ? int.Parse(m.Groups["build"].Value) : 0;
        return (major, minor, patch, build);
    }

    private static int CompareVersions(
        (int Major, int Minor, int Patch, int Build) a,
        (int Major, int Minor, int Patch, int Build) b)
    {
        if (a.Major != b.Major) return a.Major.CompareTo(b.Major);
        if (a.Minor != b.Minor) return a.Minor.CompareTo(b.Minor);
        if (a.Patch != b.Patch) return a.Patch.CompareTo(b.Patch);
        return a.Build.CompareTo(b.Build);
    }

    private record ArtifactHubCandidate(
        string Name,
        string Version,
        string? AppVersion,
        string? RepoUrl,
        string? RepoName,
        bool Official,
        bool VerifiedPublisher,
        int Stars
    );
}
