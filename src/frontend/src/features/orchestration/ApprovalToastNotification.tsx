import { useState, useEffect, useMemo } from 'react'
import { ShieldAlert, X, ArrowRight } from 'lucide-react'
import { Button } from '../../components/ui/button'
import { useHosts } from '../hosts/useHosts'
import type { JobSummary } from '../../api/jobs'

interface ApprovalToastNotificationProps {
  pendingApprovals: JobSummary[]
  onOpenApprovals: () => void
}

export function ApprovalToastNotification({
  pendingApprovals,
  onOpenApprovals,
}: ApprovalToastNotificationProps) {
  const { data: hosts } = useHosts()
  const [dismissedIds, setDismissedIds] = useState<Set<string>>(new Set())

  // Find jobs that have not been dismissed
  const activeUnseenJobs = useMemo(() => {
    return pendingApprovals.filter((j) => !dismissedIds.has(j.id))
  }, [pendingApprovals, dismissedIds])

  // Clear dismissed IDs that are no longer pending so future occurrences can trigger again
  useEffect(() => {
    const currentPendingSet = new Set(pendingApprovals.map((j) => j.id))
    setDismissedIds((prev) => {
      let changed = false
      const next = new Set<string>()
      for (const id of prev) {
        if (currentPendingSet.has(id)) {
          next.add(id)
        } else {
          changed = true
        }
      }
      return changed ? next : prev
    })
  }, [pendingApprovals])

  if (activeUnseenJobs.length === 0) return null

  const pendingCount = activeUnseenJobs.length
  const firstJob = activeUnseenJobs[0]
  const host = hosts?.find((h) => h.id === firstJob.targetHostId)
  const hostName = host?.hostname || firstJob.targetHostId

  const handleDismiss = () => {
    setDismissedIds((prev) => {
      const next = new Set(prev)
      activeUnseenJobs.forEach((j) => next.add(j.id))
      return next
    })
  }

  return (
    <aside
      aria-label="Workflow Approval Notification"
      className="fixed z-40 bottom-20 md:bottom-6 right-3 sm:right-6 max-w-[calc(100vw-1.5rem)] sm:max-w-md w-full animate-in slide-in-from-bottom-5 duration-300"
    >
      <div className="p-4 rounded-xl bg-zinc-900/95 border border-amber-500/50 shadow-2xl shadow-amber-950/30 backdrop-blur-md flex items-start justify-between gap-3 ring-1 ring-amber-500/30">
        <div className="flex items-start gap-3 min-w-0">
          <div className="p-2 rounded-lg bg-amber-500/20 text-amber-300 border border-amber-500/30 shrink-0 mt-0.5 animate-pulse">
            <ShieldAlert className="w-5 h-5" />
          </div>
          <div className="min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <h4 className="text-xs font-bold text-zinc-100">
                Action Required: Reboot Approval
              </h4>
              <span className="px-1.5 py-0.2 rounded text-[10px] font-semibold bg-amber-500/20 text-amber-300 border border-amber-500/40">
                {pendingCount} {pendingCount === 1 ? 'Waiting' : 'Waiting'}
              </span>
            </div>
            <p className="text-[11px] text-zinc-300 mt-1 leading-relaxed">
              {pendingCount === 1 ? (
                <>
                  <strong className="text-amber-200">{hostName}</strong> reached the safe reboot checkpoint and requires operator confirmation.
                </>
              ) : (
                <>
                  <strong className="text-amber-200">{pendingCount} workflows</strong> reached safety checkpoints and require operator confirmation.
                </>
              )}
            </p>
            <div className="mt-2.5 flex items-center gap-2">
              <Button
                variant="primary"
                size="sm"
                onClick={onOpenApprovals}
                className="text-xs h-7 px-3 bg-amber-500 hover:bg-amber-400 text-zinc-950 font-semibold shadow-xs shadow-amber-950/40 gap-1 cursor-pointer"
              >
                Review & Approve
                <ArrowRight className="w-3 h-3" />
              </Button>
            </div>
          </div>
        </div>

        <button
          type="button"
          onClick={handleDismiss}
          aria-label="Dismiss approval notification"
          className="p-1 rounded-lg text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800 transition-colors shrink-0"
          title="Dismiss notification (reminder remains in header & sidebar)"
        >
          <X className="w-4 h-4" />
        </button>
      </div>
    </aside>
  )
}
