namespace ControlPlane.Api.Features.Hosts;

public static class HostEndpoints
{
    public static RouteGroupBuilder MapHostEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/hosts")
            .WithTags("Hosts")
            .RequireAuthorization();

        group.MapGet("/", async (
            [AsParameters] HostFilterQuery query,
            HostService hostService,
            CancellationToken cancellationToken) =>
        {
            var hosts = await hostService.ListHostsAsync(query, cancellationToken);
            return Results.Ok(hosts);
        })
        .WithName("ListHosts")
        .WithSummary("List all managed hosts with optional filtering");

        group.MapGet("/{id:guid}", async (
            Guid id,
            HostService hostService,
            CancellationToken cancellationToken) =>
        {
            var host = await hostService.GetHostByIdAsync(id, cancellationToken);
            return host == null ? Results.NotFound(new { message = $"Host with ID '{id}' was not found." }) : Results.Ok(host);
        })
        .WithName("GetHostById")
        .WithSummary("Retrieve details of a single managed host");

        group.MapPost("/", async (
            CreateHostRequest request,
            HostService hostService,
            CancellationToken cancellationToken) =>
        {
            var (host, errors, conflict) = await hostService.CreateHostAsync(request, cancellationToken);

            if (conflict)
            {
                return Results.Conflict(new { message = "Host conflict detected.", errors });
            }

            if (errors != null)
            {
                return Results.ValidationProblem(errors);
            }

            return Results.Created($"/api/v1/hosts/{host!.Id}", host);
        })
        .WithName("CreateHost")
        .WithSummary("Register a new managed host in the inventory");

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateHostRequest request,
            HostService hostService,
            CancellationToken cancellationToken) =>
        {
            var (host, errors, conflict, notFound) = await hostService.UpdateHostAsync(id, request, cancellationToken);

            if (notFound)
            {
                return Results.NotFound(new { message = $"Host with ID '{id}' was not found." });
            }

            if (conflict)
            {
                return Results.Conflict(new { message = "Host conflict detected.", errors });
            }

            if (errors != null)
            {
                return Results.ValidationProblem(errors);
            }

            return Results.Ok(host);
        })
        .WithName("UpdateHost")
        .WithSummary("Update attributes of an existing managed host");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HostService hostService,
            CancellationToken cancellationToken) =>
        {
            var (success, notFound, errorMessage) = await hostService.DeleteHostAsync(id, cancellationToken);

            if (notFound)
            {
                return Results.NotFound(new { message = $"Host with ID '{id}' was not found." });
            }

            if (!success)
            {
                return Results.BadRequest(new { message = errorMessage });
            }

            return Results.NoContent();
        })
        .WithName("DeleteHost")
        .WithSummary("Remove a managed host from inventory");

        return group;
    }
}
