namespace ControlPlane.Api.Features.Discovery;

public static class DiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/discovery")
            .RequireAuthorization();

        group.MapGet("/scan", async (
            bool includeProxmox = true,
            bool includeKubernetes = true,
            IDiscoveryService discoveryService = null!,
            CancellationToken ct = default) =>
        {
            var result = await discoveryService.ScanAsync(includeProxmox, includeKubernetes, ct);
            return Results.Ok(result);
        });

        group.MapPost("/import", async (
            ImportCandidateRequest request,
            IDiscoveryService discoveryService,
            CancellationToken ct) =>
        {
            var result = await discoveryService.ImportCandidateAsync(request, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        return app;
    }
}
