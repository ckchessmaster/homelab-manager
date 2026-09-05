# ControlPlane Implementation Roadmap: Phase 2

This document defines the sequential implementation plan for **Phase 2** of the **Homelab Orchestration & Management Plane (ControlPlane)**, building upon the foundations in [initial-overview.md](file:///home/ckingdon/projects/homelab-manager/docs/initial-overview.md).

*(Historical Phase 1 MVP plans are preserved in [docs/plans/archive/phase1-mvp/](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase1-mvp/)).*

---

## 🧭 Phase 2 Milestone & Plan Dependency Graph

```
Phase 2: Service Discovery, Modular Pipelines & Enterprise Hardening
├── [01-service-discovery-proxmox-and-k8s.md]
│   └── Auto-discover Proxmox VMs/LXCs & Kubernetes cluster nodes with 1-click import
│
├── [02-modular-pipeline-profiles.md]
│   └── Flexible pipeline catalog, pre-built DAG profiles, and interactive workflow launcher
│
├── [03-snapshot-retention-worker.md]
│   └── Proxmox 24-hour snapshot retention background worker & pruning
│
├── [04-secrets-encryption-at-rest.md]
│   └── Application-layer AES-256-GCM envelope encryption for adapter credentials & sensitive settings
│
├── [05-standby-cli-distribution.md]
│   └── Release packaging scripts & cross-platform Standby CLI distribution
│
└── [06-zitadel-oidc-production-auth.md]
    └── Zitadel OIDC PKCE code flow in SPA & JWT Bearer RBAC in API
```

---

## 📊 Phase 2 Plan Execution Status

| Phase / Plan | Description | Status |
| :--- | :--- | :--- |
| **[Plan 01](file:///home/ckingdon/projects/homelab-manager/docs/plans/01-service-discovery-proxmox-and-k8s.md)** | Unified Service Discovery: Proxmox VMs/LXCs & Kubernetes nodes with 1-click inventory import | ✅ Completed |
| **[Plan 02](file:///home/ckingdon/projects/homelab-manager/docs/plans/02-modular-pipeline-profiles.md)** | Modular Pipeline Profiles: Selectable DAG workflows, step preview visualizer, and launch modal | ✅ Completed |
| **[Plan 03](file:///home/ckingdon/projects/homelab-manager/docs/plans/03-snapshot-retention-worker.md)** | Proxmox Snapshot 24-Hour Retention Worker & Automated Pruning | ✅ Completed |
| **[Plan 04](file:///home/ckingdon/projects/homelab-manager/docs/plans/04-secrets-encryption-at-rest.md)** | Secrets Management & AES-256-GCM Encryption at Rest for Adapter Credentials | ⏳ Not Started |
| **[Plan 05](file:///home/ckingdon/projects/homelab-manager/docs/plans/05-standby-cli-distribution.md)** | Standby CLI Release Packaging & Cross-Platform Distribution Scripts | ⏳ Not Started |
| **[Plan 06](file:///home/ckingdon/projects/homelab-manager/docs/plans/06-zitadel-oidc-production-auth.md)** | Production Zitadel OIDC Authentication & Role-Based Access Control | ⏳ Not Started |

---

## 🛠️ Iteration Process

When executing:
1. Open the target plan file in `docs/plans/`.
2. Follow the target file edits, execution steps, and verification instructions.
3. Verify all acceptance criteria and automated tests.
4. Update the plan file status and the table above to `✅ Completed`.
