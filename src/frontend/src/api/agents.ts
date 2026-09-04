import { apiClient } from './client'

export interface OutdatedHostSummary {
  hostId: string
  hostname: string
  currentVersion: string
  isOnline: boolean
}

export interface AgentVersionInfo {
  serverVersion: string
  supportedArchitectures: string[]
  totalInstalledAgents: number
  outdatedAgentsCount: number
  onlineOutdatedCount: number
  outdatedHosts: OutdatedHostSummary[]
}

export interface HostUpdateStatus {
  hostId: string
  hostname: string
  currentVersion: string
  targetVersion: string
  status: 'Dispatched' | 'SkippedOffline' | 'Failed' | string
  message?: string
}

export interface MassUpdateBatchResult {
  batchId: string
  totalTargeted: number
  dispatchedCount: number
  skippedOfflineCount: number
  details: HostUpdateStatus[]
}

export interface MassUpdateRequest {
  hostIds?: string[]
  allOutdated: boolean
}

export async function fetchAgentVersionInfo(): Promise<AgentVersionInfo> {
  return apiClient<AgentVersionInfo>('/api/v1/agents/version-info')
}

export async function triggerMassAgentUpdate(request: MassUpdateRequest): Promise<MassUpdateBatchResult> {
  return apiClient<MassUpdateBatchResult>('/api/v1/agents/mass-update', {
    method: 'POST',
    body: JSON.stringify(request),
  })
}

export async function fetchMassUpdateStatus(batchId: string): Promise<MassUpdateBatchResult> {
  return apiClient<MassUpdateBatchResult>(`/api/v1/agents/mass-update/${batchId}`)
}
