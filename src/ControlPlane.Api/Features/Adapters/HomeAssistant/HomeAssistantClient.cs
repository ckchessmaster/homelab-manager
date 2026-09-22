using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Config;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.HomeAssistant;

public class HomeAssistantClient : IHomeAssistantClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HomeAssistantClient> _logger;

    public const string InsecureHttpClientName = "HomeAssistantInsecureClient";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HomeAssistantClient(IHttpClientFactory httpClientFactory, ILogger<HomeAssistantClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private HttpClient CreateClient(bool allowSelfSigned)
    {
        return allowSelfSigned
            ? _httpClientFactory.CreateClient(InsecureHttpClientName)
            : _httpClientFactory.CreateClient();
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static string FormatUrl(string baseUrl, string path)
    {
        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    public async Task<HomeAssistantTestResultDto> TestConnectionAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var client = CreateClient(allowSelfSignedCert);
            
            // 1. Try host info first
            var hostUrl = FormatUrl(baseUrl, "/api/hassio/host/info");
            using var hostReq = CreateRequest(HttpMethod.Get, hostUrl, token);
            using var hostResp = await client.SendAsync(hostReq, ct);

            string? hostname = null;
            string? osVersion = null;

            if (hostResp.IsSuccessStatusCode)
            {
                using var hostDoc = await JsonDocument.ParseAsync(await hostResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var root = hostDoc.RootElement;
                if (root.TryGetProperty("data", out var data))
                {
                    hostname = data.TryGetProperty("hostname", out var h) ? h.GetString() : null;
                    osVersion = data.TryGetProperty("operating_system", out var os) ? os.GetString() : null;
                }
            }

            // 2. Query Core & Supervisor info
            var coreUrl = FormatUrl(baseUrl, "/api/hassio/core/info");
            using var coreReq = CreateRequest(HttpMethod.Get, coreUrl, token);
            using var coreResp = await client.SendAsync(coreReq, ct);

            if (!coreResp.IsSuccessStatusCode)
            {
                // Fallback to /api/ for core-only installations
                var apiCoreUrl = FormatUrl(baseUrl, "/api/config");
                using var apiReq = CreateRequest(HttpMethod.Get, apiCoreUrl, token);
                using var apiResp = await client.SendAsync(apiReq, ct);

                if (!apiResp.IsSuccessStatusCode)
                {
                    return new HomeAssistantTestResultDto(
                        Success: false,
                        CoreVersion: null,
                        OsVersion: null,
                        SupervisorVersion: null,
                        Hostname: null,
                        UpdateAvailable: null,
                        LatencyMs: sw.ElapsedMilliseconds,
                        Message: $"Authentication or Endpoint Error: HTTP {(int)coreResp.StatusCode} {coreResp.ReasonPhrase}"
                    );
                }

                using var apiDoc = await JsonDocument.ParseAsync(await apiResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var apiRoot = apiDoc.RootElement;
                var coreVer = apiRoot.TryGetProperty("version", out var v) ? v.GetString() : null;
                var locationName = apiRoot.TryGetProperty("location_name", out var ln) ? ln.GetString() : null;

                return new HomeAssistantTestResultDto(
                    Success: true,
                    CoreVersion: coreVer,
                    OsVersion: "Core/Container (No HAOS)",
                    SupervisorVersion: null,
                    Hostname: locationName,
                    UpdateAvailable: false,
                    LatencyMs: sw.ElapsedMilliseconds,
                    Message: "Connected to Home Assistant Core successfully."
                );
            }

            string? coreVersion = null;
            bool? coreUpdateAvailable = null;
            using (var coreDoc = await JsonDocument.ParseAsync(await coreResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct))
            {
                if (coreDoc.RootElement.TryGetProperty("data", out var cData))
                {
                    coreVersion = cData.TryGetProperty("version", out var cv) ? cv.GetString() : null;
                    coreUpdateAvailable = cData.TryGetProperty("update_available", out var cu) ? cu.GetBoolean() : false;
                }
            }

            string? supervisorVersion = null;
            var supUrl = FormatUrl(baseUrl, "/api/hassio/supervisor/info");
            using var supReq = CreateRequest(HttpMethod.Get, supUrl, token);
            using var supResp = await client.SendAsync(supReq, ct);
            if (supResp.IsSuccessStatusCode)
            {
                using var supDoc = await JsonDocument.ParseAsync(await supResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (supDoc.RootElement.TryGetProperty("data", out var sData))
                {
                    supervisorVersion = sData.TryGetProperty("version", out var sv) ? sv.GetString() : null;
                }
            }

            return new HomeAssistantTestResultDto(
                Success: true,
                CoreVersion: coreVersion,
                OsVersion: osVersion ?? "Home Assistant OS",
                SupervisorVersion: supervisorVersion,
                Hostname: hostname,
                UpdateAvailable: coreUpdateAvailable,
                LatencyMs: sw.ElapsedMilliseconds,
                Message: "Connected to Home Assistant OS successfully."
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Home Assistant at {BaseUrl}", baseUrl);
            return new HomeAssistantTestResultDto(
                Success: false,
                CoreVersion: null,
                OsVersion: null,
                SupervisorVersion: null,
                Hostname: null,
                UpdateAvailable: null,
                LatencyMs: sw.ElapsedMilliseconds,
                Message: ex.Message
            );
        }
    }

    public async Task<HomeAssistantHostInfoDto?> GetHostInfoAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(allowSelfSignedCert);
            var url = FormatUrl(baseUrl, "/api/hassio/host/info");
            using var req = CreateRequest(HttpMethod.Get, url, token);
            using var resp = await client.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;

            return new HomeAssistantHostInfoDto(
                Chassis: data.TryGetProperty("chassis", out var ch) ? ch.GetString() : null,
                Hostname: data.TryGetProperty("hostname", out var hn) ? hn.GetString() : null,
                Kernel: data.TryGetProperty("kernel", out var k) ? k.GetString() : null,
                OperatingSystem: data.TryGetProperty("operating_system", out var os) ? os.GetString() : null,
                RebootRequired: data.TryGetProperty("reboot_required", out var rr) && rr.GetBoolean(),
                DiskFreeGb: data.TryGetProperty("disk_free", out var df) ? df.GetDouble() : null,
                DiskTotalGb: data.TryGetProperty("disk_total", out var dt) ? dt.GetDouble() : null,
                DiskUsedGb: data.TryGetProperty("disk_used", out var du) ? du.GetDouble() : null
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error querying Home Assistant host info from {BaseUrl}", baseUrl);
            return null;
        }
    }

    public async Task<HomeAssistantOsInfoDto?> GetOsInfoAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(allowSelfSignedCert);
            var url = FormatUrl(baseUrl, "/api/hassio/os/info");
            using var req = CreateRequest(HttpMethod.Get, url, token);
            using var resp = await client.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;

            return new HomeAssistantOsInfoDto(
                Version: data.TryGetProperty("version", out var v) ? v.GetString() ?? "unknown" : "unknown",
                VersionLatest: data.TryGetProperty("version_latest", out var vl) ? vl.GetString() : null,
                UpdateAvailable: data.TryGetProperty("update_available", out var ua) && ua.GetBoolean(),
                Board: data.TryGetProperty("board", out var b) ? b.GetString() : null,
                BootSlot: data.TryGetProperty("boot", out var bs) ? bs.GetString() : null
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error querying Home Assistant OS info from {BaseUrl}", baseUrl);
            return null;
        }
    }

    public async Task<HomeAssistantCoreInfoDto?> GetCoreInfoAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(allowSelfSignedCert);
            var url = FormatUrl(baseUrl, "/api/hassio/core/info");
            using var req = CreateRequest(HttpMethod.Get, url, token);
            using var resp = await client.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;

            return new HomeAssistantCoreInfoDto(
                Version: data.TryGetProperty("version", out var v) ? v.GetString() ?? "unknown" : "unknown",
                VersionLatest: data.TryGetProperty("version_latest", out var vl) ? vl.GetString() : null,
                UpdateAvailable: data.TryGetProperty("update_available", out var ua) && ua.GetBoolean(),
                Arch: data.TryGetProperty("arch", out var a) ? a.GetString() : null,
                State: data.TryGetProperty("state", out var s) ? s.GetString() : null
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error querying Home Assistant Core info from {BaseUrl}", baseUrl);
            return null;
        }
    }

    public async Task<HomeAssistantSupervisorInfoDto?> GetSupervisorInfoAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(allowSelfSignedCert);
            var url = FormatUrl(baseUrl, "/api/hassio/supervisor/info");
            using var req = CreateRequest(HttpMethod.Get, url, token);
            using var resp = await client.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;

            return new HomeAssistantSupervisorInfoDto(
                Version: data.TryGetProperty("version", out var v) ? v.GetString() ?? "unknown" : "unknown",
                VersionLatest: data.TryGetProperty("version_latest", out var vl) ? vl.GetString() : null,
                UpdateAvailable: data.TryGetProperty("update_available", out var ua) && ua.GetBoolean(),
                Channel: data.TryGetProperty("channel", out var ch) ? ch.GetString() : null,
                Healthy: data.TryGetProperty("healthy", out var h) && h.GetBoolean(),
                Supported: data.TryGetProperty("supported", out var sp) && sp.GetBoolean()
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error querying Home Assistant Supervisor info from {BaseUrl}", baseUrl);
            return null;
        }
    }

    public async Task<List<HomeAssistantBackupDto>> ListBackupsAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(allowSelfSignedCert);
            var url = FormatUrl(baseUrl, "/api/hassio/backups");
            using var req = CreateRequest(HttpMethod.Get, url, token);
            using var resp = await client.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode) return new List<HomeAssistantBackupDto>();

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("backups", out var backupsArray))
            {
                return new List<HomeAssistantBackupDto>();
            }

            var list = new List<HomeAssistantBackupDto>();
            foreach (var b in backupsArray.EnumerateArray())
            {
                var slug = b.TryGetProperty("slug", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                var name = b.TryGetProperty("name", out var n) ? n.GetString() ?? slug : slug;
                var dateStr = b.TryGetProperty("date", out var d) ? d.GetString() : null;
                var type = b.TryGetProperty("type", out var t) ? t.GetString() ?? "full" : "full";
                var size = b.TryGetProperty("size", out var sz) ? sz.GetDouble() : 0.0;
                var isProtected = b.TryGetProperty("protected", out var p) && p.GetBoolean();

                var date = DateTimeOffset.TryParse(dateStr, out var parsedDate) ? parsedDate : DateTimeOffset.UtcNow;

                list.Add(new HomeAssistantBackupDto(slug, name, date, type, size, isProtected));
            }

            return list.OrderByDescending(x => x.Date).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error querying backups from Home Assistant at {BaseUrl}", baseUrl);
            return new List<HomeAssistantBackupDto>();
        }
    }

    public async Task<HomeAssistantConfigCheckResultDto> CheckCoreConfigAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(allowSelfSignedCert);
            var url = FormatUrl(baseUrl, "/api/hassio/core/check");
            using var req = CreateRequest(HttpMethod.Post, url, token);
            using var resp = await client.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode)
            {
                return new HomeAssistantConfigCheckResultDto(false, $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}", DateTimeOffset.UtcNow);
            }

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            var result = root.TryGetProperty("result", out var r) ? r.GetString() : null;

            if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                string? errors = null;
                if (root.TryGetProperty("data", out var data))
                {
                    if (data.TryGetProperty("errors", out var err) && err.ValueKind == JsonValueKind.String)
                    {
                        errors = err.GetString();
                    }
                    var checkRes = data.TryGetProperty("result", out var cr) ? cr.GetString() : null;
                    if (string.Equals(checkRes, "fatal", StringComparison.OrdinalIgnoreCase))
                    {
                        return new HomeAssistantConfigCheckResultDto(false, errors ?? "Configuration validation failed fatal check.", DateTimeOffset.UtcNow);
                    }
                }

                return new HomeAssistantConfigCheckResultDto(string.IsNullOrWhiteSpace(errors), errors, DateTimeOffset.UtcNow);
            }

            var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "Config check failed";
            return new HomeAssistantConfigCheckResultDto(false, msg, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking Home Assistant Core configuration at {BaseUrl}", baseUrl);
            return new HomeAssistantConfigCheckResultDto(false, ex.Message, DateTimeOffset.UtcNow);
        }
    }

    public async Task<bool> RebootHostAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(allowSelfSignedCert);
            var url = FormatUrl(baseUrl, "/api/hassio/host/reboot");
            using var req = CreateRequest(HttpMethod.Post, url, token);
            using var resp = await client.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger host reboot for Home Assistant at {BaseUrl}", baseUrl);
            return false;
        }
    }

    public async Task<bool> RestartCoreAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(allowSelfSignedCert);
            var url = FormatUrl(baseUrl, "/api/hassio/core/restart");
            using var req = CreateRequest(HttpMethod.Post, url, token);
            using var resp = await client.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger core restart for Home Assistant at {BaseUrl}", baseUrl);
            return false;
        }
    }

    public async Task<bool> UpdateOsAsync(
        string baseUrl,
        string token,
        bool allowSelfSignedCert,
        CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(allowSelfSignedCert);
            var url = FormatUrl(baseUrl, "/api/hassio/os/update");
            using var req = CreateRequest(HttpMethod.Post, url, token);
            using var resp = await client.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger OS update for Home Assistant at {BaseUrl}", baseUrl);
            return false;
        }
    }

    public async Task<CreateHomeAssistantBackupResponse> CreateBackupAsync(
        string baseUrl,
        string token,
        string? name,
        string? password,
        bool allowSelfSignedCert,
        CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(allowSelfSignedCert);
            var url = FormatUrl(baseUrl, "/api/hassio/backups/new/full");
            using var req = CreateRequest(HttpMethod.Post, url, token);

            var payload = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(name))
            {
                payload["name"] = name;
            }
            if (!string.IsNullOrWhiteSpace(password))
            {
                payload["password"] = password;
            }

            req.Content = JsonContent.Create(payload);
            using var resp = await client.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode)
            {
                return new CreateHomeAssistantBackupResponse(false, null, null, $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
            }

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            string? jobId = null;
            string? slug = null;

            if (root.TryGetProperty("data", out var data))
            {
                jobId = data.TryGetProperty("job_id", out var j) ? j.GetString() : null;
                slug = data.TryGetProperty("slug", out var s) ? s.GetString() : null;
            }

            return new CreateHomeAssistantBackupResponse(true, jobId, slug, "Backup initiated successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backup for Home Assistant at {BaseUrl}", baseUrl);
            return new CreateHomeAssistantBackupResponse(false, null, null, ex.Message);
        }
    }
}
