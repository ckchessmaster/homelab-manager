# Infrastructure Redesign: Separate Appliances from Compute Nodes

## Problem
UniFi/OPNsense appliances are imported into the `hosts` table alongside compute nodes, exposing inapplicable actions (SSH adoption, agent vitals, OS editing). The "Adopt" terminology is overloaded across 3 meanings.

## Design Decisions

| Decision | Choice |
|---|---|
| Appliances in `hosts`? | **No** — separate `devices` table |
| Discovery routing | Import routes to `hosts` or `devices` based on `targetType` |
| Infrastructure page UX | Tabs: All \| Compute Nodes \| Network Devices \| Firewalls |
| Row click behavior | Inspector sheet with type-specific sections |
| "Mass Adopt" rename | **"Add to Infrastructure"** |
| Adoption safety | Data model separation + explicit backend guard |
| Adapter config views | Move to Settings/Integrations; device ops → Infrastructure |
| Existing appliance data | Migration deletes appliance-type entries from `hosts`; users re-import to `devices` |

---

## Implementation Plan

### Phase 1: Backend — `devices` Table & Service

**1.1 — Entity & Migration**
- Create [`Device`](file:///home/ckingdon/projects/homelab-manager/src/ControlPlane.Api/Storage/Entities/Device.cs) entity:
  ```csharp
  public class Device
  {
      public Guid Id { get; set; }
      public string Name { get; set; } = string.Empty;
      public string? IpAddress { get; set; }
      public string? MacAddress { get; set; }
      public string DeviceType { get; set; } = string.Empty;       // switch, access_point, gateway, firewall
      public Guid? AdapterId { get; set; }                          // FK to adapter that manages it
      public string? AdapterType { get; set; }                      // "unifi" | "opnsense"
      public string? FirmwareVersion { get; set; }
      public string? Model { get; set; }
      public string? SerialNumber { get; set; }
      public string? Source { get; set; }                            // discovery source
      public string Status { get; set; } = "unknown";               // online, offline, unknown
      public string? Location { get; set; }
      public string? Notes { get; set; }
      public List<string> Tags { get; set; } = [];
      public DateTimeOffset? LastSeenAt { get; set; }
      public DateTimeOffset CreatedAt { get; set; }
      public DateTimeOffset UpdatedAt { get; set; }
  }
  ```
- Add `DbSet<Device> Devices` to `ControlPlaneDbContext`
- Add EF migration (dual-provider: PostgreSQL + SQLite)
- Migration deletes existing `hosts` rows where `target_type` in (`switch`, `access_point`, `gateway`, `network_device`, `firewall`, `appliance`)

**1.2 — Device Service & Endpoints**
- Create `IDeviceService` / `DeviceService` in `Features/Devices/`:
  - `GetAllAsync()` → returns all devices
  - `GetByIdAsync(Guid id)`
  - `CreateAsync(CreateDeviceRequest)` — used by discovery import
  - `UpdateAsync(Guid id, UpdateDeviceRequest)` — edit location/notes/tags
  - `DeleteAsync(Guid id)`
  - `RefreshStatusAsync()` — polls adapter APIs for current device state
- Register endpoints at `/api/v1/devices`

**1.3 — Discovery Import Routing**
- In [`DiscoveryService.ImportCandidateAsync()`](file:///home/ckingdon/projects/homelab-manager/src/ControlPlane.Api/Features/Discovery/DiscoveryService.cs):
  - Define `ApplianceTargetTypes = { "switch", "access_point", "gateway", "network_device", "firewall", "appliance" }`
  - If candidate `targetType` ∈ `ApplianceTargetTypes` → create `Device` via `IDeviceService`
  - Otherwise → create `Host` via `IHostService` (existing behavior)
- Update `ImportBatchAsync()` similarly

**1.4 — Adoption Safety Guard**
- In [`NodeAdoptionService.AdoptNodeAsync()`](file:///home/ckingdon/projects/homelab-manager/src/ControlPlane.Api/Features/Adoption/NodeAdoptionService.cs):
  - Add guard at top: if `host.TargetType` is an appliance type, return error `"Cannot install agent on appliance device. Appliances are managed via adapters."`

**1.5 — Host Validators Cleanup**
- In [`HostValidators.cs`](file:///home/ckingdon/projects/homelab-manager/src/ControlPlane.Api/Features/Hosts/HostValidators.cs):
  - Remove appliance-only values from `AllowedTargetTypes` (`switch`, `access_point`, `gateway`, `network_device`, `firewall`, `appliance`)
  - Remove `unifi_os` from `AllowedOsFamilies`
  - These types now belong to `Device`, not `Host`

### Phase 2: Backend — Infrastructure Query & MCP

**2.1 — Infrastructure Combined DTO**
- Create `InfrastructureItemDto` that unifies `Host` and `Device` with a `Category` discriminator:
  ```csharp
  public record InfrastructureItemDto(
      Guid Id,
      string Name,
      string? IpAddress,
      string Category,         // "compute" | "network" | "firewall"
      string Type,             // original targetType / deviceType
      string Status,           // agent online/offline for hosts, adapter-reported for devices
      string? Model,
      string? FirmwareVersion,
      string Source,            // "Proxmox", "UniFi", "OPNsense", "Manual"
      DateTimeOffset? LastSeenAt
  );
  ```
- New endpoint: `GET /api/v1/infrastructure` returns combined list
- Keep existing `/api/v1/hosts` and `/api/v1/devices` for type-specific queries

**2.2 — MCP Tool Updates**
- Update `list_hosts` tool description to clarify it returns compute nodes only
- Add `list_devices` MCP tool for appliance queries
- Consider a `list_infrastructure` tool that combines both

### Phase 3: Frontend — Infrastructure Page

**3.1 — Rename & Restructure**
- Rename `Hosts` nav item → **Infrastructure** (update sidebar, routes)
- Rename [`HostsPage`](file:///home/ckingdon/projects/homelab-manager/src/frontend/src/features/hosts/) → `InfrastructurePage`
- Add filter tabs: **All** | **Compute Nodes** | **Network Devices** | **Firewalls**

**3.2 — Unified Table**
- Fetch from `GET /api/v1/infrastructure` (or combine `/hosts` + `/devices` client-side)
- Table columns: Name, IP, Type, Status, Source, Last Seen, Actions
- **Type column** shows badges: `Compute`, `Switch`, `AP`, `Gateway`, `Firewall`
- **Actions column** renders conditionally:
  - Compute nodes: Adopt, Reboot, View Vitals, Edit
  - Network devices: PoE Cycle, Firmware Update, View in Adapter
  - Firewalls: Service Restart, View Status, View in Adapter

**3.3 — Type-Aware Inspector Sheet**
- Clicking any row opens inspector sheet
- **Compute node sections**: Agent Status, System Vitals, Update History, Pipelines, SSH Config
- **Appliance sections**: Adapter Connection, Firmware Info, Device-Specific Info (PoE ports for switches, interfaces for firewalls), Location/Notes/Tags

**3.4 — Discovery Page Updates**
- Rename "Mass Adopt" → **"Add to Infrastructure"** in [`MassAdoptModal.tsx`](file:///home/ckingdon/projects/homelab-manager/src/frontend/src/features/discovery/MassAdoptModal.tsx)
- After import, show toast with correct destination: "Added 3 compute nodes and 2 network devices to Infrastructure"

### Phase 4: Frontend — Settings/Integrations

**4.1 — Move Adapter Config**
- Create `Settings/Integrations` page
- Move adapter connection configuration (credentials, URLs, connection test) from current adapter views to this page
- Each adapter gets a card: Proxmox, Kubernetes, UniFi, OPNsense, Redfish/iDRAC

**4.2 — Clean Up Old Adapter Views**
- Remove device inventory sections from adapter views (now in Infrastructure)
- Keep adapter-specific operational tools that don't fit Infrastructure (e.g., UniFi site management, OPNsense service restart)

### Phase 5: Tests

- **Backend unit tests**: Device CRUD, import routing, adoption guard
- **E2E tests**: Infrastructure page with tabs, inspector sheet for both types, discovery import flow
- **Migration test**: Verify appliance rows removed from `hosts`
- All existing tests must continue passing (305+ backend, 22+ Playwright)

---

## File Impact Summary

### New Files
| File | Purpose |
|---|---|
| `Features/Devices/Device.cs` | Entity |
| `Features/Devices/DeviceService.cs` | CRUD service |
| `Features/Devices/DeviceEndpoints.cs` | API endpoints |
| `Features/Devices/DeviceValidators.cs` | Validation |
| `Features/Infrastructure/InfrastructureEndpoints.cs` | Combined query |
| `frontend/src/features/infrastructure/InfrastructurePage.tsx` | Main page |
| `frontend/src/features/infrastructure/InfrastructureInspector.tsx` | Inspector sheet |
| `frontend/src/features/settings/IntegrationsPage.tsx` | Adapter config |

### Modified Files
| File | Change |
|---|---|
| `ControlPlaneDbContext.cs` | Add `DbSet<Device>` |
| `DiscoveryService.cs` | Route imports to `hosts` or `devices` |
| `NodeAdoptionService.cs` | Add appliance guard |
| `HostValidators.cs` | Remove appliance types |
| `MassAdoptModal.tsx` | Rename to "Add to Infrastructure" |
| Sidebar/routing | Update nav + routes |
| `mockApi.ts` + E2E specs | Add device mocks, update tests |
| MCP tools | Add `list_devices`, update `list_hosts` |

### Deleted/Deprecated
| File | Reason |
|---|---|
| Device sections in `OPNsenseAdaptersView.tsx` | Moves to Infrastructure |
| Device sections in `UniFiAdaptersView.tsx` | Moves to Infrastructure |
| `HostsPage.tsx` | Replaced by `InfrastructurePage.tsx` |
