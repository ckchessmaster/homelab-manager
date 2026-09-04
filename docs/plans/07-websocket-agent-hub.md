# Plan 07: Outbound WebSocket Agent Hub & Heartbeat Ingestion

**Target Milestone:** Milestone 2 (Agent Architecture & Real-Time Console)  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 04: Host Inventory API](file:///home/ckingdon/projects/homelab-manager/docs/plans/04-host-inventory-api.md), [Plan 06: Compute Node Agent](file:///home/ckingdon/projects/homelab-manager/docs/plans/06-compute-node-agent.md)  

---

## 1. Objectives & Overview

Build the server-side communication hub for compute node agents:
1. Provide an ASP.NET Core WebSocket endpoint (`/agent-hub`) handling persistent outbound connections from `controlplane-agent` daemons.
2. Authenticate agents using a pre-shared or host-specific node token.
3. Ingest heartbeats every 10 seconds, updating database fields: `agent_last_seen_at`, `agent_installed = true`, `agent_version`, `pending_reboot`, and `upgradable_packages_count`.
4. Maintain an active in-memory connection registry linking connected `nodeId` to active WebSocket sessions.
5. Implement a background liveness monitor marking hosts as `Offline` when heartbeats cease for > 30 seconds.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Features/
    └── Agents/
        ├── AgentHubMiddleware.cs
        ├── AgentConnectionManager.cs
        ├── AgentHeartbeatHandler.cs
        ├── AgentLivenessBackgroundService.cs
        └── Models/
            ├── AgentHeartbeatMessage.cs
            └── AgentCommandEnvelope.cs
```

---

## 3. Implementation Details

### Step 1: WebSocket Middleware & Route
* Map `/agent-hub` endpoint in ASP.NET Core pipeline:
  ```csharp
  app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
  app.Map("/agent-hub", async (HttpContext context, AgentHubMiddleware hub) => {
      await hub.HandleConnectionAsync(context);
  });
  ```
* Validate authorization token passed in header or query parameter (`Authorization: Bearer <node-token>`).

### Step 2: In-Memory Connection Manager
* `AgentConnectionManager`: thread-safe dictionary mapping `Guid HostId` / `nodeId` to `(WebSocket socket, DateTime connectedAt)`.
* Provides methods:
  * `Register(nodeId, socket)`
  * `Unregister(nodeId)`
  * `SendCommandAsync(nodeId, commandPayload, cancellationToken)`
  * `IsOnline(nodeId)`

### Step 3: Heartbeat Ingestion & DB Persistence
* Process incoming JSON messages:
  ```json
  {
    "nodeId": "f78d92a1-0943-4e63-8a41-8c4391d0f5e1",
    "hostname": "k8s-worker-02",
    "kernelVersion": "6.8.8-2-pve",
    "pendingReboot": true,
    "packageManager": "apt",
    "metrics": { "cpuUsagePct": 14.2, "memoryUsagePct": 62.8, "diskFreePct": 38.5 },
    "packageSummary": { "upgradableCount": 4, "securityCount": 1 }
  }
  ```
* Update corresponding `Host` record with `agent_last_seen_at = DateTimeOffset.UtcNow`, `pending_reboot`, and package counts.

### Step 4: Liveness Background Worker
* Hosted service running every 15 seconds:
  * Queries database for hosts where `agent_last_seen_at < UtcNow - 30 seconds` and `agent_installed == true`.
  * Triggers real-time status update to frontend indicating the node is offline.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Start backend API
dotnet run --project src/ControlPlane.Api

# Run Go agent connected to backend
./src/agent/dist/controlplane-agent-linux-amd64 --hub-url ws://localhost:5000/agent-hub --token test-node-token
```

### Acceptance Criteria
- [ ] Agent successfully establishes WebSocket handshake with `/agent-hub`.
- [ ] Every 10 seconds, backend receives heartbeat and updates `hosts` table.
- [ ] Host status in UI changes from "Offline" to "Active" with CPU, memory, and reboot status displayed.
- [ ] Terminating the agent triggers "Offline" state within 30 seconds.
