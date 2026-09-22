import { apiClient } from './client'
import type { ProxmoxProbeResult } from './hosts'

export interface ProxmoxConfig {
  baseUrl: string
  apiTokenId: string
  apiTokenSecretMasked: string
  hasSecret: boolean
  allowSelfSignedCert: boolean
  taskPollTimeoutSeconds?: number
  taskPollIntervalMilliseconds?: number
  updatedAt?: string | null
}

export interface SaveProxmoxConfigPayload {
  baseUrl: string
  apiTokenId: string
  apiTokenSecret?: string
  allowSelfSignedCert?: boolean
  taskPollTimeoutSeconds?: number
  taskPollIntervalMilliseconds?: number
}

export interface ProxmoxProbePayload {
  baseUrl?: string
  apiTokenId?: string
  apiTokenSecret?: string
  allowSelfSignedCert?: boolean
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

export async function fetchProxmoxConfig(): Promise<ProxmoxConfig> {
  return apiClient<ProxmoxConfig>('/api/v1/adapters/proxmox/config')
}

export async function saveProxmoxConfig(payload: SaveProxmoxConfigPayload): Promise<ProxmoxConfig> {
  return apiClient<ProxmoxConfig>('/api/v1/adapters/proxmox/config', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
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

export interface ProxmoxNodeVitals {
  node: string
  status: string
  cpuUsagePct?: number | null
  maxCpu?: number | null
  memoryUsedBytes?: number | null
  memoryMaxBytes?: number | null
  memoryUsagePct?: number | null
  uptimeSeconds?: number | null
}

export interface ProxmoxVitals {
  instanceId: string
  instanceName: string
  version?: string | null
  totalNodes: number
  onlineNodes: number
  nodes: ProxmoxNodeVitals[]
  fetchedAt: string
}

export async function fetchProxmoxVitals(id: string): Promise<ProxmoxVitals> {
  return apiClient<ProxmoxVitals>(`/api/v1/adapters/proxmox/instances/${encodeURIComponent(id)}/vitals`)
}
