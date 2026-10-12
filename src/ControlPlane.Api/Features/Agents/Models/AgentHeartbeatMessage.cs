namespace ControlPlane.Api.Features.Agents.Models;

public class AgentHeartbeatMessage
{
    public string Type { get; set; } = "HEARTBEAT";
    public string NodeId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string? AgentVersion { get; set; }
    public string? KernelVersion { get; set; }
    public bool PendingReboot { get; set; }
    public string? PackageManager { get; set; }
    public AgentMetrics? Metrics { get; set; }
    public AgentPackageSummary? PackageSummary { get; set; }
    public AgentHardwareSummary? Hardware { get; set; }
    public AgentWindowsLicenseSummary? WindowsLicense { get; set; }
}

public class AgentWindowsLicenseSummary
{
    public bool IsEvaluation { get; set; }
    public string Edition { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string LicenseStatus { get; set; } = string.Empty;
    public long GracePeriodMinutes { get; set; }
    public int DaysRemaining { get; set; }
    public int HoursRemaining { get; set; }
    public string? ExpirationDate { get; set; }
    public int RearmsRemaining { get; set; }
    public string? PartialProductKey { get; set; }
    public bool IsExpiringSoon { get; set; }
    public bool IsCritical { get; set; }
    public string? CollectedAt { get; set; }
    public string? Error { get; set; }
}

public class AgentMetrics
{
    public double CpuUsagePct { get; set; }
    public double MemoryUsagePct { get; set; }
    public double DiskFreePct { get; set; }
}

public class AgentPackageSummary
{
    public string PackageManager { get; set; } = string.Empty;
    public int UpgradableCount { get; set; }
    public int SecurityCount { get; set; }
}

public class AgentHardwareSummary
{
    public List<AgentPhysicalDisk> Disks { get; set; } = new();
    public string? CollectedAt { get; set; }
    public string? ScanDuration { get; set; }
}

public class AgentPhysicalDisk
{
    public string DeviceId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string MediaType { get; set; } = "Unknown";
    public long SizeBytes { get; set; }
    public string Status { get; set; } = "Ok";
    public double? WearOutPercentage { get; set; }
    public double? TemperatureCelsius { get; set; }
    public string? SmartHealthStatus { get; set; }
    public Dictionary<string, string>? Attributes { get; set; }
}
