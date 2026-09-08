import type { UserManagerSettings } from 'oidc-client-ts'
import { WebStorageStateStore } from 'oidc-client-ts'

export type AuthMode = 'api_key' | 'oidc'

export interface AuthConfig {
  mode: AuthMode
  authority: string
  clientId: string
  redirectUri: string
  postLogoutRedirectUri: string
  responseType: string
  scope: string
  isBypass: boolean
}

export function getActiveAuthMode(): AuthMode {
  if (typeof window !== 'undefined') {
    const saved = localStorage.getItem('cp_auth_mode') as AuthMode | null
    if (saved === 'api_key' || saved === 'oidc') {
      return saved
    }
  }

  const envMode = import.meta.env.VITE_AUTH_MODE
  if (envMode === 'api_key') return 'api_key'
  if (envMode === 'oidc' || envMode === 'bypass') return 'oidc'

  // E2E test or dev bypass simulation
  if (typeof window !== 'undefined' && localStorage.getItem('cp_auth_bypass') === 'true') {
    return 'oidc'
  }

  // If Zitadel environment variables are explicitly defined, use OIDC, otherwise default to api_key
  if (import.meta.env.VITE_ZITADEL_AUTHORITY && import.meta.env.VITE_ZITADEL_CLIENT_ID) {
    return 'oidc'
  }

  return 'api_key'
}

export function setActiveAuthMode(mode: AuthMode): void {
  if (typeof window !== 'undefined') {
    localStorage.setItem('cp_auth_mode', mode)
    window.location.reload()
  }
}

export function getAuthConfig(): AuthConfig {
  const mode = getActiveAuthMode()
  const authority = import.meta.env.VITE_ZITADEL_AUTHORITY || 'http://localhost:8085'
  const clientId = import.meta.env.VITE_ZITADEL_CLIENT_ID || '389775242525999110'

  // Bypass mode is active in OIDC if explicitly set via flag/query param
  const hasBypassFlag =
    typeof window !== 'undefined' &&
    (window.localStorage?.getItem('cp_auth_bypass') === 'true' ||
      window.location.search.includes('bypass=true'))

  const isBypass =
    import.meta.env.VITE_AUTH_MODE === 'bypass' ||
    Boolean(import.meta.env.VITE_AUTH_BYPASS) ||
    hasBypassFlag

  const origin = typeof window !== 'undefined' ? window.location.origin : 'http://localhost:5173'

  return {
    mode,
    authority,
    clientId,
    redirectUri: `${origin}/auth/callback`,
    postLogoutRedirectUri: origin,
    responseType: 'code',
    scope: 'openid profile email urn:zitadel:iam:org:project:roles',
    isBypass,
  }
}

export function createOidcSettings(): UserManagerSettings {
  const config = getAuthConfig()
  return {
    authority: config.authority,
    client_id: config.clientId,
    redirect_uri: config.redirectUri,
    post_logout_redirect_uri: config.postLogoutRedirectUri,
    response_type: config.responseType,
    scope: config.scope,
    loadUserInfo: true,
    userStore: typeof window !== 'undefined' ? new WebStorageStateStore({ store: window.localStorage }) : undefined,
    automaticSilentRenew: true,
  }
}
