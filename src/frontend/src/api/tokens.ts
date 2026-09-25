import { apiClient } from './client'

export interface CreateApiTokenRequest {
  name: string
  role?: 'Admin' | 'Operator' | 'Viewer' | string
  expiresInDays?: number | null
}

export interface ApiTokenCreatedDto {
  id: string
  name: string
  token: string
  tokenPrefix: string
  role: string
  createdAt: string
  expiresAt?: string | null
}

export interface ApiTokenSummaryDto {
  id: string
  name: string
  tokenPrefix: string
  role: string
  createdAt: string
  expiresAt?: string | null
  lastUsedAt?: string | null
  isRevoked: boolean
  isExpired: boolean
}

export async function fetchApiTokens(): Promise<ApiTokenSummaryDto[]> {
  return apiClient<ApiTokenSummaryDto[]>('/api/v1/tokens')
}

export async function createApiToken(request: CreateApiTokenRequest): Promise<ApiTokenCreatedDto> {
  return apiClient<ApiTokenCreatedDto>('/api/v1/tokens', {
    method: 'POST',
    body: JSON.stringify(request),
  })
}

export async function revokeApiToken(id: string): Promise<void> {
  await apiClient<void>(`/api/v1/tokens/${id}`, {
    method: 'DELETE',
  })
}
