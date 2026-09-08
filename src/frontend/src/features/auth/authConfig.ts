import type { UserManagerSettings } from 'oidc-client-ts'
import { WebStorageStateStore } from 'oidc-client-ts'

export interface AuthConfig {
  authority: string
  clientId: string
  redirectUri: string
  postLogoutRedirectUri: string
  responseType: string
  scope: string
  isBypass: boolean
}

export function getAuthConfig(): AuthConfig {
  const authority = import.meta.env.VITE_ZITADEL_AUTHORITY || 'http://localhost:8085'
  const clientId = import.meta.env.VITE_ZITADEL_CLIENT_ID || '389775242525999110'
  
  // Bypass mode is active if explicitly set or if no IdP URL / client ID is configured
  const isBypass =
    import.meta.env.VITE_AUTH_MODE === 'bypass' ||
    Boolean(import.meta.env.VITE_AUTH_BYPASS) ||
    (!import.meta.env.VITE_ZITADEL_AUTHORITY && !import.meta.env.VITE_ZITADEL_CLIENT_ID)

  const origin = typeof window !== 'undefined' ? window.location.origin : 'http://localhost:5173'

  return {
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
