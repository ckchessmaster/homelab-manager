# Plan 18: Standby CLI Packaging & Cross-Platform Distribution Scripts

**Target Milestone:** Milestone 5 (Production Hardening, Lifecycle Automation & Distribution)  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 13: Standby CLI Runner](file:///home/ckingdon/projects/homelab-manager/docs/plans/13-standby-cli-and-lease-protocol.md)

---

## 1. Objectives & Overview

Provide automated, repeatable packaging scripts to compile the single-binary Standby Runner CLI with embedded React assets for operator workstations:
1. Create unified build scripts (`scripts/build-cli.sh` for Linux/macOS and `scripts/build-cli.ps1` for Windows).
2. Automate the end-to-end pipeline:
   * Build the production React SPA bundle (`npm --prefix src/frontend run build`).
   * Synchronize the emitted assets into `src/ControlPlane.Cli/wwwroot/`.
   * Compile self-contained, single-file native executables (`linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64`).
3. Add an automated smoke test script verifying CLI startup, embedded file delivery, and SQLite database initialization.

---

## 2. Target File Structure

```
scripts/
├── build-cli.sh
├── build-cli.ps1
└── test-cli-smoke.sh
```

---

## 3. Implementation Details

### Step 1: Bash Release Script (`scripts/build-cli.sh`)
* Check prerequisites (`node`, `npm`, `dotnet` 10 SDK).
* Accept optional flags: `--runtime <rid>` (default: current OS, e.g., `linux-x64`), `--output-dir <path>` (default: `bin/dist`).
* Step A: Build frontend:
  ```bash
  npm --prefix src/frontend ci --prefer-offline || npm --prefix src/frontend install
  npm --prefix src/frontend run build
  ```
* Step B: Copy assets to CLI project:
  ```bash
  rm -rf src/ControlPlane.Cli/wwwroot/*
  cp -r src/frontend/dist/* src/ControlPlane.Cli/wwwroot/
  ```
* Step C: Publish self-contained executable:
  ```bash
  dotnet publish src/ControlPlane.Cli/ControlPlane.Cli.csproj \
    -c Release \
    -r "$RUNTIME" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUTPUT_DIR"
  ```

### Step 2: PowerShell Equivalent (`scripts/build-cli.ps1`)
* Mirror the workflow for Windows administrative workstations compiling `win-x64`.

### Step 3: Automated CLI Smoke Test (`scripts/test-cli-smoke.sh`)
* Run built CLI with `--port 5299` in background.
* Curl `http://localhost:5299` and assert HTTP 200 with `<div id="root">`.
* Curl `http://localhost:5299/alive` and assert HTTP 200 healthy status.
* Terminate test CLI process cleanly.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Build standalone Linux CLI binary
./scripts/build-cli.sh --runtime linux-x64

# Run smoke test on generated artifact
./scripts/test-cli-smoke.sh ./bin/dist/ControlPlane.Cli
```

### Acceptance Criteria
- [ ] Single command compiles production frontend assets, stages embedded files, and publishes self-contained binary.
- [ ] Emitted binary executes without external Node.js or .NET runtime dependencies on the target workstation.
- [ ] Smoke test script verifies automated healthy startup and embedded web dashboard delivery.
