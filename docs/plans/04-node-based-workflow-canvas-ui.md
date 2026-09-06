# Plan 04: Node-Based Visual Workflow Canvas UI (@xyflow/react)

**Phase:** Phase 3 (Temporal Durable Execution & Node-Based Visual Workflow Redesign)  
**Status:** ✅ Completed  
**Dependencies:** [Plan 03: Temporal Activities, Durable Sagas & Human-in-the-Loop Approval Gates](file:///home/ckingdon/projects/homelab-manager/docs/plans/03-temporal-activities-and-durable-sagas.md)

---

## 1. Objectives & Overview

Upgrade the workflow orchestration frontend from a static linear checklist to an interactive, state-of-the-art **Node-Based Visual DAG Canvas** powered by **`@xyflow/react`** (React Flow for React 19):

1. **`@xyflow/react` Visual DAG Canvas**:
   * Canvas supporting smooth pan/zoom, auto-layout (dagre or hierarchical grid), minimap, and background grid styling matching our dark-mode glassmorphic aesthetic.
2. **Custom Flow Nodes**:
   * **`WorkflowActivityNode`**: Represents a typed Temporal Activity (Preflights, Proxmox Snapshot, K8s Cordon/Drain, Package Upgrade, Reboot, Health Probes). Displays live status badges (Pending, Running with animated pulse, Succeeded, Failed, Compensated/RolledBack), duration timer, and activity metadata.
   * **`ApprovalGateNode`**: Human-in-the-Loop interactive node rendered when `RequireApprovalBeforeReboot = true`. Shows live waiting indicator, timeout countdown, and quick-action "Approve Reboot" and "Reject" buttons.
   * **`CompensationNode` / Branch**: Visual representation of reverse Saga compensation paths (e.g. Snapshot Rollback, Node Uncordon).
3. **Animated Dynamic Edges**:
   * State-aware edges: pulsing emerald/sky gradient for active transition paths, solid emerald for completed paths, dashed rose/amber for compensation branches, and muted zinc for pending paths.
4. **Temporal State & Signal Integration**:
   * Query hook `useTemporalWorkflowStatus(workflowId)` polling `/api/v1/orchestration/temporal/workflows/{workflowId}/status`.
   * Signal mutations for sending `ApproveReboot`, `RejectReboot`, and `Cancel` signals directly from the canvas nodes or toolbar.
5. **Hybrid Canvas & Terminal Experience**:
   * Seamless toggle/split-view between the visual DAG canvas and the streaming `xterm.js` console drawer, enabling users to see both high-level activity transitions and low-level command stdout/stderr simultaneously.

---

## 2. Target File Structure

```
src/frontend/
├── package.json                                       # Add @xyflow/react
├── src/
│   └── features/
│       └── orchestration/
│           ├── canvas/
│           │   ├── WorkflowCanvasModal.tsx            # Full visual canvas drawer/dialog
│           │   ├── WorkflowDagCanvas.tsx              # Core React Flow canvas wrapper
│           │   ├── nodes/
│           │   │   ├── WorkflowActivityNode.tsx       # Custom activity node component
│           │   │   ├── ApprovalGateNode.tsx           # Interactive human-in-the-loop node
│           │   │   └── WorkflowNodeTypes.ts           # Types & node registration map
│           │   ├── edges/
│           │   │   ├── AnimatedWorkflowEdge.tsx       # Flowing/pulsing SVG edge
│           │   │   └── WorkflowEdgeTypes.ts
│           │   ├── layout/
│           │   │   └── dagLayout.ts                   # Hierarchical node positioning
│           │   └── hooks/
│           │       └── useTemporalWorkflow.ts         # Query & Signal mutations
│           ├── WorkflowsView.tsx                      # Add "View Canvas" action to jobs table
│           └── PipelineStepPreview.tsx                # Compact mini-graph canvas preview
```

---

## 3. Implementation Details

### Step 1: Install `@xyflow/react`
* Install `@xyflow/react` in `src/frontend`.
* Import `@xyflow/react/dist/style.css` in `index.css` or the canvas component.

### Step 2: Custom Node & Edge Components
* Design `WorkflowActivityNode` with:
  - Header: activity icon + name + execution state badge.
  - Body: duration stopwatch, retry count, error alert tooltip if failed.
  - Handles: top/left target, bottom/right source.
* Design `ApprovalGateNode` with:
  - Yellow/amber warning border + pulsing shield.
  - Action buttons: "Approve Reboot" (calls `ApproveRebootSignal`) and "Reject" (calls `RejectRebootSignal`).
* Design `AnimatedWorkflowEdge` with:
  - Smooth step or bezier path.
  - Animated dash stroke or glow when source node is active or recently completed.

### Step 3: Dag Layout Generator
* Build a deterministic DAG generator that maps `HostUpgradeWorkflowState` or pipeline steps into node positions `(x, y)` with clear sequential and compensation branches.

### Step 4: Temporal Workflow Status & Signals Hook
* Implement `useTemporalWorkflow(workflowId)` with:
  - TanStack Query polling `/api/v1/orchestration/temporal/workflows/{id}/status`.
  - TanStack Mutation for `sendSignal(workflowId, signalName, payload)`.

### Step 5: Integration into WorkflowsView & Drawer
* Add an interactive "Visual DAG" button alongside the "Console" button in `WorkflowsView`.
* Provide a modal/drawer displaying the live canvas with fit view, zoom controls, and a collapsible xterm.js log panel.

---

## 4. Verification & Acceptance Criteria

### Verification Steps
```bash
# 1. Install dependencies & verify frontend build
cd src/frontend && npm run build

# 2. Verify linting
npm run lint

# 3. Verify backend solution builds cleanly
dotnet build
```

### Acceptance Criteria
- [x] `@xyflow/react` installed and integrated with React 19 and Tailwind CSS.
- [x] Custom `WorkflowActivityNode` renders all activity statuses with rich micro-animations.
- [x] `ApprovalGateNode` enables 1-click reboot approval / rejection via Temporal signals.
- [x] DAG layout renders sequential pipeline steps and reverse compensation branches cleanly.
- [x] Users can open the Visual DAG canvas from `WorkflowsView` and view live activity progress.
- [x] Frontend builds without TypeScript or Oxlint errors.
