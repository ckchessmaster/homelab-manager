# Changelog

All notable changes to the **ControlPlane** (Homelab Orchestration & Management Plane) project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## v1.2.0

### Initial General Availability (GA) Release

ControlPlane v1.2.0 is the foundational release of the resilient homelab orchestration platform. It replaces brittle push-and-wait configuration management (Ansible) and solves the chicken-and-egg upgrade dilemma through a hybrid agent/agentless topology, durable Temporal workflows, unified infrastructure adapters, and built-in Model Context Protocol (MCP) integration.

---

### Highlights & Key Features

#### 1. Dual-Topology Architecture (Cluster & Standby Mode)
* **Cluster Mode**: High-availability Kubernetes deployment powered by ASP.NET Core (.NET 10) and PostgreSQL (CloudNativePG / Bitnami).
* **Standby Takeover Mode**: Single-binary CLI runner (`ControlPlane.Cli`) backed by SQLite that takes over cluster maintenance during physical node and hypervisor upgrades without circular database dependencies.
* **Outbound-Only Go Node Agent**: Lightweight, statically compiled compute node daemon (`controlplane-agent`, <15MB binary, <10MB RSS) with zero listening ports, dialing outbound to ControlPlane over secure WebSockets (`wss://`).

#### 2. Durable Temporal Workflow Orchestration
* **Durable Sagas & DAG Execution**: State-machine-driven update pipelines (`Pending` -> `Running` -> `Verifying` -> `Completed` / `RolledBack`) with automated compensations and approval gates.
* **Dual Temporal Support**: Configurable to run with local containerized Temporal for inner-loop development or external Kubernetes Temporal clusters.
* **Strict Namespace & Queue Isolation**: Default binding to dedicated `homelab-manager` namespace and `homelab-manager-tasks` task queue.
* **Zitadel M2M OAuth2 / OIDC Token Injection**: Proactive in-memory token refresh service injecting Bearer authorization metadata over gRPC to authenticated Temporal clusters.
* **Temporal Health Checks & Diagnostics**: Startup diagnostics and gRPC connectivity health probing via `DescribeNamespaceAsync`.

#### 3. Modular Agentless Infrastructure Adapters
* **Hypervisors (Proxmox VE)**: Node vitals, LXC/QEMU inventory, automated candidate discovery, snapshot management, and OS detection.
* **Firewalls & Routers (OPNsense)**: Real-time firewall status, gateway diagnostics, and candidate discovery across DHCP/ARP tables.
* **Switches & Power (Ubiquiti UniFi)**: Switch port inventory, PoE power cycling for hung smart home bridges and access points.
* **Out-of-Band Hardware BMCs (Dell iDRAC & DMTF Redfish)**: Hardware sensor metrics, temperatures, power actions, chassis indicator LED controls, fan curve telemetry, and next-boot device selection.
* **Smart Home (Home Assistant)**: Instance discovery, integration health checks, backup generation, and supervised core updates.
* **Kubernetes Orchestrator**: Workload inventory (Deployments, StatefulSets, DaemonSets, Pods), rolling restarts, and replica scaling.

#### 4. React 19 Visual Terminal Canvas
* **Modern High-Density UI**: Built with React 19, TypeScript, Vite, Tailwind CSS, and shadcn/ui adhering to homelab design system guidelines (`zinc-950` canvas base, `sky-500` accents).
* **Visual Workflow Canvas**: Interactive node-based DAG visualization and execution tracing powered by `@xyflow/react`.
* **SignalR ANSI Terminal**: Real-time terminal streaming using `xterm.js` and SignalR with monotonic sequence ordering `(job_id, sequence_id, stream_type, log_line, timestamp)`.
* **Zitadel OIDC Authentication**: PKCE authentication with role-based UI component protection (`Viewer`, `Operator`, `Admin`).

#### 5. Embedded Model Context Protocol (MCP) Server
* Built-in streamable HTTP MCP server registered at `/mcp` exposing 30+ infrastructure tools for AI pair programmers and autonomous operations.
* Full audit trail tagging destructive operations (reboots, PoE cycles, BMC power commands) with `InitiatedBy = "AI Agent via MCP"`.

#### 6. Production Helm Chart & Cloud-Native Packaging
* Production-grade Kubernetes Helm chart in `charts/controlplane` with automated OCI distribution to GitHub Container Registry (`ghcr.io/ckingdon/charts/controlplane`).
* Flexible secrets integration supporting CloudNativePG (`passwordSecretName`), external secrets (`existingSecret`), and Zitadel client secrets (`clientSecretName`).
* Nginx and Traefik ingress configurations optimized for long-lived WebSocket and SignalR terminal streaming connections.

---

### Security
* **AES-256-GCM Encryption at Rest**: All sensitive credentials, API tokens, and passwords stored securely via `ISecretEncryptionService` with automatic masking in all API responses.
* **Zero Inbound Ports on Compute Nodes**: Node agents strictly initiate outbound connections with mutual heartbeat keep-alives.
