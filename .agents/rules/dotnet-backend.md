# .NET 10 & Aspire Backend Rules

Guidelines and conventions for writing C# backend code, ASP.NET Core APIs, and .NET Aspire orchestration in ControlPlane.

## 1. Versioning & Framework Rules
* **Target Framework:** `net10.0` for all C# projects.
* **.NET Aspire:** Target the latest available Aspire workload and package versions.
* > [!IMPORTANT]
  > Aspire APIs and hosting paradigms evolve rapidly between versions. The agent must verify or query the latest official .NET Aspire documentation before introducing or refactoring AppHost integrations or ServiceDefaults.

## 2. Project Layout
* `ControlPlane.AppHost`: Aspire orchestrator project. Defines PostgreSQL container resource, backend API resource, and frontend Vite project resource.
* `ControlPlane.ServiceDefaults`: Reusable extension methods for OpenTelemetry metrics/tracing, health check endpoints (`/health`, `/alive`), and HTTP resilience.
* `ControlPlane.Api`: ASP.NET Core Web API / BFF. Exposes REST endpoints, SignalR hubs (`/hubs/jobs`), WebSocket agent hub (`/agent-hub`), MCP server (`/mcp`), and EF Core storage.
* `ControlPlane.Cli`: Single-binary workstation CLI packaged for self-contained execution with embedded frontend assets and local SQLite.

## 3. C# Language & Coding Patterns
* **Nullable Reference Types:** Enabled project-wide. Never ignore CS8600/CS8602/CS8603 warnings without explicit defensive checks or domain justification.
* **File-Scoped Namespaces:** Always use file-scoped namespace declarations:
  ```csharp
  namespace ControlPlane.Api.Features.Hosts;
  ```
* **Primary Constructors & Records:** Utilize primary constructors for dependency injection in classes and use C# records for DTOs and command envelopes:
  ```csharp
  public class HostService(ControlPlaneDbContext db, ILogger<HostService> logger)
  {
      // ...
  }
  public record CreateHostRequest(string Hostname, string IpAddress, string OsFamily, string TargetType);
  ```
* **Cancellation Tokens:** Every asynchronous method handling I/O (EF Core, HTTP, SignalR, WebSockets, MCP) must accept `CancellationToken cancellationToken = default` and pass it to underlying async calls.

## 4. Entity Framework Core Guidelines
* **Dual-Provider Configuration:** Ensure all migrations and model configurations work identically under `Npgsql` and `Sqlite`.
* **No Tracking When Reading:** Use `.AsNoTracking()` for read-only queries to maximize performance and avoid memory leaks during heavy polling or dashboard updates.
* **Resilience:** Configure retry on failure with exponential backoff on Npgsql connections.

## 5. Security, Authentication & RBAC
* **Central Identity:** Zitadel OIDC provides JWT Bearer tokens validated by ASP.NET Core `JwtBearerHandler`.
* **Authorization Policies:** Apply role gates using constants from `ControlPlane.Api.Security.AuthConstants`:
  * `RequireViewer`: Read-only queries (`GET /api/v1/workloads`, `GET /api/v1/hosts`).
  * `RequireOperator`: Operational tasks (`POST /api/v1/workloads/.../restart`, `POST /api/v1/jobs`).
  * `RequireAdmin`: Configuration & secrets (`POST /api/v1/adapters/.../instances`).
* **Secret Encryption:** Never store plaintext appliance passwords or tokens. Use `ISecretEncryptionService` with AES-256-GCM. Mask all secrets in DTO responses (`HasSecret: true`, `PasswordMasked: "••••••••"`).
* **Self-Signed Homelab Appliances:** When registering HTTP clients for Proxmox, UniFi, OPNsense, or Redfish, configure handlers with `DangerousAcceptAnyServerCertificateValidator` to prevent TLS errors against self-signed homelab certs.

## 6. Model Context Protocol (MCP) Tool Standards
* Mark MCP classes with `[McpServerToolType]` and methods with `[McpServerTool]`.
* Decorate all tools and parameters with `[Description("...")]` annotations so AI models understand arguments and schemas.
* Register MCP tool classes with `Scoped` lifecycle. Inject dependencies as optional (`= null`) in constructors when needed to maintain backward compatibility in integration tests.
* Return structured objects containing `success: bool`, descriptive messages, and relevant state.
