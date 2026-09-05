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
