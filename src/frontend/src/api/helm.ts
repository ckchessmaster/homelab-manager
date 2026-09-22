import { apiClient } from './client'

export interface HelmChartUpdateInfo {
  chartName: string
  currentVersion: string
  latestVersion?: string | null
  currentAppVersion?: string | null
  latestAppVersion?: string | null
  isOutdated: boolean
  updateType?: 'major' | 'minor' | 'patch' | string | null
  repoUrl?: string | null
  message?: string | null
  checkedAt?: string | null
  availableVersions?: string[] | null
}

export interface HelmReleaseSummary {
  name: string
  namespace: string
  revision: number
  updated: string
  status: 'deployed' | 'failed' | 'pending-install' | 'pending-upgrade' | 'pending-rollback' | 'uninstalled' | string
  chart: string
  chartName: string
  chartVersion: string
  appVersion: string
  description?: string | null
  notes?: string | null
  updateInfo?: HelmChartUpdateInfo | null
}

export interface HelmReleaseDetail {
  name: string
  namespace: string
  revision: number
  updated: string
  status: string
  chart: string
  chartName: string
  chartVersion: string
  appVersion: string
  description?: string | null
  notes?: string | null
  valuesYaml?: string | null
  computedValuesYaml?: string | null
  manifest?: string | null
  repoUrl?: string | null
  updateInfo?: HelmChartUpdateInfo | null
}

export interface HelmReleaseRevision {
  revision: number
  updated: string
  status: string
  chart: string
  appVersion: string
  description?: string | null
}

export interface InstallHelmReleasePayload {
  releaseName: string
  namespace: string
  chartName: string
  repoUrl?: string | null
  version?: string | null
  valuesYaml?: string | null
  reuseValues?: boolean
  resetValues?: boolean
  createNamespace?: boolean
  wait?: boolean
  timeoutSeconds?: number
}

export interface RollbackHelmReleasePayload {
  revision: number
  cleanupOnFail?: boolean
  wait?: boolean
  timeoutSeconds?: number
}

export interface HelmOperationResult {
  success: boolean
  message: string
  releaseName?: string | null
  revision?: number | null
  output?: string | null
}

export interface HelmCatalogItem {
  id: string
  name: string
  category: 'Networking' | 'Certificates' | 'Storage' | 'Monitoring' | 'Security' | 'Media' | 'Smart Home' | string
  description: string
  repoUrl: string
  chartName: string
  defaultNamespace: string
  defaultValuesYaml: string
  icon: string
  officialUrl?: string | null
}

// API functions
export async function getHelmCatalog(): Promise<HelmCatalogItem[]> {
  return apiClient<HelmCatalogItem[]>('/api/v1/kubernetes/helm/catalog')
}

export async function getHelmReleases(
  clusterId: string,
  namespaceName?: string
): Promise<HelmReleaseSummary[]> {
  const query = namespaceName ? `?namespaceName=${encodeURIComponent(namespaceName)}` : ''
  return apiClient<HelmReleaseSummary[]>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/helm/releases${query}`
  )
}

export async function getHelmUpdates(
  clusterId: string
): Promise<Record<string, HelmChartUpdateInfo>> {
  return apiClient<Record<string, HelmChartUpdateInfo>>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/helm/updates`
  )
}

export async function checkHelmUpdates(
  clusterId: string,
  force = false,
  releaseNames?: string[]
): Promise<Record<string, HelmChartUpdateInfo>> {
  return apiClient<Record<string, HelmChartUpdateInfo>>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/helm/updates/check`,
    {
      method: 'POST',
      body: JSON.stringify({ force, releaseNames }),
    }
  )
}

export async function getHelmReleaseDetail(
  clusterId: string,
  namespaceName: string,
  name: string,
  revision?: number
): Promise<HelmReleaseDetail> {
  const query = revision != null ? `?revision=${revision}` : ''
  return apiClient<HelmReleaseDetail>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/helm/releases/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}${query}`
  )
}

export async function getHelmReleaseHistory(
  clusterId: string,
  namespaceName: string,
  name: string
): Promise<HelmReleaseRevision[]> {
  return apiClient<HelmReleaseRevision[]>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/helm/releases/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}/history`
  )
}

export async function installOrUpgradeHelmRelease(
  clusterId: string,
  payload: InstallHelmReleasePayload
): Promise<HelmOperationResult> {
  return apiClient<HelmOperationResult>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/helm/releases`,
    {
      method: 'POST',
      body: JSON.stringify(payload),
    }
  )
}

export async function rollbackHelmRelease(
  clusterId: string,
  namespaceName: string,
  name: string,
  revision: number
): Promise<HelmOperationResult> {
  return apiClient<HelmOperationResult>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/helm/releases/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}/rollback`,
    {
      method: 'POST',
      body: JSON.stringify({ revision }),
    }
  )
}

export async function uninstallHelmRelease(
  clusterId: string,
  namespaceName: string,
  name: string
): Promise<HelmOperationResult> {
  return apiClient<HelmOperationResult>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/helm/releases/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}`,
    {
      method: 'DELETE',
    }
  )
}

export async function getChartVersions(chartName: string, repoUrl?: string): Promise<string[]> {
  const params = new URLSearchParams()
  if (repoUrl) params.append('repoUrl', repoUrl)
  const qs = params.toString() ? `?${params.toString()}` : ''
  return apiClient<string[]>(`/api/v1/kubernetes/helm/charts/${encodeURIComponent(chartName)}/versions${qs}`)
}

