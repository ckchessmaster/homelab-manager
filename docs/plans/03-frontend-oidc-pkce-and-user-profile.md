# Plan 03: Frontend OIDC Authentication Flow (PKCE) & User Profile Context

**Phase:** Phase 4: Productionization & Deployment  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 02: Zitadel Identity Setup & Backend RBAC](file:///home/ckingdon/projects/homelab-manager/docs/plans/02-zitadel-identity-setup-and-backend-rbac.md)  

---

## 1. Objectives & Overview

Integrate modern OpenID Connect (OIDC) with **Proof Key for Code Exchange (PKCE)** into the React 19 single-page application, connecting to Zitadel and adapting the UI based on assigned user roles:

1. **OIDC Client & PKCE Flow**:
   * Add `oidc-client-ts` and `react-oidc-context` to `src/frontend`.
   * Configure OIDC settings with Zitadel authority, client ID, scopes (`openid profile email urn:zitadel:iam:org:project:roles`), and redirect URIs.
   * Implement smooth login redirect and callback handling (`/auth/callback`).

2. **Authenticated API Client Interceptor**:
   * Automatically attach `Authorization: Bearer <access_token>` to all TanStack Query requests.
   * Handle silent token renewals and graceful redirect on session expiration.

3. **User Profile & Navigation Header UI**:
   * Add a sleek user profile dropdown in the top header:
     * User display name and email.
     * Assigned role badges (`Admin`, `Operator`, `Viewer`).
     * "Sign Out" button terminating session with Zitadel end-session endpoint.

4. **Role-Based UI Trimming & Route Protection**:
   * Create `usePermissions()` hook checking user roles: `isAdmin`, `isOperator`, `isViewer`.
   * Disable/hide destructive buttons ("Delete Host", "Adopt Node", "Launch Rolling Upgrade") for users with read-only `Viewer` role.
   * Display helpful tooltips on disabled actions ("Requires Admin privileges").

5. **Dev & Standby CLI Compatibility**:
   * When `VITE_AUTH_MODE=bypass` (or in Standby Runner mode), automatically synthesize an `Admin` mock profile and bypass Zitadel redirects completely.
   * Update Playwright test fixtures to seamlessly support both mock auth and bypass mode.

---

## 2. Target File Structure

```
src/frontend/src/
├── features/
│   └── auth/
│       ├── AuthProvider.tsx                 # OIDC Context Provider wrapper
│       ├── authConfig.ts                    # Zitadel client configuration
│       ├── useAuthUser.ts                   # Hook for current user, roles & permissions
│       ├── CallbackPage.tsx                 # OIDC redirect callback handler
│       ├── UserProfileDropdown.tsx          # Top bar profile menu & sign-out
│       └── RoleGate.tsx                     # Conditional rendering component based on roles
├── components/layout/
│   └── Header.tsx                           # Integrate UserProfileDropdown
└── api/
    └── client.ts                            # Bearer token injection interceptor
```

---

## 3. Implementation Steps

1. **Install OIDC Dependencies**:
   ```bash
   cd src/frontend && npm install oidc-client-ts react-oidc-context
   ```

2. **Implement `authConfig.ts` & `AuthProvider.tsx`**:
   * Read authority and client ID from `import.meta.env.VITE_ZITADEL_AUTHORITY` and `VITE_ZITADEL_CLIENT_ID`.
   * Support `VITE_AUTH_MODE=bypass` fallback so offline developers and Standby CLI runners have zero friction.

3. **Implement `useAuthUser` & `RoleGate`**:
   * Parse `profile['urn:zitadel:iam:org:project:roles']` to determine `roles`.
   * Provide `hasRole(role)` and permission flags (`canManageHosts`, `canLaunchWorkflows`).

4. **Integrate Header User Profile**:
   * Place `UserProfileDropdown` in the top right header bar.
   * Display active user initials, role chip, and logout action.

5. **Wire API Client Interceptor**:
   * Add bearer token header to fetch requests in `src/api/client.ts`.

6. **Verify**:
   * Verify login redirect and callback against Zitadel.
   * Verify dev bypass mode in local development.
   * Run Playwright tests (`npm run test:e2e`) ensuring mock auth continues to pass cleanly.

---

## 4. Acceptance Criteria

- [ ] React SPA initiates PKCE login redirect against Zitadel when unauthenticated in production mode.
- [ ] Successful login exchanges code, captures JWT, and surfaces user identity in the UI.
- [ ] Top header displays user profile dropdown with role chips and logout action.
- [ ] UI controls dynamically disable or hide destructive operations for Viewer users.
- [ ] Dev bypass mode remains 100% functional for offline development, Standby CLI, and Playwright tests.
