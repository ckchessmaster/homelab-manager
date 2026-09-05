import { apiClient } from './client'

export interface HostSnapshotItem {
  hostId: string
  hostname: string
  node: string
  vmid: number
  isLxc: boolean
  name: string
  description?: string
  createdAt?: string
  ageHours: number
  isControlPlaneSnapshot: boolean
  isProtectedByActiveJob: boolean
  isExpired: boolean
  canPrune: boolean
}

export interface SnapshotPruneRequest {
  hostId?: string
  dryRun?: boolean
}

export interface PrunedSnapshotItem {
  hostId: string
  hostname: string
  snapshotName: string
  ageHours: number
  success: boolean
  message?: string
}

export interface SnapshotPruneResult {
  totalScanned: number
  expiredCount: number
  prunedCount: number
  skippedCount: number
  dryRun: boolean
  items: PrunedSnapshotItem[]
  errors: string[]
}

export async function fetchSnapshots(hostId?: string): Promise<HostSnapshotItem[]> {
  const query = hostId ? `?hostId=${encodeURIComponent(hostId)}` : ''
  return apiClient<HostSnapshotItem[]>(`/api/v1/adapters/proxmox/snapshots${query}`)
}

export async function pruneSnapshots(req: SnapshotPruneRequest): Promise<SnapshotPruneResult> {
  return apiClient<SnapshotPruneResult>('/api/v1/adapters/proxmox/snapshots/prune', {
    method: 'POST',
    body: JSON.stringify(req),
  })
}

export async function deleteSnapshot(hostId: string, snapshotName: string): Promise<{ message: string }> {
  return apiClient<{ message: string }>(
    `/api/v1/adapters/proxmox/snapshots/${encodeURIComponent(snapshotName)}?hostId=${encodeURIComponent(hostId)}`,
    {
      method: 'DELETE',
    }
  )
}
