import { getActiveAuthMode } from '../features/auth/authConfig'

export const API_KEY_STORAGE_KEY = 'cp_api_key'

export function getApiKey(): string {
  if (typeof window === 'undefined') return ''
  return (
    localStorage.getItem(API_KEY_STORAGE_KEY) ||
    import.meta.env.VITE_API_KEY ||
    ''
  )
}

export function setApiKey(key: string): void {
  if (typeof window === 'undefined') return
  if (!key || !key.trim()) {
    localStorage.removeItem(API_KEY_STORAGE_KEY)
  } else {
    localStorage.setItem(API_KEY_STORAGE_KEY, key.trim())
  }
}

let getAuthTokenFn: (() => string | null) | null = null

export function setAuthTokenProvider(fn: (() => string | null) | null): void {
  getAuthTokenFn = fn
}

export interface ApiErrorResponse {
  message?: string
  errorMessage?: string
  title?: string
  detail?: string
  errors?: Record<string, string[]>
  status?: number
}

export class ApiError extends Error {
  status: number
  errors?: Record<string, string[]>

  constructor(message: string, status: number, errors?: Record<string, string[]>) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.errors = errors
  }
}

export async function apiClient<T>(
  endpoint: string,
  options: RequestInit = {}
): Promise<T> {
  const baseUrl = import.meta.env.VITE_API_URL || ''
  const cleanEndpoint = endpoint.startsWith('/') ? endpoint : `/${endpoint}`
  const url = `${baseUrl}${cleanEndpoint}`

  const headers = new Headers(options.headers || {})
  
  const authMode = getActiveAuthMode()

  if (authMode === 'oidc') {
    const token = getAuthTokenFn ? getAuthTokenFn() : null
    if (token && !headers.has('Authorization')) {
      headers.set('Authorization', `Bearer ${token}`)
    }
  } else {
    // API Key Mode
    const apiKey = getApiKey()
    if (apiKey && !headers.has('X-ControlPlane-Key')) {
      headers.set('X-ControlPlane-Key', apiKey)
    }
  }

  if (!headers.has('Content-Type') && !(options.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(url, {
    ...options,
    headers,
  })

  if (!response.ok) {
    let errorData: ApiErrorResponse = {}
    try {
      errorData = await response.json()
    } catch {
      errorData = { message: response.statusText }
    }

    const message =
      errorData.errorMessage ||
      errorData.message ||
      errorData.detail ||
      (errorData.errors
        ? Object.values(errorData.errors).flat().join(' ')
        : errorData.title || `HTTP error ${response.status}`)

    throw new ApiError(message, response.status, errorData.errors)
  }

  if (response.status === 204) {
    return null as unknown as T
  }

  return response.json()
}
