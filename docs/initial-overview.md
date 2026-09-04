# Technical Design Document: Homelab Orchestration & Management Plane (ControlPlane)

**Status:** Proposed  
**Author:** Chris Kingdon  
**Target Platform:** Kubernetes (In-Cluster) / Workstation CLI (Standby)  
**Primary Stack:** .NET 9 Aspire, C#, React 19, TypeScript, PostgreSQL, SQLite, SignalR  

---

## 1. Executive Summary & Objectives

### 1.1 Problem Statement
Operating a heterogeneous homelab spanning bare-metal servers (Dell PowerEdge with iDRAC), virtual hypervisors (Proxmox VE), container platforms (Kubernetes), and diverse operating systems (Debian/Ubuntu, RHEL/Rocky, Windows Server) presents acute lifecycle management challenges. 

Traditional configuration and orchestration tooling—notably **Ansible**—proves brittle in homelab environments due to:
1. **Push-and-Wait Brittleness:** Dropped SSH/WinRM connections during network resets or kernel reboots cause playbooks to crash or hang indefinitely with orphaned locks (`/var/lib/dpkg/lock`).
2. **Circular Dependency ("Chicken-and-Egg" Upgrades):** Hosting the management plane inside the primary Kubernetes cluster prevents the cluster itself from being reliably upgraded, as draining and rebooting control-plane or worker nodes kills the orchestrator mid-flight.
3. **Siloed Visibility:** Hypervisor snapshots, physical BMC controls (iDRAC), network switch PoE controls (Ubiquiti UniFi), and OS package states are fragmented across separate dashboards, preventing unified orchestration and automated rollback.

### 1.2 System Goals
* **Single Pane of Glass:** A unified Backend-for-Frontend (BFF) and responsive web dashboard displaying health, reboot status, pending security patches, and hardware vitals.
* **Deterministic, Resilient Updates:** Replace procedural shell execution with an asynchronous Directed Acyclic Graph (DAG) state machine featuring real-time terminal streaming, pre-flight safety snapshots, deterministic reboot tracking, and automated health-check rollbacks.
* **Autonomous Standby Takeover:** Provide a zero-dependency, single-binary CLI runner capable of taking over orchestration from an admin workstation when the hosting Kubernetes cluster is undergoing maintenance.
* **Hybrid Communication Architecture:** Combine direct REST/Redfish APIs for infrastructure appliances with an outbound-only lightweight agent for compute nodes, eliminating inbound listening ports and brittle SSH timeouts.

### 1.3 Non-Goals (Phase 1)
* Comprehensive continuous configuration management (e.g., replacing declarative dotfiles or Terraform infrastructure provisioning).
* Direct public Internet exposure (application resides strictly on internal VLANs/VPN).
* Full multi-tenant isolation (designed for single-administrator or small team homelab operations).

---

## 2. High-Level Architecture & Topology

The platform adopts a **Backend-for-Frontend (BFF)** pattern powered by **.NET 9 Aspire** and a modern **React + TypeScript** single-page application. The architecture is explicitly designed to operate under two interchangeable runtime topologies using the exact same underlying codebase.

```
                      +-------------------------------------------------------------+
                      |                   WEB BROWSER / CLIENT                      |
                      |          (React 19 + TypeScript + Vite + Tailwind)          |
                      +------------------------------+------------------------------+
                                                     |
                                   HTTPS / WSS (REST + SignalR)
                                                     |
                     +-------------------------------+-------------------------------+
                     |                                                               |
                     v                                                               v
   +------------------------------------+                         +------------------------------------+
   |   CLUSTER MODE (PRIMARY ASPIRANT)  |                         |    STANDBY RUNNER (LOCAL CLI)      |
   | Hosted in Kubernetes via Ingress   |                         | Single Binary: ./controlplane-cli  |
   | Mode: Full Cluster Orchestrator    |                         | Mode: Takeover / Maintenance       |
   +-----------------+------------------+                         +------------------+-----------------+
                     |                                                               |
                     | (Normal DB Access)                           (Delta Sync)     | (Local Offline DB)
                     v                                              <----------------+                 v
     +-------------------------------+                                               +-----------------+
     |  Kubernetes PostgreSQL DB     |                                               |  Local SQLite   |
     +-------------------------------+                                               +-----------------+
                     |                                                                                 |
                     +-------------------------------+-------------------------------------------------+
                                                     |
                     +-------------------------------+-------------------------------+
                     |                               |                               |
                     v                               v                               v
        +-------------------------+     +-------------------------+     +-------------------------+
        |     COMPUTE NODES       |     |   HYPERVISOR / NETWORK  |     |   BARE-METAL SERVERS    |
        |  (Linux & Windows Hosts)|     |   (Proxmox VE & UniFi)  |     |   (Dell PowerEdge R-IDs)|
        +-------------------------+     +-------------------------+     +-------------------------+
        | * ControlPlane Agent    |     | * Proxmox VE REST API   |     | * Dell iDRAC / Redfish  |
        |   (Outbound WSS/gRPC)   |     | * UniFi Controller API  |     |   REST API (Out-of-band)|
        +-------------------------+     +-------------------------+     +-------------------------+
```

### 2.1 Core Architectural Components

| Component | Technology | Responsibility |
| :--- | :--- | :--- |
| **Frontend (UI)** | React 19, TypeScript, Vite, Tailwind CSS, shadcn/ui, TanStack Query | Responsive dashboard, host status metrics, node adoption modal, DAG workflow designer, and `xterm.js` log stream. |
| **Backend for Frontend** | .NET 9 ASP.NET Core, .NET Aspire | Orchestration engine, API gateway, SignalR log hubs, Proxmox/iDRAC/UniFi adapters, EF Core persistence. |
| **Compute Node Agent** | .NET Native AOT or Go (<15MB static binary) | Daemon running on managed OS instances; executes package updates, monitors systemd/services, streams stdout/stderr. |
| **Primary Storage** | PostgreSQL 16+ (in-cluster) | Persistent store for inventory, credentials, job history, and step logs during normal operations. |
| **Standby Storage** | SQLite 3 (local file) | Autonomous zero-dependency storage utilized exclusively by the Standby Workstation CLI runner. |

---

## 3. Storage Architecture & Standby Synchronization

To resolve the chicken-and-egg dilemma when patching the Kubernetes cluster that hosts the application and database, ControlPlane implements a **Lease & Delta Synchronization Protocol**.

```
PostgreSQL (In-Cluster)                                          SQLite (Workstation CLI)
+-------------------+                                            +---------------------+
| Current Inventory |  ==== 1. Pre-Flight Snapshot Pull =====>   | Seeded Inventory    |
| Host Credentials  |                                            | Staged Job Plan     |
| Distributed Lock  |  <=== 2. Acquire Maintenance Lease ====    | (Active Execution)  |
+-------------------+                                            +---------------------+
          |                                                                 |
    [K8S GOES DOWN]                                                   [NODE REBOOTS]
          |                                                                 |
+-------------------+                                            +---------------------+
| K8s Pod Suspended |                                            | Job Results / Logs  |
| (Read-Only State) |  <=== 3. Post-Flight Delta Push =======    | Node Metrics        |
| Release Lease     |                                            | Updated State       |
+-------------------+                                            +---------------------+
```

### 3.1 EF Core Multi-Provider Abstraction
The data layer utilizes Entity Framework Core with unified entity models. The database provider is determined conditionally at service configuration:

```csharp
// Infrastructure/DependencyInjection.cs
public static IServiceCollection AddControlPlaneStorage(this IServiceCollection services, IConfiguration config)
{
    var isStandby = config.GetValue<bool>("STANDBY_MODE", false);

    services.AddDbContext<ControlPlaneDbContext>(options =>
    {
        if (isStandby)
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                ".controlplane", 
                "standby-state.db"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            options.UseSqlite($"Data Source={dbPath}");
        }
        else
        {
            options.UseNpgsql(config.GetConnectionString("PostgresDatabase"), npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), null);
            });
        }
    });

    return services;
}
```

### 3.2 Takeover Protocol Step-by-Step
1. **Invocation:** The administrator initiates maintenance from their workstation:
   ```bash
   controlplane serve --takeover --cluster-url https://controlplane.homelab.local
   ```
2. **Snapshot Export:** The CLI authenticates and requests an idempotent JSON export payload containing:
   * Target inventory (`hosts` table)
   * Stored credentials & API tokens (decrypted and re-encrypted with temporary session key)
   * Target update profiles and DAG definitions
3. **Local SQLite Seeding:** The CLI writes the snapshot into local `standby-state.db` using EF Core `Database.EnsureCreated()`.
4. **Maintenance Lease Acquisition:** The CLI issues an HTTP POST to `/api/v1/cluster/lease-acquire`. The in-cluster instance inserts a distributed lock into PostgreSQL:
   * Key: `GLOBAL_MAINTENANCE_LOCK`
   * Holder: `WORKSTATION_STANDBY_RUNNER`
   * Lease Duration: 60 minutes (with automated keep-alive heartbeats).
5. **In-Cluster Suspension:** The in-cluster Pod switches to read-only pass-through mode, disabling local job scheduling.
6. **Execution Under Failure:** As the workstation runner cordons, drains, patches, and reboots Kubernetes nodes, PostgreSQL and in-cluster Pods go offline. The workstation runner continues orchestrating via local SQLite without interruption.
7. **Reconciliation (Delta Sync):** Once the cluster nodes reboot and PostgreSQL recovers:
   * Workstation runner detects PostgreSQL readiness via TCP health probe.
   * Workstation runner pushes delta execution records: new job logs, updated host package versions, and audit logs.
   * Workstation releases the `GLOBAL_MAINTENANCE_LOCK`.
   * In-cluster instance resumes primary duty.

---

## 4. Communication & Node Execution Model

### 4.1 The Pragmatic Hybrid Model
To maximize reliability and avoid the failure modes of pure agentless architectures, ControlPlane divides targets into two distinct communication categories:

| Target Category | Examples | Chosen Model | Protocol | Justification |
| :--- | :--- | :--- | :--- | :--- |
| **Compute Nodes** | Ubuntu VMs, Debian LXCs, Rocky Linux, Windows Server | **Lightweight Agent** | Outbound TLS / WebSocket (`wss://`) | Immune to SSH timeouts during reboots; no inbound ports; real-time log streaming. |
| **Hypervisors** | Proxmox VE Nodes & Clusters | **Agentless** | HTTPS REST API (`/api2/json`) | Direct programmatic control over VM/LXC power, disk snapshots, and rollback. |
| **Bare-Metal Hardware** | Dell PowerEdge Servers | **Agentless** | HTTPS Redfish REST API | Out-of-band chassis power cycling, cold rebooting frozen kernels, thermal monitoring. |
| **Network Infrastructure**| UniFi Dream Machine, UniFi PoE Switches | **Agentless** | HTTPS UniFi Controller API | Port power-cycling (PoE bounce) for hung appliances, MAC-to-IP lease verification. |
| **Cluster Orchestrator** | Kubernetes Control Plane | **Agentless** | HTTPS Kubernetes Core API | Native cordon, drain (eviction API honoring PDBs), and uncordon workflows. |

### 4.2 Compute Node Agent Specification
* **Binary Distribution:** Single, statically linked executable compiled via .NET Native AOT or Go (<15MB).
* **Service Wrapper:** Runs as a native `systemd` service (`controlplane-agent.service`) on Linux and a Windows Service on Windows.
* **Network Posture:** **Outbound-only.** The agent connects out to `wss://controlplane.homelab.local/agent-hub`. No firewall holes or open listening ports are required on managed servers.
* **Heartbeat Payload (every 10s):**
```json
{
  "nodeId": "f78d92a1-0943-4e63-8a41-8c4391d0f5e1",
  "hostname": "k8s-worker-02",
  "kernelVersion": "6.8.8-2-pve",
  "pendingReboot": true,
  "packageManager": "apt",
  "metrics": {
    "cpuUsagePct": 14.2,
    "memoryUsagePct": 62.8,
    "diskFreePct": 38.5
  },
  "packageSummary": {
    "upgradableCount": 4,
    "securityCount": 1,
    "packages": [
      { "name": "linux-image-generic", "current": "6.8.0-31.31", "candidate": "6.8.0-35.35", "isSecurity": true }
    ]
  }
}
```

### 4.3 One-Click Agent Adoption Workflow
To eliminate manual setup friction, the React UI provides an **"Adopt Node"** modal:
1. User enters Hostname/IP and SSH credentials (or selects a stored homelab SSH key).
2. Backend temporarily connects via SSH (using `SSH.NET`), inspects architecture (`x86_64` vs `aarch64`), streams the agent binary, writes the systemd unit file, installs the node's registration token, and starts the service.
3. Once the agent establishes its outbound WebSocket handshake, the SSH connection is immediately terminated and discarded.

---

## 5. Update Engine & Orchestration State Machine

Updates are executed as durable, step-by-step **Directed Acyclic Graphs (DAG)**. If an update is interrupted, the engine retains state and provides automated rollback.

### 5.1 End-to-End Node Update Sequence

```
[Target: Linux Kubernetes Worker VM on Proxmox]

1. Pre-Flight Validation
   ├── Agent Heartbeat active? (< 15s)
   ├── Root filesystem headroom > 20%?
   └── Verify no existing package locks (/var/lib/dpkg/lock-frontend)
          │
          ▼
2. Hypervisor Safety Snapshot
   └── Proxmox REST API triggers snapshot: "cp-pre-update-{timestamp}"
          │
          ▼
3. Workload Eviction
   └── Kubernetes API cordons node and executes pod eviction (drain)
          │
          ▼
4. Package Upgrade Execution
   ├── Issue upgrade command envelope to Agent
   └── Stream raw stdout/stderr over SignalR to frontend xterm.js canvas
          │
          ▼
5. Deterministic Reboot Handling
   ├── Agent signals "REBOOT_COMMENCING" and flushes execution buffer
   ├── Agent cleanly exits; OS initiates reboot
   ├── Backend switches state to "AWAITING_RECONNECT"
   └── Agent reconnects post-boot, verifies kernel, and signals "ONLINE"
          │
          ▼
6. Post-Flight Health Probes
   ├── Synthetic HTTP/TCP status check on target services
   └── Query Agent: verify 0 systemd units in 'failed' state
          │
    [Probes Succeeded?]
    ├── YES:
    │   ├── Uncordon node in Kubernetes
    │   ├── Mark Job status COMPLETED
    │   └── Schedule Proxmox snapshot deletion (retention: 24h)
    │
    └── NO (Timeout > 300s):
        ├── Trigger CRITICAL ALERT on UI
        ├── Issue Proxmox REST API: Rollback to "cp-pre-update-{timestamp}"
        └── Mark Job status ROLLED_BACK
```

### 5.2 Real-Time Streaming Architecture
To provide visibility superior to Ansible's black-box execution:
* The Agent captures raw process output streams (`stdout` and `stderr`) in chunks.
* Chunks are framed with monotonic sequence numbers and sent over the WebSocket connection.
* ASP.NET Core receives the frames and broadcasts them via SignalR `Hub<IJobClient>` to connected React frontend instances.
* The frontend binds the stream directly into an **`xterm.js`** virtual terminal instance with full ANSI color code rendering.

---

## 6. Database Schema & Data Models

### 6.1 PostgreSQL / SQLite Schema Definition

```sql
-- Hosts and Managed Inventory
CREATE TABLE hosts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    hostname VARCHAR(255) NOT NULL,
    friendly_name VARCHAR(255),
    ip_address VARCHAR(45) NOT NULL,
    os_family VARCHAR(50) NOT NULL,      -- 'linux_debian', 'linux_rhel', 'windows'
    target_type VARCHAR(50) NOT NULL,    -- 'baremetal', 'proxmox_vm', 'proxmox_lxc'
    
    -- Correlation Identifiers for Out-of-Band & Hypervisor Adapters
    proxmox_node VARCHAR(100),
    proxmox_vmid INTEGER,
    idrac_ip VARCHAR(45),
    unifi_switch_mac VARCHAR(17),
    unifi_switch_port INTEGER,
    
    -- Runtime Agent State
    agent_installed BOOLEAN DEFAULT FALSE,
    agent_version VARCHAR(30),
    agent_last_seen_at TIMESTAMP WITH TIME ZONE,
    pending_reboot BOOLEAN DEFAULT FALSE,
    upgradable_packages_count INTEGER DEFAULT 0,
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Orchestration Jobs
CREATE TABLE update_jobs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    target_host_id UUID NOT NULL REFERENCES hosts(id) ON DELETE RESTRICT,
    initiated_by VARCHAR(100) NOT NULL,
    status VARCHAR(50) NOT NULL,         -- 'Pending', 'Running', 'Verifying', 'Completed', 'Failed', 'RolledBack'
    active_step VARCHAR(100),
    snapshot_identifier VARCHAR(255),
    started_at TIMESTAMP WITH TIME ZONE,
    completed_at TIMESTAMP WITH TIME ZONE,
    failure_reason TEXT
);

-- Monotonic Step Logs (Streamed to xterm.js)
CREATE TABLE step_logs (
    id BIGSERIAL PRIMARY KEY,
    job_id UUID NOT NULL REFERENCES update_jobs(id) ON DELETE CASCADE,
    sequence_id BIGINT NOT NULL,
    stream_type VARCHAR(10) NOT NULL,    -- 'stdout', 'stderr', 'system'
    log_line TEXT NOT NULL,
    timestamp TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE INDEX idx_step_logs_job_seq ON step_logs(job_id, sequence_id);

-- Cluster Maintenance Leases (Takeover Protocol)
CREATE TABLE cluster_leases (
    lease_key VARCHAR(100) PRIMARY KEY,
    holder_identifier VARCHAR(255) NOT NULL,
    acquired_at TIMESTAMP WITH TIME ZONE NOT NULL,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL
);
```

---

## 7. Authentication & Security Architecture

### 7.1 Phased Implementation Strategy

```
+-------------------------------------------------------------+
|                      PHASE 1 (MVP)                          |
|  * Static API Key Authentication via 'X-ControlPlane-Key'   |
|  * Dev-Bypass Mode assigns static 'Admin' ClaimsPrincipal   |
|  * React UI auto-bypasses login when VITE_AUTH_MODE=bypass  |
+-------------------------------------------------------------+
                              |
                              v
+-------------------------------------------------------------+
|                      PHASE 2 (PRODUCTION)                   |
|  * Self-Hosted Zitadel Identity Provider                    |
|  * React SPA: Authorization Code Flow with PKCE             |
|  * .NET API: Microsoft.AspNetCore.Authentication.JwtBearer  |
|  * Role-Based Access Control (RBAC): Admin vs Operator      |
+-------------------------------------------------------------+
```

### 7.2 Phase 1: Dev & Static Key Bypass Configuration
In Phase 1, the backend injects an authentication handler that validates against a pre-shared secret in `appsettings.json` or local user secrets:

```csharp
// Security/ApiKeyAuthenticationHandler.cs
public class ApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration _config;
    public ApiKeyAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration config) 
        : base(options, logger, encoder) => _config = config;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (_config.GetValue<bool>("AUTH_BYPASS", false))
        {
            var claims = new[] { new Claim(ClaimTypes.Name, "DevAdmin"), new Claim(ClaimTypes.Role, "Admin") };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bypass")), "ApiKey")));
        }

        if (Request.Headers.TryGetValue("X-ControlPlane-Key", out var key) && key == _config["ControlPlane:ApiKey"])
        {
            var claims = new[] { new Claim(ClaimTypes.Name, "ApiKeyUser"), new Claim(ClaimTypes.Role, "Admin") };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey")), "ApiKey")));
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid or missing API Key."));
    }
}
```

---

## 8. Standby Runner Distribution Specification

The Standby Runner is compiled as a self-contained, single-file native binary packaging both the .NET runtime and the compiled React production assets.

### 8.1 Build & Packaging Pipeline
```bash
# 1. Build React Production Distribution
cd src/frontend
npm run build # Emits compiled assets to src/ControlPlane.Cli/wwwroot

# 2. Compile Single-File Self-Contained CLI
cd ../ControlPlane.Cli
dotnet publish ControlPlane.Cli.csproj     -c Release     -r win-x64     --self-contained true     -p:PublishSingleFile=true     -p:IncludeNativeLibrariesForSelfExtract=true     -o ./bin/dist
```

### 8.2 Embedded Static Asset Delivery
The .NET CLI host serves the React application using embedded static file providers:

```csharp
// Cli/Program.cs
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new ManifestEmbeddedFileProvider(
        assembly: typeof(Program).Assembly, 
        root: "wwwroot")
});

// SPA Fallback for client-side routing
app.MapFallbackToFile("index.html");
```

Running `./controlplane serve` starts an in-memory Kestrel web server on `http://localhost:5200`, automatically spins up local SQLite, and opens the administrator's default browser directly into the dashboard.

---

## 9. Implementation Roadmap & Antigravity Handoff

```
MILESTONE 1: Foundation, Data Layer & Host Inventory (MVP Core)
├── Scaffolding: .NET 9 Aspire host, React 19 + Vite + Tailwind scaffolding
├── Storage: EF Core DbContext with dual Npgsql / Sqlite dynamic switching
├── Inventory API: Manual Host CRUD endpoints + Proxmox VE API connection testing
├── Dev Auth: API Key validation header & dev bypass middleware
└── UI: Host status table, pending reboot indicators, manual host modal

MILESTONE 2: Agent Architecture & Real-Time Console
├── Agent: Compile static native agent binary (Linux x86_64 / arm64)
├── Outbound Hub: WebSocket agent hub for bidirectional heartbeat streaming
├── Terminal: xterm.js terminal canvas component in React
├── SignalR Pipeline: Live step log streaming from Agent -> BFF -> UI
└── SSH Bootstrapper: One-click "Adopt Node" modal pushing agent via SSH

MILESTONE 3: The Update Orchestration Engine & Standby Runner
├── Proxmox Integration: Pre-update snapshot creation and automated rollback
├── Package Engine: APT / DNF command execution envelopes and lock polling
├── Reboot State Machine: Agent graceful reboot signaling & heartbeat recovery
├── Standby CLI: Single-binary compilation with embedded wwwroot assets
└── Lease Protocol: Takeover command pulling DB snapshot into local SQLite

MILESTONE 4: Out-of-Band Integrations & Zitadel OIDC
├── Dell iDRAC: Redfish REST client for chassis power control & thermals
├── Ubiquiti UniFi: Controller API client for PoE port bouncing
├── Kubernetes Adapter: Node cordon, drain (eviction API), and uncordon steps
└── Zitadel Auth: PKCE Authorization Code flow on UI and JWT Bearer on API
```