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
  kind?: 'Deployment' | 'StatefulSet' | 'DaemonSet' | 'CronJob' | string
  isProtected?: boolean
  schedule?: string | null
  lastScheduleTime?: string | null
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

export interface AppPortMapping {
  name: string
  containerPort: number
  servicePort: number
  protocol?: string
}

export interface AppEnvVar {
  key: string
  value: string
  isSecret?: boolean
}

export interface AppVolumeMount {
  name: string
  mountPath: string
  pvcName: string
  storageSize?: string
  storageClass?: string
}

export interface AppBundle {
  name: string
  namespace: string
  kind: 'Deployment' | 'StatefulSet'
  replicas: number
  image: string
  ports: AppPortMapping[]
  ingressHost?: string | null
  ingressPath?: string | null
  tlsEnabled?: boolean
  environmentVariables: AppEnvVar[]
  volumeMounts: AppVolumeMount[]
  cpuRequest?: string | null
  cpuLimit?: string | null
  memoryRequest?: string | null
  memoryLimit?: string | null
  rawYaml?: string | null
}

export interface ApplyResult {
  success: boolean
  message: string
  affectedResources: string[]
  warnings?: string[] | null
  diff?: string | null
}

export interface DeleteOptions {
  deleteWorkload?: boolean
  deleteService?: boolean
  deleteIngress?: boolean
  deletePvc?: boolean
  confirmedName?: string | null
}

export interface IngressRulePath {
  path: string
  pathType: string
  serviceName: string
  servicePort: number
  endpointsCount: number
}

export interface IngressSummary {
  name: string
  namespace: string
  ingressClass?: string | null
  hosts: string[]
  paths: IngressRulePath[]
  tlsHosts: string[]
  annotations: Record<string, string>
  creationTimestamp?: string | null
}

export interface CertificateSummary {
  name: string
  namespace: string
  issuer?: string | null
  secretName?: string | null
  isReady: boolean
  renewalTime?: string | null
  notAfter?: string | null
  conditions: string[]
}

export interface PvcSummary {
  name: string
  namespace: string
  status: string
  volumeName?: string | null
  capacity?: string | null
  storageClass?: string | null
  accessModes: string[]
  mountingPods: string[]
  creationTimestamp?: string | null
}

export interface StorageClassSummary {
  name: string
  provisioner: string
  reclaimPolicy: string
  volumeBindingMode: string
  isDefault: boolean
}

export interface StorageOverview {
  totalPvcs: number
  boundPvcs: number
  totalCapacityBytes: number
  pvcs: PvcSummary[]
  storageClasses: StorageClassSummary[]
  longhornDetected: boolean
}

export interface NodeVital {
  nodeName: string
  cpuUsageMillis: number
  cpuAllocatableMillis: number
  memoryUsageBytes: number
  memoryAllocatableBytes: number
  diskPressure: boolean
  memoryPressure: boolean
  pidPressure: boolean
  ready: boolean
}

export interface PodVital {
  podName: string
  namespace: string
  cpuUsageMillis: number
  memoryUsageBytes: number
}

export interface ClusterVitals {
  metricsServerAvailable: boolean
  totalCpuUsageMillis: number
  totalCpuAllocatableMillis: number
  totalMemoryUsageBytes: number
  totalMemoryAllocatableBytes: number
  nodes: NodeVital[]
  topPods: PodVital[]
}

// APIs
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
  name: string,
  kind?: string
): Promise<{ success: boolean; message: string }> {
  const query = kind ? `?kind=${encodeURIComponent(kind)}` : ''
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}/restart${query}`,
    { method: 'POST' }
  )
}

export async function recreateWorkloadPods(
  clusterId: string,
  namespaceName: string,
  name: string,
  kind?: string
): Promise<{ success: boolean; message: string }> {
  const query = kind ? `?kind=${encodeURIComponent(kind)}` : ''
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}/recreate${query}`,
    { method: 'POST' }
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

export async function triggerCronJob(
  clusterId: string,
  namespaceName: string,
  name: string
): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/${encodeURIComponent(namespaceName)}/cronjobs/${encodeURIComponent(name)}/trigger`,
    { method: 'POST' }
  )
}

export async function getAppBundle(
  clusterId: string,
  namespaceName: string,
  name: string
): Promise<AppBundle> {
  return apiClient<AppBundle>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}/bundle`
  )
}

export async function applyManifestYaml(
  clusterId: string,
  yamlContent: string,
  dryRun = false
): Promise<ApplyResult> {
  return apiClient<ApplyResult>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/apply`,
    {
      method: 'POST',
      body: JSON.stringify({ yamlContent, dryRun }),
    }
  )
}

export async function deleteAppBundle(
  clusterId: string,
  namespaceName: string,
  name: string,
  options: DeleteOptions
): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}/delete`,
    {
      method: 'POST',
      body: JSON.stringify(options),
    }
  )
}

export async function getIngresses(
  clusterId: string,
  namespaceName?: string
): Promise<IngressSummary[]> {
  const query = namespaceName ? `?namespaceName=${encodeURIComponent(namespaceName)}` : ''
  return apiClient<IngressSummary[]>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/network/ingresses${query}`
  )
}

export async function getCertificates(
  clusterId: string,
  namespaceName?: string
): Promise<CertificateSummary[]> {
  const query = namespaceName ? `?namespaceName=${encodeURIComponent(namespaceName)}` : ''
  return apiClient<CertificateSummary[]>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/network/certificates${query}`
  )
}

export async function getStorageOverview(
  clusterId: string,
  namespaceName?: string
): Promise<StorageOverview> {
  const query = namespaceName ? `?namespaceName=${encodeURIComponent(namespaceName)}` : ''
  return apiClient<StorageOverview>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/storage${query}`
  )
}

export async function getClusterVitals(
  clusterId: string
): Promise<ClusterVitals> {
  return apiClient<ClusterVitals>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/vitals`
  )
}

export const SYSTEM_CRITICAL_NAMESPACES = [
  'default',
  'kube-system',
  'kube-public',
  'kube-node-lease',
  'ingress-nginx',
  'cilium',
  'cert-manager',
  'longhorn-system',
  'controlplane',
  'monitoring',
]

export function isSystemCriticalNamespace(ns: string): boolean {
  return SYSTEM_CRITICAL_NAMESPACES.some((p) => p.toLowerCase() === ns.toLowerCase())
}

export async function deleteNamespace(
  clusterId: string,
  namespaceName: string,
  confirmedName: string
): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/namespaces/${encodeURIComponent(namespaceName)}/delete`,
    {
      method: 'POST',
      body: JSON.stringify({ confirmedName }),
    }
  )
}

export async function createNamespace(
  clusterId?: string,
  name: string = '',
  labels?: Record<string, string>,
  annotations?: Record<string, string>
): Promise<{ success: boolean; message: string }> {
  const cleanCluster = clusterId?.trim() ?? ''
  const endpoint = cleanCluster
    ? `/api/v1/kubernetes/${encodeURIComponent(cleanCluster)}/namespaces`
    : `/api/v1/kubernetes/namespaces`

  return apiClient<{ success: boolean; message: string }>(
    endpoint,
    {
      method: 'POST',
      body: JSON.stringify({ name, labels, annotations }),
    }
  )
}

