# Plan 02: Modular Pipeline Profiles & Flexible Workflow Execution

**Phase:** Phase 2  
**Status:** ✅ Completed  
**Dependencies:** [Plan 01: Service Discovery](file:///home/ckingdon/projects/homelab-manager/docs/plans/01-service-discovery-proxmox-and-k8s.md)

---

## 1. Objectives & Overview

Replace the rigid, hardcoded single upgrade pipeline with a flexible **Pipeline Catalog** offering selectable workflow profiles:
1. **Pipeline Catalog (`IPipelineCatalog`)**:
   * Define distinct, named workflow pipelines with metadata (ID, title, description, compatible target types, and ordered step factories).
2. **Pre-Built Pipeline Profiles**:
   * **`standard-os-upgrade`**: Preflight Checks ➔ Proxmox Snapshot ➔ Package Upgrade ➔ Deterministic Reboot ➔ Await Reconnection ➔ Health Probes.
   * **`k8s-node-rolling-upgrade`**: Preflight Checks ➔ Proxmox Snapshot ➔ K8s Cordon & Drain ➔ Package Upgrade ➔ Deterministic Reboot ➔ Await Reconnection ➔ Health Probes ➔ K8s Uncordon.
   * **`safe-reboot-verify`**: Deterministic Reboot ➔ Await Reconnection ➔ Post-Flight Health Probes.
   * **`preflight-dryrun`**: Preflight Heartbeat ➔ Preflight Disk Headroom ➔ Preflight Package Lock.
   * **`hypervisor-snapshot-only`**: Proxmox Safety Snapshot.
3. **Execution Engine Integration**:
   * Allow `POST /api/v1/jobs` to accept an optional `pipelineId`.
   * Record `pipeline_id` on the `UpdateJob` entity for auditability and frontend visualization.
4. **Interactive "Launch Workflow" UI**:
   * Replace the direct "Run DAG Update" action with an interactive dialog.
   * Display visual step previews of the selected pipeline.
   * Recommend the optimal profile based on host type (e.g. recommending `k8s-node-rolling-upgrade` if the host is a Kubernetes node).

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Features/
    └── Orchestration/
        ├── Pipelines/
        │   ├── PipelineProfile.cs
        │   ├── IPipelineCatalog.cs
        │   └── PipelineCatalog.cs
        ├── JobOrchestratorService.cs (refactor to use catalog)
        └── JobEndpoints.cs (add pipeline endpoints)

src/frontend/src/
├── api/
│   └── pipelines.ts
└── features/
    └── orchestration/
        ├── LaunchWorkflowModal.tsx
        ├── PipelineStepPreview.tsx
        └── usePipelines.ts
```

---

## 3. Implementation Details

### Step 1: Pipeline Catalog & Profiles
* Define `PipelineProfile`:
  * `string Id`
  * `string Name`
  * `string Description`
  * `string Icon`
  * `string[] CompatibleTargetTypes` (e.g., `["all"]`, `["proxmox_vm"]`, `["k8s_node"]`)
  * `Func<IServiceProvider, IEnumerable<IJobStep>> StepFactory`
* Implement `PipelineCatalog : IPipelineCatalog` with the 5 pre-built profiles.

### Step 2: Update Job Entity & DB Migration
* Add `pipeline_id` string column to `update_jobs` table.
* Ensure backward compatibility: default to `standard-os-upgrade` if unspecified.

### Step 3: API Endpoints
* `GET /api/v1/pipelines`: returns all registered pipeline profiles with metadata and step summaries.
* `POST /api/v1/jobs`: accepts `{ "targetHostId": "...", "pipelineId": "k8s-node-rolling-upgrade" }`.

### Step 4: Frontend "Launch Workflow" Modal
* Triggered from Host Table, Host Details, or Workflows View.
* Dropdown or radio selector for available pipeline profiles with visual badge tags.
* Step sequence preview with icons and descriptions.
* "Start Workflow" button to launch and open the live streaming terminal drawer.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Query available pipelines
curl http://localhost:5000/api/v1/pipelines \
  -H "X-ControlPlane-Key: dev-secret-key-123"

# Trigger a specific pipeline (e.g. safe-reboot-verify)
curl -X POST http://localhost:5000/api/v1/jobs \
  -H "Content-Type: application/json" \
  -H "X-ControlPlane-Key: dev-secret-key-123" \
  -d '{"targetHostId": "<host-uuid>", "pipelineId": "safe-reboot-verify"}'
```

### Acceptance Criteria
- [x] Multiple distinct pipeline profiles are registered and queryable via API.
- [x] Jobs execute the exact sequence of steps defined by the chosen pipeline profile.
- [x] UI provides an intuitive "Launch Workflow" modal with step previews and smart recommendation.
- [x] Executed jobs display their pipeline profile in the Workflows history table.
