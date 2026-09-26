---
name: controlplane
description: >-
  Operational, diagnostic, and troubleshooting skill for the Homelab Orchestration & Management Plane (ControlPlane).
  USE FOR: querying ControlPlane MCP tools, inspecting infrastructure adapters (Proxmox, Kubernetes, UniFi, OPNsense, Redfish/iDRAC),
  orchestrating DAG update pipelines, diagnosing node agents, managing Kubernetes workloads, Standby mode takeover,
  and streaming real-time console logs.
license: Apache-2.0
metadata:
  author: ControlPlane
  version: "1.3.1"
---

# ControlPlane Operations & Architecture Skill

Use this skill when interacting with, maintaining, or extending the **Homelab Orchestration & Management Plane (ControlPlane)**.

---

## 1. Quick Reference: Architecture & Topologies

| Mode | Database | Orchestrator | Primary Role |
| :--- | :--- | :--- | :--- |
| **Cluster Mode** | PostgreSQL (in-cluster) | ASP.NET Core API (`ControlPlane.Api`) | Normal operations, multi-user UI, background workers, Zitadel OIDC RBAC. |
| **Standby Runner Mode** | SQLite (`standby-state.db`) | Single-binary CLI (`ControlPlane.Cli`) | Autonomous out-of-band takeover when Kubernetes control plane or nodes are rebooting. |

### Topology Invariants
* **Compute Nodes:** Static Go agent daemon (`controlplane-agent`) dials **outbound** via WebSocket (`wss://`). No listening ports!
* **Appliances:** Managed **agentless** via HTTPS REST/API/Redfish using pooled HTTP clients and AES-256 encrypted credentials.
* **Logs:** Real-time console logs use sequence IDs `(job_id, sequence_id, stream_type, log_line, timestamp)` streamed via SignalR to `xterm.js`.
* **System Observability:** In-memory circular buffer captures backend logs for real-time querying (`query_system_logs`).

---

## 2. Model Context Protocol (MCP) Tool Workflows

When pair-programming or operating as an AI agent, the ControlPlane MCP server (`http://localhost:5029/mcp`) provides 40+ operational tools:

### Fleet Diagnosis Workflow
1. **List Inventory:** Call `list_hosts` to inspect compute node online status, pending reboot flags, and upgradable package counts.
2. **Inspect Host:** Call `get_host_details(hostId: "...")` to inspect target hypervisor binding, active jobs, and agent telemetry.
3. **Execute Debug Command:** Call `execute_debug_command(hostId: "...", command: "uptime", args: ["..."])` to run ad-hoc diagnostics over the outbound WebSocket.
4. **Stream Output:** Call `query_job_logs(jobId: "...", fromSequenceId: 0)` to read console output.

### System & Backend Logging Workflow
1. **Query Backend Logs:** Call `query_system_logs(level: "Error", search: "failed", limit: 50)` to inspect live server logs from the circular buffer.
2. **Clear Log Buffer:** Call `clear_system_logs()` when resetting diagnostic state.

### Infrastructure Adapters & Smart Home Workflows
1. **Summarize Adapters:** Call `list_adapters` to view all configured Proxmox, Kubernetes, UniFi, OPNsense, Redfish, and Home Assistant instances.
2. **Test Connectivity:** Call `test_adapter_connection(adapterType: "...", instanceId: "...")` to verify latency and API status.
3. **Inspect Hardware Sensors:** Call `get_hardware_sensors` to query BMC CPU/system temperatures, fan speeds, and power draw.
4. **Inspect Home Assistant:** Call `get_home_assistant_overview(instanceId: "...")` to inspect entities, core configuration, and updates.

### Kubernetes Workload & Helm Catalog Operations
1. **Query Workloads:** Call `list_workloads(clusterId: "...", namespaceName: "...")` to check replica status, available replicas, and image tags.
2. **Rolling Restart:** Call `restart_workload(clusterId: "...", namespaceName: "...", name: "...")` to dispatch rolling rollout restart.
3. **Scale Replicas:** Call `scale_workload(clusterId: "...", namespaceName: "...", name: "...", replicas: N)` to resize capacity.
4. **Helm Releases:** Call `list_helm_releases` and `check_helm_updates` to inspect and manage Helm chart deployments.

---

## 3. Maintenance & Update Pipeline Workflows

1. **List Pipeline Profiles:** Call `list_pipelines` to check available DAG definitions (e.g. `linux-standard-upgrade`, `proxmox-pve-upgrade`).
2. **Trigger Job:** Call `trigger_upgrade_job(hostId: "...", pipelineId: "...")`.
3. **DAG Progression:**
   * `Pending` -> Pre-flight health checks (disk space, network reachability).
   * `Running` -> Proxmox snapshot creation (if VM) -> Package upgrade dispatch (`apt update && apt upgrade -y`).
   * `Verifying` -> Post-upgrade checks, agent reconnect verification, reboot if required.
   * `Completed` or `RolledBack` (on failure, rolls back to snapshot).

---

## 4. Standby Runner Takeover Protocol

When performing maintenance on the node hosting the Kubernetes control plane or database:

1. **Pre-flight Snapshot:** Export snapshot from cluster:
   ```bash
   dotnet run --project src/ControlPlane.Cli -- export-state --output /tmp/cluster-state.json
   ```
2. **Acquire Lease:**
   ```bash
   dotnet run --project src/ControlPlane.Cli -- takeover --lease-duration 60m --state /tmp/cluster-state.json
   ```
3. **Autonomous Execution:** Standby CLI uses local SQLite to run jobs while Kubernetes is down.
4. **Cluster Recovery & Delta Sync:**
   ```bash
   dotnet run --project src/ControlPlane.Cli -- release-lease --sync-deltas
   ```

---

## 5. Development & Testing Commands

```bash
# Build the entire backend
dotnet build src/ControlPlane.Api/ControlPlane.Api.csproj

# Run all automated tests (330+ passing)
dotnet test --filter "FullyQualifiedName!~PostgresStorageTests"

# Run MCP server tests specifically
dotnet test --filter FullyQualifiedName~McpServerTests

# Run Frontend Playwright E2E tests
cd src/frontend && npx playwright test
```
