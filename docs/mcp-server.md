# Model Context Protocol (MCP) Server

ControlPlane includes a built-in Model Context Protocol (MCP) server that enables AI pair programmers (like Antigravity) to query live logs, inspect managed hosts, trigger upgrade jobs, and interact directly with the ControlPlane orchestration plane.

---

## 1. Enabling the MCP Server

To prevent unintended exposure in production environments, the MCP server is guarded by an environment parameter:

* **Environment Variable:** `ENABLE_MCP_SERVER=true`
* **Aspire AppHost Parameter:** `enable-mcp-server=true`

### In Development (Aspire AppHost)
In `src/Aspire/ControlPlane.AppHost/AppHost.cs`, the parameter is enabled by default:
```csharp
var enableMcpServer = builder.AddParameter("enable-mcp-server", "true");
api.WithEnvironment("ENABLE_MCP_SERVER", enableMcpServer);
```

### In Production / Standalone
Set the environment variable when launching `ControlPlane.Api`:
```bash
ENABLE_MCP_SERVER=false dotnet run --project src/ControlPlane.Api
```
When disabled (`false` or unset), all MCP server services, endpoints, and tool handlers are omitted from the ASP.NET Core service container and request pipeline.

---

## 2. Server Transport & Endpoint

* **Transport:** Streamable HTTP (JSON-RPC over HTTP with SSE streaming support)
* **Endpoint URL:** `http://localhost:5029/mcp`
* **Authentication:** Matches standard ControlPlane API credentials or local dev bypass.

---

## 3. Client Configuration

### Antigravity IDE Integration
The workspace is pre-configured via `.agents/mcp_config.json`:
```json
{
  "mcpServers": {
    "controlplane": {
      "serverUrl": "http://localhost:5029/mcp"
    }
  }
}
```

### Other MCP Clients (e.g. Claude Desktop, Cursor)
```json
{
  "mcpServers": {
    "controlplane": {
      "serverUrl": "http://localhost:5029/mcp"
    }
  }
}
```

---

## 4. Available MCP Tools

### Host Management & Discovery
| Tool Name | Category | Description |
| :--- | :--- | :--- |
| `list_hosts` | Inventory | Lists managed hosts with IP address, agent state (online, installed, pending reboot, updates count). |
| `get_host_details` | Inventory | Returns comprehensive host details, active jobs, Proxmox VM/node, and iDRAC targets. |
| `scan_discovery` | Discovery | Scans infrastructure (Proxmox VE, Kubernetes, UniFi, OPNsense) for unmanaged compute candidates. |
| `import_candidate_host` | Discovery | Imports a discovered candidate host into managed inventory with DNS/IP resolution. |

### Jobs, Logs & Shell Execution
| Tool Name | Category | Description |
| :--- | :--- | :--- |
| `list_pipelines` | Orchestration | Lists all modular upgrade and maintenance pipeline profiles in the catalog. |
| `trigger_upgrade_job` | Orchestration | Starts an upgrade workflow on a target host using a selected pipeline profile. |
| `list_jobs` | Jobs | Lists recent update jobs across the fleet with optional status or target host filtering. |
| `get_job` | Jobs | Retrieves current state, active step, failure reason, and execution timing for an update job. |
| `query_job_logs` | Logs | Queries sequence-ordered console logs (stdout, stderr, system) for an update or debug job with pagination (`fromSequenceId`, `limit`). |
| `execute_debug_command` | Debug | Dispatches an ad-hoc shell command (e.g. `uptime`, `df -h`) to an online agent and streams output. |

### Infrastructure Adapters & Network (Phase 5)
| Tool Name | Category | Description |
| :--- | :--- | :--- |
| `list_adapters` | Adapters | Summarizes configured adapters across Proxmox, Kubernetes, UniFi, OPNsense, and iDRAC with instance counts and health. |
| `test_adapter_connection` | Adapters | Tests connectivity to a specific adapter instance and returns latency, detected version, and node count. |
| `list_unifi_devices` | Network | Lists UniFi network devices (switches, APs, gateways) with port PoE status and power consumption. |
| `power_cycle_unifi_port` | Network | Power-cycles a PoE port on a UniFi switch to reboot a connected device (camera, AP, Pi). |
| `get_opnsense_status` | Firewall | Queries OPNsense firewall gateway status, WAN/LAN health, firmware version, and active DHCP leases. |

### Kubernetes Workloads (Phase 5)
| Tool Name | Category | Description |
| :--- | :--- | :--- |
| `list_workloads` | Workloads | Queries aggregated Kubernetes workloads (Deployments, StatefulSets, DaemonSets) across clusters with replica counts. |
| `restart_workload` | Workloads | Triggers a rolling rollout restart of a Kubernetes deployment. |
| `scale_workload` | Workloads | Adjusts the desired replica count for a Kubernetes deployment. |
| `list_helm_releases` | Helm | Queries deployed Helm releases in a Kubernetes cluster with revision, chart version, and health status. |
| `get_helm_catalog` | Helm | Returns the curated catalog of pre-configured homelab charts (Ingress, cert-manager, Longhorn, Pi-hole, etc.). |
| `install_helm_chart` | Helm | Deploys or upgrades a Helm chart onto a target cluster with optional repository URL and values YAML. |
| `uninstall_helm_release` | Helm | Uninstalls and removes a deployed Helm release from a target cluster namespace. |

### Hardware & BMC Management (Out-of-Band Redfish & In-Band Host IPMI)
| Tool Name | Category | Description |
| :--- | :--- | :--- |
| `get_hardware_sensors` | BMC / Hardware | Queries Dell iDRAC / DMTF Redfish power state, temperature sensors, and fan telemetry via direct BMC IP or in-band host agent IPMI (`hostId`). |
| `execute_hardware_power_action` | BMC / Hardware | Dispatches hardware power actions (`On`, `GracefulShutdown`, `ForceRestart`, `PowerCycle`, `ForceOff`) via out-of-band BMC or in-band baremetal host agent (`hostId`). |

---

## 5. AI Agent Operational Guidelines & Best Practices

1. **Investigation Before Mutation:**
   * Always inspect current system or host state before triggering destructive actions.
   * Check `get_host_details` and verify that the target agent `isOnline: true` before executing commands or upgrade jobs.
2. **Safe Staged Operations:**
   * When modifying Kubernetes workloads or rebooting nodes, verify workload health first via `list_workloads`.
   * For hardware power cycling, verify sensor health via `get_hardware_sensors` first.
3. **Monotonic Sequence Log Tracking:**
   * After launching a job (`trigger_upgrade_job` or `execute_debug_command`), track execution by polling `query_job_logs` with `fromSequenceId` set to the last received sequence number to avoid duplicate processing.
4. **Audit Trail:**
   * All actions dispatched via MCP tools are recorded in `update_jobs` with `InitiatedBy = "AI Agent via MCP"`.

