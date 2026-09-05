# Plan 04: Secrets Management & Encryption at Rest

**Phase:** Phase 2  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 01: Service Discovery](file:///home/ckingdon/projects/homelab-manager/docs/plans/01-service-discovery-proxmox-and-k8s.md), [Plan 02: Modular Pipeline Profiles](file:///home/ckingdon/projects/homelab-manager/docs/plans/02-modular-pipeline-profiles.md), [Plan 03: Snapshot Retention Worker](file:///home/ckingdon/projects/homelab-manager/docs/plans/03-snapshot-retention-worker.md)

---

## 1. Objectives & Overview

Ensure that sensitive adapter credentials, API tokens, and passwords stored in the database are fully protected with authenticated encryption at rest across both cluster and standby modes:

1. **Authenticated Envelope Encryption (AEAD)**:
   * Implement application-layer encryption using **AES-256-GCM** with a 128-bit authentication tag and a 96-bit unique IV/nonce per operation (`System.Security.Cryptography.AesGcm`).
   * Standardized ciphertext envelope format: `enc:v1:<base64-iv>:<base64-ciphertext>:<base64-tag>`.
   * Tamper-proof validation: any modification to ciphertext or authentication tag immediately fails verification with a `CryptographicException`.

2. **Hierarchical Master Key Provider (`ISecurityKeyProvider`)**:
   * **Priority 1 (Environment / Container Secret)**: `CONTROLPLANE_MASTER_KEY` environment variable containing a 32-byte Base64-encoded key (standard in Kubernetes Secrets and Docker environments).
   * **Priority 2 (Workstation / Standby File Key)**: File-based key stored at `~/.controlplane/master.key` (or `%USERPROFILE%/.controlplane/master.key` on Windows).
   * **Auto-Generation (Dev / Standby Bootstrap)**: If no key is supplied, generate a cryptographically secure 256-bit random key, write to disk with restrictive file permissions (`0600` on Unix/Linux), and log a clear administrative warning.

3. **Dual-Topology Invariant (PostgreSQL & SQLite)**:
   * Because encryption occurs at the application service boundary (`ISecretEncryptionService`), encryption and decryption behave identically in:
     * **Cluster Mode** (PostgreSQL in Kubernetes).
     * **Standby Mode** (SQLite on local workstations).
   * Standby CLI backup and migration exports (`GET /api/v1/standby/export`) will serialize encrypted ciphertext without exposing raw credentials in transit or in standby JSON bundles.

4. **Transparent Migration & Backward Compatibility**:
   * Legacy plaintext support: When reading existing `system_settings` records, if the stored string is not prefixed with `enc:v1:`, treat it as legacy plaintext, deserialize cleanly, and automatically encrypt upon the next write.
   * Background one-time database migration worker to encrypt all unencrypted secrets in `system_settings` on application startup.

5. **Strict Masked Egress & Auditability**:
   * The raw decrypted secret is strictly scoped to server-side adapter execution (e.g. `ProxmoxClient`, `IdracClient`, `UnifiClient`).
   * API endpoints (`GET /api/v1/adapters/*/config`) continue to enforce `••••••••` masking and never transmit decrypted secrets to the client.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
└── Features/
    ├── Security/
    │   ├── ISecretEncryptionService.cs
    │   ├── SecretEncryptionService.cs
    │   ├── ISecurityKeyProvider.cs
    │   ├── EnvironmentOrFileKeyProvider.cs
    │   ├── SecurityOptions.cs
    │   ├── SecretsMigrationWorker.cs
    │   └── SecurityEndpoints.cs
    └── Adapters/
        └── Config/
            └── AdapterConfigService.cs (integrate ISecretEncryptionService)

tests/ControlPlane.Api.Tests/
└── SecretEncryptionTests.cs
```

---

## 3. Implementation Steps

### Step 1: Security Options & Key Provider
* Create `SecurityOptions.cs` configuring:
  * `MasterKey`: optional environment/config key string.
  * `KeyFilePath`: optional custom path to key ring file (defaults to `~/.controlplane/master.key`).
  * `AutoGenerateKeyIfMissing`: boolean (default `true` in Dev/Standby, `false` in strict Production).
* Implement `EnvironmentOrFileKeyProvider : ISecurityKeyProvider`:
  * Validates 32-byte (256-bit) key size.
  * Handles POSIX file permission enforcement (`0600`) when writing key file.

### Step 2: AES-256-GCM Encryption Service
* Implement `SecretEncryptionService : ISecretEncryptionService`:
  * `string Encrypt(string plainText)`
  * `string Decrypt(string cipherTextOrPlain)`
  * `bool IsEncrypted(string value)`
* Envelope encoding:
  ```
  enc:v1:<base64(iv)>:<base64(ciphertext)>:<base64(tag)>
  ```
* Safe handling of null or empty values.

### Step 3: Adapter Config Service Integration
* In `AdapterConfigService.cs`:
  * Encrypt `ApiTokenSecret` before persisting to `SystemSetting.ValueJson`.
  * Decrypt `ApiTokenSecret` when reading stored configuration for adapter clients.
  * Ensure `GetProxmoxConfigAsync()` returns masked `••••••••` to callers while caching or supplying the decrypted secret only to `IProxmoxClient`.

### Step 4: Startup Secrets Migration Worker
* Implement `SecretsMigrationWorker : IHostedService`:
  * On startup, inspects `system_settings` where keys start with `adapter:`.
  * If unencrypted secrets are found, encrypts them and commits an update with structured logging:
    `"Migrated legacy plaintext adapter secret '{SettingKey}' to AES-256-GCM encryption."`

### Step 5: Test Suite
* Create `tests/ControlPlane.Api.Tests/SecretEncryptionTests.cs`:
  * Test roundtrip encryption and decryption of secrets.
  * Test tamper detection: mutating one byte of ciphertext or tag throws `CryptographicException`.
  * Test legacy plaintext fallback.
  * Test key provider environment variable resolution and file persistence.
  * Test `AdapterConfigService` persistence produces `enc:v1:...` in the database.

---

## 4. Acceptance Criteria & Verification

* [ ] `ISecretEncryptionService` encrypts arbitrary secrets using AES-256-GCM.
* [ ] Tampered ciphertext or tag fails authentication tag verification.
* [ ] Legacy plaintext values are handled transparently and re-encrypted on write.
* [ ] Database query on `system_settings` confirms no raw secret tokens exist in `value_json`.
* [ ] Proxmox adapter probe and discovery continue to function seamlessly using the decrypted secret.
* [ ] `dotnet test` passes with 100% test suite success.
