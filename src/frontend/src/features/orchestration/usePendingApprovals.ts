import { useMemo, useState } from 'react'
import { useQueryClient, useMutation } from '@tanstack/react-query'
import { useJobs } from './useJobs'
import { sendTemporalWorkflowSignal } from '../../api/temporal'
import { cancelJob, type JobSummary } from '../../api/jobs'

export function isJobAwaitingApproval(job: JobSummary): boolean {
  const status = job.status?.toLowerCase()
  if (status === 'awaitingapproval') return true
  const step = job.activeStep?.toLowerCase() || ''
  return step.includes('approval') || step.includes('awaiting operator')
}

export function usePendingApprovals() {
  const queryClient = useQueryClient()
  const { data: jobs, isLoading } = useJobs()
  const [actionInProgressId, setActionInProgressId] = useState<string | null>(null)

  const pendingApprovals = useMemo(() => {
    if (!jobs) return []
    return jobs.filter((j) => {
      // Exclude ad-hoc shell commands
      if (j.pipelineId === 'adhoc-command' || j.pipelineId === 'command') return false
      return isJobAwaitingApproval(j)
    })
  }, [jobs])

  const approveMutation = useMutation({
    mutationFn: async (job: JobSummary) => {
      setActionInProgressId(job.id)
      const workflowId = job.id.startsWith('host-upgrade-') ? job.id : `host-upgrade-${job.id}`
      return sendTemporalWorkflowSignal(workflowId, 'approve-reboot')
    },
    onSettled: () => {
      setActionInProgressId(null)
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      queryClient.invalidateQueries({ queryKey: ['temporalWorkflow'] })
    },
  })

  const rejectMutation = useMutation({
    mutationFn: async ({ job, reason }: { job: JobSummary; reason?: string }) => {
      setActionInProgressId(job.id)
      const workflowId = job.id.startsWith('host-upgrade-') ? job.id : `host-upgrade-${job.id}`
      const cancelReason = reason || 'Operator rejected from UI approval gate'
      try {
        await sendTemporalWorkflowSignal(workflowId, 'cancel', cancelReason)
      } catch {
        // Fall back to direct cancelJob if temporal signal encounters an issue
        await cancelJob(job.id, cancelReason)
      }
    },
    onSettled: () => {
      setActionInProgressId(null)
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      queryClient.invalidateQueries({ queryKey: ['temporalWorkflow'] })
    },
  })

  const approveAllMutation = useMutation({
    mutationFn: async () => {
      setActionInProgressId('all')
      await Promise.allSettled(
        pendingApprovals.map((job) => {
          const workflowId = job.id.startsWith('host-upgrade-') ? job.id : `host-upgrade-${job.id}`
          return sendTemporalWorkflowSignal(workflowId, 'approve-reboot')
        })
      )
    },
    onSettled: () => {
      setActionInProgressId(null)
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      queryClient.invalidateQueries({ queryKey: ['temporalWorkflow'] })
    },
  })

  return {
    pendingApprovals,
    pendingCount: pendingApprovals.length,
    hasPendingApprovals: pendingApprovals.length > 0,
    isLoading,
    actionInProgressId,
    approveJob: (job: JobSummary) => approveMutation.mutateAsync(job),
    rejectJob: (job: JobSummary, reason?: string) => rejectMutation.mutateAsync({ job, reason }),
    approveAll: () => approveAllMutation.mutateAsync(),
    isApprovingAny: approveMutation.isPending || approveAllMutation.isPending,
    isRejectingAny: rejectMutation.isPending,
  }
}
