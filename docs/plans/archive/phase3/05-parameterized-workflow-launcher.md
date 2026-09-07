# Plan 05: Parameterized Workflow Launcher & Real-Time Graph Preview

**Phase:** Phase 3 (Temporal Durable Execution & Node-Based Visual Workflow Redesign)  
**Status:** ✅ Completed  
**Dependencies:** [Plan 04: Node-Based Visual Workflow Canvas UI (@xyflow/react)](file:///home/ckingdon/projects/homelab-manager/docs/plans/04-node-based-workflow-canvas-ui.md)

---

## 1. Objectives & Overview

Upgrade the workflow launcher from a static profile selector into an advanced **Parameterized Temporal Workflow Launcher** featuring a **Real-Time Interactive DAG Preview**:

1. **Rich Parameter Configuration Controls**:
   * **Target Host Selector**: Filters managed hosts with online agents, displaying OS, pending update counts, and target type badges.
   * **Pre-Update Safety & Snapshot Options**:
     * Proxmox Snapshot toggle (auto-selected if Proxmox VM/LXC).
     * Custom snapshot identifier template (e.g. `pre-upgrade-2026-09-06`).
   * **Kubernetes Cordon & Drain Policy**:
     * Node drain/eviction toggle (auto-selected if K8s node).
     * Node name input.
   * **Reboot & Human Approval Policy**:
     * `RequireApprovalBeforeReboot` toggle with interactive warning gate explanation.
     * `AlwaysReboot` toggle vs conditional reboot on package demand.
     * Approval timeout presets (1h, 4h, 12h, 24h).
   * **Post-Upgrade Synthetic Health Probes**:
     * Add/remove custom HTTP and TCP probe URLs (e.g. `http://{host.ipAddress}:8080/healthz`).
     * Quick-add service presets (HTTP Web, SSH :22, Kubelet :10250, Proxmox :8006).

2. **Real-Time Dynamic DAG Canvas Preview**:
   * As the operator configures options in the form, the embedded `@xyflow/react` canvas updates instantaneously:
     * Toggling Proxmox Snapshot inserts the snapshot activity node and its downward Saga compensation rollback node.
     * Toggling K8s Cordon/Drain inserts the cordon/drain node, uncordon node, and Saga compensation.
     * Toggling Reboot Approval inserts the `ApprovalGateNode`.
     * Adding Health Probes adds and updates probe verification nodes.
   * The operator sees the precise execution DAG before initiating any node disruption.

3. **1-Click Temporal Workflow Dispatch**:
   * Submits parameters to `POST /api/v1/orchestration/temporal/workflows/start`.
   * On submission, seamlessly transitions to the live `WorkflowCanvasModal` or opens the terminal drawer so the operator immediately watches execution in real time.
   * Invalidates TanStack Query caches for jobs and workflows.

---

## 2. Target File Structure

```
src/frontend/
└── src/
    └── features/
        └── orchestration/
            ├── launcher/
            │   ├── ParameterizedWorkflowLauncher.tsx    # Slide-over / Modal parameter builder
            │   ├── HostSelectorField.tsx                # Host selector with capability detection
            │   ├── SnapshotPolicySection.tsx            # Proxmox snapshot controls
            │   ├── KubernetesPolicySection.tsx          # K8s cordon/drain controls
            │   ├── RebootApprovalSection.tsx            # Approval gate & reboot policy controls
            │   ├── HealthProbesSection.tsx              # Dynamic probe URL manager & presets
            │   └── LiveDagPreview.tsx                   # Synchronous @xyflow/react preview canvas
            ├── LaunchWorkflowModal.tsx                  # Integrate parameterized launcher
            └── WorkflowsView.tsx                        # Launch Workflow trigger binding
```

---

## 3. Implementation Details

### Step 1: Author Modular Launcher Sub-components
* Create `RebootApprovalSection.tsx` with toggle switch for `requireApprovalBeforeReboot` and `alwaysReboot`.
* Create `SnapshotPolicySection.tsx` with toggle and snapshot name input.
* Create `KubernetesPolicySection.tsx` with cordon/drain toggle.
* Create `HealthProbesSection.tsx` allowing operators to add custom probe URLs or click presets.

### Step 2: Live DAG Preview Generator
* Connect parameter form state to `dagLayout.ts`, passing:
  - `requireApproval = form.requireApproval`
  - `isProxmoxHost = form.enableSnapshot`
  - `isK8sHost = form.enableK8sDrain`
* Render `WorkflowDagCanvas` in preview mode (height: 280px).

### Step 3: Integrate with Temporal Workflow API
* Wire submit button to `startTemporalWorkflow()` from `src/frontend/src/api/temporal.ts`.
* On success, call `onWorkflowLaunched(response.jobId, host, response.workflowId)`.

### Step 4: Update WorkflowsView & LaunchWorkflowModal
* Replace legacy `createJob` call with `startTemporalWorkflow` when Temporal is active.
* Transition directly into the live `WorkflowCanvasModal`.

---

## 4. Verification & Acceptance Criteria

### Verification Steps
```bash
# 1. Build frontend
cd src/frontend && npm run build

# 2. Lint frontend
npm run lint

# 3. Build backend
dotnet build

# 4. Run tests
dotnet test --filter "FullyQualifiedName~TemporalWorkflowTests"
```

### Acceptance Criteria
- [x] Operator can configure target host, snapshot, K8s drain, reboot approval, and synthetic health probes in the launcher.
- [x] Live `@xyflow/react` DAG canvas updates dynamically as toggles are changed.
- [x] 1-click trigger dispatches `POST /api/v1/orchestration/temporal/workflows/start`.
- [x] On launch, UI automatically opens the live visual DAG canvas modal.
- [x] All frontend and backend builds and tests pass cleanly.
