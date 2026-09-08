# Architecture Rules & Invariants

These architectural constraints must be adhered to at all times when designing, editing, or extending ControlPlane.

## 1. Dual-Topology Compatibility (Postgres & SQLite)
* **Single DbContext:** `ControlPlaneDbContext` must support both PostgreSQL (in-cluster) and SQLite (local standby runner).
* **Portable Queries:** Avoid provider-specific SQL syntax, raw SQL fragments, or PostgreSQL-only extensions (like `citext` or specific JSON operators) inside EF Core queries.
* **Date/Time Handling:** Store all timestamps as UTC (`DateTimeOffset` or `DateTime.UtcNow`). Ensure SQLite handles ISO-8601 strings cleanly.
* **UUID Primary Keys:** In PostgreSQL, IDs use `gen_random_uuid()`. In SQLite, GUIDs are stored as 16-byte BLOBs or 36-character strings. Configure EF Core to generate client-side or provider-neutral GUIDs (`Guid.NewGuid()`).

## 2. Standby Takeover & Lease Synchronization
* The Standby CLI must operate autonomously when the Kubernetes cluster is offline.
* The Standby CLI accesses **only** its local SQLite database (`standby-state.db`) during active node maintenance and reboots.
* Database synchronization occurs strictly via:
  1. **Pre-flight snapshot export:** Cluster pushes/serves JSON snapshot -> CLI seeds SQLite.
  2. **Distributed lock:** `cluster_leases` table with `GLOBAL_MAINTENANCE_LOCK`.
  3. **In-cluster suspension:** Cluster API switches to read-only pass-through.
  4. **Post-flight delta sync:** CLI flushes new job logs, updated host states, and audit records back to PostgreSQL once available, then releases the lock.

## 3. Communication Boundary Rules
* **Compute Nodes:** Outbound-only. No inbound SSH or HTTP ports may be required for ongoing operations. All communication is over client-initiated WebSocket (`wss://`).
* **Appliances & Adapters:** Hypervisors (Proxmox), BMCs (iDRAC / Redfish), Switches (UniFi), Firewalls (OPNsense), and Orchestrators (Kubernetes) are agentless. Their credentials must be stored encrypted with `AES-256-GCM` in `system_settings` and accessed only from the backend.
* **Process Output Streaming:** Output from long-running package managers (`apt`, `dnf`) must be framed monotonically: `(job_id, sequence_id, stream_type, log_line, timestamp)` so that network drops do not duplicate or jumble log streams.

## 4. Security & Role-Based Access Control (RBAC)
* **Identity Provider:** Zitadel OIDC provides centralized identity and user directory.
* **Backend RBAC:** ASP.NET Core JWT Bearer authentication validates tokens and extracts roles:
  * `Viewer`: Read-only access to inventory, logs, and telemetry.
  * `Operator`: May initiate maintenance pipelines, restart workloads, and dispatch commands.
  * `Admin`: Full configuration control (adapters, credentials, user management).
* **Dev Bypass:** In local environments, `AUTH_BYPASS=true` automatically assigns the `Admin` role for seamless development.

## 5. Model Context Protocol (MCP) Server Boundaries
* The embedded MCP server in `Features/Mcp` exposes safe operational interfaces to AI agents.
* All tool actions must enforce role safety and include parameter descriptions with descriptive error messages.
* When mutations occur via MCP tools, the audit fields must record `InitiatedBy = "AI Agent via MCP"`.
