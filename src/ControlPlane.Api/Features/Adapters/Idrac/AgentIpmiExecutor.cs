using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Agents;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.Idrac;

public class AgentIpmiExecutor
{
    private readonly IAgentCommandExecutor _commandExecutor;
    private readonly AgentConnectionManager _connectionManager;
    private readonly ILogger<AgentIpmiExecutor> _logger;

    public AgentIpmiExecutor(
        IAgentCommandExecutor commandExecutor,
        AgentConnectionManager connectionManager,
        ILogger<AgentIpmiExecutor> logger)
    {
        _commandExecutor = commandExecutor;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<IdracTestResultDto> TestConnectionAsync(Guid hostId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        if (!_connectionManager.IsOnline(hostId))
        {
            return new IdracTestResultDto(
                Success: false,
                PowerState: "Offline",
                Model: null,
                BiosVersion: null,
                HealthStatus: "Agent Offline",
                SerialNumber: null,
                LatencyMs: sw.ElapsedMilliseconds,
                Message: "Target baremetal host agent is offline. Please ensure the agent is running."
            );
        }

        // Test power status command
        var pwrResult = await _commandExecutor.ExecuteCommandAsync(
            hostId,
            "ipmitool",
            new[] { "chassis", "power", "status" },
            ct);

        if (!pwrResult.Success)
        {
            var err = FormatIpmiErrorMessage(pwrResult.ErrorMessage, pwrResult.StandardError);
            return new IdracTestResultDto(
                Success: false,
                PowerState: "Unknown",
                Model: null,
                BiosVersion: null,
                HealthStatus: "Failed",
                SerialNumber: null,
                LatencyMs: sw.ElapsedMilliseconds,
                Message: err
            );
        }

        var powerState = ParsePowerStatus(pwrResult.StandardOutput);

        // Fetch BMC info for hardware details
        var bmcResult = await _commandExecutor.ExecuteCommandAsync(
            hostId,
            "ipmitool",
            new[] { "bmc", "info" },
            ct);

        string? fruOutput = null;
        try
        {
            using var fruCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            fruCts.CancelAfter(TimeSpan.FromSeconds(5));
            var fruResult = await _commandExecutor.ExecuteCommandAsync(
                hostId,
                "ipmitool",
                new[] { "fru" },
                fruCts.Token);

            if (fruResult.Success)
            {
                fruOutput = fruResult.StandardOutput;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read FRU data from host {HostId}", hostId);
        }

        var (model, biosVersion, serialNumber, health, bmcFw) = ParseBmcAndFruInfo(
            bmcResult.Success ? bmcResult.StandardOutput : null,
            fruOutput);

        string? realBiosVersion = null;
        try
        {
            var biosRes = await _commandExecutor.ExecuteCommandAsync(hostId, "cat", new[] { "/sys/class/dmi/id/bios_version" }, ct);
            if (biosRes.Success && !string.IsNullOrWhiteSpace(biosRes.StandardOutput))
            {
                realBiosVersion = biosRes.StandardOutput.Trim();
            }
        }
        catch { }

        if (string.IsNullOrWhiteSpace(model) || model.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var modelRes = await _commandExecutor.ExecuteCommandAsync(hostId, "cat", new[] { "/sys/class/dmi/id/product_name" }, ct);
                if (modelRes.Success && !string.IsNullOrWhiteSpace(modelRes.StandardOutput))
                {
                    model = modelRes.StandardOutput.Trim();
                }
            }
            catch { }
        }

        return new IdracTestResultDto(
            Success: true,
            PowerState: powerState,
            Model: model ?? "Baremetal Server (IPMI)",
            BiosVersion: realBiosVersion ?? biosVersion,
            HealthStatus: health ?? "OK",
            SerialNumber: serialNumber,
            LatencyMs: sw.ElapsedMilliseconds,
            Message: "Successfully connected to in-band IPMI via host agent."
        );
    }

    public async Task<IdracVitalsDto> GetVitalsAsync(Guid hostId, CancellationToken ct = default)
    {
        if (!_connectionManager.IsOnline(hostId))
        {
            return new IdracVitalsDto(
                PowerState: "Offline",
                HealthStatus: "Agent Offline",
                Model: null,
                BiosVersion: null,
                SerialNumber: null,
                PowerConsumptionWatts: null,
                Temperatures: new List<IdracSensorReading>(),
                Fans: new List<IdracFanReading>()
            );
        }

        // 1. Power status
        var pwrResult = await _commandExecutor.ExecuteCommandAsync(
            hostId,
            "ipmitool",
            new[] { "chassis", "power", "status" },
            ct);
        var powerState = ParsePowerStatus(pwrResult.Success ? pwrResult.StandardOutput : null);

        // 2. BMC Info & FRU
        var bmcResult = await _commandExecutor.ExecuteCommandAsync(
            hostId,
            "ipmitool",
            new[] { "bmc", "info" },
            ct);

        string? fruOutput = null;
        try
        {
            using var fruCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            fruCts.CancelAfter(TimeSpan.FromSeconds(5));
            var fruResult = await _commandExecutor.ExecuteCommandAsync(
                hostId,
                "ipmitool",
                new[] { "fru" },
                fruCts.Token);
            if (fruResult.Success) fruOutput = fruResult.StandardOutput;
        }
        catch { }

        var (model, biosVersion, serialNumber, bmcHealth, bmcFw) = ParseBmcAndFruInfo(
            bmcResult.Success ? bmcResult.StandardOutput : null,
            fruOutput);

        string? realBiosVersion = null;
        try
        {
            var biosRes = await _commandExecutor.ExecuteCommandAsync(hostId, "cat", new[] { "/sys/class/dmi/id/bios_version" }, ct);
            if (biosRes.Success && !string.IsNullOrWhiteSpace(biosRes.StandardOutput))
            {
                realBiosVersion = biosRes.StandardOutput.Trim();
            }
        }
        catch { }

        if (string.IsNullOrWhiteSpace(model) || model.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var modelRes = await _commandExecutor.ExecuteCommandAsync(hostId, "cat", new[] { "/sys/class/dmi/id/product_name" }, ct);
                if (modelRes.Success && !string.IsNullOrWhiteSpace(modelRes.StandardOutput))
                {
                    model = modelRes.StandardOutput.Trim();
                }
            }
            catch { }
        }

        // 3. Sensor vitals
        var sensorResult = await _commandExecutor.ExecuteCommandAsync(
            hostId,
            "ipmitool",
            new[] { "sensor" },
            ct);

        var (temperatures, fans, powerWatts, sensorHealth) = ParseSensors(
            sensorResult.Success ? sensorResult.StandardOutput : null);

        var healthStatus = (bmcHealth == "OK" && sensorHealth == "OK") ? "OK" : (sensorHealth ?? bmcHealth ?? "OK");

        return new IdracVitalsDto(
            PowerState: powerState,
            HealthStatus: healthStatus,
            Model: model ?? "Baremetal Server (IPMI)",
            BiosVersion: realBiosVersion ?? biosVersion,
            SerialNumber: serialNumber,
            PowerConsumptionWatts: powerWatts,
            Temperatures: temperatures,
            Fans: fans,
            BmcFirmwareVersion: bmcFw
        );
    }

    public async Task<IdracPowerControlResponse> ResetSystemAsync(
        Guid hostId,
        string resetType,
        CancellationToken ct = default)
    {
        if (!_connectionManager.IsOnline(hostId))
        {
            return new IdracPowerControlResponse(false, "Target baremetal host agent is offline.");
        }

        var args = MapResetTypeToArgs(resetType);
        _logger.LogInformation("Dispatching IPMI power command '{ResetType}' to host {HostId}...", resetType, hostId);

        var result = await _commandExecutor.ExecuteCommandAsync(
            hostId,
            "ipmitool",
            args,
            ct);

        if (!result.Success)
        {
            var err = FormatIpmiErrorMessage(result.ErrorMessage, result.StandardError);
            return new IdracPowerControlResponse(false, $"IPMI power action '{resetType}' failed: {err}");
        }

        var msg = !string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardOutput.Trim()
            : $"IPMI power action '{resetType}' executed successfully.";

        return new IdracPowerControlResponse(true, msg);
    }

    public async Task<(bool Success, string Message)> InstallIpmiToolAsync(Guid hostId, CancellationToken ct = default)
    {
        if (!_connectionManager.IsOnline(hostId))
        {
            return (false, "Target baremetal host agent is offline. Please ensure controlplane-agent is connected.");
        }

        _logger.LogInformation("Dispatching one-click ipmitool installation on host {HostId}", hostId);

        var script = "DEBIAN_FRONTEND=noninteractive apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y ipmitool openipmi && modprobe ipmi_devintf 2>/dev/null && modprobe ipmi_si 2>/dev/null || true";
        var result = await _commandExecutor.ExecuteCommandAsync(
            hostId,
            "sh",
            new[] { "-c", script },
            ct);

        if (result.Success)
        {
            return (true, "Successfully installed ipmitool and initialized OpenIPMI kernel modules.");
        }

        var errMsg = !string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardError : result.ErrorMessage ?? "Installation failed.";
        return (false, $"Failed to install ipmitool: {errMsg}");
    }

    public virtual async Task<(bool Success, string StandardOutput, string StandardError, string? ErrorMessage)> ExecuteLanplusCommandAsync(
        string hostOrIp,
        string username,
        string password,
        string[] ipmiArgs,
        CancellationToken ct = default)
    {
        var targetHost = hostOrIp;
        if (targetHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            targetHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            targetHost.StartsWith("ipmi://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(targetHost, UriKind.Absolute, out var uri))
            {
                targetHost = uri.Host;
            }
        }

        var processArgs = new List<string>
        {
            "-I", "lanplus",
            "-H", targetHost,
            "-U", username,
            "-P", password,
            "-N", "3",
            "-R", "1"
        };
        processArgs.AddRange(ipmiArgs);

        var psi = new ProcessStartInfo
        {
            FileName = "ipmitool",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in processArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = new Process { StartInfo = psi };
            var stdout = new System.Text.StringBuilder();
            var stderr = new System.Text.StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);

            var exitCode = process.ExitCode;
            var outStr = stdout.ToString().Trim();
            var errStr = stderr.ToString().Trim();

            return (exitCode == 0, outStr, errStr, exitCode == 0 ? null : errStr);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to execute local ipmitool lanplus command against {Host}", targetHost);
            return (false, "", ex.Message, ex.Message);
        }
    }

    public async Task<IdracTestResultDto> TestLanConnectionAsync(
        string hostOrIp,
        string username,
        string password,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var pwrResult = await ExecuteLanplusCommandAsync(
            hostOrIp,
            username,
            password,
            new[] { "chassis", "power", "status" },
            ct);

        if (!pwrResult.Success)
        {
            var err = FormatIpmiErrorMessage(pwrResult.ErrorMessage, pwrResult.StandardError);
            return new IdracTestResultDto(
                Success: false,
                PowerState: "Unknown",
                Model: null,
                BiosVersion: null,
                HealthStatus: "Failed",
                SerialNumber: null,
                LatencyMs: sw.ElapsedMilliseconds,
                Message: err
            );
        }

        var powerState = ParsePowerStatus(pwrResult.StandardOutput);

        var bmcResult = await ExecuteLanplusCommandAsync(
            hostOrIp,
            username,
            password,
            new[] { "bmc", "info" },
            ct);

        string? fruOutput = null;
        try
        {
            var fruResult = await ExecuteLanplusCommandAsync(
                hostOrIp,
                username,
                password,
                new[] { "fru" },
                ct);
            if (fruResult.Success) fruOutput = fruResult.StandardOutput;
        }
        catch { }

        var (model, biosVersion, serialNumber, health, bmcFw) = ParseBmcAndFruInfo(
            bmcResult.Success ? bmcResult.StandardOutput : null,
            fruOutput);

        return new IdracTestResultDto(
            Success: true,
            PowerState: powerState,
            Model: model,
            BiosVersion: biosVersion,
            HealthStatus: health ?? "OK",
            SerialNumber: serialNumber,
            LatencyMs: sw.ElapsedMilliseconds,
            Message: "Connected via IPMI-over-LAN (RMCP+)"
        );
    }

    public async Task<IdracVitalsDto> GetLanVitalsAsync(
        string hostOrIp,
        string username,
        string password,
        CancellationToken ct = default)
    {
        var pwrResult = await ExecuteLanplusCommandAsync(
            hostOrIp,
            username,
            password,
            new[] { "chassis", "power", "status" },
            ct);

        var powerState = pwrResult.Success ? ParsePowerStatus(pwrResult.StandardOutput) : "Unknown";

        var bmcResult = await ExecuteLanplusCommandAsync(
            hostOrIp,
            username,
            password,
            new[] { "bmc", "info" },
            ct);

        string? fruOutput = null;
        try
        {
            var fruResult = await ExecuteLanplusCommandAsync(
                hostOrIp,
                username,
                password,
                new[] { "fru" },
                ct);
            if (fruResult.Success) fruOutput = fruResult.StandardOutput;
        }
        catch { }

        var (model, biosVersion, serialNumber, bmcHealth, bmcFw) = ParseBmcAndFruInfo(
            bmcResult.Success ? bmcResult.StandardOutput : null,
            fruOutput);

        var sensorResult = await ExecuteLanplusCommandAsync(
            hostOrIp,
            username,
            password,
            new[] { "sensor" },
            ct);

        var (temps, fans, watts, sensorHealth) = ParseSensors(sensorResult.Success ? sensorResult.StandardOutput : null);

        var health = (sensorHealth == "Critical" || bmcHealth == "Critical") ? "Critical"
            : (sensorHealth == "Warning" || bmcHealth == "Warning") ? "Warning"
            : (bmcHealth ?? sensorHealth ?? "OK");

        return new IdracVitalsDto(
            PowerState: powerState,
            Model: model,
            BiosVersion: biosVersion,
            HealthStatus: health,
            SerialNumber: serialNumber,
            PowerConsumptionWatts: watts,
            Temperatures: temps,
            Fans: fans,
            BmcFirmwareVersion: bmcFw
        );
    }

    public async Task<IdracPowerControlResponse> ResetLanSystemAsync(
        string hostOrIp,
        string username,
        string password,
        string action,
        CancellationToken ct = default)
    {
        var args = MapResetTypeToArgs(action);
        var result = await ExecuteLanplusCommandAsync(
            hostOrIp,
            username,
            password,
            args,
            ct);

        if (result.Success)
        {
            var outMsg = !string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardOutput.Trim()
                : $"IPMI-over-LAN power action '{action}' executed successfully.";
            return new IdracPowerControlResponse(true, outMsg);
        }

        var err = FormatIpmiErrorMessage(result.ErrorMessage, result.StandardError);
        return new IdracPowerControlResponse(false, $"IPMI-over-LAN power action '{action}' failed: {err}");
    }

    public static string[] MapResetTypeToArgs(string resetType)
    {
        return resetType.ToLowerInvariant() switch
        {
            "on" or "poweron" => new[] { "chassis", "power", "on" },
            "gracefulshutdown" or "shutdown" or "soft" => new[] { "chassis", "power", "soft" },
            "powercycle" or "cycle" => new[] { "chassis", "power", "cycle" },
            "forcerestart" or "reset" => new[] { "chassis", "power", "reset" },
            "forceoff" or "off" => new[] { "chassis", "power", "off" },
            _ => new[] { "chassis", "power", resetType.ToLowerInvariant() }
        };
    }

    public static string ParsePowerStatus(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return "Unknown";
        if (stdout.Contains("is on", StringComparison.OrdinalIgnoreCase)) return "On";
        if (stdout.Contains("is off", StringComparison.OrdinalIgnoreCase)) return "Off";
        return "Unknown";
    }

    public static (string? Model, string? BiosVersion, string? SerialNumber, string? HealthStatus, string? BmcFirmwareVersion) ParseBmcAndFruInfo(
        string? bmcInfoStdout,
        string? fruStdout)
    {
        string? firmwareRev = null;
        string? productName = null;
        string? manufacturerName = null;
        string? serialNumber = null;
        string? health = "OK";

        if (!string.IsNullOrWhiteSpace(bmcInfoStdout))
        {
            var lines = bmcInfoStdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;

                var key = line[..idx].Trim();
                var val = line[(idx + 1)..].Trim();

                if (key.Equals("Firmware Revision", StringComparison.OrdinalIgnoreCase))
                {
                    firmwareRev = val;
                }
                else if (key.Equals("Product Name", StringComparison.OrdinalIgnoreCase))
                {
                    if (!val.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        productName = val;
                    }
                }
                else if (key.Equals("Manufacturer Name", StringComparison.OrdinalIgnoreCase))
                {
                    manufacturerName = val;
                }
                else if (key.Equals("Device Available", StringComparison.OrdinalIgnoreCase))
                {
                    health = val.Equals("yes", StringComparison.OrdinalIgnoreCase) ? "OK" : "Warning";
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(fruStdout))
        {
            var lines = fruStdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;

                var key = line[..idx].Trim();
                var val = line[(idx + 1)..].Trim();

                if (string.IsNullOrWhiteSpace(serialNumber) &&
                    (key.Equals("Product Serial", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("Chassis Serial", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("Board Serial", StringComparison.OrdinalIgnoreCase)))
                {
                    serialNumber = val;
                }
                else if ((string.IsNullOrWhiteSpace(productName) || productName.Contains("Unknown", StringComparison.OrdinalIgnoreCase)) &&
                         (key.Equals("Product Name", StringComparison.OrdinalIgnoreCase) ||
                          key.Equals("Board Product", StringComparison.OrdinalIgnoreCase)))
                {
                    productName = val;
                }
                else if (string.IsNullOrWhiteSpace(manufacturerName) &&
                         (key.Equals("Product Manufacturer", StringComparison.OrdinalIgnoreCase) ||
                          key.Equals("Board Mfg", StringComparison.OrdinalIgnoreCase)))
                {
                    manufacturerName = val;
                }
            }
        }

        string? combinedModel = productName;
        if (!string.IsNullOrWhiteSpace(manufacturerName) && !string.IsNullOrWhiteSpace(productName))
        {
            combinedModel = productName.StartsWith(manufacturerName, StringComparison.OrdinalIgnoreCase)
                ? productName
                : $"{manufacturerName} {productName}";
        }

        return (combinedModel, firmwareRev, serialNumber, health, firmwareRev);
    }

    public static (List<IdracSensorReading> Temperatures, List<IdracFanReading> Fans, double? PowerWatts, string HealthStatus) ParseSensors(
        string? sensorStdout)
    {
        var temperatures = new List<IdracSensorReading>();
        var fans = new List<IdracFanReading>();
        double? powerWatts = null;
        var worstHealth = "OK";

        if (string.IsNullOrWhiteSpace(sensorStdout))
        {
            return (temperatures, fans, powerWatts, worstHealth);
        }

        var lines = sensorStdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length < 3) continue;

            var name = parts[0];

            // Handle standard `ipmitool sensor` format (>= 4 columns):
            // Name | Reading | Units | Status | ... | CriticalThreshold
            if (parts.Length >= 4)
            {
                var readingStr = parts[1];
                var units = parts[2].ToLowerInvariant();
                var status = parts[3].ToLowerInvariant();

                if (double.TryParse(readingStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var readingVal))
                {
                    double? criticalVal = null;
                    if (parts.Length >= 8 && double.TryParse(parts[7], NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedCrit))
                    {
                        criticalVal = parsedCrit;
                    }

                    var sensorStatus = (status == "ok") ? "OK" : (status == "nc" ? "Warning" : (status == "cr" ? "Critical" : status.ToUpperInvariant()));
                    if (sensorStatus == "Critical") worstHealth = "Critical";
                    else if (sensorStatus == "Warning" && worstHealth != "Critical") worstHealth = "Warning";

                    if (units.Contains("degrees c") || units.Contains("c") || name.Contains("temp", StringComparison.OrdinalIgnoreCase))
                    {
                        temperatures.Add(new IdracSensorReading(name, readingVal, criticalVal, sensorStatus));
                    }
                    else if (units.Contains("rpm") || name.Contains("fan", StringComparison.OrdinalIgnoreCase))
                    {
                        fans.Add(new IdracFanReading(name, (int)readingVal, sensorStatus));
                    }
                    else if (units.Contains("watt") || name.Contains("power", StringComparison.OrdinalIgnoreCase) || name.Contains("pwr", StringComparison.OrdinalIgnoreCase))
                    {
                        powerWatts = readingVal;
                    }
                }
            }
            // Handle `ipmitool sdr` format (3 columns):
            // Name | 21 degrees C | ok
            else if (parts.Length == 3)
            {
                var readingAndUnits = parts[1];
                var status = parts[2].ToLowerInvariant();

                var match = Regex.Match(readingAndUnits, @"^([\d\.]+)\s*(.+)$");
                if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                {
                    var units = match.Groups[2].Value.ToLowerInvariant();
                    var sensorStatus = (status == "ok") ? "OK" : (status == "nc" ? "Warning" : (status == "cr" ? "Critical" : status.ToUpperInvariant()));
                    if (sensorStatus == "Critical") worstHealth = "Critical";
                    else if (sensorStatus == "Warning" && worstHealth != "Critical") worstHealth = "Warning";

                    if (units.Contains("degrees c") || units.Contains("c") || name.Contains("temp", StringComparison.OrdinalIgnoreCase))
                    {
                        temperatures.Add(new IdracSensorReading(name, val, null, sensorStatus));
                    }
                    else if (units.Contains("rpm") || name.Contains("fan", StringComparison.OrdinalIgnoreCase))
                    {
                        fans.Add(new IdracFanReading(name, (int)val, sensorStatus));
                    }
                    else if (units.Contains("watt") || name.Contains("power", StringComparison.OrdinalIgnoreCase) || name.Contains("pwr", StringComparison.OrdinalIgnoreCase))
                    {
                        powerWatts = val;
                    }
                }
            }
        }

        return (temperatures, fans, powerWatts, worstHealth);
    }

    private static string FormatIpmiErrorMessage(string? errorMsg, string? stderr)
    {
        var raw = $"{errorMsg} {stderr}".Trim();

        if (raw.Contains("executable file not found", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("not found in $PATH", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase) && raw.Contains("ipmitool", StringComparison.OrdinalIgnoreCase))
        {
            return "'ipmitool' is not installed on the selected baremetal host. Please install it using 'apt install ipmitool' or click 'Install IPMI Tools'.";
        }

        if (raw.Contains("Unable to establish IPMI v2 / RMCP+ session", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("authentication failed", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("invalid username or password", StringComparison.OrdinalIgnoreCase))
        {
            return "Unable to establish IPMI v2 / RMCP+ session. Check the BMC IP, username, and password, and ensure IPMI-over-LAN is enabled in BMC settings.";
        }

        if (raw.Contains("Connection refused", StringComparison.OrdinalIgnoreCase))
        {
            return "Connection refused. IPMI-over-LAN (UDP port 623) may be disabled on this BMC.";
        }

        if (raw.Contains("Could not open device", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("/dev/ipmi", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenIPMI driver (/dev/ipmi0) not found on host. Ensure the IPMI kernel modules (ipmi_devintf, ipmi_si) are loaded.";
        }

        if (raw.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return "Permission denied accessing /dev/ipmi0. The agent may require root privileges or access to the ipmi group.";
        }

        return string.IsNullOrWhiteSpace(raw) ? "IPMI command execution failed." : raw;
    }

    // --- Fan Control ---

    public async Task<BmcFanControlResponse> SetFanControlAsync(
        Guid hostId,
        string mode,
        int? percentage,
        CancellationToken ct = default)
    {
        if (!_connectionManager.IsOnline(hostId))
        {
            return new BmcFanControlResponse(false, "Target baremetal host agent is offline.", mode, percentage);
        }

        var isAuto = string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase);
        if (isAuto)
        {
            _logger.LogInformation("Restoring automatic dynamic fan control on host {HostId}...", hostId);
            var result = await _commandExecutor.ExecuteCommandAsync(
                hostId,
                "ipmitool",
                new[] { "raw", "0x30", "0x30", "0x01", "0x01" },
                ct);

            if (!result.Success)
            {
                var err = FormatIpmiErrorMessage(result.ErrorMessage, result.StandardError);
                return new BmcFanControlResponse(false, $"Failed to restore automatic fan control: {err}", "Auto");
            }

            return new BmcFanControlResponse(true, "Automatic dynamic fan control restored successfully.", "Auto");
        }
        else
        {
            var pct = Math.Clamp(percentage ?? 37, 10, 100);
            var hexSpeed = $"0x{pct:X2}";
            _logger.LogInformation("Setting manual fan speed to {Pct}% ({Hex}) on host {HostId}...", pct, hexSpeed, hostId);

            var modeResult = await _commandExecutor.ExecuteCommandAsync(
                hostId,
                "ipmitool",
                new[] { "raw", "0x30", "0x30", "0x01", "0x00" },
                ct);

            if (!modeResult.Success)
            {
                var err = FormatIpmiErrorMessage(modeResult.ErrorMessage, modeResult.StandardError);
                return new BmcFanControlResponse(false, $"Failed to enable manual fan control mode: {err}", "Manual", pct);
            }

            var speedResult = await _commandExecutor.ExecuteCommandAsync(
                hostId,
                "ipmitool",
                new[] { "raw", "0x30", "0x30", "0x02", "0xff", hexSpeed },
                ct);

            if (!speedResult.Success)
            {
                var err = FormatIpmiErrorMessage(speedResult.ErrorMessage, speedResult.StandardError);
                return new BmcFanControlResponse(false, $"Manual mode enabled, but setting fan speed to {pct}% failed: {err}", "Manual", pct);
            }

            return new BmcFanControlResponse(true, $"Fan speed set to {pct}% (hex: {hexSpeed}) successfully.", "Manual", pct);
        }
    }

    public async Task<BmcFanControlResponse> SetLanFanControlAsync(
        string hostOrIp,
        string username,
        string password,
        string mode,
        int? percentage,
        CancellationToken ct = default)
    {
        var isAuto = string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase);
        if (isAuto)
        {
            _logger.LogInformation("Restoring automatic dynamic fan control on {Host} via IPMI-over-LAN...", hostOrIp);
            var result = await ExecuteLanplusCommandAsync(
                hostOrIp,
                username,
                password,
                new[] { "raw", "0x30", "0x30", "0x01", "0x01" },
                ct);

            if (!result.Success)
            {
                var err = FormatIpmiErrorMessage(result.ErrorMessage, result.StandardError);
                return new BmcFanControlResponse(false, $"Failed to restore automatic fan control: {err}", "Auto");
            }

            return new BmcFanControlResponse(true, "Automatic dynamic fan control restored successfully.", "Auto");
        }
        else
        {
            var pct = Math.Clamp(percentage ?? 37, 10, 100);
            var hexSpeed = $"0x{pct:X2}";
            _logger.LogInformation("Setting manual fan speed to {Pct}% ({Hex}) on {Host} via IPMI-over-LAN...", pct, hexSpeed, hostOrIp);

            var modeResult = await ExecuteLanplusCommandAsync(
                hostOrIp,
                username,
                password,
                new[] { "raw", "0x30", "0x30", "0x01", "0x00" },
                ct);

            if (!modeResult.Success)
            {
                var err = FormatIpmiErrorMessage(modeResult.ErrorMessage, modeResult.StandardError);
                return new BmcFanControlResponse(false, $"Failed to enable manual fan control mode: {err}", "Manual", pct);
            }

            var speedResult = await ExecuteLanplusCommandAsync(
                hostOrIp,
                username,
                password,
                new[] { "raw", "0x30", "0x30", "0x02", "0xff", hexSpeed },
                ct);

            if (!speedResult.Success)
            {
                var err = FormatIpmiErrorMessage(speedResult.ErrorMessage, speedResult.StandardError);
                return new BmcFanControlResponse(false, $"Manual mode enabled, but setting fan speed to {pct}% failed: {err}", "Manual", pct);
            }

            return new BmcFanControlResponse(true, $"Fan speed set to {pct}% (hex: {hexSpeed}) successfully.", "Manual", pct);
        }
    }

    // --- Chassis Identify (Locator LED / UID) ---

    public async Task<BmcChassisIdentifyResponse> SetChassisIdentifyAsync(
        Guid hostId,
        string state,
        int durationSeconds = 15,
        CancellationToken ct = default)
    {
        if (!_connectionManager.IsOnline(hostId))
        {
            return new BmcChassisIdentifyResponse(false, "Target baremetal host agent is offline.", state);
        }

        var args = MapIdentifyStateToArgs(state, durationSeconds);
        var result = await _commandExecutor.ExecuteCommandAsync(
            hostId,
            "ipmitool",
            args,
            ct);

        if (!result.Success)
        {
            var err = FormatIpmiErrorMessage(result.ErrorMessage, result.StandardError);
            return new BmcChassisIdentifyResponse(false, $"Chassis identify failed: {err}", state);
        }

        var msg = state.Equals("Off", StringComparison.OrdinalIgnoreCase)
            ? "Chassis locator LED turned off."
            : $"Chassis locator LED set to {state} (duration: {durationSeconds}s).";
        return new BmcChassisIdentifyResponse(true, msg, state);
    }

    public async Task<BmcChassisIdentifyResponse> SetLanChassisIdentifyAsync(
        string hostOrIp,
        string username,
        string password,
        string state,
        int durationSeconds = 15,
        CancellationToken ct = default)
    {
        var args = MapIdentifyStateToArgs(state, durationSeconds);
        var result = await ExecuteLanplusCommandAsync(
            hostOrIp,
            username,
            password,
            args,
            ct);

        if (!result.Success)
        {
            var err = FormatIpmiErrorMessage(result.ErrorMessage, result.StandardError);
            return new BmcChassisIdentifyResponse(false, $"Chassis identify failed: {err}", state);
        }

        var msg = state.Equals("Off", StringComparison.OrdinalIgnoreCase)
            ? "Chassis locator LED turned off."
            : $"Chassis locator LED set to {state} (duration: {durationSeconds}s).";
        return new BmcChassisIdentifyResponse(true, msg, state);
    }

    // --- Boot Device Override ---

    public async Task<BmcBootOverrideResponse> SetBootOverrideAsync(
        Guid hostId,
        string target,
        CancellationToken ct = default)
    {
        if (!_connectionManager.IsOnline(hostId))
        {
            return new BmcBootOverrideResponse(false, "Target baremetal host agent is offline.", target);
        }

        var ipmiTarget = NormalizeBootTarget(target);
        var result = await _commandExecutor.ExecuteCommandAsync(
            hostId,
            "ipmitool",
            new[] { "chassis", "bootdev", ipmiTarget },
            ct);

        if (!result.Success)
        {
            var err = FormatIpmiErrorMessage(result.ErrorMessage, result.StandardError);
            return new BmcBootOverrideResponse(false, $"Setting boot device override failed: {err}", target);
        }

        return new BmcBootOverrideResponse(true, $"Next one-time boot device set to '{target}' successfully.", target);
    }

    public async Task<BmcBootOverrideResponse> SetLanBootOverrideAsync(
        string hostOrIp,
        string username,
        string password,
        string target,
        CancellationToken ct = default)
    {
        var ipmiTarget = NormalizeBootTarget(target);
        var result = await ExecuteLanplusCommandAsync(
            hostOrIp,
            username,
            password,
            new[] { "chassis", "bootdev", ipmiTarget },
            ct);

        if (!result.Success)
        {
            var err = FormatIpmiErrorMessage(result.ErrorMessage, result.StandardError);
            return new BmcBootOverrideResponse(false, $"Setting boot device override failed: {err}", target);
        }

        return new BmcBootOverrideResponse(true, $"Next one-time boot device set to '{target}' successfully.", target);
    }

    public static string[] MapIdentifyStateToArgs(string state, int durationSeconds)
    {
        if (state.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "chassis", "identify", "0" };
        }
        var sec = durationSeconds > 0 ? durationSeconds : 15;
        return new[] { "chassis", "identify", sec.ToString() };
    }

    public static string NormalizeBootTarget(string target)
    {
        return target.ToLowerInvariant() switch
        {
            "biossetup" or "bios" or "setup" => "bios",
            "pxe" or "network" => "pxe",
            "disk" or "hdd" or "harddrive" => "disk",
            "cd" or "cdrom" or "dvd" => "cdrom",
            _ => target.ToLowerInvariant()
        };
    }
}

