import { apiClient } from './client'

export interface OPNsenseInstanceDto {
  id: string
  name: string
  baseUrl: string
  apiKey: string
  apiSecretMasked: string
  hasSecret: boolean
  allowSelfSignedCert: boolean
  updatedAt?: string | null
}

export interface SaveOPNsenseInstancePayload {
  id?: string
  name: string
  baseUrl: string
  apiKey: string
  apiSecret?: string | null
  allowSelfSignedCert?: boolean
}

export interface OPNsenseTestResult {
  success: boolean
  hostname?: string | null
  version?: string | null
  status?: string | null
  latencyMs: number
  message?: string | null
}

export interface OPNsenseGateway {
  name: string
  interface: string
  status: string
  latencyMs?: number | null
  lossPercentage?: number | null
  address?: string | null
}

export interface OPNsenseInterface {
  name: string
  device: string
  ipAddress?: string | null
  status: string
  media?: string | null
}

export interface OPNsenseService {
  id: string
  name: string
  description: string
  running: boolean
  enabled: boolean
}

export interface OPNsenseDhcpLease {
  ip: string
  mac: string
  hostname?: string | null
  starts?: string | null
  ends?: string | null
  status: string
}

export interface OPNsenseFirmware {
  version: string
  status: string
  updatesAvailable: number
  packages?: string[] | null
  lastCheck?: string | null
}

export interface OPNsenseTelemetry {
  hostname: string
  version: string
  status: string
  gateways: OPNsenseGateway[]
  interfaces: OPNsenseInterface[]
  services: OPNsenseService[]
  timestamp: string
}

export async function fetchOPNsenseInstances(): Promise<OPNsenseInstanceDto[]> {
  return apiClient<OPNsenseInstanceDto[]>('/api/v1/adapters/opnsense/instances')
}

export async function fetchOPNsenseInstance(id: string): Promise<OPNsenseInstanceDto> {
  return apiClient<OPNsenseInstanceDto>(`/api/v1/adapters/opnsense/instances/${encodeURIComponent(id)}`)
}

export async function saveOPNsenseInstance(payload: SaveOPNsenseInstancePayload): Promise<OPNsenseInstanceDto> {
  return apiClient<OPNsenseInstanceDto>('/api/v1/adapters/opnsense/instances', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function deleteOPNsenseInstance(id: string): Promise<void> {
  await apiClient<void>(`/api/v1/adapters/opnsense/instances/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  })
}

export async function testOPNsenseConnection(payload: SaveOPNsenseInstancePayload): Promise<OPNsenseTestResult> {
  return apiClient<OPNsenseTestResult>('/api/v1/adapters/opnsense/test-connection', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function testSavedOPNsenseConnection(id: string): Promise<OPNsenseTestResult> {
  return apiClient<OPNsenseTestResult>(`/api/v1/adapters/opnsense/instances/${encodeURIComponent(id)}/test-connection`, {
    method: 'POST',
  })
}

export async function fetchOPNsenseTelemetry(id: string): Promise<OPNsenseTelemetry> {
  return apiClient<OPNsenseTelemetry>(`/api/v1/adapters/opnsense/instances/${encodeURIComponent(id)}/telemetry`)
}

export async function fetchOPNsenseServices(id: string): Promise<OPNsenseService[]> {
  return apiClient<OPNsenseService[]>(`/api/v1/adapters/opnsense/instances/${encodeURIComponent(id)}/services`)
}

export async function restartOPNsenseService(id: string, serviceName: string): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/adapters/opnsense/instances/${encodeURIComponent(id)}/services/${encodeURIComponent(serviceName)}/restart`,
    {
      method: 'POST',
    }
  )
}

export async function fetchOPNsenseDhcpLeases(id: string): Promise<OPNsenseDhcpLease[]> {
  return apiClient<OPNsenseDhcpLease[]>(`/api/v1/adapters/opnsense/instances/${encodeURIComponent(id)}/dhcp/leases`)
}

export async function fetchOPNsenseFirmware(id: string): Promise<OPNsenseFirmware> {
  return apiClient<OPNsenseFirmware>(`/api/v1/adapters/opnsense/instances/${encodeURIComponent(id)}/firmware`)
}
