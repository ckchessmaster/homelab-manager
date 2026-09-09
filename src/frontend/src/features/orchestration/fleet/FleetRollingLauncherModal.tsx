import { useState, useMemo } from 'react'
import {
  Layers,
  Play,
  X,
  AlertTriangle,
  Loader2,
  ShieldCheck,
  CheckSquare,
  Square,
  ShieldAlert,
  RotateCcw,
} from 'lucide-react'
import type { Host } from '../../../api/hosts'
import { startRollingUpgrade } from '../../../api/temporal'
import { useActiveJobsByHost } from '../useJobs'
import { Button } from '../../../components/ui/button'
import { useQueryClient } from '@tanstack/react-query'

export interface FleetRollingLauncherModalProps {
  isOpen: boolean
  onClose: () => void
  availableHosts: Host[]
  onFleetLaunched: (batchId: string, targetHosts: Host[]) => void
}

export function FleetRollingLauncherModal({
  isOpen,
  onClose,
  availableHosts,
  onFleetLaunched,
}: FleetRollingLauncherModalProps) {
  const queryClient = useQueryClient()
  const activeJobsByHost = useActiveJobsByHost()

  // Selected host IDs
  const [selectedHostIds, setSelectedHostIds] = useState<string[]>(() => {
    // Default select all hosts with updates or online, excluding active DAGs
    return availableHosts
      .filter((h) => h.agent?.installed && (h.agent?.upgradablePackagesCount || 0) > 0 && !activeJobsByHost.has(h.id))
      .map((h) => h.id)
  })

  // Concurrency & Policies
  const [maxParallelism, setMaxParallelism] = useState<number>(1)
  const [failureStrategy, setFailureStrategy] = useState<'StopOnFirstFailure' | 'ContinueRemaining'>('StopOnFirstFailure')
  const [requireApprovalBeforeReboot, setRequireApprovalBeforeReboot] = useState(false)
  const [requireApprovalBetweenHosts, setRequireApprovalBetweenHosts] = useState(false)
  const [alwaysReboot, setAlwaysReboot] = useState(false)
  const [snapshotPrefix, setSnapshotPrefix] = useState('pre-rolling')

  const [isSubmitting, setIsSubmitting] = useState(false)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

  // Filtered selections
  const selectedHosts = useMemo(() => {
    return availableHosts.filter((h) => selectedHostIds.includes(h.id))
  }, [availableHosts, selectedHostIds])

  const totalPendingUpdates = useMemo(() => {
    return selectedHosts.reduce((acc, h) => acc + (h.agent?.upgradablePackagesCount || 0), 0)
  }, [selectedHosts])

  if (!isOpen) return null

  const toggleHost = (id: string) => {
    if (selectedHostIds.includes(id)) {
      setSelectedHostIds(selectedHostIds.filter((hId) => hId !== id))
    } else {
      setSelectedHostIds([...selectedHostIds, id])
    }
  }

  const selectAllK8s = () => {
    const k8sIds = availableHosts
      .filter((h) => h.targetType?.toLowerCase().includes('k8s') || h.targetType?.toLowerCase().includes('kubernetes'))
      .map((h) => h.id)
    setSelectedHostIds(Array.from(new Set([...selectedHostIds, ...k8sIds])))
  }

  const selectAllOutdated = () => {
    const outdatedIds = availableHosts
      .filter((h) => (h.agent?.upgradablePackagesCount || 0) > 0 && !activeJobsByHost.has(h.id))
      .map((h) => h.id)
    setSelectedHostIds(outdatedIds)
  }

  const handleLaunch = async () => {
    if (selectedHostIds.length === 0) return

    setIsSubmitting(true)
    setErrorMsg(null)

    try {
      const response = await startRollingUpgrade({
        hostIds: selectedHostIds,
        maxParallelism,
        failureStrategy,
        requireApprovalBeforeReboot,
        requireApprovalBetweenHosts,
        alwaysReboot,
        snapshotPrefix: snapshotPrefix.trim() || undefined,
        initiatedBy: 'Operator',
      })

      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      queryClient.invalidateQueries({ queryKey: ['rollingUpgrade', response.batchId] })

      onFleetLaunched(response.batchId, selectedHosts)
      onClose()
    } catch (err: unknown) {
      const msg =
        err && typeof err === 'object' && 'message' in err
          ? (err as { message: string }).message
          : 'Failed to initiate rolling upgrade.'
      setErrorMsg(msg)
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-5 md:p-6 bg-black/80 backdrop-blur-md animate-in fade-in duration-200">
      <div className="relative w-full max-w-[1300px] h-[90vh] bg-zinc-950 border border-zinc-800 rounded-2xl shadow-2xl flex flex-col overflow-hidden">
        {/* Header */}
        <div className="px-6 py-4 bg-zinc-900/90 border-b border-zinc-800 flex items-center justify-between gap-4 shrink-0">
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-xl bg-indigo-500/10 border border-indigo-500/20 text-indigo-400">
              <Layers className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-base font-bold text-zinc-100 flex items-center gap-2">
                <span>Multi-Node Fleet Rolling Orchestration</span>
                <span className="px-2 py-0.5 rounded text-[10px] font-mono bg-indigo-500/10 text-indigo-400 border border-indigo-500/20">
                  Zero-Downtime Pipeline
                </span>
              </h3>
              <p className="text-xs text-zinc-400 mt-0.5">
                Execute sequential or concurrent rolling upgrades across cluster nodes with cordon, drain, snapshot, and failure guardrails.
              </p>
            </div>
          </div>

          <button
            type="button"
            onClick={onClose}
            className="p-1.5 rounded-lg text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content Body */}
        <div className="flex-1 min-h-0 grid grid-cols-1 lg:grid-cols-12 overflow-hidden">
          {/* Left Column: Host Target Selection (6 cols) */}
          <div className="lg:col-span-7 p-6 space-y-4 overflow-y-auto border-b lg:border-b-0 lg:border-r border-zinc-800">
            {errorMsg && (
              <div className="p-3 rounded-lg bg-rose-500/10 border border-rose-500/20 text-xs text-rose-300 flex items-center gap-2">
                <AlertTriangle className="w-4 h-4 shrink-0 text-rose-400" />
                <span>{errorMsg}</span>
              </div>
            )}

            {/* Quick Selection Shortcuts */}
            <div className="flex items-center justify-between flex-wrap gap-2 pb-1">
              <div className="text-xs font-semibold text-zinc-200 flex items-center gap-2">
                <span>Select Target Cluster Nodes</span>
                <span className="text-[11px] font-mono text-indigo-400">
                  ({selectedHostIds.length} of {availableHosts.length} selected)
                </span>
              </div>

              <div className="flex items-center gap-1.5 flex-wrap">
                <button
                  type="button"
                  onClick={selectAllK8s}
                  className="px-2 py-1 rounded text-[11px] font-mono bg-zinc-900 hover:bg-zinc-800 text-zinc-300 border border-zinc-800 hover:border-zinc-700 transition-colors cursor-pointer"
                >
                  + All K8s Nodes
                </button>
                <button
                  type="button"
                  onClick={selectAllOutdated}
                  className="px-2 py-1 rounded text-[11px] font-mono bg-zinc-900 hover:bg-zinc-800 text-zinc-300 border border-zinc-800 hover:border-zinc-700 transition-colors cursor-pointer"
                >
                  + All Outdated
                </button>
                <button
                  type="button"
                  onClick={() => setSelectedHostIds([])}
                  className="px-2 py-1 rounded text-[11px] font-mono text-zinc-500 hover:text-zinc-300 transition-colors cursor-pointer"
                >
                  Clear
                </button>
              </div>
            </div>

            {/* Host Checklist */}
            <div className="space-y-2">
              {availableHosts.map((host) => {
                const isSelected = selectedHostIds.includes(host.id)
                const isOnline = host.agent?.isOnline ?? host.agent?.installed
                const hasActiveDag = activeJobsByHost.has(host.id)
                const activeJob = activeJobsByHost.get(host.id)
                const updatesCount = host.agent?.upgradablePackagesCount || 0

                return (
                  <div
                    key={host.id}
                    onClick={() => isOnline && !hasActiveDag && toggleHost(host.id)}
                    className={`p-3 rounded-xl border transition-all flex items-center justify-between gap-3 select-none ${
                      !isOnline || hasActiveDag
                        ? 'opacity-50 bg-zinc-950/40 border-zinc-900 cursor-not-allowed'
                        : isSelected
                        ? 'bg-zinc-900/90 border-indigo-500/60 ring-1 ring-indigo-500/30 cursor-pointer'
                        : 'bg-zinc-950/60 border-zinc-800/80 hover:bg-zinc-900/40 hover:border-zinc-700 cursor-pointer'
                    }`}
                  >
                    <div className="flex items-center gap-3 min-w-0">
                      <div className="shrink-0 text-indigo-400">
                        {isSelected ? (
                          <CheckSquare className="w-5 h-5 fill-indigo-500/20" />
                        ) : (
                          <Square className="w-5 h-5 text-zinc-600" />
                        )}
                      </div>
                      <div className="min-w-0">
                        <div className="flex items-center gap-2">
                          <span className="text-xs font-bold text-zinc-100 truncate">
                            {host.hostname}
                          </span>
                          <span className="text-[11px] font-mono text-zinc-500">
                            {host.ipAddress}
                          </span>
                          <span className="text-[10px] uppercase font-mono px-1.5 py-0.2 rounded bg-zinc-800 text-zinc-400 border border-zinc-700/60">
                            {host.targetType || 'host'}
                          </span>
                        </div>
                        <p className="text-[11px] text-zinc-400 mt-0.5">
                          {host.osFamily} &bull; {updatesCount} upgradable packages
                        </p>
                      </div>
                    </div>

                    <div className="shrink-0 text-right">
                      {hasActiveDag ? (
                        <span
                          className="inline-flex items-center gap-1 text-[10px] font-semibold text-amber-400 bg-amber-500/10 px-2 py-0.5 rounded-full border border-amber-500/20"
                          title={`Active DAG: ${activeJob?.activeStep || activeJob?.status || 'In progress'}`}
                        >
                          DAG in progress
                        </span>
                      ) : isOnline ? (
                        <span className="inline-flex items-center gap-1 text-[10px] font-semibold text-emerald-400 bg-emerald-500/10 px-2 py-0.5 rounded-full border border-emerald-500/20">
                          Online
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 text-[10px] font-semibold text-zinc-500 bg-zinc-800 px-2 py-0.5 rounded-full">
                          Offline
                        </span>
                      )}
                    </div>
                  </div>
                )
              })}
            </div>
          </div>

          {/* Right Column: Rolling Policies & Parameters (5 cols) */}
          <div className="lg:col-span-5 p-6 space-y-5 overflow-y-auto bg-zinc-950/60 flex flex-col justify-between">
            <div className="space-y-4">
              <h4 className="text-xs font-semibold text-zinc-200 uppercase tracking-wider font-mono">
                Fleet Rolling Configuration
              </h4>

              {/* Concurrency Selector */}
              <div className="p-3.5 rounded-xl bg-zinc-900/60 border border-zinc-800 space-y-2">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-medium text-zinc-200">Max Rolling Concurrency</span>
                  <span className="text-xs font-mono font-bold text-indigo-400">
                    {maxParallelism === 1 ? '1 Node (Strict Serial)' : `${maxParallelism} Nodes in Parallel`}
                  </span>
                </div>
                <div className="flex items-center gap-2 pt-1">
                  {[1, 2, 3].map((num) => (
                    <button
                      key={num}
                      type="button"
                      onClick={() => setMaxParallelism(num)}
                      className={`flex-1 py-1.5 rounded-lg text-xs font-medium border transition-colors cursor-pointer ${
                        maxParallelism === num
                          ? 'bg-indigo-600 text-white border-indigo-500 shadow-md shadow-indigo-950/40'
                          : 'bg-zinc-900 text-zinc-400 border-zinc-800 hover:text-zinc-200 hover:border-zinc-700'
                      }`}
                    >
                      {num === 1 ? 'Serial (1)' : `Batch (${num})`}
                    </button>
                  ))}
                </div>
                <p className="text-[11px] text-zinc-400 pt-1 leading-relaxed">
                  {maxParallelism === 1
                    ? 'Recommended for high-availability clusters to prevent quorum loss.'
                    : 'Increases throughput by upgrading small subsets of nodes simultaneously.'}
                </p>
              </div>

              {/* Failure Strategy */}
              <div className="p-3.5 rounded-xl bg-zinc-900/60 border border-zinc-800 space-y-2">
                <span className="text-xs font-medium text-zinc-200">Failure Blast Radius Policy</span>
                <div className="grid grid-cols-2 gap-2 pt-1">
                  <button
                    type="button"
                    onClick={() => setFailureStrategy('StopOnFirstFailure')}
                    className={`p-2 rounded-lg text-left border transition-colors cursor-pointer ${
                      failureStrategy === 'StopOnFirstFailure'
                        ? 'bg-indigo-500/10 border-indigo-500/60 text-indigo-200'
                        : 'bg-zinc-900 border-zinc-800 text-zinc-400 hover:text-zinc-200'
                    }`}
                  >
                    <div className="text-xs font-semibold">Halt on Error</div>
                    <div className="text-[10px] text-zinc-400 mt-0.5">Stop queue if node fails</div>
                  </button>

                  <button
                    type="button"
                    onClick={() => setFailureStrategy('ContinueRemaining')}
                    className={`p-2 rounded-lg text-left border transition-colors cursor-pointer ${
                      failureStrategy === 'ContinueRemaining'
                        ? 'bg-indigo-500/10 border-indigo-500/60 text-indigo-200'
                        : 'bg-zinc-900 border-zinc-800 text-zinc-400 hover:text-zinc-200'
                    }`}
                  >
                    <div className="text-xs font-semibold">Continue Remaining</div>
                    <div className="text-[10px] text-zinc-400 mt-0.5">Log failure & proceed</div>
                  </button>
                </div>
              </div>

              {/* Safety Toggles */}
              <div className="p-3.5 rounded-xl bg-zinc-900/60 border border-zinc-800 space-y-3 text-xs">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <ShieldAlert className="w-4 h-4 text-amber-400" />
                    <span className="text-zinc-200">Pause for Approval Between Hosts</span>
                  </div>
                  <input
                    type="checkbox"
                    checked={requireApprovalBetweenHosts}
                    onChange={(e) => setRequireApprovalBetweenHosts(e.target.checked)}
                    className="rounded border-zinc-700 bg-zinc-900 text-indigo-500 cursor-pointer"
                  />
                </div>

                <div className="flex items-center justify-between pt-2 border-t border-zinc-800/80">
                  <div className="flex items-center gap-2">
                    <ShieldCheck className="w-4 h-4 text-emerald-400" />
                    <span className="text-zinc-200">Approval Before Node Reboot</span>
                  </div>
                  <input
                    type="checkbox"
                    checked={requireApprovalBeforeReboot}
                    onChange={(e) => setRequireApprovalBeforeReboot(e.target.checked)}
                    className="rounded border-zinc-700 bg-zinc-900 text-indigo-500 cursor-pointer"
                  />
                </div>

                <div className="flex items-center justify-between pt-2 border-t border-zinc-800/80">
                  <div className="flex items-center gap-2">
                    <RotateCcw className="w-4 h-4 text-sky-400" />
                    <span className="text-zinc-200">Always Reboot Nodes</span>
                  </div>
                  <input
                    type="checkbox"
                    checked={alwaysReboot}
                    onChange={(e) => setAlwaysReboot(e.target.checked)}
                    className="rounded border-zinc-700 bg-zinc-900 text-indigo-500 cursor-pointer"
                  />
                </div>
              </div>

              {/* Snapshot Prefix */}
              <div className="p-3 rounded-xl bg-zinc-900/60 border border-zinc-800 flex items-center gap-2 text-xs">
                <span className="text-zinc-400 shrink-0">Snapshot Prefix:</span>
                <input
                  type="text"
                  value={snapshotPrefix}
                  onChange={(e) => setSnapshotPrefix(e.target.value)}
                  placeholder="pre-rolling"
                  className="flex-1 px-2.5 py-1 bg-zinc-900 border border-zinc-700/80 rounded-md text-zinc-200 font-mono text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
                />
              </div>
            </div>

            {/* Launch Summary & Actions */}
            <div className="mt-4 p-4 rounded-xl bg-zinc-900/90 border border-zinc-800 space-y-3 shrink-0">
              <div className="flex items-center justify-between text-xs">
                <span className="text-zinc-400">Total Nodes Targeted:</span>
                <span className="font-bold text-zinc-100">{selectedHostIds.length} Nodes</span>
              </div>
              <div className="flex items-center justify-between text-xs">
                <span className="text-zinc-400">Total Upgradable Packages:</span>
                <span className="font-mono text-emerald-400 font-semibold">{totalPendingUpdates} Packages</span>
              </div>

              <div className="flex items-center justify-end gap-2 pt-2 border-t border-zinc-800">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={onClose}
                  className="text-xs h-9 border-zinc-700 bg-zinc-900 text-zinc-300 hover:text-zinc-100"
                >
                  Cancel
                </Button>
                <Button
                  variant="primary"
                  size="sm"
                  onClick={handleLaunch}
                  disabled={selectedHostIds.length === 0 || isSubmitting}
                  className="text-xs h-9 px-5 bg-indigo-600 hover:bg-indigo-500 text-white font-semibold shadow-lg shadow-indigo-950/50 disabled:opacity-50 gap-2 cursor-pointer"
                >
                  {isSubmitting ? (
                    <>
                      <Loader2 className="w-4 h-4 animate-spin" />
                      Dispatching Fleet Batch...
                    </>
                  ) : (
                    <>
                      <Play className="w-4 h-4 fill-current" />
                      Launch Fleet Upgrade
                    </>
                  )}
                </Button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
