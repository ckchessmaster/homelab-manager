import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { AuthProvider as OidcAuthProvider, useAuth as useOidcAuth } from 'react-oidc-context'
import { AuthContext } from './AuthContext'
import { getAuthConfig, createOidcSettings } from './authConfig'
import type { AuthState, UserProfile, UserRole } from './AuthTypes'
import { ROLE_HIERARCHY, parseZitadelRoles, hasRequiredRole, getHighestRole } from './roleUtils'
import { setAuthTokenProvider } from '../../api/client'

interface AuthProviderProps {
  children: ReactNode
}

function BypassAuthProvider({ children }: { children: ReactNode }) {
  const [activeRole, setActiveRoleState] = useState<UserRole>(() => {
    const saved = localStorage.getItem('cp_bypass_role')
    return (saved === 'Admin' || saved === 'Operator' || saved === 'Viewer') ? saved : 'Admin'
  })

  const setActiveRole = (role: UserRole) => {
    setActiveRoleState(role)
    localStorage.setItem('cp_bypass_role', role)
  }

  useEffect(() => {
    setAuthTokenProvider(null)
  }, [])

  const effectiveRoles = ROLE_HIERARCHY[activeRole]

  const user: UserProfile = {
    id: 'dev-admin',
    name: 'Dev Admin',
    email: 'admin@controlplane.local',
    roles: effectiveRoles,
  }

  const contextValue: AuthState = {
    isAuthenticated: true,
    isLoading: false,
    isBypass: true,
    user,
    token: null,
    roles: effectiveRoles,
    activeRole,
    setActiveRole,
    login: async () => {},
    logout: async () => {},
    hasRole: (role) => hasRequiredRole(effectiveRoles, role),
    isAdmin: hasRequiredRole(effectiveRoles, 'Admin'),
    isOperator: hasRequiredRole(effectiveRoles, 'Operator'),
    isViewer: hasRequiredRole(effectiveRoles, 'Viewer'),
  }

  return <AuthContext.Provider value={contextValue}>{children}</AuthContext.Provider>
}

function OidcBridge({ children }: { children: ReactNode }) {
  const auth = useOidcAuth()

  useEffect(() => {
    if (auth.user?.access_token) {
      setAuthTokenProvider(() => auth.user?.access_token ?? null)
    } else {
      setAuthTokenProvider(null)
    }
  }, [auth.user?.access_token])

  const effectiveRoles = useMemo(() => {
    if (!auth.isAuthenticated || !auth.user) return []
    return parseZitadelRoles(auth.user.profile as Record<string, unknown>)
  }, [auth.isAuthenticated, auth.user])

  const activeRole = useMemo(() => {
    return getHighestRole(effectiveRoles)
  }, [effectiveRoles])

  const user: UserProfile | null = useMemo(() => {
    if (!auth.user) return null
    return {
      id: auth.user.profile.sub,
      name:
        auth.user.profile.name ||
        (auth.user.profile.preferred_username as string) ||
        auth.user.profile.email ||
        'Zitadel User',
      email: auth.user.profile.email || '',
      roles: effectiveRoles,
    }
  }, [auth.user, effectiveRoles])

  const contextValue: AuthState = {
    isAuthenticated: auth.isAuthenticated,
    isLoading: auth.isLoading,
    isBypass: false,
    user,
    token: auth.user?.access_token ?? null,
    roles: effectiveRoles,
    activeRole,
    login: async () => {
      await auth.signinRedirect()
    },
    logout: async () => {
      await auth.signoutRedirect()
    },
    hasRole: (role) => hasRequiredRole(effectiveRoles, role),
    isAdmin: hasRequiredRole(effectiveRoles, 'Admin'),
    isOperator: hasRequiredRole(effectiveRoles, 'Operator'),
    isViewer: hasRequiredRole(effectiveRoles, 'Viewer'),
  }

  return <AuthContext.Provider value={contextValue}>{children}</AuthContext.Provider>
}

export function AuthProvider({ children }: AuthProviderProps) {
  const config = useMemo(() => getAuthConfig(), [])

  if (config.isBypass) {
    return <BypassAuthProvider>{children}</BypassAuthProvider>
  }

  const oidcSettings = createOidcSettings()

  return (
    <OidcAuthProvider
      {...oidcSettings}
      onSigninCallback={() => {
        window.history.replaceState({}, document.title, window.location.pathname)
      }}
    >
      <OidcBridge>{children}</OidcBridge>
    </OidcAuthProvider>
  )
}
