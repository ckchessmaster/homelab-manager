# Plan 03: Ubiquiti UniFi Network Container Adapter

This plan implements the **Ubiquiti UniFi Adapter**, connecting ControlPlane directly to a self-hosted **UniFi Network Application** (container or appliance). It provides session/cookie-based authentication, switch and access point inventory, firmware upgrade orchestration, device power restarts, and switch PoE port control for managing homelab hardware.

---

## 🎯 Objectives & Acceptance Criteria

1. **UniFi Controller Configuration & Session Authentication:**
   - Store encrypted credentials in `SystemSettings` under `adapters:unifi:instances` via `ISecretEncryptionService`.
   - Support self-hosted UniFi Network Application containers (e.g. `linuxserver/unifi-network-application`, official UniFi container) running on custom ports (e.g., `8443`, `443`).
   - Implement cookie/session authentication (`POST /api/auth/login` for UniFi OS / Network 8.x+ and `/api/login` fallback for legacy controllers) with automatic re-authentication upon session expiration.
   - Endpoints:
     - `GET /api/v1/adapters/unifi/instances`: List configured controllers.
     - `POST /api/v1/adapters/unifi/instances`: Add or update controller configuration.
     - `DELETE /api/v1/adapters/unifi/instances/{id}`: Remove controller.
     - `POST /api/v1/adapters/unifi/instances/{id}/test-connection`: Validate credentials, retrieve controller version and site list.

2. **UniFi Dynamic Client & Device Telemetry:**
   - Implement `IUniFiClientFactory` and `UniFiClient` with custom SSL certificate handler (supporting self-signed homelab certs).
   - Retrieve all managed devices (`GET /api/s/{site}/stat/device`):
     - Switches, APs, Gateways, and Dream Machines.
     - Model, MAC address, IP address, uptime, current firmware version, upgrade available status, and temperature/fan metrics.

3. **Switch PoE Port Management:**
   - Query switch port table (`port_table`) including link status, speed, power draw (Watts), and PoE mode (`auto`, `passthrough`, `off`).
   - Action endpoint: `POST /api/v1/adapters/unifi/instances/{id}/devices/{deviceMac}/ports/{portIdx}/power-cycle`:
     - Cycles PoE power to reboot attached devices (e.g., PoE-powered Raspberry Pis, camera nodes, mini-PCs).

4. **Device Lifecycle Operations:**
   - Device Restart: `POST /api/v1/adapters/unifi/instances/{id}/devices/{deviceMac}/restart`.
   - Firmware Upgrade: `POST /api/v1/adapters/unifi/instances/{id}/devices/{deviceMac}/upgrade` (initiates rolling firmware upgrade with status monitoring).

5. **Host Port Binding & Client Discovery:**
   - Read active clients (`GET /api/s/{site}/stat/sta`) to correlate host MAC and IP addresses with physical switch ports (`unifi_switch_mac`, `unifi_switch_port`).
   - Push newly detected network clients into `DiscoveryService` for 1-click adoption.

6. **Frontend UniFi Adapter UI:**
   - Tab in `AdaptersView.tsx`: `UniFiAdaptersView.tsx`.
   - Controller status cards showing firmware, adopted device counts, and client count.
   - Device inventory table with quick action dropdowns (`Restart`, `Upgrade Firmware`).
   - Visual switch port diagram with PoE state indicators and 1-click power cycle buttons.
   - `AddUniFiModal.tsx` with pre-flight connection test.

---

## 📁 Target File Structure

```
src/ControlPlane.Api/
├── Features/
│   └── Adapters/
│       └── UniFi/
│           ├── IUniFiClient.cs
│           ├── UniFiClient.cs
│           ├── IUniFiClientFactory.cs
│           ├── UniFiClientFactory.cs
│           ├── UniFiModels.cs
│           └── UniFiEndpoints.cs
└── Storage/Entities/
    └── Host.cs (uses existing UnifiSwitchMac and UnifiSwitchPort)

src/frontend/src/
├── api/
│   └── unifi.ts
├── features/adapters/
│   └── unifi/
│       ├── UniFiAdaptersView.tsx
│       ├── AddUniFiModal.tsx
│       ├── SwitchPortVisualizer.tsx
│       └── useUniFi.ts
```

---

## 📋 Task Checklist

- [x] Define UniFi DTOs (`UniFiInstanceDto`, `UniFiDeviceDto`, `UniFiPortDto`, `UniFiMacLease`) in `UniFiModels.cs` and `AdapterConfigModels.cs`.
- [x] Implement `IUniFiClientFactory` and `UniFiClient` handling login sessions, CSRF tokens, and cookie persistence.
- [x] Implement controller CRUD endpoints and `/test-connection` in `UniFiEndpoints.cs`.
- [x] Implement device inventory and control endpoints (`/devices`, `/restart`, `/upgrade`).
- [x] Implement switch port inspection and PoE power-cycling endpoint (`/ports/{portIdx}/power-cycle`).
- [x] Update `DiscoveryService.cs` to discover network clients via UniFi controller tables.
- [x] Create frontend API client in `src/frontend/src/api/unifi.ts` and React Query hooks in `useUniFi.ts`.
- [x] Build `UniFiAdaptersView.tsx`, `AddUniFiModal.tsx`, and `SwitchPortVisualizer.tsx`.
- [x] Write backend unit tests in `ControlPlane.Api.Tests` (`UniFiMultiInstanceTests.cs`).
- [x] Verify `dotnet test` (all 169 tests pass) and `npm run build` (builds cleanly).
