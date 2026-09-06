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

        return app;
    }
}
