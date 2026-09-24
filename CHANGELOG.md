# Changelog

All notable changes to the **ControlPlane** (Homelab Orchestration & Management Plane) project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## v1.2.6

### Fixed
* **Frontend Content Security Policy (CSP) OIDC Scoping**: Dynamically scoped `connect-src` and `frame-src` in the frontend Nginx reverse proxy to include `.Values.zitadel.authority`, allowing the React SPA to query OIDC metadata (`/.well-known/openid-configuration`), fetch JWKS, exchange authorization codes, and perform silent token renewal without violating CSP.
* **Granular CSP Domain Configuration**: Added `frontend.csp.extraConnectSrc` and `frontend.csp.extraFrameSrc` options in `values.yaml` and `frontend-configmap.yaml` to allow administrators to whitelist additional specific domains while maintaining strict least-privilege security without wildcards.
* **Standalone Container CSP Entrypoint Hook**: Added `/docker-entrypoint.d/40-configure-csp.sh` to dynamically configure Nginx CSP headers from environment variables (`ZITADEL_AUTHORITY`, `CSP_CONNECT_SRC`, `CSP_FRAME_SRC`) in standalone Docker and Docker Compose deployments.

---

## v1.2.5

### Fixed
* **ASP.NET Core Environment Configuration Mapping**: Supported both hierarchical colon-delimited (`Database:Host`) and double-underscore (`Database__Host`) configuration keys, ensuring environment variables injected via Kubernetes `ConfigMap` and `Secret` are correctly bound.
* **Database Secret Sanitization**: Updated `charts/controlplane/templates/secret.yaml` to store database password under `Database__Password` instead of generating a hardcoded `ConnectionStrings__ControlPlaneDatabase`, eliminating stale default host references.
* **Migration Target Diagnostics**: Enhanced `InitializeDatabaseAsync` startup logs to explicitly output the resolved target PostgreSQL host and database name (`DataSource/Database`).

---

## v1.2.4

### Fixed
* **Database Connection Configuration**: Made explicit `Database__Host`, `Database__Database`, `Database__Username`, and `Database__Port` always take precedence over stale connection strings (e.g. from chart defaults or prior installs) in `DependencyInjection.cs`.
* **CloudNativePG Secret Resolution**: Supported external password secrets (e.g. `passwordSecretName`) mounted by CloudNativePG, checking keys `password`, `PASSWORD`, `DB_PASSWORD`, and preventing namespace-local short URI hostnames from breaking cross-namespace DNS.
* **Helm Deployment Rolling Updates**: Added dynamic checksum annotations (`checksum/config` and `checksum/secret`) to `api-deployment.yaml` and `frontend-deployment.yaml` so `helm upgrade` triggers a rolling restart when configuration values change.
* **NetworkPolicy In-Cluster & Egress Rules**: Permitted cross-namespace in-cluster egress (PostgreSQL in `cnpg-services`, Temporal in `temporal`, Kubernetes API) and outbound HTTPS/HTTP (port 443 for Zitadel IdP and OCI registries) in `networkpolicy.yaml`.
* **Temporal Server URL Default**: Defaulted `temporal.serverUrl` to empty string in `values.yaml` so custom `temporal.address` values populate both `Temporal__Address` and `Temporal__ServerUrl`.

---

## v1.2.3

### Added
* **Dynamic Frontend OIDC Bootstrap**: Added unauthenticated `GET /api/v1/auth/config` endpoint in the API to serve active auth mode, Zitadel authority, client ID, and role mappings to the React SPA at runtime, eliminating hardcoded build-time URLs.
* **Configurable Custom Role Mapping**: Supported mapping custom IdP roles, groups, or claims (e.g. `homelab-admins`, `devops`, `family`) to ControlPlane canonical roles (`Admin`, `Operator`, `Viewer`) in both backend JWT claims transformation and frontend permissions evaluation.
* **Helm Role Configuration**: Added `zitadel.clientId` and `zitadel.roles` (with `admin`, `operator`, and `viewer` lists) to `values.yaml` and mapped them into `configmap.yaml`.
* **Dynamic Nginx Reverse Proxy Template**: Added `frontend-configmap.yaml` Helm template that dynamically sets Nginx's `upstream api_upstream` to match the exact release-specific API service name (`{{ include "controlplane.fullname" . }}-api`), preventing upstream resolution crashes regardless of the Helm release name.
* **Identity Provider Display**: Displayed active Zitadel authority URL on the login page when OIDC mode is active.

### Fixed
* Fixed frontend Nginx crash (`host not found in upstream "controlplane-api:8080"`) when installing the Helm chart with custom release names (e.g. `homelab-manager`).
* Added runtime configuration fallbacks (`localStorage` and `window.__CONTROLPLANE_CONFIG__`) for local development and offline environments.

---

## v1.2.1

### Fixed
* Fixed image repository references in default `values.yaml` to point to `ghcr.io/ckchessmaster/controlplane-api` and `ghcr.io/ckchessmaster/controlplane-frontend`.
* Added native OCI registry support in `HelmClient` and `InstallHelmModal` without passing the unsupported `--repo` flag.

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
