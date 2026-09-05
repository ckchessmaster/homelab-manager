# Plan 12: Deterministic Reboot Protocol & Post-Flight Health Probes

**Target Milestone:** Milestone 3 (Update Orchestration Engine & Standby Runner)  
**Status:** ✅ Completed  
**Dependencies:** [Plan 10: DAG Orchestration Engine](file:///home/ckingdon/projects/homelab-manager/docs/plans/10-dag-orchestration-engine.md)  

---

## 1. Objectives & Overview

Solve the classic Ansible reboot failure mode (dropped SSH connections leaving playbooks hung indefinitely):
1. Implement a **Deterministic Reboot Handshake**:
   * Backend issues reboot envelope to the Go agent.
   * Agent replies with `REBOOT_COMMENCING`, flushes all buffered output frames, and triggers the OS reboot.
   * Backend transitions the job to `AwaitingReconnect` state.
2. The Go agent reconnects upon system startup, sends an initial greeting containing the new running kernel version, and transitions the state machine forward.
3. Execute **Post-Flight Health Probes**:
   * Query agent for any failed `systemd` units (`systemctl --failed`).
   * Perform synthetic HTTP/TCP status probes on configured service endpoints.
4. If probes pass within the timeout window (default 300s), mark the job `Completed`. If timeout expires, trigger the rollback sequence.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Features/
    └── Orchestration/
        └── Steps/
            ├── DeterministicRebootStep.cs
            ├── AwaitReconnectionStep.cs
            └── PostFlightHealthProbeStep.cs

src/agent/
└── internal/
    └── lifecycle/
        └── reboot.go
```

---

## 3. Implementation Details

### Step 1: Agent Reboot Routine (Go)
* When receiving `CMD_REBOOT`:
  * Log: `"Reboot signal received. Flushing buffers..."`
  * Send message type: `REBOOT_COMMENCING` with current timestamp.
  * Invoke `exec.Command("systemctl", "reboot").Run()` (or Windows equivalent).
  * Agent process terminates cleanly.

### Step 2: Backend Reconnection Watcher
* In `AwaitReconnectionStep`:
  * Register a completion source with `AgentConnectionManager` for the target `HostId`.
  * Wait up to 300 seconds for a new WebSocket handshake from the host.
  * When the agent reconnects:
    * Compare pre-reboot kernel with new kernel version.
    * Log `"Node back online. Kernel updated to: {newKernel}"`.
    * Complete step successfully.

### Step 3: Post-Flight Health Verification Step
1. **Systemd Service Inspection:**
   * Agent runs `systemctl --failed --no-legend` and returns any failed unit names.
   * If any failed units are detected, log warnings or fail the probe depending on policy.
2. **Synthetic Probes:**
   * Execute TCP ping or HTTP GET on configured probe URLs (e.g. `http://192.168.1.50:6443/healthz` for K8s API, or app health endpoints).
   * Confirm HTTP 200 OK.
3. If all probes pass, job status transitions to `Completed`.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Trigger reboot test against connected test VM
curl -X POST http://localhost:5000/api/v1/debug/test-reboot \
  -H "X-ControlPlane-Key: dev-secret-key-123" \
  -d '{"hostId": "<host-uuid>"}'
```

### Acceptance Criteria
- [x] Agent emits `REBOOT_COMMENCING` before terminating.
- [x] Backend detects connection drop as expected reboot, not an unexpected failure.
- [x] Post-boot agent reconnection is automatically matched to the in-flight job.
- [x] Health probes correctly verify systemd status and endpoint reachability before marking the job `Completed`.
