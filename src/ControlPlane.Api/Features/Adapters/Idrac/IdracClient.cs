using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Config;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.Idrac;

public class IdracClient : IIdracClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IdracClient> _logger;

    public const string InsecureHttpClientName = "IdracInsecureClient";

    public IdracClient(IHttpClientFactory httpClientFactory, ILogger<IdracClient> logger)
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

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string username, string password)
    {
        var request = new HttpRequestMessage(method, url);
        var authHeaderValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeaderValue);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    public async Task<IdracTestResultDto> TestConnectionAsync(
        string bmcUrl,
        string username,
        string password,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var client = CreateClient(allowSelfSigned);
            var url = await ResolveSystemUrlAsync(client, bmcUrl, username, password, ct);

            using var request = CreateRequest(HttpMethod.Get, url, username, password);
            using var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                return new IdracTestResultDto(
                    Success: false,
                    PowerState: "Unknown",
                    Model: null,
                    BiosVersion: null,
                    HealthStatus: null,
                    SerialNumber: null,
                    LatencyMs: sw.ElapsedMilliseconds,
                    Message: $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
                );
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            var powerState = root.TryGetProperty("PowerState", out var ps) ? ps.GetString() ?? "Unknown" : "Unknown";
            var model = root.TryGetProperty("Model", out var m) ? m.GetString() : null;
            var biosVersion = root.TryGetProperty("BiosVersion", out var bv) ? bv.GetString() : null;
            var serialNumber = root.TryGetProperty("SerialNumber", out var sn) ? sn.GetString() : null;

            string? health = null;
            if (root.TryGetProperty("Status", out var status) && status.TryGetProperty("Health", out var h))
            {
                health = h.GetString();
            }

            return new IdracTestResultDto(
                Success: true,
                PowerState: powerState,
                Model: model,
                BiosVersion: biosVersion,
                HealthStatus: health ?? "OK",
                SerialNumber: serialNumber,
                LatencyMs: sw.ElapsedMilliseconds,
                Message: "Connected to BMC successfully."
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to BMC at {BmcUrl}", bmcUrl);
            return new IdracTestResultDto(
                Success: false,
                PowerState: "Offline",
                Model: null,
                BiosVersion: null,
                HealthStatus: null,
                SerialNumber: null,
                LatencyMs: sw.ElapsedMilliseconds,
                Message: ex.Message
            );
        }
    }

    public async Task<IdracVitalsDto> GetVitalsAsync(
        string bmcUrl,
        string username,
        string password,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        var client = CreateClient(allowSelfSigned);
        var sysUrl = await ResolveSystemUrlAsync(client, bmcUrl, username, password, ct);

        string powerState = "Unknown";
        string? health = "OK";
        string? model = null;
        string? biosVersion = null;
        string? serialNumber = null;

        try
        {
            using var sysReq = CreateRequest(HttpMethod.Get, sysUrl, username, password);
            using var sysRes = await client.SendAsync(sysReq, ct);
            if (sysRes.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await sysRes.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var root = doc.RootElement;
                powerState = root.TryGetProperty("PowerState", out var ps) ? ps.GetString() ?? "Unknown" : "Unknown";
                model = root.TryGetProperty("Model", out var m) ? m.GetString() : null;
                biosVersion = root.TryGetProperty("BiosVersion", out var bv) ? bv.GetString() : null;
                serialNumber = root.TryGetProperty("SerialNumber", out var sn) ? sn.GetString() : null;

                if (root.TryGetProperty("Status", out var status) && status.TryGetProperty("Health", out var h))
                {
                    health = h.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading system info from {BmcUrl}", bmcUrl);
        }

        var temperatures = new List<IdracSensorReading>();
        var fans = new List<IdracFanReading>();

        try
        {
            var thermalUrl = await ResolveChassisUrlAsync(client, bmcUrl, username, password, "Thermal", ct);
            using var thermReq = CreateRequest(HttpMethod.Get, thermalUrl, username, password);
            using var thermRes = await client.SendAsync(thermReq, ct);
            if (thermRes.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await thermRes.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var root = doc.RootElement;

                if (root.TryGetProperty("Temperatures", out var temps) && temps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var temp in temps.EnumerateArray())
                    {
                        var name = temp.TryGetProperty("Name", out var n) ? n.GetString() ?? "Sensor" : "Sensor";
                        var reading = temp.TryGetProperty("ReadingCelsius", out var r) ? r.GetDouble() : 0.0;
                        double? critical = temp.TryGetProperty("UpperThresholdCritical", out var c) ? c.GetDouble() : null;
                        var sensorStatus = "OK";
                        if (temp.TryGetProperty("Status", out var s) && s.TryGetProperty("Health", out var sh))
                        {
                            sensorStatus = sh.GetString() ?? "OK";
                        }

                        temperatures.Add(new IdracSensorReading(name, reading, critical, sensorStatus));
                    }
                }

                if (root.TryGetProperty("Fans", out var fansArray) && fansArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var fan in fansArray.EnumerateArray())
                    {
                        var name = fan.TryGetProperty("FanName", out var fn) ? fn.GetString() ?? "Fan"
                            : (fan.TryGetProperty("Name", out var fn2) ? fn2.GetString() ?? "Fan" : "Fan");
                        var rpm = fan.TryGetProperty("Reading", out var fr) ? fr.GetInt32()
                            : (fan.TryGetProperty("ReadingRpm", out var fr2) ? fr2.GetInt32() : 0);
                        var fanStatus = "OK";
                        if (fan.TryGetProperty("Status", out var fs) && fs.TryGetProperty("Health", out var fsh))
                        {
                            fanStatus = fsh.GetString() ?? "OK";
                        }

                        fans.Add(new IdracFanReading(name, rpm, fanStatus));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading thermal vitals from {BmcUrl}", bmcUrl);
        }

        double? powerWatts = null;
        try
        {
            var powerUrl = await ResolveChassisUrlAsync(client, bmcUrl, username, password, "Power", ct);
            using var powReq = CreateRequest(HttpMethod.Get, powerUrl, username, password);
            using var powRes = await client.SendAsync(powReq, ct);
            if (powRes.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await powRes.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var root = doc.RootElement;
                if (root.TryGetProperty("PowerControl", out var pc) && pc.ValueKind == JsonValueKind.Array)
                {
                    var first = pc.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("PowerConsumedWatts", out var pw))
                    {
                        powerWatts = pw.GetDouble();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading power consumption from {BmcUrl}", bmcUrl);
        }

        return new IdracVitalsDto(
            PowerState: powerState,
            HealthStatus: health,
            Model: model,
            BiosVersion: biosVersion,
            SerialNumber: serialNumber,
            PowerConsumptionWatts: powerWatts,
            Temperatures: temperatures,
            Fans: fans
        );
    }

    public async Task<IdracPowerControlResponse> ResetSystemAsync(
        string bmcUrl,
        string username,
        string password,
        string resetType,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Issuing BMC power action '{ResetType}' to {BmcUrl}...", resetType, bmcUrl);

        var client = CreateClient(allowSelfSigned);
        var actionUrl = await ResolveResetActionUrlAsync(client, bmcUrl, username, password, ct);

        using var request = CreateRequest(HttpMethod.Post, actionUrl, username, password);
        request.Content = JsonContent.Create(
            new { ResetType = NormalizeResetType(resetType) },
            options: new JsonSerializerOptions { PropertyNamingPolicy = null });

        using var response = await client.SendAsync(request, ct);
        var success = response.IsSuccessStatusCode;
        var message = success
            ? $"Power action '{resetType}' sent successfully."
            : $"Power action '{resetType}' failed with HTTP {(int)response.StatusCode}.";

        return new IdracPowerControlResponse(success, message);
    }

    private static string NormalizeResetType(string resetType)
    {
        return resetType.ToLowerInvariant() switch
        {
            "on" or "poweron" => "On",
            "forceoff" or "off" => "ForceOff",
            "gracefulshutdown" or "shutdown" => "GracefulShutdown",
            "powercycle" or "cycle" => "PowerCycle",
            "forcerestart" or "reset" => "ForceRestart",
            _ => resetType
        };
    }

    private static async Task<string> ResolveSystemUrlAsync(HttpClient client, string bmcUrl, string username, string password, CancellationToken ct)
    {
        // Try Dell embedded system standard first
        var dellUrl = FormatUrl(bmcUrl, "/redfish/v1/Systems/System.Embedded.1");
        try
        {
            using var req = CreateRequest(HttpMethod.Get, dellUrl, username, password);
            using var res = await client.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return dellUrl;
        }
        catch { }

        // Fallback: query /redfish/v1/Systems collection
        var rootSystemsUrl = FormatUrl(bmcUrl, "/redfish/v1/Systems");
        try
        {
            using var req = CreateRequest(HttpMethod.Get, rootSystemsUrl, username, password);
            using var res = await client.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("Members", out var members) && members.ValueKind == JsonValueKind.Array)
                {
                    var first = members.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("@odata.id", out var odataId))
                    {
                        var path = odataId.GetString();
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            return FormatUrl(bmcUrl, path);
                        }
                    }
                }
            }
        }
        catch { }

        return dellUrl;
    }

    private static async Task<string> ResolveChassisUrlAsync(HttpClient client, string bmcUrl, string username, string password, string subPath, CancellationToken ct)
    {
        var dellUrl = FormatUrl(bmcUrl, $"/redfish/v1/Chassis/System.Embedded.1/{subPath}");
        try
        {
            using var req = CreateRequest(HttpMethod.Get, dellUrl, username, password);
            using var res = await client.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return dellUrl;
        }
        catch { }

        var rootChassisUrl = FormatUrl(bmcUrl, "/redfish/v1/Chassis");
        try
        {
            using var req = CreateRequest(HttpMethod.Get, rootChassisUrl, username, password);
            using var res = await client.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("Members", out var members) && members.ValueKind == JsonValueKind.Array)
                {
                    var first = members.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("@odata.id", out var odataId))
                    {
                        var path = odataId.GetString();
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            return FormatUrl(bmcUrl, $"{path.TrimEnd('/')}/{subPath}");
                        }
                    }
                }
            }
        }
        catch { }

        return dellUrl;
    }

    private static async Task<string> ResolveResetActionUrlAsync(HttpClient client, string bmcUrl, string username, string password, CancellationToken ct)
    {
        var dellUrl = FormatUrl(bmcUrl, "/redfish/v1/Systems/System.Embedded.1/Actions/ComputerSystem.Reset");
        try
        {
            var sysUrl = await ResolveSystemUrlAsync(client, bmcUrl, username, password, ct);
            using var req = CreateRequest(HttpMethod.Get, sysUrl, username, password);
            using var res = await client.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("Actions", out var actions) &&
                    actions.TryGetProperty("#ComputerSystem.Reset", out var resetAction) &&
                    resetAction.TryGetProperty("target", out var target))
                {
                    var actionTarget = target.GetString();
                    if (!string.IsNullOrWhiteSpace(actionTarget))
                    {
                        return FormatUrl(bmcUrl, actionTarget);
                    }
                }
            }
        }
        catch { }

        return dellUrl;
    }

    private static string FormatUrl(string hostOrIp, string path)
    {
        var cleaned = hostOrIp.Trim().TrimEnd('/');
        if (!cleaned.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !cleaned.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = $"https://{cleaned}";
        }

        return $"{cleaned}{path}";
    }
}
