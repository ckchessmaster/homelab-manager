using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.Adoption;

public static class NodeAdoptionEndpoints
{
    public static IEndpointRouteBuilder MapNodeAdoptionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/hosts")
            .WithTags("Host Adoption");

        group.MapPost("/adopt", async (
            [FromBody] AdoptNodeRequest request,
            NodeAdoptionService adoptionService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.TargetHost))
            {
                return Results.BadRequest(new { message = "Host target (IP or hostname) is required." });
            }

            var result = await adoptionService.AdoptNodeAsync(request, null, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("AdoptNode")
        .WithSummary("Adopt an unmanaged Linux server into ControlPlane via SSH bootstrap");

        group.MapPost("/{id:guid}/adopt", async (
            Guid id,
            [FromBody] AdoptNodeRequest request,
            NodeAdoptionService adoptionService,
            CancellationToken cancellationToken) =>
        {
            var reqWithId = request with { HostId = id };
            var result = await adoptionService.AdoptNodeAsync(reqWithId, null, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("AdoptHostById")
        .WithSummary("Adopt an existing inventory host via SSH bootstrap");

        return routes;
    }
}
