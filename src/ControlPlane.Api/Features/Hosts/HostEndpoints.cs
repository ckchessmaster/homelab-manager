using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;

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

        group.MapPost("/{id:guid}/reboot", async (
            Guid id,
            ControlPlaneDbContext db,
            AgentConnectionManager connectionManager,
            CancellationToken cancellationToken) =>
        {
            var host = await db.Hosts.FindAsync(new object[] { id }, cancellationToken);
            if (host == null)
            {
                return Results.NotFound(new { message = $"Host with ID '{id}' was not found." });
            }

            if (!connectionManager.IsOnline(host.Id))
            {
                return Results.BadRequest(new { message = $"Agent for host '{host.Hostname}' is currently offline. Cannot initiate reboot." });
            }

            var jobId = Guid.NewGuid();
            var job = new UpdateJob
            {
                Id = jobId,
                TargetHostId = host.Id,
                InitiatedBy = "Operator",
                Status = "Running",
                ActiveStep = "Rebooting node",
                StartedAt = DateTimeOffset.UtcNow
            };

            db.UpdateJobs.Add(job);
            await db.SaveChangesAsync(cancellationToken);

            var cmdEnvelope = new AgentCommandEnvelope
            {
                Type = "EXECUTE_COMMAND",
                JobId = jobId,
                Command = "systemctl",
                Args = new[] { "reboot" }
            };

            var dispatched = await connectionManager.SendCommandAsync(host.Id, cmdEnvelope, cancellationToken);
            if (!dispatched)
            {
                job.Status = "Failed";
                job.FailureReason = "Failed to dispatch reboot command to connected agent.";
                job.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }

            return Results.Accepted($"/api/v1/jobs/{jobId}", new
            {
                jobId,
                hostId = host.Id,
                status = "Running",
                message = $"Reboot initiated for {host.Hostname}"
            });
        })
        .WithName("RebootHost")
        .WithSummary("Dispatch a reboot command to a connected host agent");

        return group;
    }
}
