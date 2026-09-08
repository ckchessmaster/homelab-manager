import { apiClient } from './client'

export interface UniFiInstanceDto {
  id: string
  name: string
  controllerUrl: string
  username: string
  passwordMasked: string
  hasPassword: boolean
  site: string
  allowSelfSignedCert: boolean
  updatedAt?: string | null
  authType?: 'api_key' | 'credentials'
  apiKeyMasked?: string | null
  hasApiKey?: boolean
}

export interface SaveUniFiInstancePayload {
  id?: string
  name: string
  controllerUrl: string
  username?: string
  password?: string | null
  apiKey?: string | null
  authType?: 'api_key' | 'credentials'
  site?: string
  allowSelfSignedCert?: boolean
}

export interface UniFiTestResult {
  success: boolean
  controllerVersion?: string | null
  deviceCount?: number | null
  clientCount?: number | null
  sites?: string[] | null
  latencyMs: number
  message?: string | null
}

export interface UniFiPortDto {
  portIdx: number
  name?: string | null
  up: boolean
  speedMbps?: number | null
  poeMode: string
  poePowerWatts?: number | null
  poeVoltage?: number | null
  poeCurrent?: number | null
}

export interface UniFiDeviceDto {
  mac: string
  name?: string | null
  model: string
  type: string
  ip?: string | null
  state: string
  version?: string | null
  upgradeAvailable: boolean
  uptimeSeconds?: number | null
  temperature?: number | null
  ports: UniFiPortDto[]
}

export interface UniFiMacLease {
  mac: string
  ip?: string | null
  hostname?: string | null
  lastSeen?: string | null
}

export interface UniFiBounceResult {
  success: boolean
  message: string
  switchMac: string
  portNumber: number
}

export async function fetchUniFiInstances(): Promise<UniFiInstanceDto[]> {
  return apiClient<UniFiInstanceDto[]>('/api/v1/adapters/unifi/instances')
}

export async function fetchUniFiInstance(id: string): Promise<UniFiInstanceDto> {
  return apiClient<UniFiInstanceDto>(`/api/v1/adapters/unifi/instances/${encodeURIComponent(id)}`)
}

export async function saveUniFiInstance(payload: SaveUniFiInstancePayload): Promise<UniFiInstanceDto> {
  return apiClient<UniFiInstanceDto>('/api/v1/adapters/unifi/instances', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function deleteUniFiInstance(id: string): Promise<{ message: string }> {
  return apiClient<{ message: string }>(`/api/v1/adapters/unifi/instances/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  })
}

export async function testUniFiInstanceConnection(id: string): Promise<UniFiTestResult> {
  return apiClient<UniFiTestResult>(`/api/v1/adapters/unifi/instances/${encodeURIComponent(id)}/test-connection`, {
    method: 'POST',
  })
}

export async function testUniFiPreflight(payload: SaveUniFiInstancePayload): Promise<UniFiTestResult> {
  return apiClient<UniFiTestResult>('/api/v1/adapters/unifi/test-connection', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function fetchUniFiDevices(instanceId: string): Promise<UniFiDeviceDto[]> {
  return apiClient<UniFiDeviceDto[]>(`/api/v1/adapters/unifi/${encodeURIComponent(instanceId)}/devices`)
}

export async function restartUniFiDevice(instanceId: string, deviceMac: string, reason?: string): Promise<{ message: string }> {
  return apiClient<{ message: string }>(
    `/api/v1/adapters/unifi/${encodeURIComponent(instanceId)}/devices/${encodeURIComponent(deviceMac)}/restart`,
    {
      method: 'POST',
      body: JSON.stringify({ reason }),
    }
  )
}

export async function upgradeUniFiDevice(instanceId: string, deviceMac: string): Promise<{ message: string }> {
  return apiClient<{ message: string }>(
    `/api/v1/adapters/unifi/${encodeURIComponent(instanceId)}/devices/${encodeURIComponent(deviceMac)}/upgrade`,
    {
      method: 'POST',
      body: JSON.stringify({}),
    }
  )
}

export async function powerCycleUniFiPort(
  instanceId: string,
  deviceMac: string,
  portIdx: number,
  delaySeconds = 5
): Promise<UniFiBounceResult> {
  return apiClient<UniFiBounceResult>(
    `/api/v1/adapters/unifi/${encodeURIComponent(instanceId)}/devices/${encodeURIComponent(deviceMac)}/ports/${portIdx}/power-cycle`,
    {
      method: 'POST',
      body: JSON.stringify({ delaySeconds }),
    }
  )
}

export async function fetchUniFiClients(instanceId: string): Promise<UniFiMacLease[]> {
  return apiClient<UniFiMacLease[]>(`/api/v1/adapters/unifi/${encodeURIComponent(instanceId)}/clients`)
}

export const unifiApi = {
  getInstances: fetchUniFiInstances,
  getInstance: fetchUniFiInstance,
  saveInstance: saveUniFiInstance,
  deleteInstance: deleteUniFiInstance,
  testInstanceConnection: testUniFiInstanceConnection,
  testRawConnection: testUniFiPreflight,
  getDevices: fetchUniFiDevices,
  restartDevice: restartUniFiDevice,
  upgradeDevice: upgradeUniFiDevice,
  powerCyclePort: powerCycleUniFiPort,
  getClients: fetchUniFiClients,
}
