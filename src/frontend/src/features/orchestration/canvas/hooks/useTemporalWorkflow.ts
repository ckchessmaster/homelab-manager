import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getTemporalWorkflowStatus,
  sendTemporalWorkflowSignal,
  type TemporalWorkflowStatusResponse,
} from '../../../../api/temporal'

export function useTemporalWorkflow(workflowId?: string | null) {
  const queryClient = useQueryClient()

  const query = useQuery<TemporalWorkflowStatusResponse>({
    queryKey: ['temporalWorkflow', workflowId],
    queryFn: () => {
      if (!workflowId) throw new Error('No workflowId provided')
      return getTemporalWorkflowStatus(workflowId)
    },
    enabled: Boolean(workflowId),
    refetchInterval: (query) => {
      const data = query.state.data
      if (!data) return 2000
      const status = data.executionStatus?.toLowerCase()
      if (status === 'completed' || status === 'failed' || status === 'terminated' || status === 'timedout' || status === 'canceled') {
        return false
      }
      return 2000
    },
  })

  const approveRebootMutation = useMutation({
    mutationFn: async () => {
      if (!workflowId) throw new Error('No workflowId provided')
      return sendTemporalWorkflowSignal(workflowId, 'approve-reboot')
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['temporalWorkflow', workflowId] })
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
    },
  })

  const rejectWorkflowMutation = useMutation({
    mutationFn: async (reason?: string) => {
      if (!workflowId) throw new Error('No workflowId provided')
      return sendTemporalWorkflowSignal(workflowId, 'cancel', reason || 'Operator rejected from UI')
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['temporalWorkflow', workflowId] })
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
    },
  })

  return {
    ...query,
    approveReboot: approveRebootMutation.mutate,
    isApproving: approveRebootMutation.isPending,
    rejectWorkflow: rejectWorkflowMutation.mutate,
    isRejecting: rejectWorkflowMutation.isPending,
  }
}
