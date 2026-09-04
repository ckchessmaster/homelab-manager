var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    status = "healthy",
    service = "ControlPlane.Api",
    timestamp = DateTimeOffset.UtcNow
}));

app.Run();
