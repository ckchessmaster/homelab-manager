# Plan 02: Data Layer & Dual-Provider Storage (PostgreSQL & SQLite)

**Target Milestone:** Milestone 1 (Foundation, Data Layer & Host Inventory)  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 01: Solution Scaffolding](file:///home/ckingdon/projects/homelab-manager/docs/plans/01-solution-scaffolding.md)  

---

## 1. Objectives & Overview

Implement the persistence tier using Entity Framework Core with dynamic runtime provider switching:
1. Model core entities: `Host`, `UpdateJob`, `StepLog`, and `ClusterLease` as defined in the Technical Design Document.
2. Implement `ControlPlaneDbContext` with portable table and column mappings.
3. Configure dual-provider support in `AddControlPlaneStorage`:
   * In-cluster mode: `Npgsql.EntityFrameworkCore.PostgreSQL` with connection retry.
   * Standby mode (`STANDBY_MODE=true`): `Microsoft.EntityFrameworkCore.Sqlite` pointed at `~/.controlplane/standby-state.db`.
4. Generate initial migrations and ensure seamless database schema creation/upgrade on application startup.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
├── Storage/
│   ├── ControlPlaneDbContext.cs
│   ├── DependencyInjection.cs
│   ├── Entities/
│   │   ├── Host.cs
│   │   ├── UpdateJob.cs
│   │   ├── StepLog.cs
│   │   └── ClusterLease.cs
│   └── Configurations/
│       ├── HostConfiguration.cs
│       ├── UpdateJobConfiguration.cs
│       ├── StepLogConfiguration.cs
│       └── ClusterLeaseConfiguration.cs
└── Migrations/
```

---

## 3. Implementation Details

### Step 1: Entity Models
Define strong C# records/classes with non-nullable invariants:
* **`Host`**: `Id` (Guid), `Hostname`, `FriendlyName`, `IpAddress`, `OsFamily` (`linux_debian`, `linux_rhel`, `windows`), `TargetType` (`baremetal`, `proxmox_vm`, `proxmox_lxc`), correlation fields (`ProxmoxNode`, `ProxmoxVmid`, `IdracIp`, `UnifiSwitchMac`, `UnifiSwitchPort`), and agent runtime fields (`AgentInstalled`, `AgentVersion`, `AgentLastSeenAt`, `PendingReboot`, `UpgradablePackagesCount`).
* **`UpdateJob`**: `Id`, `TargetHostId`, `InitiatedBy`, `Status` (enum/string: `Pending`, `Running`, `Verifying`, `Completed`, `Failed`, `RolledBack`), `ActiveStep`, `SnapshotIdentifier`, `StartedAt`, `CompletedAt`, `FailureReason`.
* **`StepLog`**: `Id` (long), `JobId`, `SequenceId`, `StreamType` (`stdout`, `stderr`, `system`), `LogLine`, `Timestamp`.
* **`ClusterLease`**: `LeaseKey` (PK), `HolderIdentifier`, `AcquiredAt`, `ExpiresAt`.

### Step 2: EF Core Fluent Configurations
* Ensure indexes:
  * `step_logs`: Composite index on `(JobId, SequenceId)` for fast stream retrieval.
  * `hosts`: Unique index on `Hostname` and `IpAddress`.
* Portable types:
  * Map `Guid` to provider-appropriate representations.
  * Store timestamps as UTC (`DateTimeOffset`).

### Step 3: Provider Selection Dependency Injection
In `Storage/DependencyInjection.cs`:
```csharp
public static IServiceCollection AddControlPlaneStorage(this IServiceCollection services, IConfiguration config)
{
    var isStandby = config.GetValue<bool>("STANDBY_MODE", false);

    services.AddDbContext<ControlPlaneDbContext>(options =>
    {
        if (isStandby)
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                ".controlplane", 
                "standby-state.db"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            options.UseSqlite($"Data Source={dbPath}");
        }
        else
        {
            var connectionString = config.GetConnectionString("PostgresDatabase");
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
            });
        }
    });

    return services;
}
```

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Verify DbContext compilation and migration creation
dotnet ef migrations add InitialCreate --project src/ControlPlane.Api

# Verify SQLite initialization in standby mode
STANDBY_MODE=true dotnet run --project src/ControlPlane.Api
```

### Acceptance Criteria
- [ ] `ControlPlaneDbContext` compiles and applies migrations cleanly.
- [ ] Running with `STANDBY_MODE=true` creates and seeds `~/.controlplane/standby-state.db`.
- [ ] Running in standard mode connects successfully to PostgreSQL.
- [ ] Basic unit/integration tests verify inserting and querying `Host` and `UpdateJob` entities on both SQLite and PostgreSQL.
