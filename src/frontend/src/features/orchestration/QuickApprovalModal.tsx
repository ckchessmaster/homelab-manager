import { useState, useMemo } from 'react'
import {
  X,
  ShieldAlert,
  CheckCircle2,
  XCircle,
  Loader2,
  GitFork,
  Check,
  Server,
  Layers,
  Clock,
} from 'lucide-react'
import { Button } from '../../components/ui/button'
import { useHosts } from '../hosts/useHosts'
import { usePendingApprovals } from './usePendingApprovals'
import type { JobSummary } from '../../api/jobs'

interface QuickApprovalModalProps {
  isOpen: boolean
  onClose: () => void
  onOpenCanvas?: (job: JobSummary) => void
}

export function QuickApprovalModal({
  isOpen,
  onClose,
  onOpenCanvas,
}: QuickApprovalModalProps) {
  const { data: hosts } = useHosts()
  const {
    pendingApprovals,
    pendingCount,
    actionInProgressId,
    approveJob,
    rejectJob,
    approveAll,
  } = usePendingApprovals()

  const [rejectingJobId, setRejectingJobId] = useState<string | null>(null)
  const [rejectReason, setRejectReason] = useState('')

  // Map host lookup
  const hostMap = useMemo(() => {
    const map = new Map<string, { hostname: string; ipAddress: string }>()
    hosts?.forEach((h) => {
      map.set(h.id, { hostname: h.hostname, ipAddress: h.ipAddress })
    })
    return map
  }, [hosts])

  if (!isOpen) return null

  const handleApprove = async (job: JobSummary) => {
    try {
      await approveJob(job)
      if (pendingCount <= 1) {
        onClose()
      }
    } catch (err) {
      console.error('Failed to approve job:', err)
    }
  }

  const handleConfirmReject = async (job: JobSummary) => {
    try {
      await rejectJob(job, rejectReason || 'Operator rejected from Quick Approval Modal')
      setRejectingJobId(null)
      setRejectReason('')
      if (pendingCount <= 1) {
        onClose()
      }
    } catch (err) {
      console.error('Failed to reject job:', err)
    }
  }

  const handleApproveAll = async () => {
    try {
      await approveAll()
      onClose()
    } catch (err) {
      console.error('Failed to approve all jobs:', err)
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="quick-approval-title"
      className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-6 bg-black/80 backdrop-blur-md animate-in fade-in duration-200"
    >
      <div className="relative w-full max-w-2xl max-h-[90vh] bg-zinc-950 border border-zinc-800 rounded-2xl shadow-2xl flex flex-col overflow-hidden">
        {/* Modal Header */}
        <div className="p-4 sm:px-6 bg-gradient-to-r from-amber-950/40 via-zinc-900/80 to-zinc-900 border-b border-zinc-800 flex items-center justify-between gap-4 shrink-0">
          <div className="flex items-center gap-3 min-w-0">
            <div className="p-2.5 rounded-xl bg-amber-500/20 text-amber-400 border border-amber-500/30 shrink-0 animate-pulse">
              <ShieldAlert className="w-5 h-5" />
            </div>
            <div className="min-w-0">
              <div className="flex items-center gap-2 flex-wrap">
                <h3 id="quick-approval-title" className="text-base font-bold text-zinc-100 truncate">
                  Workflow Approval Gate
                </h3>
                {pendingCount > 0 && (
                  <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-amber-500/20 text-amber-300 border border-amber-500/40">
                    {pendingCount} {pendingCount === 1 ? 'Job Waiting' : 'Jobs Waiting'}
                  </span>
                )}
              </div>
              <p className="text-xs text-zinc-400 mt-0.5 truncate">
                Workflows paused at safe checkpoints. Operator verification required before node reboot.
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2 shrink-0">
            {pendingCount > 1 && (
              <Button
                variant="primary"
                size="sm"
                onClick={handleApproveAll}
                disabled={Boolean(actionInProgressId)}
                className="text-xs h-8 px-3 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold shadow-md shadow-emerald-950/40"
              >
                {actionInProgressId === 'all' ? (
                  <Loader2 className="w-3.5 h-3.5 animate-spin mr-1.5" />
                ) : (
                  <CheckCircle2 className="w-3.5 h-3.5 mr-1.5" />
                )}
                Approve All ({pendingCount})
              </Button>
            )}
            <button
              type="button"
              onClick={onClose}
              aria-label="Close approval modal"
              className="p-1.5 rounded-lg text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800 transition-colors"
            >
              <X className="w-5 h-5" />
            </button>
          </div>
        </div>

        {/* Modal Body / Items List */}
        <div className="p-4 sm:p-6 space-y-4 overflow-y-auto max-h-[calc(90vh-140px)] bg-zinc-950">
          {pendingCount === 0 ? (
            <div className="py-12 text-center space-y-3">
              <div className="w-12 h-12 rounded-full bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 mx-auto flex items-center justify-center">
                <CheckCircle2 className="w-6 h-6" />
              </div>
              <div className="text-sm font-semibold text-zinc-200">No Approvals Waiting</div>
              <p className="text-xs text-zinc-500 max-w-sm mx-auto">
                All update workflows have either completed or are progressing automatically without human gates.
              </p>
              <Button
                variant="outline"
                size="sm"
                onClick={onClose}
                className="text-xs mt-2 border-zinc-700 bg-zinc-900 text-zinc-300"
              >
                Close
              </Button>
            </div>
          ) : (
            pendingApprovals.map((job) => {
              const hostInfo = hostMap.get(job.targetHostId)
              const hostname = hostInfo?.hostname || job.targetHostId
              const ipAddress = hostInfo?.ipAddress || '—'
              const isProcessing = actionInProgressId === job.id
              const isRejecting = rejectingJobId === job.id

              return (
                <div
                  key={job.id}
                  className="p-4 rounded-xl bg-zinc-900/80 border border-amber-500/30 shadow-lg shadow-amber-950/10 space-y-3 transition-all"
                >
                  <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
                    <div className="flex items-center gap-2.5">
                      <div className="p-2 rounded-lg bg-zinc-800 border border-zinc-700 text-zinc-300">
                        <Server className="w-4 h-4 text-sky-400" />
                      </div>
                      <div>
                        <div className="flex items-center gap-2">
                          <span className="text-sm font-bold text-zinc-100">{hostname}</span>
                          <span className="font-mono text-xs text-zinc-400">{ipAddress}</span>
                        </div>
                        <div className="text-[11px] text-zinc-500 flex items-center gap-2 mt-0.5">
                          <span>Job: <code className="text-zinc-400 font-mono">{job.id.slice(0, 8)}...</code></span>
                          <span>•</span>
                          <span>Pipeline: <strong className="text-zinc-300">{job.pipelineId || 'Standard Upgrade'}</strong></span>
                        </div>
                      </div>
                    </div>

                    <div className="flex items-center gap-1.5 self-start sm:self-auto">
                      <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-semibold bg-amber-500/20 text-amber-300 border border-amber-500/40 animate-pulse">
                        <Clock className="w-2.5 h-2.5" />
                        Awaiting Approval
                      </span>
                    </div>
                  </div>

                  {/* Step Description */}
                  <div className="p-3 rounded-lg bg-zinc-950/80 border border-zinc-800 text-xs text-zinc-300 space-y-1">
                    <div className="text-zinc-400 font-medium">Checkpoint status:</div>
                    <p className="text-zinc-300 text-[11px] leading-relaxed">
                      {job.activeStep || 'Workflow paused at safe state. Operator approval required before node restarts.'}
                    </p>
                  </div>

                  {/* Reject Reason input dialog */}
                  {isRejecting ? (
                    <div className="p-3 rounded-lg bg-rose-950/20 border border-rose-800/40 space-y-2.5">
                      <div className="text-xs font-semibold text-rose-300 flex items-center gap-1.5">
                        <XCircle className="w-4 h-4" />
                        Reject & Rollback Workflow
                      </div>
                      <input
                        type="text"
                        placeholder="Reason for rejection (optional)..."
                        value={rejectReason}
                        onChange={(e) => setRejectReason(e.target.value)}
                        className="w-full px-3 py-1.5 text-xs bg-zinc-950 border border-zinc-700 rounded-lg text-zinc-200 focus:outline-none focus:ring-1 focus:ring-rose-500"
                      />
                      <div className="flex items-center justify-end gap-2">
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => {
                            setRejectingJobId(null)
                            setRejectReason('')
                          }}
                          className="text-xs h-7 px-2.5 border-zinc-700 text-zinc-400"
                        >
                          Cancel
                        </Button>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => handleConfirmReject(job)}
                          disabled={isProcessing}
                          className="text-xs h-7 px-3 bg-rose-900/60 border-rose-700 text-rose-200 hover:bg-rose-800"
                        >
                          {isProcessing ? (
                            <Loader2 className="w-3 h-3 animate-spin mr-1" />
                          ) : (
                            <X className="w-3 h-3 mr-1" />
                          )}
                          Confirm Rejection
                        </Button>
                      </div>
                    </div>
                  ) : (
                    /* Action Buttons */
                    <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-2 pt-1 border-t border-zinc-800/80">
                      <div>
                        {onOpenCanvas && (
                          <button
                            type="button"
                            onClick={() => {
                              onOpenCanvas(job)
                              onClose()
                            }}
                            className="inline-flex items-center gap-1.5 text-xs text-sky-400 hover:text-sky-300 hover:underline py-1"
                          >
                            <GitFork className="w-3.5 h-3.5" />
                            View Full DAG Canvas
                          </button>
                        )}
                      </div>

                      <div className="flex items-center gap-2">
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => setRejectingJobId(job.id)}
                          disabled={isProcessing}
                          className="flex-1 sm:flex-none text-xs h-8 px-3 border-zinc-700 bg-zinc-800/80 hover:bg-rose-950/50 hover:border-rose-800 text-zinc-300 hover:text-rose-200 transition-colors"
                        >
                          <X className="w-3.5 h-3.5 mr-1" />
                          Reject & Abort
                        </Button>
                        <Button
                          variant="primary"
                          size="sm"
                          onClick={() => handleApprove(job)}
                          disabled={isProcessing}
                          className="flex-1 sm:flex-none text-xs h-8 px-4 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold shadow-md shadow-emerald-950/40"
                        >
                          {isProcessing ? (
                            <Loader2 className="w-3.5 h-3.5 animate-spin mr-1.5" />
                          ) : (
                            <Check className="w-3.5 h-3.5 mr-1.5" />
                          )}
                          Approve Reboot
                        </Button>
                      </div>
                    </div>
                  )}
                </div>
              )
            })
          )}
        </div>

        {/* Modal Footer */}
        <div className="p-3 sm:px-6 bg-zinc-900/90 border-t border-zinc-800 flex items-center justify-between text-xs text-zinc-500 shrink-0">
          <span className="flex items-center gap-1.5">
            <Layers className="w-3.5 h-3.5 text-sky-400" />
            Human-in-the-Loop Safety Gate
          </span>
          <Button
            variant="outline"
            size="sm"
            onClick={onClose}
            className="text-xs h-7 px-3 border-zinc-700 bg-zinc-900 text-zinc-300"
          >
            Close
          </Button>
        </div>
      </div>
    </div>
  )
}
