# Plan 01: Database Reorganization & Schema Isolation

**Phase:** Phase 3 (Temporal Durable Execution & Node-Based Visual Workflow Redesign)  
**Status:** ✅ Completed  
**Dependencies:** [Phase 2 Archive](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase2/)

---

## 1. Objectives & Overview

Reorganize the storage layer to achieve complete database and schema isolation between application state and the upcoming Temporal durable orchestration engine:

1. **Multi-Database PostgreSQL Topology (Aspire)**:
   * In `ControlPlane.AppHost`, split the single PostgreSQL resource into two distinct logical databases:
     * **`controlplane` (`ControlPlaneDatabase`)**: Dedicated database for all application entities (`hosts`, `update_jobs`, `step_logs`, `cluster_leases`, `system_settings`).
     * **`temporal` (`TemporalDatabase`)**: Dedicated database reserved for the Temporal server persistence layer.
2. **Dedicated Application Schema (`controlplane`)**:
   * Configure EF Core `ControlPlaneDbContext` with an explicit default schema:
     * When running on PostgreSQL: `modelBuilder.HasDefaultSchema("controlplane")`.
     * When running on SQLite (Standby Mode): Omit schema designation (SQLite does not support SQL schemas).
   * Ensure `InitializeDatabaseAsync` automatically provisions the `controlplane` schema (`CREATE SCHEMA IF NOT EXISTS controlplane;`) prior to applying migrations.
3. **Standby Mode Isolation Preservation**:
   * Preserve SQLite standalone storage at `~/.controlplane/standby-state.db`.
   * Ensure schema configuration does not break SQLite compatibility under the dual-topology invariant.
4. **Configuration & Service Wiring**:
   * Update `ControlPlane.Api` connection string references from `PostgresDatabase` to `ControlPlaneDatabase` (retaining backward-compatible fallback).
   * Verify all existing integration tests run cleanly against the new configuration.

---

## 2. Target File Structure

```
src/Aspire/ControlPlane.AppHost/
└── AppHost.cs                          # Provision ControlPlaneDatabase and TemporalDatabase

src/ControlPlane.Api/
├── Storage/
│   ├── ControlPlaneDbContext.cs       # Configure HasDefaultSchema("controlplane") for PostgreSQL
│   ├── DependencyInjection.cs         # Update connection string resolution and ensure schema creation
│   └── ControlPlaneDbContextFactory.cs # Update design-time factory connection string default
└── appsettings.Development.json        # Update local development connection string reference
```

---

## 3. Implementation Details

### Step 1: Update Aspire `AppHost.cs` Database Resources
* In `src/Aspire/ControlPlane.AppHost/AppHost.cs`:
  ```csharp
  var postgres = builder.AddPostgres("postgres")
      .WithDataVolume()
      .WithPgAdmin();

  var controlPlaneDb = postgres.AddDatabase("ControlPlaneDatabase", "controlplane");
  var temporalDb = postgres.AddDatabase("TemporalDatabase", "temporal");

  var api = builder.AddProject<Projects.ControlPlane_Api>("api")
      .WithReference(controlPlaneDb)
      .WaitFor(controlPlaneDb)
      ...
  ```

### Step 2: Configure Schema in `ControlPlaneDbContext.cs`
* In `src/ControlPlane.Api/Storage/ControlPlaneDbContext.cs`:
  ```csharp
  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
      base.OnModelCreating(modelBuilder);

      if (!Database.IsSqlite())
      {
          modelBuilder.HasDefaultSchema("controlplane");
      }

      // Entity mappings...
  }
  ```

### Step 3: Ensure Schema Creation in `DependencyInjection.cs`
* In `src/ControlPlane.Api/Storage/DependencyInjection.cs`:
  * Update connection string lookup:
    ```csharp
    var connectionString = config.GetConnectionString("ControlPlaneDatabase")
        ?? config.GetConnectionString("PostgresDatabase")
        ?? throw new InvalidOperationException("Connection string 'ControlPlaneDatabase' not found.");
    ```
  * In `InitializeDatabaseAsync`:
    ```csharp
    if (!context.Database.IsSqlite())
    {
        logger.LogInformation("Ensuring 'controlplane' schema exists in PostgreSQL.");
        await context.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS controlplane;", cancellationToken);
        logger.LogInformation("Applying PostgreSQL migrations.");
        await context.Database.MigrateAsync(cancellationToken);
    }
    ```

### Step 4: Verify Design-Time and Test Compatibility
* Update `ControlPlaneDbContextFactory.cs` default connection string to point to database `controlplane`.
* Ensure in-memory SQLite and SQLite tests (`tests/ControlPlane.Api.Tests/`) execute without schema errors.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# 1. Build the entire solution
dotnet build

# 2. Run all unit and integration tests
dotnet test

# 3. Verify SQLite standby initialization
STANDBY_MODE=true dotnet run --project src/ControlPlane.Api --no-build -- --urls "http://localhost:5298" &
PID=$!
sleep 3
curl -s http://localhost:5298/healthz || curl -s http://localhost:5298/api/status
kill -9 $PID
```

### Acceptance Criteria
- [ ] Aspire `AppHost.cs` defines distinct `ControlPlaneDatabase` (`controlplane`) and `TemporalDatabase` (`temporal`) resources on PostgreSQL.
- [ ] EF Core PostgreSQL models reside under the `controlplane` schema (`controlplane.hosts`, `controlplane.update_jobs`, etc.).
- [ ] Standby SQLite mode operates cleanly without SQL schema errors.
- [ ] All automated tests pass.
