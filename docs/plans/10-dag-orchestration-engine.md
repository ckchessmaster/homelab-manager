# Plan 10: DAG Update Orchestration State Machine & Pre-Flight Engine

**Target Milestone:** Milestone 3 (Update Orchestration Engine & Standby Runner)  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 07: WebSocket Agent Hub](file:///home/ckingdon/projects/homelab-manager/docs/plans/07-websocket-agent-hub.md), [Plan 08: Real-Time Terminal Pipeline](file:///home/ckingdon/projects/homelab-manager/docs/plans/08-realtime-terminal-pipeline.md)  

---

## 1. Objectives & Overview

Implement the durable, asynchronous Directed Acyclic Graph (DAG) state machine for orchestrating host upgrades:
1. Define the orchestration state machine: `Pending` -> `Running` -> `Verifying` -> `Completed` (or `Failed` / `RolledBack`).
2. Implement **Pre-Flight Validation Steps**:
   * Verify agent heartbeat freshness (< 15 seconds).
   * Check root filesystem headroom (> 20% available space).
   * Verify absence of stale package locks (`/var/lib/dpkg/lock-frontend`, `/var/lib/dpkg/lock`, `/var/lib/rpm/.rpm.lock`).
3. Maintain step execution context and persist step transitions in the `update_jobs` table.
4. Broadcast state changes to the UI in real-time via SignalR.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Features/
    └── Orchestration/
        ├── StateMachine/
        │   ├── UpdateJobState.cs
        │   ├── IJobStep.cs
        │   ├── JobStepResult.cs
        │   ├── JobExecutionContext.cs
        │   └── DagExecutionPipeline.cs
        ├── Steps/
        │   ├── PreflightHeartbeatCheckStep.cs
        │   ├── PreflightDiskHeadroomCheckStep.cs
        │   ├── PreflightPackageLockCheckStep.cs
        │   └── PackageUpgradeStep.cs
        ├── JobOrchestratorService.cs
        └── JobEndpoints.cs
```

---

## 3. Implementation Details

### Step 1: Step Abstraction & Pipeline Context
* Define step interface:
  ```csharp
  public interface IJobStep
  {
      string StepName { get; }
      Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct);
      Task RollbackAsync(JobExecutionContext context, CancellationToken ct);
  }
  ```
* `JobExecutionContext`: encapsulates `UpdateJob`, target `Host`, SignalR logger, cancellation token, and dynamic state dictionary.

### Step 2: Pre-Flight Safety Checks
1. **Heartbeat Freshness:** Ensure `DateTimeOffset.UtcNow - host.AgentLastSeenAt < TimeSpan.FromSeconds(15)`. Abort if agent is stale.
2. **Disk Space Check:** Query agent for root partition free percentage. Abort if free space < 20%.
3. **Lock Verification:** Instruct agent to inspect filesystem lock files:
   * Debian/Ubuntu: `fuser /var/lib/dpkg/lock-frontend` or test file open.
   * If another process is holding the lock, pause or abort cleanly without leaving orphaned state.

### Step 3: Package Upgrade Execution Step
* Dispatch upgrade envelope to agent over WebSocket:
  * Debian: `DEBIAN_FRONTEND=noninteractive apt-get dist-upgrade -y -o Dpkg::Options::="--force-confdef" -o Dpkg::Options::="--force-confold"`
  * RHEL: `dnf upgrade -y`
* Agent streams `stdout`/`stderr` chunks back to backend; backend records `step_logs` and broadcasts via SignalR.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Trigger an update job
curl -X POST http://localhost:5000/api/v1/jobs \
  -H "Content-Type: application/json" \
  -H "X-ControlPlane-Key: dev-secret-key-123" \
  -d '{"targetHostId": "<host-uuid>"}'

# Query job status
curl http://localhost:5000/api/v1/jobs/<job-uuid> -H "X-ControlPlane-Key: dev-secret-key-123"
```

### Acceptance Criteria
- [ ] Pre-flight checks fail safely and halt the pipeline if the agent is offline or disk space is < 20%.
- [ ] Valid pre-flight checks transition the job from `Pending` to `Running`.
- [ ] All step transitions are recorded in `update_jobs` and reflected live on the UI.
