import { useQuery } from '@tanstack/react-query'
import { fetchPipelines, type PipelineProfile } from '../../api/pipelines'
import type { Host } from '../../api/hosts'

export const PIPELINES_QUERY_KEY = ['pipelines']

export function usePipelines() {
  return useQuery<PipelineProfile[]>({
    queryKey: PIPELINES_QUERY_KEY,
    queryFn: fetchPipelines,
    staleTime: 60000,
  })
}

export function getRecommendedPipelineId(host?: Host | null): string {
  if (!host) return 'standard-os-upgrade'
  if (host.targetType?.toLowerCase() === 'k8s_node') {
    return 'k8s-node-rolling-upgrade'
  }
  return 'standard-os-upgrade'
}
