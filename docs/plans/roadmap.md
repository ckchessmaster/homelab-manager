# ControlPlane Implementation Roadmap

This document defines the master implementation roadmap and historical milestone tracking for the **Homelab Orchestration & Management Plane (ControlPlane)**.

*(Historical plans are preserved in [docs/plans/archive/phase1-mvp/](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase1-mvp/), [docs/plans/archive/phase2/](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase2/), [docs/plans/archive/phase3/](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase3/), [docs/plans/archive/phase4/](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase4/), and [docs/plans/archive/phase5/](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase5/)).*

---

## 🧭 Phase 4 Milestone & Plan Dependency Graph (Archived)

```
Phase 4: Productionization, Zitadel Identity & Deployment Architecture
├── [archive/phase4/01-production-containerization-and-compose.md]
│   └── Multi-stage OCI Dockerfiles for .NET 10 API & React 19 Frontend + Production Compose Stack
│
├── [archive/phase4/02-zitadel-identity-setup-and-backend-rbac.md]
│   └── Self-hosted Zitadel OIDC setup, ASP.NET Core JWT Bearer validation & Composite RBAC engine
│
├── [archive/phase4/03-frontend-oidc-pkce-and-user-profile.md]
│   └── React 19 OIDC PKCE integration, authenticated API interceptor, user profile menu & role UI gates
│
├── [archive/phase4/05-kubernetes-production-packaging-and-helm.md]
│   └── Complete production Helm chart (charts/controlplane) & Kustomize overlays for homelab clusters
│
└── [archive/phase4/06-cicd-github-actions-and-release-pipeline.md]
    └── GitHub Actions CI matrix, multi-arch GHCR image publishing, agent release artifacts & Helm linting
```

---

## 📊 Phase 4 Plan Execution Status (Archived)

| Phase / Plan | Description | Status |
| :--- | :--- | :--- |
| **[Plan 01](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase4/01-production-containerization-and-compose.md)** | Multi-Stage OCI Containerization & Production Compose Stack | ✅ Completed |
| **[Plan 02](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase4/02-zitadel-identity-setup-and-backend-rbac.md)** | Zitadel Identity Provider Setup & Backend JWT / RBAC Engine | ✅ Completed |
| **[Plan 03](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase4/03-frontend-oidc-pkce-and-user-profile.md)** | Frontend OIDC Authentication Flow (PKCE) & User Profile Context | ✅ Completed |
| **[Plan 05](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase4/05-kubernetes-production-packaging-and-helm.md)** | Kubernetes Production Packaging & Helm Chart (`charts/controlplane`) | ✅ Completed |
| **[Plan 06](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase4/06-cicd-github-actions-and-release-pipeline.md)** | CI/CD GitHub Actions Automation & Release Pipeline | ✅ Completed (Built Out, Triggers Paused) |

---

## 🧭 Phase 5 Milestone & Plan Dependency Graph (Archived)

```
Phase 5: Unified Infrastructure Adapters & Multi-Platform Management Plane
├── [archive/phase5/phase5-01-adapters-hub-and-multi-proxmox.md]
│   └── Retire "probes" terminology, create modular AdaptersView, multi-instance Proxmox VE configs & discovery
│
├── [archive/phase5/phase5-02-kubernetes-adapter-and-workloads.md]
│   └── Multi-cluster K8s adapter, dynamic kubeconfig, cluster health, node cordon/drain & workload engine
│
├── [archive/phase5/phase5-03-ubiquiti-unifi-adapter.md]
│   └── UniFi Network Application session auth, device inventory, firmware upgrade, reboot & switch PoE control
│
├── [archive/phase5/phase5-04-grouped-inventory-and-apps.md]
│   └── Modern segmented filter chips/grouping (no clumsy trees) & dedicated K8s Applications / Workloads view
│
└── [archive/phase5/phase5-05-opnsense-and-oob-adapters.md]
    └── OPNsense gateway/DHCP/service control & BMC iDRAC/Redfish out-of-band power and sensor telemetry
```

---

## 📊 Phase 5 Plan Execution Status (Archived)

| Phase / Plan | Description | Status |
| :--- | :--- | :--- |
| **[Plan 01](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase5/phase5-01-adapters-hub-and-multi-proxmox.md)** | Adapters Hub Refactor & Multi-Instance Proxmox VE Adapter | ✅ Completed |
| **[Plan 02](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase5/phase5-02-kubernetes-adapter-and-workloads.md)** | Multi-Cluster Kubernetes Adapter & Workload Engine | ✅ Completed |
| **[Plan 03](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase5/phase5-03-ubiquiti-unifi-adapter.md)** | Ubiquiti UniFi Network Container Adapter | ✅ Completed |
| **[Plan 04](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase5/phase5-04-grouped-inventory-and-apps.md)** | Modern Grouped Inventory & Application Workloads Hub | ✅ Completed |
| **[Plan 05](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase5/phase5-05-opnsense-and-oob-adapters.md)** | OPNsense & Out-of-Band (iDRAC/Redfish) Adapters | ✅ Completed |

---

## 🛠️ Iteration Process

When executing:
1. Open the target plan file in `docs/plans/`.
2. Follow the target file edits, execution steps, and verification instructions.
3. Verify all acceptance criteria and automated tests.
4. Update the plan file status and the table above to `✅ Completed`.
