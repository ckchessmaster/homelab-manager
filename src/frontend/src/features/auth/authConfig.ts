import type { UserManagerSettings } from 'oidc-client-ts'
import { WebStorageStateStore } from 'oidc-client-ts'
import type { ServerAuthConfig } from './AuthTypes'

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

let cachedServerConfig: ServerAuthConfig | null = null

export async function fetchServerAuthConfig(): Promise<ServerAuthConfig | null> {
  if (cachedServerConfig) return cachedServerConfig

  try {
    const baseUrl = import.meta.env.VITE_API_URL || ''
    const res = await fetch(`${baseUrl}/api/v1/auth/config`)
    if (res.ok) {
      cachedServerConfig = await res.json()
      return cachedServerConfig
    }
  } catch (err) {
    console.warn('[ControlPlane Auth] Could not fetch server auth configuration, falling back to local defaults.', err)
  }
  return null
}

export function getServerAuthConfig(): ServerAuthConfig | null {
  return cachedServerConfig
}

export function getActiveAuthMode(serverConfig?: ServerAuthConfig | null): AuthMode {
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

  const effectiveServer = serverConfig || cachedServerConfig
  if (effectiveServer?.authMode) {
    return effectiveServer.authMode
  }

  // If Zitadel environment variables are explicitly defined, use OIDC, otherwise default to api_key
  if (import.meta.env.VITE_ZITADEL_AUTHORITY && import.meta.env.VITE_ZITADEL_CLIENT_ID) {
    return 'oidc'
  }

  if (effectiveServer?.zitadel?.enabled && effectiveServer.zitadel.authority) {
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

export function getAuthConfig(serverConfig?: ServerAuthConfig | null): AuthConfig {
  const effectiveServer = serverConfig || cachedServerConfig
  const mode = getActiveAuthMode(effectiveServer)

  const runtimeWindow = typeof window !== 'undefined' ? (window as any).__CONTROLPLANE_CONFIG__ : null
  const storedAuthority = typeof window !== 'undefined' ? localStorage.getItem('cp_zitadel_authority') : null
  const storedClientId = typeof window !== 'undefined' ? localStorage.getItem('cp_zitadel_client_id') : null

  const authority =
    storedAuthority ||
    runtimeWindow?.zitadelAuthority ||
    effectiveServer?.zitadel?.authority ||
    import.meta.env.VITE_ZITADEL_AUTHORITY ||
    'http://localhost:8085'

  const clientId =
    storedClientId ||
    runtimeWindow?.zitadelClientId ||
    effectiveServer?.zitadel?.clientId ||
    import.meta.env.VITE_ZITADEL_CLIENT_ID ||
    '389775242525999110'

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

export function createOidcSettings(serverConfig?: ServerAuthConfig | null): UserManagerSettings {
  const config = getAuthConfig(serverConfig)
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
