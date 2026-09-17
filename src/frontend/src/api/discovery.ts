import { apiClient } from './client'

export interface DiscoveredCandidate {
  id: string
  source: 'Proxmox' | 'Kubernetes' | 'UniFi' | 'OPNsense' | string
  name: string
  ipAddress: string | null
  targetType: string
  osFamily: string
  status: string
  proxmoxNode?: string | null
  proxmoxVmid?: number | null
  k8sNodeName?: string | null
  roles?: string[] | null
  isManaged: boolean
  existingHostId?: string | null
  existingHostname?: string | null
}

export interface DiscoveryScanResult {
  candidates: DiscoveredCandidate[]
  totalDiscovered: number
  alreadyManaged: number
  unmanagedCount: number
  scannedAt: string
  errors: string[]
}

export interface ImportCandidatePayload {
  name: string
  ipAddress: string
  targetType: string
  osFamily: string
  friendlyName?: string
  proxmoxNode?: string
  proxmoxVmid?: number
  k8sNodeName?: string
}

export interface ImportCandidateResponse {
  success: boolean
  hostId?: string
  hostname?: string
  errorMessage?: string
}

export interface BatchImportCandidatesPayload {
  candidates: ImportCandidatePayload[]
  commonTargetType?: string
  commonOsFamily?: string
}

export interface BatchImportItemResult {
  name: string
  success: boolean
  hostId?: string | null
  hostname?: string | null
  errorMessage?: string | null
}

export interface BatchImportCandidatesResponse {
  totalRequested: number
  succeededCount: number
  failedCount: number
  results: BatchImportItemResult[]
}

export async function scanDiscovery(options?: {
  includeProxmox?: boolean
  includeKubernetes?: boolean
  includeUniFi?: boolean
  includeOPNsense?: boolean
}): Promise<DiscoveryScanResult> {
  const includePve = options?.includeProxmox ?? true
  const includeK8s = options?.includeKubernetes ?? true
  const includeUniFi = options?.includeUniFi ?? true
  const includeOPNsense = options?.includeOPNsense ?? true
  return apiClient<DiscoveryScanResult>(
    `/api/v1/discovery/scan?includeProxmox=${includePve}&includeKubernetes=${includeK8s}&includeUniFi=${includeUniFi}&includeOPNsense=${includeOPNsense}`
  )
}

export async function importCandidate(
  payload: ImportCandidatePayload
): Promise<ImportCandidateResponse> {
  return apiClient<ImportCandidateResponse>('/api/v1/discovery/import', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function importCandidatesBatch(
  payload: BatchImportCandidatesPayload
): Promise<BatchImportCandidatesResponse> {
  return apiClient<BatchImportCandidatesResponse>('/api/v1/discovery/import-batch', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

