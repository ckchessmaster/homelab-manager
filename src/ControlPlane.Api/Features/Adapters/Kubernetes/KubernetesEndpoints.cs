namespace ControlPlane.Api.Features.Adapters.Kubernetes;

public static class KubernetesEndpoints
{
    public static IEndpointRouteBuilder MapKubernetesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/adapters/k8s")
            .RequireAuthorization();

        group.MapPost("/cordon", async (
            K8sCordonRequest request,
            IKubernetesAdapter adapter,
            CancellationToken ct) =>
        {
            var success = await adapter.CordonNodeAsync(request.NodeName, ct);
            return success
                ? Results.Ok(new { nodeName = request.NodeName, unschedulable = true, message = $"Node '{request.NodeName}' cordoned successfully." })
                : Results.BadRequest(new { nodeName = request.NodeName, message = $"Failed to cordon node '{request.NodeName}'." });
        });

        group.MapPost("/uncordon", async (
            K8sUncordonRequest request,
            IKubernetesAdapter adapter,
            CancellationToken ct) =>
        {
            var success = await adapter.UncordonNodeAsync(request.NodeName, ct);
            return success
                ? Results.Ok(new { nodeName = request.NodeName, unschedulable = false, message = $"Node '{request.NodeName}' uncordoned successfully." })
                : Results.BadRequest(new { nodeName = request.NodeName, message = $"Failed to uncordon node '{request.NodeName}'." });
        });

        group.MapPost("/drain", async (
            K8sDrainRequest request,
            IKubernetesAdapter adapter,
            CancellationToken ct) =>
        {
            var result = await adapter.DrainNodeAsync(
                request.NodeName,
                TimeSpan.FromSeconds(request.TimeoutSeconds),
                request.IgnoreDaemonSets,
                request.DeleteEmptyDirData,
                ct);

            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        group.MapGet("/node-status", async (
            string nodeName,
            IKubernetesAdapter adapter,
            CancellationToken ct) =>
        {
            var status = await adapter.GetNodeStatusAsync(nodeName, ct);
            return status != null
                ? Results.Ok(status)
                : Results.NotFound(new { message = $"Node '{nodeName}' not found or unreachable." });
        });

        return app;
    }
}
