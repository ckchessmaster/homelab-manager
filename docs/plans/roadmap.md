# ControlPlane Implementation Roadmap: Phase 4

This document defines the sequential implementation plan for **Phase 4** of the **Homelab Orchestration & Management Plane (ControlPlane)**, focusing on **Productionization, Zitadel Identity Management & Deployment Architecture**.

*(Historical plans are preserved in [docs/plans/archive/phase1-mvp/](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase1-mvp/), [docs/plans/archive/phase2/](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase2/), and [docs/plans/archive/phase3/](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase3/)).*

---

## 🧭 Phase 4 Milestone & Plan Dependency Graph

```
Phase 4: Productionization, Zitadel Identity & Deployment Architecture
├── [01-production-containerization-and-compose.md]
│   └── Multi-stage OCI Dockerfiles for .NET 10 API & React 19 Frontend + Production Compose Stack
│
├── [02-zitadel-identity-setup-and-backend-rbac.md]
│   └── Self-hosted Zitadel OIDC setup, ASP.NET Core JWT Bearer validation & Composite RBAC engine
│
├── [03-frontend-oidc-pkce-and-user-profile.md]
│   └── React 19 OIDC PKCE integration, authenticated API interceptor, user profile menu & role UI gates
│
├── [04-agent-cross-compilation-and-systemd-installer.md]
│   └── Go agent cross-compilation matrix (amd64/arm64) & automated curl | bash systemd installer endpoint
│
├── [05-kubernetes-production-packaging-and-helm.md]
│   └── Complete production Helm chart (charts/controlplane) & Kustomize overlays for homelab clusters
│
└── [06-cicd-github-actions-and-release-pipeline.md]
    └── GitHub Actions CI matrix, multi-arch GHCR image publishing, agent release artifacts & Helm linting
```

---

## 📊 Phase 4 Plan Execution Status

| Phase / Plan | Description | Status |
| :--- | :--- | :--- |
| **[Plan 01](file:///home/ckingdon/projects/homelab-manager/docs/plans/01-production-containerization-and-compose.md)** | Multi-Stage OCI Containerization & Production Compose Stack | ⏳ Not Started |
| **[Plan 02](file:///home/ckingdon/projects/homelab-manager/docs/plans/02-zitadel-identity-setup-and-backend-rbac.md)** | Zitadel Identity Provider Setup & Backend JWT / RBAC Engine | ⏳ Not Started |
| **[Plan 03](file:///home/ckingdon/projects/homelab-manager/docs/plans/03-frontend-oidc-pkce-and-user-profile.md)** | Frontend OIDC Authentication Flow (PKCE) & User Profile Context | ⏳ Not Started |
| **[Plan 04](file:///home/ckingdon/projects/homelab-manager/docs/plans/04-agent-cross-compilation-and-systemd-installer.md)** | Agent Cross-Platform Compilation & Automated Systemd Installer | ⏳ Not Started |
| **[Plan 05](file:///home/ckingdon/projects/homelab-manager/docs/plans/05-kubernetes-production-packaging-and-helm.md)** | Kubernetes Production Packaging & Helm Chart (`charts/controlplane`) | ⏳ Not Started |
| **[Plan 06](file:///home/ckingdon/projects/homelab-manager/docs/plans/06-cicd-github-actions-and-release-pipeline.md)** | CI/CD GitHub Actions Automation & Release Pipeline | ⏳ Not Started |

---

## 🛠️ Iteration Process

When executing:
1. Open the target plan file in `docs/plans/`.
2. Follow the target file edits, execution steps, and verification instructions.
3. Verify all acceptance criteria and automated tests.
4. Update the plan file status and the table above to `✅ Completed`.
