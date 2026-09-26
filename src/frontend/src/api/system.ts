import { apiClient } from './client'

export interface SystemLogEntryDto {
  id: number
  timestamp: string
  logLevel: 'Trace' | 'Debug' | 'Information' | 'Warning' | 'Error' | 'Critical' | string
  category: string
  message: string
  exception?: string | null
  eventId?: number | null
  eventName?: string | null
}

export interface SystemLogStatsDto {
  totalCount: number
  errorCount: number
  warningCount: number
  infoCount: number
  debugCount: number
  capacity: number
}

export interface SystemLogResponseDto {
  logs: SystemLogEntryDto[]
  stats: SystemLogStatsDto
  totalAvailable: number
}

export interface SystemInfoDto {
  osDescription: string
  frameworkDescription: string
  machineName: string
  processorCount: number
  uptime: string
  workingSetBytes: number
  serverTimeUtc: string
  environmentName: string
}

export interface FetchSystemLogsParams {
  level?: string
  minLevel?: string
  category?: string
  search?: string
  sinceId?: number
  limit?: number
  tail?: boolean
}

export async function fetchSystemLogs(params?: FetchSystemLogsParams): Promise<SystemLogResponseDto> {
  const searchParams = new URLSearchParams()
  if (params?.level) searchParams.set('level', params.level)
  if (params?.minLevel) searchParams.set('minLevel', params.minLevel)
  if (params?.category) searchParams.set('category', params.category)
  if (params?.search) searchParams.set('search', params.search)
  if (params?.sinceId !== undefined && params?.sinceId !== null) searchParams.set('sinceId', String(params.sinceId))
  if (params?.limit) searchParams.set('limit', String(params.limit))
  if (params?.tail !== undefined) searchParams.set('tail', String(params.tail))

  const queryString = searchParams.toString()
  const endpoint = `/api/v1/system/logs${queryString ? `?${queryString}` : ''}`
  return apiClient<SystemLogResponseDto>(endpoint)
}

export async function fetchSystemLogStats(): Promise<SystemLogStatsDto> {
  return apiClient<SystemLogStatsDto>('/api/v1/system/logs/stats')
}

export async function clearSystemLogs(): Promise<{ message: string }> {
  return apiClient<{ message: string }>('/api/v1/system/logs', {
    method: 'DELETE',
  })
}

export async function fetchSystemInfo(): Promise<SystemInfoDto> {
  return apiClient<SystemInfoDto>('/api/v1/system/info')
}
