# Plan 06: Multi-Node Fleet Rolling Orchestration & Batch Workflows

**Phase:** Phase 3 (Temporal Durable Execution & Node-Based Visual Workflow Redesign)  
**Status:** ✅ Completed  
**Dependencies:** [Plan 05: Parameterized Workflow Launcher & Real-Time Graph Preview](file:///home/ckingdon/projects/homelab-manager/docs/plans/05-parameterized-workflow-launcher.md)

---

## 1. Objectives & Overview

Orchestrate automated, zero-downtime fleet-wide updates across multiple Kubernetes cluster nodes, Proxmox hypervisors, and server tiers using **Temporal Child Workflows**:

1. **`RollingUpgradeWorkflow` (Parent Temporal Workflow)**:
   * Accepts a fleet of target hosts with configurable concurrency:
     * `MaxParallelism: 1` (default strict zero-downtime rolling update) or `N` (batch concurrency).
     * `FailureStrategy`: `StopOnFirstFailure` (blast-radius mitigation) vs `ContinueRemaining`.
     * `RequireApprovalBetweenHosts`: optional checkpoint pause between nodes.
   * Spawns `HostUpgradeWorkflow` instances as **Temporal Child Workflows**:
     * Tracks execution state deterministically across process crashes or network interruptions.
     * Manages per-host progress: `Queued` ➔ `In Progress` ➔ `Verifying` ➔ `Completed` / `Failed`.
   * Signals: `PauseAsync()`, `ResumeAsync()`, `CancelAsync()`.
   * Queries: `GetWorkflowState()` returning aggregate fleet metrics and per-node progress.

2. **Cluster Health & Schedulability Gating**:
   * For Kubernetes cluster nodes, strictly verifies node uncordon and cluster health verification before starting the next node in the rolling queue.
   * On failure, halts the fleet queue immediately to prevent cluster quorum loss or multi-node downtime.

3. **REST API Endpoints**:
   * `POST /api/v1/orchestration/temporal/batch/rolling-upgrade`: Start rolling fleet workflow.
   * `GET /api/v1/orchestration/temporal/batch/{batchId}/status`: Query live batch and per-node progress.
   * `POST /api/v1/orchestration/temporal/batch/{batchId}/signals/{signalName}`: Dispatch pause, resume, or abort signals.

4. **Frontend Fleet Rolling Experience**:
   * **Multi-Host Selection Mode**: In the workflow launcher, toggle between "Single Host" and "Multi-Host Fleet", allowing quick filters (e.g., "All Kubernetes Workers", "All Outdated Hosts").
   * **Fleet Rolling Dashboard / Modal**: Visual multi-node tracker showing the active rolling sequence, per-node status chips, live aggregate progress bar, and 1-click drill-down to any node's live DAG canvas.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Features/
    └── Orchestration/
        └── Temporal/
            ├── Workflows/
            │   ├── IRollingUpgradeWorkflow.cs
            │   ├── RollingUpgradeWorkflow.cs
            │   └── Models/
            │       ├── RollingUpgradeWorkflowInput.cs
            │       ├── RollingUpgradeWorkflowResult.cs
            │       └── RollingUpgradeWorkflowState.cs
            └── Endpoints/
                └── TemporalWorkflowEndpoints.cs (add batch endpoints)

src/frontend/
└── src/
    ├── api/
    │   └── temporal.ts (add batch models & api methods)
    └── features/
        └── orchestration/
            ├── fleet/
            │   ├── FleetRollingLauncherModal.tsx     # Multi-node batch launcher
            │   └── FleetRollingDashboardModal.tsx    # Live multi-node rolling tracker
            └── WorkflowsView.tsx                     # Add "Rolling Fleet Update" action
```

---

## 3. Implementation Details

### Step 1: Implement `RollingUpgradeWorkflow` with Child Workflows
* Define `RollingUpgradeWorkflowInput`, `RollingUpgradeWorkflowResult`, and `RollingUpgradeWorkflowState`.
* Implement `RollingUpgradeWorkflow`:
  ```csharp
  [Workflow]
  public class RollingUpgradeWorkflow : IRollingUpgradeWorkflow
  {
      [WorkflowRun]
      public async Task<RollingUpgradeWorkflowResult> RunAsync(RollingUpgradeWorkflowInput input)
      {
          foreach (var target in input.TargetHosts)
          {
              // Check cancellation / pause
              await Workflow.WaitConditionAsync(() => !_paused || _cancelled);
              if (_cancelled) break;

              // Launch child workflow
              var childInput = new HostUpgradeWorkflowInput(...);
              var result = await Workflow.ExecuteChildWorkflowAsync(
                  (IHostUpgradeWorkflow w) => w.RunAsync(childInput),
                  new ChildWorkflowOptions { Id = $"child-upgrade-{target.HostId}-{input.BatchId}" }
              );

              if (!result.Success && input.FailureStrategy == "StopOnFirstFailure")
              {
                  break; // Halt rolling upgrade to protect cluster
              }
          }
      }
  }
  ```

### Step 2: Register Rolling Workflow & Worker
* In `TemporalServiceCollectionExtensions.cs`:
  ```csharp
  services.AddHostedTemporalWorker(options.TaskQueue)
      .AddWorkflow<HostUpgradeWorkflow>()
      .AddWorkflow<RollingUpgradeWorkflow>()
      ...
  ```

### Step 3: Batch API Endpoints
* Map `/api/v1/orchestration/temporal/batch/rolling-upgrade` and `/api/v1/orchestration/temporal/batch/{batchId}/status`.

### Step 4: Frontend Fleet Rolling Components
* Add fleet batch API methods to `src/frontend/src/api/temporal.ts`.
* Create `FleetRollingLauncherModal.tsx` allowing multi-host selection, concurrency slider, and failure strategy selector.
* Create `FleetRollingDashboardModal.tsx` displaying live multi-node progression with node cards, status badges, and pause/resume/abort controls.
* Add "Rolling Fleet Upgrade" button to `WorkflowsView.tsx`.

---

## 4. Verification & Acceptance Criteria

### Verification Steps
```bash
# 1. Build and test backend
dotnet build
dotnet test --filter "FullyQualifiedName~Temporal"

# 2. Build and lint frontend
cd src/frontend && npm run build && npm run lint
```

### Acceptance Criteria
- [x] `RollingUpgradeWorkflow` executes child workflows sequentially or concurrently with configurable `MaxParallelism`.
- [x] Workflow stops immediately when a child node fails under `StopOnFirstFailure`.
- [x] REST API endpoints allow launching rolling batches and querying aggregate status.
- [x] Frontend allows multi-node selection and displays real-time fleet progress.
- [x] All automated tests and builds pass cleanly.
