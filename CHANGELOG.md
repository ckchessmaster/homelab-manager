# Changelog

All notable changes to the **ControlPlane** (Homelab Orchestration & Management Plane) project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## v1.4.0

### Added
* **Mobile-First Responsive Navigation & Ergonomics**:
  * Persistent `<MobileBottomNav />` with bottom thumb-reach navigation for primary tabs (**Hosts**, **Workloads**, **Discovery**, **Workflows**), pending reboot alert beacons, and safe-area insets (`env(safe-area-inset-bottom)`).
  * Slide-up "More" drawer providing fast access to secondary views (**Adapters**, **System & Settings**, **Storage & Runtime**).
  * Viewport-adaptive shell hiding the desktop sidebar and adapting header padding on viewports `<768px`.
  * `useMediaQuery` and `useIsMobile` hooks for responsive layout management.
* **Mobile Inventory & Candidate Card Views**:
  * Added `<HostMobileCardList />` rendering touch-friendly cards with platform badges, IP links, status pills, and action menus.
  * Added responsive mobile card view for discovered candidates in `<DiscoveryView />`.
  * Viewport-conditional unmounting (`isMobile ? <MobileCardList /> : <Table />`) preventing horizontal overflow and DOM collisions.
* **Mobile DAG Workflow Timeline**:
  * Implemented `<MobileDagTimeline />` to degrade complex 2D ReactFlow canvas DAG graphs into an accessible, touch-friendly vertical execution timeline on mobile devices.
* **Touch Primitives & Defensive UI Hardening**:
  * Touch-optimized `<MetricStrip />` with snap-scrolling, minimum 44×44px interactive targets, and defensive guards against `NaN` metrics.
  * Responsive `<TableToolbar />` with fluid full-width search and controls on mobile viewports.
  * Safe-area padding and 44px dismiss controls in `<Sheet />` inspectors and modal dialogs.
* **Frontend Testing & Verification Infrastructure**:
  * Configured Vitest, `@testing-library/react`, `@testing-library/jest-dom`, and `jsdom` with co-located component test suites (`*.test.tsx`).
  * Configured Playwright multi-project responsive test matrix covering Desktop Chrome, Mobile Chrome (`Pixel 7`), and Mobile Safari (`iPhone 14`).
* **Agentic Mobile & Quality Governance**:
  * Updated `AGENTS.md`, `.agents/rules/frontend-react.md`, and `DESIGN_SYSTEM.md` establishing mandatory verification gates (`npm test`, `npm run lint`, `npm run test:e2e`, and `dotnet test`) and mobile-first design invariants for all future coding agents.

---

## v1.3.1

### Added
* **Backend Application Logs UI & System Diagnostics**:
  * Reorganized navigation: renamed **Settings & Security** to **System & Settings** in the primary navigation sidebar.
  * Added segmented sub-navigation in the new System & Settings view:
    * **Backend Logs**: Interactive console and diagnostics viewer.
    * **Security & Tokens**: Authentication mode, active API headers, and Personal Access Tokens management.
    * **Agent Binaries**: Compute node agent binary matrix and GitHub release synchronizer.
    * **Storage & Runtime**: Dual-topology PostgreSQL/SQLite status, process uptime, memory footprint, and host diagnostics.
  * Implemented `SystemLogsView` with:
    * Compact top `<MetricStrip>` showing total buffered logs, errors (with critical alert badge), warnings, information events, and ring buffer capacity.
    * Unified `<TableToolbar>` with live text search, log level filter (All Levels, Information+, Warning+, Errors Only), and category filter.
    * Live tail toggle with real-time polling (2.5s interval) and pulsing status beacon.
    * Auto-scroll toggle, download/export to `.log` file, and safe buffer clear with confirmation dialog.
    * Right-sliding `<Sheet>` inspector drawer displaying full log message, exception stack trace with one-click copy, and raw JSON payload.
  * Implemented high-performance, thread-safe in-memory circular buffer (`SystemLogBuffer`) with capacity of 2,500 entries.
  * Wired `SystemLogProvider` into the ASP.NET Core `ILoggerFactory` pipeline to capture application logs across all subsystems.
  * Added REST endpoints in `SystemEndpoints`: `GET /api/v1/system/logs`, `GET /api/v1/system/logs/stats`, `DELETE /api/v1/system/logs`, and `GET /api/v1/system/info`.
  * Added `QuerySystemLogs` and `ClearSystemLogs` Model Context Protocol (MCP) tools to `ControlPlaneMcpTools` for AI assistant diagnostics.
* **Agent Binary Auto-Sync Engine**:
  * Implemented `IAgentBinarySyncService` and `AgentBinaryBackgroundService` to automatically synchronize static Go agent binaries from GitHub releases at startup and periodically.
  * Added interactive binary distribution management card (`AgentBinariesCard`) with manual sync trigger and force re-download options.

### Fixed
* **Windows Compute Node Agent Service Installation (`install.ps1`)**:
  * Fixed Windows Service failure (`Cannot start service ControlPlaneAgent on computer '.'`) caused by improper backslash escaping in Windows registry `ImagePath`.
  * Added missing `[switch]$Insecure` parameter to both `install-agent.ps1` and `FallbackInstallScript` in `AgentManagementEndpoints.cs`.
  * Enabled automatic SSL certificate validation bypass in PowerShell when `-Insecure` is specified to allow downloading agent binaries from self-signed HTTPS endpoints.
  * Forwarded `--insecure` CLI flag to the Go agent daemon service command line when installed in insecure mode.
  * Packaged `install-agent.ps1` into `/app/scripts/` inside the production Docker image.

---

## v1.3.0

### Added
* **Personal Access Tokens (PAT) & Machine Keys Engine**:
  * Implemented scoped, revocable API tokens (`cp_pat_...`) with SHA-256 cryptographic hashing, prefix tracking, fine-grained RBAC roles (`Admin`, `Operator`, `Viewer`), and custom expiration policies (30d, 90d, 1y, or non-expiring).
  * Dual-topology storage support for both PostgreSQL (`api_tokens` table via EF Core migration) and Standby Runner SQLite.
  * Added `GET /api/v1/tokens`, `POST /api/v1/tokens`, and `DELETE /api/v1/tokens/{id}` REST endpoints.
  * Extended `ApiKeyAuthenticationHandler` and `CompositeAuthenticationHandler` to automatically authenticate PAT tokens via either `X-ControlPlane-Key` header or `Authorization: Bearer cp_pat_...`.
* **Personal Access Tokens UI in Settings**:
  * Added interactive PAT management card in the Settings tab, allowing administrators to generate scoped tokens, view expiration/last-used metadata, and revoke tokens immediately.
  * Added modal displaying newly generated raw tokens with one-click copy and pre-formatted Model Context Protocol (MCP) JSON client configurations for Cursor, Claude Desktop, and Antigravity.
* **Configurable Model Context Protocol (MCP) Server in Helm**:
  * Disabled the embedded MCP server (`/mcp`) by default in `charts/controlplane/values.yaml` (`mcp.enabled: false`).
  * Added configurable Helm values for MCP ingress routing (`mcp.ingress.enabled` and `mcp.ingress.path: /mcp`), allowing administrators to safely expose MCP over custom ingress hosts or keep it internal.
  * Added MCP streaming proxy configuration in both frontend Nginx and `frontend-configmap.yaml` with SSE buffering disabled (`proxy_buffering off`, `proxy_cache off`).
* **Node Agent Insecure TLS Flag**:
  * Added `--insecure` CLI flag and `CONTROLPLANE_INSECURE=true` environment variable to `controlplane-agent` (Go) daemon to bypass TLS verification (`InsecureSkipVerify: true`) when connecting to homelab servers with self-signed certificates.
  * Exposed `--insecure` checkbox in single-node and mass-adoption frontend modals.

### Fixed
* **Agent Adoption Port & Scheme Resolution**:
  * Fixed frontend agent adoption modals (`AdoptNodeModal.tsx` and `MassAdoptHostsModal.tsx`) hardcoding `:5029` and `ws://` in `getInitialHubUrl()`. In cluster mode, ingress operates on port 80/443 without port 5029 exposed; the frontend now dynamically derives the hub URL from `window.location.host` and matching WebSocket protocols (`wss://` / `ws://`).
  * Added `ResolveHubUrl` in backend `NodeAdoptionService` to automatically replace `localhost` or loopback addresses with the backend host or configured `ControlPlane:HubUrl`.
* **Remote Adoption Diagnostic Capture**:
  * Added automatic capture of remote `journalctl -u controlplane-agent -n 15` output upon handshake timeout, outputting the exact daemon failure or TLS refusal directly in adoption step 5.
* **Database Query Portability**:
  * Resolved SQLite `DateTimeOffset` in-memory sorting compatibility in `ApiTokenService` to maintain strict dual-topology invariants across PostgreSQL and Standby SQLite modes.

---

## v1.2.8

### Added
* **Embedded Static Agent Binaries for Node Adoption**: Multi-stage Docker build packaging precompiled static Go node agents (`controlplane-agent-linux-amd64`, `controlplane-agent-linux-arm64`, `controlplane-agent-windows-amd64.exe`) directly into `/app/agent-dist` within the `controlplane-api` image, enabling one-click SSH bootstrap adoption in Kubernetes without missing binary errors.
* **Configurable Agent Distribution Directory**: Added `AGENT_DIST_DIR` environment variable and `ControlPlane:AgentDistDir` configuration support in `NodeAdoptionService` to support custom or volume-mounted binary locations.
* **CiliumNetworkPolicy Support**: Added optional `CiliumNetworkPolicy` resource (`charts/controlplane/templates/cilium-networkpolicy.yaml`) enabled via `networkPolicy.cilium.enabled` (default `false`) to permit ControlPlane API egress to the Kubernetes API server (`kube-apiserver`) in Cilium CNI clusters.
* **Dynamic Frontend OIDC Bootstrap**: Added unauthenticated `GET /api/v1/auth/config` endpoint in the API to serve active auth mode, Zitadel authority, client ID, and role mappings to the React SPA at runtime, eliminating hardcoded build-time URLs.
* **Configurable Custom Role Mapping**: Supported mapping custom IdP roles, groups, or claims (e.g. `homelab-admins`, `devops`, `family`) to ControlPlane canonical roles (`Admin`, `Operator`, `Viewer`) in both backend JWT claims transformation and frontend permissions evaluation.
* **Helm Role Configuration**: Added `zitadel.clientId` and `zitadel.roles` (with `admin`, `operator`, and `viewer` lists) to `values.yaml` and mapped them into `configmap.yaml`.
* **Dynamic Nginx Reverse Proxy Template**: Added `frontend-configmap.yaml` Helm template that dynamically sets Nginx's `upstream api_upstream` to match the exact release-specific API service name (`{{ include "controlplane.fullname" . }}-api`), preventing upstream resolution crashes regardless of the Helm release name.
* **Granular CSP Domain Configuration**: Added `frontend.csp.extraConnectSrc` and `frontend.csp.extraFrameSrc` options in `values.yaml` and `frontend-configmap.yaml` to allow administrators to whitelist additional specific domains while maintaining strict least-privilege security without wildcards.
* **Standalone Container CSP Entrypoint Hook**: Added `/docker-entrypoint.d/40-configure-csp.sh` to dynamically configure Nginx CSP headers from environment variables (`ZITADEL_AUTHORITY`, `CSP_CONNECT_SRC`, `CSP_FRAME_SRC`) in standalone Docker and Docker Compose deployments.
* **Native OCI Helm Registry Support**: Added native OCI registry support in `HelmClient` and `InstallHelmModal` without passing the unsupported `--repo` flag.
* **Identity Provider Display**: Displayed active Zitadel authority URL on the login page when OIDC mode is active.

### Fixed
* **Zitadel Client ID Default Fallback**: Fixed `Zitadel__ClientId` falling back to chart default `"controlplane"` instead of `.Values.zitadel.audience` when `zitadel.clientId` is omitted in `values.yaml`, resolving `Errors.App.NotFound` from Zitadel during OIDC authorization.
* **Frontend Content Security Policy (CSP) OIDC Scoping**: Dynamically scoped `connect-src` and `frame-src` in the frontend Nginx reverse proxy to include `.Values.zitadel.authority`, allowing the React SPA to query OIDC metadata (`/.well-known/openid-configuration`), fetch JWKS, exchange authorization codes, and perform silent token renewal without violating CSP.
* **Database Connection Configuration**: Made explicit `Database__Host`, `Database__Database`, `Database__Username`, and `Database__Port` always take precedence over stale connection strings (e.g. from chart defaults or prior installs) in `DependencyInjection.cs`.
* **ASP.NET Core Environment Configuration Mapping**: Supported both hierarchical colon-delimited (`Database:Host`) and double-underscore (`Database__Host`) configuration keys, ensuring environment variables injected via Kubernetes `ConfigMap` and `Secret` are correctly bound.
* **CloudNativePG Secret Resolution**: Supported external password secrets (e.g. `passwordSecretName`) mounted by CloudNativePG, checking keys `password`, `PASSWORD`, `DB_PASSWORD`, and preventing namespace-local short URI hostnames from breaking cross-namespace DNS.
* **Helm Deployment Rolling Updates**: Added dynamic checksum annotations (`checksum/config` and `checksum/secret`) to `api-deployment.yaml` and `frontend-deployment.yaml` so `helm upgrade` triggers a rolling restart when configuration values change.
* **NetworkPolicy In-Cluster & Egress Rules**: Permitted cross-namespace in-cluster egress (PostgreSQL in `cnpg-services`, Temporal in `temporal`, Kubernetes API) and outbound HTTPS/HTTP (port 443 for Zitadel IdP and OCI registries) in `networkpolicy.yaml`.
* **Temporal Server URL Default**: Defaulted `temporal.serverUrl` to empty string in `values.yaml` so custom `temporal.address` values populate both `Temporal__Address` and `Temporal__ServerUrl`.
* **Database Secret Sanitization**: Updated `charts/controlplane/templates/secret.yaml` to store database password under `Database__Password` instead of generating a hardcoded `ConnectionStrings__ControlPlaneDatabase`, eliminating stale default host references.
* **Migration Target Diagnostics**: Enhanced `InitializeDatabaseAsync` startup logs to explicitly output the resolved target PostgreSQL host and database name (`DataSource/Database`).
* **Frontend Nginx Upstream Resolution**: Fixed frontend Nginx crash (`host not found in upstream "controlplane-api:8080"`) when installing the Helm chart with custom release names (e.g. `homelab-manager`).
* **Default Image Repository References**: Fixed image repository references in default `values.yaml` to point to `ghcr.io/ckchessmaster/controlplane-api` and `ghcr.io/ckchessmaster/controlplane-frontend`.
* **Runtime Config Fallbacks**: Added runtime configuration fallbacks (`localStorage` and `window.__CONTROLPLANE_CONFIG__`) for local development and offline environments.

---

## v1.2.0

### Initial General Availability (GA) Release

ControlPlane v1.2.0 is the foundational release of the resilient homelab orchestration platform. It replaces brittle push-and-wait configuration management (Ansible) and solves the chicken-and-egg upgrade dilemma through a hybrid agent/agentless topology, durable Temporal workflows, unified infrastructure adapters, and built-in Model Context Protocol (MCP) integration.

---

### Highlights & Key Features

#### 1. Dual-Topology Architecture (Cluster & Standby Mode)
* **Cluster Mode**: High-availability Kubernetes deployment powered by ASP.NET Core (.NET 10) and PostgreSQL (CloudNativePG / Bitnami).
* **Standby Takeover Mode**: Single-binary CLI runner (`ControlPlane.Cli`) backed by SQLite that takes over cluster maintenance during physical node and hypervisor upgrades without circular database dependencies.
* **Outbound-Only Go Node Agent**: Lightweight, statically compiled compute node daemon (`controlplane-agent`, <15MB binary, <10MB RSS) with zero listening ports, dialing outbound to ControlPlane over secure WebSockets (`wss://`).

#### 2. Durable Temporal Workflow Orchestration
* **Durable Sagas & DAG Execution**: State-machine-driven update pipelines (`Pending` -> `Running` -> `Verifying` -> `Completed` / `RolledBack`) with automated compensations and approval gates.
* **Dual Temporal Support**: Configurable to run with local containerized Temporal for inner-loop development or external Kubernetes Temporal clusters.
* **Strict Namespace & Queue Isolation**: Default binding to dedicated `homelab-manager` namespace and `homelab-manager-tasks` task queue.
* **Zitadel M2M OAuth2 / OIDC Token Injection**: Proactive in-memory token refresh service injecting Bearer authorization metadata over gRPC to authenticated Temporal clusters.
* **Temporal Health Checks & Diagnostics**: Startup diagnostics and gRPC connectivity health probing via `DescribeNamespaceAsync`.

#### 3. Modular Agentless Infrastructure Adapters
* **Hypervisors (Proxmox VE)**: Node vitals, LXC/QEMU inventory, automated candidate discovery, snapshot management, and OS detection.
* **Firewalls & Routers (OPNsense)**: Real-time firewall status, gateway diagnostics, and candidate discovery across DHCP/ARP tables.
* **Switches & Power (Ubiquiti UniFi)**: Switch port inventory, PoE power cycling for hung smart home bridges and access points.
* **Out-of-Band Hardware BMCs (Dell iDRAC & DMTF Redfish)**: Hardware sensor metrics, temperatures, power actions, chassis indicator LED controls, fan curve telemetry, and next-boot device selection.
* **Smart Home (Home Assistant)**: Instance discovery, integration health checks, backup generation, and supervised core updates.
* **Kubernetes Orchestrator**: Workload inventory (Deployments, StatefulSets, DaemonSets, Pods), rolling restarts, and replica scaling.

#### 4. React 19 Visual Terminal Canvas
* **Modern High-Density UI**: Built with React 19, TypeScript, Vite, Tailwind CSS, and shadcn/ui adhering to homelab design system guidelines (`zinc-950` canvas base, `sky-500` accents).
* **Visual Workflow Canvas**: Interactive node-based DAG visualization and execution tracing powered by `@xyflow/react`.
* **SignalR ANSI Terminal**: Real-time terminal streaming using `xterm.js` and SignalR with monotonic sequence ordering `(job_id, sequence_id, stream_type, log_line, timestamp)`.
* **Zitadel OIDC Authentication**: PKCE authentication with role-based UI component protection (`Viewer`, `Operator`, `Admin`).

#### 5. Embedded Model Context Protocol (MCP) Server
* Built-in streamable HTTP MCP server registered at `/mcp` exposing 30+ infrastructure tools for AI pair programmers and autonomous operations.
* Full audit trail tagging destructive operations (reboots, PoE cycles, BMC power commands) with `InitiatedBy = "AI Agent via MCP"`.

#### 6. Production Helm Chart & Cloud-Native Packaging
* Production-grade Kubernetes Helm chart in `charts/controlplane` with automated OCI distribution to GitHub Container Registry (`ghcr.io/ckingdon/charts/controlplane`).
* Flexible secrets integration supporting CloudNativePG (`passwordSecretName`), external secrets (`existingSecret`), and Zitadel client secrets (`clientSecretName`).
* Nginx and Traefik ingress configurations optimized for long-lived WebSocket and SignalR terminal streaming connections.

---

### Security
* **AES-256-GCM Encryption at Rest**: All sensitive credentials, API tokens, and passwords stored securely via `ISecretEncryptionService` with automatic masking in all API responses.
* **Zero Inbound Ports on Compute Nodes**: Node agents strictly initiate outbound connections with mutual heartbeat keep-alives.
