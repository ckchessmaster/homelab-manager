# Plan 03: Proxmox Snapshot 24-Hour Retention Worker & Automated Pruning

**Phase:** Phase 2  
**Status:** ✅ Completed  
**Dependencies:** [Plan 01: Service Discovery](file:///home/ckingdon/projects/homelab-manager/docs/plans/01-service-discovery-proxmox-and-k8s.md), [Plan 02: Modular Pipeline Profiles](file:///home/ckingdon/projects/homelab-manager/docs/plans/02-modular-pipeline-profiles.md)

---

## 1. Objectives & Overview

Prevent disk storage exhaustion on Proxmox VE hypervisors caused by lingering pre-update safety snapshots:
1. **Background Retention Service (`SnapshotRetentionWorker : BackgroundService`)**:
   * Runs periodic background evaluation (configurable interval, default: 60 minutes).
   * Queries all managed hosts with linked Proxmox VMs or LXCs.
   * Discovers existing hypervisor snapshots via Proxmox REST API (`GET /nodes/{node}/{vmType}/{vmid}/snapshot`).
2. **Deterministic Retention & Safety Policy (`ISnapshotRetentionService`)**:
   * Configurable retention window (default: 24 hours, `Proxmox:Retention:RetentionHours`).
   * **Active Job Protection**: Never delete a snapshot referenced by an `UpdateJob` currently in `Running` or `Verifying` state.
   * **Scope Isolation**: Only prune ControlPlane-created safety snapshots (`cp-pre-update-*` or recorded in `UpdateJob.SnapshotIdentifier`), ignoring manual operator snapshots and the Proxmox `"current"` state pointer.
   * **Safe Deletion**: Issue `DELETE /nodes/{node}/{vmType}/{vmid}/snapshot/{snapname}` and poll task UPID until completed.
3. **Operator Control & REST Endpoints**:
   * `GET /api/v1/adapters/proxmox/snapshots?hostId={hostId}`: list snapshots with age, expiration status, and active job locks.
   * `POST /api/v1/adapters/proxmox/snapshots/prune`: trigger on-demand retention pruning across all hosts or a specific host (with `dryRun` support).
   * `DELETE /api/v1/adapters/proxmox/snapshots/{snapshotName}?hostId={hostId}`: immediate deletion of an individual snapshot with confirmation.
4. **Interactive UI (React 19)**:
   * "Hypervisor Snapshots" management drawer / modal accessible from Host Inventory and Workflows.
   * Visual indicators of snapshot age, expiration badge, and job protection lock.
   * One-click "Prune Expired Snapshots" with dry-run preview count.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Features/
    └── Adapters/
        └── Proxmox/
            ├── IProxmoxClient.cs (add ListVmSnapshotsAsync)
            ├── ProxmoxClient.cs (implement ListVmSnapshotsAsync)
            ├── ProxmoxModels.cs (add ProxmoxSnapshotItem, ProxmoxSnapshotListResponse)
            ├── SnapshotRetentionOptions.cs
            ├── ISnapshotRetentionService.cs
            ├── SnapshotRetentionService.cs
            ├── SnapshotRetentionWorker.cs
            ├── SnapshotRetentionEndpoints.cs
            └── SnapshotDtos.cs

src/frontend/src/
├── api/
│   └── snapshots.ts
└── features/
    └── snapshots/
        ├── SnapshotManagementModal.tsx
        ├── SnapshotTable.tsx
        └── useSnapshots.ts

tests/ControlPlane.Api.Tests/
└── SnapshotRetentionTests.cs
```

---

## 3. Implementation Details

### Step 1: Proxmox API Client Snapshot Enumeration
* In `IProxmoxClient`:
  ```csharp
  Task<List<ProxmoxSnapshotItem>> ListVmSnapshotsAsync(
      string node,
      int vmid,
      bool isLxc = false,
      CancellationToken ct = default);
  ```
* In `ProxmoxModels.cs`:
  ```csharp
  public record ProxmoxSnapshotItem(
      [property: JsonPropertyName("name")] string Name,
      [property: JsonPropertyName("snaptime")] long? SnapTime = null,
      [property: JsonPropertyName("description")] string? Description = null,
      [property: JsonPropertyName("vmstate")] int? VmState = null,
      [property: JsonPropertyName("parent")] string? Parent = null
  );
  ```

### Step 2: Retention Policy & Service
* Define `SnapshotRetentionOptions`:
  * `RetentionHours` (default: 24)
  * `ScanIntervalMinutes` (default: 60)
  * `Enabled` (default: true)
  * `SnapshotPrefix` (default: `"cp-pre-update-"`)
* Implement `ISnapshotRetentionService`:
  * Correlate active `UpdateJob` entities: if any job has `SnapshotIdentifier == snap.Name` and state is `Running` or `Verifying`, mark `IsProtected = true` and do not delete.
  * Evaluate age: compare current UTC time to `snaptime` (or timestamp in snapshot name `cp-pre-update-yyyyMMddHHmmss`).
  * If `ageHours >= RetentionHours` and snapshot matches prefix, mark `IsExpired = true`.
  * Support `dryRun` flag on `PruneExpiredSnapshotsAsync` to calculate what would be deleted without executing actual DELETE calls.

### Step 3: Background Worker (`SnapshotRetentionWorker`)
* `SnapshotRetentionWorker : BackgroundService`:
  * Sleeps on `PeriodicTimer(TimeSpan.FromMinutes(options.ScanIntervalMinutes))`.
  * In each loop:
    * Check if enabled.
    * Run `_retentionService.PruneExpiredSnapshotsAsync(dryRun: false, ct)`.
    * Log summary: `{PrunedCount} expired snapshots pruned, {SkippedCount} skipped/protected, {ErrorCount} errors`.

### Step 4: REST Management Endpoints
* Map endpoints under `/api/v1/adapters/proxmox/snapshots`:
  * `GET /`: returns snapshots across all or selected host.
  * `POST /prune`: accepts `{ hostId?, dryRun? }` and triggers pruning.
  * `DELETE /{snapshotName}?hostId={hostId}`: deletes specific snapshot immediately.

### Step 5: Frontend UI
* Create `src/frontend/src/features/snapshots/SnapshotManagementModal.tsx`:
  * View active snapshots across Proxmox VMs/LXCs.
  * Badge tags: `Protected (Running Job)`, `Active (<24h)`, `Expired (>24h)`.
  * Actions: "Prune All Expired", "Delete".
* Link modal in `HostTable.tsx` row action dropdown ("Proxmox Snapshots") and `AppHeader` / `WorkflowsView`.

---

## 4. Verification & Acceptance Criteria

### Automated Tests
* Add `tests/ControlPlane.Api.Tests/SnapshotRetentionTests.cs`:
  * Verify `ListVmSnapshotsAsync` deserializes correctly and filters out `"current"`.
  * Verify active jobs (`Running`, `Verifying`) protect snapshots from pruning.
  * Verify expired snapshots (>24h) are pruned and task completion polled.
  * Verify unexpired snapshots (<24h) and non-ControlPlane snapshots are preserved.
  * Verify `dryRun: true` returns candidates without calling `DeleteVmSnapshotAsync`.
  * Verify REST endpoints return 200 OK with correct payloads.

### Acceptance Criteria
- [x] Snapshots older than 24 hours created by ControlPlane are automatically pruned.
- [x] Active jobs currently running or verifying are never stripped of their safety snapshot.
- [x] Operators can view all hypervisor snapshots and trigger manual pruning from the UI.
- [x] All .NET, React, and Go tests pass cleanly.
