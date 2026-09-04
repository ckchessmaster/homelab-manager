# Plan 16: Production Zitadel OIDC Authentication & Role-Based Access Control

**Target Milestone:** Milestone 4 (Out-of-Band Integrations & Zitadel OIDC)  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 03: Dev Auth](file:///home/ckingdon/projects/homelab-manager/docs/plans/03-dev-auth-and-api-key.md), [Plan 05: Frontend Dashboard](file:///home/ckingdon/projects/homelab-manager/docs/plans/05-frontend-inventory-dashboard.md)  

---

## 1. Objectives & Overview

Upgrade the system from Phase 1 static API keys to enterprise-grade OpenID Connect (OIDC) identity management:
1. Provide deployment manifests / Compose configuration for a self-hosted **Zitadel** instance.
2. Integrate Zitadel in the React 19 SPA using Authorization Code Flow with Proof Key for Code Exchange (PKCE).
3. Configure the ASP.NET Core API with `Microsoft.AspNetCore.Authentication.JwtBearer` to validate JWT tokens issued by Zitadel.
4. Enforce Role-Based Access Control (RBAC):
   * **Admin:** Full access to node adoption, manual execution, rollback, and appliance credentials.
   * **Operator:** Read-only access to host tables, telemetry vitals, and execution logs.
5. Retain static API key authentication for headless CLI execution and automated scripts.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Security/
    ├── Oidc/
    │   ├── JwtBearerConfiguration.cs
    │   └── RbacPolicyConstants.cs
    └── CompositeAuthenticationHandler.cs

src/frontend/src/
└── features/
    └── auth/
        ├── AuthProvider.tsx
        ├── useAuth.ts
        ├── LoginPage.tsx
        └── authConfig.ts
```

---

## 3. Implementation Details

### Step 1: Frontend PKCE Authentication Flow
* Install `oidc-client-ts` / `react-oidc-context`.
* Configure Zitadel client:
  * Authority: `https://zitadel.homelab.local`
  * Client ID: `controlplane-spa`
  * Redirect URI: `https://controlplane.homelab.local/callback`
  * Response Type: `code`
  * Scope: `openid profile email urn:zitadel:iam:org:project:roles`
* Attach Bearer token to all TanStack Query API requests via Axios/fetch interceptor.

### Step 2: Backend JWT Bearer Validation
* In `Security/DependencyInjection.cs`:
  ```csharp
  services.AddAuthentication()
      .AddJwtBearer(options =>
      {
          options.Authority = config["Zitadel:Authority"];
          options.Audience = config["Zitadel:ClientId"];
          options.TokenValidationParameters.ValidateAudience = true;
      });
  ```
* Implement a composite scheme forwarding between JWT Bearer and the existing `ApiKey` scheme when `X-ControlPlane-Key` is present.

### Step 3: RBAC Policies
* Define policies:
  * `[Authorize(Policy = RbacPolicyConstants.RequireAdmin)]` on mutation endpoints (Adoption, Trigger Upgrade, Rollback).
  * `[Authorize(Policy = RbacPolicyConstants.RequireOperator)]` on read endpoints.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Verify API rejects unauthenticated requests when dev bypass is disabled
AUTH_BYPASS=false curl -i http://localhost:5000/api/v1/hosts

# Verify JWT token validation with valid Zitadel bearer token
curl -i -H "Authorization: Bearer <valid-jwt>" http://localhost:5000/api/v1/hosts
```

### Acceptance Criteria
- [ ] React SPA redirects unauthenticated operators to Zitadel login page and completes PKCE code exchange upon callback.
- [ ] JWT Bearer tokens are validated by the ASP.NET Core API.
- [ ] Operators with `Operator` role cannot trigger destructive node upgrades or adopt new nodes.
- [ ] Milestone 4 and full production hardening are complete upon finishing this plan.
