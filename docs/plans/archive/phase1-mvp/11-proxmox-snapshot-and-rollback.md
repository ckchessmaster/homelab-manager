# Plan 11: Hypervisor Safety Snapshots & Automated Rollback (Proxmox VE)

**Target Milestone:** Milestone 3 (Update Orchestration Engine & Standby Runner)  
**Status:** ✅ Completed  
**Dependencies:** [Plan 10: DAG Orchestration Engine](file:///home/ckingdon/projects/homelab-manager/docs/plans/10-dag-orchestration-engine.md)  

---

## 1. Objectives & Overview

Protect virtualized infrastructure by integrating pre-update hypervisor snapshots and automated rollbacks:
1. Implement a robust Proxmox VE REST client interacting with `/api2/json`.
2. Before any package upgrade begins on a virtualized host (`proxmox_vm` or `proxmox_lxc`), trigger a safety snapshot: `cp-pre-update-{timestamp}`.
3. Track snapshot creation progress and persist the snapshot identifier in `update_jobs.snapshot_identifier`.
4. Implement automated rollback: if subsequent steps fail or post-boot health probes timeout (> 300 seconds), call Proxmox to rollback the VM/container to the pre-update snapshot.
5. On successful update completion, retain snapshot with a 24-hour expiration lifecycle.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
├── Features/
│   ├── Adapters/
│   │   └── Proxmox/
│   │       ├── IProxmoxClient.cs
│   │       ├── ProxmoxClient.cs
│   │       ├── ProxmoxModels.cs
│   │       └── ProxmoxTaskPoller.cs
│   └── Orchestration/
│       └── Steps/
│           ├── ProxmoxSnapshotStep.cs
│           └── ProxmoxRollbackStep.cs
```

---

## 3. Implementation Details

### Step 1: Proxmox REST API Client
* Base URL: `https://<proxmox-host>:8006/api2/json`
* Authentication: API Token via `Authorization: PVEAPIToken=USER@REALM!TOKENID=UUID`.
* Core methods:
  * `CreateVmSnapshotAsync(node, vmid, snapName, description, ct)`: `POST /nodes/{node}/qemu/{vmid}/snapshot`
  * `RollbackVmSnapshotAsync(node, vmid, snapName, ct)`: `POST /nodes/{node}/qemu/{vmid}/snapshot/{snapname}/rollback`
  * `DeleteVmSnapshotAsync(node, vmid, snapName, ct)`: `DELETE /nodes/{node}/qemu/{vmid}/snapshot/{snapname}`
  * `PollTaskCompletionAsync(node, upid, timeout, ct)`: Polls `/nodes/{node}/tasks/{upid}/status` until `status == "stopped"`.

### Step 2: Proxmox Snapshot Step in the DAG
* Step checks if target host has `TargetType == "proxmox_vm"` or `"proxmox_lxc"` and valid `ProxmoxNode` & `ProxmoxVmid`.
* Generates snapshot tag: `cp-pre-update-yyyyMMddHHmmss`.
* Invokes snapshot API and polls task until completion.
* Writes snapshot name into `context.Job.SnapshotIdentifier`.

### Step 3: Automated Rollback Handler
* Triggered if any subsequent step throws an unhandled error, package install fails with an error exit code, or post-reboot health verification times out.
* Logs critical alert to `step_logs`: `"[ROLLBACK] Initiating automated hypervisor rollback to snapshot {snapName}..."`.
* Invokes Proxmox rollback API, polls for completion, and sets job status to `RolledBack`.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Unit & integration tests against mock Proxmox server
dotnet test --filter Category=ProxmoxIntegration
```

### Acceptance Criteria
- [x] Snapshot is created on Proxmox VE prior to executing any destructive update command.
- [x] Snapshot identifier is recorded in `update_jobs` table.
- [x] Simulating an update failure triggers an automated rollback to the exact pre-update snapshot.
- [x] Task completion polling respects timeout limits and avoids hanging threads.
