# Plan 07: Standby Mode Temporal CLI Integration (Embedded SQLite Dev Server)

**Phase:** Phase 3 (Temporal Durable Execution & Node-Based Visual Workflow Redesign)  
**Status:** ✅ Completed  
**Dependencies:** [Plan 06: Multi-Node Fleet Rolling Orchestrator & Batch Workflows](file:///home/ckingdon/projects/homelab-manager/docs/plans/06-multi-node-fleet-rolling-orchestration.md)

---

## 1. Objectives & Overview

Achieve **100% workflow parity offline** when running `ControlPlane.Cli` as an autonomous Standby Runner during cluster maintenance windows:

1. **Embedded / Managed Temporal Dev Server**:
   * Integrate a managed child process runner (`TemporalDevServerManager`) into the CLI `serve` command.
   * Runs the official lightweight `temporal server start-dev` process backed by SQLite persistence (`--db-filename ~/.controlplane/temporal-standby.db`).
   * Operates without Docker, Kubernetes, or PostgreSQL—pure single-binary workstation execution.
   * Auto-discovers local or cached `temporal` CLI binary in `~/.controlplane/bin/temporal` or downloads platform-specific binary on demand.
   * Automatically starts and stops with the CLI lifecycle (`IHostApplicationLifetime`).

2. **Temporal Client & Worker Registration in Standby Mode**:
   * Enable `AddTemporalOrchestration` to register when `STANDBY_MODE=true` and `Temporal:Enabled=true`.
   * Configure Temporal gRPC (`localhost:7233`) and Web UI (`localhost:8233`).
   * Host the Temporal Worker in-process inside `ControlPlane.Cli`, running `HostUpgradeWorkflow`, `RollingUpgradeWorkflow`, and all activities against local SQLite state.

3. **Complete Endpoint & Adapter Parity**:
   * Register infrastructure adapters (Proxmox, Kubernetes, Redfish, UniFi, Discovery, Security) and map `MapTemporalWorkflowEndpoints()` in `ServeCommand.cs`.
   * Ensure the Standby runner serves the full `@xyflow/react` Visual DAG Canvas and Parameterized Workflow Launcher from embedded `wwwroot` assets.

4. **CLI Options**:
   * `--start-temporal`: Auto-launch local Temporal dev server (default: `true`).
   * `--temporal-port`: Temporal gRPC port (default: `7233`).
   * `--temporal-ui-port`: Temporal Web UI port (default: `8233`).
   * `--temporal-db`: Path to SQLite database for Temporal history (default: `~/.controlplane/temporal-standby.db`).
   * `--temporal-url`: Direct connection string (default: `127.0.0.1:7233`).
   * `--no-temporal`: Disable Temporal and run in legacy fallback mode.
   * `--temporal-bin`: Explicit path to `temporal` CLI binary.

---

## 2. Target File Structure

```
src/ControlPlane.Cli/
├── Temporal/
│   ├── ITemporalDevServerManager.cs
│   └── TemporalDevServerManager.cs
├── Commands/
│   └── ServeCommand.cs                 # Add Temporal flags, lifecycle, adapter registrations & endpoints
├── ControlPlane.Cli.csproj             # Add pre-build asset copy target for latest frontend dist
└── wwwroot/                            # Synchronized React 19 SPA assets

src/ControlPlane.Api/
└── Features/
    └── Orchestration/
        └── Temporal/
            └── TemporalServiceCollectionExtensions.cs # Enable Temporal when STANDBY_MODE & Temporal:Enabled

tests/ControlPlane.Api.Tests/
├── TemporalDevServerManagerTests.cs    # Unit tests for dev server manager & argument building
└── TemporalConfigurationTests.cs      # Test Standby mode Temporal registration behaviors
```

---

## 3. Verification & Acceptance Criteria

### Verification Steps
```bash
# 1. Build solution and test CLI help
dotnet build
dotnet run --project src/ControlPlane.Cli -- serve --help

# 2. Run Temporal configuration and Standby tests
dotnet test --filter "FullyQualifiedName~Temporal|FullyQualifiedName~Standby"

# 3. Verify frontend assets in CLI
ls -la src/ControlPlane.Cli/wwwroot/assets
```

### Acceptance Criteria
- [x] `TemporalDevServerManager` constructs correct arguments and checks port readiness.
- [x] `ServeCommand` registers `AddTemporalOrchestration` and `MapTemporalWorkflowEndpoints` alongside infrastructure adapters.
- [x] `ServeCommand` exposes `--start-temporal`, `--temporal-port`, `--temporal-ui-port`, `--temporal-db`, and `--no-temporal` options.
- [x] `TemporalServiceCollectionExtensions` enables Temporal client/worker in Standby mode when explicitly requested.
- [x] Latest React 19 visual workflow frontend is synchronized into `wwwroot/`.
- [x] All automated tests pass cleanly.
