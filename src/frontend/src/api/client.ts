export const API_KEY_STORAGE_KEY = 'cp_api_key'

export function getApiKey(): string {
  return (
    localStorage.getItem(API_KEY_STORAGE_KEY) ||
    import.meta.env.VITE_API_KEY ||
    'dev-secret-key-123'
  )
}

export function setApiKey(key: string): void {
  localStorage.setItem(API_KEY_STORAGE_KEY, key.trim())
}

export interface ApiErrorResponse {
  message?: string
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
  
  if (!headers.has('X-ControlPlane-Key')) {
    headers.set('X-ControlPlane-Key', getApiKey())
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
      errorData.message ||
      (errorData.errors
        ? Object.values(errorData.errors).flat().join(' ')
        : `HTTP error ${response.status}`)

    throw new ApiError(message, response.status, errorData.errors)
  }

  if (response.status === 204) {
    return null as unknown as T
  }

  return response.json()
}
