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
* **Appliances:** Hypervisors (Proxmox), BMCs (iDRAC), Switches (UniFi), and Orchestrators (Kubernetes) are agentless. Their credentials must be stored encrypted and accessed only from the backend.
* **Process Output Streaming:** Output from long-running package managers (`apt`, `dnf`) must be framed monotonically: `(job_id, sequence_id, stream_type, log_line, timestamp)` so that network drops do not duplicate or jumble log streams.
