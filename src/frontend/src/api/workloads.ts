import { apiClient } from './client'

export interface ImageUpdateInfo {
  image: string
  currentTag: string
  latestTag?: string | null
  isOutdated: boolean
  updateType?: 'major' | 'minor' | 'patch' | 'floating' | string | null
  latestDigest?: string | null
  message?: string | null
  checkedAt?: string | null
  availableTags?: string[] | null
}

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
  imageUpdate?: ImageUpdateInfo | null
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
  containers?: string[]
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
  secretName?: string
  secretKey?: string
  configMapName?: string
  configMapKey?: string
  containerName?: string
}

export interface AppEnvFromSource {
  secretRef?: string | null
  configMapRef?: string | null
  prefix?: string | null
  containerName?: string | null
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
  kind: 'Deployment' | 'StatefulSet' | 'DaemonSet' | 'CronJob' | 'Job' | 'Pod' | string
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
  envFrom?: AppEnvFromSource[] | null
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
  dnsNames?: string[]
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
  usedBytes?: number | null
  capacityBytes?: number | null
  replicaHealth?: 'Healthy' | 'Degraded' | 'Faulted' | string | null
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

export interface K8sVersionInfo {
  gitVersion: string
  major?: string | null
  minor?: string | null
  platform?: string | null
  latestStableVersion?: string | null
  isOutdated: boolean
  updateType?: 'major' | 'minor' | 'patch' | string | null
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
  unschedulable?: boolean
  kubeletVersion?: string | null
  osImage?: string | null
  kernelVersion?: string | null
  containerRuntime?: string | null
  architecture?: string | null
}

export interface RolloutRevision {
  revision: number
  creationTimestamp?: string | null
  images: string[]
  replicas: number
  readyReplicas: number
  isCurrent: boolean
}

export interface ClusterEvent {
  name: string
  namespace: string
  type: 'Normal' | 'Warning' | string
  reason: string
  message: string
  involvedObjectKind: string
  involvedObjectName: string
  count?: number | null
  lastTimestamp?: string | null
  sourceComponent?: string | null
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
  serverVersion?: K8sVersionInfo | null
}

// APIs
export async function getWorkloads(
  clusterId?: string,
  namespaceName?: string,
  includeAll = false
): Promise<WorkloadAggregationResult> {
  const params = new URLSearchParams()
  if (clusterId) params.append('clusterId', clusterId)
  if (namespaceName) params.append('namespaceName', namespaceName)
  if (includeAll) params.append('includeAll', 'true')

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

export interface ResourceYamlResult {
  name: string
  namespace: string
  kind: string
  yamlContent: string
}

export async function getResourceYaml(
  clusterId: string,
  namespaceName: string,
  name: string,
  kind?: string
): Promise<ResourceYamlResult> {
  const query = kind ? `?kind=${encodeURIComponent(kind)}` : ''
  return apiClient<ResourceYamlResult>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}/yaml${query}`
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

export interface ServicePort {
  name?: string | null
  port: number
  targetPort?: string | null
  protocol: string
  nodePort?: number | null
}

export interface ServiceSummary {
  name: string
  namespace: string
  type: string
  clusterIp?: string | null
  externalIps?: string[] | null
  ports: ServicePort[]
  selector?: Record<string, string> | null
  endpointsCount: number
  creationTimestamp?: string | null
}

export async function getServices(
  clusterId: string,
  namespaceName?: string
): Promise<ServiceSummary[]> {
  const query = namespaceName ? `?namespaceName=${encodeURIComponent(namespaceName)}` : ''
  return apiClient<ServiceSummary[]>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/network/services${query}`
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

export interface K8sServiceDetail {
  name: string
  namespace: string
  type: string
  clusterIp?: string | null
  clusterIps?: string[] | null
  externalIps?: string[] | null
  ports: ServicePort[]
  selector?: Record<string, string> | null
  annotations?: Record<string, string> | null
  labels?: Record<string, string> | null
  endpointsCount: number
  creationTimestamp?: string | null
  rawYaml: string
}

export interface UpdateServicePayload {
  rawYaml?: string
  type?: string
  ports?: ServicePort[]
  selector?: Record<string, string>
  annotations?: Record<string, string>
  labels?: Record<string, string>
}

export async function getService(
  clusterId: string,
  namespaceName: string,
  name: string
): Promise<K8sServiceDetail> {
  return apiClient<K8sServiceDetail>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/network/services/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}`
  )
}

export async function updateService(
  clusterId: string,
  namespaceName: string,
  name: string,
  payload: UpdateServicePayload
): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/network/services/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}`,
    {
      method: 'PUT',
      body: JSON.stringify(payload),
    }
  )
}

export interface K8sIngressDetail {
  name: string
  namespace: string
  ingressClass?: string | null
  hosts: string[]
  paths: IngressRulePath[]
  tlsHosts: string[]
  tlsSecretName?: string | null
  annotations: Record<string, string>
  labels?: Record<string, string> | null
  creationTimestamp?: string | null
  rawYaml: string
}

export interface UpdateIngressPayload {
  rawYaml?: string
  ingressClass?: string
  hosts?: string[]
  paths?: IngressRulePath[]
  tlsEnabled?: boolean
  tlsSecretName?: string
  annotations?: Record<string, string>
}

export async function getIngress(
  clusterId: string,
  namespaceName: string,
  name: string
): Promise<K8sIngressDetail> {
  return apiClient<K8sIngressDetail>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/network/ingresses/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}`
  )
}

export async function updateIngress(
  clusterId: string,
  namespaceName: string,
  name: string,
  payload: UpdateIngressPayload
): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/network/ingresses/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}`,
    {
      method: 'PUT',
      body: JSON.stringify(payload),
    }
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

export async function getPodLogs(
  clusterId: string,
  namespaceName: string,
  podName: string,
  container?: string,
  tailLines = 100
): Promise<{ podName: string; container?: string; logs: string }> {
  const params = new URLSearchParams()
  if (container) params.set('container', container)
  if (tailLines) params.set('tailLines', tailLines.toString())
  const q = params.toString() ? `?${params.toString()}` : ''
  return apiClient<{ podName: string; container?: string; logs: string }>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/${encodeURIComponent(namespaceName)}/pods/${encodeURIComponent(podName)}/logs${q}`
  )
}

export async function getWorkloadRevisions(
  clusterId: string,
  namespaceName: string,
  name: string
): Promise<RolloutRevision[]> {
  return apiClient<RolloutRevision[]>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}/revisions`
  )
}

export async function rollbackWorkloadRevision(
  clusterId: string,
  namespaceName: string,
  name: string,
  revision: number
): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}/rollback`,
    {
      method: 'POST',
      body: JSON.stringify({ revision }),
    }
  )
}

export async function getClusterEvents(
  clusterId?: string,
  namespaceName?: string,
  type?: string
): Promise<ClusterEvent[]> {
  const params = new URLSearchParams()
  if (clusterId) params.set('clusterId', clusterId)
  if (namespaceName) params.set('namespaceName', namespaceName)
  if (type && type !== 'all') params.set('type', type)
  const q = params.toString() ? `?${params.toString()}` : ''
  return apiClient<ClusterEvent[]>(`/api/v1/events${q}`)
}

export async function getCachedImageUpdates(): Promise<Record<string, ImageUpdateInfo>> {
  return apiClient<Record<string, ImageUpdateInfo>>('/api/v1/workloads/image-updates')
}

export async function checkImageUpdates(
  images?: string[],
  force = false
): Promise<Record<string, ImageUpdateInfo>> {
  return apiClient<Record<string, ImageUpdateInfo>>('/api/v1/workloads/image-updates/check', {
    method: 'POST',
    body: JSON.stringify({ images, force }),
  })
}

export interface UpdateWorkloadImagePayload {
  image: string
  kind?: string
  containerName?: string
}

export async function getImageTags(image: string): Promise<string[]> {
  return apiClient<string[]>(`/api/v1/workloads/images/tags?image=${encodeURIComponent(image)}`)
}

export async function updateWorkloadImage(
  clusterId: string,
  namespaceName: string,
  name: string,
  payload: UpdateWorkloadImagePayload
): Promise<{ success: boolean; image: string; message: string }> {
  return apiClient<{ success: boolean; image: string; message: string }>(
    `/api/v1/workloads/${encodeURIComponent(clusterId)}/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}/image`,
    {
      method: 'POST',
      body: JSON.stringify(payload),
    }
  )
}



