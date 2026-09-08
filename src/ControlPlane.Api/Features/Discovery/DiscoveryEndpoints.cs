using ControlPlane.Api.Security;

namespace ControlPlane.Api.Features.Discovery;

public static class DiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/discovery")
            .RequireAuthorization(AuthConstants.RequireViewer);

        group.MapGet("/scan", async (
            bool includeProxmox = true,
            bool includeKubernetes = true,
            bool includeUniFi = true,
            bool includeOPNsense = true,
            IDiscoveryService discoveryService = null!,
            CancellationToken ct = default) =>
        {
            var result = await discoveryService.ScanAsync(includeProxmox, includeKubernetes, includeUniFi, includeOPNsense, ct);
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
        })
        .RequireAuthorization(AuthConstants.RequireAdmin);

        group.MapPost("/import-batch", async (
            BatchImportCandidatesRequest request,
            IDiscoveryService discoveryService,
            CancellationToken ct) =>
        {
            var result = await discoveryService.ImportCandidatesBatchAsync(request, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(AuthConstants.RequireAdmin);

        return app;
    }
}
