# Plan 01: Unified Service Discovery (Proxmox VE & Kubernetes Nodes)

**Phase:** Phase 2  
**Status:** ✅ Completed  
**Dependencies:** None (builds on Phase 1 adapters)

---

## 1. Objectives & Overview

Eliminate manual host inventory entry by implementing automated discovery of compute targets from both hypervisors and cluster orchestrators:
1. **Proxmox VE Cluster Scanning**:
   * Enumerate all QEMU VMs and LXC containers via `GET /cluster/resources?type=vm`.
   * Resolve guest IPv4 addresses via QEMU guest agent (`GET /nodes/{node}/qemu/{vmid}/agent/network-get-interfaces`).
2. **Kubernetes Cluster Node Scanning**:
   * Enumerate all nodes in the cluster via `CoreV1.ListNodeAsync()`.
   * Extract internal IP, node roles (`control-plane` vs `worker`), OS image, and kernel version.
3. **De-Duplication & Cross-Referencing**:
   * Correlate discovered candidates against the existing `hosts` table (matching on IP, hostname, or Proxmox node/vmid).
   * Flag each item as either `Managed` (already in inventory) or `Discovered` (new).
4. **1-Click Import**:
   * Allow single or bulk import of discovered items into the `hosts` inventory with all correlation metadata pre-populated.
5. **Interactive UI**:
   * Provide a dedicated **Discovery & Adoption Hub** in the React UI with scanning controls, status filters, and import actions.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Features/
    ├── Adapters/
    │   ├── Proxmox/
    │   │   ├── IProxmoxClient.cs (add DiscoverClusterResourcesAsync)
    │   │   └── ProxmoxClient.cs
    │   └── Kubernetes/
    │       ├── IKubernetesAdapter.cs (add ListNodesAsync)
    │       └── KubernetesAdapter.cs
    └── Discovery/
        ├── DiscoveryModels.cs
        ├── IDiscoveryService.cs
        ├── DiscoveryService.cs
        └── DiscoveryEndpoints.cs

src/frontend/src/
├── api/
│   └── discovery.ts
└── features/
    └── discovery/
        ├── DiscoveryView.tsx
        ├── DiscoveryCandidatesTable.tsx
        └── useDiscovery.ts
```

---

## 3. Implementation Details

### Step 1: Proxmox Resource Discovery
* Query `GET /cluster/resources?type=vm`.
* Parse list of `ProxmoxClusterResourceDto` (`vmid`, `name`, `node`, `type`, `status`, `uptime`, `maxmem`, `maxdisk`, `tags`).
* For running QEMU VMs, attempt to resolve IP from guest agent if reachable; fallback to Proxmox config if needed.

### Step 2: Kubernetes Node Discovery
* Call `_client.CoreV1.ListNodeAsync(cancellationToken: ct)`.
* For each node:
  * Name: `node.Metadata.Name`
  * Internal IP: `node.Status.Addresses` where `Type == "InternalIP"`
  * Role: Check labels `node-role.kubernetes.io/control-plane` or `master`
  * OS: `node.Status.NodeInfo.OsImage`, `KernelVersion`
  * Condition: `Ready == True`

### Step 3: Unified Discovery Service & De-Duplication
* Combine both sources into a unified list of `DiscoveredHostCandidate`.
* For each candidate, check database for existing matching host:
  * Match by Proxmox VMID + Node
  * Match by IP Address
  * Match by Hostname
* Set `IsManaged = true` if match found, linking existing `HostId`.

### Step 4: Import Endpoint
* `POST /api/v1/discovery/import`:
  * Takes candidate details, creates new `Host` entity, and saves to database.
  * Emits signal/event for UI inventory refresh.

### Step 5: Frontend Discovery Hub
* Add **Discovery** tab to navigation in `AppSidebar.tsx` and `App.tsx`.
* Render filterable candidate table with badges:
  * Type: `Proxmox VM`, `Proxmox LXC`, `K8s Control Plane`, `K8s Worker`
  * Status: `New Discovery` (green) vs `Already Managed` (zinc)
* "Import to Inventory" button triggering import mutation.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Scan for discovered hosts
curl http://localhost:5000/api/v1/discovery/scan \
  -H "X-ControlPlane-Key: dev-secret-key-123"

# Import candidate into inventory
curl -X POST http://localhost:5000/api/v1/discovery/import \
  -H "Content-Type: application/json" \
  -H "X-ControlPlane-Key: dev-secret-key-123" \
  -d '{"name": "k8s-worker-01", "ipAddress": "192.168.1.150", "targetType": "proxmox_vm", "osFamily": "linux_debian", "proxmoxNode": "pve1", "proxmoxVmid": 101}'
```

### Acceptance Criteria
- [ ] Proxmox cluster scanning discovers VMs and LXCs across cluster nodes.
- [ ] Kubernetes node scanning extracts node names, IPs, roles, and conditions.
- [ ] Candidates are de-duplicated against existing database inventory.
- [ ] 1-click import creates `Host` records with populated correlation metadata.
- [ ] React UI provides a clean, responsive Discovery view.
