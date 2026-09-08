import { apiClient } from './client'

export interface WorkloadSummary {
  clusterId: string
  clusterName: string
  namespace: string
  name: string
  desiredReplicas: number
  readyReplicas: number
  availableReplicas: number
  images: string[]
  creationTimestamp?: string | null
  status: 'Ready' | 'Progressing' | 'Degraded' | 'ScaledDown' | string
}

export interface WorkloadAggregationResult {
  items: WorkloadSummary[]
  clusters: string[]
  namespaces: string[]
  totalDeployments: number
  healthyDeployments: number
}

export interface PodSummary {
  name: string
  namespace: string
  phase: string
  nodeName?: string | null
  podIp?: string | null
  restartCount: number
  isReady: boolean
  startTime?: string | null
}

export interface ScaleWorkloadPayload {
  replicas: number
}

export async function getWorkloads(
  clusterId?: string,
  namespaceName?: string
): Promise<WorkloadAggregationResult> {
  const params = new URLSearchParams()
  if (clusterId) params.append('clusterId', clusterId)
  if (namespaceName) params.append('namespaceName', namespaceName)

  const query = params.toString() ? `?${params.toString()}` : ''
  return apiClient<WorkloadAggregationResult>(`/api/v1/workloads${query}`)
}

export async function getWorkloadPods(
  clusterId: string,
  namespaceName: string,
  name: string
): Promise<PodSummary[]> {
  return apiClient<PodSummary[]>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}/pods`
  )
}

export async function restartWorkload(
  clusterId: string,
  namespaceName: string,
  name: string
): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}/restart`,
    {
      method: 'POST',
    }
  )
}

export async function scaleWorkload(
  clusterId: string,
  namespaceName: string,
  name: string,
  replicas: number
): Promise<{ success: boolean; replicas: number; message: string }> {
  return apiClient<{ success: boolean; replicas: number; message: string }>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}/scale`,
    {
      method: 'POST',
      body: JSON.stringify({ replicas }),
    }
  )
}
