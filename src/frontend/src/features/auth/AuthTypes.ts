import type { AuthMode } from './authConfig'

export type UserRole = 'Admin' | 'Operator' | 'Viewer'

export interface RoleMappingConfig {
  admin?: string[]
  operator?: string[]
  viewer?: string[]
}

export interface ServerAuthConfig {
  authMode?: AuthMode
  zitadel?: {
    enabled?: boolean
    authority?: string
    clientId?: string
    roles?: RoleMappingConfig
  }
}

export interface UserProfile {
  id: string
  name: string
  email: string
  avatar?: string
  roles: UserRole[]
  rawRoles?: string[]
}

export interface AuthState {
  authMode: AuthMode
  isAuthenticated: boolean
  isLoading: boolean
  isBypass: boolean
  user: UserProfile | null
  token: string | null
  apiKey?: string | null
  roles: UserRole[]
  activeRole: UserRole
  login: (key?: string) => Promise<void>
  logout: () => Promise<void>
  hasRole: (requiredRole: UserRole) => boolean
  isAdmin: boolean
  isOperator: boolean
  isViewer: boolean
  setActiveRole?: (role: UserRole) => void
  setApiKey?: (key: string) => void
  switchAuthMode?: (mode: AuthMode) => void
}
