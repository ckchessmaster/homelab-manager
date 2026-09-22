using Microsoft.AspNetCore.Mvc;
using ControlPlane.Api.Security;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Kubernetes.Helm;
using ControlPlane.Api.Features.Workloads.ImageUpdates;

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
            [FromQuery] bool? includeAll,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var result = await workloadService.GetAggregatedWorkloadsAsync(clusterId, namespaceName, includeAll == true, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetAggregatedWorkloads");

        workloadsGroup.MapGet("/image-updates", (
            IImageUpdateService imageUpdateService) =>
        {
            var cached = imageUpdateService.GetAllCached();
            return Results.Ok(cached);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetCachedImageUpdates");

        workloadsGroup.MapPost("/image-updates/check", async (
            [FromBody] ImageCheckRequestDto? req,
            IImageUpdateService imageUpdateService,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            IEnumerable<string> imagesToCheck;
            if (req?.Images != null && req.Images.Count > 0)
            {
                imagesToCheck = req.Images;
            }
            else
            {
                var workloads = await workloadService.GetAggregatedWorkloadsAsync(null, null, ct);
                imagesToCheck = workloads.Items.SelectMany(w => w.Images).Distinct();
            }

            var results = await imageUpdateService.CheckImagesAsync(imagesToCheck, req?.Force ?? false, ct);
            return Results.Ok(results);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("CheckImageUpdates");

        workloadsGroup.MapGet("/images/tags", async (
            [FromQuery] string image,
            IImageUpdateService imageUpdateService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(image)) return Results.Ok(new List<string>());
            var tags = await imageUpdateService.GetImageTagsAsync(image, ct);
            return Results.Ok(tags);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetWorkloadImageTags");

        workloadsGroup.MapPost("/{clusterId}/{namespaceName}/{name}/image", async (
            string clusterId,
            string namespaceName,
            string name,
            [FromBody] UpdateWorkloadImageRequestDto req,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Image))
            {
                return Results.BadRequest(new { success = false, message = "Target container image reference is required." });
            }

            var success = await workloadService.UpdateWorkloadImageAsync(
                clusterId,
                namespaceName,
                name,
                req.Image,
                req.Kind ?? "Deployment",
                req.ContainerName,
                ct);

            return success
                ? Results.Ok(new { success = true, image = req.Image, message = $"Workload '{name}' container image updated to '{req.Image}'." })
                : Results.BadRequest(new { success = false, message = $"Failed to update container image for '{name}'." });
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("UpdateWorkloadImage");

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

        workloadsGroup.MapGet("/{clusterId}/{namespaceName}/pods/{podName}/logs", async (
            string clusterId,
            string namespaceName,
            string podName,
            [FromQuery] string? container,
            [FromQuery] int? tailLines,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var logs = await workloadService.GetPodLogsAsync(clusterId, namespaceName, podName, container, tailLines ?? 100, ct);
            return Results.Ok(new { podName, container, logs });
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetPodLogs");

        workloadsGroup.MapGet("/{clusterId}/{namespaceName}/{name}/revisions", async (
            string clusterId,
            string namespaceName,
            string name,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var revisions = await workloadService.GetDeploymentRevisionsAsync(clusterId, namespaceName, name, ct);
            return Results.Ok(revisions);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetDeploymentRevisions");

        workloadsGroup.MapPost("/{clusterId}/{namespaceName}/{name}/rollback", async (
            string clusterId,
            string namespaceName,
            string name,
            [FromBody] K8sRollbackRequestDto req,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var success = await workloadService.RollbackDeploymentAsync(clusterId, namespaceName, name, req.Revision, ct);
            return success
                ? Results.Ok(new { success = true, message = $"Deployment '{name}' successfully rolled back to revision {req.Revision}." })
                : Results.BadRequest(new { success = false, message = $"Failed to roll back deployment '{name}' to revision {req.Revision}." });
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("RollbackDeployment");

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

        workloadsGroup.MapGet("/{clusterId}/{namespaceName}/{name}/yaml", async (
            string clusterId,
            string namespaceName,
            string name,
            [FromQuery] string? kind,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var yaml = await workloadService.GetResourceYamlAsync(clusterId, namespaceName, name, kind, ct);
            return yaml != null
                ? Results.Ok(new K8sResourceYamlDto(name, namespaceName, kind ?? "Resource", yaml))
                : Results.NotFound(new { message = $"Resource '{name}' ({kind ?? "Resource"}) in namespace '{namespaceName}' not found." });
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetResourceYaml");

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

        k8sGroup.MapGet("/{clusterId}/network/services", async (
            string clusterId,
            [FromQuery] string? namespaceName,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var services = await workloadService.ListServicesAsync(clusterId, namespaceName, ct);
            return Results.Ok(services);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("ListServices");

        k8sGroup.MapGet("/{clusterId}/network/services/{namespaceName}/{name}", async (
            string clusterId,
            string namespaceName,
            string name,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var service = await workloadService.GetServiceAsync(clusterId, namespaceName, name, ct);
            return service != null
                ? Results.Ok(service)
                : Results.NotFound(new { message = $"Service '{name}' in namespace '{namespaceName}' not found." });
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetService");

        k8sGroup.MapPut("/{clusterId}/network/services/{namespaceName}/{name}", async (
            string clusterId,
            string namespaceName,
            string name,
            [FromBody] K8sUpdateServiceRequestDto request,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var result = await workloadService.UpdateServiceAsync(clusterId, namespaceName, name, request, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("UpdateService");

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

        k8sGroup.MapGet("/{clusterId}/network/ingresses/{namespaceName}/{name}", async (
            string clusterId,
            string namespaceName,
            string name,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var ingress = await workloadService.GetIngressAsync(clusterId, namespaceName, name, ct);
            return ingress != null
                ? Results.Ok(ingress)
                : Results.NotFound(new { message = $"Ingress '{name}' in namespace '{namespaceName}' not found." });
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetIngress");

        k8sGroup.MapPut("/{clusterId}/network/ingresses/{namespaceName}/{name}", async (
            string clusterId,
            string namespaceName,
            string name,
            [FromBody] K8sUpdateIngressRequestDto request,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var result = await workloadService.UpdateIngressAsync(clusterId, namespaceName, name, request, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .RequireAuthorization(AuthConstants.RequireOperator)
        .WithName("UpdateIngress");

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

        helmGroup.MapGet("/helm/charts/{chartName}/versions", async (
            string chartName,
            [FromQuery] string? repoUrl,
            IHelmUpdateService helmUpdateService,
            CancellationToken ct) =>
        {
            var versions = await helmUpdateService.GetChartVersionsAsync(chartName, repoUrl, ct);
            return Results.Ok(versions);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetHelmChartVersions");

        helmGroup.MapGet("/helm/updates", (
            IHelmUpdateService helmUpdateService) =>
        {
            var cached = helmUpdateService.GetAllCached();
            return Results.Ok(cached);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetAllHelmUpdates");

        helmGroup.MapGet("/{clusterId}/helm/updates", (
            IHelmUpdateService helmUpdateService) =>
        {
            var cached = helmUpdateService.GetAllCached();
            return Results.Ok(cached);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("GetClusterHelmUpdates");

        helmGroup.MapPost("/{clusterId}/helm/updates/check", async (
            string clusterId,
            [FromBody] HelmCheckUpdatesRequestDto? req,
            IWorkloadService workloadService,
            IHelmUpdateService helmUpdateService,
            CancellationToken ct) =>
        {
            var releases = await workloadService.ListHelmReleasesAsync(clusterId, null, ct);
            if (req?.ReleaseNames != null && req.ReleaseNames.Count > 0)
            {
                var filterSet = new HashSet<string>(req.ReleaseNames, StringComparer.OrdinalIgnoreCase);
                releases = releases.Where(r => filterSet.Contains(r.Name)).ToList();
            }

            var results = await helmUpdateService.CheckReleasesAsync(releases, req?.Force ?? false, ct);
            return Results.Ok(results);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithName("CheckHelmUpdates");

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
            [FromQuery] int? revision,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var detail = await workloadService.GetHelmReleaseAsync(clusterId, namespaceName, name, revision, ct);
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

        app.MapGet("/api/v1/events", async (
            [FromQuery] string? clusterId,
            [FromQuery] string? namespaceName,
            [FromQuery] string? type,
            IWorkloadService workloadService,
            CancellationToken ct) =>
        {
            var events = await workloadService.GetClusterEventsAsync(clusterId, namespaceName, type, ct);
            return Results.Ok(events);
        })
        .RequireAuthorization(AuthConstants.RequireViewer)
        .WithTags("Events")
        .WithName("GetClusterEvents");
    }
}
