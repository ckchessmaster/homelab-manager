# Antigravity Agent Guidelines: Homelab Orchestration & Management Plane (ControlPlane)

Welcome to the **ControlPlane** repository. This document serves as the foundational guide for Antigravity AI pair programmers and automated coding agents operating in this codebase.

---

## 1. System Vision & Core Architectural Invariants

ControlPlane is an asynchronous, resilient homelab orchestration platform. It is engineered to solve the "chicken-and-egg" upgrade dilemma and Ansible's push-and-wait brittleness through a hybrid agent/agentless topology and an autonomous standby takeover runner.

### Architectural Invariants (Must Never Be Broken)

1. **Dual-Topology Portability:**
   * The backend code must execute identically in **Cluster Mode** (running in Kubernetes against PostgreSQL) and **Standby Runner Mode** (running as a single-binary CLI on a workstation against local SQLite).
   * Do not write PostgreSQL-specific raw SQL queries that fail on SQLite, or vice-versa. Rely on standard EF Core LINQ queries, or use provider-aware abstractions.
2. **Outbound-Only Compute Node Agent:**
   * Managed compute nodes run a static **Go** daemon (`controlplane-agent`) that dials **outbound** over WebSocket (`wss://`) to the backend.
   * **Never** open listening TCP ports on managed compute nodes. All agent communication is outbound-initiated with automatic reconnection and heartbeat transmission.
3. **Agentless Infrastructure Adapters:**
   * Hypervisors (Proxmox VE), BMCs (Dell iDRAC), Switches (Ubiquiti UniFi), and Cluster Orchestrators (Kubernetes) are managed strictly **agentless** via their HTTPS REST/Redfish/API endpoints.
4. **Resilient State Machine (DAG):**
   * Update jobs must follow a deterministic Directed Acyclic Graph (DAG) state machine: `Pending` -> `Running` -> `Verifying` -> `Completed` or `RolledBack`.
   * Real-time console logs must be framed with monotonic sequence numbers and streamed over SignalR directly to `xterm.js`.
5. **No Direct Database Coupling for Standby Mode:**
   * When taking over during maintenance, the Standby CLI receives a JSON export payload from the cluster, seeds its local SQLite database, acquires a lease (`GLOBAL_MAINTENANCE_LOCK`), and pushes deltas back upon cluster recovery. The Standby runner does not connect directly to the in-cluster PostgreSQL instance during node downtime.

---

## 2. Technology Stack Standards

| Component | Technology | Version / Guidelines |
| :--- | :--- | :--- |
| **Backend & BFF** | .NET (C#) & ASP.NET Core | **.NET 10** SDK; file-scoped namespaces; nullable reference types enabled. |
| **Orchestration** | .NET Aspire | **Latest Aspire workload & packages**. *Note: Aspire APIs change frequently; agents must consult the latest Aspire docs when implementing hosting logic.* |
| **Data Access** | Entity Framework Core | Dual-provider: `Npgsql.EntityFrameworkCore.PostgreSQL` and `Microsoft.EntityFrameworkCore.Sqlite`. |
| **Frontend** | React, TypeScript, Vite | **React 19**, TypeScript 5.x, Vite, Tailwind CSS, shadcn/ui, TanStack Query. |
| **Terminal Canvas** | xterm.js & SignalR | ANSI color rendering, streaming stdout/stderr framing. |
| **Node Agent** | Go | **Go 1.22+**, statically compiled (`CGO_ENABLED=0`), <15MB binary size. |
| **Authentication** | Custom / Zitadel | Phase 1: `X-ControlPlane-Key` + Dev Bypass; Phase 2: Zitadel OIDC (PKCE). |

---

## 3. Directory Layout & Conventions

```
homelab-manager/
├── .agents/                    # Customization rules
│   └── rules/                  # Specialized rules (architecture, dotnet, react, go)
├── docs/
│   ├── initial-overview.md     # Technical Design Document (system specifications)
│   └── plans/                  # Sequential implementation plans
│       ├── roadmap.md          # Master roadmap & milestone progress tracker
│       ├── 01-solution-scaffolding.md
│       └── ... (02 through 16)
├── src/
│   ├── Aspire/
│   │   ├── ControlPlane.AppHost/           # Aspire distributed application orchestrator
│   │   └── ControlPlane.ServiceDefaults/   # Telemetry, health checks, resilience defaults
│   ├── ControlPlane.Api/                   # ASP.NET Core BFF & Orchestration Web API
│   ├── ControlPlane.Cli/                   # Standby single-binary CLI runner
│   ├── frontend/                           # React 19 SPA (Vite + Tailwind CSS)
│   └── agent/                              # Go compute node agent daemon
├── AGENTS.md                   # This manifest
└── README.md                   # Developer documentation & prerequisites
```

---

## 4. Plan-Driven Execution Workflow

All development on ControlPlane must follow the iterative plan sequence outlined in [docs/plans/roadmap.md](file:///home/ckingdon/projects/homelab-manager/docs/plans/roadmap.md).

### When working on a task:
1. **Locate the Plan:** Read the relevant plan file in `docs/plans/` (e.g. `01-solution-scaffolding.md`).
2. **Review Dependencies:** Verify that all prerequisite plan files are marked as completed in `docs/plans/roadmap.md`.
3. **Execute Incrementally:** Implement the specified components, files, and logic adhering strictly to the file's target structure.
4. **Verify & Test:** Run the verification commands specified in the plan file.
5. **Update Status:** Mark completed items with `[x]` in both the specific plan file and `docs/plans/roadmap.md`.

---

## 5. Coding & Implementation Guidelines

### C# / .NET 10 Standards
* **Null Safety:** Enable `<Nullable>enable</Nullable>` on all C# projects. Treat warnings as errors where possible.
* **Asynchronous Programming:** Always accept and forward `CancellationToken cancellationToken = default` on async methods (EF queries, HTTP requests, stream reading).
* **Dependency Injection:** Register services with appropriate lifecycles (`Scoped` for DbContext, `Singleton` for stateless adapters or background hubs).
* **Logging & Telemetry:** Use `ILogger<T>` structured logging with high-performance source-generated log methods or semantic message templates.

### Go Agent Standards
* **Portability:** Agent must compile with `CGO_ENABLED=0` for `GOOS=linux GOARCH=amd64` and `GOOS=linux GOARCH=arm64`.
* **Resource Constraint:** Keep idle memory footprint under 10MB RSS. Avoid heavy external dependencies.
* **Signal Handling:** Cleanly intercept `SIGTERM` and `SIGINT` to gracefully notify the backend before shutting down or initiating a system reboot.

### React 19 & Frontend Standards
* **Visual Excellence:** The UI must look modern, sleek, and high-quality. Use Tailwind CSS with dark mode support, subtle glassmorphism, clean badge states, and responsive layouts.
* **Server State:** Use TanStack Query (`@tanstack/react-query`) for all remote data fetching, mutation, and cache invalidation.
* **Terminal Streaming:** Encapsulate `xterm.js` inside a dedicated React component with ResizeObserver, auto-scroll toggle, and ANSI color theme matching the application theme.
* **No Placeholders:** Ensure all UI views, tables, and dialogs are fully interactive with realistic sample data or live API bindings.
