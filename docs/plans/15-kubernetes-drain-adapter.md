# Plan 15: Kubernetes Cordon & Drain Workload Adapter

**Target Milestone:** Milestone 4 (Out-of-Band Integrations & Zitadel OIDC)  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 10: DAG Orchestration Engine](file:///home/ckingdon/projects/homelab-manager/docs/plans/10-dag-orchestration-engine.md)  

---

## 1. Objectives & Overview

Enable safe rolling updates of Kubernetes cluster nodes without service disruption:
1. Integrate the official Kubernetes C# Client (`KubernetesClient`).
2. Implement **Node Cordon**: mark the target node unschedulable (`spec.unschedulable = true`) so new pods are not scheduled to it.
3. Implement **Workload Eviction (Drain)**:
   * Enumerate pods running on the node (skipping DaemonSets and static mirror pods).
   * Issue eviction requests via the Kubernetes Eviction API (`POST /api/v1/namespaces/{ns}/pods/{name}/eviction`), strictly honoring `PodDisruptionBudgets` (PDBs).
   * Poll with timeout until all evictable pods have cleanly terminated or rescheduled.
4. Implement **Node Uncordon**: restore scheduling (`spec.unschedulable = false`) once post-boot health verification succeeds.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Features/
    ├── Adapters/
    │   └── Kubernetes/
    │       ├── IKubernetesAdapter.cs
    │       ├── KubernetesAdapter.cs
    │       ├── KubernetesModels.cs
    │       └── KubernetesConfigOptions.cs
    └── Orchestration/
        └── Steps/
            ├── KubernetesCordonStep.cs
            ├── KubernetesDrainStep.cs
            └── KubernetesUncordonStep.cs
```

---

## 3. Implementation Details

### Step 1: Kubernetes Client Configuration
* Support both in-cluster service account configuration (`KubernetesClientConfiguration.InClusterConfig()`) and external kubeconfig path / certificate data for standby workstation use.

### Step 2: Cordon & Uncordon Logic
* `CordonNodeAsync(nodeName, ct)`:
  * Submit JSON merge patch: `{"spec": {"unschedulable": true}}`.
* `UncordonNodeAsync(nodeName, ct)`:
  * Submit JSON merge patch: `{"spec": {"unschedulable": false}}`.

### Step 3: Eviction Loop (Drain)
* List pods on node: `fieldSelector: "spec.nodeName=" + nodeName`.
* Filter out:
  * DaemonSet pods (ownerReference `Kind == "DaemonSet"`).
  * Mirror pods (`kubernetes.io/config.mirror` annotation).
* For remaining pods:
  * Create `V1Eviction` object with GracePeriodSeconds.
  * Submit eviction request.
  * If HTTP 429 Too Many Requests (PDB violation), back off and retry.
  * Wait for pod termination up to a configurable timeout (e.g. 180s).

### Step 4: Step Placement in Update Pipeline
* In `DagExecutionPipeline`:
  ```
  1. Preflight Checks -> 2. Proxmox Snapshot -> 3. K8s Cordon & Drain -> 
  4. Package Upgrade -> 5. Deterministic Reboot -> 6. Health Probes -> 7. K8s Uncordon
  ```

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Test cordon/drain against dev cluster
curl -X POST http://localhost:5000/api/v1/adapters/k8s/cordon \
  -H "Content-Type: application/json" \
  -H "X-ControlPlane-Key: dev-secret-key-123" \
  -d '{"nodeName": "k8s-worker-02"}'
```

### Acceptance Criteria
- [ ] Node is successfully marked unschedulable in Kubernetes.
- [ ] Eviction API respects PodDisruptionBudgets and safely evicts non-DaemonSet pods.
- [ ] Post-flight uncordon step reliably restores scheduling to the updated node.
