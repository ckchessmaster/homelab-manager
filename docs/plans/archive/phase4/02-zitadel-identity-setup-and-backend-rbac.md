# Plan 02: Zitadel Identity Provider Setup & Backend JWT / RBAC Engine

**Phase:** Phase 4: Productionization & Deployment  
**Status:** ✅ Completed  
**Dependencies:** [Plan 01: Production Containerization & Compose](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase4/01-production-containerization-and-compose.md)  

---

## 1. Objectives & Overview

Establish enterprise-grade OpenID Connect (OIDC) identity management with **Zitadel** and enforce Role-Based Access Control (RBAC) across the ASP.NET Core Backend API:

1. **Zitadel Service Configuration & Initialization**:
   * Define Zitadel container definitions in the production Compose stack (`docker/compose/docker-compose.zitadel.yml`) and Aspire orchestrator (`ControlPlane.AppHost`).
   * Automate tenant initialization script (`docker/compose/zitadel/init-zitadel.sh`) creating Organization, Project `controlplane`, SPA Application with PKCE, Machine User for API, and Roles: `admin`, `operator`, `viewer`.

2. **Backend JWT Bearer Token Validation**:
   * Add `Microsoft.AspNetCore.Authentication.JwtBearer` to `ControlPlane.Api`.
   * Configure JWT validation against Zitadel issuer (`/oauth/v2/keys`, TokenValidationParameters, ValidAudience, ValidIssuer).

3. **Composite Authentication Scheme**:
   * Implement `CompositeAuthenticationHandler` supporting:
     * **Zitadel Bearer Token**: Standard browser SPA sessions (`Authorization: Bearer <jwt>`) with WebSocket access_token fallback.
     * **API Key (`X-ControlPlane-Key`)**: Headless CLI runners, cron scripts, Standby CLI runner.
     * **Dev Bypass (`AUTH_BYPASS=true`)**: Local offline development.

4. **Role-Based Access Control (RBAC) Policies**:
   * Map Zitadel project roles (`urn:zitadel:iam:org:project:roles`) into ASP.NET Core claims (`ClaimTypes.Role`).
   * Define policies:
     * `RequireAdmin`: Host adoption, credentials modification, deleting hosts, initiating node rollbacks, executing custom remote commands.
     * `RequireOperator`: Triggering predefined upgrade workflows, approving reboot gates, launching rolling fleet batches.
     * `RequireViewer`: Read-only access to host tables, discovery candidates, workflow metrics, audit logs.
   * Apply policies across all Minimal API route groups.

---

## 2. Target File Structure

```
docker/
└── compose/
    ├── docker-compose.zitadel.yml           # Zitadel container & init setup
    └── zitadel/
        └── init-zitadel.sh                  # Bootstrap script creating org, project, app & roles

src/Aspire/ControlPlane.AppHost/
└── AppHost.cs                               # Zitadel container orchestration in Aspire

src/ControlPlane.Api/
├── Security/
│   ├── AuthConstants.cs                    # Policy and scheme name constants
│   ├── CompositeAuthenticationHandler.cs   # Forwards between JwtBearer, ApiKey, and DevBypass
│   ├── ZitadelJwtOptions.cs                # Zitadel configuration POCO
│   └── ZitadelRoleClaimsTransformation.cs  # Extracts Zitadel project roles into ClaimsPrincipal
└── Program.cs                              # Authentication & Authorization registration
```

---

## 3. Implementation Steps

1. **Zitadel Compose & Bootstrap Setup**:
   * Configure Zitadel container with PostgreSQL backend and self-signed or Let's Encrypt TLS.
   * Create `init-zitadel.sh` using Zitadel CLI or Management API to create the `controlplane` project, define roles (`admin`, `operator`, `viewer`), and register the SPA client.

2. **Configure ASP.NET Core Authentication**:
   * Add `Microsoft.AspNetCore.Authentication.JwtBearer` package.
   * Configure JWT Bearer options with `Authority`, `Audience`, and metadata discovery.
   * Wire `CompositeAuthenticationHandler` to inspect the `Authorization` header vs `X-ControlPlane-Key`.

3. **Role Claims Transformation**:
   * Implement `IClaimsTransformation` to parse Zitadel's nested JSON role claims from the JWT and inject standard `Claim(ClaimTypes.Role, "admin")` into the identity.

4. **Enforce Authorization on Endpoints**:
   * Update host mutation endpoints (`POST /api/v1/hosts`, `DELETE /api/v1/hosts/{id}`) with `.RequireAuthorization(AuthConstants.RequireAdmin)`.
   * Update workflow triggers (`POST /api/v1/workflows/launch`) with `.RequireAuthorization(AuthConstants.RequireOperator)`.
   * Keep read endpoints with `.RequireAuthorization(AuthConstants.RequireViewer)`.

5. **Verify**:
   * Test API endpoints with valid admin token, operator token, viewer token, API key, and invalid tokens.
   * Ensure unauthorized actions return HTTP 403 Forbidden.

---

## 4. Acceptance Criteria

- [x] Zitadel compose definition runs and initializes the `controlplane` application and roles.
- [x] ASP.NET Core API validates Zitadel JWT tokens via OIDC discovery.
- [x] Composite authentication supports both Zitadel JWTs and static `X-ControlPlane-Key`.
- [x] RBAC policies correctly restrict admin vs operator vs viewer actions.
