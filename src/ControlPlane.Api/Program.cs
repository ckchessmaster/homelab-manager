using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControlPlaneStorage(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();

await app.InitializeDatabaseAsync();

app.MapGet("/", () => Results.Ok(new
{
    status = "healthy",
    service = "ControlPlane.Api",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/api/storage/status", async (ControlPlaneDbContext db) =>
{
    var provider = db.Database.ProviderName;
    var hostCount = await db.Hosts.CountAsync();
    var leaseCount = await db.ClusterLeases.CountAsync();

    return Results.Ok(new
    {
        provider,
        hostCount,
        leaseCount,
        timestamp = DateTimeOffset.UtcNow
    });
});

app.Run();
