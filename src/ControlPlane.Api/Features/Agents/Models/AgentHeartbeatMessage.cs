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
