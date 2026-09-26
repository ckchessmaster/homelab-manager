import { apiClient } from './client'

export interface AgentBinaryPlatformDto {
  architecture: string
  displayName: string
  fileName: string
  isAvailable: boolean
  sizeBytes?: number | null
  lastModifiedAtUtc?: string | null
  downloadPath: string
}

export interface AgentBinaryStatusDto {
  currentInstalledVersion: string
  latestAvailableVersion?: string | null
  lastCheckedAtUtc?: string | null
  lastDownloadedAtUtc?: string | null
  status: string
  lastError?: string | null
  isSyncing: boolean
  autoSyncEnabled: boolean
  syncIntervalHours: number
  repository: string
  platforms: AgentBinaryPlatformDto[]
}

export interface AgentBinarySyncResultDto {
  success: boolean
  version?: string | null
  message: string
  updatedBinaries: string[]
  status: AgentBinaryStatusDto
}

export async function fetchAgentBinaryStatus(): Promise<AgentBinaryStatusDto> {
  return apiClient<AgentBinaryStatusDto>('/api/v1/agents/binaries/status')
}

export async function syncAgentBinaries(force: boolean = false): Promise<AgentBinarySyncResultDto> {
  return apiClient<AgentBinarySyncResultDto>('/api/v1/agents/binaries/sync', {
    method: 'POST',
    body: JSON.stringify({ force }),
  })
}
