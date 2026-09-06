import { useState } from 'react'
import {
  Layers,
  X,
  RefreshCw,
  Play,
  Pause,
  StopCircle,
  GitFork,
  CheckCircle2,
  AlertCircle,
  Clock,
  Loader2,
  Server,
  ArrowUpRight,
} from 'lucide-react'
import type { Host } from '../../../api/hosts'
import type { JobSummary } from '../../../api/jobs'
import { useRollingUpgrade } from './useRollingUpgrade'
import { WorkflowCanvasModal } from '../canvas/WorkflowCanvasModal'
import { Button } from '../../../components/ui/button'

export interface FleetRollingDashboardModalProps {
  isOpen: boolean
  onClose: () => void
  batchId: string | null
  targetHosts: Host[]
  onOpenTerminalForJob?: (job: JobSummary) => void
}

export function FleetRollingDashboardModal({
  isOpen,
  onClose,
  batchId,
  targetHosts,
  onOpenTerminalForJob,
}: FleetRollingDashboardModalProps) {
  const {
    data: batchData,
    isFetching,
    refetch,
    pauseFleet,
    isPausing,
    resumeFleet,
    isResuming,
    abortFleet,
    isAborting,
  } = useRollingUpgrade(isOpen ? batchId : null)

  const [selectedNodeJob, setSelectedNodeJob] = useState<JobSummary | null>(null)
  const [selectedNodeHost, setSelectedNodeHost] = useState<Host | null>(null)
  const [isNodeDagOpen, setIsNodeDagOpen] = useState(false)

  if (!isOpen || !batchId) return null

  const state = batchData?.state
  const status = batchData?.executionStatus || state?.status || 'Running'
  const isPaused = state?.isPaused || status === 'Paused'
  const isCompleted = status === 'Completed'
  const isFailed = status === 'Failed' || status === 'PartiallyFailed'
  const isCancelled = status === 'Cancelled' || state?.cancelled

  const totalHosts = state?.totalHosts || targetHosts.length
  const completedHosts = state?.completedHosts || 0
  const progressPercent = totalHosts > 0 ? Math.round((completedHosts / totalHosts) * 100) : 0

  const handleInspectNode = (host: Host, childWorkflowId?: string | null) => {
    const jobId = childWorkflowId || `host-upgrade-${host.id}-${batchId}`
    setSelectedNodeHost(host)
    setSelectedNodeJob({
      id: jobId,
      targetHostId: host.id,
      pipelineId: 'temporal-rolling-node',
      status: state?.hostProgresses?.[host.id]?.status || 'Running',
      activeStep: state?.hostProgresses?.[host.id]?.currentStep || null,
      initiatedBy: 'RollingOrchestrator',
      startedAt: state?.hostProgresses?.[host.id]?.startedAt || null,
      completedAt: state?.hostProgresses?.[host.id]?.completedAt || null,
      failureReason: state?.hostProgresses?.[host.id]?.errorMessage || null,
    })
    setIsNodeDagOpen(true)
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-5 md:p-6 bg-black/80 backdrop-blur-md animate-in fade-in duration-200">
      <div className="relative w-full max-w-[1400px] h-[90vh] bg-zinc-950 border border-zinc-800 rounded-2xl shadow-2xl flex flex-col overflow-hidden">
        {/* Header */}
        <div className="px-6 py-4 bg-zinc-900/90 border-b border-zinc-800 flex items-center justify-between gap-4 shrink-0">
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-xl bg-indigo-500/10 border border-indigo-500/20 text-indigo-400">
              <Layers className="w-5 h-5" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h3 className="text-base font-bold text-zinc-100">
                  Fleet Rolling Upgrade Progress
                </h3>
                <span className="text-[11px] font-mono px-2 py-0.5 rounded bg-zinc-800 text-zinc-300 border border-zinc-700">
                  Batch: {batchId.substring(0, 8)}...
                </span>
                <span
                  className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[11px] font-semibold border ${
                    isCompleted
                      ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/30'
                      : isFailed
                      ? 'bg-rose-500/10 text-rose-400 border-rose-500/30'
                      : isPaused
                      ? 'bg-amber-500/10 text-amber-400 border-amber-500/30'
                      : 'bg-sky-500/10 text-sky-400 border-sky-500/30 animate-pulse'
                  }`}
                >
                  {status}
                </span>
              </div>
              <p className="text-xs text-zinc-400 mt-0.5">
                {state?.activeHostname ? (
                  <span>Currently processing: <strong className="text-sky-300">{state.activeHostname}</strong></span>
                ) : isCompleted ? (
                  <span className="text-emerald-400">All fleet nodes upgraded and verified successfully.</span>
                ) : (
                  <span>Waiting for queue dispatch...</span>
                )}
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            {/* Pause / Resume buttons */}
            {!isCompleted && !isFailed && !isCancelled && (
              <>
                {isPaused ? (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => resumeFleet()}
                    disabled={isResuming}
                    className="text-xs h-8 gap-1.5 border-emerald-700 bg-emerald-950/40 text-emerald-300 hover:bg-emerald-900/60"
                  >
                    {isResuming ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Play className="w-3.5 h-3.5" />}
                    Resume Fleet
                  </Button>
                ) : (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => pauseFleet()}
                    disabled={isPausing}
                    className="text-xs h-8 gap-1.5 border-amber-700 bg-amber-950/40 text-amber-300 hover:bg-amber-900/60"
                  >
                    {isPausing ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Pause className="w-3.5 h-3.5" />}
                    Pause Fleet
                  </Button>
                )}

                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => abortFleet()}
                  disabled={isAborting}
                  className="text-xs h-8 gap-1.5 border-rose-800 bg-rose-950/40 text-rose-300 hover:bg-rose-900/60"
                >
                  {isAborting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <StopCircle className="w-3.5 h-3.5" />}
                  Abort
                </Button>
              </>
            )}

            <Button
              variant="outline"
              size="sm"
              onClick={() => refetch()}
              disabled={isFetching}
              className="text-xs h-8 px-2 border-zinc-700 bg-zinc-900 text-zinc-300 hover:text-zinc-100"
            >
              <RefreshCw className={`w-3.5 h-3.5 ${isFetching ? 'animate-spin' : ''}`} />
            </Button>
            <button
              type="button"
              onClick={onClose}
              className="p-1.5 rounded-lg text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800 transition-colors"
            >
              <X className="w-5 h-5" />
            </button>
          </div>
        </div>

        {/* Aggregate Progress Bar Banner */}
        <div className="px-6 py-3 bg-zinc-900/40 border-b border-zinc-800 shrink-0">
          <div className="flex items-center justify-between text-xs mb-1.5">
            <span className="font-semibold text-zinc-300">
              Fleet Upgrade Completion: {completedHosts} of {totalHosts} Nodes
            </span>
            <span className="font-mono text-indigo-400 font-bold">{progressPercent}%</span>
          </div>
          <div className="w-full h-2 rounded-full bg-zinc-800 overflow-hidden">
            <div
              className={`h-full transition-all duration-500 ${
                isFailed ? 'bg-rose-500' : 'bg-gradient-to-r from-indigo-500 to-emerald-500'
              }`}
              style={{ width: `${progressPercent}%` }}
            />
          </div>
        </div>

        {/* Node Cards List */}
        <div className="flex-1 p-6 overflow-y-auto space-y-3">
          {targetHosts.map((host, idx) => {
            const progress = state?.hostProgresses?.[host.id]
            const hostStatus = progress?.status || 'Pending'
            const isNodeRunning = hostStatus === 'Running'
            const isNodeCompleted = hostStatus === 'Completed'
            const isNodeFailed = hostStatus === 'Failed'
            const isNodeSkipped = hostStatus === 'Skipped'

            return (
              <div
                key={host.id}
                className={`p-4 rounded-xl border transition-all flex flex-col md:flex-row md:items-center justify-between gap-4 ${
                  isNodeRunning
                    ? 'bg-zinc-900/95 border-sky-500/60 ring-1 ring-sky-500/30 shadow-lg shadow-sky-950/20'
                    : isNodeCompleted
                    ? 'bg-zinc-900/60 border-emerald-500/40'
                    : isNodeFailed
                    ? 'bg-rose-950/30 border-rose-600/60'
                    : isNodeSkipped
                    ? 'bg-zinc-950/40 border-zinc-800/40 opacity-60'
                    : 'bg-zinc-950/80 border-zinc-800'
                }`}
              >
                {/* Node details */}
                <div className="flex items-start gap-3 min-w-0">
                  <div
                    className={`p-2.5 rounded-xl border shrink-0 ${
                      isNodeRunning
                        ? 'bg-sky-500/10 text-sky-400 border-sky-500/30'
                        : isNodeCompleted
                        ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/30'
                        : isNodeFailed
                        ? 'bg-rose-500/10 text-rose-400 border-rose-500/30'
                        : 'bg-zinc-800 text-zinc-400 border-zinc-700/60'
                    }`}
                  >
                    <Server className="w-5 h-5" />
                  </div>

                  <div className="min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="text-xs font-mono px-1.5 py-0.5 rounded bg-zinc-800 text-zinc-400">
                        Node #{idx + 1}
                      </span>
                      <h4 className="text-sm font-bold text-zinc-100 truncate">
                        {host.hostname}
                      </h4>
                      <span className="text-xs font-mono text-zinc-500">
                        ({host.ipAddress})
                      </span>
                      <span className="text-[10px] uppercase font-mono px-1.5 py-0.2 rounded bg-zinc-800 text-zinc-400 border border-zinc-700/60">
                        {host.targetType || 'host'}
                      </span>
                    </div>

                    <p className="text-xs text-zinc-400 mt-1 flex items-center gap-2 flex-wrap">
                      <span>OS: <strong className="text-zinc-300 font-normal">{host.osFamily}</strong></span>
                      {progress?.currentStep && (
                        <span>&bull; Step: <strong className="text-sky-300 font-normal">{progress.currentStep}</strong></span>
                      )}
                      {progress?.errorMessage && (
                        <span className="text-rose-400 font-mono text-[11px] truncate max-w-lg">
                          &bull; Error: {progress.errorMessage}
                        </span>
                      )}
                    </p>
                  </div>
                </div>

                {/* Status chip & Actions */}
                <div className="flex items-center gap-3 shrink-0 self-end md:self-center">
                  <div>
                    {isNodeRunning && (
                      <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-sky-500/20 text-sky-300 border border-sky-500/30 animate-pulse">
                        <Loader2 className="w-3.5 h-3.5 animate-spin" />
                        In Progress
                      </span>
                    )}
                    {isNodeCompleted && (
                      <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
                        <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" />
                        Completed
                      </span>
                    )}
                    {isNodeFailed && (
                      <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-rose-500/20 text-rose-300 border border-rose-500/30">
                        <AlertCircle className="w-3.5 h-3.5 text-rose-400" />
                        Failed
                      </span>
                    )}
                    {isNodeSkipped && (
                      <span className="inline-flex items-center gap-1 px-3 py-1 rounded-full text-xs font-medium bg-zinc-800 text-zinc-500 border border-zinc-700">
                        Skipped
                      </span>
                    )}
                    {hostStatus === 'Pending' && (
                      <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-medium bg-zinc-900 text-zinc-400 border border-zinc-800">
                        <Clock className="w-3.5 h-3.5 text-zinc-500" />
                        Queued
                      </span>
                    )}
                  </div>

                  {/* Button to drill down into node's live DAG */}
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => handleInspectNode(host, progress?.childWorkflowId)}
                    className="text-xs h-8 px-2.5 gap-1.5 border-zinc-700 bg-zinc-900 text-zinc-300 hover:text-white"
                    title="View individual node DAG canvas"
                  >
                    <GitFork className="w-3.5 h-3.5 text-emerald-400" />
                    Node DAG
                    <ArrowUpRight className="w-3 h-3 text-zinc-500" />
                  </Button>
                </div>
              </div>
            )
          })}
        </div>

        {/* Footer */}
        <div className="px-6 py-2.5 bg-zinc-900/90 border-t border-zinc-800 flex items-center justify-between text-xs text-zinc-400 shrink-0">
          <div className="flex items-center gap-3">
            <span>Concurrency: <strong>{state?.totalHosts || targetHosts.length} hosts targeted</strong></span>
            <span>&bull;</span>
            <span>Engine: <strong>Temporal Child Workflows</strong></span>
          </div>
          <div className="text-[11px] font-mono text-zinc-500">
            Automated cluster cordon ➔ drain ➔ upgrade ➔ reboot ➔ verify ➔ uncordon
          </div>
        </div>
      </div>

      {/* Individual Node DAG Drill-down Modal */}
      {selectedNodeJob && selectedNodeHost && (
        <WorkflowCanvasModal
          isOpen={isNodeDagOpen}
          onClose={() => {
            setIsNodeDagOpen(false)
            setSelectedNodeJob(null)
            setSelectedNodeHost(null)
          }}
          job={selectedNodeJob}
          host={selectedNodeHost}
          onOpenTerminal={onOpenTerminalForJob}
        />
      )}
    </div>
  )
}
