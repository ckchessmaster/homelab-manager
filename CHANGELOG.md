# Changelog

All notable changes to the **ControlPlane** (Homelab Orchestration & Management Plane) project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## v1.10.0

### Added
* **Generic OpenID Connect (OIDC) Authentication & IdP Agnostic Architecture**:
  * **Backend JWT Authentication Engine (`OidcJwtOptions`, `OidcRoleClaimsTransformation`)**: Completely migrated from Zitadel-specific authentication to generic OpenID Connect. Added configuration options supporting any standard OIDC identity provider (Authentik, Dex, Keycloak, etc.) via `Oidc:Authority` / `Oidc:IssuerUrl`. The audience automatically falls back to `ClientId` if unspecified.
  * **Configurable Claims & Strict Role Transformation**: Implemented `OidcRoleClaimsTransformation` to extract roles from configurable claims (`Oidc:RoleClaimType`, default `groups`) and map custom IdP groups (e.g. `admin`, `authentik Admins`) to canonical ControlPlane roles (`Admin`, `Operator`, `Viewer`). Username resolution maps cleanly to `Oidc:NameClaimType` (`preferred_username`).
  * **Generic Temporal M2M OAuth2 Token Provider (`ITemporalTokenProvider`, `TemporalOAuth2TokenProvider`)**: Replaced Zitadel-coupled token acquisition with a generic OAuth2 `client_credentials` provider for Temporal workflow orchestrator communication.
  * **Frontend OIDC PKCE Integration (`authConfig`, `roleUtils`, `AuthProvider`)**: Standardized frontend authentication on generic `VITE_OIDC_*` variables with dynamic fallback to `/api/v1/auth/config`. Updated role mapping utilities and login button branding.
  * **Lightweight Dex Identity Provider for Local Development (`ControlPlane.AppHost`)**: Replaced the legacy Zitadel container stack and YARP proxy in Aspire with a single-container Dex identity provider (`dexidp/dex:v2.41.1`) pre-configured with local test users (`admin`, `operator`, `viewer`) and direct Authentik proxy compatibility.
  * **Production Helm Chart Modernization (`charts/controlplane`)**: Updated chart configuration from Zitadel to `oidc.*`, updated frontend CSP script injection, defaulted `oidc.audience` fallback, and removed legacy compose directory.

---

## v1.9.0

### Added
* **Global Workflow Approval Alerting & Quick-Review System**:
  * **App Header Indicator (`AppHeader`)**: Added a persistent amber vitals pill with pulsing shield icon visible across all application views on desktop and mobile, displaying active waiting approval counts and opening the review modal directly.
  * **Navigation Badges (`AppSidebar` & `MobileBottomNav`)**: Dynamic warning badge on the `Workflows & DAGs` sidebar item and pulsing badge dot on mobile bottom navigation whenever an update pipeline reaches a human gate.
  * **Persistent Approval Toast (`ApprovalToastNotification`)**: Floating high-contrast glassmorphic notification banner with direct "Review & Approve" action and dismissible reminder lifecycle.
  * **Global Quick Approval Modal (`QuickApprovalModal`)**: Modal accessible from any tab without losing view state, supporting multi-job queues, single-click "Approve Reboot", "Reject & Abort" (with optional reason prompt), batch "Approve All", and deep links to the visual DAG canvas.
  * **Workflows & DAGs Table Integration (`WorkflowsView`)**: Added dedicated "Awaiting Approval" filter tab, glowing amber status badge, and prominent "Review Approval" row actions.
  * **Host Inventory Amber Gate Badges (`HostTable`, `HostMobileCardList`, `GroupedHostView`)**: Distinct amber "Awaiting Approval" badges with shield icons on host rows, opening the approval modal on click.
* **Windows Server License Evaluation & Automatic Rearm Engine**:
  * **Agent Evaluation Collector**: Added automatic detection of Windows evaluation status, remaining grace days, and rearm counts via `slmgr.vbs` and WMI.
  * **Durable Rearm Workflow (`IWindowsLicenseRearmWorkflow`)**: Temporal workflow coordinating preflight checks, non-destructive rearm execution, coordinated reboot, and health verification.
  * **Operator UI & Badges**: Added `WindowsLicenseBadge`, evaluation warnings in Host Inventory, and modal with one-click rearm trigger.

---

## v1.8.1

### Fixed
* **Multi-Pod Command Result Resolution (`AgentCommandExecutor`)**:
  * Added `IAgentMessageBus.OnJobLogReceived` subscription to `AgentCommandExecutor`. When an agent command is forwarded to a peer replica, the originating pod now receives the streamed log frames and `complete` / `exit_code` events across the inter-pod message bus, successfully resolving `WaitForCompletionAsync` without timing out.
* **Proxmox VM Hypervisor Recovery Following Rollback**:
  * Implemented `StartVmAsync` across `IProxmoxClient`, `ProxmoxClient`, and `MockProxmoxClient` using Proxmox VE API `POST /api2/json/nodes/{node}/qemu/{vmid}/status/start`.
  * Updated `ProxmoxActivities.RollbackSnapshotAsync` to automatically power on the virtual machine immediately after `qmrollback` completes, preventing nodes from remaining permanently powered off in stopped state following snapshot restoration.
* **Hypervisor Snapshot Pruning & Idempotency**:
  * Enhanced `ProxmoxActivities.CreateSnapshotAsync` to query existing snapshots via `ListVmSnapshotsAsync` and automatically prune pre-existing snapshots bearing identical names before creating a new baseline snapshot, eliminating Proxmox duplicate snapshot errors.
* **Orchestration Foreign Key Protection & On-the-Fly Job Upsert**:
  * Added `HostId` parameter to `UpdateJobStatusInput` and forwarded target host ID from `HostUpgradeWorkflow` Step 0.
  * Enhanced `UpdateJobActivities.UpdateStatusAsync` and `WorkflowLogEmitter` to automatically upsert missing `UpdateJob` database records when `HostId` is available.
  * Guarded `step_logs` database persistence against unpersisted jobs, preventing PostgreSQL foreign key constraint violations (`FK_step_logs_update_jobs_JobId`) when sub-workflows emit logs before job creation.
* **Decoupled API Liveness and Readiness Probes**:
  * Mapped a lightweight dedicated `/healthz/live` endpoint checking process liveness without external database dependencies, preserving pod survival during transient database failovers.
  * Updated Helm chart `api.livenessProbe` to probe `/healthz/live` while maintaining readiness checks against `/healthz`.
  * Updated frontend Nginx proxy configuration to forward `/healthz` subpaths cleanly to the API upstream.

---

## v1.8.0

### Added
* **Active-Active Multi-Replica Support (Zero-Broker Inter-Pod Bus via PostgreSQL `LISTEN`/`NOTIFY`)**:
  * **Inter-Pod Message Bus (`IAgentMessageBus`, `PostgresAgentMessageBus`, `InMemoryAgentMessageBus`)**: Built an asynchronous brokerless communication plane using PostgreSQL's native `LISTEN`/`NOTIFY` on channel `controlplane_bus` for Kubernetes cluster mode, with a clean in-memory fallback for SQLite Standby mode preserving Invariant 1 (Dual-Topology Portability).
  * **Global Cross-Pod Agent Presence & Liveness Sync**: Updated `AgentConnectionManager` to track remote pod presence alongside local WebSocket sessions. In-flight heartbeats, connections, and disconnections are fanned out immediately across pods. Cold-booting pods automatically seed remote presence on startup via database records so newly launched replicas recognize online agents without waiting.
  * **Inter-Pod Command Forwarding**: Commands issued via REST, MCP, or UI directed at agents connected to peer pods are transparently routed across the message bus to the owning pod and dispatched to the active WebSocket session, completely eliminating split-brain drops.
  * **Deterministic Reboot Acknowledgment Fanout**: Reboot commencing signals received from agents are published across the message bus, unblocking `WaitForRebootCommencingAsync` callers on any replica.
  * **Cross-Pod Real-Time Terminal Streaming (`JobLogHubBridge`)**: Added `JobLogHubBridge` to subscribe to inter-pod `JobLog` and `JobStatus` events, forwarding frames directly to local SignalR `JobLogHub` client groups. Users connected to any pod can watch real-time console logs regardless of which pod the target agent is connected to.
  * **Production High Availability Scale**: Restored `replicaCount: 2` in production Helm chart with 100% stable fleet connectivity across all 13 node agents.

---

## v1.7.0

### Added
* **Resilient In-Cluster Upgrades & Deadlock Elimination**:
  * **Self-Pod Eviction Exemption**: Updated `KubernetesAdapter.IsEvictable` to exempt ControlPlane API pods (`app.kubernetes.io/part-of=controlplane` & `app.kubernetes.io/component=api`) from eviction during `DrainNodeAsync`. Prevents orchestrator suicide during node updates, allowing the active worker to finish package upgrades and trigger host reboots.
  * **Orchestrator-Last Rolling Update Queue Sorting**: Added `SortTargetsOrchestratorLast` to `RollingUpgradeWorkflow`. Automatically detects the Kubernetes node hosting the orchestrator pod via Downward API `KUBERNETES_NODE_NAME` and moves that node to the end of the batch queue so all other cluster nodes are updated, verified, and uncordoned first.
  * **Kubernetes Node 'Ready' Condition Gate**: Added `WaitForNodeReadyAsync` in `IKubernetesAdapter` and `AwaitNodeReadyAsync` in `KubernetesActivities`. In `HostUpgradeWorkflow`, explicitly verifies that the rebooted node reaches condition `Ready == True` before restoring scheduling via `UncordonNodeAsync`.
  * **Database & CloudNativePG Failover Resiliency**: Extended Npgsql EF Core connection resiliency to 10 retries with up to 30-second backoff and configured TCP keepalives (`Keepalive=15`) and increased connection timeouts (`Timeout=30`, `CommandTimeout=60`), preventing database dropouts and workflow aborts during 15–45 second CloudNativePG primary failovers.
  * **High Availability Helm Chart Primitives**:
    * Created `api-pdb.yaml` with a `PodDisruptionBudget` (`minAvailable: 1`) protecting ControlPlane API pods.
    * Increased default `api.replicaCount` to 2 and enabled default `podAntiAffinity` on `kubernetes.io/hostname`.
    * Injected `KUBERNETES_NODE_NAME` via Downward API into `api-deployment.yaml`.
  * **Go Compute Node Agent Multi-Hub Fallback**:
    * Added `CONTROLPLANE_STANDBY_HUB_URL` / `-standby-hub-url` flag and configuration support in `src/agent/internal/config`.
    * Implemented automatic failover in the agent WebSocket loop to connect to the Standby runner when the in-cluster hub is unreachable during full-cluster maintenance.

---

## v1.6.0

### Added
* **Private Helm Repositories & OCI Registry Authentication**:
  * **Private OCI Registry Support (`oci://...`)**: Added first-class support for installing and upgrading Helm charts from private OCI registries (such as GitHub Container Registry `ghcr.io`, Docker Hub, Harbor, Quay, AWS ECR, and Azure ACR). Dynamically synthesizes an isolated temporary Docker config (`--registry-config`) with restricted permissions and cleans it up after execution.
  * **Classic HTTP/HTTPS Repository Authentication**: Added `--username`, `--password`, and `--pass-credentials` flags when installing from authenticated HTTP/HTTPS Helm repositories (Nexus, Artifactory, ChartMuseum).
  * **Kubernetes Secret Resolution**: Seamlessly resolves credentials from existing cluster secrets (`kubernetes.io/dockerconfigjson`, `kubernetes.io/dockercfg`, or `Opaque` credentials) so existing `imagePullSecrets` can be linked directly to Helm chart deployments.
  * **Registry Credential Store (`IRegistryCredentialStore`)**: Introduced a thread-safe registry credential store that caches credentials, discovers host Docker/Helm configurations (`~/.config/helm/registry/config.json`, `~/.docker/config.json`), and authenticates bearer token exchanges (e.g. against `https://ghcr.io/token`) during background chart version and update discovery.
  * **MCP Operational Tooling**: Enriched the `install_helm_chart` tool with optional `registryUsername`, `registryPassword`, and `registrySecretName` parameters for autonomous AI pair programming.
  * **Interactive Frontend UI**: Added an expandable **Private Repository & Registry Authentication** card in `<InstallHelmModal />` with one-click selection between Public, Username & Token (with masked input and GHCR PAT guidance), and existing Cluster Secret dropdown selection.
  * **Security & Credential Sanitization**: Enforced strict masking (`********`) of sensitive tokens and passwords across all process outputs, log streams, and error diagnostics.

---

## v1.5.2

### Fixed
* **Kubernetes Custom Resource Definitions (CRDs) Discovery & Management**:
  * **Alphabetical CRD Truncation**: Resolved an issue in `KubernetesAdapter.ListAllWorkloadsAsync` where custom resources beyond the first 25 alphabetically sorted CRDs (such as Opstree Redis operator CRDs under `redis.redis.opstreelabs.in`) were omitted from "All Resources" and CRD view filtering. Expanded CRD discovery up to 250 CRDs with 10 concurrent query workers and support for both namespaced and cluster-scoped custom objects.
  * **Custom Resource Conflict Resolution & Dry-Run**: Fixed `KubernetesAdapter.ApplyManifestYamlAsync` to handle existing custom resources during create/apply. Instead of failing with unhandled 409/422 conflicts when updating without a specified resourceVersion, it resolves existing objects, injects current `metadata.resourceVersion`, strips stale `status`, and applies updates via native `MergePatch` with fallback to `Replace`.
  * **Accurate API Version & Group Resolution**: Enhanced manifest parsing in `ApplyManifestYamlAsync` to parse `apiVersion` (group/version) directly from YAML documents, correctly routing custom resource manifests to their intended API versions (e.g. `v1beta2` vs `v1beta1`).
  * **Clean YAML Export for CRDs**: Enhanced `KubernetesAdapter.GetResourceYamlAsync` to strip cluttering server-managed metadata (`managedFields`, `resourceVersion`, `uid`, `generation`, `creationTimestamp`, and `status`) when exporting custom resource YAML, allowing seamless in-place editing.
  * **Dry-Run & Error Transparency**: Updated both the backend result messaging and `<CreateResourceModal />` to surface detailed server warnings and ensure dry-run/apply results report failure when 0 resources are processed, preventing false success indications.

---

## v1.5.1

### Fixed
* **Out-of-Band BMC (iDRAC / Redfish) Inventory & Host Resolution**:
  * **Network BMC Resolver**: Resolved an issue in `IdracClientFactory.ResolveByHostIdAsync` where instances configured with `ConnectionMode = "network"` were ignored when linking hosts to their out-of-band Redfish controllers. Added fallback to single configured BMC instances.
  * **Physical vs Virtual Disk Priority**: Updated `HostHardwareService.GetHardwareInventoryAsync` so that when an out-of-band BMC (iDRAC / Redfish) returns true physical disks, it detects virtual RAID controller LUNs (such as PERC H710P/H730P or MegaRAID volumes), surfaces the controller in **Storage Controllers & RAID**, and populates **Physical Disks & SMART** with the underlying physical drives.
  * **Dell Bay Slot Parsing**: Enhanced `RedfishClient.GetDrivesAsync` with regex fallback to extract drive bay numbers (`Bay 0`, `Bay 1`) from Dell drive identifiers (`Disk.Bay.N:...`).
  * **Live BMC Vitals on Host Overview**: Updated `HostService.GetHostVitalsAsync` to resolve network BMC telemetry (live power draw, ambient/CPU thermals) without requiring redundant manual IP linkage.

---

## v1.5.0

### Added
* **OPNsense Adapter Enhancements & Security Center**:
  * **Interface Parsing & Zone Inspection**: Resolved empty interface tables by implementing defensive JSON parsing in `OPNsenseClient` that handles numeric metadata tokens, evaluates booleans dynamically, and falls back to `/api/diagnostics/interface/getInterfaceConfig`. Surfaces all active zones (`[CAMERA]`, `[GUEST]`, `[IOT]`, `[KUBERNETES]`, `[LAN]`, `[SERVERS]`, `[TRUSTED]`, `[WAN]`, `[WAN2]`) in `<OPNsenseInterfacesTable />` with search filtering and one-click MAC copy.
  * **Firmware Updates & On-Demand Checking**: Added on-demand update trigger (`POST /api/adapters/opnsense/{instanceId}/firmware/check`) and `<OPNsenseFirmwareBanner />` with upgrade package counts, reboot required warnings, and direct WebGUI updater deep-links.
  * **HAProxy & ACME Telemetry**: Deep inspection of HAProxy frontends, backends, sessions, and server UP/DOWN health (`os-haproxy`) and Let's Encrypt SSL/TLS certificate expiry countdowns (`os-acme-client`) with graceful plugin fallback notices.
  * **Firewall States & Suricata IDS Alerts**: Active Packet Filter (PF) live states table utilization, filter rule counts, network aliases, and real-time Suricata IDS alert feed with alert dismissal, UI-only threat signature ignoring, and an Ignored Rules management dialog.
  * **Hardware Vitals & Live ARP Discovery**: Live CPU load averages (1m / 5m / 15m), Memory gauge, Disk gauge, thermal sensors, and ARP neighbor table with one-click node adoption.
  * **MCP Operational Tools**: Enriched `get_op_nsense_status` and added `check_opnsense_updates` tool for AI pair programmers.
* **Hardware Inventory, Monitoring & Wear-Out Telemetry**:
  * **Comprehensive Storage Telemetry**: Physical disk inventory tracking media types (NVMe, SATA, SAS), SMART health status, wear-out percentages, and operating temperatures.
  * **RAID Controllers & PSUs**: In-band and out-of-band telemetry for storage controllers (battery backup unit health, firmware) and redundant PSUs with input wattage and line status.
  * **Hardware Threshold Alerts**: Configurable temperature and wear-out threshold indicators in hardware inventory and settings.
* **Demo Mode & Standalone Presentation Architecture**:
  * Added demo mode adapters across all infrastructure providers (Proxmox, Kubernetes, UniFi, OPNsense, Redfish, iDRAC, Helm, Home Assistant) using scrubbed RFC 5737 documentation subnets and generic homelab domains.
  * Added one-click demo state reset modal.

---

## v1.4.2

### Added
* **Multi-Homed Host & Multi-IP Adoption Support**:
  * Enhanced Proxmox QEMU/LXC adapter with `TryGetGuestIpAddressesAsync` to collect all active IPv4 interfaces from guest agents.
  * Added `AllIpAddresses` and `AgentInstalled` properties to discovery candidates and `DetectedIps` to `HostResponse`.
  * Added multi-IP dropdown selector with manual IP override in both `<AdoptNodeModal />` and `<ImportCandidateModal />`, allowing one-click selection of alternative network interfaces (e.g. VLAN 20, 30, 70, 80) or custom IP input for multi-homed nodes like `nvr-host`.
  * Multi-IP interface badge in DiscoveryView desktop and mobile candidate cards showing additional detected IP count.

### Fixed
* **Discovery Candidate State & Adoption Blockers**:
  * Resolved false-positive "Managed" status on discovered candidates where a host record existed in inventory but the background agent daemon was not yet deployed (`!candidate.agentInstalled`).
  * Added distinct "Pending Agent" badge and direct "Adopt Agent" workflow button in `<DiscoveryView />` (desktop and mobile) for unadopted nodes.
  * Re-enabled checkbox selection on "Pending Agent" candidates to allow batch adoption.
  * Fixed conflict rejection in `ImportCandidateAsync` so importing existing unmanaged or partially-adopted candidates safely updates their network IP and adapter metadata without failing.
* **Hub URL Resolution & Local Dev Preservation**:
  * Fixed `ResolveHubUrl` and `ResolveLanAddress` in `NodeAdoptionService` to prevent stripping the `/agent-hub` path from WebSocket connection strings.
  * Added automatic LAN address resolution for remote target nodes when the UI requested WebSocket URL points to `localhost` or `127.0.0.1`, while strictly preserving loopback routing for local development and automated testing environments.

---

## v1.4.1

### Fixed
* **Remote Agent Self-Update & Reverse Proxy URL Protocol**:
  * Fixed an issue where clicking the agent update button in the web UI failed to update remote compute node agents behind reverse proxies or ingress controllers (e.g. Traefik, NGINX).
  * Configured `ForwardedHeadersOptions` with `KnownIPNetworks.Clear()` and `KnownProxies.Clear()` in ASP.NET Core pipelines (`Program.cs` and `ServeCommand.cs`) to properly respect `X-Forwarded-Proto` and `X-Forwarded-Host` from ingress controllers in containerized environments.
  * Corrected download URL synthesis across `AgentHubMiddleware`, `MassAgentUpdateService`, and `AgentManagementEndpoints` to automatically detect HTTPS reverse proxy termination and normalize WebSocket schemes (`wss://` $\to$ `https://`, `ws://` $\to$ `http://`), preventing agents from receiving insecure `http://` URLs that redirected or failed TLS validation.
  * Added agent protocol tracking and acknowledgment for `UPDATE_COMMENCING` and `UPDATE_APPLIED` status messages during in-band binary upgrades.
  * Enhanced Go compute node agent with optional insecure TLS skip (`--insecure`) during binary downloads for homelabs operating with custom or self-signed internal certificates.
  * Hardened AES encryption master key provider fallback to use `/tmp` when `.controlplane` user directory is read-only.
  * Added comprehensive unit tests in `MassAgentUpdateTests` verifying forwarded headers, scheme normalization, and secure download URL generation.

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
