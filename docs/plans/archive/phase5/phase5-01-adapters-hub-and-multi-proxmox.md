# Plan 01: Adapters Hub Refactor & Multi-Instance Proxmox VE Adapter

This plan executes the first milestone of **Phase 5**: removing legacy "probes" terminology, converting the Adapters tab into a multi-adapter hub with a secondary navigation bar, and providing full multi-instance Proxmox VE support across backend storage, client routing, host binding, discovery, and the frontend UI.

---

## 🎯 Objectives & Acceptance Criteria

1. **Retire "Probes" Terminology:**
   - Sidebar tab renamed to **"Infrastructure Adapters"** (or **"Adapters"**).
   - AppHeader title updated to **"Infrastructure Adapters"** with an updated subtitle.
   - Any "connection probe" buttons repurposed to standard "Test Connection" / "Sync & Health Check".
2. **Multi-Instance Proxmox Configuration in Backend:**
   - Support saving, retrieving, updating, and deleting multiple Proxmox VE instances in `SystemSettings` under `adapters:proxmox:instances`.
   - Protect API token secrets at rest via `ISecretEncryptionService`.
   - Backward compatibility: automatically migrate single `adapter:proxmox` settings to an initial instance.
3. **Host Entity & ProxmoxTarget:**
   - `ProxmoxTarget` entity includes `InstanceId`.
   - EF Core mappings updated with column `proxmox_instance_id`.
4. **Dynamic Proxmox Client Resolution:**
   - Introduce `IProxmoxClientFactory` to produce `IProxmoxClient` bound to a specific instance ID.
5. **Multi-Instance Discovery:**
   - `DiscoveryService` iterates across all configured Proxmox instances.
   - Candidate IDs carry `pve:{instanceId}:{node}:{vmid}`.
   - Host adoption correctly assigns `host.Proxmox.InstanceId`.
6. **Frontend Adapters Hub UI:**
   - `AdaptersView.tsx` with sub-navigation for `[ Proxmox VE ] [ Kubernetes ] [ Ubiquiti UniFi ] [ OPNsense ] [ BMC / iDRAC ]`.
   - `ProxmoxAdaptersView.tsx` displaying configured instances as responsive cards with connection status, edit/delete actions, and test connection results.
   - `AddProxmoxModal.tsx` for creating or editing an instance with pre-flight connection testing.

---

## 📁 Target File Structure

```
src/ControlPlane.Api/
├── Features/
│   ├── Adapters/
│   │   ├── Config/
│   │   │   ├── AdapterConfigModels.cs
│   │   │   ├── IAdapterConfigService.cs
│   │   └── AdapterConfigService.cs
│   │   └── Proxmox/
│   │       ├── IProxmoxClient.cs
│   │       ├── ProxmoxClient.cs
│   │       ├── IProxmoxClientFactory.cs
│   │       ├── ProxmoxClientFactory.cs
│   │       └── ProxmoxEndpoints.cs
│   ├── Discovery/
│   │   └── DiscoveryService.cs
│   └── Hosts/
│       └── HostService.cs
└── Storage/
    ├── Configurations/
    │   └── HostConfiguration.cs
    └── Entities/
        └── ProxmoxTarget.cs

src/frontend/src/
├── components/layout/
│   ├── AppSidebar.tsx
│   └── AppHeader.tsx
├── features/adapters/
│   ├── AdaptersView.tsx
│   ├── useAdapters.ts
│   └── proxmox/
│       ├── ProxmoxAdaptersView.tsx
│       └── AddProxmoxModal.tsx
└── App.tsx
```

---

## 📋 Task Checklist

- [x] Update `ProxmoxTarget.cs` and `HostConfiguration.cs` with `InstanceId` (`proxmox_instance_id`).
- [x] Add EF Core migration for `ProxmoxInstanceId`.
- [x] Update `AdapterConfigModels.cs`, `IAdapterConfigService.cs`, and `AdapterConfigService.cs` for multi-instance Proxmox.
- [x] Implement `IProxmoxClientFactory` and `ProxmoxClientFactory`.
- [x] Update `ProxmoxEndpoints.cs` with multi-instance endpoints (`/api/v1/adapters/proxmox/instances`, `/test-connection`).
- [x] Update `DiscoveryService.cs` to scan across all configured instances and tag candidates.
- [x] Update `HostService.cs` to preserve `InstanceId` during adoption.
- [x] Update frontend layout navigation (`AppSidebar.tsx`, `AppHeader.tsx`, `App.tsx`).
- [x] Implement `AdaptersView.tsx`, `ProxmoxAdaptersView.tsx`, `AddProxmoxModal.tsx`, and update `useAdapters.ts`.
- [x] Update E2E and unit tests, verify all tests pass.
