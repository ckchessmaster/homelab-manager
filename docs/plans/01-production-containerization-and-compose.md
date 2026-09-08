# Plan 01: Multi-Stage OCI Containerization & Production Compose Stack

**Phase:** Phase 4: Productionization & Deployment  
**Status:** ✅ Completed  
**Dependencies:** [Phase 3 Architecture](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase3/08-playwright-e2e-testing.md)  

---

## 1. Objectives & Overview

Containerize the ControlPlane platform using lightweight, secure, multi-stage OCI Dockerfiles and assemble a unified production `docker-compose.prod.yml` stack suitable for baremetal workstations or standalone homelab nodes:

1. **`ControlPlane.Api` Dockerfile**:
   * Multi-stage .NET 10 build targeting ASP.NET Core runtime (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` or Debian-slim).
   * Run as non-root user (`app`, UID 1654).
   * Health check endpoint integration (`/healthz`).
   * Minimal image footprint (<180MB) and hardened attack surface.

2. **`frontend` Production Dockerfile**:
   * Multi-stage build: Node 22 alpine build stage compiles static assets (`npm run build`).
   * Production runtime stage: Unprivileged Nginx or Caddy serving compiled static assets.
   * Client-side SPA routing (`try_files $uri $uri/ /index.html`).
   * Reverse proxy rules forwarding `/api/` and `/hubs/` (WebSocket support) to the API backend container.
   * Gzip/Brotli compression and hardened security headers (CSP, X-Content-Type-Options, HSTS).

3. **Production Compose Stack (`docker-compose.prod.yml`)**:
   * Orchestrate:
     * `controlplane-postgres`: PostgreSQL 16 with dedicated `controlplane` database and volume persistence.
     * `controlplane-temporal`: Temporal dev server or standalone server with persistent sqlite/postgres backend.
     * `controlplane-api`: ASP.NET Core BFF container wired to postgres & temporal.
     * `controlplane-frontend`: Static web server with TLS / port 80/443 exposure.
   * Parameterized environment configuration using `.env.prod.example`.

---

## 2. Target File Structure

```
homelab-manager/
├── docker/
│   ├── api/
│   │   └── Dockerfile                       # Multi-stage .NET 10 API container
│   ├── frontend/
│   │   ├── Dockerfile                       # Multi-stage Node -> Nginx container
│   │   └── nginx.conf                       # Production Nginx reverse proxy & SPA router
│   └── compose/
│       ├── docker-compose.prod.yml          # Production multi-container composition
│       └── .env.prod.example                # Sample production environment variables
```

---

## 3. Implementation Steps

1. **Create API Dockerfile (`docker/api/Dockerfile`)**:
   * Build stage: SDK `mcr.microsoft.com/dotnet/sdk:10.0`, restore solution packages, publish in `Release` mode with optimizations.
   * Runtime stage: `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, copy publish output, configure port `8080`, define healthcheck.

2. **Create Frontend Dockerfile & Nginx Configuration (`docker/frontend/`)**:
   * Nginx configuration with gzip, WebSocket headers for SignalR (`/hubs/`), and SPA fallback.
   * Multi-stage Dockerfile building the React 19 app and copying `/dist` into `/usr/share/nginx/html`.

3. **Create Production Compose Stack (`docker/compose/docker-compose.prod.yml`)**:
   * Define networks, volumes, health checks, dependency ordering (`depends_on: { condition: service_healthy }`).
   * Provide sample `.env.prod.example`.

4. **Verify**:
   * Build both container images locally using `docker build`.
   * Test compose stack startup and ensure `/healthz` and frontend dashboard load successfully.

---

## 4. Acceptance Criteria

- [x] `docker/api/Dockerfile` builds cleanly without warnings and runs as a non-root user.
- [x] `docker/frontend/Dockerfile` compiles React 19 SPA and serves it via Nginx with WebSocket forwarding for SignalR hubs.
- [x] `docker/compose/docker-compose.prod.yml` starts PostgreSQL, Temporal, API, and Frontend with health checks.
- [x] Security headers and health probes pass verification.
