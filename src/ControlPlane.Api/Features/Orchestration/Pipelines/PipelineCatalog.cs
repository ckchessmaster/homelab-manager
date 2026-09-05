using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Api.Features.Orchestration.Pipelines;

public class PipelineCatalog : IPipelineCatalog
{
    private readonly Dictionary<string, PipelineProfile> _profiles;

    public PipelineCatalog()
    {
        var profiles = CreateBuiltinProfiles();
        _profiles = profiles.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<PipelineProfile> GetProfiles() => _profiles.Values.ToList();

    public PipelineProfile? GetProfile(string id)
    {
        _profiles.TryGetValue(id, out var profile);
        return profile;
    }

    public string GetRecommendedProfileId(string? targetType, string? osFamily)
    {
        if (string.Equals(targetType, "k8s_node", StringComparison.OrdinalIgnoreCase))
        {
            return "k8s-node-rolling-upgrade";
        }

        return "standard-os-upgrade";
    }

    public DagExecutionPipeline BuildPipeline(string pipelineId, IServiceProvider serviceProvider)
    {
        var profile = GetProfile(pipelineId)
            ?? throw new KeyNotFoundException($"Pipeline profile '{pipelineId}' is not registered in the catalog.");

        var steps = profile.StepFactory(serviceProvider);
        return new DagExecutionPipeline(steps);
    }

    private static List<PipelineProfile> CreateBuiltinProfiles()
    {
        return new List<PipelineProfile>
        {
            new()
            {
                Id = "standard-os-upgrade",
                Name = "Standard OS Upgrade",
                Description = "Full host upgrade workflow: preflight safety checks, hypervisor snapshot, non-interactive package upgrades, deterministic reboot, reconnection monitoring, and health probes.",
                Icon = "Terminal",
                CompatibleTargetTypes = new[] { "all", "proxmox_vm", "baremetal" },
                Steps = new[]
                {
                    new PipelineStepSummary("Preflight: Heartbeat Freshness", "Verifies agent WebSocket connection and heartbeat < 15s."),
                    new PipelineStepSummary("Preflight: Disk Headroom", "Validates root filesystem free space > 20%."),
                    new PipelineStepSummary("Preflight: Package Lock", "Ensures no active package manager locks exist."),
                    new PipelineStepSummary("Proxmox Safety Snapshot", "Creates an immutable pre-upgrade snapshot if running in Proxmox VE."),
                    new PipelineStepSummary("Package Upgrade", "Executes non-interactive package upgrade with streaming output."),
                    new PipelineStepSummary("Deterministic Reboot", "Pre-reboot filesystem sync and controlled reboot command emission."),
                    new PipelineStepSummary("Await Reconnection", "Monitors WebSocket reconnection window following host reboot."),
                    new PipelineStepSummary("Post-Flight Health Probes", "Runs automated post-boot sanity checks on network and key services.")
                },
                StepFactory = sp => new IJobStep[]
                {
                    new PreflightHeartbeatCheckStep(),
                    new PreflightDiskHeadroomCheckStep(),
                    new PreflightPackageLockCheckStep(),
                    new ProxmoxSnapshotStep(sp.GetService<IProxmoxClient>()),
                    new PackageUpgradeStep(),
                    new DeterministicRebootStep(),
                    new AwaitReconnectionStep(),
                    new PostFlightHealthProbeStep()
                }
            },
            new()
            {
                Id = "k8s-node-rolling-upgrade",
                Name = "Kubernetes Node Rolling Upgrade",
                Description = "Zero-downtime rolling node upgrade: preflights, Proxmox snapshot, Kubernetes cordon & drain, package upgrade, reboot, reconnection, health probes, and Kubernetes uncordon.",
                Icon = "Layers",
                CompatibleTargetTypes = new[] { "k8s_node" },
                Steps = new[]
                {
                    new PipelineStepSummary("Preflight: Heartbeat Freshness", "Verifies agent WebSocket connection and heartbeat < 15s."),
                    new PipelineStepSummary("Preflight: Disk Headroom", "Validates root filesystem free space > 20%."),
                    new PipelineStepSummary("Preflight: Package Lock", "Ensures no active package manager locks exist."),
                    new PipelineStepSummary("Proxmox Safety Snapshot", "Creates an immutable pre-upgrade snapshot if running in Proxmox VE."),
                    new PipelineStepSummary("Kubernetes Node Cordon", "Marks node as Unschedulable in the Kubernetes API."),
                    new PipelineStepSummary("Kubernetes Workload Drain", "Evicts non-daemonset pods cleanly with grace periods to prevent service disruption."),
                    new PipelineStepSummary("Package Upgrade", "Executes non-interactive package upgrade with streaming output."),
                    new PipelineStepSummary("Deterministic Reboot", "Pre-reboot filesystem sync and controlled reboot command emission."),
                    new PipelineStepSummary("Await Reconnection", "Monitors WebSocket reconnection window following host reboot."),
                    new PipelineStepSummary("Post-Flight Health Probes", "Runs automated post-boot sanity checks on network and key services."),
                    new PipelineStepSummary("Kubernetes Node Uncordon", "Marks node as Schedulable again to resume workload processing.")
                },
                StepFactory = sp => new IJobStep[]
                {
                    new PreflightHeartbeatCheckStep(),
                    new PreflightDiskHeadroomCheckStep(),
                    new PreflightPackageLockCheckStep(),
                    new ProxmoxSnapshotStep(sp.GetService<IProxmoxClient>()),
                    new KubernetesCordonStep(sp.GetService<IKubernetesAdapter>()),
                    new KubernetesDrainStep(sp.GetService<IKubernetesAdapter>()),
                    new PackageUpgradeStep(),
                    new DeterministicRebootStep(),
                    new AwaitReconnectionStep(),
                    new PostFlightHealthProbeStep(),
                    new KubernetesUncordonStep(sp.GetService<IKubernetesAdapter>())
                }
            },
            new()
            {
                Id = "safe-reboot-verify",
                Name = "Safe Reboot & Verification",
                Description = "Orchestrated reboot sequence with pre-reboot sync, agent heartbeat monitoring across reboot, and post-boot service health verification.",
                Icon = "RotateCcw",
                CompatibleTargetTypes = new[] { "all", "proxmox_vm", "k8s_node", "baremetal" },
                Steps = new[]
                {
                    new PipelineStepSummary("Preflight: Heartbeat Freshness", "Verifies active WebSocket connection before rebooting."),
                    new PipelineStepSummary("Deterministic Reboot", "Pre-reboot filesystem sync and controlled reboot command emission."),
                    new PipelineStepSummary("Await Reconnection", "Monitors WebSocket reconnection window following host reboot."),
                    new PipelineStepSummary("Post-Flight Health Probes", "Runs automated post-boot sanity checks on network and key services.")
                },
                StepFactory = _ => new IJobStep[]
                {
                    new PreflightHeartbeatCheckStep(),
                    new DeterministicRebootStep(),
                    new AwaitReconnectionStep(),
                    new PostFlightHealthProbeStep()
                }
            },
            new()
            {
                Id = "preflight-dryrun",
                Name = "Preflight Health Dry-Run",
                Description = "Non-mutating preflight inspection verifying agent WebSocket health, root filesystem headroom, and package manager lock status without changing host state.",
                Icon = "ShieldCheck",
                CompatibleTargetTypes = new[] { "all", "proxmox_vm", "k8s_node", "baremetal" },
                Steps = new[]
                {
                    new PipelineStepSummary("Preflight: Heartbeat Freshness", "Verifies agent WebSocket connection and heartbeat freshness."),
                    new PipelineStepSummary("Preflight: Disk Headroom", "Validates root filesystem free space > 20%."),
                    new PipelineStepSummary("Preflight: Package Lock", "Ensures no active package manager locks exist.")
                },
                StepFactory = _ => new IJobStep[]
                {
                    new PreflightHeartbeatCheckStep(),
                    new PreflightDiskHeadroomCheckStep(),
                    new PreflightPackageLockCheckStep()
                }
            },
            new()
            {
                Id = "hypervisor-snapshot-only",
                Name = "Hypervisor Snapshot Only",
                Description = "Creates an immutable pre-maintenance snapshot in Proxmox VE for instant rollback capability before manual changes.",
                Icon = "Camera",
                CompatibleTargetTypes = new[] { "proxmox_vm" },
                Steps = new[]
                {
                    new PipelineStepSummary("Preflight: Heartbeat Freshness", "Verifies agent is alive prior to snapshot."),
                    new PipelineStepSummary("Proxmox Safety Snapshot", "Dispatches Proxmox VE snapshot API call.")
                },
                StepFactory = sp => new IJobStep[]
                {
                    new PreflightHeartbeatCheckStep(),
                    new ProxmoxSnapshotStep(sp.GetService<IProxmoxClient>())
                }
            }
        };
    }
}
