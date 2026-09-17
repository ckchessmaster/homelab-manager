using System;
using System.Collections.Generic;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;

// Preflight Models
public record PreflightHeartbeatInput(Guid JobId, Guid HostId, string Hostname, double MaxHeartbeatAgeSeconds = 15.0);
public record PreflightDiskHeadroomInput(Guid JobId, Guid HostId, string Hostname, double MinFreePct = 20.0, string? OsFamily = null);
public record PreflightPackageLockInput(Guid JobId, Guid HostId, string Hostname, string? OsFamily = null);
public record PreflightCheckResult(bool Success, string Message);

// Proxmox Models
public record ProxmoxSnapshotInput(Guid JobId, Guid HostId, string Hostname, string? TargetType, string? SnapshotName = null);
public record ProxmoxSnapshotResult(bool Success, bool Created, string? SnapshotName, string Message);
public record ProxmoxRollbackInput(Guid JobId, Guid HostId, string? SnapshotName, string? TargetType);
public record ProxmoxRollbackResult(bool Success, string Message);

// Kubernetes Models
public record KubernetesNodeInput(Guid JobId, Guid HostId, string? NodeName = null);
public record KubernetesCordonResult(bool Success, bool Cordoned, string? NodeName, string Message);
public record KubernetesDrainInput(Guid JobId, Guid HostId, string NodeName, int TimeoutSeconds = 180, bool IgnoreDaemonSets = true);
public record KubernetesDrainResult(bool Success, bool Drained, int EvictedPodCount, string Message);
public record KubernetesUncordonResult(bool Success, string? NodeName, string Message);

// Agent Models
public record AgentUpgradeInput(Guid JobId, Guid HostId, string Hostname, string? OsFamily = null);
public record AgentUpgradeResult(bool Success, int ExitCode, string Message);
public record AgentRebootInput(Guid JobId, Guid HostId, string Hostname, bool AlwaysReboot = false, int HandshakeTimeoutSeconds = 5, string? OsFamily = null);
public record AgentRebootResult(bool Success, bool Skipped, string? PreRebootKernel, string Message);
public record AgentReconnectInput(Guid JobId, Guid HostId, string Hostname, bool RebootSkipped = false, string? PreRebootKernel = null, int TimeoutSeconds = 300);
public record AgentReconnectResult(bool Success, string? KernelVersion, string Message);

// Health Probe Models
public record HealthProbeInput(
    Guid JobId,
    Guid HostId,
    string Hostname,
    string? OsFamily = null,
    List<string>? ProbeUrls = null,
    bool FailOnFailedServices = true,
    int ProbeTimeoutSeconds = 10);
public record HealthProbeResult(bool Success, string Message, List<string> ProbeDetails);

// Job State Lifecycle Models
public record UpdateJobStatusInput(Guid JobId, string Status, string? ActiveStep, string? FailureReason = null);
public record RecordJobCompletionInput(Guid JobId, string Status, string? FailureReason = null);
