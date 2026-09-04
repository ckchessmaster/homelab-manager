var builder = DistributedApplication.CreateBuilder(args);

var apiKey = builder.AddParameter("api-key", secret: true);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var db = postgres.AddDatabase("PostgresDatabase");

var api = builder.AddProject<Projects.ControlPlane_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    .WithHttpEndpoint(port: 5029, targetPort: 5029, isProxied: false)
    .WithEnvironment("ASPNETCORE_URLS", "http://0.0.0.0:5029")
    .WithEnvironment("ControlPlane__ApiKey", apiKey);

builder.AddViteApp("frontend", "../../frontend")
    .WithNpm(install: false)
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
