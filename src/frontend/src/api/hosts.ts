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
}

export interface Host {
  id: string
  hostname: string
  friendlyName?: string | null
  ipAddress: string
  osFamily: string
  targetType: string
  proxmox?: ProxmoxTarget | null
  idrac?: IdracTarget | null
  networkPort?: UnifiPortTarget | null
  agent: AgentState
  createdAt: string
  updatedAt: string
}

export interface CreateHostPayload {
  hostname: string
  friendlyName?: string
  ipAddress: string
  osFamily: string
  targetType: string
  proxmoxNode?: string
  proxmoxVmid?: number
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

export async function probeProxmox(payload: ProxmoxProbePayload): Promise<ProxmoxProbeResult> {
  return apiClient<ProxmoxProbeResult>('/api/v1/adapters/proxmox/test-connection', {
    method: 'POST',
    body: JSON.stringify(payload),
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

export async function adoptNode(payload: AdoptNodePayload): Promise<NodeAdoptionResponse> {
  const endpoint = payload.hostId ? `/api/v1/hosts/${payload.hostId}/adopt` : '/api/v1/hosts/adopt'
  return apiClient<NodeAdoptionResponse>(endpoint, {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

