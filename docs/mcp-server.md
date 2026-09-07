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

| Tool Name | Category | Description |
| :--- | :--- | :--- |
| `query_job_logs` | Logs | Queries sequence-ordered console logs (stdout, stderr, system) for an update or debug job, with pagination support (`fromSequenceId`, `limit`). |
| `get_job` | Jobs | Retrieves current state, active step, failure reason, and execution timing for an update job. |
| `list_jobs` | Jobs | Lists recent update jobs across the fleet with optional status or target host filtering. |
| `list_hosts` | Inventory | Lists managed hosts with IP address, agent state (online, installed, pending reboot, updates count). |
| `get_host_details` | Inventory | Returns comprehensive host details, active jobs, Proxmox VM/node, and iDRAC targets. |
| `scan_discovery` | Discovery | Scans infrastructure (Proxmox VE hypervisors and Kubernetes nodes) for unmanaged compute candidates. |
| `import_candidate_host` | Discovery | Imports a discovered candidate host into managed inventory with DNS/IP resolution. |
| `list_pipelines` | Orchestration | Lists all modular upgrade and maintenance pipeline profiles in the catalog. |
| `trigger_upgrade_job` | Orchestration | Starts an upgrade workflow on a target host using a selected pipeline profile. |
| `execute_debug_command` | Debug | Dispatches an ad-hoc shell command (e.g. `uptime`, `df -h`) to an online agent and streams output. |
