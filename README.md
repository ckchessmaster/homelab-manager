# Homelab Orchestration & Management Plane (ControlPlane)

ControlPlane is an enterprise-grade, resilient homelab lifecycle and orchestration plane engineered to solve the acute challenges of managing heterogeneous bare-metal servers (Dell PowerEdge with iDRAC), virtual hypervisors (Proxmox VE), container platforms (Kubernetes), and diverse operating systems (Debian/Ubuntu, RHEL/Rocky, Windows).

ControlPlane replaces brittle push-and-wait configuration management (e.g. Ansible) with an asynchronous Directed Acyclic Graph (DAG) state machine, real-time terminal streaming over SignalR, pre-flight hypervisor snapshots, deterministic reboot tracking, and an autonomous zero-dependency Standby CLI runner capable of taking over orchestration while Kubernetes itself is undergoing maintenance.

---

## 🛠️ Architecture & Technology Stack

* **Orchestrator Backend & BFF:** .NET 10 (C#) orchestrated with **.NET Aspire** (latest version).
* **Frontend SPA:** React 19, TypeScript, Vite, Tailwind CSS, shadcn/ui, TanStack Query, and `xterm.js`.
* **Compute Node Agent:** Static single-binary daemon in **Go** (<15MB, outbound WebSocket).
* **Storage Layer:** Dual-provider Entity Framework Core (PostgreSQL 16+ in-cluster, local SQLite 3 for standby workstation execution).
* **Appliances (Agentless):** Proxmox VE REST API, Dell iDRAC / Redfish REST, Ubiquiti UniFi Controller API, and Kubernetes Eviction/Drain APIs.

---

## 📋 Prerequisites & Setup

### Environment Requirements

1. **.NET SDK:**
   * **Target:** **.NET 10 SDK** (latest preview/release).
   * **.NET Aspire:** Latest Aspire workload / packages.
   * > [!IMPORTANT]
     > Aspire evolves rapidly. When implementing or modifying Aspire components, implementation agents must consult the latest Aspire documentation or release notes for current hosting APIs and configuration patterns.
   * *WSL/Ubuntu Installation Options:*
     ```bash
     sudo snap install dotnet --classic # or install via Microsoft package repository
     ```
2. **Node.js & Package Manager:**
   * Node.js v22.x+ (`node -v`)
   * npm v10.x+ (`npm -v`)
3. **Go (Agent Daemon):**
   * Go 1.22+ (for building the static compute node agent binary for `linux/amd64` and `linux/arm64`)
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
existingSecret: "temporal-db-secret"

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
    # Option A: Mount a specific existing secret name directly
    clientSecretName: "homelab-manager-client-secret"
    # Option B: Or target a specific secret & key reference
    # secretRef:
    #   name: "zitadel-m2m-secret"
    #   key: "client-secret"
    # Option C: Or inline clientSecret (stored in chart-managed Secret)
    # clientSecret: "your-zitadel-client-secret"

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
  # Sourced from an existing secret (e.g. CloudNativePG or custom secret):
  passwordSecretName: "homelab-manager-db-password"
  # Or provide inline password:
  # password: "your_secure_db_password"

# 4. Security Keys
auth:
  apiKey: "your_controlplane_api_key"
  masterKey: "your_256bit_base64_master_key" # generate with: openssl rand -base64 32
  # Or reference existing Kubernetes Secrets:
  # apiKeySecretRef: { name: "cp-secrets", key: "apiKey" }
  # masterKeySecretRef: { name: "cp-secrets", key: "masterKey" }
  # existingSecret: "controlplane-all-in-one-secret"
```

> **Tip on Secret Keys:** When using `clientSecretName`, `passwordSecretName`, or `existingSecret`, keys are automatically mapped by standard convention.
> * **Temporal Client Secret**: Accepts `client-secret`, `clientSecret`, `client_secret`, `secret`, `temporal-client-secret`, `Temporal__Auth__ClientSecret`, or `CLIENT_SECRET`.
> * **Database Password / Connection**: Accepts `password`, `postgres-password`, `db-password`, `DB_PASSWORD`, `POSTGRES_PASSWORD`, `Database__Password`, or standard PostgreSQL connection URIs (`uri`, `URI`, `DATABASE_URL`).

---

## 🗺️ Project Documentation & Roadmap

* **[Technical Design Document (Initial Overview)](file:///home/ckingdon/projects/homelab-manager/docs/initial-overview.md)**: Full architectural blueprint, state machine sequence, lease protocol, and database schema.
* **[Master Implementation Roadmap](file:///home/ckingdon/projects/homelab-manager/docs/plans/roadmap.md)**: Sequential breakdown across all 4 project milestones, containing links to all 16 iterative plan files.
* **[Agent & Development Guidelines (AGENTS.md)](file:///home/ckingdon/projects/homelab-manager/AGENTS.md)**: Repository guidelines, standards, and workflow instructions for Antigravity pair programming.

---

## 📂 Repository Layout

```
homelab-manager/
├── .agents/                    # Antigravity domain rules and customizations
│   └── rules/                  # Specialized rules (architecture, dotnet, react, go)
├── docs/                       # Architectural design docs and roadmap
│   ├── initial-overview.md     # Foundational technical design document
│   └── plans/                  # 16 sequential plan files and roadmap.md
├── src/                        # Source code (scaffolded in Phase 1)
│   ├── Aspire/                 # AppHost and ServiceDefaults
│   ├── ControlPlane.Api/       # ASP.NET Core BFF API
│   ├── ControlPlane.Cli/       # Standby Workstation Runner single-binary CLI
│   ├── frontend/               # React 19 + TypeScript SPA
│   └── agent/                  # Go compute node daemon
├── AGENTS.md                   # Antigravity core repository guide
└── README.md                   # Project overview & developer setup
```