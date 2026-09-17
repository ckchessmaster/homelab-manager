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
  connectionMode?: 'network' | 'agent'
  hostId?: string | null
  hostName?: string | null
}

export interface SaveIdracInstancePayload {
  id?: string
  name: string
  bmcUrl?: string
  username?: string
  password?: string | null
  hostnameOrIp?: string | null
  allowSelfSignedCert?: boolean
  connectionMode?: 'network' | 'agent'
  hostId?: string | null
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
  bmcFirmwareVersion?: string | null
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

export interface BmcFanControlPayload {
  mode: 'Auto' | 'Manual' | string
  percentage?: number
}

export interface BmcFanControlResponse {
  success: boolean
  message: string
  mode: string
  percentage?: number
}

export interface BmcChassisIdentifyPayload {
  state: 'Blink' | 'On' | 'Off' | string
  durationSeconds?: number
}

export interface BmcChassisIdentifyResponse {
  success: boolean
  message: string
  state: string
}

export interface BmcBootOverridePayload {
  target: 'BiosSetup' | 'Pxe' | 'Disk' | 'Cdrom' | string
}

export interface BmcBootOverrideResponse {
  success: boolean
  message: string
  target: string
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
  idracIp?: string,
  resetType: string = '',
  username?: string,
  password?: string,
  hostId?: string
): Promise<IdracPowerActionResponse> {
  return apiClient<IdracPowerActionResponse>('/api/v1/adapters/idrac/power-action-by-ip', {
    method: 'POST',
    body: JSON.stringify({
      idracIp,
      resetType,
      username,
      password,
      hostId,
    }),
  })
}

export async function installIpmiTool(hostId: string): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(`/api/v1/adapters/idrac/hosts/${encodeURIComponent(hostId)}/install-ipmitool`, {
    method: 'POST',
  })
}

// --- Fan Control ---
export async function sendBmcFanControl(id: string, payload: BmcFanControlPayload): Promise<BmcFanControlResponse> {
  return apiClient<BmcFanControlResponse>(`/api/v1/adapters/idrac/instances/${encodeURIComponent(id)}/fan-control`, {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function sendBmcFanControlByIp(payload: {
  idracIp?: string
  hostId?: string
  mode: string
  percentage?: number
  username?: string
  password?: string
}): Promise<BmcFanControlResponse> {
  return apiClient<BmcFanControlResponse>('/api/v1/adapters/idrac/fan-control-by-ip', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

// --- Chassis Identify (Locator LED / UID) ---
export async function sendBmcChassisIdentify(id: string, payload: BmcChassisIdentifyPayload): Promise<BmcChassisIdentifyResponse> {
  return apiClient<BmcChassisIdentifyResponse>(`/api/v1/adapters/idrac/instances/${encodeURIComponent(id)}/identify`, {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function sendBmcChassisIdentifyByIp(payload: {
  idracIp?: string
  hostId?: string
  state: string
  durationSeconds?: number
  username?: string
  password?: string
}): Promise<BmcChassisIdentifyResponse> {
  return apiClient<BmcChassisIdentifyResponse>('/api/v1/adapters/idrac/identify-by-ip', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

// --- One-Time Boot Device Override ---
export async function sendBmcBootOverride(id: string, payload: BmcBootOverridePayload): Promise<BmcBootOverrideResponse> {
  return apiClient<BmcBootOverrideResponse>(`/api/v1/adapters/idrac/instances/${encodeURIComponent(id)}/boot-override`, {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function sendBmcBootOverrideByIp(payload: {
  idracIp?: string
  hostId?: string
  target: string
  username?: string
  password?: string
}): Promise<BmcBootOverrideResponse> {
  return apiClient<BmcBootOverrideResponse>('/api/v1/adapters/idrac/boot-override-by-ip', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}
