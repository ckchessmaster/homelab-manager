# Plan 02: Multi-Cluster Kubernetes Adapter & Workload Engine

This plan implements the **Kubernetes Infrastructure Adapter**, enabling ControlPlane to manage multiple Kubernetes clusters (e.g., k3s, RKE2, Talos, vanilla k8s). It provides encrypted kubeconfig/token credential storage, dynamic client resolution, cluster health telemetry, node lifecycle management (cordon/drain), pod and workload discovery, and application management actions (scaling, rollout restarts).

---

## 🎯 Objectives & Acceptance Criteria

1. **Multi-Cluster Kubernetes Configuration in Backend:**
   - Store encrypted cluster connection credentials in `SystemSettings` under `adapters:kubernetes:clusters` using `ISecretEncryptionService`.
   - Support both raw YAML kubeconfig files (with context selection) and direct API Server URL + Bearer Token / Client Certificates.
   - Endpoints for cluster management:
     - `GET /api/v1/adapters/kubernetes/clusters`: List configured clusters with status and node/pod tallies.
     - `GET /api/v1/adapters/kubernetes/clusters/{id}`: Detailed cluster information.
     - `POST /api/v1/adapters/kubernetes/clusters`: Add or update a cluster configuration.
     - `DELETE /api/v1/adapters/kubernetes/clusters/{id}`: Remove a cluster.
     - `POST /api/v1/adapters/kubernetes/clusters/{id}/test-connection`: Ping API server, validate auth, return k8s server version and node count.

2. **Dynamic Kubernetes Client Factory:**
   - Implement `IKubernetesClientFactory` and `KubernetesClientFactory` using the official `KubernetesClient` NuGet package or resilient typed `HttpClient`.
   - Dynamically build `KubernetesClientConfiguration` per cluster configuration with support for custom CA certificates and skip-TLS verification options.

3. **Node Lifecycle & Cluster Health:**
   - Query cluster nodes: name, roles (control-plane, worker), conditions (Ready, DiskPressure, MemoryPressure, PIDPressure), architecture, OS, container runtime, and kubelet version.
   - Node administrative operations:
     - `POST /api/v1/adapters/kubernetes/clusters/{id}/nodes/{nodeName}/cordon`: Mark node unschedulable.
     - `POST /api/v1/adapters/kubernetes/clusters/{id}/nodes/{nodeName}/uncordon`: Mark node schedulable.
     - `POST /api/v1/adapters/kubernetes/clusters/{id}/nodes/{nodeName}/drain`: Safely evict pods honoring PodDisruptionBudgets and daemonsets.

4. **Workload & Pod Inventory:**
   - Fetch cluster namespaces, deployments, daemonsets, statefulsets, and pods.
   - Workload operations:
     - `POST /api/v1/adapters/kubernetes/clusters/{id}/workloads/deployments/{namespace}/{name}/restart`: Trigger rollout restart by patching pod template annotation (`kubectl rollout restart`).
     - `POST /api/v1/adapters/kubernetes/clusters/{id}/workloads/deployments/{namespace}/{name}/scale`: Scale replica count up or down.

5. **Discovery Integration:**
   - `DiscoveryService.cs` discovers cluster nodes as candidate hosts tagged `k8s:{clusterId}:{nodeName}`.
   - 1-click adoption creates a managed `Host` with `TargetType.KubernetesNode`.

6. **Frontend Kubernetes Adapter UI:**
   - Tab in `AdaptersView.tsx`: `KubernetesAdaptersView.tsx`.
   - Cluster overview cards with server version, node summary badges, and live connection status.
   - `AddKubernetesModal.tsx`: Upload `.kube/config` file, paste YAML, or input API endpoint + Token with pre-flight connection test.
   - Cluster detail drawer showing nodes, conditions, cordon/drain buttons, and namespace summary.

---

## 📁 Target File Structure

```
src/ControlPlane.Api/
├── Features/
│   └── Adapters/
│       └── Kubernetes/
│           ├── IKubernetesClient.cs
│           ├── KubernetesClient.cs
│           ├── IKubernetesClientFactory.cs
│           ├── KubernetesClientFactory.cs
│           ├── KubernetesModels.cs
│           └── KubernetesEndpoints.cs
└── Storage/Entities/
    └── Host.cs (Kubernetes cluster & node binding fields)

src/frontend/src/
├── api/
│   └── kubernetes.ts
├── features/adapters/
│   └── kubernetes/
│       ├── KubernetesAdaptersView.tsx
│       ├── AddKubernetesModal.tsx
│       ├── ClusterDetailDrawer.tsx
│       └── useKubernetes.ts
```

---

## 📋 Task Checklist

- [x] Add dynamic Kubernetes client abstraction supporting kubeconfig and bearer token auth.
- [x] Define cluster config models (`KubernetesClusterDto`, `SaveKubernetesClusterRequest`, `ClusterNodeDto`, `ClusterWorkloadDto`) in `KubernetesModels.cs` and `AdapterConfigModels.cs`.
- [x] Implement `IKubernetesClientFactory` and dynamic `KubernetesClient` supporting kubeconfig and bearer token auth with AES-256 encryption.
- [x] Implement cluster CRUD endpoints and `/test-connection` in `KubernetesEndpoints.cs`.
- [x] Implement node management endpoints (`/nodes`, `/cordon`, `/uncordon`, `/drain`).
- [x] Implement workload endpoints (`/namespaces`, `/deployments`, `/restart`, `/scale`, `/pods`).
- [x] Update `DiscoveryService.cs` to discover Kubernetes nodes across all configured clusters as candidate hosts.
- [x] Create frontend API client in `src/frontend/src/api/kubernetes.ts` and React Query hooks in `useKubernetes.ts`.
- [x] Build `KubernetesAdaptersView.tsx`, `AddKubernetesModal.tsx`, and `ClusterDetailDrawer.tsx`.
- [x] Write backend unit tests in `ControlPlane.Api.Tests` (`KubernetesMultiClusterTests.cs`).
- [x] Verify `dotnet test` (all 166 tests pass) and `npm run build` (builds cleanly).
