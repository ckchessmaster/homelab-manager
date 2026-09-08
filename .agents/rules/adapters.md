# Infrastructure Adapter Rules & Best Practices

Guidelines for writing, maintaining, and testing agentless Infrastructure Adapters in ControlPlane (`src/ControlPlane.Api/Features/Adapters/`).

---

## 1. Core Adapter Philosophy

* **Agentless Appliance Management:** Infrastructure appliances (hypervisors, switches, BMCs, firewalls, and orchestrators) must never have daemons installed. They are controlled exclusively via their official HTTPS REST, GraphQL, Redfish, or Kubernetes API endpoints.
* **Unified Modular Design:** All adapters implement a consistent pattern:
  1. Config DTOs and Stored Entities in `Features/Adapters/Config/`.
  2. Factory interface (e.g. `IUniFiClientFactory`, `IKubernetesClientFactory`) resolving decrypted credentials and pooled HTTP clients.
  3. Client implementation (e.g. `UniFiClient`, `OPNsenseClient`, `RedfishClient`).
  4. Endpoints mapping under `/api/v1/adapters/{adapterName}`.
  5. Discovery integration in `IDiscoveryService.ScanAsync`.

---

## 2. Credential Security & Encryption

* **Encrypted at Rest:** Passwords, API tokens, and kubeconfigs must be encrypted using `AES-256-GCM` via `ISecretEncryptionService`.
* **Masked in Responses:** When returning adapter instance DTOs, never echo decrypted credentials back to the client or MCP server. Use masked placeholders (`PasswordMasked: "••••••••"`) and booleans (`HasSecret: true`).
* **Preserve Unchanged Secrets:** When receiving a `Save...Request`, if the incoming secret matches the masked placeholder or is empty, retain the existing stored encrypted secret.

---

## 3. Homelab TLS & Certificate Resilience

* Homelab appliances frequently operate with self-signed SSL/TLS certificates or private internal CAs.
* Always configure named `HttpClient` instances with `DangerousAcceptAnyServerCertificateValidator` when `AllowSelfSignedCert: true` (or by default for BMCs/hypervisors).
* Never let TLS certificate validation failures silently crash background workers. Handle TLS exceptions gracefully and return descriptive test results.

---

## 4. Connection Testing & Diagnostics

Every adapter must implement a non-destructive `TestConnectionAsync` method that validates:
1. Endpoint reachability and TLS handshake.
2. Authentication validity (token, session cookie, or basic auth).
3. System identity and version detection (e.g. Proxmox VE version, UniFi controller version, OPNsense firmware version, BMC BIOS/power state).
4. Round-trip network latency in milliseconds.

---

## 5. Destructive Operations Safety

* **Power Cycling & Reboots:** Hardware reset actions (`ResetSystemAsync` on BMCs, `CyclePoEPortAsync` on UniFi, or VM power state on Proxmox) are high-impact.
* Always ensure clear confirmation in UI dialogs and include audit trail tagging (`InitiatedBy = "AI Agent via MCP"` or operator identity) in background tasks.
* Use sensible timeout guards (e.g. 15-30s) on appliance HTTP calls so network timeouts do not hang the orchestrator.
