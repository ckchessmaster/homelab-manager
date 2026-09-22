using ControlPlane.Api.Features.Adapters.Config;

namespace ControlPlane.Api.Features.Adapters.Proxmox;

public static class ProxmoxProbeEndpoints
{
    public static RouteGroupBuilder MapProxmoxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/adapters/proxmox")
            .WithTags("Proxmox Adapter")
            .RequireAuthorization();

        group.MapGet("/config", async (
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var config = await configService.GetProxmoxConfigAsync(ct);
            return Results.Ok(config);
        })
        .WithName("GetProxmoxConfig")
        .WithSummary("Get active Proxmox VE adapter configuration with masked secret token");

        group.MapPost("/config", async (
            SaveProxmoxConfigRequest request,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.BaseUrl) || string.IsNullOrWhiteSpace(request.ApiTokenId))
            {
                return Results.BadRequest(new { message = "BaseUrl and ApiTokenId are required." });
            }

            var saved = await configService.SaveProxmoxConfigAsync(request, ct);
            return Results.Ok(saved);
        })
        .WithName("SaveProxmoxConfig")
        .WithSummary("Save or update Proxmox VE adapter configuration");

        group.MapPost("/test-connection", async (
            ProxmoxProbeRequest request,
            ProxmoxProbeService probeService,
            IAdapterConfigService configService,
            CancellationToken cancellationToken) =>
        {
            var active = await configService.GetActiveProxmoxOptionsAsync(cancellationToken);

            var baseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? active.BaseUrl : request.BaseUrl.Trim();
            var tokenId = string.IsNullOrWhiteSpace(request.ApiTokenId) ? active.ApiTokenId : request.ApiTokenId.Trim();
            var secret = string.IsNullOrWhiteSpace(request.ApiTokenSecret) || request.ApiTokenSecret == AdapterConfigService.MaskedPlaceholder
                ? active.ApiTokenSecret
                : request.ApiTokenSecret.Trim();

            if (string.IsNullOrWhiteSpace(baseUrl) ||
                string.IsNullOrWhiteSpace(tokenId) ||
                string.IsNullOrWhiteSpace(secret))
            {
                return Results.BadRequest(new
                {
                    message = "BaseUrl, ApiTokenId, and ApiTokenSecret are required (or must be configured beforehand)."
                });
            }

            var effectiveRequest = new ProxmoxProbeRequest(
                BaseUrl: baseUrl,
                ApiTokenId: tokenId,
                ApiTokenSecret: secret,
                AllowSelfSignedCert: request.AllowSelfSignedCert
            );

            var result = await probeService.ProbeAsync(effectiveRequest, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("TestProxmoxConnection")
        .WithSummary("Probe and verify connectivity to a Proxmox VE API endpoint");

        group.MapGet("/instances", async (
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var instances = await configService.GetProxmoxInstancesAsync(ct);
            return Results.Ok(instances);
        })
        .WithName("GetProxmoxInstances")
        .WithSummary("Get all configured Proxmox VE adapter instances");

        group.MapGet("/instances/{id}", async (
            string id,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var instance = await configService.GetProxmoxInstanceAsync(id, ct);
            return instance != null
                ? Results.Ok(instance)
                : Results.NotFound(new { message = $"Proxmox instance '{id}' not found." });
        })
        .WithName("GetProxmoxInstanceById")
        .WithSummary("Get a specific Proxmox VE adapter instance by id");

        group.MapPost("/instances", async (
            SaveProxmoxInstanceRequest request,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.BaseUrl) || string.IsNullOrWhiteSpace(request.ApiTokenId))
            {
                return Results.BadRequest(new { message = "BaseUrl and ApiTokenId are required." });
            }

            var saved = await configService.SaveProxmoxInstanceAsync(request, ct);
            return Results.Ok(saved);
        })
        .WithName("SaveProxmoxInstance")
        .WithSummary("Create or update a Proxmox VE adapter instance");

        group.MapDelete("/instances/{id}", async (
            string id,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var deleted = await configService.DeleteProxmoxInstanceAsync(id, ct);
            return deleted
                ? Results.Ok(new { success = true, id })
                : Results.NotFound(new { message = $"Proxmox instance '{id}' not found." });
        })
        .WithName("DeleteProxmoxInstance")
        .WithSummary("Delete a Proxmox VE adapter instance");

        group.MapPost("/instances/{id}/test-connection", async (
            string id,
            ProxmoxProbeService probeService,
            IAdapterConfigService configService,
            CancellationToken cancellationToken) =>
        {
            var options = await configService.GetActiveProxmoxOptionsAsync(id, cancellationToken);
            if (string.IsNullOrWhiteSpace(options.BaseUrl) ||
                string.IsNullOrWhiteSpace(options.ApiTokenId) ||
                string.IsNullOrWhiteSpace(options.ApiTokenSecret))
            {
                return Results.BadRequest(new
                {
                    message = $"Proxmox instance '{id}' has incomplete credentials."
                });
            }

            var probeRequest = new ProxmoxProbeRequest(
                BaseUrl: options.BaseUrl,
                ApiTokenId: options.ApiTokenId,
                ApiTokenSecret: options.ApiTokenSecret,
                AllowSelfSignedCert: options.AllowSelfSignedCert
            );

            var result = await probeService.ProbeAsync(probeRequest, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("TestProxmoxInstanceConnection")
        .WithSummary("Probe and verify connectivity to a specific saved Proxmox instance");

        group.MapGet("/instances/{id}/vitals", async (
            string id,
            IProxmoxClientFactory clientFactory,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var instance = await configService.GetProxmoxInstanceAsync(id, ct);
            if (instance == null)
            {
                return Results.NotFound(new { message = $"Proxmox instance '{id}' not found." });
            }

            try
            {
                var client = await clientFactory.GetClientAsync(id, ct);
                var nodes = await client.ListNodesAsync(ct);

                var nodeVitals = nodes.Select(n =>
                {
                    double? cpuPct = n.Cpu.HasValue
                        ? Math.Round(n.Cpu.Value <= 1.0 ? n.Cpu.Value * 100 : n.Cpu.Value, 1)
                        : null;
                    double? memPct = n.Memory.HasValue && n.MaxMemory.HasValue && n.MaxMemory.Value > 0
                        ? Math.Round((double)n.Memory.Value / n.MaxMemory.Value * 100, 1)
                        : null;

                    return new ProxmoxNodeVitalsDto(
                        Node: n.Node,
                        Status: n.Status,
                        CpuUsagePct: cpuPct,
                        MaxCpu: n.MaxCpu,
                        MemoryUsedBytes: n.Memory,
                        MemoryMaxBytes: n.MaxMemory,
                        MemoryUsagePct: memPct,
                        UptimeSeconds: n.Uptime
                    );
                }).ToList();

                var vitals = new ProxmoxVitalsDto(
                    InstanceId: instance.Id,
                    InstanceName: instance.Name,
                    Version: null,
                    TotalNodes: nodeVitals.Count,
                    OnlineNodes: nodeVitals.Count(n => string.Equals(n.Status, "online", StringComparison.OrdinalIgnoreCase)),
                    Nodes: nodeVitals,
                    FetchedAt: DateTimeOffset.UtcNow
                );

                return Results.Ok(vitals);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500);
            }
        })
        .WithName("GetProxmoxInstanceVitals")
        .WithSummary("Retrieve real-time node and cluster vitals for a Proxmox VE instance");

        return group;
    }
}
