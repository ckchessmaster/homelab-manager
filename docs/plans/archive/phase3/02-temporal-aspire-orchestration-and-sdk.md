# Plan 02: Temporal Aspire Hosting (`temporalio/dev-server`) & .NET SDK Setup

**Phase:** Phase 3 (Temporal Durable Execution & Node-Based Visual Workflow Redesign)  
**Status:** ✅ Completed  
**Dependencies:** [Plan 01: Database Reorganization & Schema Isolation](file:///home/ckingdon/projects/homelab-manager/docs/plans/01-database-reorganization-and-schema-isolation.md)

---

## 1. Objectives & Overview

Establish the local Temporal orchestration infrastructure in .NET Aspire and integrate the official `Temporalio` .NET SDK into `ControlPlane.Api`:

1. **Aspire Temporal Container Hosting (`temporalio/dev-server`)**:
   * Add the Temporal development server container (`temporalio/dev-server:latest`) to `ControlPlane.AppHost`.
   * Configure container ports:
     * **gRPC Endpoint:** Port `7233` (target port `7233`) for SDK client and worker traffic.
     * **Web UI Endpoint:** Port `8233` (target port `8233`) with UI enabled, surfaced directly as a clickable link in the Aspire Dashboard.
   * Configure the `api` service to reference the `temporal` resource and wait for its readiness.
2. **Temporal .NET 10 SDK Integration**:
   * Add NuGet packages:
     * `Temporalio` (Core SDK)
     * `Temporalio.Extensions.Hosting` (Generic Host worker & DI integration)
   * Configure dependency injection in `ControlPlane.Api`:
     * Register `ITemporalClient` connected to the Aspire-provided Temporal address (with fallback to `localhost:7233`).
     * Register a hosted background worker (`AddHostedTemporalWorker`) listening on the task queue: `"controlplane-orchestration"`.
3. **Temporal Health Check & Diagnostics**:
   * Add a health probe for Temporal client connectivity.
   * Ensure clean graceful shutdown of the Temporal worker when the API application stops.
4. **Integration Test Infrastructure**:
   * Add a verification test confirming `ITemporalClient` connection and basic namespace discovery.

---

## 2. Target File Structure

```
src/Aspire/ControlPlane.AppHost/
└── AppHost.cs                          # Add temporalio/dev-server container resource with gRPC and Web UI

src/ControlPlane.Api/
├── Features/
│   └── Orchestration/
│       └── Temporal/
│           ├── TemporalOptions.cs      # Options configuration for task queue and server URL
│           └── TemporalServiceCollectionExtensions.cs # DI registration for client and hosted worker
├── Program.cs                          # Register Temporal services in API startup
└── ControlPlane.Api.csproj             # Add Temporalio and Temporalio.Extensions.Hosting packages

tests/ControlPlane.Api.Tests/
└── TemporalConnectionTests.cs          # Verification tests for Temporal client initialization
```

---

## 3. Implementation Details

### Step 1: Add Temporal Container to `AppHost.cs`
* In `src/Aspire/ControlPlane.AppHost/AppHost.cs`:
  ```csharp
  var temporal = builder.AddContainer("temporal", "temporalio/dev-server")
      .WithHttpEndpoint(port: 7233, targetPort: 7233, name: "grpc")
      .WithHttpEndpoint(port: 8233, targetPort: 8233, name: "ui")
      .WithArgs("--ip", "0.0.0.0", "--ui-port", "8233");

  var api = builder.AddProject<Projects.ControlPlane_Api>("api")
      .WithReference(controlPlaneDb)
      .WaitFor(controlPlaneDb)
      .WithReference(temporal)
      .WaitFor(temporal)
      ...
  ```

### Step 2: Install SDK Packages in `ControlPlane.Api.csproj`
* Add packages:
  ```xml
  <PackageReference Include="Temporalio" Version="1.5.0" />
  <PackageReference Include="Temporalio.Extensions.Hosting" Version="1.5.0" />
  ```

### Step 3: Configure DI Registration & Hosted Worker
* In `Features/Orchestration/Temporal/TemporalServiceCollectionExtensions.cs`:
  * Add configuration binding: `TemporalOptions` (`ServerUrl`, `Namespace = "default"`, `TaskQueue = "controlplane-orchestration"`).
  * Configure `AddTemporalClient(...)`.
  * Configure `AddHostedTemporalWorker("controlplane-orchestration")`.

### Step 4: Wire in `Program.cs` & Service Defaults
* Ensure `app.MapDefaultEndpoints()` monitors Temporal connectivity.
* Verify Standby mode can run with Temporal disabled or pointing to local dev server.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# 1. Build entire solution
dotnet build

# 2. Run all tests
dotnet test

# 3. Verify AppHost compilation and package references
dotnet build src/Aspire/ControlPlane.AppHost/ControlPlane.AppHost.csproj
```

### Acceptance Criteria
- [ ] Aspire `AppHost.cs` defines `temporal` container using `temporalio/dev-server` exposing ports 7233 (gRPC) and 8233 (UI).
- [ ] `ControlPlane.Api` compiles with `Temporalio` and `Temporalio.Extensions.Hosting`.
- [ ] `ITemporalClient` and hosted worker on task queue `"controlplane-orchestration"` are registered in DI.
- [ ] All unit and integration tests build and pass cleanly.
