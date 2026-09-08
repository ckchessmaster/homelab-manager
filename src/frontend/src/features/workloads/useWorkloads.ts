import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getWorkloads,
  getWorkloadPods,
  restartWorkload,
  scaleWorkload,
  type WorkloadAggregationResult,
  type PodSummary,
} from '../../api/workloads'

export function useWorkloads(clusterId?: string, namespaceName?: string) {
  return useQuery<WorkloadAggregationResult>({
    queryKey: ['workloads', clusterId || 'all', namespaceName || 'all'],
    queryFn: () => getWorkloads(clusterId, namespaceName),
    refetchInterval: 10000,
  })
}

export function useWorkloadPods(
  clusterId?: string,
  namespaceName?: string,
  name?: string,
  enabled: boolean = true
) {
  return useQuery<PodSummary[]>({
    queryKey: ['workloadPods', clusterId, namespaceName, name],
    queryFn: () =>
      clusterId && namespaceName && name
        ? getWorkloadPods(clusterId, namespaceName, name)
        : Promise.resolve([]),
    enabled: Boolean(enabled && clusterId && namespaceName && name),
    refetchInterval: 5000,
  })
}

export function useRestartWorkload() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      clusterId,
      namespaceName,
      name,
    }: {
      clusterId: string
      namespaceName: string
      name: string
    }) => restartWorkload(clusterId, namespaceName, name),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
      queryClient.invalidateQueries({ queryKey: ['workloadPods'] })
    },
  })
}

export function useScaleWorkload() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      clusterId,
      namespaceName,
      name,
      replicas,
    }: {
      clusterId: string
      namespaceName: string
      name: string
      replicas: number
    }) => scaleWorkload(clusterId, namespaceName, name, replicas),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
      queryClient.invalidateQueries({ queryKey: ['workloadPods'] })
    },
  })
}
