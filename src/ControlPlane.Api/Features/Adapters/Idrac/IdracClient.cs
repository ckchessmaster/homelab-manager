using System.Diagnostics;
using System.Net;
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
    private readonly AgentIpmiExecutor? _agentIpmiExecutor;

    public const string InsecureHttpClientName = "IdracInsecureClient";

    public IdracClient(
        IHttpClientFactory httpClientFactory,
        ILogger<IdracClient> logger,
        AgentIpmiExecutor? agentIpmiExecutor = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _agentIpmiExecutor = agentIpmiExecutor;
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
        if (bmcUrl.StartsWith("agent://", StringComparison.OrdinalIgnoreCase))
        {
            var hostStr = bmcUrl["agent://".Length..].Trim().TrimEnd('/');
            if (Guid.TryParse(hostStr, out var hostId))
            {
                return await TestAgentConnectionAsync(hostId, ct);
            }
        }

        if (bmcUrl.StartsWith("ipmi://", StringComparison.OrdinalIgnoreCase))
        {
            if (_agentIpmiExecutor == null)
            {
                return new IdracTestResultDto(false, "Offline", null, null, "Failed", null, 0, "Host agent IPMI executor is not available.");
            }
            return await _agentIpmiExecutor.TestLanConnectionAsync(bmcUrl, username, password, ct);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var client = CreateClient(allowSelfSigned);
            var url = await ResolveSystemUrlAsync(client, bmcUrl, username, password, ct);

            using var request = CreateRequest(HttpMethod.Get, url, username, password);
            using var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound ||
                    response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                    response.StatusCode == HttpStatusCode.NotImplemented ||
                    (int)response.StatusCode >= 500)
                {
                    if (_agentIpmiExecutor != null)
                    {
                        _logger.LogInformation("Redfish returned {StatusCode} for {BmcUrl}. Attempting fallback to IPMI-over-LAN (RMCP+)", response.StatusCode, bmcUrl);
                        var lanResult = await _agentIpmiExecutor.TestLanConnectionAsync(bmcUrl, username, password, ct);
                        if (lanResult.Success)
                        {
                            return lanResult;
                        }
                    }
                }

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
        if (bmcUrl.StartsWith("agent://", StringComparison.OrdinalIgnoreCase))
        {
            var hostStr = bmcUrl["agent://".Length..].Trim().TrimEnd('/');
            if (Guid.TryParse(hostStr, out var hostId))
            {
                return await GetAgentVitalsAsync(hostId, ct);
            }
        }

        if (bmcUrl.StartsWith("ipmi://", StringComparison.OrdinalIgnoreCase))
        {
            if (_agentIpmiExecutor == null)
            {
                return new IdracVitalsDto("Unknown", "Unavailable", null, null, null, null, new List<IdracSensorReading>(), new List<IdracFanReading>());
            }
            return await _agentIpmiExecutor.GetLanVitalsAsync(bmcUrl, username, password, ct);
        }

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
            else if (sysRes.StatusCode == HttpStatusCode.NotFound || (int)sysRes.StatusCode >= 400)
            {
                if (_agentIpmiExecutor != null)
                {
                    _logger.LogInformation("Redfish system URL returned {Status} for {BmcUrl}. Attempting fallback to IPMI-over-LAN", sysRes.StatusCode, bmcUrl);
                    try
                    {
                        return await _agentIpmiExecutor.GetLanVitalsAsync(bmcUrl, username, password, ct);
                    }
                    catch (Exception lanEx)
                    {
                        _logger.LogWarning(lanEx, "IPMI-over-LAN fallback also failed for {BmcUrl}", bmcUrl);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading system info from {BmcUrl}", bmcUrl);
            if (_agentIpmiExecutor != null)
            {
                try
                {
                    return await _agentIpmiExecutor.GetLanVitalsAsync(bmcUrl, username, password, ct);
                }
                catch { }
            }
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
        if (bmcUrl.StartsWith("agent://", StringComparison.OrdinalIgnoreCase))
        {
            var hostStr = bmcUrl["agent://".Length..].Trim().TrimEnd('/');
            if (Guid.TryParse(hostStr, out var hostId))
            {
                return await ResetAgentSystemAsync(hostId, resetType, ct);
            }
        }

        if (bmcUrl.StartsWith("ipmi://", StringComparison.OrdinalIgnoreCase))
        {
            if (_agentIpmiExecutor == null)
            {
                return new IdracPowerControlResponse(false, "Host agent IPMI executor is not available.");
            }
            return await _agentIpmiExecutor.ResetLanSystemAsync(bmcUrl, username, password, resetType, ct);
        }

        _logger.LogInformation("Issuing BMC power action '{ResetType}' to {BmcUrl}...", resetType, bmcUrl);

        try
        {
            var client = CreateClient(allowSelfSigned);
            var actionUrl = await ResolveResetActionUrlAsync(client, bmcUrl, username, password, ct);

            using var request = CreateRequest(HttpMethod.Post, actionUrl, username, password);
            request.Content = JsonContent.Create(
                new { ResetType = NormalizeResetType(resetType) },
                options: new JsonSerializerOptions { PropertyNamingPolicy = null });

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (_agentIpmiExecutor != null)
                {
                    _logger.LogInformation("Redfish power action returned {StatusCode} for {BmcUrl}. Attempting fallback to IPMI-over-LAN", response.StatusCode, bmcUrl);
                    return await _agentIpmiExecutor.ResetLanSystemAsync(bmcUrl, username, password, resetType, ct);
                }
            }

            return new IdracPowerControlResponse(true, $"Power action '{resetType}' sent successfully.");
        }
        catch (Exception ex)
        {
            if (_agentIpmiExecutor != null)
            {
                _logger.LogWarning(ex, "Redfish power action failed for {BmcUrl}. Falling back to IPMI-over-LAN", bmcUrl);
                return await _agentIpmiExecutor.ResetLanSystemAsync(bmcUrl, username, password, resetType, ct);
            }
            return new IdracPowerControlResponse(false, $"Redfish power action failed: {ex.Message}");
        }
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

    public async Task<IdracTestResultDto> TestAgentConnectionAsync(Guid hostId, CancellationToken ct = default)
    {
        if (_agentIpmiExecutor == null)
        {
            return new IdracTestResultDto(false, "Offline", null, null, "Failed", null, 0, "Host agent IPMI executor is not available.");
        }
        return await _agentIpmiExecutor.TestConnectionAsync(hostId, ct);
    }

    public async Task<IdracVitalsDto> GetAgentVitalsAsync(Guid hostId, CancellationToken ct = default)
    {
        if (_agentIpmiExecutor == null)
        {
            return new IdracVitalsDto("Unknown", "Unavailable", null, null, null, null, new List<IdracSensorReading>(), new List<IdracFanReading>());
        }
        return await _agentIpmiExecutor.GetVitalsAsync(hostId, ct);
    }

    public async Task<IdracPowerControlResponse> ResetAgentSystemAsync(Guid hostId, string resetType, CancellationToken ct = default)
    {
        if (_agentIpmiExecutor == null)
        {
            return new IdracPowerControlResponse(false, "Host agent IPMI executor is not available.");
        }
        return await _agentIpmiExecutor.ResetSystemAsync(hostId, resetType, ct);
    }

    public async Task<IdracTestResultDto> TestInstanceAsync(IdracStoredInstance config, string password, CancellationToken ct = default)
    {
        if (string.Equals(config.ConnectionMode, "agent", StringComparison.OrdinalIgnoreCase) && config.HostId.HasValue)
        {
            return await TestAgentConnectionAsync(config.HostId.Value, ct);
        }

        return await TestConnectionAsync(config.BmcUrl, config.Username, password, config.AllowSelfSignedCert, ct);
    }

    public async Task<IdracVitalsDto> GetInstanceVitalsAsync(IdracStoredInstance config, string password, CancellationToken ct = default)
    {
        if (string.Equals(config.ConnectionMode, "agent", StringComparison.OrdinalIgnoreCase) && config.HostId.HasValue)
        {
            return await GetAgentVitalsAsync(config.HostId.Value, ct);
        }

        return await GetVitalsAsync(config.BmcUrl, config.Username, password, config.AllowSelfSignedCert, ct);
    }

    public async Task<IdracPowerControlResponse> ResetInstanceSystemAsync(IdracStoredInstance config, string password, string resetType, CancellationToken ct = default)
    {
        if (string.Equals(config.ConnectionMode, "agent", StringComparison.OrdinalIgnoreCase) && config.HostId.HasValue)
        {
            return await ResetAgentSystemAsync(config.HostId.Value, resetType, ct);
        }

        return await ResetSystemAsync(config.BmcUrl, config.Username, password, resetType, config.AllowSelfSignedCert, ct);
    }

    // --- Fan Control ---

    public async Task<BmcFanControlResponse> SetFanControlAsync(
        string bmcUrl,
        string username,
        string password,
        string mode,
        int? percentage,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        if (bmcUrl.StartsWith("agent://", StringComparison.OrdinalIgnoreCase))
        {
            var hostStr = bmcUrl["agent://".Length..].Trim().TrimEnd('/');
            if (Guid.TryParse(hostStr, out var hostId))
            {
                return await SetAgentFanControlAsync(hostId, mode, percentage, ct);
            }
        }

        if (_agentIpmiExecutor == null)
        {
            return new BmcFanControlResponse(false, "Host agent IPMI executor is not available for fan control.", mode, percentage);
        }

        return await _agentIpmiExecutor.SetLanFanControlAsync(bmcUrl, username, password, mode, percentage, ct);
    }

    public async Task<BmcFanControlResponse> SetAgentFanControlAsync(Guid hostId, string mode, int? percentage, CancellationToken ct = default)
    {
        if (_agentIpmiExecutor == null)
        {
            return new BmcFanControlResponse(false, "Host agent IPMI executor is not available.", mode, percentage);
        }
        return await _agentIpmiExecutor.SetFanControlAsync(hostId, mode, percentage, ct);
    }

    public async Task<BmcFanControlResponse> SetInstanceFanControlAsync(IdracStoredInstance config, string password, string mode, int? percentage, CancellationToken ct = default)
    {
        if (string.Equals(config.ConnectionMode, "agent", StringComparison.OrdinalIgnoreCase) && config.HostId.HasValue)
        {
            return await SetAgentFanControlAsync(config.HostId.Value, mode, percentage, ct);
        }
        return await SetFanControlAsync(config.BmcUrl, config.Username, password, mode, percentage, config.AllowSelfSignedCert, ct);
    }

    // --- Chassis Identify (Locator LED / UID) ---

    public async Task<BmcChassisIdentifyResponse> SetChassisIdentifyAsync(
        string bmcUrl,
        string username,
        string password,
        string state,
        int durationSeconds = 15,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        if (bmcUrl.StartsWith("agent://", StringComparison.OrdinalIgnoreCase))
        {
            var hostStr = bmcUrl["agent://".Length..].Trim().TrimEnd('/');
            if (Guid.TryParse(hostStr, out var hostId))
            {
                return await SetAgentChassisIdentifyAsync(hostId, state, durationSeconds, ct);
            }
        }

        if (bmcUrl.StartsWith("ipmi://", StringComparison.OrdinalIgnoreCase) || _agentIpmiExecutor == null)
        {
            if (_agentIpmiExecutor != null)
            {
                return await _agentIpmiExecutor.SetLanChassisIdentifyAsync(bmcUrl, username, password, state, durationSeconds, ct);
            }
        }

        // Try Redfish PATCH first
        try
        {
            var client = CreateClient(allowSelfSigned);
            var sysUrl = await ResolveSystemUrlAsync(client, bmcUrl, username, password, ct);
            using var req = CreateRequest(HttpMethod.Patch, sysUrl, username, password);

            var isOff = state.Equals("Off", StringComparison.OrdinalIgnoreCase);
            var indicatorPayload = new
            {
                LocationIndicatorActive = !isOff,
                IndicatorLED = isOff ? "Off" : "Blinking"
            };
            req.Content = JsonContent.Create(indicatorPayload);

            using var res = await client.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
            {
                return new BmcChassisIdentifyResponse(true, $"Chassis locator LED set to '{state}' via Redfish.", state);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Redfish locator LED control failed for {BmcUrl}. Falling back to IPMI-over-LAN", bmcUrl);
        }

        if (_agentIpmiExecutor != null)
        {
            return await _agentIpmiExecutor.SetLanChassisIdentifyAsync(bmcUrl, username, password, state, durationSeconds, ct);
        }

        return new BmcChassisIdentifyResponse(false, "Failed to set locator LED: no IPMI or Redfish service available.", state);
    }

    public async Task<BmcChassisIdentifyResponse> SetAgentChassisIdentifyAsync(Guid hostId, string state, int durationSeconds = 15, CancellationToken ct = default)
    {
        if (_agentIpmiExecutor == null)
        {
            return new BmcChassisIdentifyResponse(false, "Host agent IPMI executor is not available.", state);
        }
        return await _agentIpmiExecutor.SetChassisIdentifyAsync(hostId, state, durationSeconds, ct);
    }

    public async Task<BmcChassisIdentifyResponse> SetInstanceChassisIdentifyAsync(IdracStoredInstance config, string password, string state, int durationSeconds = 15, CancellationToken ct = default)
    {
        if (string.Equals(config.ConnectionMode, "agent", StringComparison.OrdinalIgnoreCase) && config.HostId.HasValue)
        {
            return await SetAgentChassisIdentifyAsync(config.HostId.Value, state, durationSeconds, ct);
        }
        return await SetChassisIdentifyAsync(config.BmcUrl, config.Username, password, state, durationSeconds, config.AllowSelfSignedCert, ct);
    }

    // --- Boot Device Override ---

    public async Task<BmcBootOverrideResponse> SetBootOverrideAsync(
        string bmcUrl,
        string username,
        string password,
        string target,
        bool allowSelfSigned = true,
        CancellationToken ct = default)
    {
        if (bmcUrl.StartsWith("agent://", StringComparison.OrdinalIgnoreCase))
        {
            var hostStr = bmcUrl["agent://".Length..].Trim().TrimEnd('/');
            if (Guid.TryParse(hostStr, out var hostId))
            {
                return await SetAgentBootOverrideAsync(hostId, target, ct);
            }
        }

        if (bmcUrl.StartsWith("ipmi://", StringComparison.OrdinalIgnoreCase) || _agentIpmiExecutor == null)
        {
            if (_agentIpmiExecutor != null)
            {
                return await _agentIpmiExecutor.SetLanBootOverrideAsync(bmcUrl, username, password, target, ct);
            }
        }

        // Try Redfish PATCH first
        try
        {
            var client = CreateClient(allowSelfSigned);
            var sysUrl = await ResolveSystemUrlAsync(client, bmcUrl, username, password, ct);
            using var req = CreateRequest(HttpMethod.Patch, sysUrl, username, password);

            var redfishTarget = target.ToLowerInvariant() switch
            {
                "bios" or "biossetup" or "setup" => "BiosSetup",
                "pxe" or "network" => "Pxe",
                "disk" or "hdd" => "Hdd",
                "cd" or "cdrom" or "dvd" => "Cd",
                _ => target
            };

            var bootPayload = new
            {
                Boot = new
                {
                    BootSourceOverrideTarget = redfishTarget,
                    BootSourceOverrideEnabled = "Once"
                }
            };
            req.Content = JsonContent.Create(bootPayload);

            using var res = await client.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
            {
                return new BmcBootOverrideResponse(true, $"One-time boot override set to '{target}' via Redfish.", target);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Redfish boot override failed for {BmcUrl}. Falling back to IPMI-over-LAN", bmcUrl);
        }

        if (_agentIpmiExecutor != null)
        {
            return await _agentIpmiExecutor.SetLanBootOverrideAsync(bmcUrl, username, password, target, ct);
        }

        return new BmcBootOverrideResponse(false, "Failed to set boot override: no IPMI or Redfish service available.", target);
    }

    public async Task<BmcBootOverrideResponse> SetAgentBootOverrideAsync(Guid hostId, string target, CancellationToken ct = default)
    {
        if (_agentIpmiExecutor == null)
        {
            return new BmcBootOverrideResponse(false, "Host agent IPMI executor is not available.", target);
        }
        return await _agentIpmiExecutor.SetBootOverrideAsync(hostId, target, ct);
    }

    public async Task<BmcBootOverrideResponse> SetInstanceBootOverrideAsync(IdracStoredInstance config, string password, string target, CancellationToken ct = default)
    {
        if (string.Equals(config.ConnectionMode, "agent", StringComparison.OrdinalIgnoreCase) && config.HostId.HasValue)
        {
            return await SetAgentBootOverrideAsync(config.HostId.Value, target, ct);
        }
        return await SetBootOverrideAsync(config.BmcUrl, config.Username, password, target, config.AllowSelfSignedCert, ct);
    }
}
