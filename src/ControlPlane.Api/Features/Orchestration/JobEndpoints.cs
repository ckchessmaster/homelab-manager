using System.Security.Claims;
using ControlPlane.Api.Features.Orchestration.Pipelines;
using ControlPlane.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.Orchestration;

public record PipelineStepInfoDto(string Name, string Description);

public record PipelineProfileDto(
    string Id,
    string Name,
    string Description,
    string Icon,
    IReadOnlyList<string> CompatibleTargetTypes,
    IReadOnlyList<PipelineStepInfoDto> Steps
);

public record CreateJobRequest(Guid TargetHostId, string? PipelineId = null);

public record CancelJobRequest(string? Reason = null);

public record JobSummaryDto(
    Guid Id,
    Guid TargetHostId,
    string PipelineId,
    string Status,
    string? ActiveStep,
    string InitiatedBy,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason
);

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder routes)
    {
        // Pipeline catalog endpoints
        routes.MapGet("/api/v1/pipelines", (IPipelineCatalog catalog) =>
        {
            var profiles = catalog.GetProfiles().Select(p => new PipelineProfileDto(
                p.Id,
                p.Name,
                p.Description,
                p.Icon,
                p.CompatibleTargetTypes,
                p.Steps.Select(s => new PipelineStepInfoDto(s.Name, s.Description)).ToList()
            ));
            return Results.Ok(profiles);
        })
        .WithTags("Jobs & Orchestration")
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("ListPipelines")
        .WithSummary("List all available modular upgrade and maintenance pipeline profiles");

        var group = routes.MapGroup("/api/v1/jobs")
            .WithTags("Jobs & Orchestration")
            .RequireAuthorization(AuthConstants.RequireViewer);

        group.MapPost("/", async (
            [FromBody] CreateJobRequest request,
            JobOrchestratorService orchestrator,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (request.TargetHostId == Guid.Empty)
            {
                return Results.BadRequest(new { message = "TargetHostId is required." });
            }

            var initiatedBy = user.Identity?.Name ?? "Operator";
            var (job, error) = await orchestrator.CreateAndStartJobAsync(request.TargetHostId, request.PipelineId, initiatedBy, ct);

            if (job == null)
            {
                return Results.NotFound(new { message = error ?? "Target host not found." });
            }

            return Results.Accepted($"/api/v1/jobs/{job.Id}", new JobSummaryDto(
                job.Id,
                job.TargetHostId,
                job.PipelineId,
                job.Status,
                job.ActiveStep,
                job.InitiatedBy,
                job.StartedAt,
                job.CompletedAt,
                job.FailureReason
            ));
        })
        .WithName("CreateJob")
        .WithSummary("Trigger a new update job using the DAG orchestration pipeline")
        .RequireAuthorization(AuthConstants.RequireOperator);

        group.MapGet("/", async (
            [FromQuery] Guid? hostId,
            [FromQuery] int? limit,
            JobOrchestratorService orchestrator,
            CancellationToken ct) =>
        {
            var actualLimit = limit.GetValueOrDefault(50);
            if (actualLimit <= 0) actualLimit = 50;
            var jobs = await orchestrator.ListJobsAsync(hostId, actualLimit, ct);
            var dtos = jobs.Select(j => new JobSummaryDto(
                j.Id,
                j.TargetHostId,
                j.PipelineId,
                j.Status,
                j.ActiveStep,
                j.InitiatedBy,
                j.StartedAt,
                j.CompletedAt,
                j.FailureReason
            ));

            return Results.Ok(dtos);
        })
        .WithName("ListJobs")
        .WithSummary("List update jobs with optional host filtering");

        group.MapGet("/{id:guid}", async (
            Guid id,
            JobOrchestratorService orchestrator,
            CancellationToken ct) =>
        {
            var job = await orchestrator.GetJobByIdAsync(id, ct);
            if (job == null)
            {
                return Results.NotFound(new { message = $"Job {id} not found." });
            }

            return Results.Ok(new JobSummaryDto(
                job.Id,
                job.TargetHostId,
                job.PipelineId,
                job.Status,
                job.ActiveStep,
                job.InitiatedBy,
                job.StartedAt,
                job.CompletedAt,
                job.FailureReason
            ));
        })
        .WithName("GetJobById")
        .WithSummary("Retrieve details and status for a specific update job");

        group.MapPost("/{id:guid}/cancel", async (
            Guid id,
            [FromBody] CancelJobRequest? request,
            JobOrchestratorService orchestrator,
            CancellationToken ct) =>
        {
            var success = await orchestrator.CancelJobAsync(id, request?.Reason, ct);
            if (!success)
            {
                return Results.BadRequest(new { message = $"Job '{id}' cannot be cancelled (not found or already completed/failed)." });
            }

            return Results.Ok(new { success = true, jobId = id, message = "Job cancelled." });
        })
        .WithName("CancelJob")
        .WithSummary("Cancel an active update job")
        .RequireAuthorization(AuthConstants.RequireOperator);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            JobOrchestratorService orchestrator,
            CancellationToken ct) =>
        {
            var success = await orchestrator.DeleteJobAsync(id, ct);
            if (!success)
            {
                return Results.BadRequest(new { message = $"Job '{id}' cannot be deleted (running, or not found)." });
            }

            return Results.Ok(new { success = true, jobId = id, message = "Job deleted." });
        })
        .WithName("DeleteJob")
        .WithSummary("Delete a finished update job and its console logs")
        .RequireAuthorization(AuthConstants.RequireOperator);

        group.MapPost("/purge", async (
            JobOrchestratorService orchestrator,
            CancellationToken ct) =>
        {
            var purgedCount = await orchestrator.PurgeFinishedJobsAsync(ct);
            return Results.Ok(new { success = true, purgedCount, message = $"Successfully purged {purgedCount} finished workflow execution(s)." });
        })
        .WithName("PurgeFinishedJobs")
        .WithSummary("Purge all completed, failed, and rolled-back update jobs and their logs")
        .RequireAuthorization(AuthConstants.RequireOperator);

        return routes;
    }
}
