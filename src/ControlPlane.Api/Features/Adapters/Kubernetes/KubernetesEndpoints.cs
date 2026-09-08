using ControlPlane.Api.Features.Adapters.Config;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.Adapters.Kubernetes;

public static class KubernetesEndpoints
{
    public static IEndpointRouteBuilder MapKubernetesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/adapters/k8s")
            .RequireAuthorization();

        // --- Cluster Management ---

        group.MapGet("/clusters", async (
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var clusters = await configService.GetKubernetesClustersAsync(ct);
            return Results.Ok(clusters);
        });

        group.MapGet("/clusters/{id}", async (
            string id,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var cluster = await configService.GetKubernetesClusterAsync(id, ct);
            return cluster != null ? Results.Ok(cluster) : Results.NotFound(new { message = $"Cluster '{id}' not found." });
        });

        group.MapPost("/clusters", async (
            SaveKubernetesClusterRequest request,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "Cluster name is required." });
            }

            var saved = await configService.SaveKubernetesClusterAsync(request, ct);
            return Results.Ok(saved);
        });

        group.MapDelete("/clusters/{id}", async (
            string id,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var success = await configService.DeleteKubernetesClusterAsync(id, ct);
            return success
                ? Results.Ok(new { message = $"Cluster '{id}' removed successfully." })
                : Results.NotFound(new { message = $"Cluster '{id}' not found." });
        });

        group.MapPost("/clusters/{id}/test-connection", async (
            string id,
            IKubernetesClientFactory clientFactory,
            CancellationToken ct) =>
        {
            var adapter = await clientFactory.CreateAdapterAsync(id, ct);
            var result = await adapter.TestConnectionAsync(ct);
            return Results.Ok(result);
        });

        group.MapPost("/test-connection", async (
            SaveKubernetesClusterRequest request,
            IAdapterConfigService configService,
            IKubernetesClientFactory clientFactory,
            CancellationToken ct) =>
        {
            // Transient test connection without persisting
            var tempCluster = new KubernetesStoredCluster
            {
                Id = "temp-test",
                Name = request.Name,
                ApiServerUrl = request.ApiServerUrl,
                EncryptedKubeConfig = request.KubeConfigRaw,
                EncryptedToken = request.Token,
                ContextName = request.ContextName,
                SkipTlsVerify = request.SkipTlsVerify
            };

            // Save temporarily with a test ID or build adapter
            var testReq = request with { Id = "temp-test-preflight" };
            var saved = await configService.SaveKubernetesClusterAsync(testReq, ct);
            try
            {
                var adapter = await clientFactory.CreateAdapterAsync(saved.Id, ct);
                var result = await adapter.TestConnectionAsync(ct);
                return Results.Ok(result);
            }
            finally
            {
                await configService.DeleteKubernetesClusterAsync(saved.Id, ct);
            }
        });

        // --- Multi-Cluster Node Operations ---

        group.MapGet("/clusters/{id}/nodes", async (
            string id,
            IKubernetesClientFactory clientFactory,
            CancellationToken ct) =>
        {
            var adapter = await clientFactory.CreateAdapterAsync(id, ct);
            var nodes = await adapter.ListNodesAsync(ct);
            return Results.Ok(nodes);
        });

        group.MapPost("/clusters/{id}/nodes/{nodeName}/cordon", async (
            string id,
            string nodeName,
            IKubernetesClientFactory clientFactory,
            CancellationToken ct) =>
        {
            var adapter = await clientFactory.CreateAdapterAsync(id, ct);
            var success = await adapter.CordonNodeAsync(nodeName, ct);
            return success
                ? Results.Ok(new { nodeName, unschedulable = true, message = $"Node '{nodeName}' cordoned successfully." })
                : Results.BadRequest(new { nodeName, message = $"Failed to cordon node '{nodeName}'." });
        });

        group.MapPost("/clusters/{id}/nodes/{nodeName}/uncordon", async (
            string id,
            string nodeName,
            IKubernetesClientFactory clientFactory,
            CancellationToken ct) =>
        {
            var adapter = await clientFactory.CreateAdapterAsync(id, ct);
            var success = await adapter.UncordonNodeAsync(nodeName, ct);
            return success
                ? Results.Ok(new { nodeName, unschedulable = false, message = $"Node '{nodeName}' uncordoned successfully." })
                : Results.BadRequest(new { nodeName, message = $"Failed to uncordon node '{nodeName}'." });
        });

        group.MapPost("/clusters/{id}/nodes/{nodeName}/drain", async (
            string id,
            string nodeName,
            [FromBody] K8sDrainRequest request,
            IKubernetesClientFactory clientFactory,
            CancellationToken ct) =>
        {
            var adapter = await clientFactory.CreateAdapterAsync(id, ct);
            var result = await adapter.DrainNodeAsync(
                nodeName,
                TimeSpan.FromSeconds(request.TimeoutSeconds),
                request.IgnoreDaemonSets,
                request.DeleteEmptyDirData,
                ct);

            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        group.MapGet("/clusters/{id}/nodes/{nodeName}/status", async (
            string id,
            string nodeName,
            IKubernetesClientFactory clientFactory,
            CancellationToken ct) =>
        {
            var adapter = await clientFactory.CreateAdapterAsync(id, ct);
            var status = await adapter.GetNodeStatusAsync(nodeName, ct);
            return status != null
                ? Results.Ok(status)
                : Results.NotFound(new { message = $"Node '{nodeName}' not found or unreachable." });
        });

        // --- Workload & Pod Operations ---

        group.MapGet("/clusters/{id}/namespaces", async (
            string id,
            IKubernetesClientFactory clientFactory,
            CancellationToken ct) =>
        {
            var adapter = await clientFactory.CreateAdapterAsync(id, ct);
            var namespaces = await adapter.ListNamespacesAsync(ct);
            return Results.Ok(namespaces);
        });

        group.MapGet("/clusters/{id}/workloads/deployments", async (
            string id,
            [FromQuery] string? namespaceName,
            IKubernetesClientFactory clientFactory,
            CancellationToken ct) =>
        {
            var adapter = await clientFactory.CreateAdapterAsync(id, ct);
            var deployments = await adapter.ListDeploymentsAsync(namespaceName, ct);
            return Results.Ok(deployments);
        });

        group.MapPost("/clusters/{id}/workloads/deployments/{namespaceName}/{deploymentName}/restart", async (
            string id,
            string namespaceName,
            string deploymentName,
            IKubernetesClientFactory clientFactory,
            CancellationToken ct) =>
        {
            var adapter = await clientFactory.CreateAdapterAsync(id, ct);
            var success = await adapter.RestartDeploymentAsync(namespaceName, deploymentName, ct);
            return success
                ? Results.Ok(new { namespaceName, deploymentName, message = "Rollout restart initiated successfully." })
                : Results.BadRequest(new { message = $"Failed to restart deployment '{namespaceName}/{deploymentName}'." });
        });

        group.MapPost("/clusters/{id}/workloads/deployments/{namespaceName}/{deploymentName}/scale", async (
            string id,
            string namespaceName,
            string deploymentName,
            [FromBody] K8sScaleDeploymentRequest request,
            IKubernetesClientFactory clientFactory,
            CancellationToken ct) =>
        {
            if (request.Replicas < 0)
            {
                return Results.BadRequest(new { message = "Replicas must be greater than or equal to 0." });
            }

            var adapter = await clientFactory.CreateAdapterAsync(id, ct);
            var success = await adapter.ScaleDeploymentAsync(namespaceName, deploymentName, request.Replicas, ct);
            return success
                ? Results.Ok(new { namespaceName, deploymentName, replicas = request.Replicas, message = $"Scaled to {request.Replicas} replicas." })
                : Results.BadRequest(new { message = $"Failed to scale deployment '{namespaceName}/{deploymentName}'." });
        });

        group.MapGet("/clusters/{id}/workloads/pods", async (
            string id,
            [FromQuery] string? namespaceName,
            [FromQuery] string? nodeName,
            IKubernetesClientFactory clientFactory,
            CancellationToken ct) =>
        {
            var adapter = await clientFactory.CreateAdapterAsync(id, ct);
            var pods = await adapter.ListPodsAsync(namespaceName, nodeName, ct);
            return Results.Ok(pods);
        });

        // --- Backward Compatible Single-Cluster Endpoints ---

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
