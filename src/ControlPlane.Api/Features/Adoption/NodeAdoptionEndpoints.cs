using ControlPlane.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.Adoption;

public static class NodeAdoptionEndpoints
{
    public static IEndpointRouteBuilder MapNodeAdoptionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/hosts")
            .WithTags("Host Adoption")
            .RequireAuthorization(AuthConstants.RequireAdmin);

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

        group.MapPost("/adopt-batch", async (
            [FromBody] BatchAdoptNodesRequest request,
            NodeAdoptionService adoptionService,
            CancellationToken cancellationToken) =>
        {
            var results = new List<BatchAdoptItemResult>();
            var succeeded = 0;
            var failed = 0;

            foreach (var item in request.Hosts)
            {
                var singleReq = new AdoptNodeRequest(
                    HostId: item.HostId,
                    Hostname: item.Hostname,
                    TargetHost: item.TargetHost,
                    Port: request.Port,
                    Username: request.Username,
                    Password: request.Password,
                    PrivateKey: request.PrivateKey,
                    HubUrl: request.HubUrl
                );

                try
                {
                    var res = await adoptionService.AdoptNodeAsync(singleReq, null, cancellationToken);
                    if (res.Success)
                    {
                        succeeded++;
                        results.Add(new BatchAdoptItemResult(item.HostId, item.Hostname ?? item.TargetHost, true, res.Message));
                    }
                    else
                    {
                        failed++;
                        results.Add(new BatchAdoptItemResult(item.HostId, item.Hostname ?? item.TargetHost, false, res.Message));
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    results.Add(new BatchAdoptItemResult(item.HostId, item.Hostname ?? item.TargetHost, false, ex.Message));
                }
            }

            return Results.Ok(new BatchAdoptNodesResponse(request.Hosts.Count, succeeded, failed, results));
        })
        .WithName("AdoptNodesBatch")
        .WithSummary("Adopt multiple existing inventory hosts in batch via SSH bootstrap");

        return routes;
    }
}
