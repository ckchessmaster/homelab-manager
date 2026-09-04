using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Features.Jobs;

public record ExecuteDebugCommandRequest(Guid HostId, string Command, string[]? Args);

public record StepLogDto(long Id, Guid JobId, long SequenceId, string StreamType, string LogLine, DateTimeOffset Timestamp);

public record JobDetailsDto(Guid Id, Guid TargetHostId, string Status, string? ActiveStep, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? FailureReason);

public static class StepLogQueryEndpoints
{
    public static IEndpointRouteBuilder MapJobLogEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/jobs")
            .WithTags("Jobs & Logs");

        group.MapGet("/{id:guid}/logs", async (
            Guid id,
            ControlPlaneDbContext db,
            [FromQuery] long? fromSequenceId = null,
            CancellationToken cancellationToken = default) =>
        {
            var seq = fromSequenceId ?? 0;
            var logs = await db.StepLogs
                .AsNoTracking()
                .Where(l => l.JobId == id && l.SequenceId >= seq)
                .OrderBy(l => l.SequenceId)
                .Select(l => new StepLogDto(
                    l.Id,
                    l.JobId,
                    l.SequenceId,
                    l.StreamType,
                    l.LogLine,
                    l.Timestamp
                ))
                .ToListAsync(cancellationToken);

            return Results.Ok(logs);
        })
        .WithName("GetJobLogs")
        .WithSummary("Retrieve sequence-ordered console logs for a job");

        routes.MapPost("/api/v1/debug/execute-command", async (
            [FromBody] ExecuteDebugCommandRequest request,
            ControlPlaneDbContext db,
            AgentConnectionManager connectionManager,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Command))
            {
                return Results.BadRequest(new { message = "Command cannot be empty." });
            }

            var host = await db.Hosts.FindAsync(new object[] { request.HostId }, cancellationToken);
            if (host == null)
            {
                return Results.NotFound(new { message = $"Host {request.HostId} not found." });
            }

            if (!connectionManager.IsOnline(host.Id))
            {
                return Results.BadRequest(new { message = $"Agent for host '{host.Hostname}' is currently offline." });
            }

            var jobId = Guid.NewGuid();
            var job = new UpdateJob
            {
                Id = jobId,
                TargetHostId = host.Id,
                InitiatedBy = "Operator",
                Status = "Running",
                ActiveStep = $"{request.Command} {string.Join(' ', request.Args ?? Array.Empty<string>())}",
                StartedAt = DateTimeOffset.UtcNow
            };

            db.UpdateJobs.Add(job);
            await db.SaveChangesAsync(cancellationToken);

            var cmdEnvelope = new AgentCommandEnvelope
            {
                Type = "EXECUTE_COMMAND",
                JobId = jobId,
                Command = request.Command,
                Args = request.Args ?? Array.Empty<string>()
            };

            var dispatched = await connectionManager.SendCommandAsync(host.Id, cmdEnvelope, cancellationToken);
            if (!dispatched)
            {
                job.Status = "Failed";
                job.FailureReason = "Failed to dispatch command to connected agent.";
                job.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }

            return Results.Accepted($"/api/v1/jobs/{jobId}", new
            {
                jobId,
                hostId = host.Id,
                command = request.Command,
                args = request.Args ?? Array.Empty<string>(),
                status = "Running"
            });
        })
        .WithName("ExecuteDebugCommand")
        .WithSummary("Dispatch an ad-hoc command to a connected host agent for live execution and streaming");

        return routes;
    }
}
