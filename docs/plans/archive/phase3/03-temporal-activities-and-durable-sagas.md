# Plan 03: Temporal Activities, Durable Sagas & Human-in-the-Loop Approval Gates

**Phase:** Phase 3 (Temporal Durable Execution & Node-Based Visual Workflow Redesign)  
**Status:** ✅ Completed  
**Dependencies:** [Plan 02: Temporal Aspire Hosting & .NET SDK Setup](file:///home/ckingdon/projects/homelab-manager/docs/plans/02-temporal-aspire-orchestration-and-sdk.md)

---

## 1. Objectives & Overview

Migrate the execution engine from in-memory sequential `Task.Run` pipelines to durable **Temporal Workflows** featuring **Activities**, **Saga Compensations**, and **Human-in-the-Loop Signals**:

1. **Typed Temporal Activities**:
   * Migrate existing step implementations into structured, DI-injected Activity classes:
     * **`PreflightActivities`**: Heartbeat freshness (< 15s), disk headroom (> 20%), package manager lock checking.
     * **`ProxmoxActivities`**: Pre-update safety snapshot creation and snapshot rollback on failure.
     * **`KubernetesActivities`**: Node cordon, non-daemonset pod drain with eviction API, and node uncordon.
     * **`AgentActivities`**: Non-interactive package upgrade execution with activity heartbeats and SignalR log streaming, reboot initiation, and post-reboot reconnection monitoring.
     * **`HealthProbeActivities`**: Synthetic HTTP/TCP status probing and systemd service verification.
2. **Durable Workflow Definition (`HostUpgradeWorkflow`)**:
   * Implement `[Workflow]` interface:
     * Accepts parameterized `HostUpgradeWorkflowInput` (target host, snapshot options, reboot policy, health probe configs).
     * Tracks execution state deterministically across process crashes or host reboots.
   * **Saga Compensation Pattern**:
     * Automatically registers compensations upon activity completion (e.g. uncordon node, revert snapshot).
     * Automatically triggers reverse compensations if any activity fails or times out.
3. **Human-in-the-Loop Approval Gates via Signals**:
   * Support `[WorkflowSignal] ApproveRebootSignal()`, `[WorkflowSignal] RejectSignal()`, and `[WorkflowSignal] CancelSignal()`.
   * When `RequireApprovalBeforeReboot = true`, workflow halts before reboot:
     `await Workflow.WaitConditionAsync(() => _rebootApproved, timeout: TimeSpan.FromHours(4))`.
   * Expose query: `[WorkflowQuery] GetWorkflowState()` returning active step, completed steps, and approval requirement.
4. **API Endpoints for Workflow Management**:
   * `POST /api/v1/orchestration/temporal/workflows/start`: Launch workflow via `ITemporalClient`.
   * `POST /api/v1/orchestration/temporal/workflows/{workflowId}/signals/{signalName}`: Send approval/cancel signal.
   * `GET /api/v1/orchestration/temporal/workflows/{workflowId}/status`: Query live workflow progress.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Features/
    └── Orchestration/
        └── Temporal/
            ├── Activities/
            │   ├── IPreflightActivities.cs & PreflightActivities.cs
            │   ├── IProxmoxActivities.cs & ProxmoxActivities.cs
            │   ├── IKubernetesActivities.cs & KubernetesActivities.cs
            │   ├── IAgentActivities.cs & AgentActivities.cs
            │   └── IHealthProbeActivities.cs & HealthProbeActivities.cs
            ├── Workflows/
            │   ├── IHostUpgradeWorkflow.cs
            │   ├── HostUpgradeWorkflow.cs
            │   └── Models/
            │       ├── HostUpgradeWorkflowInput.cs
            │       ├── HostUpgradeWorkflowResult.cs
            │       └── HostUpgradeWorkflowState.cs
            └── Endpoints/
                └── TemporalWorkflowEndpoints.cs
```

---

## 3. Implementation Details

### Step 1: Author Activity Classes
* Register each activity with appropriate `[Activity]` attributes and timeout/retry defaults:
  ```csharp
  [Activity]
  public async Task<PreflightResult> CheckHeartbeatAsync(Guid hostId);
  ```
* Ensure `AgentActivities` emit monotonic SignalR logs while reporting activity heartbeats.

### Step 2: Implement `HostUpgradeWorkflow` with Sagas & Signals
* Implement the workflow orchestration logic:
  ```csharp
  [Workflow]
  public class HostUpgradeWorkflow : IHostUpgradeWorkflow
  {
      private bool _rebootApproved = false;
      private bool _cancelled = false;

      [WorkflowSignal]
      public Task ApproveRebootAsync() { _rebootApproved = true; return Task.CompletedTask; }

      [WorkflowRun]
      public async Task<HostUpgradeWorkflowResult> RunAsync(HostUpgradeWorkflowInput input)
      {
          // 1. Preflights
          // 2. Proxmox Snapshot + Register Saga Compensation
          // 3. Kubernetes Cordon/Drain + Register Saga Compensation
          // 4. Package Upgrade
          // 5. Approval Gate if requested:
          if (input.RequireApprovalBeforeReboot)
          {
              await Workflow.WaitConditionAsync(() => _rebootApproved || _cancelled);
          }
          // 6. Reboot & Await Reconnect
          // 7. Health Probes
          // 8. Kubernetes Uncordon
      }
  }
  ```

### Step 3: Register Activities & Workflows with Hosted Worker
* In `TemporalServiceCollectionExtensions.cs`:
  ```csharp
  services.AddHostedTemporalWorker(options.TaskQueue)
      .AddWorkflow<HostUpgradeWorkflow>()
      .AddScopedActivities<PreflightActivities>()
      .AddScopedActivities<ProxmoxActivities>()
      .AddScopedActivities<KubernetesActivities>()
      .AddScopedActivities<AgentActivities>()
      .AddScopedActivities<HealthProbeActivities>();
  ```

### Step 4: Map Temporal Workflow API Endpoints
* In `TemporalWorkflowEndpoints.cs`, map endpoints to trigger workflows, dispatch signals, and fetch live workflow status.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# 1. Build the solution
dotnet build

# 2. Run unit and integration tests
dotnet test

# 3. Verify workflow and activity registration
dotnet test --filter "FullyQualifiedName~TemporalWorkflowTests"
```

### Acceptance Criteria
- [x] All 12 step capabilities ported to typed Temporal Activities.
- [x] `HostUpgradeWorkflow` executes end-to-end with Saga compensations on activity failure.
- [x] Workflow pauses and resumes cleanly on `ApproveRebootSignal`.
- [x] REST API endpoints permit starting workflows, sending signals, and querying workflow status.
- [x] All automated tests pass.
