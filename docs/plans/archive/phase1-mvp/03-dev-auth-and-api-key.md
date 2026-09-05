# Plan 03: Dev Auth & Static API Key Authentication

**Target Milestone:** Milestone 1 (Foundation, Data Layer & Host Inventory)  
**Status:** ✅ Completed  
**Dependencies:** [Plan 01: Solution Scaffolding](file:///home/ckingdon/projects/homelab-manager/docs/plans/01-solution-scaffolding.md)  

---

## 1. Objectives & Overview

Implement the Phase 1 authentication strategy:
1. Provide an `ApiKeyAuthenticationHandler` that authenticates requests using the `X-ControlPlane-Key` HTTP header.
2. Implement a developer bypass mode (`AUTH_BYPASS=true`), automatically establishing an authenticated `DevAdmin` user with `Admin` claims.
3. Configure ASP.NET Core Authorization policies (`AdminPolicy`, `OperatorPolicy`).
4. Configure OpenAPI / Scalar / Swagger to support entering the API key during interactive API testing.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Security/
    ├── ApiKeyAuthenticationHandler.cs
    ├── ApiKeyAuthenticationOptions.cs
    ├── SecurityHeadersMiddleware.cs
    └── DependencyInjection.cs
```

---

## 3. Implementation Details

### Step 1: Authentication Handler
Implement `ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>`:
* Check `_config.GetValue<bool>("AUTH_BYPASS", false)`:
  * If true, return `AuthenticateResult.Success` with claims: `Name: "DevAdmin"`, `Role: "Admin"`.
* Inspect `Request.Headers["X-ControlPlane-Key"]`:
  * Match against `_config["ControlPlane:ApiKey"]` (stored in user secrets or environment).
  * If matched, return `AuthenticateResult.Success` with claims: `Name: "ApiKeyUser"`, `Role: "Admin"`.
  * If missing or mismatched, return `AuthenticateResult.Fail("Invalid or missing API Key.")`.

### Step 2: Policy & Middleware Registration
In `Security/DependencyInjection.cs`:
```csharp
services.AddAuthentication("ApiKey")
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthHandler>("ApiKey", null);

services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
});
```

### Step 3: OpenAPI Documentation Support
Configure OpenAPI to register the `X-ControlPlane-Key` security requirement so developers can test authenticated endpoints directly via Swagger/Scalar UI.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Test with invalid key (should return 401 Unauthorized)
curl -i http://localhost:5000/api/v1/hosts

# Test with valid key (should return 200 OK)
curl -i -H "X-ControlPlane-Key: dev-secret-key-123" http://localhost:5000/api/v1/hosts

# Test with AUTH_BYPASS=true (should return 200 OK without header)
AUTH_BYPASS=true curl -i http://localhost:5000/api/v1/hosts
```

### Acceptance Criteria
- [x] Endpoints protected with `[Authorize]` reject requests without a valid header when `AUTH_BYPASS=false`.
- [x] Requests bearing a matching `X-ControlPlane-Key` header succeed and receive `Admin` claims.
- [x] With `AUTH_BYPASS=true`, all endpoints succeed transparently as `DevAdmin`.
