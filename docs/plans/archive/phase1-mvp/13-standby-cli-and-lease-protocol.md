# Plan 13: Standby CLI Runner & Lease Synchronization Protocol

**Target Milestone:** Milestone 3 (Update Orchestration Engine & Standby Runner)  
**Status:** ✅ Completed  
**Dependencies:** [Plan 02: Data Layer](file:///home/ckingdon/projects/homelab-manager/docs/plans/02-data-layer-and-storage.md), [Plan 05: Frontend Dashboard](file:///home/ckingdon/projects/homelab-manager/docs/plans/05-frontend-inventory-dashboard.md), [Plan 10: DAG Orchestration Engine](file:///home/ckingdon/projects/homelab-manager/docs/plans/10-dag-orchestration-engine.md)  

---

## 1. Objectives & Overview

Solve the chicken-and-egg dilemma of upgrading the hosting Kubernetes cluster itself:
1. Build `ControlPlane.Cli`: a self-contained, single-file native binary packaging both the .NET runtime and compiled React production assets.
2. Serve the embedded React SPA directly from the CLI via `ManifestEmbeddedFileProvider`.
3. Implement the **Takeover Protocol**:
   * CLI invokes `--takeover --cluster-url https://controlplane.homelab.local`.
   * Pulls idempotent JSON inventory snapshot from in-cluster instance.
   * Seeds local SQLite `~/.controlplane/standby-state.db`.
   * Acquires `GLOBAL_MAINTENANCE_LOCK` distributed lease in cluster PostgreSQL.
   * In-cluster pod switches to read-only pass-through mode.
4. Execute updates completely from the workstation while Kubernetes nodes reboot.
5. Implement **Reconciliation (Delta Sync)**: detect PostgreSQL recovery, push execution deltas, release lease, and restore in-cluster primary operations.

---

## 2. Target File Structure

```
src/ControlPlane.Cli/
├── ControlPlane.Cli.csproj
├── Program.cs
├── Commands/
│   ├── ServeCommand.cs
│   └── TakeoverCommand.cs
├── Synchronization/
│   ├── SnapshotPuller.cs
│   ├── LeaseManager.cs
│   └── DeltaSyncPusher.cs
└── wwwroot/ # Compiled React SPA assets copied at build time
```

---

## 3. Implementation Details

### Step 1: Single-Binary CLI Project & Embedded Web Assets
* Configure `ControlPlane.Cli.csproj`:
  ```xml
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <GenerateEmbeddedFilesManifest>true</GenerateEmbeddedFilesManifest>
  </PropertyGroup>
  <ItemGroup>
    <EmbeddedResource Include="wwwroot\**\*" />
  </ItemGroup>
  ```
* In `Program.cs`, configure embedded file provider:
  ```csharp
  app.UseDefaultFiles();
  app.UseStaticFiles(new StaticFileOptions {
      FileProvider = new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot")
  });
  app.MapFallbackToFile("index.html");
  ```

### Step 2: Takeover Protocol Implementation
1. **CLI Flag Parsing:** Using `System.CommandLine`, parse `serve --takeover --cluster-url <url> --api-key <key>`.
2. **Snapshot Pull:** Call cluster endpoint `GET /api/v1/cluster/export-snapshot`.
   * Returns JSON with `hosts`, `update_jobs`, and credentials.
3. **SQLite Seeding:** Run EF Core `Database.EnsureCreated()` on `~/.controlplane/standby-state.db` and insert snapshot records.
4. **Lease Acquisition:** Call `POST /api/v1/cluster/lease-acquire`:
   * Inserts row into `cluster_leases` with `lease_key = "GLOBAL_MAINTENANCE_LOCK"`, `expires_at = UtcNow + 60m`.
   * Cluster pod sets internal state flag `IsSuspended = true`.

### Step 3: Standby Execution & Delta Reconciliation
* Workstation runs orchestration against local SQLite.
* Background probe checks cluster PostgreSQL reachability every 15 seconds.
* When cluster recovers:
  * Export delta records: new jobs executed on workstation, `step_logs`, and updated host states.
  * Post to `POST /api/v1/cluster/reconcile-delta`.
  * Release `GLOBAL_MAINTENANCE_LOCK`.
  * Cluster resumes primary orchestration.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Build standalone CLI with embedded frontend
npm --prefix src/frontend run build
dotnet publish src/ControlPlane.Cli -c Release -r linux-x64 -o ./bin/dist

# Run standalone CLI
./bin/dist/ControlPlane.Cli serve --port 5200
```

### Acceptance Criteria
- [x] Single executable starts and automatically serves the embedded React dashboard at `http://localhost:5200`.
- [x] CLI runs with zero dependencies on PostgreSQL, using local SQLite cleanly.
- [x] Takeover protocol successfully pulls cluster snapshot and locks primary in-cluster scheduler.
- [x] Post-maintenance delta sync flushes execution history back to PostgreSQL and releases the lease.
- [x] Milestone 3 is fully achieved upon completion of this plan.
