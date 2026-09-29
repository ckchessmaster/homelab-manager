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

    public async Task<List<RedfishStorageControllerInfo>> GetStorageControllersAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default)
    {
        var controllers = new List<RedfishStorageControllerInfo>();
        try
        {
            var client = CreateClient(insecureTls);
            var url = FormatUrl(hostOrIp, "/redfish/v1/Systems/System.Embedded.1/Storage");

            using var request = CreateRequest(HttpMethod.Get, url, username, password);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return controllers;

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("Members", out var members) && members.ValueKind == JsonValueKind.Array)
            {
                foreach (var member in members.EnumerateArray())
                {
                    if (member.TryGetProperty("@odata.id", out var odataId))
                    {
                        var memberUrl = FormatUrl(hostOrIp, odataId.GetString() ?? "");
                        try
                        {
                            using var mReq = CreateRequest(HttpMethod.Get, memberUrl, username, password);
                            using var mResp = await client.SendAsync(mReq, ct);
                            if (!mResp.IsSuccessStatusCode) continue;

                            using var mDoc = await JsonDocument.ParseAsync(await mResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                            var mRoot = mDoc.RootElement;

                            var cId = mRoot.TryGetProperty("Id", out var idProp) ? idProp.GetString() ?? "StorageController" : "StorageController";
                            var cName = mRoot.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() ?? cId : cId;
                            var cStatus = "OK";
                            if (mRoot.TryGetProperty("Status", out var statusObj) && statusObj.TryGetProperty("Health", out var hProp))
                            {
                                cStatus = hProp.GetString() ?? "OK";
                            }

                            string? model = null;
                            string? fw = null;
                            if (mRoot.TryGetProperty("StorageControllers", out var scArray) && scArray.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var sc in scArray.EnumerateArray())
                                {
                                    if (sc.TryGetProperty("Model", out var mProp)) model = mProp.GetString();
                                    if (sc.TryGetProperty("FirmwareVersion", out var fProp)) fw = fProp.GetString();
                                }
                            }

                            controllers.Add(new RedfishStorageControllerInfo(cId, cName, cStatus, model, fw));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to inspect Redfish storage controller at {Url}", memberUrl);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query Redfish storage controllers for {Host}", hostOrIp);
        }

        return controllers;
    }

    public async Task<List<RedfishDriveInfo>> GetDrivesAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default)
    {
        var drives = new List<RedfishDriveInfo>();
        try
        {
            var client = CreateClient(insecureTls);
            var url = FormatUrl(hostOrIp, "/redfish/v1/Systems/System.Embedded.1/Storage");

            using var request = CreateRequest(HttpMethod.Get, url, username, password);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return drives;

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("Members", out var members) && members.ValueKind == JsonValueKind.Array)
            {
                foreach (var member in members.EnumerateArray())
                {
                    if (member.TryGetProperty("@odata.id", out var odataId))
                    {
                        var storageUrl = FormatUrl(hostOrIp, odataId.GetString() ?? "");
                        try
                        {
                            using var sReq = CreateRequest(HttpMethod.Get, storageUrl, username, password);
                            using var sResp = await client.SendAsync(sReq, ct);
                            if (!sResp.IsSuccessStatusCode) continue;

                            using var sDoc = await JsonDocument.ParseAsync(await sResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                            var sRoot = sDoc.RootElement;

                            if (sRoot.TryGetProperty("Drives", out var drivesArray) && drivesArray.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var driveRef in drivesArray.EnumerateArray())
                                {
                                    if (driveRef.TryGetProperty("@odata.id", out var driveUri))
                                    {
                                        var driveUrl = FormatUrl(hostOrIp, driveUri.GetString() ?? "");
                                        try
                                        {
                                            using var dReq = CreateRequest(HttpMethod.Get, driveUrl, username, password);
                                            using var dResp = await client.SendAsync(dReq, ct);
                                            if (!dResp.IsSuccessStatusCode) continue;

                                            using var dDoc = await JsonDocument.ParseAsync(await dResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                                            var dRoot = dDoc.RootElement;

                                            var dId = dRoot.TryGetProperty("Id", out var idProp) ? idProp.GetString() ?? "Drive" : "Drive";
                                            var dName = dRoot.TryGetProperty("Name", out var nProp) ? nProp.GetString() : null;
                                            var dModel = dRoot.TryGetProperty("Model", out var mProp) ? mProp.GetString() : null;
                                            var dSerial = dRoot.TryGetProperty("SerialNumber", out var sProp) ? sProp.GetString() : null;
                                            var dMedia = dRoot.TryGetProperty("MediaType", out var medProp) ? medProp.GetString() ?? "SSD" : "SSD";
                                            long dCap = dRoot.TryGetProperty("CapacityBytes", out var capProp) && capProp.TryGetInt64(out var cVal) ? cVal : 0;
                                            var dStatus = "OK";
                                            if (dRoot.TryGetProperty("Status", out var stObj) && stObj.TryGetProperty("Health", out var hProp))
                                            {
                                                dStatus = hProp.GetString() ?? "OK";
                                            }

                                            double? lifeLeft = null;
                                            if (dRoot.TryGetProperty("PredictedMediaLifeLeftPercent", out var lifeProp) && lifeProp.TryGetDouble(out var lVal))
                                            {
                                                lifeLeft = lVal;
                                            }

                                            bool? failurePred = null;
                                            if (dRoot.TryGetProperty("FailurePredicted", out var failProp))
                                            {
                                                failurePred = failProp.GetBoolean();
                                            }

                                            string? slot = null;
                                            if (dRoot.TryGetProperty("PhysicalLocation", out var locObj) && locObj.TryGetProperty("PartLocation", out var partObj))
                                            {
                                                if (partObj.TryGetProperty("LocationOrdinalValue", out var ordVal))
                                                {
                                                    slot = $"Bay {ordVal.GetInt32()}";
                                                }
                                            }
                                            if (slot == null && !string.IsNullOrWhiteSpace(dId))
                                            {
                                                var bayMatch = System.Text.RegularExpressions.Regex.Match(dId, @"Bay\.(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                                if (bayMatch.Success)
                                                {
                                                    slot = $"Bay {bayMatch.Groups[1].Value}";
                                                }
                                            }

                                            drives.Add(new RedfishDriveInfo(dId, dName, dModel, dSerial, dMedia, dCap, dStatus, lifeLeft, failurePred, slot));
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogDebug(ex, "Failed to parse drive details at {Url}", driveUrl);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to query Redfish storage at {Url}", storageUrl);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query Redfish drives for {Host}", hostOrIp);
        }

        return drives;
    }

    public async Task<RedfishPowerVitals> GetPowerVitalsAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default)
    {
        var psus = new List<RedfishPowerSupplyInfo>();
        double? totalPowerWatts = null;
        bool redundancyHealthy = true;

        try
        {
            var client = CreateClient(insecureTls);
            var url = FormatUrl(hostOrIp, "/redfish/v1/Chassis/System.Embedded.1/Power");

            using var request = CreateRequest(HttpMethod.Get, url, username, password);
            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var root = doc.RootElement;

                // Total system power consumed
                if (root.TryGetProperty("PowerControl", out var pcArray) && pcArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pc in pcArray.EnumerateArray())
                    {
                        if (pc.TryGetProperty("PowerConsumedWatts", out var pcw) && pcw.TryGetDouble(out var pVal))
                        {
                            totalPowerWatts = pVal;
                            break;
                        }
                    }
                }

                // Power supplies
                if (root.TryGetProperty("PowerSupplies", out var psArray) && psArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ps in psArray.EnumerateArray())
                    {
                        var pId = ps.TryGetProperty("MemberId", out var midProp) ? midProp.GetString() ?? "PSU" : "PSU";
                        var pName = ps.TryGetProperty("Name", out var nProp) ? nProp.GetString() : pId;
                        var pHealth = "OK";
                        var pState = "Enabled";

                        if (ps.TryGetProperty("Status", out var stObj))
                        {
                            if (stObj.TryGetProperty("Health", out var hProp)) pHealth = hProp.GetString() ?? "OK";
                            if (stObj.TryGetProperty("State", out var sProp)) pState = sProp.GetString() ?? "Enabled";
                        }

                        double? outWatts = ps.TryGetProperty("LastPowerOutputWatts", out var outProp) && outProp.TryGetDouble(out var ow) ? ow : null;
                        double? inVolts = ps.TryGetProperty("LineInputVoltage", out var inProp) && inProp.TryGetDouble(out var iv) ? iv : null;
                        double? capWatts = ps.TryGetProperty("PowerCapacityWatts", out var capProp) && capProp.TryGetDouble(out var cw) ? cw : null;

                        psus.Add(new RedfishPowerSupplyInfo(pId, pName, pHealth, pState, outWatts, inVolts, capWatts));
                    }
                }

                // Redundancy check
                if (root.TryGetProperty("Redundancy", out var redArray) && redArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var red in redArray.EnumerateArray())
                    {
                        if (red.TryGetProperty("Status", out var rSt) && rSt.TryGetProperty("Health", out var rHealth))
                        {
                            var rh = rHealth.GetString();
                            if (string.Equals(rh, "Critical", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(rh, "Warning", StringComparison.OrdinalIgnoreCase))
                            {
                                redundancyHealthy = false;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query Redfish power vitals for {Host}", hostOrIp);
        }

        return new RedfishPowerVitals(totalPowerWatts, redundancyHealthy, psus);
    }

    public async Task<List<RedfishMemoryInfo>> GetMemoryModulesAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default)
    {
        var modules = new List<RedfishMemoryInfo>();
        try
        {
            var client = CreateClient(insecureTls);
            var url = FormatUrl(hostOrIp, "/redfish/v1/Systems/System.Embedded.1/Memory");

            using var request = CreateRequest(HttpMethod.Get, url, username, password);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return modules;

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("Members", out var members) && members.ValueKind == JsonValueKind.Array)
            {
                foreach (var member in members.EnumerateArray())
                {
                    if (member.TryGetProperty("@odata.id", out var odataId))
                    {
                        var dimmUrl = FormatUrl(hostOrIp, odataId.GetString() ?? "");
                        try
                        {
                            using var dReq = CreateRequest(HttpMethod.Get, dimmUrl, username, password);
                            using var dResp = await client.SendAsync(dReq, ct);
                            if (!dResp.IsSuccessStatusCode) continue;

                            using var dDoc = await JsonDocument.ParseAsync(await dResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                            var dRoot = dDoc.RootElement;

                            var dId = dRoot.TryGetProperty("Id", out var idProp) ? idProp.GetString() ?? "DIMM" : "DIMM";
                            var loc = dRoot.TryGetProperty("DeviceLocator", out var locProp) ? locProp.GetString() ?? dId : dId;
                            long capBytes = 0;
                            if (dRoot.TryGetProperty("CapacityMiB", out var mibProp) && mibProp.TryGetInt64(out var mibVal))
                            {
                                capBytes = mibVal * 1024 * 1024;
                            }

                            string? speed = null;
                            if (dRoot.TryGetProperty("OperatingSpeedMhz", out var spdProp) && spdProp.TryGetInt32(out var spdVal))
                            {
                                speed = $"{spdVal} MHz";
                            }

                            var health = "OK";
                            var state = "Enabled";
                            if (dRoot.TryGetProperty("Status", out var stObj))
                            {
                                if (stObj.TryGetProperty("Health", out var hProp)) health = hProp.GetString() ?? "OK";
                                if (stObj.TryGetProperty("State", out var sProp)) state = sProp.GetString() ?? "Enabled";
                            }

                            // Only add installed modules
                            if (!string.Equals(state, "Absent", StringComparison.OrdinalIgnoreCase))
                            {
                                modules.Add(new RedfishMemoryInfo(dId, loc, capBytes, speed, health, state));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to parse DIMM at {Url}", dimmUrl);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query Redfish memory for {Host}", hostOrIp);
        }

        return modules;
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
