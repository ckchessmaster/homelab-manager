import { apiClient } from './client'

export interface KubernetesClusterDto {
  id: string
  name: string
  apiServerUrl?: string | null
  contextName?: string | null
  hasKubeConfig: boolean
  hasToken: boolean
  skipTlsVerify: boolean
  updatedAt?: string | null
}

export interface SaveKubernetesClusterPayload {
  id?: string
  name: string
  apiServerUrl?: string | null
  kubeConfigRaw?: string | null
  token?: string | null
  contextName?: string | null
  skipTlsVerify?: boolean
}

export interface KubernetesClusterTestResult {
  success: boolean
  serverVersion?: string | null
  nodeCount: number
  latencyMs: number
  message?: string | null
}

export interface K8sDiscoveredNode {
  name: string
  internalIp?: string | null
  roles: string[]
  isReady: boolean
  unschedulable: boolean
  osImage?: string | null
  kernelVersion?: string | null
  containerRuntimeVersion?: string | null
  labels: Record<string, string>
}

export interface K8sDeploymentSummary {
  name: string
  namespace: string
  desiredReplicas: number
  readyReplicas: number
  availableReplicas: number
  images: string[]
  creationTimestamp?: string | null
}

export interface K8sPodSummary {
  name: string
  namespace: string
  phase: string
  nodeName?: string | null
  podIp?: string | null
  restartCount: number
  isReady: boolean
  startTime?: string | null
}

export async function fetchKubernetesClusters(): Promise<KubernetesClusterDto[]> {
  return apiClient<KubernetesClusterDto[]>('/api/v1/adapters/k8s/clusters')
}

export async function fetchKubernetesCluster(id: string): Promise<KubernetesClusterDto> {
  return apiClient<KubernetesClusterDto>(`/api/v1/adapters/k8s/clusters/${encodeURIComponent(id)}`)
}

export async function saveKubernetesCluster(payload: SaveKubernetesClusterPayload): Promise<KubernetesClusterDto> {
  return apiClient<KubernetesClusterDto>('/api/v1/adapters/k8s/clusters', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function deleteKubernetesCluster(id: string): Promise<{ message: string }> {
  return apiClient<{ message: string }>(`/api/v1/adapters/k8s/clusters/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  })
}

export async function testKubernetesClusterConnection(id: string): Promise<KubernetesClusterTestResult> {
  return apiClient<KubernetesClusterTestResult>(`/api/v1/adapters/k8s/clusters/${encodeURIComponent(id)}/test-connection`, {
    method: 'POST',
  })
}

export async function testKubernetesClusterPreflight(payload: SaveKubernetesClusterPayload): Promise<KubernetesClusterTestResult> {
  return apiClient<KubernetesClusterTestResult>('/api/v1/adapters/k8s/test-connection', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function fetchClusterNodes(id: string): Promise<K8sDiscoveredNode[]> {
  return apiClient<K8sDiscoveredNode[]>(`/api/v1/adapters/k8s/clusters/${encodeURIComponent(id)}/nodes`)
}

export async function cordonClusterNode(id: string, nodeName: string): Promise<{ nodeName: string; unschedulable: boolean }> {
  return apiClient<{ nodeName: string; unschedulable: boolean }>(`/api/v1/adapters/k8s/clusters/${encodeURIComponent(id)}/nodes/${encodeURIComponent(nodeName)}/cordon`, {
    method: 'POST',
  })
}

export async function uncordonClusterNode(id: string, nodeName: string): Promise<{ nodeName: string; unschedulable: boolean }> {
  return apiClient<{ nodeName: string; unschedulable: boolean }>(`/api/v1/adapters/k8s/clusters/${encodeURIComponent(id)}/nodes/${encodeURIComponent(nodeName)}/uncordon`, {
    method: 'POST',
  })
}

export async function drainClusterNode(id: string, nodeName: string, timeoutSeconds = 180): Promise<{ success: boolean; evictedPodCount: number; remainingPods: number; errorMessage?: string }> {
  return apiClient<{ success: boolean; evictedPodCount: number; remainingPods: number; errorMessage?: string }>(`/api/v1/adapters/k8s/clusters/${encodeURIComponent(id)}/nodes/${encodeURIComponent(nodeName)}/drain`, {
    method: 'POST',
    body: JSON.stringify({ nodeName, timeoutSeconds, ignoreDaemonSets: true, deleteEmptyDirData: true }),
  })
}

export async function fetchClusterNamespaces(id: string): Promise<string[]> {
  return apiClient<string[]>(`/api/v1/adapters/k8s/clusters/${encodeURIComponent(id)}/namespaces`)
}

export async function fetchClusterDeployments(id: string, namespace?: string): Promise<K8sDeploymentSummary[]> {
  const query = namespace ? `?namespaceName=${encodeURIComponent(namespace)}` : ''
  return apiClient<K8sDeploymentSummary[]>(`/api/v1/adapters/k8s/clusters/${encodeURIComponent(id)}/workloads/deployments${query}`)
}

export async function restartClusterDeployment(id: string, namespace: string, name: string): Promise<{ message: string }> {
  return apiClient<{ message: string }>(`/api/v1/adapters/k8s/clusters/${encodeURIComponent(id)}/workloads/deployments/${encodeURIComponent(namespace)}/${encodeURIComponent(name)}/restart`, {
    method: 'POST',
  })
}

export async function scaleClusterDeployment(id: string, namespace: string, name: string, replicas: number): Promise<{ replicas: number; message: string }> {
  return apiClient<{ replicas: number; message: string }>(`/api/v1/adapters/k8s/clusters/${encodeURIComponent(id)}/workloads/deployments/${encodeURIComponent(namespace)}/${encodeURIComponent(name)}/scale`, {
    method: 'POST',
    body: JSON.stringify({ replicas }),
  })
}

export async function fetchClusterPods(id: string, namespace?: string, nodeName?: string): Promise<K8sPodSummary[]> {
  const params = new URLSearchParams()
  if (namespace) params.set('namespaceName', namespace)
  if (nodeName) params.set('nodeName', nodeName)
  const query = params.toString() ? `?${params.toString()}` : ''
  return apiClient<K8sPodSummary[]>(`/api/v1/adapters/k8s/clusters/${encodeURIComponent(id)}/workloads/pods${query}`)
}
