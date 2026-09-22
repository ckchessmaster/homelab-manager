import { apiClient } from './client'

export interface ProxmoxTarget {
  node: string
  vmid: number
}

export interface IdracTarget {
  ipAddress: string
}

export interface UnifiPortTarget {
  switchMac: string
  portNumber: number
}

export interface AgentState {
  installed: boolean
  version?: string | null
  lastSeenAt?: string | null
  pendingReboot: boolean
  upgradablePackagesCount: number
  isOnline?: boolean
}

export interface KubernetesTarget {
  clusterId?: string | null
  nodeName?: string | null
}

export interface HypervisorHostSummary {
  hostId: string
  hostname: string
  friendlyName?: string | null
  nodeName?: string | null
}

export interface HostedVmSummary {
  hostId: string
  hostname: string
  friendlyName?: string | null
  vmid: number
  targetType: string
  isOnline: boolean
}

export interface HostVitals {
  cpuUsagePct?: number | null
  memoryUsagePct?: number | null
  diskFreePct?: number | null
  temperatureCelsius?: number | null
  powerWatts?: number | null
  uptimeSeconds?: number | null
  powerState?: string | null
  healthStatus?: string | null
  source?: string | null
}

export interface Host {
  id: string
  hostname: string
  friendlyName?: string | null
  ipAddress: string
  osFamily: string
  targetType: string
  proxmox?: ProxmoxTarget | null
  proxmoxInstanceId?: string | null
  kubernetes?: KubernetesTarget | null
  k8sClusterId?: string | null
  k8sNodeName?: string | null
  idrac?: IdracTarget | null
  idracIp?: string | null
  networkPort?: UnifiPortTarget | null
  agent: AgentState
  createdAt: string
  updatedAt: string
  hypervisor?: HypervisorHostSummary | null
  hostedVms?: HostedVmSummary[] | null
  vitals?: HostVitals | null
}

export interface CreateHostPayload {
  hostname: string
  friendlyName?: string
  ipAddress: string
  osFamily: string
  targetType: string
  proxmoxNode?: string
  proxmoxVmid?: number
  proxmoxInstanceId?: string
  k8sClusterId?: string
  k8sNodeName?: string
  idracIp?: string
  unifiSwitchMac?: string
  unifiSwitchPort?: number
}

export interface UpdateHostPayload {
  hostname?: string
  friendlyName?: string
  ipAddress?: string
  osFamily?: string
  targetType?: string
  proxmoxNode?: string
  proxmoxVmid?: number
  proxmoxInstanceId?: string
  k8sClusterId?: string
  k8sNodeName?: string
  idracIp?: string
  unifiSwitchMac?: string
  unifiSwitchPort?: number
  pendingReboot?: boolean
}

export interface HostFilterParams {
  osFamily?: string
  targetType?: string
  pendingReboot?: boolean
  hasUpdates?: boolean
  search?: string
}

export interface ProxmoxProbePayload {
  baseUrl: string
  apiTokenId: string
  apiTokenSecret: string
  allowSelfSignedCert?: boolean
}

export interface ProxmoxNodeSummary {
  node: string
  status: string
  cpu?: number | null
  maxCpu?: number | null
  memory?: number | null
  maxMemory?: number | null
  uptime?: number | null
}

export interface ProxmoxProbeResult {
  success: boolean
  version?: string | null
  release?: string | null
  repoid?: string | null
  nodes?: ProxmoxNodeSummary[] | null
  errorMessage?: string | null
}

export async function fetchHosts(params?: HostFilterParams): Promise<Host[]> {
  const query = new URLSearchParams()
  if (params?.osFamily) query.set('osFamily', params.osFamily)
  if (params?.targetType) query.set('targetType', params.targetType)
  if (params?.pendingReboot !== undefined) query.set('pendingReboot', String(params.pendingReboot))
  if (params?.hasUpdates !== undefined) query.set('hasUpdates', String(params.hasUpdates))
  if (params?.search) query.set('search', params.search)

  const queryString = query.toString()
  const endpoint = `/api/v1/hosts${queryString ? `?${queryString}` : ''}`
  return apiClient<Host[]>(endpoint)
}

export async function fetchHostById(id: string): Promise<Host> {
  return apiClient<Host>(`/api/v1/hosts/${id}`)
}

export async function createHost(payload: CreateHostPayload): Promise<Host> {
  return apiClient<Host>('/api/v1/hosts', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function updateHost(id: string, payload: UpdateHostPayload): Promise<Host> {
  return apiClient<Host>(`/api/v1/hosts/${id}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  })
}

export async function deleteHost(id: string): Promise<void> {
  return apiClient<void>(`/api/v1/hosts/${id}`, {
    method: 'DELETE',
  })
}

export interface ProxmoxInstanceDto {
  id: string
  name: string
  baseUrl: string
  apiTokenId: string
  apiTokenSecretMasked: string
  hasSecret: boolean
  allowSelfSignedCert: boolean
  taskPollTimeoutSeconds: number
  taskPollIntervalMilliseconds: number
  updatedAt?: string | null
}

export interface SaveProxmoxInstancePayload {
  id?: string
  name: string
  baseUrl: string
  apiTokenId: string
  apiTokenSecret?: string
  allowSelfSignedCert?: boolean
  taskPollTimeoutSeconds?: number
  taskPollIntervalMilliseconds?: number
}

export async function probeProxmox(payload: ProxmoxProbePayload): Promise<ProxmoxProbeResult> {
  return apiClient<ProxmoxProbeResult>('/api/v1/adapters/proxmox/test-connection', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function fetchProxmoxInstances(): Promise<ProxmoxInstanceDto[]> {
  return apiClient<ProxmoxInstanceDto[]>('/api/v1/adapters/proxmox/instances')
}

export async function fetchProxmoxInstance(id: string): Promise<ProxmoxInstanceDto> {
  return apiClient<ProxmoxInstanceDto>(`/api/v1/adapters/proxmox/instances/${encodeURIComponent(id)}`)
}

export async function saveProxmoxInstance(payload: SaveProxmoxInstancePayload): Promise<ProxmoxInstanceDto> {
  return apiClient<ProxmoxInstanceDto>('/api/v1/adapters/proxmox/instances', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function deleteProxmoxInstance(id: string): Promise<{ success: boolean; id: string }> {
  return apiClient<{ success: boolean; id: string }>(`/api/v1/adapters/proxmox/instances/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  })
}

export async function testProxmoxInstanceConnection(id: string): Promise<ProxmoxProbeResult> {
  return apiClient<ProxmoxProbeResult>(`/api/v1/adapters/proxmox/instances/${encodeURIComponent(id)}/test-connection`, {
    method: 'POST',
  })
}

export type AdoptionStepStatus = 'Pending' | 'Running' | 'Completed' | 'Failed'

export interface AdoptionStepEvent {
  stepKey: string
  stepTitle: string
  status: AdoptionStepStatus
  message?: string | null
  timestamp: string
}

export interface AdoptNodePayload {
  hostId?: string | null
  hostname?: string | null
  targetHost: string
  port?: number
  username?: string
  password?: string | null
  privateKey?: string | null
  hubUrl?: string | null
}

export interface NodeAdoptionResponse {
  hostId: string
  success: boolean
  message: string
  steps: AdoptionStepEvent[]
}

export interface BatchAdoptHostItem {
  hostId: string
  targetHost: string
  hostname?: string
}

export interface BatchAdoptNodesPayload {
  hosts: BatchAdoptHostItem[]
  port?: number
  username?: string
  password?: string | null
  privateKey?: string | null
  hubUrl?: string | null
}

export interface BatchAdoptItemResult {
  hostId: string
  hostname: string
  success: boolean
  message: string
}

export interface BatchAdoptNodesResponse {
  totalRequested: number
  succeededCount: number
  failedCount: number
  results: BatchAdoptItemResult[]
}

export async function adoptNode(payload: AdoptNodePayload): Promise<NodeAdoptionResponse> {
  const endpoint = payload.hostId ? `/api/v1/hosts/${payload.hostId}/adopt` : '/api/v1/hosts/adopt'
  return apiClient<NodeAdoptionResponse>(endpoint, {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function adoptNodesBatch(payload: BatchAdoptNodesPayload): Promise<BatchAdoptNodesResponse> {
  return apiClient<BatchAdoptNodesResponse>('/api/v1/hosts/adopt-batch', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}


export interface CorrelatedVm {
  vmid: number
  name: string
  type: string
  status: string
  hostId?: string | null
  hostname?: string | null
  ipAddress?: string | null
  isAgentOnline: boolean
  k8sClusterId?: string | null
  k8sNodeName?: string | null
}

export interface CorrelatedHypervisor {
  hostId?: string | null
  hostname: string
  friendlyName?: string | null
  proxmoxNode: string
  instanceId?: string | null
  isOnline: boolean
}

export interface CorrelatedKubernetes {
  clusterId: string
  nodeName: string
  roles: string[]
  isReady: boolean
  runningPodsCount: number
}

export interface HostCorrelation {
  hostId: string
  hostname: string
  isHypervisor: boolean
  hypervisorNode?: string | null
  hostedVms: CorrelatedVm[]
  isVm: boolean
  hypervisor?: CorrelatedHypervisor | null
  isKubernetesNode: boolean
  kubernetes?: CorrelatedKubernetes | null
}

export interface KubernetesClusterImpact {
  clusterId: string
  nodeName: string
  isControlPlane: boolean
  isOnlyControlPlane: boolean
  totalNodes: number
  readyNodes: number
  runningPodsCount: number
  quorumAtRisk: boolean
  summary: string
}

export interface HostRebootImpact {
  hostId: string
  hostname: string
  isHypervisor: boolean
  hypervisorNode?: string | null
  affectedRunningVms: CorrelatedVm[]
  isKubernetesNode: boolean
  kubernetesImpact?: KubernetesClusterImpact | null
  hasWarnings: boolean
  requiresConfirmation: boolean
  warningMessages: string[]
}

export interface RebootHostResponse {
  jobId: string
  hostId: string
  pipelineId?: string
  status: string
  message: string
}

export interface SyncCorrelationResult {
  success: boolean
  correlatedKubernetesNodes: number
  correlatedProxmoxHosts: number
  totalHostsUpdated: number
  messages: string[]
}

export async function syncHostCorrelations(): Promise<SyncCorrelationResult> {
  return apiClient<SyncCorrelationResult>('/api/v1/hosts/sync-correlation', {
    method: 'POST',
  })
}

export async function fetchHostRebootImpact(id: string): Promise<HostRebootImpact> {
  return apiClient<HostRebootImpact>(`/api/v1/hosts/${id}/reboot-impact`)
}

export async function fetchHostCorrelation(id: string): Promise<HostCorrelation> {
  return apiClient<HostCorrelation>(`/api/v1/hosts/${id}/correlation`)
}

export async function rebootHost(hostId: string, pipelineId?: string, force = false): Promise<RebootHostResponse> {
  return apiClient<RebootHostResponse>(`/api/v1/hosts/${hostId}/reboot`, {
    method: 'POST',
    body: JSON.stringify({ pipelineId, force }),
  })
}

export async function fetchHostVitals(hostId: string): Promise<HostVitals> {
  return apiClient<HostVitals>(`/api/v1/hosts/${hostId}/vitals`)
}

export function isBaremetalHost(h: Host): boolean {
  const isPhysicalTarget =
    h.targetType === 'baremetal' ||
    h.targetType === 'physical' ||
    h.targetType === 'proxmox_node' ||
    h.targetType === 'hypervisor'
  const isNotGuestVm = !h.proxmox || h.proxmox.vmid <= 0
  return isPhysicalTarget && isNotGuestVm
}

export function isProxmoxHost(h: Host): boolean {
  return Boolean(h.proxmox || h.targetType?.startsWith('proxmox') || h.proxmoxInstanceId)
}

export function isKubernetesHost(h: Host): boolean {
  return Boolean(
    h.kubernetes?.clusterId ||
    h.kubernetes?.nodeName ||
    h.k8sClusterId ||
    h.k8sNodeName ||
    h.targetType?.includes('k8s') ||
    h.targetType?.includes('kubernetes')
  )
}

