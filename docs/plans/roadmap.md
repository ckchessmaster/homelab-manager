# ControlPlane Master Implementation Roadmap

This document defines the sequential implementation plan for the **Homelab Orchestration & Management Plane (ControlPlane)**, decomposed from the [Technical Design Document](file:///home/ckingdon/projects/homelab-manager/docs/initial-overview.md).

---

## 🧭 Milestone & Plan Dependency Graph

```
Milestone 1: Foundation, Data Layer & Host Inventory (MVP Core)
├── [01-solution-scaffolding.md]
│   └── [02-data-layer-and-storage.md]
│       ├── [03-dev-auth-and-api-key.md]
│       │   └── [04-host-inventory-api.md]
│       │       └── [05-frontend-inventory-dashboard.md]
│
Milestone 2: Agent Architecture & Real-Time Console
├── [06-compute-node-agent.md] (Go daemon)
│   └── [07-websocket-agent-hub.md]
│       ├── [08-realtime-terminal-pipeline.md]
│       └── [09-one-click-agent-adoption.md]
│
Milestone 3: Update Orchestration Engine & Standby Runner
├── [10-dag-orchestration-engine.md]
│   ├── [11-proxmox-snapshot-and-rollback.md]
│   ├── [12-deterministic-reboot-handler.md]
│   └── [13-standby-cli-and-lease-protocol.md]
│
Milestone 4: Out-of-Band Integrations & Zitadel OIDC
├── [14-hardware-and-network-adapters.md] (iDRAC & UniFi)
├── [15-kubernetes-drain-adapter.md]
└── [16-zitadel-oidc-production-auth.md]
```

---

## 📊 Plan Execution Status

| Phase / Plan | Target Milestone | Description | Status |
| :--- | :--- | :--- | :--- |
| **[Plan 01](file:///home/ckingdon/projects/homelab-manager/docs/plans/01-solution-scaffolding.md)** | Milestone 1 | .NET 10 Aspire host, BFF API, and React 19 SPA scaffolding | ⏳ Not Started |
| **[Plan 02](file:///home/ckingdon/projects/homelab-manager/docs/plans/02-data-layer-and-storage.md)** | Milestone 1 | EF Core DbContext, entity models, PostgreSQL/SQLite switching | ⏳ Not Started |
| **[Plan 03](file:///home/ckingdon/projects/homelab-manager/docs/plans/03-dev-auth-and-api-key.md)** | Milestone 1 | API key auth handler (`X-ControlPlane-Key`) & dev bypass | ⏳ Not Started |
| **[Plan 04](file:///home/ckingdon/projects/homelab-manager/docs/plans/04-host-inventory-api.md)** | Milestone 1 | Host CRUD endpoints and Proxmox connection probe | ⏳ Not Started |
| **[Plan 05](file:///home/ckingdon/projects/homelab-manager/docs/plans/05-frontend-inventory-dashboard.md)** | Milestone 1 | React 19 UI, TanStack Query, host table, manual host modal | ⏳ Not Started |
| **[Plan 06](file:///home/ckingdon/projects/homelab-manager/docs/plans/06-compute-node-agent.md)** | Milestone 2 | Go static agent daemon, system metrics, package inspection | ⏳ Not Started |
| **[Plan 07](file:///home/ckingdon/projects/homelab-manager/docs/plans/07-websocket-agent-hub.md)** | Milestone 2 | ASP.NET Core WebSocket hub, heartbeat ingestion, liveness | ⏳ Not Started |
| **[Plan 08](file:///home/ckingdon/projects/homelab-manager/docs/plans/08-realtime-terminal-pipeline.md)** | Milestone 2 | Monotonic stdout/stderr framing, SignalR, xterm.js UI | ⏳ Not Started |
| **[Plan 09](file:///home/ckingdon/projects/homelab-manager/docs/plans/09-one-click-agent-adoption.md)** | Milestone 2 | SSH bootstrapper, arch detection, systemd provisioning, UI modal | ⏳ Not Started |
| **[Plan 10](file:///home/ckingdon/projects/homelab-manager/docs/plans/10-dag-orchestration-engine.md)** | Milestone 3 | Durable DAG state machine, step transitions, pre-flight checks | ⏳ Not Started |
| **[Plan 11](file:///home/ckingdon/projects/homelab-manager/docs/plans/11-proxmox-snapshot-and-rollback.md)** | Milestone 3 | Proxmox REST API snapshot trigger and automated rollback | ⏳ Not Started |
| **[Plan 12](file:///home/ckingdon/projects/homelab-manager/docs/plans/12-deterministic-reboot-handler.md)** | Milestone 3 | Reboot coordination protocol, reconnection, health probe | ⏳ Not Started |
| **[Plan 13](file:///home/ckingdon/projects/homelab-manager/docs/plans/13-standby-cli-and-lease-protocol.md)** | Milestone 3 | Single-binary CLI, embedded wwwroot, SQLite sync, lease protocol | ⏳ Not Started |
| **[Plan 14](file:///home/ckingdon/projects/homelab-manager/docs/plans/14-hardware-and-network-adapters.md)** | Milestone 4 | Dell iDRAC / Redfish REST & Ubiquiti UniFi PoE control | ⏳ Not Started |
| **[Plan 15](file:///home/ckingdon/projects/homelab-manager/docs/plans/15-kubernetes-drain-adapter.md)** | Milestone 4 | Kubernetes Core API cordon, drain (PDB eviction), uncordon | ⏳ Not Started |
| **[Plan 16](file:///home/ckingdon/projects/homelab-manager/docs/plans/16-zitadel-oidc-production-auth.md)** | Milestone 4 | Zitadel IDP, PKCE code flow in SPA, JWT Bearer in API | ⏳ Not Started |

---

## 🛠️ Iteration Process

When selecting a plan for execution:
1. Open the target plan file in `docs/plans/`.
2. Ensure its prerequisites are satisfied.
3. Follow the target file edits, execution steps, and verification instructions.
4. Verify all acceptance criteria.
5. Update the plan file status and the table above from `⏳ Not Started` to `✅ Completed`.
