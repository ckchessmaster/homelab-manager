using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using ControlPlane.Api.Features.Agents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Agents;

public class AgentBinarySyncService : IAgentBinarySyncService
{
    public const string HttpClientName = "AgentBinarySyncClient";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentBinaryService _binaryService;
    private readonly IOptions<AgentBinarySyncOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentBinarySyncService> _logger;

    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private DateTimeOffset? _lastCheckedAtUtc;
    private DateTimeOffset? _lastDownloadedAtUtc;
    private string? _latestAvailableVersion;
    private string _lastStatus = "Idle";
    private string? _lastError;
    private bool _isSyncing;

    public AgentBinarySyncService(
        IHttpClientFactory httpClientFactory,
        AgentBinaryService binaryService,
        IOptions<AgentBinarySyncOptions> options,
        IConfiguration configuration,
        ILogger<AgentBinarySyncService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _binaryService = binaryService;
        _options = options;
        _configuration = configuration;
        _logger = logger;

        LoadPersistedVersionInfo();
    }

    private string ResolveRepository()
    {
        var configured = _configuration["ControlPlane:AgentBinaryRepo"] ??
                         Environment.GetEnvironmentVariable("AGENT_BINARY_REPO") ??
                         Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");

        return !string.IsNullOrWhiteSpace(configured) ? configured.Trim() : _options.Value.Repository.Trim();
    }

    private void LoadPersistedVersionInfo()
    {
        try
        {
            var distDir = _binaryService.GetDistDirectory();
            var metaFile = Path.Combine(distDir, "version.json");
            if (File.Exists(metaFile))
            {
                var content = File.ReadAllText(metaFile);
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (root.TryGetProperty("version", out var v))
                {
                    _latestAvailableVersion = v.GetString();
                }
                if (root.TryGetProperty("lastDownloadedAtUtc", out var d) && d.TryGetDateTimeOffset(out var dt))
                {
                    _lastDownloadedAtUtc = dt;
                    _lastCheckedAtUtc = dt;
                    _lastStatus = "Success";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load persisted agent binary version.json metadata.");
        }
    }

    private void PersistVersionInfo(string version, string repository)
    {
        try
        {
            var distDir = _binaryService.GetDistDirectory();
            Directory.CreateDirectory(distDir);
            var metaFile = Path.Combine(distDir, "version.json");

            var payload = new
            {
                version,
                repository,
                lastDownloadedAtUtc = DateTimeOffset.UtcNow
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metaFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist version.json in agent distribution directory.");
        }
    }

    public Task<AgentBinaryStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var repo = ResolveRepository();
        var currentVersion = _binaryService.GetTargetVersion();
        var platforms = _binaryService.GetPlatformDetails();

        var status = new AgentBinaryStatusDto(
            CurrentInstalledVersion: currentVersion,
            LatestAvailableVersion: _latestAvailableVersion,
            LastCheckedAtUtc: _lastCheckedAtUtc,
            LastDownloadedAtUtc: _lastDownloadedAtUtc,
            Status: _lastStatus,
            LastError: _lastError,
            IsSyncing: _isSyncing,
            AutoSyncEnabled: _options.Value.Enabled,
            SyncIntervalHours: _options.Value.SyncIntervalHours,
            Repository: repo,
            Platforms: platforms
        );

        return Task.FromResult(status);
    }

    public async Task<AgentBinarySyncResultDto> SyncBinariesAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!await _syncLock.WaitAsync(0, cancellationToken))
        {
            _logger.LogInformation("Agent binary synchronization already in progress, skipping duplicate request.");
            var currentStatus = await GetStatusAsync(cancellationToken);
            return new AgentBinarySyncResultDto(
                Success: false,
                Version: _latestAvailableVersion,
                Message: "Synchronization is already running.",
                UpdatedBinaries: Array.Empty<string>(),
                Status: currentStatus
            );
        }

        _isSyncing = true;
        _lastStatus = "Checking";
        _lastCheckedAtUtc = DateTimeOffset.UtcNow;
        var repo = ResolveRepository();

        try
        {
            using var client = CreateHttpClient();
            var releaseUrl = $"https://api.github.com/repos/{repo}/releases/latest";
            _logger.LogInformation("Checking for latest agent binaries at {ReleaseUrl}", releaseUrl);

            using var response = await client.GetAsync(releaseUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = $"GitHub release check returned HTTP {response.StatusCode} ({response.ReasonPhrase})";
                _logger.LogWarning("Failed to query GitHub releases for repository {Repo}: {Error}", repo, errorMsg);
                _lastStatus = "Failed";
                _lastError = errorMsg;

                var status = await GetStatusAsync(cancellationToken);
                return new AgentBinarySyncResultDto(
                    Success: false,
                    Version: _latestAvailableVersion,
                    Message: errorMsg,
                    UpdatedBinaries: Array.Empty<string>(),
                    Status: status
                );
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
            _latestAvailableVersion = tagName;

            if (string.IsNullOrWhiteSpace(tagName))
            {
                var msg = "GitHub release did not contain a valid tag_name.";
                _lastStatus = "Failed";
                _lastError = msg;
                var status = await GetStatusAsync(cancellationToken);
                return new AgentBinarySyncResultDto(false, null, msg, Array.Empty<string>(), status);
            }

            var currentInstalled = _binaryService.GetTargetVersion();
            var platformDetails = _binaryService.GetPlatformDetails();
            var allPlatformsAvailable = platformDetails.All(p => p.IsAvailable && (p.SizeBytes ?? 0) > 0);

            var versionMatches = string.Equals(tagName.TrimStart('v'), currentInstalled.TrimStart('v'), StringComparison.OrdinalIgnoreCase);

            if (!force && versionMatches && allPlatformsAvailable)
            {
                _logger.LogInformation("Agent binaries for release {TagName} are already up to date.", tagName);
                _lastStatus = "UpToDate";
                _lastError = null;
                var status = await GetStatusAsync(cancellationToken);
                return new AgentBinarySyncResultDto(
                    Success: true,
                    Version: tagName,
                    Message: $"Agent binaries for version {tagName} are already up to date.",
                    UpdatedBinaries: Array.Empty<string>(),
                    Status: status
                );
            }

            if (!root.TryGetProperty("assets", out var assetsElem) || assetsElem.ValueKind != JsonValueKind.Array)
            {
                var msg = "Release contains no attached assets.";
                _lastStatus = "Failed";
                _lastError = msg;
                var status = await GetStatusAsync(cancellationToken);
                return new AgentBinarySyncResultDto(false, tagName, msg, Array.Empty<string>(), status);
            }

            var assets = new List<(string Name, string DownloadUrl, long Size)>();
            foreach (var asset in assetsElem.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                var size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0L;

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(downloadUrl))
                {
                    assets.Add((name, downloadUrl, size));
                }
            }

            _lastStatus = "Downloading";
            var distDir = _binaryService.GetDistDirectory();
            Directory.CreateDirectory(distDir);
            var updatedFiles = new List<string>();

            // 1. Linux AMD64
            var amd64Asset = FindAsset(assets, "linux", "amd64");
            if (amd64Asset != null)
            {
                await DownloadAndExtractAssetAsync(client, amd64Asset.Value.DownloadUrl, amd64Asset.Value.Name,
                    distDir, "controlplane-agent-linux-amd64", cancellationToken);
                updatedFiles.Add("controlplane-agent-linux-amd64");
            }
            else
            {
                _logger.LogWarning("No suitable asset found for linux-amd64 in release {Tag}", tagName);
            }

            // 2. Linux ARM64
            var arm64Asset = FindAsset(assets, "linux", "arm64");
            if (arm64Asset != null)
            {
                await DownloadAndExtractAssetAsync(client, arm64Asset.Value.DownloadUrl, arm64Asset.Value.Name,
                    distDir, "controlplane-agent-linux-arm64", cancellationToken);
                updatedFiles.Add("controlplane-agent-linux-arm64");
            }
            else
            {
                _logger.LogWarning("No suitable asset found for linux-arm64 in release {Tag}", tagName);
            }

            // 3. Windows AMD64
            var winAsset = FindAsset(assets, "windows", "amd64");
            if (winAsset != null)
            {
                await DownloadAndExtractAssetAsync(client, winAsset.Value.DownloadUrl, winAsset.Value.Name,
                    distDir, "controlplane-agent-windows-amd64.exe", cancellationToken);
                updatedFiles.Add("controlplane-agent-windows-amd64.exe");
            }
            else
            {
                _logger.LogWarning("No suitable asset found for windows-amd64 in release {Tag}", tagName);
            }

            PersistVersionInfo(tagName, repo);
            _lastDownloadedAtUtc = DateTimeOffset.UtcNow;
            _lastStatus = "Success";
            _lastError = null;

            _logger.LogInformation("Successfully downloaded {Count} agent binaries for release {Tag}", updatedFiles.Count, tagName);
            var finalStatus = await GetStatusAsync(cancellationToken);
            return new AgentBinarySyncResultDto(
                Success: true,
                Version: tagName,
                Message: $"Successfully synchronized {updatedFiles.Count} agent binaries for {tagName}.",
                UpdatedBinaries: updatedFiles,
                Status: finalStatus
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _lastStatus = "Canceled";
            var status = await GetStatusAsync(CancellationToken.None);
            return new AgentBinarySyncResultDto(false, _latestAvailableVersion, "Synchronization was canceled.", Array.Empty<string>(), status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during agent binary synchronization.");
            _lastStatus = "Failed";
            _lastError = ex.Message;
            var status = await GetStatusAsync(CancellationToken.None);
            return new AgentBinarySyncResultDto(false, _latestAvailableVersion, $"Sync failed: {ex.Message}", Array.Empty<string>(), status);
        }
        finally
        {
            _isSyncing = false;
            _syncLock.Release();
        }
    }

    private static (string Name, string DownloadUrl, long Size)? FindAsset(
        List<(string Name, string DownloadUrl, long Size)> assets,
        string os,
        string arch)
    {
        // 1. Check for archive format (e.g. controlplane-agent_v1.3.0_linux_amd64.tar.gz or .zip)
        var archiveMatch = assets.FirstOrDefault(a =>
            a.Name.Contains(os, StringComparison.OrdinalIgnoreCase) &&
            a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
            (a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
             a.Name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ||
             a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)));

        if (!string.IsNullOrEmpty(archiveMatch.Name)) return archiveMatch;

        // 2. Check for direct binary
        var expectedBinaryName = os switch
        {
            "windows" => $"controlplane-agent-windows-{arch}.exe",
            _ => $"controlplane-agent-{os}-{arch}"
        };

        var directMatch = assets.FirstOrDefault(a =>
            string.Equals(a.Name, expectedBinaryName, StringComparison.OrdinalIgnoreCase) ||
            (a.Name.Contains(os, StringComparison.OrdinalIgnoreCase) &&
             a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
             !a.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
             !a.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)));

        if (!string.IsNullOrEmpty(directMatch.Name)) return directMatch;

        return null;
    }

    private async Task DownloadAndExtractAssetAsync(
        HttpClient client,
        string downloadUrl,
        string assetName,
        string distDir,
        string targetFileName,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading asset {AssetName} from {Url}", assetName, downloadUrl);

        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var targetPath = Path.Combine(distDir, targetFileName);
        var tempTargetPath = targetPath + ".tmp";

        if (assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            assetName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var gzipStream = new GZipStream(contentStream, CompressionMode.Decompress);
            await using var tarReader = new TarReader(gzipStream);

            var extracted = false;
            while (await tarReader.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
            {
                if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                {
                    var entryFileName = Path.GetFileName(entry.Name);
                    // Match either exact target filename or general agent binary
                    if (string.Equals(entryFileName, targetFileName, StringComparison.OrdinalIgnoreCase) ||
                        entryFileName.StartsWith("controlplane-agent", StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(tempTargetPath)) File.Delete(tempTargetPath);
                        await entry.ExtractToFileAsync(tempTargetPath, overwrite: true, cancellationToken);
                        extracted = true;
                        break;
                    }
                }
            }

            if (!extracted)
            {
                throw new InvalidOperationException($"Archive {assetName} did not contain a recognizable binary for {targetFileName}");
            }
        }
        else if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            // Read zip to temp file or memory
            var tempZip = Path.Combine(Path.GetTempPath(), $"agent-download-{Guid.NewGuid()}.zip");
            try
            {
                await using (var fs = File.Create(tempZip))
                {
                    await response.Content.CopyToAsync(fs, cancellationToken);
                }

                using var zip = ZipFile.OpenRead(tempZip);
                var entry = zip.Entries.FirstOrDefault(e =>
                    string.Equals(e.Name, targetFileName, StringComparison.OrdinalIgnoreCase) ||
                    (e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                     e.Name.StartsWith("controlplane-agent", StringComparison.OrdinalIgnoreCase)));

                if (entry == null)
                {
                    throw new InvalidOperationException($"Zip archive {assetName} did not contain {targetFileName}");
                }

                if (File.Exists(tempTargetPath)) File.Delete(tempTargetPath);
                entry.ExtractToFile(tempTargetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempZip))
                {
                    try { File.Delete(tempZip); } catch { }
                }
            }
        }
        else
        {
            // Direct binary download
            await using (var fs = File.Create(tempTargetPath))
            {
                await response.Content.CopyToAsync(fs, cancellationToken);
            }
        }

        // Apply executable permissions on Unix systems
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(tempTargetPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not set unix execute permissions on {Path}", tempTargetPath);
            }
        }

        // Atomic replace
        File.Move(tempTargetPath, targetPath, overwrite: true);
        _logger.LogInformation("Successfully saved binary to {Path}", targetPath);
    }

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromMinutes(3);

        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ControlPlane-AgentBinarySync", "1.0"));
        }

        var token = _options.Value.GitHubToken ??
                    _configuration["ControlPlane:GitHubToken"] ??
                    Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        if (!string.IsNullOrWhiteSpace(token) && !client.DefaultRequestHeaders.Contains("Authorization"))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }

        return client;
    }
}
