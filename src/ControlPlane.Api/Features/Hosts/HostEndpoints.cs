using System.Security.Claims;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Orchestration;
using ControlPlane.Api.Features.Orchestration.Pipelines;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.Hosts;

public record RebootHostRequest(string? PipelineId = null, bool Force = false);

public static class HostEndpoints
{
    public static RouteGroupBuilder MapHostEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/hosts")
            .WithTags("Hosts")
            .RequireAuthorization(AuthConstants.RequireViewer);

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
        .WithSummary("Register a new managed host in the inventory")
        .RequireAuthorization(AuthConstants.RequireAdmin);

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
        .WithSummary("Update attributes of an existing managed host")
        .RequireAuthorization(AuthConstants.RequireAdmin);

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
        .WithSummary("Remove a managed host from inventory")
        .RequireAuthorization(AuthConstants.RequireAdmin);

        group.MapGet("/{id:guid}/reboot-impact", async (
            Guid id,
            IHostCorrelationService correlationService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var impact = await correlationService.GetRebootImpactAsync(id, cancellationToken);
                return Results.Ok(impact);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"Host with ID '{id}' was not found." });
            }
        })
        .WithName("GetHostRebootImpact")
        .WithSummary("Evaluate hypervisor and Kubernetes cluster impact before rebooting a host");

        group.MapGet("/{id:guid}/correlation", async (
            Guid id,
            IHostCorrelationService correlationService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var correlation = await correlationService.GetHostCorrelationAsync(id, cancellationToken);
                return Results.Ok(correlation);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"Host with ID '{id}' was not found." });
            }
        })
        .WithName("GetHostCorrelation")
        .WithSummary("Retrieve hypervisor, VM, and Kubernetes correlation links for a host");

        group.MapPost("/sync-correlation", async (
            IHostCorrelationService correlationService,
            CancellationToken cancellationToken) =>
        {
            var result = await correlationService.SyncHostCorrelationsAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("SyncHostCorrelations")
        .WithSummary("Synchronize and persist Proxmox hypervisor and Kubernetes cluster correlations to the database")
        .RequireAuthorization(AuthConstants.RequireOperator);

        group.MapPost("/{id:guid}/reboot", async (
            Guid id,
            [FromBody] RebootHostRequest? request,
            ControlPlaneDbContext db,
            JobOrchestratorService jobOrchestrator,
            IPipelineCatalog pipelineCatalog,
            AgentConnectionManager connectionManager,
            IHostCorrelationService correlationService,
            ClaimsPrincipal user,
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

            // Check correlation & reboot impact
            var impact = await correlationService.GetRebootImpactAsync(host.Id, cancellationToken);
            if (impact.RequiresConfirmation && (request == null || !request.Force))
            {
                return Results.Conflict(new
                {
                    message = $"Reboot confirmation required: {string.Join(" ", impact.WarningMessages)}",
                    impact
                });
            }

            var isK8s = host.Kubernetes != null || string.Equals(host.TargetType, "k8s_node", StringComparison.OrdinalIgnoreCase);
            var pipelineId = !string.IsNullOrWhiteSpace(request?.PipelineId)
                ? request.PipelineId
                : pipelineCatalog.GetRecommendedRebootProfileId(host.TargetType, host.OsFamily, isK8s);

            var initiatedBy = user.Identity?.Name ?? "Operator";
            var (job, error) = await jobOrchestrator.CreateAndStartJobAsync(
                host.Id,
                pipelineId,
                initiatedBy,
                cancellationToken);

            if (job == null)
            {
                return Results.Conflict(new { message = error ?? $"Failed to start reboot DAG pipeline for host '{host.Hostname}'." });
            }

            return Results.Accepted($"/api/v1/jobs/{job.Id}", new
            {
                jobId = job.Id,
                hostId = host.Id,
                pipelineId = job.PipelineId,
                status = job.Status,
                message = $"Reboot initiated for {host.Hostname}"
            });
        })
        .WithName("RebootHost")
        .WithSummary("Dispatch an orchestrated reboot DAG pipeline to a connected host agent")
        .RequireAuthorization(AuthConstants.RequireOperator);

        return group;
    }
}
