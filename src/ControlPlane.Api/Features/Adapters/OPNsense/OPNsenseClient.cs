using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Config;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.OPNsense;

public class OPNsenseClient : IOPNsenseClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OPNsenseClient> _logger;

    public const string InsecureHttpClientName = "OPNsenseInsecureClient";

    public OPNsenseClient(IHttpClientFactory httpClientFactory, ILogger<OPNsenseClient> logger)
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

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string apiKey, string apiSecret)
    {
        var request = new HttpRequestMessage(method, url);
        var authHeaderValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeaderValue);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    public async Task<OPNsenseTestResultDto> TestConnectionAsync(
        string baseUrl,
        string apiKey,
        string apiSecret,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var client = CreateClient(allowSelfSigned);
            var url = FormatUrl(baseUrl, "/api/core/system/status");

            using var request = CreateRequest(HttpMethod.Get, url, apiKey, apiSecret);
            using var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                // Fallback to /api/core/firmware/status
                var fwUrl = FormatUrl(baseUrl, "/api/core/firmware/status");
                using var fwRequest = CreateRequest(HttpMethod.Get, fwUrl, apiKey, apiSecret);
                using var fwResponse = await client.SendAsync(fwRequest, ct);

                if (!fwResponse.IsSuccessStatusCode)
                {
                    return new OPNsenseTestResultDto(
                        Success: false,
                        Hostname: null,
                        Version: null,
                        Status: "Authentication or Endpoint Error",
                        LatencyMs: sw.ElapsedMilliseconds,
                        Message: $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
                    );
                }

                using var fwDoc = await JsonDocument.ParseAsync(await fwResponse.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var fwRoot = fwDoc.RootElement;
                var fwVer = fwRoot.TryGetProperty("product_version", out var pv) ? pv.GetString() : "OPNsense";

                return new OPNsenseTestResultDto(
                    Success: true,
                    Hostname: ParseHostFromUrl(baseUrl),
                    Version: fwVer,
                    Status: "Online",
                    LatencyMs: sw.ElapsedMilliseconds,
                    Message: "Connected successfully."
                );
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            string? hostname = null;
            if (root.TryGetProperty("hostname", out var h))
            {
                hostname = h.GetString();
            }
            if (string.IsNullOrWhiteSpace(hostname))
            {
                hostname = ParseHostFromUrl(baseUrl);
            }

            string? version = null;
            if (root.TryGetProperty("product_version", out var v))
            {
                version = v.GetString();
            }
            else if (root.TryGetProperty("version", out var v2))
            {
                version = v2.GetString();
            }

            var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "Online" : "Online";

            return new OPNsenseTestResultDto(
                Success: true,
                Hostname: hostname,
                Version: version ?? "OPNsense",
                Status: status,
                LatencyMs: sw.ElapsedMilliseconds,
                Message: "Connected successfully."
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to OPNsense firewall at {BaseUrl}", baseUrl);
            return new OPNsenseTestResultDto(
                Success: false,
                Hostname: null,
                Version: null,
                Status: "Unreachable",
                LatencyMs: sw.ElapsedMilliseconds,
                Message: ex.Message
            );
        }
    }

    public async Task<OPNsenseTelemetryResponse> GetTelemetryAsync(
        string baseUrl,
        string apiKey,
        string apiSecret,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        var hostname = ParseHostFromUrl(baseUrl);
        var version = "OPNsense";
        var status = "Online";

        try
        {
            var test = await TestConnectionAsync(baseUrl, apiKey, apiSecret, allowSelfSigned, ct);
            if (test.Success)
            {
                hostname = test.Hostname ?? hostname;
                version = test.Version ?? version;
                status = test.Status ?? status;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query system status in telemetry collection.");
        }

        var gatewaysTask = GetGatewaysAsync(baseUrl, apiKey, apiSecret, allowSelfSigned, ct);
        var interfacesTask = GetInterfacesAsync(baseUrl, apiKey, apiSecret, allowSelfSigned, ct);
        var servicesTask = GetServicesAsync(baseUrl, apiKey, apiSecret, allowSelfSigned, ct);

        await Task.WhenAll(gatewaysTask, interfacesTask, servicesTask);

        return new OPNsenseTelemetryResponse(
            Hostname: hostname,
            Version: version,
            Status: status,
            Gateways: await gatewaysTask,
            Interfaces: await interfacesTask,
            Services: await servicesTask,
            Timestamp: DateTimeOffset.UtcNow
        );
    }

    public async Task<List<OPNsenseGatewayStatus>> GetGatewaysAsync(
        string baseUrl,
        string apiKey,
        string apiSecret,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        var result = new List<OPNsenseGatewayStatus>();
        try
        {
            var client = CreateClient(allowSelfSigned);
            var url = FormatUrl(baseUrl, "/api/routes/gateway/status");

            using var request = CreateRequest(HttpMethod.Get, url, apiKey, apiSecret);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("OPNsense gateway status endpoint returned {StatusCode}", response.StatusCode);
                return result;
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            JsonElement itemsElement;
            if (root.TryGetProperty("items", out var it))
            {
                itemsElement = it;
            }
            else
            {
                itemsElement = root;
            }

            if (itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsElement.EnumerateArray())
                {
                    var gw = ParseGatewayElement(item);
                    if (gw != null) result.Add(gw);
                }
            }
            else if (itemsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in itemsElement.EnumerateObject())
                {
                    var gw = ParseGatewayElement(prop.Value, prop.Name);
                    if (gw != null) result.Add(gw);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve OPNsense gateways from {BaseUrl}", baseUrl);
        }

        return result;
    }

    private static OPNsenseGatewayStatus? ParseGatewayElement(JsonElement item, string? fallbackName = null)
    {
        var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? fallbackName : fallbackName;
        if (string.IsNullOrWhiteSpace(name)) return null;

        var iface = item.TryGetProperty("interface", out var ifc) ? ifc.GetString() ?? "unknown" : "unknown";
        var status = item.TryGetProperty("status", out var st) ? st.GetString() ?? "Online" : "Online";
        var address = item.TryGetProperty("address", out var addr) ? addr.GetString() : null;

        double? delay = null;
        if (item.TryGetProperty("delay", out var d))
        {
            if (d.ValueKind == JsonValueKind.Number)
            {
                delay = d.GetDouble();
            }
            else if (d.ValueKind == JsonValueKind.String)
            {
                var clean = d.GetString()?.Replace("ms", "").Trim();
                if (double.TryParse(clean, out var val)) delay = val;
            }
        }

        double? loss = null;
        if (item.TryGetProperty("loss", out var l))
        {
            if (l.ValueKind == JsonValueKind.Number)
            {
                loss = l.GetDouble();
            }
            else if (l.ValueKind == JsonValueKind.String)
            {
                var clean = l.GetString()?.Replace("%", "").Trim();
                if (double.TryParse(clean, out var val)) loss = val;
            }
        }

        return new OPNsenseGatewayStatus(name, iface, status, delay, loss, address);
    }

    public async Task<List<OPNsenseInterfaceInfo>> GetInterfacesAsync(
        string baseUrl,
        string apiKey,
        string apiSecret,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        var result = new List<OPNsenseInterfaceInfo>();
        try
        {
            var client = CreateClient(allowSelfSigned);
            var url = FormatUrl(baseUrl, "/api/interfaces/overview/interfacesInfo");

            using var request = CreateRequest(HttpMethod.Get, url, apiKey, apiSecret);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return result;
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    var item = prop.Value;
                    var name = prop.Name;
                    var device = item.TryGetProperty("device", out var d) ? d.GetString() ?? name : name;
                    var status = item.TryGetProperty("status", out var s) ? s.GetString() ?? "up" : "up";
                    var media = item.TryGetProperty("media", out var m) ? m.GetString() : null;

                    string? ip = null;
                    if (item.TryGetProperty("ipv4", out var ipv4) && ipv4.ValueKind == JsonValueKind.Array)
                    {
                        var first = ipv4.EnumerateArray().FirstOrDefault();
                        if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("ipaddr", out var ipAddr))
                        {
                            ip = ipAddr.GetString();
                        }
                    }
                    else if (item.TryGetProperty("ipaddr", out var directIp))
                    {
                        ip = directIp.GetString();
                    }

                    result.Add(new OPNsenseInterfaceInfo(name, device, ip, status, media));
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "eth" : "eth";
                    var device = item.TryGetProperty("device", out var d) ? d.GetString() ?? name : name;
                    var status = item.TryGetProperty("status", out var s) ? s.GetString() ?? "up" : "up";
                    var ip = item.TryGetProperty("ipaddr", out var ipProp) ? ipProp.GetString() : null;
                    var media = item.TryGetProperty("media", out var m) ? m.GetString() : null;
                    result.Add(new OPNsenseInterfaceInfo(name, device, ip, status, media));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve OPNsense interfaces from {BaseUrl}", baseUrl);
        }

        return result;
    }

    public async Task<List<OPNsenseServiceItem>> GetServicesAsync(
        string baseUrl,
        string apiKey,
        string apiSecret,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        var result = new List<OPNsenseServiceItem>();
        try
        {
            var client = CreateClient(allowSelfSigned);
            var url = FormatUrl(baseUrl, "/api/core/service/search");

            using var request = CreateRequest(HttpMethod.Get, url, apiKey, apiSecret);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return result;
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            JsonElement rows;
            if (root.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array)
            {
                rows = r;
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                rows = root;
            }
            else
            {
                return result;
            }

            foreach (var item in rows.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? id : id;
                var desc = item.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? name : name;

                bool running = false;
                if (item.TryGetProperty("running", out var runProp))
                {
                    if (runProp.ValueKind == JsonValueKind.Number) running = runProp.GetInt32() == 1;
                    else if (runProp.ValueKind == JsonValueKind.True) running = true;
                    else if (runProp.ValueKind == JsonValueKind.String) running = runProp.GetString() == "1" || runProp.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
                }

                bool enabled = true;
                if (item.TryGetProperty("enabled", out var enProp))
                {
                    if (enProp.ValueKind == JsonValueKind.Number) enabled = enProp.GetInt32() == 1;
                    else if (enProp.ValueKind == JsonValueKind.False) enabled = false;
                    else if (enProp.ValueKind == JsonValueKind.String) enabled = enProp.GetString() == "1" || enProp.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
                }

                result.Add(new OPNsenseServiceItem(id, name, desc, running, enabled));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve OPNsense services from {BaseUrl}", baseUrl);
        }

        return result;
    }

    public async Task<OPNsenseServiceActionResult> RestartServiceAsync(
        string baseUrl,
        string apiKey,
        string apiSecret,
        string serviceName,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Issuing service restart for '{ServiceName}' on OPNsense at {BaseUrl}", serviceName, baseUrl);
        try
        {
            var client = CreateClient(allowSelfSigned);
            var url = FormatUrl(baseUrl, $"/api/core/service/restart/{Uri.EscapeDataString(serviceName)}");

            using var request = CreateRequest(HttpMethod.Post, url, apiKey, apiSecret);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return new OPNsenseServiceActionResult(true, $"Service '{serviceName}' restarted successfully.");
            }

            // Attempt reconfigure fallback
            var reconfUrl = FormatUrl(baseUrl, $"/api/core/service/reconfigure/{Uri.EscapeDataString(serviceName)}");
            using var reconfRequest = CreateRequest(HttpMethod.Post, reconfUrl, apiKey, apiSecret);
            reconfRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using var reconfResponse = await client.SendAsync(reconfRequest, ct);
            if (reconfResponse.IsSuccessStatusCode)
            {
                return new OPNsenseServiceActionResult(true, $"Service '{serviceName}' reconfigured and started successfully.");
            }

            return new OPNsenseServiceActionResult(false, $"Service restart returned HTTP {response.StatusCode}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restart service '{ServiceName}' on {BaseUrl}", serviceName, baseUrl);
            return new OPNsenseServiceActionResult(false, ex.Message);
        }
    }

    public async Task<List<OPNsenseDhcpLease>> GetDhcpLeasesAsync(
        string baseUrl,
        string apiKey,
        string apiSecret,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        var result = new List<OPNsenseDhcpLease>();
        try
        {
            var client = CreateClient(allowSelfSigned);
            // 1. Try Dnsmasq / ISC DHCP lease search
            var url = FormatUrl(baseUrl, "/api/diagnostics/dhcp/searchLeases");

            using var request = CreateRequest(HttpMethod.Get, url, apiKey, apiSecret);
            using var response = await client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var root = doc.RootElement;
                JsonElement rows;
                if (root.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array)
                {
                    rows = r;
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    rows = root;
                }
                else
                {
                    rows = default;
                }

                if (rows.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in rows.EnumerateArray())
                    {
                        var ip = item.TryGetProperty("ip", out var ipProp) ? ipProp.GetString() ?? string.Empty : string.Empty;
                        var mac = item.TryGetProperty("mac", out var macProp) ? macProp.GetString() ?? string.Empty : string.Empty;
                        var hostname = item.TryGetProperty("hostname", out var hProp) ? hProp.GetString() : null;
                        var starts = item.TryGetProperty("starts", out var sProp) ? sProp.GetString() : null;
                        var ends = item.TryGetProperty("ends", out var eProp) ? eProp.GetString() : null;
                        var status = item.TryGetProperty("status", out var stProp) ? stProp.GetString() ?? "active" : "active";

                        if (!string.IsNullOrWhiteSpace(ip) && !string.IsNullOrWhiteSpace(mac))
                        {
                            result.Add(new OPNsenseDhcpLease(ip, mac, hostname, starts, ends, status));
                        }
                    }
                }
            }

            // 2. If no leases found, try Kea DHCP search
            if (result.Count == 0)
            {
                var keaUrl = FormatUrl(baseUrl, "/api/kea/dhcpv4/searchLease");
                using var keaRequest = CreateRequest(HttpMethod.Get, keaUrl, apiKey, apiSecret);
                using var keaResponse = await client.SendAsync(keaRequest, ct);
                if (keaResponse.IsSuccessStatusCode)
                {
                    using var keaDoc = await JsonDocument.ParseAsync(await keaResponse.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                    var keaRoot = keaDoc.RootElement;
                    if (keaRoot.TryGetProperty("rows", out var keaRows) && keaRows.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in keaRows.EnumerateArray())
                        {
                            var ip = item.TryGetProperty("ip-address", out var ipProp) ? ipProp.GetString() ?? string.Empty : string.Empty;
                            var mac = item.TryGetProperty("hw-address", out var macProp) ? macProp.GetString() ?? string.Empty : string.Empty;
                            var hostname = item.TryGetProperty("hostname", out var hProp) ? hProp.GetString() : null;
                            var status = item.TryGetProperty("state", out var stProp) ? stProp.GetString() ?? "active" : "active";

                            if (!string.IsNullOrWhiteSpace(ip) && !string.IsNullOrWhiteSpace(mac))
                            {
                                result.Add(new OPNsenseDhcpLease(ip, mac, hostname, null, null, status));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve OPNsense DHCP leases from {BaseUrl}", baseUrl);
        }

        return result;
    }

    public async Task<OPNsenseFirmwareInfo> GetFirmwareStatusAsync(
        string baseUrl,
        string apiKey,
        string apiSecret,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(allowSelfSigned);
            var url = FormatUrl(baseUrl, "/api/core/firmware/status");

            using var request = CreateRequest(HttpMethod.Get, url, apiKey, apiSecret);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return new OPNsenseFirmwareInfo("Unknown", $"HTTP {response.StatusCode}", 0, null, null);
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            var version = root.TryGetProperty("product_version", out var v) ? v.GetString() ?? "Unknown" : "Unknown";
            var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "OK" : "OK";
            var lastCheck = root.TryGetProperty("last_check", out var lc) ? lc.GetString() : null;

            var packages = new List<string>();
            if (root.TryGetProperty("all_packages", out var pkgs) && pkgs.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in pkgs.EnumerateArray())
                {
                    if (p.TryGetProperty("name", out var pn) && pn.GetString() is { } name)
                    {
                        packages.Add(name);
                    }
                }
            }

            int updatesAvailable = 0;
            if (root.TryGetProperty("new_packages", out var np) && np.ValueKind == JsonValueKind.Array)
            {
                updatesAvailable = np.GetArrayLength();
            }

            return new OPNsenseFirmwareInfo(version, status, updatesAvailable, packages, lastCheck);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check OPNsense firmware status on {BaseUrl}", baseUrl);
            return new OPNsenseFirmwareInfo("Unknown", ex.Message, 0, null, null);
        }
    }

    private static string FormatUrl(string baseUrl, string path)
    {
        var cleanedBase = baseUrl.Trim().TrimEnd('/');
        if (!cleanedBase.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !cleanedBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            cleanedBase = $"https://{cleanedBase}";
        }

        return $"{cleanedBase}{path}";
    }

    private static string ParseHostFromUrl(string baseUrl)
    {
        try
        {
            var cleaned = baseUrl.Trim();
            if (!cleaned.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !cleaned.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = $"https://{cleaned}";
            }

            if (Uri.TryCreate(cleaned, UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }
        }
        catch
        {
            // ignore
        }

        return baseUrl;
    }
}
