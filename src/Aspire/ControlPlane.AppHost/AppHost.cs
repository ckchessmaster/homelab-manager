var builder = DistributedApplication.CreateBuilder(args);

var apiKey = builder.AddParameter("api-key", secret: true);
var masterKey = builder.AddParameter("master-key", secret: true);
var enableMcpServer = builder.AddParameter("enable-mcp-server", "true");

var externalOidcAuthority = builder.Configuration["Oidc:Authority"]
    ?? builder.Configuration["oidc-authority"]
    ?? builder.Configuration["OIDC_AUTHORITY"];

var oidcClientId = builder.Configuration["Oidc:ClientId"]
    ?? builder.Configuration["oidc-client-id"]
    ?? builder.Configuration["OIDC_CLIENT_ID"]
    ?? "controlplane";

var isDemoEnabled = string.Equals(Environment.GetEnvironmentVariable("DEMO_MODE"), "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration["DEMO_MODE"], "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration["demo-mode"], "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration["demoMode"], "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration["demo"], "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration["Demo:Enabled"], "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration["Parameters:demo-mode"], "true", StringComparison.OrdinalIgnoreCase);

builder.AddParameter("demo-mode", isDemoEnabled ? "true" : "false");

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var controlPlaneDb = postgres.AddDatabase("ControlPlaneDatabase", "controlplane");
var temporalDb = postgres.AddDatabase("TemporalDatabase", "temporal");

var externalTemporalEndpoint = builder.Configuration["Temporal:Endpoint"]
    ?? builder.Configuration["Temporal:Address"]
    ?? builder.Configuration["Temporal:ServerUrl"]
    ?? builder.Configuration["temporal-endpoint"];

IResourceBuilder<ContainerResource>? temporal = null;
if (string.IsNullOrWhiteSpace(externalTemporalEndpoint))
{
    temporal = builder.AddContainer("temporal", "temporalio/temporal")
        .WithArgs("server", "start-dev", "--ip", "0.0.0.0", "--port", "7233", "--ui-port", "8233", "--db-filename", "/home/temporal/temporal.db", "--namespace", "homelab-manager")
        .WithVolume("temporal-data", "/home/temporal")
        .WithHttpEndpoint(port: 7233, targetPort: 7233, name: "grpc")
        .WithHttpEndpoint(port: 8233, targetPort: 8233, name: "ui");
}

// Local mock OIDC provider (Dex) when no external OIDC / Authentik authority is specified
IResourceBuilder<ContainerResource>? dex = null;
string oidcAuthority;

if (string.IsNullOrWhiteSpace(externalOidcAuthority))
{
    var dexConfigPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "dex/config.yaml"));
    dex = builder.AddContainer("dex", "ghcr.io/dexidp/dex", "v2.41.1")
        .WithArgs("dex", "serve", "/etc/dex/cfg/config.yaml")
        .WithBindMount(dexConfigPath, "/etc/dex/cfg/config.yaml", isReadOnly: true)
        .WithHttpEndpoint(port: 8085, targetPort: 5556, name: "http");

    oidcAuthority = "http://localhost:8085/dex";
}
else
{
    oidcAuthority = externalOidcAuthority;
}

var api = builder.AddProject<Projects.ControlPlane_Api>("api")
    .WithReference(controlPlaneDb)
    .WaitFor(controlPlaneDb)
    .WithHttpEndpoint(port: 5029, targetPort: 5029, isProxied: false)
    .WithEnvironment("ASPNETCORE_URLS", "http://0.0.0.0:5029")
    .WithEnvironment("ControlPlane__ApiKey", apiKey)
    .WithEnvironment("CONTROLPLANE_MASTER_KEY", masterKey)
    .WithEnvironment("ENABLE_MCP_SERVER", enableMcpServer)
    .WithEnvironment("Oidc__Authority", oidcAuthority)
    .WithEnvironment("Oidc__Audience", "controlplane")
    .WithEnvironment("Oidc__ClientId", oidcClientId)
    .WithEnvironment("Oidc__DisplayName", string.IsNullOrWhiteSpace(externalOidcAuthority) ? "Single Sign-On (Dex)" : "Authentik")
    .WithEnvironment("Oidc__RoleClaimType", "groups")
    .WithEnvironment("Oidc__RequireHttpsMetadata", string.IsNullOrWhiteSpace(externalOidcAuthority) ? "false" : "true")
    .WithEnvironment("DEMO_MODE", isDemoEnabled ? "true" : "false");

if (dex != null)
{
    api.WaitFor(dex);
}

if (temporal != null)
{
    api.WithReference(temporal.GetEndpoint("grpc"))
       .WaitFor(temporal)
       .WithEnvironment("Temporal__Address", temporal.GetEndpoint("grpc"))
       .WithEnvironment("Temporal__ServerUrl", temporal.GetEndpoint("grpc"));
}
else
{
    api.WithEnvironment("Temporal__Address", externalTemporalEndpoint!)
       .WithEnvironment("Temporal__ServerUrl", externalTemporalEndpoint!);
}

api.WithEnvironment("Temporal__Namespace", builder.Configuration["Temporal:Namespace"] ?? "homelab-manager")
   .WithEnvironment("Temporal__TaskQueue", builder.Configuration["Temporal:TaskQueue"] ?? "homelab-manager-tasks");

if (!string.IsNullOrWhiteSpace(builder.Configuration["Temporal:Auth:TokenUrl"]))
{
    api.WithEnvironment("Temporal__Auth__Enabled", builder.Configuration["Temporal:Auth:Enabled"] ?? "true")
       .WithEnvironment("Temporal__Auth__TokenUrl", builder.Configuration["Temporal:Auth:TokenUrl"]!)
       .WithEnvironment("Temporal__Auth__ClientId", builder.Configuration["Temporal:Auth:ClientId"] ?? "")
       .WithEnvironment("Temporal__Auth__ClientSecret", builder.Configuration["Temporal:Auth:ClientSecret"] ?? "")
       .WithEnvironment("Temporal__Auth__Scopes", builder.Configuration["Temporal:Auth:Scopes"] ?? "openid");
}

var frontend = builder.AddViteApp("frontend", "../../frontend")
    .WithNpm(install: false)
    .WithHttpEndpoint(port: 5173, targetPort: 5173, isProxied: false)
    .WithReference(api)
    .WaitFor(api)
    .WithEnvironment("VITE_OIDC_AUTHORITY", oidcAuthority)
    .WithEnvironment("VITE_OIDC_CLIENT_ID", oidcClientId)
    .WithEnvironment("VITE_OIDC_DISPLAY_NAME", string.IsNullOrWhiteSpace(externalOidcAuthority) ? "Single Sign-On (Dex)" : "Authentik")
    .WithEnvironment("VITE_DEMO_MODE", isDemoEnabled ? "true" : "false")
    .WithExternalHttpEndpoints();

if (dex != null)
{
    frontend.WaitFor(dex);
}

builder.Build().Run();
