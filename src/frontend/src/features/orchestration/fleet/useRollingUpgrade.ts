import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getRollingUpgradeStatus,
  sendRollingUpgradeSignal,
  type RollingUpgradeStatusResponse,
} from '../../../api/temporal'

export function useRollingUpgrade(batchId?: string | null) {
  const queryClient = useQueryClient()

  const query = useQuery<RollingUpgradeStatusResponse>({
    queryKey: ['rollingUpgrade', batchId],
    queryFn: () => {
      if (!batchId) throw new Error('No batchId provided')
      return getRollingUpgradeStatus(batchId)
    },
    enabled: Boolean(batchId),
    refetchInterval: (q) => {
      const data = q.state.data
      if (!data) return 1500
      const st = data.executionStatus?.toLowerCase()
      if (st === 'completed' || st === 'failed' || st === 'terminated' || st === 'timedout' || st === 'canceled') {
        return false
      }
      return 1500
    },
  })

  const pauseMutation = useMutation({
    mutationFn: async () => {
      if (!batchId) throw new Error('No batchId provided')
      return sendRollingUpgradeSignal(batchId, 'pause')
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['rollingUpgrade', batchId] })
    },
  })

  const resumeMutation = useMutation({
    mutationFn: async () => {
      if (!batchId) throw new Error('No batchId provided')
      return sendRollingUpgradeSignal(batchId, 'resume')
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['rollingUpgrade', batchId] })
    },
  })

  const abortMutation = useMutation({
    mutationFn: async (reason?: string) => {
      if (!batchId) throw new Error('No batchId provided')
      return sendRollingUpgradeSignal(batchId, 'cancel', reason || 'Operator aborted rolling upgrade')
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['rollingUpgrade', batchId] })
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
    },
  })

  return {
    ...query,
    pauseFleet: pauseMutation.mutate,
    isPausing: pauseMutation.isPending,
    resumeFleet: resumeMutation.mutate,
    isResuming: resumeMutation.isPending,
    abortFleet: abortMutation.mutate,
    isAborting: abortMutation.isPending,
  }
}
