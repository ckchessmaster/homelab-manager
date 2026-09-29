using ControlPlane.Api.Features.Adapters.Redfish;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockRedfishClient : IRedfishClient
{
    public Task<RedfishSystemInfo> GetSystemInfoAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default)
    {
        return Task.FromResult(new RedfishSystemInfo(
            PowerState: "On",
            Model: "PowerEdge R740xd",
            BiosVersion: "2.19.1",
            HealthStatus: "OK",
            SerialNumber: "J6K7L89"
        ));
    }

    public Task<RedfishThermalVitals> GetThermalVitalsAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default)
    {
        return Task.FromResult(new RedfishThermalVitals(
            Temperatures: new List<RedfishSensorReading>
            {
                new("System Board Inlet Temp", 21.0, 42.0, "OK"),
                new("System Board Exhaust Temp", 34.0, 70.0, "OK"),
                new("CPU1 Temp", 38.0, 88.0, "OK"),
                new("CPU2 Temp", 42.0, 88.0, "OK")
            },
            Fans: new List<RedfishFanReading>
            {
                new("Fan 1 RPM", 3240, "OK"),
                new("Fan 2 RPM", 3180, "OK"),
                new("Fan 3 RPM", 3300, "OK"),
                new("Fan 4 RPM", 3210, "OK"),
                new("Fan 5 RPM", 3150, "OK"),
                new("Fan 6 RPM", 3270, "OK")
            }
        ));
    }

    public Task<RedfishResetResponse> ResetSystemAsync(string hostOrIp, string username, string password, string resetType, bool insecureTls = true, CancellationToken ct = default)
    {
        return Task.FromResult(new RedfishResetResponse(true, $"System reset '{resetType}' accepted (Demo Mode)"));
    }

    public Task<List<RedfishStorageControllerInfo>> GetStorageControllersAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default)
    {
        return Task.FromResult(new List<RedfishStorageControllerInfo>
        {
            new("RAID.Integrated.1-1", "PERC H730P Mini", "OK", "H730P Mini", "25.5.9.0001")
        });
    }

    public Task<List<RedfishDriveInfo>> GetDrivesAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default)
    {
        return Task.FromResult(new List<RedfishDriveInfo>
        {
            new("Disk.Bay.0", "Disk 0", "Samsung MZ7L31T9HBLT", "S692NE0R123456", "SSD", 1920383410176, "OK", 98.0, false, "Bay 0"),
            new("Disk.Bay.1", "Disk 1", "Samsung MZ7L31T9HBLT", "S692NE0R123457", "SSD", 1920383410176, "OK", 97.0, false, "Bay 1"),
            new("Disk.Bay.2", "Disk 2", "Seagate ST8000NM0055", "ZA123458", "HDD", 8001563222016, "OK", null, false, "Bay 2"),
            new("Disk.Bay.3", "Disk 3", "Seagate ST8000NM0055", "ZA123459", "HDD", 8001563222016, "OK", null, false, "Bay 3")
        });
    }

    public Task<RedfishPowerVitals> GetPowerVitalsAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default)
    {
        return Task.FromResult(new RedfishPowerVitals(
            TotalPowerWatts: 185.0,
            RedundancyHealthy: true,
            PowerSupplies: new List<RedfishPowerSupplyInfo>
            {
                new("PSU.Slot.1", "Power Supply 1", "OK", "Enabled", 95.0, 120.0, 750.0),
                new("PSU.Slot.2", "Power Supply 2", "OK", "Enabled", 90.0, 120.0, 750.0)
            }
        ));
    }

    public Task<List<RedfishMemoryInfo>> GetMemoryModulesAsync(string hostOrIp, string username, string password, bool insecureTls = true, CancellationToken ct = default)
    {
        return Task.FromResult(new List<RedfishMemoryInfo>
        {
            new("DIMM.Socket.A1", "DIMM A1", 34359738368, "2933", "OK", "Enabled"),
            new("DIMM.Socket.A2", "DIMM A2", 34359738368, "2933", "OK", "Enabled"),
            new("DIMM.Socket.B1", "DIMM B1", 34359738368, "2933", "OK", "Enabled"),
            new("DIMM.Socket.B2", "DIMM B2", 34359738368, "2933", "OK", "Enabled")
        });
    }
}
