# Plan 05: OPNsense Gateway & Out-of-Band (BMC / iDRAC) Adapters

This plan implements two crucial homelab infrastructure adapters: the **OPNsense Firewall & Gateway Adapter** and the **Out-of-Band BMC (Dell iDRAC / IPMI / Redfish) Adapter**. These adapters provide deep network visibility, automated DHCP lease discovery for instant adoption, firewall service management, and remote hardware power control for physical servers.

---

## 🎯 Objectives & Acceptance Criteria

1. **OPNsense Core REST API Adapter:**
   - Store encrypted API credentials (`api_key` + `api_secret`) in `SystemSettings` under `adapters:opnsense:instances` via `ISecretEncryptionService`.
   - Support self-signed TLS certificates and custom HTTPS ports.
   - Implement `IOPNsenseClientFactory` and dynamic `OPNsenseClient` using HTTP Basic authentication over HTTPS.
   - Endpoints:
     - `GET /api/v1/adapters/opnsense/instances`: List configured OPNsense firewalls.
     - `POST /api/v1/adapters/opnsense/instances`: Add or update firewall configuration.
     - `DELETE /api/v1/adapters/opnsense/instances/{id}`: Remove firewall configuration.
     - `POST /api/v1/adapters/opnsense/instances/{id}/test-connection`: Ping API, validate credentials, retrieve OPNsense version and system hostname.

2. **OPNsense Live Telemetry & Service Control:**
   - Query gateway status, WAN/LAN interfaces, public IP, and packet gateway latency.
   - Service Management (`/api/core/service/*`):
     - Query status of critical services (`unbound`, `suricata`, `wireguard`, `openvpn`, `dhcpd` / `kea`).
     - 1-click service restart: `POST /api/v1/adapters/opnsense/instances/{id}/services/{serviceName}/restart`.
   - Firmware & Updates (`/api/core/firmware/*`):
     - Query available upgrades and package statuses.
     - Trigger update check and upgrade reboot with maintenance window awareness.

3. **OPNsense DHCP Lease Discovery (Instant Adoption):**
   - Query active DHCP leases (`/api/diagnostics/dhcp/searchLeases` or Kea lease search).
   - Integrate with `DiscoveryService.cs` to discover unmanaged devices by MAC, IP, and DHCP hostname.
   - 1-click adoption creates a managed `Host` with pre-filled IP and hostname.

4. **Out-of-Band BMC / iDRAC Redfish Adapter:**
   - Connect to BMC endpoints (Dell iDRAC, HP iLO, Supermicro BMC) using Redfish REST API (`/redfish/v1/Systems/System.Embedded.1` or standard Redfish chassis).
   - Store encrypted credentials in `SystemSettings` under `adapters:idrac:instances`.
   - Telemetry: Power state (On/Off), health status, chassis temperature, fan RPMs, and power consumption (Watts).
   - Remote Power Actions:
     - `POST /api/v1/adapters/idrac/instances/{id}/power`: `GracefulShutdown`, `ForceOff`, `On`, `PowerCycle`.

5. **Frontend UI Integration:**
   - Tab in `AdaptersView.tsx`: `OPNsenseAdaptersView.tsx` with:
     - Gateway telemetry cards (WAN IP, gateway ping, uptime).
     - Core services grid with status badges and restart buttons.
     - Live DHCP lease discovery table with 1-click adopt buttons.
     - `AddOPNsenseModal.tsx` with live pre-flight connection test.
   - Tab in `AdaptersView.tsx`: `IdracAdaptersView.tsx` with:
     - BMC server cards, thermal/fan meters, and hardware power buttons.
     - `AddIdracModal.tsx` for registering iDRAC/Redfish endpoints.
   - Host Inventory detail modal: Expose direct BMC power buttons for hosts with configured `idrac_ip`.

---

## 📁 Target File Structure

```
src/ControlPlane.Api/
├── Features/
│   └── Adapters/
│       ├── OPNsense/
│       │   ├── IOPNsenseClient.cs
│       │   ├── OPNsenseClient.cs
│       │   ├── IOPNsenseClientFactory.cs
│       │   ├── OPNsenseClientFactory.cs
│       │   ├── OPNsenseModels.cs
│       │   └── OPNsenseEndpoints.cs
│       └── Idrac/
│           ├── IIdracClient.cs
│           ├── IdracClient.cs
│           ├── IIdracClientFactory.cs
│           ├── IdracClientFactory.cs
│           ├── IdracModels.cs
│           └── IdracEndpoints.cs
└── Storage/Entities/
    └── Host.cs (uses existing IdracIp)

src/frontend/src/
├── api/
│   ├── opnsense.ts
│   └── idrac.ts
├── features/adapters/
│   ├── opnsense/
│   │   ├── OPNsenseAdaptersView.tsx
│   │   ├── AddOPNsenseModal.tsx
│   │   └── useOPNsense.ts
│   └── idrac/
│       ├── IdracAdaptersView.tsx
│       ├── AddIdracModal.tsx
│       └── useIdrac.ts
```

---

## 📋 Task Checklist

- [x] Define OPNsense DTOs (`OPNsenseInstanceDto`, `OPNsenseServiceDto`, `OPNsenseDhcpLeaseDto`) in `OPNsenseModels.cs`.
- [x] Implement `IOPNsenseClientFactory` and `OPNsenseClient` using API key + secret Basic auth and self-signed SSL handler.
- [x] Implement OPNsense CRUD endpoints, `/test-connection`, and `/services/{service}/restart` in `OPNsenseEndpoints.cs`.
- [x] Update `DiscoveryService.cs` to ingest active DHCP leases from OPNsense as candidate hosts.
- [x] Define BMC/Redfish DTOs (`IdracInstanceDto`, `ChassisPowerState`, `ThermalSensorDto`) in `IdracModels.cs`.
- [x] Implement `IIdracClientFactory` and `IdracClient` with standard Redfish power and sensor endpoints.
- [x] Implement iDRAC CRUD and power control endpoints (`/power`) in `IdracEndpoints.cs`.
- [x] Create frontend API clients `src/frontend/src/api/opnsense.ts` and `src/frontend/src/api/idrac.ts`.
- [x] Build `OPNsenseAdaptersView.tsx`, `AddOPNsenseModal.tsx`, `IdracAdaptersView.tsx`, and `AddIdracModal.tsx`.
- [x] Connect BMC power actions to physical host details in the inventory view.
- [x] Write backend unit tests in `ControlPlane.Api.Tests` for OPNsense and Redfish clients.
- [x] Verify `dotnet test` and `npm run build`.
