import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  fetchAgentVersionInfo,
  triggerMassAgentUpdate,
  fetchMassUpdateStatus,
  type MassUpdateRequest,
} from '../../api/agents'
import { HOSTS_QUERY_KEY } from './useHosts'

export const AGENT_VERSION_QUERY_KEY = ['agent-version-info']

export function useAgentVersionInfo() {
  return useQuery({
    queryKey: AGENT_VERSION_QUERY_KEY,
    queryFn: fetchAgentVersionInfo,
    refetchInterval: 15000,
  })
}

export function useTriggerMassUpdate() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: MassUpdateRequest) => triggerMassAgentUpdate(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: HOSTS_QUERY_KEY })
      queryClient.invalidateQueries({ queryKey: AGENT_VERSION_QUERY_KEY })
    },
  })
}

export function useMassUpdateStatus(batchId: string | null) {
  return useQuery({
    queryKey: ['mass-update-batch', batchId],
    queryFn: () => (batchId ? fetchMassUpdateStatus(batchId) : Promise.reject('No batch ID')),
    enabled: Boolean(batchId),
    refetchInterval: 2000,
  })
}
