using ControlPlane.Api.Features.Orchestration.Temporal.Workflows;
using ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Temporalio.Client;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Endpoints;

public record StartWorkflowRequest(
    Guid HostId,
    bool RequireApprovalBeforeReboot = false,
    bool AlwaysReboot = false,
    List<string>? ProbeUrls = null,
    string? SnapshotName = null,
    string? K8sNodeName = null,
    string InitiatedBy = "Operator"
);

public record StartWorkflowResponse(string WorkflowId, string? RunId, Guid JobId, Guid HostId);

public record SignalRequest(string? Reason = null);

public record WorkflowStatusResponse(string WorkflowId, string ExecutionStatus, HostUpgradeWorkflowState? State);

public record StartRollingUpgradeRequest(
    List<Guid> HostIds,
    int MaxParallelism = 1,
    string FailureStrategy = "StopOnFirstFailure",
    bool RequireApprovalBeforeReboot = false,
    bool RequireApprovalBetweenHosts = false,
    bool AlwaysReboot = false,
    List<string>? ProbeUrls = null,
    string? SnapshotPrefix = null,
    string InitiatedBy = "Operator"
);

public record StartRollingUpgradeResponse(
    Guid BatchId,
    string WorkflowId,
    int TotalHosts,
    List<Guid> TargetHostIds
);

public record RollingUpgradeStatusResponse(
    string WorkflowId,
    string ExecutionStatus,
    RollingUpgradeWorkflowState? State
);

public static class TemporalWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapTemporalWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orchestration/temporal/workflows")
            .WithTags("Temporal Orchestration");

        group.MapPost("/start", async (
            [FromBody] StartWorkflowRequest request,
            ControlPlaneDbContext db,
            IOptions<TemporalOptions> options,
            [FromServices] ITemporalClient? temporalClient = null) =>
        {
            if (temporalClient == null)
            {
                return Results.Problem(
                    "Temporal client is not available in the current runtime mode.",
                    statusCode: StatusCodes.Status503ServiceUnavailable
                );
            }

            var host = await db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == request.HostId);
            if (host == null)
            {
                return Results.NotFound(new { error = $"Target host '{request.HostId}' not found." });
            }

            var job = new UpdateJob
            {
                Id = Guid.NewGuid(),
                TargetHostId = host.Id,
                PipelineId = "temporal-host-upgrade",
                InitiatedBy = request.InitiatedBy,
                Status = "Pending",
                StartedAt = DateTimeOffset.UtcNow
            };
            db.UpdateJobs.Add(job);
            await db.SaveChangesAsync();

            var workflowId = $"host-upgrade-{job.Id}";
            var workflowInput = new HostUpgradeWorkflowInput(
                JobId: job.Id,
                HostId: host.Id,
                Hostname: host.Hostname,
                TargetType: host.TargetType,
                OsFamily: host.OsFamily,
                RequireApprovalBeforeReboot: request.RequireApprovalBeforeReboot,
                AlwaysReboot: request.AlwaysReboot,
                ProbeUrls: request.ProbeUrls,
                SnapshotName: request.SnapshotName,
                K8sNodeName: request.K8sNodeName
            );

            var taskQueue = options.Value.TaskQueue;
            var handle = await temporalClient.StartWorkflowAsync(
                (IHostUpgradeWorkflow w) => w.RunAsync(workflowInput),
                new WorkflowOptions(workflowId, taskQueue)
            );

            return Results.Ok(new StartWorkflowResponse(handle.Id, handle.ResultRunId, job.Id, host.Id));
        });

        group.MapPost("/{workflowId}/signals/{signalName}", async (
            string workflowId,
            string signalName,
            [FromBody] SignalRequest? body,
            [FromServices] ITemporalClient? temporalClient = null) =>
        {
            if (temporalClient == null)
            {
                return Results.Problem(
                    "Temporal client is not available in the current runtime mode.",
                    statusCode: StatusCodes.Status503ServiceUnavailable
                );
            }

            var handle = temporalClient.GetWorkflowHandle<IHostUpgradeWorkflow>(workflowId);

            if (signalName.Equals("approve-reboot", StringComparison.OrdinalIgnoreCase) ||
                signalName.Equals("approve", StringComparison.OrdinalIgnoreCase))
            {
                await handle.SignalAsync(w => w.ApproveRebootAsync());
                return Results.Ok(new { success = true, signal = "approve-reboot", workflowId });
            }

            if (signalName.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                var cancelReason = body?.Reason;
                await handle.SignalAsync(w => w.CancelAsync(cancelReason));
                return Results.Ok(new { success = true, signal = "cancel", workflowId, reason = cancelReason });
            }

            return Results.BadRequest(new
            {
                error = $"Unsupported signal '{signalName}'. Supported signals: 'approve-reboot', 'cancel'."
            });
        });

        group.MapGet("/{workflowId}/status", async (
            string workflowId,
            [FromServices] ITemporalClient? temporalClient = null) =>
        {
            if (temporalClient == null)
            {
                return Results.Problem(
                    "Temporal client is not available in the current runtime mode.",
                    statusCode: StatusCodes.Status503ServiceUnavailable
                );
            }

            var handle = temporalClient.GetWorkflowHandle<IHostUpgradeWorkflow>(workflowId);
            var describe = await handle.DescribeAsync();
            HostUpgradeWorkflowState? state = null;

            try
            {
                state = await handle.QueryAsync(w => w.GetWorkflowState());
            }
            catch
            {
                // Query may not be available if workflow has not yet dispatched first task
            }

            return Results.Ok(new WorkflowStatusResponse(
                workflowId,
                describe.Status.ToString(),
                state
            ));
        });

        // Batch Rolling Upgrade Endpoints
        var batchGroup = app.MapGroup("/api/v1/orchestration/temporal/batch")
            .WithTags("Temporal Batch Orchestration");

        batchGroup.MapPost("/rolling-upgrade", async (
            [FromBody] StartRollingUpgradeRequest request,
            ControlPlaneDbContext db,
            IOptions<TemporalOptions> options,
            [FromServices] ITemporalClient? temporalClient = null) =>
        {
            if (temporalClient == null)
            {
                return Results.Problem(
                    "Temporal client is not available in the current runtime mode.",
                    statusCode: StatusCodes.Status503ServiceUnavailable
                );
            }

            if (request.HostIds == null || request.HostIds.Count == 0)
            {
                return Results.BadRequest(new { error = "At least one target host must be specified." });
            }

            var hosts = await db.Hosts.AsNoTracking()
                .Where(h => request.HostIds.Contains(h.Id))
                .ToListAsync();

            if (hosts.Count == 0)
            {
                return Results.BadRequest(new { error = "None of the specified target hosts were found." });
            }

            var targets = hosts.Select(h => new RollingHostTarget(
                HostId: h.Id,
                Hostname: h.Hostname,
                TargetType: h.TargetType,
                OsFamily: h.OsFamily,
                K8sNodeName: h.Hostname
            )).ToList();

            var batchId = Guid.NewGuid();
            var workflowId = $"rolling-upgrade-{batchId}";

            var workflowInput = new RollingUpgradeWorkflowInput(
                BatchId: batchId,
                TargetHosts: targets,
                MaxParallelism: request.MaxParallelism,
                FailureStrategy: request.FailureStrategy,
                RequireApprovalBeforeReboot: request.RequireApprovalBeforeReboot,
                RequireApprovalBetweenHosts: request.RequireApprovalBetweenHosts,
                AlwaysReboot: request.AlwaysReboot,
                ProbeUrls: request.ProbeUrls,
                SnapshotPrefix: request.SnapshotPrefix,
                InitiatedBy: request.InitiatedBy
            );

            var taskQueue = options.Value.TaskQueue;
            await temporalClient.StartWorkflowAsync(
                (IRollingUpgradeWorkflow w) => w.RunAsync(workflowInput),
                new WorkflowOptions(workflowId, taskQueue)
            );

            return Results.Ok(new StartRollingUpgradeResponse(
                batchId,
                workflowId,
                targets.Count,
                targets.Select(t => t.HostId).ToList()
            ));
        });

        batchGroup.MapGet("/{batchId}/status", async (
            string batchId,
            [FromServices] ITemporalClient? temporalClient = null) =>
        {
            if (temporalClient == null)
            {
                return Results.Problem(
                    "Temporal client is not available in the current runtime mode.",
                    statusCode: StatusCodes.Status503ServiceUnavailable
                );
            }

            var workflowId = batchId.StartsWith("rolling-upgrade-", StringComparison.OrdinalIgnoreCase)
                ? batchId
                : $"rolling-upgrade-{batchId}";

            var handle = temporalClient.GetWorkflowHandle<IRollingUpgradeWorkflow>(workflowId);
            var describe = await handle.DescribeAsync();
            RollingUpgradeWorkflowState? state = null;

            try
            {
                state = await handle.QueryAsync(w => w.GetWorkflowState());
            }
            catch
            {
                // Query may not be available if workflow has not yet dispatched first task
            }

            return Results.Ok(new RollingUpgradeStatusResponse(
                workflowId,
                describe.Status.ToString(),
                state
            ));
        });

        batchGroup.MapPost("/{batchId}/signals/{signalName}", async (
            string batchId,
            string signalName,
            [FromBody] SignalRequest? body,
            [FromServices] ITemporalClient? temporalClient = null) =>
        {
            if (temporalClient == null)
            {
                return Results.Problem(
                    "Temporal client is not available in the current runtime mode.",
                    statusCode: StatusCodes.Status503ServiceUnavailable
                );
            }

            var workflowId = batchId.StartsWith("rolling-upgrade-", StringComparison.OrdinalIgnoreCase)
                ? batchId
                : $"rolling-upgrade-{batchId}";

            var handle = temporalClient.GetWorkflowHandle<IRollingUpgradeWorkflow>(workflowId);

            if (signalName.Equals("pause", StringComparison.OrdinalIgnoreCase))
            {
                await handle.SignalAsync(w => w.PauseAsync());
                return Results.Ok(new { success = true, signal = "pause", workflowId });
            }

            if (signalName.Equals("resume", StringComparison.OrdinalIgnoreCase))
            {
                await handle.SignalAsync(w => w.ResumeAsync());
                return Results.Ok(new { success = true, signal = "resume", workflowId });
            }

            if (signalName.Equals("cancel", StringComparison.OrdinalIgnoreCase) ||
                signalName.Equals("abort", StringComparison.OrdinalIgnoreCase))
            {
                var cancelReason = body?.Reason;
                await handle.SignalAsync(w => w.CancelAsync(cancelReason));
                return Results.Ok(new { success = true, signal = "cancel", workflowId, reason = cancelReason });
            }

            return Results.BadRequest(new
            {
                error = $"Unsupported batch signal '{signalName}'. Supported: 'pause', 'resume', 'cancel'."
            });
        });

        return app;
    }
}
