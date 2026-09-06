# ControlPlane Implementation Roadmap: Phase 3

This document defines the sequential implementation plan for **Phase 3** of the **Homelab Orchestration & Management Plane (ControlPlane)**, focusing on **Temporal Durable Execution & Node-Based Visual Workflow Redesign**.

*(Historical plans are preserved in [docs/plans/archive/phase1-mvp/](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase1-mvp/) and [docs/plans/archive/phase2/](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase2/)).*

---

## 🧭 Phase 3 Milestone & Plan Dependency Graph

```
Phase 3: Temporal Durable Execution & Node-Based Visual Workflow Redesign
├── [01-database-reorganization-and-schema-isolation.md]
│   └── App-specific PostgreSQL DB & schema isolation (controlplane) + SQLite standby separation
│
├── [02-temporal-aspire-orchestration-and-sdk.md]
│   └── Aspire hosting with temporalio/dev-server (UI enabled), Temporal DB, and Temporalio .NET SDK setup
│
├── [03-temporal-activities-and-durable-sagas.md]
│   └── Convert steps to Activities, implement HostUpgradeWorkflow with Sagas & Reboot Approval Signals
│
├── [04-node-based-workflow-canvas-ui.md]
│   └── React 19 @xyflow/react visual DAG canvas with live status pulsing, timers & approval action gates
│
├── [05-parameterized-workflow-launcher.md]
│   └── Slide-over workflow launcher, parameter builder (snapshot, reboot policy, health probes), and 1-click trigger
│
├── [06-multi-node-fleet-rolling-orchestration.md]
│   └── Multi-host rolling cluster upgrade workflow (cordon ➔ drain ➔ upgrade ➔ reboot ➔ verify ➔ uncordon)
│
└── [07-standby-temporal-cli-integration.md]
    └── Standby CLI runner with embedded Temporal SQLite dev server for 100% workflow parity offline
```

---

## 📊 Phase 3 Plan Execution Status

| Phase / Plan | Description | Status |
| :--- | :--- | :--- |
| **[Plan 01](file:///home/ckingdon/projects/homelab-manager/docs/plans/01-database-reorganization-and-schema-isolation.md)** | Database Reorganization & Schema Isolation (`controlplane` DB & schema) | ✅ Completed |
| **[Plan 02](file:///home/ckingdon/projects/homelab-manager/docs/plans/02-temporal-aspire-orchestration-and-sdk.md)** | Temporal Aspire Hosting (`temporalio/dev-server`) & .NET SDK Setup | ✅ Completed |
| **[Plan 03](file:///home/ckingdon/projects/homelab-manager/docs/plans/03-temporal-activities-and-durable-sagas.md)** | Temporal Activities, Durable Sagas & Human-in-the-Loop Approval Gates | ✅ Completed |
| **[Plan 04](file:///home/ckingdon/projects/homelab-manager/docs/plans/04-node-based-workflow-canvas-ui.md)** | Node-Based Visual Workflow Canvas UI (`@xyflow/react`) | ✅ Completed |
| **[Plan 05](file:///home/ckingdon/projects/homelab-manager/docs/plans/05-parameterized-workflow-launcher.md)** | Parameterized Workflow Launcher & Real-Time Graph Preview | ✅ Completed |
| **[Plan 06](file:///home/ckingdon/projects/homelab-manager/docs/plans/06-multi-node-fleet-rolling-orchestration.md)** | Multi-Node Fleet Rolling Orchestrator & Batch Workflows | ⏳ Not Started |
| **[Plan 07](file:///home/ckingdon/projects/homelab-manager/docs/plans/07-standby-temporal-cli-integration.md)** | Standby Mode Temporal CLI Integration (Embedded SQLite Dev Server) | ⏳ Not Started |

---

## 🛠️ Iteration Process

When executing:
1. Open the target plan file in `docs/plans/`.
2. Follow the target file edits, execution steps, and verification instructions.
3. Verify all acceptance criteria and automated tests.
4. Update the plan file status and the table above to `✅ Completed`.
