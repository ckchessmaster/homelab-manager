# Plan 14: Out-of-Band Hardware Adapters (Dell iDRAC & Ubiquiti UniFi)

**Target Milestone:** Milestone 4 (Out-of-Band Integrations & Zitadel OIDC)  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 04: Host Inventory API](file:///home/ckingdon/projects/homelab-manager/docs/plans/04-host-inventory-api.md), [Plan 10: DAG Orchestration Engine](file:///home/ckingdon/projects/homelab-manager/docs/plans/10-dag-orchestration-engine.md)  

---

## 1. Objectives & Overview

Integrate physical infrastructure controls to handle bare-metal power cycling and hung network appliances:
1. Implement a **Dell iDRAC Redfish REST API Client**:
   * Authenticate via Basic Auth or Redfish Sessions.
   * Query thermal sensors and chassis power states (`On`, `Off`, `PoweringOn`, `PoweringOff`).
   * Issue chassis power commands: Graceful Restart, Force Restart, Power Cycle.
2. Implement a **Ubiquiti UniFi Controller API Client**:
   * Authenticate against local UniFi OS / Network Application (e.g. UniFi Dream Machine).
   * Perform PoE port power-cycling (PoE bounce) on managed switches for appliances or PoE-powered SBCs.
   * Query MAC-to-IP active lease table to verify hardware reachability.
3. Integrate physical recovery steps into the DAG engine for unrecoverable host hangs.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Features/
    └── Adapters/
        ├── Redfish/
        │   ├── IRedfishClient.cs
        │   ├── RedfishClient.cs
        │   ├── RedfishModels.cs
        │   └── RedfishEndpoints.cs
        └── UniFi/
            ├── IUniFiClient.cs
            ├── UniFiClient.cs
            ├── UniFiModels.cs
            └── UniFiEndpoints.cs
```

---

## 3. Implementation Details

### Step 1: Redfish REST Client (Dell iDRAC)
* Endpoints:
  * System Info: `GET /redfish/v1/Systems/System.Embedded.1`
  * Thermal Vitals: `GET /redfish/v1/Chassis/System.Embedded.1/Thermal`
  * Reset / Power: `POST /redfish/v1/Systems/System.Embedded.1/Actions/ComputerSystem.Reset`
    * Payload: `{"ResetType": "ForceRestart"}` or `{"ResetType": "PushPowerButton"}`
* Handle self-signed SSL certificates gracefully via dedicated `HttpClientHandler`.

### Step 2: UniFi Controller REST Client
* Endpoints:
  * Login: `POST /api/auth/login`
  * Device Port Overrides: `PUT /proxy/network/api/s/{site}/rest/device/{deviceId}`
* Method `CyclePoEPortAsync(switchMac, portNumber, ct)`:
  * Temporarily set port `poe_mode: "off"`.
  * Wait 5 seconds.
  * Restore port `poe_mode: "auto"`.

### Step 3: DAG Recovery Integration
* In `Host` detail view: display iDRAC vitals (temperatures, power draw) and UniFi switch port status.
* Provide an emergency "Hardware Power Cycle" button in the UI for bare-metal servers.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Test iDRAC Redfish power status query
curl http://localhost:5000/api/v1/adapters/redfish/power-state?idracIp=192.168.1.120 \
  -H "X-ControlPlane-Key: dev-secret-key-123"

# Test UniFi PoE port bounce
curl -X POST http://localhost:5000/api/v1/adapters/unifi/bounce-poe \
  -H "Content-Type: application/json" \
  -H "X-ControlPlane-Key: dev-secret-key-123" \
  -d '{"switchMac": "74:83:c2:...", "portNumber": 8}'
```

### Acceptance Criteria
- [ ] Redfish client successfully reads power state and thermal vitals from Dell iDRAC.
- [ ] UniFi client successfully authenticates and triggers PoE bounce on configured switch ports.
- [ ] UI displays hardware vitals for bare-metal hosts with linked BMC credentials.
