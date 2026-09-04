# Plan 04: Host Inventory API & Proxmox Connection Probe

**Target Milestone:** Milestone 1 (Foundation, Data Layer & Host Inventory)  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 02: Data Layer](file:///home/ckingdon/projects/homelab-manager/docs/plans/02-data-layer-and-storage.md), [Plan 03: Dev Auth](file:///home/ckingdon/projects/homelab-manager/docs/plans/03-dev-auth-and-api-key.md)  

---

## 1. Objectives & Overview

Build the core REST API for managed host inventory and infrastructure probing:
1. Provide CRUD endpoints for `Host` entities (`/api/v1/hosts`).
2. Implement host validation rules (valid IP addresses, MAC address syntax, supported OS families).
3. Implement a Proxmox VE API connection testing endpoint (`/api/v1/adapters/proxmox/test-connection`) allowing users to verify API tokens and connectivity to a Proxmox node or cluster.
4. Support filtering and sorting hosts by status, OS family, and pending reboot state.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
├── Features/
│   ├── Hosts/
│   │   ├── HostEndpoints.cs
│   │   ├── HostDtos.cs
│   │   ├── HostService.cs
│   │   └── HostValidators.cs
│   └── Adapters/
│       └── Proxmox/
│           ├── ProxmoxProbeEndpoints.cs
│           ├── ProxmoxProbeService.cs
│           └── ProxmoxClientOptions.cs
```

---

## 3. Implementation Details

### Step 1: Host CRUD Minimal API Endpoints
Register endpoints under `/api/v1/hosts`:
* `GET /api/v1/hosts`: List all hosts with optional query filters (`?osFamily=...&pendingReboot=true`).
* `GET /api/v1/hosts/{id}`: Retrieve detailed single host record with hardware correlation metadata.
* `POST /api/v1/hosts`: Register a new host manually.
* `PUT /api/v1/hosts/{id}`: Update host attributes (friendly name, tags, IP address).
* `DELETE /api/v1/hosts/{id}`: Remove a host from inventory (with cascade restrictions for running jobs).

### Step 2: DTOs & Validation
* Define `CreateHostRequest`:
  * `Hostname` (Required, DNS-compliant).
  * `IpAddress` (Required, valid IPv4 or IPv6).
  * `OsFamily` (`linux_debian`, `linux_rhel`, `windows`).
  * `TargetType` (`baremetal`, `proxmox_vm`, `proxmox_lxc`).
  * Optional: `ProxmoxNode`, `ProxmoxVmid`, `IdracIp`, `UnifiSwitchMac`, `UnifiSwitchPort`.
* Define `HostResponse` projecting runtime vitals, package counts, and reboot flags.

### Step 3: Proxmox Connection Probe
* Endpoint: `POST /api/v1/adapters/proxmox/test-connection`
* Request payload: `BaseUrl`, `ApiTokenId`, `ApiTokenSecret`, `AllowSelfSignedCert`.
* Logic:
  * Uses `IHttpClientFactory` configured with optional self-signed cert bypass (common in homelab setups).
  * Queries `GET /api2/json/version` or `GET /api2/json/nodes` on the Proxmox cluster.
  * Returns connection status, Proxmox version (e.g. `pve-manager/8.2.4`), and cluster nodes list on success, or clear error diagnostics on failure.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Register a test host
curl -X POST http://localhost:5000/api/v1/hosts \
  -H "Content-Type: application/json" \
  -H "X-ControlPlane-Key: dev-secret-key-123" \
  -d '{
    "hostname": "pve-worker-01",
    "ipAddress": "192.168.1.50",
    "osFamily": "linux_debian",
    "targetType": "proxmox_vm",
    "proxmoxNode": "pve-01",
    "proxmoxVmid": 105
  }'

# Query host list
curl http://localhost:5000/api/v1/hosts -H "X-ControlPlane-Key: dev-secret-key-123"
```

### Acceptance Criteria
- [ ] CRUD operations on `/api/v1/hosts` persist and read accurately from the database.
- [ ] Duplicate hostnames or invalid IP addresses return HTTP 400 with descriptive error messages.
- [ ] Proxmox probe endpoint cleanly validates reachability and reports version details or connection error reasons.
