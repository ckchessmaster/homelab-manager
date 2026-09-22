using System.Diagnostics;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Features.Adapters.HomeAssistant;

public static class HomeAssistantEndpoints
{
    public static IEndpointRouteBuilder MapHomeAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/adapters/home-assistant")
            .RequireAuthorization();

        // --- Instance Management ---

        group.MapGet("/instances", async (
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var instances = await configService.GetHomeAssistantInstancesAsync(ct);
            return Results.Ok(instances);
        });

        group.MapGet("/instances/{id}", async (
            string id,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var instance = await configService.GetHomeAssistantInstanceAsync(id, ct);
            return instance != null
                ? Results.Ok(instance)
                : Results.NotFound(new { message = $"Home Assistant instance '{id}' not found." });
        });

        group.MapPost("/instances", async (
            SaveHomeAssistantInstanceRequest request,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "Home Assistant instance name is required." });
            }

            if (string.IsNullOrWhiteSpace(request.BaseUrl))
            {
                return Results.BadRequest(new { message = "Home Assistant base URL is required." });
            }

            var saved = await configService.SaveHomeAssistantInstanceAsync(request, ct);
            return Results.Ok(saved);
        });

        group.MapDelete("/instances/{id}", async (
            string id,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var deleted = await configService.DeleteHomeAssistantInstanceAsync(id, ct);
            return deleted
                ? Results.Ok(new { message = $"Home Assistant instance '{id}' deleted." })
                : Results.NotFound(new { message = $"Home Assistant instance '{id}' not found." });
        });

        // --- Connectivity Testing ---

        group.MapPost("/test-connection", async (
            SaveHomeAssistantInstanceRequest request,
            IHomeAssistantClient client,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.BaseUrl))
            {
                return Results.BadRequest(new { message = "Base URL is required for testing." });
            }

            var result = await client.TestConnectionAsync(
                request.BaseUrl,
                request.Token ?? string.Empty,
                request.AllowSelfSignedCert,
                ct);

            return Results.Ok(result);
        });

        group.MapPost("/instances/{id}/test-connection", async (
            string id,
            IHomeAssistantClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, token) = await factory.ResolveAsync(id, ct);
                var result = await client.TestConnectionAsync(
                    config.BaseUrl,
                    token,
                    config.AllowSelfSignedCert,
                    ct);

                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"Home Assistant instance '{id}' not found." });
            }
        });

        // --- Telemetry & Overview ---

        group.MapGet("/instances/{id}/overview", async (
            string id,
            IHomeAssistantClientFactory factory,
            ControlPlaneDbContext dbContext,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, token) = await factory.ResolveAsync(id, ct);
                var sw = Stopwatch.StartNew();

                var hostTask = client.GetHostInfoAsync(config.BaseUrl, token, config.AllowSelfSignedCert, ct);
                var osTask = client.GetOsInfoAsync(config.BaseUrl, token, config.AllowSelfSignedCert, ct);
                var coreTask = client.GetCoreInfoAsync(config.BaseUrl, token, config.AllowSelfSignedCert, ct);
                var supTask = client.GetSupervisorInfoAsync(config.BaseUrl, token, config.AllowSelfSignedCert, ct);
                var backupsTask = client.ListBackupsAsync(config.BaseUrl, token, config.AllowSelfSignedCert, ct);

                await Task.WhenAll(hostTask, osTask, coreTask, supTask, backupsTask);

                var host = await hostTask;
                var os = await osTask;
                var core = await coreTask;
                var sup = await supTask;
                var backups = await backupsTask;

                // Correlate with managed hosts by IP or Hostname
                Guid? matchedHostId = null;
                string? matchedHostName = null;

                var uri = Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var parsedUri) ? parsedUri : null;
                var hostAddress = uri?.Host;

                if (!string.IsNullOrWhiteSpace(hostAddress))
                {
                    var existingHost = await dbContext.Hosts.AsNoTracking().FirstOrDefaultAsync(h =>
                        h.IpAddress == hostAddress ||
                        (host != null && !string.IsNullOrWhiteSpace(host.Hostname) && h.Hostname.ToLower() == host.Hostname.ToLower()), ct);

                    if (existingHost != null)
                    {
                        matchedHostId = existingHost.Id;
                        matchedHostName = existingHost.Hostname;
                    }
                }

                var overview = new HomeAssistantOverviewDto(
                    InstanceId: config.Id,
                    Name: config.Name,
                    BaseUrl: config.BaseUrl,
                    Host: host,
                    Os: os,
                    Core: core,
                    Supervisor: sup,
                    RecentBackups: backups.Take(5).ToList(),
                    CorrelatedHostId: matchedHostId,
                    CorrelatedHostName: matchedHostName,
                    LatencyMs: sw.ElapsedMilliseconds,
                    FetchedAt: DateTimeOffset.UtcNow
                );

                return Results.Ok(overview);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"Home Assistant instance '{id}' not found." });
            }
        });

        // --- Appliance Controls ---

        group.MapPost("/instances/{id}/core/check", async (
            string id,
            IHomeAssistantClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, token) = await factory.ResolveAsync(id, ct);
                var result = await client.CheckCoreConfigAsync(config.BaseUrl, token, config.AllowSelfSignedCert, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"Home Assistant instance '{id}' not found." });
            }
        });

        group.MapPost("/instances/{id}/core/restart", async (
            string id,
            IHomeAssistantClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, token) = await factory.ResolveAsync(id, ct);
                var success = await client.RestartCoreAsync(config.BaseUrl, token, config.AllowSelfSignedCert, ct);
                return success
                    ? Results.Ok(new { message = "Home Assistant Core restart initiated." })
                    : Results.BadRequest(new { message = "Failed to restart Home Assistant Core." });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"Home Assistant instance '{id}' not found." });
            }
        });

        group.MapPost("/instances/{id}/host/reboot", async (
            string id,
            IHomeAssistantClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, token) = await factory.ResolveAsync(id, ct);
                var success = await client.RebootHostAsync(config.BaseUrl, token, config.AllowSelfSignedCert, ct);
                return success
                    ? Results.Ok(new { message = "Home Assistant Host OS reboot initiated." })
                    : Results.BadRequest(new { message = "Failed to reboot Home Assistant host." });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"Home Assistant instance '{id}' not found." });
            }
        });

        group.MapPost("/instances/{id}/os/update", async (
            string id,
            IHomeAssistantClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, token) = await factory.ResolveAsync(id, ct);
                var success = await client.UpdateOsAsync(config.BaseUrl, token, config.AllowSelfSignedCert, ct);
                return success
                    ? Results.Ok(new { message = "Home Assistant OS update initiated." })
                    : Results.BadRequest(new { message = "Failed to trigger Home Assistant OS update." });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"Home Assistant instance '{id}' not found." });
            }
        });

        group.MapGet("/instances/{id}/backups", async (
            string id,
            IHomeAssistantClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, token) = await factory.ResolveAsync(id, ct);
                var backups = await client.ListBackupsAsync(config.BaseUrl, token, config.AllowSelfSignedCert, ct);
                return Results.Ok(backups);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"Home Assistant instance '{id}' not found." });
            }
        });

        group.MapPost("/instances/{id}/backups", async (
            string id,
            [FromBody] CreateHomeAssistantBackupRequest? request,
            IHomeAssistantClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, token) = await factory.ResolveAsync(id, ct);
                var res = await client.CreateBackupAsync(
                    config.BaseUrl,
                    token,
                    request?.Name,
                    request?.Password,
                    config.AllowSelfSignedCert,
                    ct);

                return res.Success
                    ? Results.Accepted($"/api/v1/adapters/home-assistant/instances/{id}/backups", res)
                    : Results.BadRequest(res);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"Home Assistant instance '{id}' not found." });
            }
        });

        return app;
    }
}
