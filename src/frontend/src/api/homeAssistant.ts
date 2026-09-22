import { apiClient } from './client'

export interface HomeAssistantInstanceDto {
  id: string
  name: string
  baseUrl: string
  tokenMasked: string
  hasToken: boolean
  allowSelfSignedCert: boolean
  updatedAt?: string | null
}

export interface SaveHomeAssistantInstancePayload {
  id?: string
  name: string
  baseUrl: string
  token?: string | null
  allowSelfSignedCert?: boolean
}

export interface HomeAssistantTestResult {
  success: boolean
  coreVersion?: string | null
  osVersion?: string | null
  supervisorVersion?: string | null
  hostname?: string | null
  updateAvailable?: boolean | null
  latencyMs: number
  message?: string | null
}

export interface HomeAssistantHostInfo {
  chassis?: string | null
  hostname?: string | null
  kernel?: string | null
  operatingSystem?: string | null
  rebootRequired: boolean
  diskFreeGb?: number | null
  diskTotalGb?: number | null
  diskUsedGb?: number | null
}

export interface HomeAssistantOsInfo {
  version: string
  versionLatest?: string | null
  updateAvailable: boolean
  board?: string | null
  bootSlot?: string | null
}

export interface HomeAssistantCoreInfo {
  version: string
  versionLatest?: string | null
  updateAvailable: boolean
  arch?: string | null
  state?: string | null
}

export interface HomeAssistantSupervisorInfo {
  version: string
  versionLatest?: string | null
  updateAvailable: boolean
  channel?: string | null
  healthy: boolean
  supported: boolean
}

export interface HomeAssistantBackup {
  slug: string
  name: string
  date: string
  type: string
  sizeMb: number
  protected: boolean
}

export interface HomeAssistantOverview {
  instanceId: string
  name: string
  baseUrl: string
  host?: HomeAssistantHostInfo | null
  os?: HomeAssistantOsInfo | null
  core?: HomeAssistantCoreInfo | null
  supervisor?: HomeAssistantSupervisorInfo | null
  recentBackups: HomeAssistantBackup[]
  correlatedHostId?: string | null
  correlatedHostName?: string | null
  latencyMs: number
  fetchedAt?: string | null
}

export interface HomeAssistantConfigCheckResult {
  isValid: boolean
  errors?: string | null
  checkedAt: string
}

export interface CreateBackupRequest {
  name?: string
  password?: string
}

export interface CreateBackupResponse {
  success: boolean
  jobId?: string | null
  slug?: string | null
  message?: string | null
}

export const homeAssistantApi = {
  getInstances: async (): Promise<HomeAssistantInstanceDto[]> => {
    return apiClient<HomeAssistantInstanceDto[]>('/api/v1/adapters/home-assistant/instances')
  },

  getInstance: async (id: string): Promise<HomeAssistantInstanceDto> => {
    return apiClient<HomeAssistantInstanceDto>(`/api/v1/adapters/home-assistant/instances/${encodeURIComponent(id)}`)
  },

  saveInstance: async (payload: SaveHomeAssistantInstancePayload): Promise<HomeAssistantInstanceDto> => {
    return apiClient<HomeAssistantInstanceDto>('/api/v1/adapters/home-assistant/instances', {
      method: 'POST',
      body: JSON.stringify(payload),
    })
  },

  deleteInstance: async (id: string): Promise<void> => {
    await apiClient<void>(`/api/v1/adapters/home-assistant/instances/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    })
  },

  testConnection: async (payload: SaveHomeAssistantInstancePayload): Promise<HomeAssistantTestResult> => {
    return apiClient<HomeAssistantTestResult>('/api/v1/adapters/home-assistant/test-connection', {
      method: 'POST',
      body: JSON.stringify(payload),
    })
  },

  testInstanceConnection: async (id: string): Promise<HomeAssistantTestResult> => {
    return apiClient<HomeAssistantTestResult>(`/api/v1/adapters/home-assistant/instances/${encodeURIComponent(id)}/test-connection`, {
      method: 'POST',
    })
  },

  getOverview: async (id: string): Promise<HomeAssistantOverview> => {
    return apiClient<HomeAssistantOverview>(`/api/v1/adapters/home-assistant/instances/${encodeURIComponent(id)}/overview`)
  },

  checkConfig: async (id: string): Promise<HomeAssistantConfigCheckResult> => {
    return apiClient<HomeAssistantConfigCheckResult>(`/api/v1/adapters/home-assistant/instances/${encodeURIComponent(id)}/core/check`, {
      method: 'POST',
    })
  },

  restartCore: async (id: string): Promise<{ message: string }> => {
    return apiClient<{ message: string }>(`/api/v1/adapters/home-assistant/instances/${encodeURIComponent(id)}/core/restart`, {
      method: 'POST',
    })
  },

  rebootHost: async (id: string): Promise<{ message: string }> => {
    return apiClient<{ message: string }>(`/api/v1/adapters/home-assistant/instances/${encodeURIComponent(id)}/host/reboot`, {
      method: 'POST',
    })
  },

  updateOs: async (id: string): Promise<{ message: string }> => {
    return apiClient<{ message: string }>(`/api/v1/adapters/home-assistant/instances/${encodeURIComponent(id)}/os/update`, {
      method: 'POST',
    })
  },

  getBackups: async (id: string): Promise<HomeAssistantBackup[]> => {
    return apiClient<HomeAssistantBackup[]>(`/api/v1/adapters/home-assistant/instances/${encodeURIComponent(id)}/backups`)
  },

  createBackup: async (id: string, payload?: CreateBackupRequest): Promise<CreateBackupResponse> => {
    return apiClient<CreateBackupResponse>(`/api/v1/adapters/home-assistant/instances/${encodeURIComponent(id)}/backups`, {
      method: 'POST',
      body: JSON.stringify(payload || {}),
    })
  }
}
