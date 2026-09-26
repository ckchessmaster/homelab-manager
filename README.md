# Homelab Orchestration & Management Plane (ControlPlane)

[![Release](https://img.shields.io/badge/release-v1.3.1-blue.svg)](https://github.com/ckchessmaster/homelab-manager/releases)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![React](https://img.shields.io/badge/React-19.0-61dafb.svg)](https://react.dev/)
[![Aspire](https://img.shields.io/badge/Aspire-13.4-512bd4.svg)](https://learn.microsoft.com/dotnet/aspire/)
[![Tests](https://img.shields.io/badge/tests-333%20passed-brightgreen.svg)]()
[![License](https://img.shields.io/badge/License-Apache_2.0-yellow.svg)](LICENSE)

ControlPlane is an enterprise-grade, resilient homelab lifecycle and orchestration plane engineered to solve the acute challenges of managing heterogeneous bare-metal servers (Dell PowerEdge with iDRAC), virtual hypervisors (Proxmox VE), container platforms (Kubernetes), network appliances (Ubiquiti UniFi, OPNsense), smart home infrastructure (Home Assistant), and diverse operating systems (Debian/Ubuntu, RHEL/Rocky, Windows).

ControlPlane replaces brittle push-and-wait configuration management (e.g. Ansible) with an asynchronous Directed Acyclic Graph (DAG) state machine, real-time terminal streaming over SignalR, pre-flight hypervisor snapshots, deterministic reboot tracking, an embedded Model Context Protocol (MCP) server for autonomous AI assistance, and a zero-dependency Standby CLI runner capable of taking over orchestration while Kubernetes itself is undergoing maintenance.

---

## 🛠️ Architecture & Technology Stack

* **Orchestrator Backend & BFF:** .NET 10 (C#) orchestrated with **.NET Aspire** (latest version).
* **Frontend SPA:** React 19, TypeScript, Vite, Tailwind CSS, shadcn/ui, TanStack Query, and `xterm.js`.
* **Compute Node Agent:** Static single-binary daemon in **Go** (<15MB RSS, outbound WebSocket `wss://`, cross-platform support for Linux `amd64`/`arm64` and Windows Service).
* **Storage Layer:** Dual-provider Entity Framework Core (PostgreSQL 16+ for in-cluster production, local SQLite 3 for standby workstation execution).
* **Appliances (Agentless):** Proxmox VE REST API, Dell iDRAC / DMTF Redfish REST, Ubiquiti UniFi Controller API, OPNsense Firewall REST API, Home Assistant REST/WebSocket API, and Kubernetes Eviction/Drain APIs.
* **Multi-Cluster Kubernetes Workloads:** Aggregated management of Deployments, StatefulSets, DaemonSets, and Pods with rolling restart and replica scaling capabilities.
* **Helm Application Catalog:** Repository catalog synchronization, semantic version inspection, one-click upgrades, and automated rollback integration.
* **Embedded AI MCP Server:** Built-in Model Context Protocol server exposing 40+ diagnostic and operational tools at `/mcp` for seamless pair programming with AI agents.
* **Centralized System Observability:** High-throughput in-memory circular logging buffer (2,500 entries) with real-time UI streaming, multi-level filtering, search, and export.

---

## 🔖 Release & Versioning Strategy

ControlPlane adheres strictly to [Semantic Versioning (SemVer 2.0.0)](https://semver.org/): `MAJOR.MINOR.PATCH` (e.g. `v1.3.1`).

* **MAJOR (`X.0.0`)**:
  * Breaking architectural shifts or protocol redesigns (such as breaking changes to the agent WebSocket communication protocol requiring a simultaneous fleet-wide reinstall).
  * Breaking database schema modifications without backward-compatible automatic EF Core migrations.
  * Runtime or platform upgrades with breaking API changes (e.g. .NET or React major framework migrations).
* **MINOR (`1.X.0`)**:
  * Significant new operational features, modules, or platform capabilities (e.g. adding Multi-Cluster Workloads, Helm release management, Candidate Discovery engine, or System & Settings log streaming).
  * New infrastructure adapters (e.g. Home Assistant, TrueNAS, Mikrotik).
  * Non-breaking database schema expansions (new tables, columns with defaults).
  * Additions to the embedded Model Context Protocol (MCP) tool suite.
* **PATCH (`1.3.X`)**:
  * Targeted bug fixes, edge case resolutions, and runtime stability enhancements.
  * Agent platform installer fixes and compatibility tweaks (such as Windows Service string escaping or `-Insecure` SSL flags).
  * UI/UX refinements, CSS/theme adjustments, or minor performance optimizations.
  * Security patches for direct dependencies without breaking public interfaces or behavioral contracts.

---

## 📋 Prerequisites & Setup

### Environment Requirements

1. **.NET SDK:**
   * **Target:** **.NET 10 SDK** (latest release).
   * **.NET Aspire:** Latest Aspire workload / packages.
   * *WSL/Ubuntu Installation Options:*
     ```bash
     sudo snap install dotnet --classic # or install via Microsoft package repository
     ```
2. **Node.js & Package Manager:**
   * Node.js v22.x+ (`node -v`)
   * npm v10.x+ (`npm -v`)
3. **Go (Agent Daemon):**
   * Go 1.22+ (for building the static compute node agent binary for `linux/amd64`, `linux/arm64`, and `windows/amd64`)
4. **Container Engine (Optional / Aspire Dev):**
   * Docker or Podman for local Aspire resource orchestration (PostgreSQL container, etc.)

---

## 🚀 Kubernetes Deployment with Helm

ControlPlane provides a production-ready Helm chart located in `charts/controlplane` and published as an OCI artifact to GitHub Container Registry (`oci://ghcr.io/ckchessmaster/charts/controlplane`).

### Quickstart Installation

```bash
# 1. Create the target namespace
kubectl create namespace controlplane --dry-run=client -o yaml | kubectl apply -f -

# 2. Deploy using the local chart
helm upgrade --install controlplane ./charts/controlplane \
  --namespace controlplane \
  --values values.yaml

# Or deploy directly from the GHCR OCI registry:
helm upgrade --install controlplane oci://ghcr.io/ckchessmaster/charts/controlplane \
  --namespace controlplane \
  --values values.yaml
```

### Production Configuration Example (`values.yaml`)

```yaml
# Root-level option: Mount a combined existing secret (e.g. created by SealedSecrets / Vault / ExternalSecrets)
existingSecret: "controlplane-production-secrets"

# 1. Temporal Workflow Orchestration (External Cluster)
temporal:
  enabled: true
  address: "temporal-frontend.temporal.svc.cluster.local:7233"
  namespace: "homelab-manager"
  taskQueue: "homelab-manager-tasks"
  auth:
    enabled: true
    tokenUrl: "https://auth.chriskingdon.com/oauth/v2/token"
    clientId: "homelab-manager-client"
    scopes: "openid urn:zitadel:iam:org:projects:roles urn:zitadel:iam:org:project:id:392065020537602975:aud"
    clientSecretName: "homelab-manager-client-secret"

# 2. ControlPlane UI Ingress (React Frontend + API & WebSockets)
ingress:
  enabled: true
  className: "nginx" # or "traefik"
  annotations:
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
    nginx.ingress.kubernetes.io/proxy-read-timeout: "86400"
    nginx.ingress.kubernetes.io/proxy-send-timeout: "86400"
  hosts:
    - host: manage.local.chriskingdon.com
      paths:
        - path: /
          pathType: Prefix
          service: frontend # React 19 SPA UI
        - path: /api
          pathType: Prefix
          service: api      # ASP.NET Core API
        - path: /hubs
          pathType: Prefix
          service: api      # SignalR Terminal Streaming Hub
        - path: /agent-hub
          pathType: Prefix
          service: api      # Compute Node Agent WebSocket Hub
        - path: /mcp
          pathType: Prefix
          service: api      # Model Context Protocol Server
  tls:
    - secretName: homelab-manager-web-tls
      hosts:
        - manage.local.chriskingdon.com

# 3. PostgreSQL Database
database:
  host: "postgres-rw.cnpg-services.svc.cluster.local"
  port: 5432
  database: "homelab_manager"
  username: "homelab_manager"
  passwordSecretName: "homelab-manager-db-password"

# 4. Security Keys
auth:
  apiKey: "your_controlplane_api_key"
  masterKey: "your_256bit_base64_master_key" # generate with: openssl rand -base64 32
```

> **Tip on Secret Keys:** When using `clientSecretName`, `passwordSecretName`, or `existingSecret`, keys are automatically mapped by standard convention.
> * **Temporal Client Secret**: Accepts `client-secret`, `clientSecret`, `client_secret`, `secret`, `temporal-client-secret`, `Temporal__Auth__ClientSecret`, or `CLIENT_SECRET`.
> * **Database Password / Connection**: Accepts `password`, `postgres-password`, `db-password`, `DB_PASSWORD`, `POSTGRES_PASSWORD`, `Database__Password`, or standard PostgreSQL connection URIs (`uri`, `URI`, `DATABASE_URL`).

---

## 💻 Compute Node Agent Installation

Compute nodes run the lightweight `controlplane-agent` daemon which establishes an outbound WebSocket connection (`wss://`) to the backend. No incoming firewall ports are needed on compute nodes.

### Linux (Systemd)

```bash
curl -fsSL https://manage.local.chriskingdon.com/api/v1/agents/install.sh | sudo bash -s -- \
  --hub-url wss://manage.local.chriskingdon.com/agent-hub \
  --token <YOUR_NODE_TOKEN> \
  --node-id <YOUR_NODE_ID>
```

### Windows (PowerShell Service)

Run in an elevated PowerShell session:

```powershell
& ([scriptblock]::Create((iwr -UseBasicParsing 'https://manage.local.chriskingdon.com/api/v1/agents/install.ps1').Content)) `
  -HubUrl 'wss://manage.local.chriskingdon.com/agent-hub' `
  -Token '<YOUR_NODE_TOKEN>' `
  -NodeId '<YOUR_NODE_ID>' `
  -Insecure
```

---

## 🗺️ Project Documentation & Roadmap

* **[Technical Design Document (Initial Overview)](file:///home/ckingdon/projects/homelab-manager/docs/initial-overview.md)**: Full architectural blueprint, state machine sequence, lease protocol, and database schema.
* **[MCP Server Documentation](file:///home/ckingdon/projects/homelab-manager/docs/mcp-server.md)**: Model Context Protocol tools schema and operational patterns.
* **[Master Implementation Roadmap](file:///home/ckingdon/projects/homelab-manager/docs/plans/roadmap.md)**: Sequential breakdown across all milestones, containing links to all 16 iterative plan files.
* **[Agent & Development Guidelines (AGENTS.md)](file:///home/ckingdon/projects/homelab-manager/AGENTS.md)**: Repository guidelines, standards, and workflow instructions for Antigravity pair programming.

---

## 📂 Repository Layout

```
homelab-manager/
├── .agents/                    # Antigravity domain rules, skills, and MCP configuration
│   ├── rules/                  # Specialized rules (architecture, dotnet, react, go, adapters)
│   └── skills/                 # Operational skills (controlplane, aspire, etc.)
├── charts/
│   └── controlplane/           # Production Kubernetes Helm chart
├── deploy/
│   ├── compose/                # Production Docker Compose stack
│   ├── k8s/                    # Base Kubernetes manifests and Kustomize environment overlays
│   └── zitadel/                # Self-hosted Zitadel OIDC bootstrap & setup scripts
├── docs/                       # Architectural design docs, plans, and roadmap
├── src/
│   ├── Aspire/
│   │   ├── ControlPlane.AppHost/           # Aspire distributed application orchestrator
│   │   └── ControlPlane.ServiceDefaults/   # Telemetry, health checks, resilience defaults
│   ├── ControlPlane.Api/                   # ASP.NET Core BFF, Adapters, MCP & Orchestration API
│   │   └── Features/
│   │       ├── Adapters/                   # Modular adapters: Proxmox, K8s, UniFi, OPNsense, Redfish, HomeAssistant
│   │       ├── Workloads/                  # Multi-cluster Kubernetes application engine
│   │       ├── Helm/                       # Helm catalog and release management
│   │       ├── Mcp/                        # Built-in Model Context Protocol server tools (40+)
│   │       ├── Security/                   # Zitadel JWT, RBAC, PAT tokens, and AES-256-GCM encryption
│   │       ├── SystemLogs/                 # Circular log buffer, real-time query API, and diagnostics
│   │       ├── Agents/                     # WebSocket agent hub, binary management, and auto-sync
│   │       ├── Discovery/                  # Dynamic candidate discovery across hypervisors/firewalls
│   │       ├── Hosts/                      # Host inventory & adoption service
│   │       └── Orchestration/              # DAG state machine, pipelines, and Temporal workflows
│   ├── ControlPlane.Cli/                   # Standby single-binary CLI runner with SQLite
│   ├── frontend/                           # React 19 SPA (Vite + Tailwind CSS + shadcn/ui)
│   └── agent/                              # Go compute node agent daemon (cross-platform)
├── tests/
│   ├── ControlPlane.Api.Tests/             # Comprehensive xUnit test suite (330+ tests)
│   └── ControlPlane.Cli.Tests/             # CLI and standby runner unit tests
├── AGENTS.md                   # Antigravity core repository guide
├── CHANGELOG.md                # Release notes and version history
└── README.md                   # Project overview & developer setup
```

---

## 🧪 Testing & Verification

```bash
# Build the entire backend
dotnet build src/ControlPlane.Api/ControlPlane.Api.csproj

# Run all unit and integration tests (330+ passing)
dotnet test --filter "FullyQualifiedName!~PostgresStorageTests"

# Build the React 19 frontend
cd src/frontend && npm run build
```