var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.ControlPlane_Api>("api");

builder.AddViteApp("frontend", "../../frontend")
    .WithNpm(install: false)
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
