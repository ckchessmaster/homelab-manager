import {
  Camera,
  Layers,
  RotateCcw,
  ShieldAlert,
} from 'lucide-react'

export interface SafetyPolicyProps {
  // Snapshot options
  enableSnapshot: boolean
  onToggleSnapshot: (enabled: boolean) => void
  snapshotName: string
  onChangeSnapshotName: (name: string) => void
  isProxmoxCapable: boolean

  // Kubernetes options
  enableK8sDrain: boolean
  onToggleK8sDrain: (enabled: boolean) => void
  k8sNodeName: string
  onChangeK8sNodeName: (name: string) => void
  isK8sCapable: boolean

  // Reboot & Approval options
  requireApprovalBeforeReboot: boolean
  onToggleApproval: (required: boolean) => void
  alwaysReboot: boolean
  onToggleAlwaysReboot: (always: boolean) => void
}

export function SafetyPolicySections({
  enableSnapshot,
  onToggleSnapshot,
  snapshotName,
  onChangeSnapshotName,
  isProxmoxCapable,
  enableK8sDrain,
  onToggleK8sDrain,
  k8sNodeName,
  onChangeK8sNodeName,
  isK8sCapable,
  requireApprovalBeforeReboot,
  onToggleApproval,
  alwaysReboot,
  onToggleAlwaysReboot,
}: SafetyPolicyProps) {
  return (
    <div className="space-y-4">
      {/* 1. Proxmox Snapshot Safety Gate */}
      <div className="p-4 rounded-xl bg-zinc-950/80 border border-zinc-800 space-y-3">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-lg bg-purple-500/10 text-purple-400 border border-purple-500/20">
              <Camera className="w-4 h-4" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xs font-semibold text-zinc-100">
                  Proxmox Safety Snapshot & Rollback
                </span>
                {isProxmoxCapable && (
                  <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-purple-500/20 text-purple-300">
                    Detected
                  </span>
                )}
              </div>
              <p className="text-[11px] text-zinc-400 mt-0.5 leading-relaxed">
                Take an atomic VM snapshot prior to package upgrades with automatic Saga rollback if verification fails.
              </p>
            </div>
          </div>

          <label className="relative inline-flex items-center cursor-pointer shrink-0 ml-4">
            <input
              type="checkbox"
              checked={enableSnapshot}
              onChange={(e) => onToggleSnapshot(e.target.checked)}
              className="sr-only peer"
            />
            <div className="w-10 h-5 bg-zinc-800 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-purple-600"></div>
          </label>
        </div>

        {enableSnapshot && (
          <div className="mt-2 pt-2 border-t border-zinc-800/80 flex flex-col sm:flex-row sm:items-center gap-2 animate-in fade-in duration-200">
            <span className="text-[11px] text-zinc-400 whitespace-nowrap">Snapshot Identifier:</span>
            <input
              type="text"
              value={snapshotName}
              onChange={(e) => onChangeSnapshotName(e.target.value)}
              placeholder="e.g. pre-upgrade-snap"
              className="flex-1 px-2.5 py-1 text-xs bg-zinc-900 border border-zinc-700/80 rounded-md text-zinc-200 focus:outline-none focus:ring-1 focus:ring-purple-500 font-mono"
            />
          </div>
        )}
      </div>

      {/* 2. Kubernetes Cordon & Drain Policy */}
      <div className="p-4 rounded-xl bg-zinc-950/80 border border-zinc-800 space-y-3">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-lg bg-indigo-500/10 text-indigo-400 border border-indigo-500/20">
              <Layers className="w-4 h-4" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xs font-semibold text-zinc-100">
                  Kubernetes Node Eviction & Schedulability
                </span>
                {isK8sCapable && (
                  <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-indigo-500/20 text-indigo-300">
                    Detected
                  </span>
                )}
              </div>
              <p className="text-[11px] text-zinc-400 mt-0.5 leading-relaxed">
                Cordon node and gracefully evict pods via the Kubernetes Eviction API prior to restart; uncordon on completion.
              </p>
            </div>
          </div>

          <label className="relative inline-flex items-center cursor-pointer shrink-0 ml-4">
            <input
              type="checkbox"
              checked={enableK8sDrain}
              onChange={(e) => onToggleK8sDrain(e.target.checked)}
              className="sr-only peer"
            />
            <div className="w-10 h-5 bg-zinc-800 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-indigo-600"></div>
          </label>
        </div>

        {enableK8sDrain && (
          <div className="mt-2 pt-2 border-t border-zinc-800/80 flex flex-col sm:flex-row sm:items-center gap-2 animate-in fade-in duration-200">
            <span className="text-[11px] text-zinc-400 whitespace-nowrap">Cluster Node Name:</span>
            <input
              type="text"
              value={k8sNodeName}
              onChange={(e) => onChangeK8sNodeName(e.target.value)}
              placeholder="e.g. k8s-worker-01"
              className="flex-1 px-2.5 py-1 text-xs bg-zinc-900 border border-zinc-700/80 rounded-md text-zinc-200 focus:outline-none focus:ring-1 focus:ring-indigo-500 font-mono"
            />
          </div>
        )}
      </div>

      {/* 3. Reboot & Human Approval Policy */}
      <div className="p-4 rounded-xl bg-zinc-950/80 border border-zinc-800 space-y-4">
        {/* Human-in-the-Loop Approval Gate */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-lg bg-amber-500/10 text-amber-400 border border-amber-500/20">
              <ShieldAlert className="w-4 h-4" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xs font-semibold text-zinc-100">
                  Human-in-the-Loop Approval Gate
                </span>
                <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-amber-500/20 text-amber-300">
                  Temporal Signal
                </span>
              </div>
              <p className="text-[11px] text-zinc-400 mt-0.5 leading-relaxed">
                Pause workflow after packages install. Await operator 1-click confirmation before executing host reboot.
              </p>
            </div>
          </div>

          <label className="relative inline-flex items-center cursor-pointer shrink-0 ml-4">
            <input
              type="checkbox"
              checked={requireApprovalBeforeReboot}
              onChange={(e) => onToggleApproval(e.target.checked)}
              className="sr-only peer"
            />
            <div className="w-10 h-5 bg-zinc-800 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-amber-600"></div>
          </label>
        </div>

        {/* Always Reboot vs Conditional */}
        <div className="pt-3 border-t border-zinc-800/80 flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-lg bg-zinc-800 text-zinc-300 border border-zinc-700/60">
              <RotateCcw className="w-4 h-4 text-emerald-400" />
            </div>
            <div>
              <span className="text-xs font-semibold text-zinc-200">
                Deterministic Host Reboot
              </span>
              <p className="text-[11px] text-zinc-400 mt-0.5">
                {alwaysReboot
                  ? 'Reboot node unconditionally upon upgrade completion.'
                  : 'Reboot only if kernel, systemd, or glibc packages trigger reboot requirement.'}
              </p>
            </div>
          </div>

          <label className="relative inline-flex items-center cursor-pointer shrink-0 ml-4">
            <input
              type="checkbox"
              checked={alwaysReboot}
              onChange={(e) => onToggleAlwaysReboot(e.target.checked)}
              className="sr-only peer"
            />
            <div className="w-10 h-5 bg-zinc-800 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-emerald-600"></div>
          </label>
        </div>
      </div>
    </div>
  )
}
