# Plan 09: One-Click Agent Adoption Workflow (SSH.NET Bootstrapper)

**Target Milestone:** Milestone 2 (Agent Architecture & Real-Time Console)  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 06: Compute Node Agent](file:///home/ckingdon/projects/homelab-manager/docs/plans/06-compute-node-agent.md), [Plan 07: WebSocket Agent Hub](file:///home/ckingdon/projects/homelab-manager/docs/plans/07-websocket-agent-hub.md), [Plan 08: Real-Time Terminal Pipeline](file:///home/ckingdon/projects/homelab-manager/docs/plans/08-realtime-terminal-pipeline.md)  

---

## 1. Objectives & Overview

Eliminate manual server configuration friction through a zero-friction adoption workflow:
1. Provide an **"Adopt Node"** modal in the React UI where the administrator enters an IP/hostname and temporary SSH credentials (password or private key).
2. The backend connects temporarily using `SSH.NET`, inspects CPU architecture (`x86_64` vs `aarch64`), and streams the appropriate Go static agent binary to `/usr/local/bin/controlplane-agent`.
3. The backend generates a unique node token, writes the `systemd` service configuration, enables and starts the service.
4. As soon as the agent establishes its outbound WebSocket connection to `/agent-hub`, the backend immediately terminates and discards the SSH session.
5. The UI shows live step-by-step progress checklist (Connecting SSH -> Checking Arch -> Uploading Binary -> Starting Service -> WebSocket Verified).

---

## 2. Target File Structure

```
src/ControlPlane.Api/
├── Features/
│   └── Adoption/
│       ├── NodeAdoptionEndpoints.cs
│       ├── NodeAdoptionService.cs
│       ├── SshBootstrapper.cs
│       └── NodeAdoptionDtos.cs

src/frontend/src/
└── features/
    └── hosts/
        ├── AdoptNodeModal.tsx
        ├── AdoptionStepProgress.tsx
        └── useAdoptNode.ts
```

---

## 3. Implementation Details

### Step 1: Backend SSH Bootstrapper Service
* Use `SSH.NET` NuGet package.
* Method: `AdoptNodeAsync(AdoptNodeRequest request, CancellationToken ct)`
* Steps:
  1. **Connect & Probe:** `ssh.RunCommand("uname -m")`. Detect `x86_64` vs `aarch64`.
  2. **Transfer Binary:** Use `SftpClient` to upload static binary (`controlplane-agent-linux-amd64` or `arm64`) to `/tmp/controlplane-agent`, then `chmod +x` and move to `/usr/local/bin/controlplane-agent`.
  3. **Write Unit File:** Write `/etc/systemd/system/controlplane-agent.service` populated with the backend's WebSocket URL and generated host token.
  4. **Start Service:** Execute `systemctl daemon-reload && systemctl enable --now controlplane-agent`.
  5. **Await WebSocket Handshake:** Poll `AgentConnectionManager.IsOnline(nodeId)` with a 30s timeout.
  6. **Teardown SSH:** Close and dispose SSH client credentials immediately upon handshake.

### Step 2: Streaming Adoption Progress via SignalR
* Stream granular step events to UI:
  * `SSH_CONNECTING`
  * `ARCH_DETECTED` (e.g. `x86_64`)
  * `BINARY_STREAMING`
  * `SERVICE_STARTING`
  * `HANDSHAKE_VERIFIED`
  * `SSH_DISCONNECTED`

### Step 3: Frontend Adoption Modal
* Form inputs: Host target (or select existing un-adopted host), SSH Port (default 22), Username (default `root`), Auth method (Password vs Private Key).
* Interactive checklist showing animated checkmarks as each step completes.
* Automatically refreshes the host list once adoption succeeds.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Trigger node adoption via REST API
curl -X POST http://localhost:5000/api/v1/hosts/adopt \
  -H "Content-Type: application/json" \
  -H "X-ControlPlane-Key: dev-secret-key-123" \
  -d '{
    "host": "192.168.1.55",
    "sshPort": 22,
    "username": "ubuntu",
    "password": "sample-password"
  }'
```

### Acceptance Criteria
- [ ] SSH connection establishes and correctly identifies host architecture.
- [ ] Appropriate Go agent binary is streamed and placed at `/usr/local/bin/controlplane-agent`.
- [ ] Systemd service starts cleanly and registers an outbound connection to `/agent-hub`.
- [ ] SSH session is cleanly disconnected immediately after handshake.
- [ ] Milestone 2 is fully achieved upon completion of this plan.
