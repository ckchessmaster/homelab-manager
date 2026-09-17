# Plan 06: Home Assistant OS & Appliance Adapter

This plan implements the **Home Assistant Adapter**, enabling agentless management of Home Assistant OS (HAOS), Supervised, and Container appliances. It provides Bearer token authentication, real-time appliance telemetry (Host OS, Core, Supervisor), pre-flight configuration validation, automated full/partial backups, graceful host reboots, and OTA OS updates.

---

## 🎯 Objectives & Acceptance Criteria

1. **Home Assistant Instance Configuration & Secret Encryption:**
   - Store encrypted API credentials (`Token`) in `SystemSettings` under `adapters:homeassistant:instances` via `ISecretEncryptionService` (`AES-256-GCM`).
   - Never echo unmasked tokens in API or MCP responses (`••••••••` masked placeholder, `HasToken: true`).
   - Preserve existing encrypted secret when a save request contains an empty or masked token.
   - Support self-signed TLS certificates and custom HTTP/HTTPS ports (e.g., `8123`, `443`).
   - Endpoints:
     - `GET /api/v1/adapters/home-assistant/instances`: List configured Home Assistant instances.
     - `POST /api/v1/adapters/home-assistant/instances`: Add or update instance configuration.
     - `DELETE /api/v1/adapters/home-assistant/instances/{id}`: Remove instance configuration.
     - `POST /api/v1/adapters/home-assistant/instances/{id}/test-connection`: Validate credentials, test reachability, and retrieve versions & latency.

2. **Home Assistant Client & Supervisor API Integration:**
   - Implement `IHomeAssistantClientFactory` and `HomeAssistantClient` using pooled `HttpClientFactory` with self-signed certificate handler.
   - Query Home Assistant Supervisor & Core endpoints:
     - Host OS info (`GET /api/hassio/host/info`): OS version, kernel, chassis, disk usage, reboot-required flag.
     - OS info (`GET /api/hassio/os/info`): Current version, latest available version, update available, A/B boot slot.
     - Core info (`GET /api/hassio/core/info`): Core version, state, architecture.
     - Supervisor info (`GET /api/hassio/supervisor/info`): Supervisor version, channel, health.
     - Backups (`GET /api/hassio/backups`): List stored backups with timestamps, types, and sizes.

3. **Appliance Lifecycle & Safety Operations:**
   - **Pre-flight Config Check:** `POST /api/v1/adapters/home-assistant/instances/{id}/core/check` (`POST /api/hassio/core/check`) to validate configuration before updates.
   - **Host Reboot:** `POST /api/v1/adapters/home-assistant/instances/{id}/host/reboot` (`POST /api/hassio/host/reboot`) for graceful system reboots.
   - **Core Restart:** `POST /api/v1/adapters/home-assistant/instances/{id}/core/restart`.
   - **Automated Backups:** `POST /api/v1/adapters/home-assistant/instances/{id}/backups` (`POST /api/hassio/backups/new/full`) to create full backups before maintenance.
   - **OS Updates:** `POST /api/v1/adapters/home-assistant/instances/{id}/os/update` (`POST /api/hassio/os/update`) to trigger official OTA updates.

4. **Discovery & Host Correlation:**
   - In `DiscoveryService.cs`, correlate Home Assistant instance IP and hostname with discovered Proxmox VMs and DHCP leases.
   - Link the Home Assistant telemetry to matching hosts in `HostCorrelationService.cs`.

5. **Model Context Protocol (MCP) Server Integration:**
   - Add tools to `ControlPlaneMcpTools.cs`:
     - `list_home_assistant_instances`
     - `test_home_assistant_connection`
     - `get_home_assistant_overview`
     - `check_home_assistant_core_config`
     - `create_home_assistant_backup`
     - `reboot_home_assistant_host`
     - `trigger_home_assistant_update`

6. **Frontend UI Integration:**
   - Add **Home Assistant** tab to `AdaptersView.tsx` with dedicated Lucide `Home` icon.
   - Create `HomeAssistantAdaptersView.tsx` showing:
     - Instance cards with Core, OS, and Supervisor version badges.
     - Update available alert badges with latest version tags.
     - Disk usage meter, reboot-required badge, and A/B boot slot indicator.
     - Quick action buttons: *Check Config*, *Create Backup*, *Reboot Host*, *Update OS*.
     - Backups drawer/modal showing recent backup history.
   - Create `AddHomeAssistantModal.tsx` with pre-flight connection test.

---

## 📁 Target File Structure

```
src/ControlPlane.Api/
├── Features/
│   └── Adapters/
│       ├── Config/
│       │   ├── AdapterConfigModels.cs       # HomeAssistantInstanceDto, SaveHomeAssistantInstanceRequest, HomeAssistantTestResultDto
│       │   ├── IAdapterConfigService.cs     # Home Assistant CRUD & resolver signatures
│       │   └── AdapterConfigService.cs       # Implementation with AES-256-GCM encryption & masked secrets
│       ├── HomeAssistant/
│       │   ├── IHomeAssistantClient.cs      # Core client interface
│       │   ├── HomeAssistantClient.cs       # REST implementation calling /api/hassio/*
│       │   ├── IHomeAssistantClientFactory.cs
│       │   ├── HomeAssistantClientFactory.cs# Pooled client resolver
│       │   ├── HomeAssistantModels.cs       # DTOs: HostInfo, OsInfo, CoreInfo, BackupDto, OverviewDto
│       │   └── HomeAssistantEndpoints.cs    # Minimal API route mappings under /api/v1/adapters/home-assistant
│       ├── Mcp/
│       │   └── ControlPlaneMcpTools.cs      # Home Assistant MCP tools
│       └── Discovery/
│           └── DiscoveryService.cs          # Correlate HA instance candidates
└── Program.cs                               # Service registration & endpoint mapping

src/frontend/src/
├── api/
│   └── homeAssistant.ts                     # TypeScript API client & types
├── features/adapters/
│   ├── AdaptersView.tsx                     # Home Assistant tab addition
│   └── homeAssistant/
│       ├── HomeAssistantAdaptersView.tsx    # Telemetry cards, actions, backups
│       ├── AddHomeAssistantModal.tsx        # Add/Edit instance modal
│       └── useHomeAssistant.ts              # TanStack Query hooks & mutations

tests/ControlPlane.Api.Tests/
└── HomeAssistantAdapterTests.cs             # Unit & integration tests for client, encryption, endpoints
```

---

## 📋 Task Checklist

- [ ] Define Home Assistant DTOs (`HomeAssistantInstanceDto`, `HomeAssistantHostInfoDto`, `HomeAssistantOsInfoDto`, `HomeAssistantCoreInfoDto`, `HomeAssistantBackupDto`, `HomeAssistantOverviewDto`, `HomeAssistantTestResultDto`) in `HomeAssistantModels.cs` and `AdapterConfigModels.cs`.
- [ ] Extend `IAdapterConfigService` and `AdapterConfigService` with Home Assistant instance storage, AES-256-GCM encryption, and secret masking.
- [ ] Implement `IHomeAssistantClient` and `HomeAssistantClient` with Bearer token authentication, timeout handling, and custom TLS certificate validation.
- [ ] Implement `IHomeAssistantClientFactory` and `HomeAssistantClientFactory`.
- [ ] Implement REST endpoints in `HomeAssistantEndpoints.cs` and register them in `Program.cs`.
- [ ] Implement Home Assistant MCP tools in `ControlPlaneMcpTools.cs`.
- [ ] Integrate Home Assistant correlation in `DiscoveryService.cs`.
- [ ] Implement frontend API client in `src/frontend/src/api/homeAssistant.ts` and React Query hooks in `useHomeAssistant.ts`.
- [ ] Build `HomeAssistantAdaptersView.tsx` and `AddHomeAssistantModal.tsx`, integrating into `AdaptersView.tsx`.
- [ ] Write backend unit & integration tests in `tests/ControlPlane.Api.Tests/HomeAssistantAdapterTests.cs`.
- [ ] Verify `dotnet test` (all tests pass) and `npm run build` (clean Vite build).
