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

        return group;
    }
}
