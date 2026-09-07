var builder = DistributedApplication.CreateBuilder(args);

var apiKey = builder.AddParameter("api-key", secret: true);
var masterKey = builder.AddParameter("master-key", secret: true);
var enableMcpServer = builder.AddParameter("enable-mcp-server", "true");

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var controlPlaneDb = postgres.AddDatabase("ControlPlaneDatabase", "controlplane");
var temporalDb = postgres.AddDatabase("TemporalDatabase", "temporal");

var temporal = builder.AddContainer("temporal", "temporalio/temporal")
    .WithArgs("server", "start-dev", "--ip", "0.0.0.0", "--port", "7233", "--ui-port", "8233")
    .WithHttpEndpoint(port: 7233, targetPort: 7233, name: "grpc")
    .WithHttpEndpoint(port: 8233, targetPort: 8233, name: "ui");

var api = builder.AddProject<Projects.ControlPlane_Api>("api")
    .WithReference(controlPlaneDb)
    .WaitFor(controlPlaneDb)
    .WithReference(temporal.GetEndpoint("grpc"))
    .WaitFor(temporal)
    .WithHttpEndpoint(port: 5029, targetPort: 5029, isProxied: false)
    .WithEnvironment("ASPNETCORE_URLS", "http://0.0.0.0:5029")
    .WithEnvironment("ControlPlane__ApiKey", apiKey)
    .WithEnvironment("CONTROLPLANE_MASTER_KEY", masterKey)
    .WithEnvironment("Temporal__ServerUrl", temporal.GetEndpoint("grpc"))
    .WithEnvironment("ENABLE_MCP_SERVER", enableMcpServer);

builder.AddViteApp("frontend", "../../frontend")
    .WithNpm(install: false)
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
