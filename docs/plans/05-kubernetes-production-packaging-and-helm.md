# Plan 05: Kubernetes Production Packaging & Helm Chart

**Phase:** Phase 4: Productionization & Deployment  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 01: Production Containerization](file:///home/ckingdon/projects/homelab-manager/docs/plans/01-production-containerization-and-compose.md), [Plan 02: Zitadel Setup](file:///home/ckingdon/projects/homelab-manager/docs/plans/02-zitadel-identity-setup-and-backend-rbac.md)  

---

## 1. Objectives & Overview

Deliver production-ready **Kubernetes Deployment Manifests** and a modular **Helm Chart** (`charts/controlplane`) to enable zero-friction deployment to homelab Kubernetes clusters (k3s, Talos Linux, RKE2, microk8s):

1. **Helm Chart Architecture (`charts/controlplane`)**:
   * Parameterized `values.yaml` supporting:
     * Dual deployment modes: All-in-one homelab mode vs external infrastructure mode (external PostgreSQL, external Temporal, external Zitadel).
     * Image repository, tags, and pull policies.
     * Replica counts and Horizontal Pod Autoscaling (HPA).
     * Ingress controller configurations (Traefik, NGINX Ingress, Gateway API) with cert-manager TLS annotations.
     * Persistence configurations for local volume claims.

2. **Kubernetes Workloads & Resilience**:
   * `controlplane-api`: Deployment with rolling update strategy, liveness & readiness probes pointing to `/healthz`, resource requests/limits, security context (`runAsNonRoot: true`, `readOnlyRootFilesystem: false`).
   * `controlplane-frontend`: Lightweight Nginx SPA deployment.
   * `Service` definitions exposing ports `8080` (API) and `80` (Frontend).
   * WebSocket routing annotations ensuring uninterrupted SignalR and Agent WebSocket connections.

3. **Kustomize Overlays (`deploy/k8s/`)**:
   * Provide clean base manifests and environment overlays (`base/`, `overlays/production/`, `overlays/staging/`) for GitOps tools (ArgoCD, FluxCD).

4. **Network Policies & Security Hardening**:
   * Declarative `NetworkPolicy` resources isolating the database and restricting pod egress to designated infrastructure subnets (Proxmox hypervisors, iDRAC BMCs).

---

## 2. Target File Structure

```
charts/
└── controlplane/
    ├── Chart.yaml                           # Helm chart metadata
    ├── values.yaml                          # Default configuration values
    └── templates/
        ├── _helpers.tpl                     # Named template helpers
        ├── api-deployment.yaml              # Backend ASP.NET Core API deployment
        ├── api-service.yaml                 # API ClusterIP service
        ├── frontend-deployment.yaml         # Frontend Nginx SPA deployment
        ├── frontend-service.yaml            # Frontend ClusterIP service
        ├── ingress.yaml                     # Unified Ingress / WebSocket routing
        ├── configmap.yaml                   # Application configuration
        ├── secret.yaml                      # DB passwords and API keys
        └── networkpolicy.yaml               # Cluster network isolation rules

deploy/
└── k8s/
    ├── base/                                # Raw Kubernetes YAML manifests
    │   ├── kustomization.yaml
    │   ├── api.yaml
    │   ├── frontend.yaml
    │   └── ingress.yaml
    └── overlays/
        └── homelab/
            ├── kustomization.yaml
            └── patches/
```

---

## 3. Implementation Steps

1. **Scaffold Helm Chart (`charts/controlplane/`)**:
   * Create `Chart.yaml` specifying version `1.0.0` and appVersion.
   * Define comprehensive `values.yaml` with documented settings for DB, Temporal, Zitadel, and Ingress.

2. **Develop Helm Templates**:
   * Implement deployments, services, ingress, and configmaps with template variables.
   * Add WebSocket annotations for Traefik and Nginx Ingress controllers.

3. **Develop Kustomize Overlays (`deploy/k8s/`)**:
   * Provide a pure Kustomize alternative for users not utilizing Helm.

4. **Verify**:
   * Run `helm lint charts/controlplane`.
   * Run `helm template controlplane charts/controlplane` to validate YAML rendering.
   * Test manifest dry-run validation using `kubectl apply --dry-run=client`.

---

## 4. Acceptance Criteria

- [ ] `helm lint charts/controlplane` passes with zero errors or warnings.
- [ ] Rendered templates include health probes, non-root security contexts, and WebSocket ingress rules.
- [ ] Chart supports both internal containerized PostgreSQL/Temporal and external managed endpoints.
- [ ] Kustomize manifests validate cleanly via `kubectl kustomize`.
