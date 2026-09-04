using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.Redfish;

public class RedfishClient : IRedfishClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RedfishClient> _logger;

    public const string InsecureHttpClientName = "RedfishInsecureClient";

    public RedfishClient(IHttpClientFactory httpClientFactory, ILogger<RedfishClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private HttpClient CreateClient(bool insecureTls)
    {
        return insecureTls
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

    public async Task<RedfishSystemInfo> GetSystemInfoAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default)
    {
        var client = CreateClient(insecureTls);
        var url = FormatUrl(hostOrIp, "/redfish/v1/Systems/System.Embedded.1");

        using var request = CreateRequest(HttpMethod.Get, url, username, password);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = document.RootElement;

        var powerState = root.TryGetProperty("PowerState", out var ps) ? ps.GetString() ?? "Unknown" : "Unknown";
        var model = root.TryGetProperty("Model", out var m) ? m.GetString() : null;
        var biosVersion = root.TryGetProperty("BiosVersion", out var bv) ? bv.GetString() : null;
        var serialNumber = root.TryGetProperty("SerialNumber", out var sn) ? sn.GetString() : null;

        string? health = null;
        if (root.TryGetProperty("Status", out var status) && status.TryGetProperty("Health", out var h))
        {
            health = h.GetString();
        }

        return new RedfishSystemInfo(powerState, model, biosVersion, health, serialNumber);
    }

    public async Task<RedfishThermalVitals> GetThermalVitalsAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default)
    {
        var client = CreateClient(insecureTls);
        var url = FormatUrl(hostOrIp, "/redfish/v1/Chassis/System.Embedded.1/Thermal");

        using var request = CreateRequest(HttpMethod.Get, url, username, password);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = document.RootElement;

        var temperatures = new List<RedfishSensorReading>();
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

                temperatures.Add(new RedfishSensorReading(name, reading, critical, sensorStatus));
            }
        }

        var fans = new List<RedfishFanReading>();
        if (root.TryGetProperty("Fans", out var fansArray) && fansArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var fan in fansArray.EnumerateArray())
            {
                var name = fan.TryGetProperty("FanName", out var fn) ? fn.GetString() ?? "Fan" : "Fan";
                var rpm = fan.TryGetProperty("Reading", out var fr) ? fr.GetInt32() : 0;
                var fanStatus = "OK";
                if (fan.TryGetProperty("Status", out var fs) && fs.TryGetProperty("Health", out var fsh))
                {
                    fanStatus = fsh.GetString() ?? "OK";
                }

                fans.Add(new RedfishFanReading(name, rpm, fanStatus));
            }
        }

        return new RedfishThermalVitals(temperatures, fans);
    }

    public async Task<RedfishResetResponse> ResetSystemAsync(string hostOrIp, string username, string password, string resetType, bool insecureTls = true, CancellationToken ct = default)
    {
        _logger.LogInformation("Issuing Redfish reset command '{ResetType}' to BMC at {Host}...", resetType, hostOrIp);

        var client = CreateClient(insecureTls);
        var url = FormatUrl(hostOrIp, "/redfish/v1/Systems/System.Embedded.1/Actions/ComputerSystem.Reset");

        using var request = CreateRequest(HttpMethod.Post, url, username, password);
        request.Content = JsonContent.Create(new RedfishResetRequest(resetType));

        using var response = await client.SendAsync(request, ct);
        var success = response.IsSuccessStatusCode;
        var message = success
            ? $"Reset command '{resetType}' accepted successfully."
            : $"Reset command failed with HTTP {response.StatusCode}.";

        return new RedfishResetResponse(success, message);
    }

    private static string FormatUrl(string hostOrIp, string path)
    {
        if (hostOrIp.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            hostOrIp.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return $"{hostOrIp.TrimEnd('/')}{path}";
        }

        return $"https://{hostOrIp.TrimEnd('/')}{path}";
    }
}
