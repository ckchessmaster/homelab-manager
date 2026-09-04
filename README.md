# Homelab Orchestration & Management Plane (ControlPlane)

ControlPlane is an enterprise-grade, resilient homelab lifecycle and orchestration plane engineered to solve the acute challenges of managing heterogeneous bare-metal servers (Dell PowerEdge with iDRAC), virtual hypervisors (Proxmox VE), container platforms (Kubernetes), and diverse operating systems (Debian/Ubuntu, RHEL/Rocky, Windows).

ControlPlane replaces brittle push-and-wait configuration management (e.g. Ansible) with an asynchronous Directed Acyclic Graph (DAG) state machine, real-time terminal streaming over SignalR, pre-flight hypervisor snapshots, deterministic reboot tracking, and an autonomous zero-dependency Standby CLI runner capable of taking over orchestration while Kubernetes itself is undergoing maintenance.

---

## 🛠️ Architecture & Technology Stack

* **Orchestrator Backend & BFF:** .NET 10 (C#) orchestrated with **.NET Aspire** (latest version).
* **Frontend SPA:** React 19, TypeScript, Vite, Tailwind CSS, shadcn/ui, TanStack Query, and `xterm.js`.
* **Compute Node Agent:** Static single-binary daemon in **Go** (<15MB, outbound WebSocket).
* **Storage Layer:** Dual-provider Entity Framework Core (PostgreSQL 16+ in-cluster, local SQLite 3 for standby workstation execution).
* **Appliances (Agentless):** Proxmox VE REST API, Dell iDRAC / Redfish REST, Ubiquiti UniFi Controller API, and Kubernetes Eviction/Drain APIs.

---

## 📋 Prerequisites & Setup

### Environment Requirements

1. **.NET SDK:**
   * **Target:** **.NET 10 SDK** (latest preview/release).
   * **.NET Aspire:** Latest Aspire workload / packages.
   * > [!IMPORTANT]
     > Aspire evolves rapidly. When implementing or modifying Aspire components, implementation agents must consult the latest Aspire documentation or release notes for current hosting APIs and configuration patterns.
   * *WSL/Ubuntu Installation Options:*
     ```bash
     sudo snap install dotnet --classic # or install via Microsoft package repository
     ```
2. **Node.js & Package Manager:**
   * Node.js v22.x+ (`node -v`)
   * npm v10.x+ (`npm -v`)
3. **Go (Agent Daemon):**
   * Go 1.22+ (for building the static compute node agent binary for `linux/amd64` and `linux/arm64`)
4. **Container Engine (Optional / Aspire Dev):**
   * Docker or Podman for local Aspire resource orchestration (PostgreSQL container, etc.)

---

## 🗺️ Project Documentation & Roadmap

* **[Technical Design Document (Initial Overview)](file:///home/ckingdon/projects/homelab-manager/docs/initial-overview.md)**: Full architectural blueprint, state machine sequence, lease protocol, and database schema.
* **[Master Implementation Roadmap](file:///home/ckingdon/projects/homelab-manager/docs/plans/roadmap.md)**: Sequential breakdown across all 4 project milestones, containing links to all 16 iterative plan files.
* **[Agent & Development Guidelines (AGENTS.md)](file:///home/ckingdon/projects/homelab-manager/AGENTS.md)**: Repository guidelines, standards, and workflow instructions for Antigravity pair programming.

---

## 📂 Repository Layout

```
homelab-manager/
├── .agents/                    # Antigravity domain rules and customizations
│   └── rules/                  # Specialized rules (architecture, dotnet, react, go)
├── docs/                       # Architectural design docs and roadmap
│   ├── initial-overview.md     # Foundational technical design document
│   └── plans/                  # 16 sequential plan files and roadmap.md
├── src/                        # Source code (scaffolded in Phase 1)
│   ├── Aspire/                 # AppHost and ServiceDefaults
│   ├── ControlPlane.Api/       # ASP.NET Core BFF API
│   ├── ControlPlane.Cli/       # Standby Workstation Runner single-binary CLI
│   ├── frontend/               # React 19 + TypeScript SPA
│   └── agent/                  # Go compute node daemon
├── AGENTS.md                   # Antigravity core repository guide
└── README.md                   # Project overview & developer setup
```