using Microsoft.AspNetCore.Mvc;
using ControlPlane.Api.Security;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Kubernetes.Helm;

namespace ControlPlane.Api.Features.Workloads;

public static class WorkloadEndpoints
{
    public static void MapWorkloadEndpoints(this IEndpointRouteBuilder app)
    {
        var workloadsGroup = app.MapGroup("/api/v1/workloads")
            .WithTags("Workloads");

        workloadsGroup.MapGet("/", async (
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

        workloadsGroup.MapGet("/{clusterId}/{namespaceName}/{name}/pods", async (
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

        workloadsGroup.MapPost("/{clusterId}/{namespaceName}/{name}/restart", async (
            string clusterId,
            string namespaceName,
            string name,
            [FromQuery] string? kind,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var targetKind = !string.IsNullOrWhiteSpace(kind) ? kind : "Deployment";
            var success = await workloadService.RestartWorkloadAsync(clusterId, namespaceName, name, targetKind, ct);
            return success
                ? Results.Ok(new { success = true, message = $"{targetKind} '{name}' rollout restart triggered successfully." })
                : Results.BadRequest(new { success = false, message = $"Failed to restart {targetKind} '{name}'." });
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("RestartWorkload");

        workloadsGroup.MapPost("/{clusterId}/{namespaceName}/{name}/recreate", async (
            string clusterId,
            string namespaceName,
            string name,
            [FromQuery] string? kind,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var targetKind = !string.IsNullOrWhiteSpace(kind) ? kind : "Deployment";
            var success = await workloadService.RecreateWorkloadPodsAsync(clusterId, namespaceName, name, targetKind, ct);
            return success
                ? Results.Ok(new { success = true, message = $"Recreating pods for {targetKind} '{name}' initiated." })
                : Results.BadRequest(new { success = false, message = $"Failed to recreate pods for {targetKind} '{name}'." });
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("RecreateWorkloadPods");

        workloadsGroup.MapPost("/{clusterId}/{namespaceName}/{name}/scale", async (
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

        workloadsGroup.MapPost("/{clusterId}/{namespaceName}/cronjobs/{name}/trigger", async (
            string clusterId,
            string namespaceName,
            string name,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var success = await workloadService.TriggerCronJobAsync(clusterId, namespaceName, name, ct);
            return success
                ? Results.Ok(new { success = true, message = $"CronJob '{name}' manual execution triggered successfully." })
                : Results.BadRequest(new { success = false, message = $"Failed to trigger CronJob '{name}'." });
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("TriggerCronJob");

        workloadsGroup.MapGet("/{clusterId}/{namespaceName}/{name}/bundle", async (
            string clusterId,
            string namespaceName,
            string name,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var bundle = await workloadService.GetAppBundleAsync(clusterId, namespaceName, name, ct);
            return bundle != null
                ? Results.Ok(bundle)
                : Results.NotFound(new { message = $"App bundle '{name}' in namespace '{namespaceName}' not found." });
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetAppBundle");

        workloadsGroup.MapPost("/{clusterId}/apply", async (
            string clusterId,
            [FromBody] K8sApplyRequestDto req,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var result = await workloadService.ApplyManifestYamlAsync(clusterId, req.YamlContent, req.DryRun, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("ApplyManifestYaml");

        workloadsGroup.MapPost("/{clusterId}/{namespaceName}/{name}/delete", async (
            string clusterId,
            string namespaceName,
            string name,
            [FromBody] K8sDeleteOptionsDto options,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            try
            {
                var success = await workloadService.DeleteAppBundleAsync(clusterId, namespaceName, name, options, ct);
                return success
                    ? Results.Ok(new { success = true, message = $"App bundle '{name}' deleted successfully." })
                    : Results.BadRequest(new { success = false, message = $"Failed to delete app bundle '{name}'." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("DeleteAppBundle");

        var k8sGroup = app.MapGroup("/api/v1/kubernetes")
            .WithTags("Kubernetes");

        k8sGroup.MapGet("/{clusterId}/network/ingresses", async (
            string clusterId,
            [FromQuery] string? namespaceName,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var ingresses = await workloadService.ListIngressesAsync(clusterId, namespaceName, ct);
            return Results.Ok(ingresses);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("ListIngresses");

        k8sGroup.MapGet("/{clusterId}/network/certificates", async (
            string clusterId,
            [FromQuery] string? namespaceName,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var certs = await workloadService.ListCertificatesAsync(clusterId, namespaceName, ct);
            return Results.Ok(certs);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("ListCertificates");

        k8sGroup.MapGet("/{clusterId}/storage", async (
            string clusterId,
            [FromQuery] string? namespaceName,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var storage = await workloadService.GetStorageOverviewAsync(clusterId, namespaceName, ct);
            return Results.Ok(storage);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetStorageOverview");

        k8sGroup.MapGet("/{clusterId}/vitals", async (
            string clusterId,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var vitals = await workloadService.GetClusterVitalsAsync(clusterId, ct);
            return Results.Ok(vitals);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetClusterVitals");

        // --- Secrets ---

        k8sGroup.MapGet("/{clusterId}/secrets", async (
            string clusterId,
            [FromQuery] string? namespaceName,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var secrets = await workloadService.ListSecretsAsync(clusterId, namespaceName, ct);
            return Results.Ok(secrets);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("ListSecrets");

        k8sGroup.MapGet("/{clusterId}/secrets/{namespaceName}/{name}", async (
            string clusterId,
            string namespaceName,
            string name,
            [FromQuery] bool? reveal,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var mask = reveal != true;
            var secret = await workloadService.GetSecretAsync(clusterId, namespaceName, name, mask, ct);
            return secret != null
                ? Results.Ok(secret)
                : Results.NotFound(new { message = $"Secret '{name}' in namespace '{namespaceName}' not found." });
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetSecret");

        k8sGroup.MapPost("/{clusterId}/secrets", async (
            string clusterId,
            [FromBody] K8sCreateSecretRequestDto req,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Namespace))
            {
                return Results.BadRequest(new { message = "Secret name and namespace are required." });
            }

            try
            {
                var result = await workloadService.CreateSecretAsync(clusterId, req.Namespace, req, ct);
                return result.Success
                    ? Results.Ok(result)
                    : Results.BadRequest(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("CreateSecret");

        k8sGroup.MapPut("/{clusterId}/secrets/{namespaceName}/{name}", async (
            string clusterId,
            string namespaceName,
            string name,
            [FromBody] K8sCreateSecretRequestDto req,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            try
            {
                var updateReq = req with { Name = name, Namespace = namespaceName };
                var result = await workloadService.UpdateSecretAsync(clusterId, namespaceName, name, updateReq, ct);
                return result.Success
                    ? Results.Ok(result)
                    : Results.BadRequest(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("UpdateSecret");

        k8sGroup.MapDelete("/{clusterId}/secrets/{namespaceName}/{name}", async (
            string clusterId,
            string namespaceName,
            string name,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            try
            {
                var success = await workloadService.DeleteSecretAsync(clusterId, namespaceName, name, ct);
                return success
                    ? Results.Ok(new { success = true, message = $"Secret '{name}' deleted successfully." })
                    : Results.BadRequest(new { success = false, message = $"Failed to delete secret '{name}'." });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("DeleteSecret");

        // --- ConfigMaps ---

        k8sGroup.MapGet("/{clusterId}/configmaps", async (
            string clusterId,
            [FromQuery] string? namespaceName,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var configMaps = await workloadService.ListConfigMapsAsync(clusterId, namespaceName, ct);
            return Results.Ok(configMaps);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("ListConfigMaps");

        k8sGroup.MapGet("/{clusterId}/configmaps/{namespaceName}/{name}", async (
            string clusterId,
            string namespaceName,
            string name,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var configMap = await workloadService.GetConfigMapAsync(clusterId, namespaceName, name, ct);
            return configMap != null
                ? Results.Ok(configMap)
                : Results.NotFound(new { message = $"ConfigMap '{name}' in namespace '{namespaceName}' not found." });
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetConfigMap");

        k8sGroup.MapPost("/{clusterId}/configmaps", async (
            string clusterId,
            [FromBody] K8sCreateConfigMapRequestDto req,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Namespace))
            {
                return Results.BadRequest(new { message = "ConfigMap name and namespace are required." });
            }

            try
            {
                var result = await workloadService.CreateConfigMapAsync(clusterId, req.Namespace, req, ct);
                return result.Success
                    ? Results.Ok(result)
                    : Results.BadRequest(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("CreateConfigMap");

        k8sGroup.MapPut("/{clusterId}/configmaps/{namespaceName}/{name}", async (
            string clusterId,
            string namespaceName,
            string name,
            [FromBody] K8sCreateConfigMapRequestDto req,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            try
            {
                var updateReq = req with { Name = name, Namespace = namespaceName };
                var result = await workloadService.UpdateConfigMapAsync(clusterId, namespaceName, name, updateReq, ct);
                return result.Success
                    ? Results.Ok(result)
                    : Results.BadRequest(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("UpdateConfigMap");

        k8sGroup.MapDelete("/{clusterId}/configmaps/{namespaceName}/{name}", async (
            string clusterId,
            string namespaceName,
            string name,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            try
            {
                var success = await workloadService.DeleteConfigMapAsync(clusterId, namespaceName, name, ct);
                return success
                    ? Results.Ok(new { success = true, message = $"ConfigMap '{name}' deleted successfully." })
                    : Results.BadRequest(new { success = false, message = $"Failed to delete config map '{name}'." });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("DeleteConfigMap");

        k8sGroup.MapPost("/{clusterId}/namespaces/{namespaceName}/delete", async (
            string clusterId,
            string namespaceName,
            [FromBody] K8sDeleteNamespaceRequestDto req,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            try
            {
                var success = await workloadService.DeleteNamespaceAsync(clusterId, namespaceName, req.ConfirmedName, ct);
                return success
                    ? Results.Ok(new { success = true, message = $"Namespace '{namespaceName}' deletion initiated successfully." })
                    : Results.BadRequest(new { success = false, message = $"Failed to delete namespace '{namespaceName}'." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("DeleteNamespace");

        k8sGroup.MapPost("/{clusterId}/namespaces", async (
            string clusterId,
            [FromBody] K8sCreateNamespaceRequestDto req,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            try
            {
                var success = await workloadService.CreateNamespaceAsync(clusterId, req.Name, req.Labels, req.Annotations, ct);
                return success
                    ? Results.Ok(new { success = true, message = $"Namespace '{req.Name}' created successfully." })
                    : Results.BadRequest(new { success = false, message = $"Failed to create namespace '{req.Name}'." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("CreateNamespace");

        k8sGroup.MapPost("/namespaces", async (
            [FromBody] K8sCreateNamespaceRequestDto req,
            [FromQuery] string? clusterId,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            try
            {
                var success = await workloadService.CreateNamespaceAsync(clusterId ?? string.Empty, req.Name, req.Labels, req.Annotations, ct);
                return success
                    ? Results.Ok(new { success = true, message = $"Namespace '{req.Name}' created successfully." })
                    : Results.BadRequest(new { success = false, message = $"Failed to create namespace '{req.Name}'." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("CreateNamespaceDefault");

        workloadsGroup.MapPost("/{clusterId}/namespaces", async (
            string clusterId,
            [FromBody] K8sCreateNamespaceRequestDto req,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            try
            {
                var success = await workloadService.CreateNamespaceAsync(clusterId, req.Name, req.Labels, req.Annotations, ct);
                return success
                    ? Results.Ok(new { success = true, message = $"Namespace '{req.Name}' created successfully." })
                    : Results.BadRequest(new { success = false, message = $"Failed to create namespace '{req.Name}'." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("WorkloadsCreateNamespace");

        workloadsGroup.MapPost("/namespaces", async (
            [FromBody] K8sCreateNamespaceRequestDto req,
            [FromQuery] string? clusterId,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            try
            {
                var success = await workloadService.CreateNamespaceAsync(clusterId ?? string.Empty, req.Name, req.Labels, req.Annotations, ct);
                return success
                    ? Results.Ok(new { success = true, message = $"Namespace '{req.Name}' created successfully." })
                    : Results.BadRequest(new { success = false, message = $"Failed to create namespace '{req.Name}'." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("WorkloadsCreateNamespaceDefault");

        // --- Helm Releases & Catalog ---

        var helmGroup = app.MapGroup("/api/v1/kubernetes")
            .WithTags("Kubernetes Helm");

        helmGroup.MapGet("/helm/catalog", (
            IWorkloadService workloadService) =>
        {
            var catalog = workloadService.GetHelmCatalog();
            return Results.Ok(catalog);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetHelmCatalog");

        helmGroup.MapGet("/{clusterId}/helm/releases", async (
            string clusterId,
            [FromQuery] string? namespaceName,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var releases = await workloadService.ListHelmReleasesAsync(clusterId, namespaceName, ct);
            return Results.Ok(releases);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("ListHelmReleases");

        helmGroup.MapGet("/{clusterId}/helm/releases/{namespaceName}/{name}", async (
            string clusterId,
            string namespaceName,
            string name,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var detail = await workloadService.GetHelmReleaseAsync(clusterId, namespaceName, name, ct);
            return detail != null
                ? Results.Ok(detail)
                : Results.NotFound(new { message = $"Helm release '{name}' in namespace '{namespaceName}' not found." });
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetHelmReleaseDetail");

        helmGroup.MapGet("/{clusterId}/helm/releases/{namespaceName}/{name}/history", async (
            string clusterId,
            string namespaceName,
            string name,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var history = await workloadService.GetHelmReleaseHistoryAsync(clusterId, namespaceName, name, ct);
            return Results.Ok(history);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetHelmReleaseHistory");

        helmGroup.MapPost("/{clusterId}/helm/releases", async (
            string clusterId,
            [FromBody] InstallHelmReleaseRequestDto request,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ReleaseName) || string.IsNullOrWhiteSpace(request.ChartName))
            {
                return Results.BadRequest(new { message = "Release name and Chart name are required." });
            }

            var result = await workloadService.InstallOrUpgradeHelmReleaseAsync(clusterId, request, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("InstallOrUpgradeHelmRelease");

        helmGroup.MapPost("/{clusterId}/helm/releases/{namespaceName}/{name}/rollback", async (
            string clusterId,
            string namespaceName,
            string name,
            [FromBody] RollbackHelmReleaseRequestDto request,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            if (request.Revision <= 0)
            {
                return Results.BadRequest(new { message = "Revision must be greater than 0." });
            }

            var result = await workloadService.RollbackHelmReleaseAsync(clusterId, namespaceName, name, request.Revision, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("RollbackHelmRelease");

        helmGroup.MapDelete("/{clusterId}/helm/releases/{namespaceName}/{name}", async (
            string clusterId,
            string namespaceName,
            string name,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var result = await workloadService.UninstallHelmReleaseAsync(clusterId, namespaceName, name, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("UninstallHelmRelease");
    }
}
