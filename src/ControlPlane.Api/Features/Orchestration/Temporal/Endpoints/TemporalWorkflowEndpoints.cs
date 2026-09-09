using ControlPlane.Api.Features.Orchestration.Temporal.Workflows;
using ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;
using ControlPlane.Api.Hubs;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
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

public record StoredRollingBatch(
    string BatchId,
    string WorkflowId,
    List<string> HostIds,
    List<string> Hostnames,
    int MaxParallelism,
    string FailureStrategy,
    string InitiatedBy,
    DateTimeOffset StartedAt
);

public record RollingBatchSummaryDto(
    string BatchId,
    string WorkflowId,
    string Status,
    int TotalHosts,
    int CompletedHosts,
    int FailedHosts,
    string? ActiveHostname,
    bool IsPaused,
    List<string> HostIds,
    List<string> Hostnames,
    string InitiatedBy,
    DateTimeOffset StartedAt
);

public static class TemporalWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapTemporalWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orchestration/temporal/workflows")
            .WithTags("Temporal Orchestration")
            .RequireAuthorization(AuthConstants.RequireViewer);

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

            var activeStatuses = new[] { "Pending", "Running", "Verifying", "AwaitingReconnect", "AwaitingApproval" };
            var hasActiveJob = await db.UpdateJobs.AnyAsync(j => j.TargetHostId == host.Id && activeStatuses.Contains(j.Status));
            if (hasActiveJob)
            {
                return Results.Conflict(new { error = $"Host '{host.Hostname}' already has an active update workflow in progress." });
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
        })
        .RequireAuthorization(AuthConstants.RequireOperator);

        group.MapPost("/{workflowId}/signals/{signalName}", async (
            string workflowId,
            string signalName,
            [FromBody] SignalRequest? body,
            ControlPlaneDbContext db,
            IHubContext<JobLogHub, IJobClient> hubContext,
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
                try
                {
                    await handle.CancelAsync();
                }
                catch
                {
                    // Fall back to signal if direct workflow cancellation encounters an error
                }

                try
                {
                    await handle.SignalAsync(w => w.CancelAsync(cancelReason));
                }
                catch
                {
                    // Workflow may have already terminated
                }

                // Synchronize DB job state immediately to prevent UI desync
                var prefix = "host-upgrade-";
                if (workflowId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    Guid.TryParse(workflowId[prefix.Length..], out var jId))
                {
                    var job = await db.UpdateJobs.FirstOrDefaultAsync(j => j.Id == jId);
                    if (job != null && job.Status != UpdateJobState.Failed && job.Status != UpdateJobState.Completed && job.Status != UpdateJobState.Cancelled)
                    {
                        job.Status = UpdateJobState.Cancelled;
                        job.FailureReason = cancelReason ?? "Workflow cancelled by operator";
                        job.CompletedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync();
                        _ = hubContext.Clients.Group(jId.ToString()).JobStatusChanged(jId, UpdateJobState.Cancelled, job.FailureReason);
                    }
                }

                return Results.Ok(new { success = true, signal = "cancel", workflowId, reason = cancelReason });
            }

            return Results.BadRequest(new
            {
                error = $"Unsupported signal '{signalName}'. Supported signals: 'approve-reboot', 'cancel'."
            });
        })
        .RequireAuthorization(AuthConstants.RequireOperator);

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
            .WithTags("Temporal Batch Orchestration")
            .RequireAuthorization(AuthConstants.RequireViewer);

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

            // Persist batch metadata to system settings for fleet discovery & tracking
            try
            {
                var storedBatch = new StoredRollingBatch(
                    batchId.ToString(),
                    workflowId,
                    targets.Select(t => t.HostId.ToString()).ToList(),
                    targets.Select(t => t.Hostname).ToList(),
                    request.MaxParallelism,
                    request.FailureStrategy ?? "StopOnFirstFailure",
                    request.InitiatedBy ?? "Operator",
                    DateTimeOffset.UtcNow
                );

                db.SystemSettings.Add(new SystemSetting
                {
                    Key = $"rolling_batch:{batchId}",
                    ValueJson = System.Text.Json.JsonSerializer.Serialize(storedBatch),
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }
            catch
            {
                // Non-fatal if setting persistence fails
            }

            return Results.Ok(new StartRollingUpgradeResponse(
                batchId,
                workflowId,
                targets.Count,
                targets.Select(t => t.HostId).ToList()
            ));
        })
        .RequireAuthorization(AuthConstants.RequireOperator);

        batchGroup.MapGet("/", async (
            ControlPlaneDbContext db,
            [FromServices] ITemporalClient? temporalClient = null) =>
        {
            var rawBatches = await db.SystemSettings.AsNoTracking()
                .Where(s => s.Key.StartsWith("rolling_batch:"))
                .OrderByDescending(s => s.UpdatedAt)
                .Take(25)
                .ToListAsync();

            var list = new List<RollingBatchSummaryDto>();

            foreach (var raw in rawBatches)
            {
                try
                {
                    var meta = System.Text.Json.JsonSerializer.Deserialize<StoredRollingBatch>(raw.ValueJson);
                    if (meta == null) continue;

                    string status = "Pending";
                    int completedHosts = 0;
                    int failedHosts = 0;
                    string? activeHostname = null;
                    bool isPaused = false;

                    if (temporalClient != null)
                    {
                        try
                        {
                            var handle = temporalClient.GetWorkflowHandle<IRollingUpgradeWorkflow>(meta.WorkflowId);
                            var describe = await handle.DescribeAsync();
                            status = describe.Status.ToString();

                            try
                            {
                                var state = await handle.QueryAsync(w => w.GetWorkflowState());
                                if (state != null)
                                {
                                    status = state.Status;
                                    completedHosts = state.CompletedHosts;
                                    failedHosts = state.FailedHosts;
                                    activeHostname = state.ActiveHostname;
                                    isPaused = state.IsPaused;
                                }
                            }
                            catch
                            {
                            }
                        }
                        catch
                        {
                        }
                    }

                    list.Add(new RollingBatchSummaryDto(
                        meta.BatchId,
                        meta.WorkflowId,
                        status,
                        meta.Hostnames.Count,
                        completedHosts,
                        failedHosts,
                        activeHostname,
                        isPaused,
                        meta.HostIds,
                        meta.Hostnames,
                        meta.InitiatedBy,
                        meta.StartedAt
                    ));
                }
                catch
                {
                }
            }

            return Results.Ok(list);
        });

        batchGroup.MapGet("/active", async (
            ControlPlaneDbContext db,
            [FromServices] ITemporalClient? temporalClient = null) =>
        {
            if (temporalClient == null)
            {
                return Results.Ok((RollingBatchSummaryDto?)null);
            }

            var rawBatches = await db.SystemSettings.AsNoTracking()
                .Where(s => s.Key.StartsWith("rolling_batch:"))
                .OrderByDescending(s => s.UpdatedAt)
                .Take(10)
                .ToListAsync();

            foreach (var raw in rawBatches)
            {
                try
                {
                    var meta = System.Text.Json.JsonSerializer.Deserialize<StoredRollingBatch>(raw.ValueJson);
                    if (meta == null) continue;

                    var handle = temporalClient.GetWorkflowHandle<IRollingUpgradeWorkflow>(meta.WorkflowId);
                    var describe = await handle.DescribeAsync();
                    var descStatus = describe.Status.ToString();

                    if (descStatus.Equals("Running", StringComparison.OrdinalIgnoreCase))
                    {
                        int completedHosts = 0;
                        int failedHosts = 0;
                        string? activeHostname = null;
                        bool isPaused = false;
                        string status = "Running";

                        try
                        {
                            var state = await handle.QueryAsync(w => w.GetWorkflowState());
                            if (state != null)
                            {
                                status = state.Status;
                                completedHosts = state.CompletedHosts;
                                failedHosts = state.FailedHosts;
                                activeHostname = state.ActiveHostname;
                                isPaused = state.IsPaused;
                            }
                        }
                        catch
                        {
                        }

                        return Results.Ok(new RollingBatchSummaryDto(
                            meta.BatchId,
                            meta.WorkflowId,
                            status,
                            meta.Hostnames.Count,
                            completedHosts,
                            failedHosts,
                            activeHostname,
                            isPaused,
                            meta.HostIds,
                            meta.Hostnames,
                            meta.InitiatedBy,
                            meta.StartedAt
                        ));
                    }
                }
                catch
                {
                }
            }

            return Results.Ok((RollingBatchSummaryDto?)null);
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
                try
                {
                    await handle.CancelAsync();
                }
                catch
                {
                    // Fall back to signal if direct workflow cancellation encounters an error
                }

                try
                {
                    await handle.SignalAsync(w => w.CancelAsync(cancelReason));
                }
                catch
                {
                    // Workflow may have already terminated
                }

                return Results.Ok(new { success = true, signal = "cancel", workflowId, reason = cancelReason });
            }

            return Results.BadRequest(new
            {
                error = $"Unsupported batch signal '{signalName}'. Supported: 'pause', 'resume', 'cancel'."
            });
        })
        .RequireAuthorization(AuthConstants.RequireOperator);

        return app;
    }
}
