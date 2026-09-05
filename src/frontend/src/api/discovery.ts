import { apiClient } from './client'

export interface DiscoveredCandidate {
  id: string
  source: 'Proxmox' | 'Kubernetes'
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

export async function scanDiscovery(options?: {
  includeProxmox?: boolean
  includeKubernetes?: boolean
}): Promise<DiscoveryScanResult> {
  const includePve = options?.includeProxmox ?? true
  const includeK8s = options?.includeKubernetes ?? true
  return apiClient<DiscoveryScanResult>(
    `/api/v1/discovery/scan?includeProxmox=${includePve}&includeKubernetes=${includeK8s}`
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
