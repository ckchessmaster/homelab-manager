# Plan 17: Proxmox Snapshot 24-Hour Retention & Automated Pruning

**Target Milestone:** Milestone 5 (Production Hardening, Lifecycle Automation & Distribution)  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 11: Proxmox Snapshot and Rollback](file:///home/ckingdon/projects/homelab-manager/docs/plans/11-proxmox-snapshot-and-rollback.md)

---

## 1. Objectives & Overview

Prevent disk bloat on Proxmox hypervisor storage caused by lingering pre-update safety snapshots:
1. Implement a background retention worker in ASP.NET Core (`SnapshotRetentionWorker` implementing `BackgroundService`).
2. Discover and evaluate pre-update snapshots created by ControlPlane (`cp-pre-update-{timestamp}`).
3. Enforce retention window policy (configurable via `Proxmox:SnapshotRetentionHours`, default 24 hours):
   * When snapshot age exceeds retention window, call `IProxmoxClient.DeleteVmSnapshotAsync`.
   * Log deletion progress and outcome to system audit log.
4. Expose REST endpoints and UI controls for operators to inspect snapshot ages and manually purge pre-update snapshots on demand.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Features/
    └── Adapters/
        └── Proxmox/
            ├── SnapshotRetentionWorker.cs
            ├── SnapshotRetentionOptions.cs
            └── SnapshotRetentionEndpoints.cs

src/frontend/src/
└── features/
    └── adapters/
        └── ProxmoxSnapshotManager.tsx
```

---

## 3. Implementation Details

### Step 1: Retention Configuration & Background Service
* Define `SnapshotRetentionOptions`:
  * `RetentionHours` (default: 24)
  * `ScanIntervalMinutes` (default: 60)
  * `Enabled` (default: true)
* Implement `SnapshotRetentionWorker : BackgroundService`:
  * Every interval, query managed hosts with linked Proxmox VMs/LXCs.
  * Inspect existing snapshots matching prefix `cp-pre-update-`.
  * Parse UTC timestamp from snapshot name or query job records.
  * Trigger `DeleteVmSnapshotAsync` for expired snapshots and poll UPID until completed.

### Step 2: REST Management Endpoints
* `GET /api/v1/adapters/proxmox/snapshots?hostId={hostId}`: list current snapshots with age and retention status.
* `DELETE /api/v1/adapters/proxmox/snapshots`: immediately purge a specific snapshot or all expired snapshots for a host.

### Step 3: Frontend UI Integration
* Display active pre-update safety snapshots in the Host details modal and Proxmox adapter view.
* Provide an immediate "Prune Snapshot" button with confirmation dialog.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Query active Proxmox snapshots for a host
curl http://localhost:5000/api/v1/adapters/proxmox/snapshots?hostId=<host-uuid> \
  -H "X-ControlPlane-Key: dev-secret-key-123"

# Manually trigger snapshot deletion
curl -X DELETE http://localhost:5000/api/v1/adapters/proxmox/snapshots \
  -H "Content-Type: application/json" \
  -H "X-ControlPlane-Key: dev-secret-key-123" \
  -d '{"hostId": "<host-uuid>", "snapshotName": "cp-pre-update-20260901T120000Z"}'
```

### Acceptance Criteria
- [ ] Background worker periodically evaluates Proxmox snapshots without impacting live update jobs.
- [ ] Snapshots older than configured retention window (24h) are pruned cleanly using Proxmox REST API.
- [ ] Operators can view snapshot age and manually trigger snapshot cleanup from the web dashboard.
