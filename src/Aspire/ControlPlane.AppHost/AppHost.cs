var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var db = postgres.AddDatabase("PostgresDatabase");

var api = builder.AddProject<Projects.ControlPlane_Api>("api")
    .WithReference(db)
    .WaitFor(db);

builder.AddViteApp("frontend", "../../frontend")
    .WithNpm(install: false)
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
