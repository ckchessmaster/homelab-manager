export type UserRole = 'Admin' | 'Operator' | 'Viewer'

export interface UserProfile {
  id: string
  name: string
  email: string
  avatar?: string
  roles: UserRole[]
  rawRoles?: string[]
}

export interface AuthState {
  isAuthenticated: boolean
  isLoading: boolean
  isBypass: boolean
  user: UserProfile | null
  token: string | null
  roles: UserRole[]
  activeRole: UserRole
  login: () => Promise<void>
  logout: () => Promise<void>
  hasRole: (requiredRole: UserRole) => boolean
  isAdmin: boolean
  isOperator: boolean
  isViewer: boolean
  setActiveRole?: (role: UserRole) => void
}
