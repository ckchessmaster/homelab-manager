using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Idrac;
using ControlPlane.Api.Features.Hosts.Hardware;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockIdracClient : IIdracClient
{
    private string _fanMode = "Automatic";
    private int? _fanPercentage = 35;
    private string _ledState = "Off";

    public Task<IdracTestResultDto> TestConnectionAsync(string bmcUrl, string username, string password, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new IdracTestResultDto(true, "On", "PowerEdge R740xd", "2.19.1", "OK", "J6K7L89", 14, null));
    }

    public Task<IdracVitalsDto> GetVitalsAsync(string bmcUrl, string username, string password, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        var vitals = new IdracVitalsDto(
            PowerState: "On",
            HealthStatus: "OK",
            Model: "PowerEdge R740xd",
            BiosVersion: "2.19.1",
            SerialNumber: "J6K7L89",
            PowerConsumptionWatts: 185.0,
            Temperatures: new List<IdracSensorReading>
            {
                new("System Board Inlet Temp", 21.0, 42.0, "OK"),
                new("System Board Exhaust Temp", 34.0, 70.0, "OK"),
                new("CPU1 Temp", 38.0, 88.0, "OK"),
                new("CPU2 Temp", 42.0, 88.0, "OK")
            },
            Fans: new List<IdracFanReading>
            {
                new("Fan 1 RPM", 3240, "OK"),
                new("Fan 2 RPM", 3180, "OK"),
                new("Fan 3 RPM", 3300, "OK"),
                new("Fan 4 RPM", 3210, "OK"),
                new("Fan 5 RPM", 3150, "OK"),
                new("Fan 6 RPM", 3270, "OK")
            },
            BmcFirmwareVersion: "7.00.00.00"
        );
        return Task.FromResult(vitals);
    }

    public Task<IdracPowerControlResponse> ResetSystemAsync(string bmcUrl, string username, string password, string resetType, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new IdracPowerControlResponse(true, $"Power action '{resetType}' sent successfully (Demo Mode)", "On"));
    }

    public Task<IdracTestResultDto> TestAgentConnectionAsync(Guid hostId, CancellationToken ct = default) => TestConnectionAsync("https://192.168.1.25", "root", "calvin", true, ct);
    public Task<IdracVitalsDto> GetAgentVitalsAsync(Guid hostId, CancellationToken ct = default) => GetVitalsAsync("https://192.168.1.25", "root", "calvin", true, ct);
    public Task<IdracPowerControlResponse> ResetAgentSystemAsync(Guid hostId, string resetType, CancellationToken ct = default) => ResetSystemAsync("https://192.168.1.25", "root", "calvin", resetType, true, ct);

    public Task<IdracTestResultDto> TestInstanceAsync(IdracStoredInstance config, string password, CancellationToken ct = default) => TestConnectionAsync(config.BmcUrl, config.Username, password, config.AllowSelfSignedCert, ct);
    public Task<IdracVitalsDto> GetInstanceVitalsAsync(IdracStoredInstance config, string password, CancellationToken ct = default) => GetVitalsAsync(config.BmcUrl, config.Username, password, config.AllowSelfSignedCert, ct);
    public Task<IdracPowerControlResponse> ResetInstanceSystemAsync(IdracStoredInstance config, string password, string resetType, CancellationToken ct = default) => ResetSystemAsync(config.BmcUrl, config.Username, password, resetType, config.AllowSelfSignedCert, ct);

    public Task<BmcHardwareInventoryDto> GetHardwareInventoryAsync(string bmcUrl, string username, string password, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        var inventory = new BmcHardwareInventoryDto(
            Disks: new List<PhysicalDiskDto>
            {
                new("Disk.Bay.0", "Disk 0", "Samsung MZ7L31T9HBLT", "S692NE0R123456", "SSD", 1920383410176, HardwareHealthStatus.Ok, 2.0, 31.0, "Bay 0", "OK"),
                new("Disk.Bay.1", "Disk 1", "Samsung MZ7L31T9HBLT", "S692NE0R123457", "SSD", 1920383410176, HardwareHealthStatus.Ok, 3.0, 32.0, "Bay 1", "OK"),
                new("Disk.Bay.2", "Disk 2", "Seagate ST8000NM0055", "ZA123458", "HDD", 8001563222016, HardwareHealthStatus.Ok, null, 36.0, "Bay 2", "OK"),
                new("Disk.Bay.3", "Disk 3", "Seagate ST8000NM0055", "ZA123459", "HDD", 8001563222016, HardwareHealthStatus.Ok, null, 35.0, "Bay 3", "OK")
            },
            Controllers: new List<StorageControllerDto>
            {
                new("RAID.Integrated.1-1", "PERC H730P Mini", HardwareHealthStatus.Ok, "H730P Mini", "25.5.9.0001", true)
            },
            PowerSupplies: new List<PowerSupplyDto>
            {
                new("PSU.Slot.1", "Power Supply 1 (750W)", HardwareHealthStatus.Ok, 95.0, 85.0, 120.0, true),
                new("PSU.Slot.2", "Power Supply 2 (750W)", HardwareHealthStatus.Ok, 90.0, 80.0, 120.0, true)
            },
            MemoryModules: new List<MemoryModuleDto>
            {
                new("DIMM.Socket.A1", 34359738368, "2933", HardwareHealthStatus.Ok, 0, 0),
                new("DIMM.Socket.A2", 34359738368, "2933", HardwareHealthStatus.Ok, 0, 0),
                new("DIMM.Socket.B1", 34359738368, "2933", HardwareHealthStatus.Ok, 0, 0),
                new("DIMM.Socket.B2", 34359738368, "2933", HardwareHealthStatus.Ok, 0, 0)
            }
        );
        return Task.FromResult(inventory);
    }

    public Task<BmcHardwareInventoryDto> GetInstanceHardwareInventoryAsync(IdracStoredInstance config, string password, CancellationToken ct = default) => GetHardwareInventoryAsync(config.BmcUrl, config.Username, password, config.AllowSelfSignedCert, ct);

    public Task<BmcFanControlResponse> SetFanControlAsync(string bmcUrl, string username, string password, string mode, int? percentage, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        _fanMode = mode;
        _fanPercentage = percentage;
        return Task.FromResult(new BmcFanControlResponse(true, $"Fan profile set to '{mode}' ({(percentage.HasValue ? $"{percentage}%" : "Auto")})", mode, percentage));
    }

    public Task<BmcFanControlResponse> SetAgentFanControlAsync(Guid hostId, string mode, int? percentage, CancellationToken ct = default) => SetFanControlAsync("https://192.168.1.25", "root", "calvin", mode, percentage, true, ct);
    public Task<BmcFanControlResponse> SetInstanceFanControlAsync(IdracStoredInstance config, string password, string mode, int? percentage, CancellationToken ct = default) => SetFanControlAsync(config.BmcUrl, config.Username, password, mode, percentage, config.AllowSelfSignedCert, ct);

    public Task<BmcChassisIdentifyResponse> SetChassisIdentifyAsync(string bmcUrl, string username, string password, string state, int durationSeconds = 15, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        _ledState = state;
        return Task.FromResult(new BmcChassisIdentifyResponse(true, $"Chassis locator LED set to '{state}'", state));
    }

    public Task<BmcChassisIdentifyResponse> SetAgentChassisIdentifyAsync(Guid hostId, string state, int durationSeconds = 15, CancellationToken ct = default) => SetChassisIdentifyAsync("https://192.168.1.25", "root", "calvin", state, durationSeconds, true, ct);
    public Task<BmcChassisIdentifyResponse> SetInstanceChassisIdentifyAsync(IdracStoredInstance config, string password, string state, int durationSeconds = 15, CancellationToken ct = default) => SetChassisIdentifyAsync(config.BmcUrl, config.Username, password, state, durationSeconds, config.AllowSelfSignedCert, ct);

    public Task<BmcBootOverrideResponse> SetBootOverrideAsync(string bmcUrl, string username, string password, string target, bool allowSelfSigned = true, CancellationToken ct = default)
    {
        return Task.FromResult(new BmcBootOverrideResponse(true, $"One-time boot target configured to '{target}' (Demo Mode)", target));
    }

    public Task<BmcBootOverrideResponse> SetAgentBootOverrideAsync(Guid hostId, string target, CancellationToken ct = default) => SetBootOverrideAsync("https://192.168.1.25", "root", "calvin", target, true, ct);
    public Task<BmcBootOverrideResponse> SetInstanceBootOverrideAsync(IdracStoredInstance config, string password, string target, CancellationToken ct = default) => SetBootOverrideAsync(config.BmcUrl, config.Username, password, target, config.AllowSelfSignedCert, ct);
}
