namespace ControlPlane.Api.Features.Adapters.Proxmox;

public static class ProxmoxProbeEndpoints
{
    public static RouteGroupBuilder MapProxmoxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/adapters/proxmox")
            .WithTags("Proxmox Adapter")
            .RequireAuthorization();

        group.MapPost("/test-connection", async (
            ProxmoxProbeRequest request,
            ProxmoxProbeService probeService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.BaseUrl) ||
                string.IsNullOrWhiteSpace(request.ApiTokenId) ||
                string.IsNullOrWhiteSpace(request.ApiTokenSecret))
            {
                return Results.BadRequest(new
                {
                    message = "BaseUrl, ApiTokenId, and ApiTokenSecret are required."
                });
            }

            var result = await probeService.ProbeAsync(request, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("TestProxmoxConnection")
        .WithSummary("Probe and verify connectivity to a Proxmox VE API endpoint");

        return group;
    }
}
