# Antigravity Agent Guidelines: Homelab Orchestration & Management Plane (ControlPlane)

Welcome to the **ControlPlane** repository. This document serves as the foundational guide for Antigravity AI pair programmers and automated coding agents operating in this codebase.

---

## 1. System Vision & Core Architectural Invariants

ControlPlane is an asynchronous, resilient homelab orchestration platform. It is engineered to solve the "chicken-and-egg" upgrade dilemma and Ansible's push-and-wait brittleness through a hybrid agent/agentless topology, unified infrastructure adapters, and an autonomous standby takeover runner.

### Architectural Invariants (Must Never Be Broken)

1. **Dual-Topology Portability:**
   * The backend code must execute identically in **Cluster Mode** (running in Kubernetes against PostgreSQL) and **Standby Runner Mode** (running as a single-binary CLI on a workstation against local SQLite).
   * Do not write PostgreSQL-specific raw SQL queries that fail on SQLite, or vice-versa. Rely on standard EF Core LINQ queries, or use provider-aware abstractions.
2. **Outbound-Only Compute Node Agent:**
   * Managed compute nodes run a static **Go** daemon (`controlplane-agent`) that dials **outbound** over WebSocket (`wss://`) to the backend.
   * **Never** open listening TCP ports on managed compute nodes. All agent communication is outbound-initiated with automatic reconnection and heartbeat transmission.
3. **Unified Agentless Infrastructure Adapters:**
   * Hypervisors (Proxmox VE), BMCs (Dell iDRAC & DMTF Redfish), Switches (Ubiquiti UniFi), Firewalls (OPNsense), and Cluster Orchestrators (Kubernetes) are managed strictly **agentless** via modular adapters.
   * Credentials (API tokens, passwords, secrets, kubeconfigs) must be stored encrypted with `AES-256-GCM` via `ISecretEncryptionService` / `ISecurityKeyProvider` and masked in all API/MCP responses (`HasSecret: true`).
   * Appliance HTTP clients must be pooled via `IHttpClientFactory` and configured with `DangerousAcceptAnyServerCertificateValidator` to gracefully accommodate homelab self-signed certificates.
4. **Resilient State Machine (DAG):**
   * Update jobs must follow a deterministic Directed Acyclic Graph (DAG) state machine: `Pending` -> `Running` -> `Verifying` -> `Completed` or `RolledBack`.
   * Real-time console logs must be framed with monotonic sequence numbers `(job_id, sequence_id, stream_type, log_line, timestamp)` and streamed over SignalR directly to `xterm.js`.
5. **No Direct Database Coupling for Standby Mode:**
   * When taking over during maintenance, the Standby CLI receives a JSON export payload from the cluster, seeds its local SQLite database, acquires a lease (`GLOBAL_MAINTENANCE_LOCK`), and pushes deltas back upon cluster recovery. The Standby runner does not connect directly to the in-cluster PostgreSQL instance during node downtime.
6. **Multi-Cluster Kubernetes Workloads Engine:**
   * Kubernetes applications and workloads (Deployments, StatefulSets, DaemonSets, Pods) are aggregated across clusters and namespaces through `IWorkloadService`.
   * Rollout restarts and scaling actions must respect role-based permissions (`Operator` or `Admin`).
7. **Secure Embedded Model Context Protocol (MCP) Server:**
   * The backend exposes a built-in MCP server (`/mcp`) enabling AI agents to query inventory, stream console logs, inspect adapter telemetry, and execute operational workflows.
   * Destructive actions (reboots, PoE power cycles, BMC resets) initiated via MCP must set `InitiatedBy = "AI Agent via MCP"` for comprehensive auditability.

---

## 2. Technology Stack Standards

| Component | Technology | Version / Guidelines |
| :--- | :--- | :--- |
| **Backend & BFF** | .NET (C#) & ASP.NET Core | **.NET 10** SDK; file-scoped namespaces; nullable reference types enabled. |
| **Orchestration** | .NET Aspire | **Latest Aspire workload & packages**. AppHost orchestrates API, PostgreSQL, Frontend, and Temporal. |
| **Data Access** | Entity Framework Core | Dual-provider: `Npgsql.EntityFrameworkCore.PostgreSQL` and `Microsoft.EntityFrameworkCore.Sqlite`. |
| **Frontend** | React, TypeScript, Vite | **React 19**, TypeScript 5.x, Vite, Tailwind CSS, shadcn/ui, TanStack Query. |
| **Terminal Canvas** | xterm.js & SignalR | ANSI color rendering, streaming stdout/stderr framing with monotonic sequence numbers. |
| **Node Agent** | Go | **Go 1.22+**, statically compiled (`CGO_ENABLED=0`), <15MB binary size, <10MB RSS. |
| **Authentication & RBAC** | Zitadel OIDC / JWT Bearer | Zitadel PKCE for Frontend; ASP.NET Core JWT Bearer validation; composite RBAC (`Viewer`, `Operator`, `Admin`); `AUTH_BYPASS=true` for local development. |
| **Infrastructure Adapters** | Proxmox, K8s, UniFi, OPNsense, Redfish/iDRAC, Home Assistant | Modular adapters in `Features/Adapters/` with unified config management (`IAdapterConfigService`). |
| **System Observability** | In-Memory Circular Buffer (2,500) | `Features/SystemLogs/` with live streaming API, multi-level filtering, and UI viewer. |
| **Packaging & CI/CD** | Docker, Helm, GitHub Actions | Multi-stage OCI Dockerfiles, Helm chart (`charts/controlplane`), Kustomize overlays (`deploy/k8s`), and GitHub Actions workflows (`.github/workflows/`). |
| **AI Integration (MCP)** | Model Context Protocol | Built-in streamable HTTP MCP server in `Features/Mcp/` registered at `/mcp` (40+ operational tools). |

---

## 3. Directory Layout & Conventions

```
homelab-manager/
├── .agents/                    # Agent customizations, rules, and skills
│   ├── rules/                  # Architecture, .NET backend, React frontend, Go agent, Adapters
│   ├── skills/                 # Specialized operational skills (controlplane, aspire, etc.)
│   └── mcp_config.json         # MCP server connection configuration
├── .github/
│   └── workflows/              # GitHub Actions CI/CD, image publishing, and security scans
├── charts/
│   └── controlplane/           # Production Kubernetes Helm chart
├── deploy/
│   ├── compose/                # Production Docker Compose stack
│   ├── k8s/                    # Base Kubernetes manifests and Kustomize environment overlays
│   └── zitadel/                # Self-hosted Zitadel OIDC bootstrap & setup scripts
├── docs/
│   ├── initial-overview.md     # Technical Design Document (system specifications)
│   ├── mcp-server.md           # Model Context Protocol server documentation
│   └── plans/                  # Master roadmap & archived implementation plans (Phases 1-5)
├── src/
│   ├── Aspire/
│   │   ├── ControlPlane.AppHost/           # Aspire distributed application orchestrator
│   │   └── ControlPlane.ServiceDefaults/   # Telemetry, health checks, resilience defaults
│   ├── ControlPlane.Api/                   # ASP.NET Core BFF, Adapters, MCP & Orchestration API
│   │   └── Features/
│   │       ├── Adapters/                   # Modular adapters: Proxmox, K8s, UniFi, OPNsense, Redfish, HomeAssistant
│   │       ├── Workloads/                  # Multi-cluster Kubernetes application engine
│   │       ├── Helm/                       # Helm catalog and release management
│   │       ├── Mcp/                        # Built-in Model Context Protocol server tools (40+)
│   │       ├── Security/                   # Zitadel JWT, RBAC, PAT tokens, and AES-256-GCM encryption
│   │       ├── SystemLogs/                 # Circular log buffer, query API, and diagnostics
│   │       ├── Agents/                     # WebSocket agent hub, binary management, and auto-sync
│   │       ├── Discovery/                  # Dynamic candidate discovery across hypervisors/firewalls
│   │       ├── Hosts/                      # Host inventory & adoption service
│   │       └── Orchestration/              # DAG state machine, pipelines, and Temporal workflows
│   ├── ControlPlane.Cli/                   # Standby single-binary CLI runner with SQLite
│   ├── frontend/                           # React 19 SPA (Vite + Tailwind CSS + shadcn/ui)
│   └── agent/                              # Go compute node agent daemon (cross-platform)
├── tests/
│   ├── ControlPlane.Api.Tests/             # Comprehensive xUnit test suite (330+ tests)
│   └── ControlPlane.Cli.Tests/             # CLI and standby runner unit tests
├── AGENTS.md                   # This master manifest
├── CHANGELOG.md                # Release notes and version history
└── README.md                   # Developer documentation & prerequisites
```

---

## 4. Coding & Implementation Guidelines

### C# / .NET 10 Standards
* **Null Safety:** Enable `<Nullable>enable</Nullable>` on all C# projects. Treat warnings as errors where possible.
* **Asynchronous Programming:** Always accept and forward `CancellationToken cancellationToken = default` on async methods (EF queries, HTTP requests, stream reading).
* **Dependency Injection:** Register services with appropriate lifecycles (`Scoped` for DbContext, client factories, and `ControlPlaneMcpTools`; `Singleton` for stateless catalogs, background hubs, and encryption providers).
* **Adapter Architecture:** Implement modular adapters behind interfaces (e.g. `IUniFiClientFactory`, `IKubernetesClientFactory`). Store secrets encrypted and return masked values in DTOs.
* **Logging & Telemetry:** Use `ILogger<T>` structured logging with high-performance semantic message templates.
* **Namespace Discipline:** Avoid generic sub-namespaces like `ControlPlane.Api.Features.System` which shadow the BCL `System` root namespace. Always use distinct namespaces like `Features.SystemLogs`.

### Go Agent Standards
* **Portability:** Agent must compile with `CGO_ENABLED=0` for `GOOS=linux GOARCH=amd64`, `GOOS=linux GOARCH=arm64`, and `GOOS=windows GOARCH=amd64`.
* **Resource Constraint:** Keep idle memory footprint under 10MB RSS. Avoid heavy external dependencies.
* **Signal Handling:** Cleanly intercept `SIGTERM` and `SIGINT` to gracefully notify the backend before shutting down or initiating a system reboot.
* **Installer Resilience:** PowerShell service install scripts must properly escape executable and parameter paths and support optional switches (e.g. `-Insecure` for homelab self-signed certificates).

### React 19 & Frontend Standards
* **Design System Governance:** Strictly follow `/DESIGN_SYSTEM.md`. Use `zinc-950` canvas base, `zinc-900` surfaces, `zinc-800` borders, `sky-500` brand accent, and semantic-only health colors. Never use pure black (`#000000`) or card grids for lists exceeding 6 items. Use `<MetricStrip />`, `<TableToolbar />`, and `<InspectorSheet />`.
* **Visual Excellence:** The UI must look modern, sleek, and high-quality. Use Tailwind CSS with dark mode support, subtle glassmorphism, clean badge states, and responsive layouts.
* **Authentication:** Use `react-oidc-context` with Zitadel PKCE. Gate privileged UI components behind `<RequireRole role="Operator">` or `<RequireRole role="Admin">`.
* **Server State:** Use TanStack Query (`@tanstack/react-query`) for all remote data fetching, mutation, and cache invalidation.
* **Terminal Streaming:** Encapsulate `xterm.js` inside a dedicated React component with ResizeObserver, auto-scroll toggle, and ANSI color theme matching the application theme.

---

## 5. Release & Versioning Strategy

ControlPlane strictly follows **Semantic Versioning (SemVer 2.0.0)** (`MAJOR.MINOR.PATCH`):

* **MAJOR (`X.0.0`)**:
  * Breaking architectural shifts or protocol redesigns (such as breaking wire changes in the agent WebSocket hub requiring a simultaneous fleet-wide reinstall).
  * Major breaking database schema modifications without backward-compatible automatic EF Core migrations.
  * Runtime / platform upgrades with breaking changes (e.g. future .NET or React major framework migrations).
* **MINOR (`1.X.0`)**:
  * Significant new operational features, modules, or platform capabilities (e.g. adding Multi-Cluster Workloads, Helm release management, Discovery engine, or System & Settings log streaming).
  * New infrastructure adapters (e.g. Home Assistant, TrueNAS, Mikrotik).
  * Non-breaking database schema expansions (new tables, columns with defaults).
  * Additions to the embedded Model Context Protocol (MCP) tool suite.
* **PATCH (`1.3.X`)**:
  * Targeted bug fixes, edge case resolutions, and runtime stability enhancements.
  * Agent platform installer fixes and compatibility tweaks (such as Windows Service escaping or `-Insecure` SSL flags).
  * UI/UX refinements, CSS/theme adjustments, or minor performance optimizations.
  * Security patches for direct dependencies without breaking public interfaces or behavioral contracts.

When creating a release, all corresponding version references must be bumped simultaneously:
1. `charts/controlplane/Chart.yaml` (`version` and `appVersion`)
2. `src/ControlPlane.Api/ControlPlane.Api.csproj` (`<Version>`)
3. `src/ControlPlane.Cli/ControlPlane.Cli.csproj` (`<Version>`)
4. `src/ControlPlane.Api/Features/Agents/AgentBinaryService.cs` (`CurrentAgentVersion` and `DefaultAgentVersion`)
5. `src/agent/cmd/agent/main.go` (`Version`)
6. `src/frontend/package.json` (`version`)
7. `CHANGELOG.md` (new section with categorized changes)

---

## 6. AI Pair Programmer Operational Protocols

When acting as an AI pair programmer or autonomous agent with access to ControlPlane MCP tools:

1. **Observe Before Action:** Always inspect inventory (`list_hosts`, `list_adapters`, `list_workloads`) before making assumptions about infrastructure state.
2. **Safety Gates for Destructive Actions:**
   * Do not dispatch reboots or power actions without confirming the target node or workload state.
   * For compute nodes, check `get_host_details` to verify agent liveness.
   * For Kubernetes workloads, verify healthy replicas via `list_workloads` before initiating rolling restarts.
   * For out-of-band power operations, inspect temperature and fan sensors via `get_hardware_sensors` before issuing power commands.
3. **Monotonic Log Streaming:**
   * When tracking jobs, query `query_job_logs` with monotonic sequence IDs (`fromSequenceId`) to avoid duplicate log processing.
4. **Plan-Driven Workflows:** Follow the iterative planning sequence in `docs/plans/roadmap.md`. After completing any task, run `dotnet test` (all 330+ tests must pass).

