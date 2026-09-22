import { apiClient } from './client'

export interface K8sSecretSummary {
  name: string
  namespace: string
  type: string
  keysCount: number
  keys: string[]
  creationTimestamp?: string | null
  isSystem?: boolean
  usedBy?: string[]
}

export interface K8sSecretDetail {
  name: string
  namespace: string
  type: string
  data: Record<string, string>
  labels?: Record<string, string> | null
  annotations?: Record<string, string> | null
  creationTimestamp?: string | null
}

export interface CreateSecretPayload {
  name: string
  namespace: string
  type?: string
  stringData?: Record<string, string>
  labels?: Record<string, string>
  annotations?: Record<string, string>
}

export interface K8sConfigMapSummary {
  name: string
  namespace: string
  keysCount: number
  keys: string[]
  creationTimestamp?: string | null
  isSystem?: boolean
  usedBy?: string[]
}

export interface K8sConfigMapDetail {
  name: string
  namespace: string
  data: Record<string, string>
  labels?: Record<string, string> | null
  annotations?: Record<string, string> | null
  creationTimestamp?: string | null
}

export interface CreateConfigMapPayload {
  name: string
  namespace: string
  data?: Record<string, string>
  labels?: Record<string, string>
  annotations?: Record<string, string>
}

// --- Secrets API ---

export async function listSecrets(
  clusterId: string,
  namespaceName?: string
): Promise<K8sSecretSummary[]> {
  const url = namespaceName
    ? `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/secrets?namespaceName=${encodeURIComponent(namespaceName)}`
    : `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/secrets`

  return apiClient<K8sSecretSummary[]>(url)
}

export async function getSecret(
  clusterId: string,
  namespaceName: string,
  name: string,
  reveal: boolean = false
): Promise<K8sSecretDetail> {
  const url = `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/secrets/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}${reveal ? '?reveal=true' : ''}`
  return apiClient<K8sSecretDetail>(url)
}

export async function createSecret(
  clusterId: string,
  payload: CreateSecretPayload
): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/secrets`,
    {
      method: 'POST',
      body: JSON.stringify(payload),
    }
  )
}

export async function updateSecret(
  clusterId: string,
  namespaceName: string,
  name: string,
  payload: CreateSecretPayload
): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/secrets/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}`,
    {
      method: 'PUT',
      body: JSON.stringify(payload),
    }
  )
}

export async function deleteSecret(
  clusterId: string,
  namespaceName: string,
  name: string
): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/secrets/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}`,
    {
      method: 'DELETE',
    }
  )
}

// --- ConfigMaps API ---

export async function listConfigMaps(
  clusterId: string,
  namespaceName?: string
): Promise<K8sConfigMapSummary[]> {
  const url = namespaceName
    ? `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/configmaps?namespaceName=${encodeURIComponent(namespaceName)}`
    : `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/configmaps`

  return apiClient<K8sConfigMapSummary[]>(url)
}

export async function getConfigMap(
  clusterId: string,
  namespaceName: string,
  name: string
): Promise<K8sConfigMapDetail> {
  return apiClient<K8sConfigMapDetail>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/configmaps/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}`
  )
}

export async function createConfigMap(
  clusterId: string,
  payload: CreateConfigMapPayload
): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/configmaps`,
    {
      method: 'POST',
      body: JSON.stringify(payload),
    }
  )
}

export async function updateConfigMap(
  clusterId: string,
  namespaceName: string,
  name: string,
  payload: CreateConfigMapPayload
): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/configmaps/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}`,
    {
      method: 'PUT',
      body: JSON.stringify(payload),
    }
  )
}

export async function deleteConfigMap(
  clusterId: string,
  namespaceName: string,
  name: string
): Promise<{ success: boolean; message: string }> {
  return apiClient<{ success: boolean; message: string }>(
    `/api/v1/kubernetes/${encodeURIComponent(clusterId)}/configmaps/${encodeURIComponent(namespaceName)}/${encodeURIComponent(name)}`,
    {
      method: 'DELETE',
    }
  )
}
