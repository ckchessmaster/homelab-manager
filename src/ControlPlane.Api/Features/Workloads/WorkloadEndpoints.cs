using Microsoft.AspNetCore.Mvc;
using ControlPlane.Api.Security;

namespace ControlPlane.Api.Features.Workloads;

public static class WorkloadEndpoints
{
    public static RouteGroupBuilder MapWorkloadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workloads")
            .WithTags("Workloads");

        group.MapGet("/", async (
            [FromQuery] string? clusterId,
            [FromQuery] string? namespaceName,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var result = await workloadService.GetAggregatedWorkloadsAsync(clusterId, namespaceName, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetAggregatedWorkloads");

        group.MapGet("/{clusterId}/{namespaceName}/{name}/pods", async (
            string clusterId,
            string namespaceName,
            string name,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var pods = await workloadService.GetWorkloadPodsAsync(clusterId, namespaceName, name, ct);
            return Results.Ok(pods);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetWorkloadPods");

        group.MapPost("/{clusterId}/{namespaceName}/{name}/restart", async (
            string clusterId,
            string namespaceName,
            string name,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var success = await workloadService.RestartWorkloadAsync(clusterId, namespaceName, name, ct);
            return success
                ? Results.Ok(new { success = true, message = $"Deployment '{name}' rollout restart triggered successfully." })
                : Results.BadRequest(new { success = false, message = $"Failed to restart deployment '{name}'." });
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("RestartWorkload");

        group.MapPost("/{clusterId}/{namespaceName}/{name}/scale", async (
            string clusterId,
            string namespaceName,
            string name,
            [FromBody] ScaleWorkloadRequest req,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var success = await workloadService.ScaleWorkloadAsync(clusterId, namespaceName, name, req.Replicas, ct);
            return success
                ? Results.Ok(new { success = true, replicas = req.Replicas, message = $"Deployment '{name}' scaled to {req.Replicas} replicas." })
                : Results.BadRequest(new { success = false, message = $"Failed to scale deployment '{name}'." });
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("ScaleWorkload");

        return group;
    }
}
