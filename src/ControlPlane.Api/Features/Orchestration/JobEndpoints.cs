using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.Orchestration;

public record CreateJobRequest(Guid TargetHostId);

public record JobSummaryDto(
    Guid Id,
    Guid TargetHostId,
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
        var group = routes.MapGroup("/api/v1/jobs")
            .WithTags("Jobs & Orchestration")
            .RequireAuthorization();

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
            var (job, error) = await orchestrator.CreateAndStartJobAsync(request.TargetHostId, initiatedBy, ct);

            if (job == null)
            {
                return Results.NotFound(new { message = error ?? "Target host not found." });
            }

            return Results.Accepted($"/api/v1/jobs/{job.Id}", new JobSummaryDto(
                job.Id,
                job.TargetHostId,
                job.Status,
                job.ActiveStep,
                job.InitiatedBy,
                job.StartedAt,
                job.CompletedAt,
                job.FailureReason
            ));
        })
        .WithName("CreateJob")
        .WithSummary("Trigger a new update job using the DAG orchestration pipeline");

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

        return routes;
    }
}
