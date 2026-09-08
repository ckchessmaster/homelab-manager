import { apiClient } from './client'

export interface IdracInstanceDto {
  id: string
  name: string
  bmcUrl: string
  username: string
  passwordMasked: string
  hasPassword: boolean
  hostnameOrIp?: string | null
  allowSelfSignedCert: boolean
  updatedAt?: string | null
}

export interface SaveIdracInstancePayload {
  id?: string
  name: string
  bmcUrl: string
  username: string
  password?: string | null
  hostnameOrIp?: string | null
  allowSelfSignedCert?: boolean
}

export interface IdracTestResult {
  success: boolean
  powerState?: string | null
  model?: string | null
  biosVersion?: string | null
  healthStatus?: string | null
  serialNumber?: string | null
  latencyMs: number
  message?: string | null
}

export interface IdracSensorReading {
  name: string
  currentReadingCelsius: number
  criticalThresholdCelsius?: number | null
  status: string
}

export interface IdracFanReading {
  name: string
  readingRpm: number
  status: string
}

export interface IdracVitals {
  powerState: string
  healthStatus?: string | null
  model?: string | null
  biosVersion?: string | null
  serialNumber?: string | null
  powerConsumptionWatts?: number | null
  temperatures: IdracSensorReading[]
  fans: IdracFanReading[]
}

export interface IdracPowerActionResponse {
  success: boolean
  message: string
  powerState?: string | null
}

export async function fetchIdracInstances(): Promise<IdracInstanceDto[]> {
  return apiClient<IdracInstanceDto[]>('/api/v1/adapters/idrac/instances')
}

export async function fetchIdracInstance(id: string): Promise<IdracInstanceDto> {
  return apiClient<IdracInstanceDto>(`/api/v1/adapters/idrac/instances/${encodeURIComponent(id)}`)
}

export async function saveIdracInstance(payload: SaveIdracInstancePayload): Promise<IdracInstanceDto> {
  return apiClient<IdracInstanceDto>('/api/v1/adapters/idrac/instances', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function deleteIdracInstance(id: string): Promise<void> {
  await apiClient<void>(`/api/v1/adapters/idrac/instances/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  })
}

export async function testIdracConnection(payload: SaveIdracInstancePayload): Promise<IdracTestResult> {
  return apiClient<IdracTestResult>('/api/v1/adapters/idrac/test-connection', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function testSavedIdracConnection(id: string): Promise<IdracTestResult> {
  return apiClient<IdracTestResult>(`/api/v1/adapters/idrac/instances/${encodeURIComponent(id)}/test-connection`, {
    method: 'POST',
  })
}

export async function fetchIdracVitals(id: string): Promise<IdracVitals> {
  return apiClient<IdracVitals>(`/api/v1/adapters/idrac/instances/${encodeURIComponent(id)}/vitals`)
}

export async function sendIdracPowerAction(id: string, resetType: string): Promise<IdracPowerActionResponse> {
  return apiClient<IdracPowerActionResponse>(`/api/v1/adapters/idrac/instances/${encodeURIComponent(id)}/power`, {
    method: 'POST',
    body: JSON.stringify({ resetType }),
  })
}

export async function sendIdracPowerActionByIp(
  idracIp: string,
  resetType: string,
  username?: string,
  password?: string
): Promise<IdracPowerActionResponse> {
  return apiClient<IdracPowerActionResponse>('/api/v1/adapters/idrac/power-action-by-ip', {
    method: 'POST',
    body: JSON.stringify({
      idracIp,
      resetType,
      username,
      password,
    }),
  })
}
