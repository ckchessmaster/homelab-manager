using ControlPlane.Api.Features.Demo;
using ControlPlane.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.Demo;

public static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/demo")
            .WithTags("Demo Mode");

        group.MapGet("/status", (Microsoft.Extensions.Options.IOptions<DemoOptions> options) =>
        {
            return Results.Ok(new
            {
                enabled = options.Value.Enabled,
                isDemoMode = options.Value.Enabled,
                simulationTickSeconds = options.Value.SimulationTickSeconds,
                autoSeed = options.Value.AutoSeed
            });
        })
        .WithName("GetDemoStatus")
        .WithSummary("Retrieve current Demo Mode status and configuration")
        .AllowAnonymous();

        group.MapPost("/reset", async (
            DemoDataSeeder seeder,
            CancellationToken ct) =>
        {
            await seeder.SeedAsync(forceReset: true, ct);
            return Results.Ok(new
            {
                success = true,
                message = "Demo homelab environment reset to baseline state successfully.",
                resetAt = DateTimeOffset.UtcNow
            });
        })
        .WithName("ResetDemoData")
        .WithSummary("Reset all homelab inventory and adapter state back to the pristine demo baseline")
        .RequireAuthorization(AuthConstants.RequireOperator);

        return routes;
    }
}
