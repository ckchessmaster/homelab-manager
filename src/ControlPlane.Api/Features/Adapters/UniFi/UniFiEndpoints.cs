using ControlPlane.Api.Features.Adapters.Config;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.Adapters.UniFi;

public static class UniFiEndpoints
{
    public static IEndpointRouteBuilder MapUniFiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/adapters/unifi")
            .RequireAuthorization();

        // --- Instance Management ---

        group.MapGet("/instances", async (
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var instances = await configService.GetUniFiInstancesAsync(ct);
            return Results.Ok(instances);
        });

        group.MapGet("/instances/{id}", async (
            string id,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var instance = await configService.GetUniFiInstanceAsync(id, ct);
            return instance != null
                ? Results.Ok(instance)
                : Results.NotFound(new { message = $"UniFi controller '{id}' not found." });
        });

        group.MapPost("/instances", async (
            SaveUniFiInstanceRequest request,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "Controller name is required." });
            }
            if (string.IsNullOrWhiteSpace(request.ControllerUrl))
            {
                return Results.BadRequest(new { message = "Controller URL is required." });
            }

            var isApiKey = string.Equals(request.AuthType, "api_key", StringComparison.OrdinalIgnoreCase) ||
                           !string.IsNullOrWhiteSpace(request.ApiKey);

            if (!isApiKey && string.IsNullOrWhiteSpace(request.Username))
            {
                return Results.BadRequest(new { message = "Username is required when using credential authentication." });
            }

            var saved = await configService.SaveUniFiInstanceAsync(request, ct);
            return Results.Ok(saved);
        });

        group.MapDelete("/instances/{id}", async (
            string id,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var success = await configService.DeleteUniFiInstanceAsync(id, ct);
            return success
                ? Results.Ok(new { message = $"UniFi controller '{id}' removed successfully." })
                : Results.NotFound(new { message = $"UniFi controller '{id}' not found." });
        });

        group.MapPost("/instances/{id}/test-connection", async (
            string id,
            IUniFiClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, password, apiKey) = await factory.ResolveAsync(id, ct);
                var result = await client.TestConnectionAsync(
                    config.ControllerUrl,
                    config.Username,
                    password,
                    config.Site,
                    apiKey,
                    ct);

                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"UniFi controller '{id}' not found." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new UniFiTestResultDto(false, null, null, null, null, 0, ex.Message));
            }
        });

        group.MapPost("/test-connection", async (
            SaveUniFiInstanceRequest request,
            IUniFiClient client,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ControllerUrl))
            {
                return Results.BadRequest(new { message = "Controller URL is required." });
            }

            var isApiKey = string.Equals(request.AuthType, "api_key", StringComparison.OrdinalIgnoreCase) ||
                           !string.IsNullOrWhiteSpace(request.ApiKey);

            if (!isApiKey && string.IsNullOrWhiteSpace(request.Username))
            {
                return Results.BadRequest(new { message = "Username is required when using credential authentication." });
            }

            if (isApiKey && string.IsNullOrWhiteSpace(request.ApiKey))
            {
                return Results.BadRequest(new { message = "API Key is required when using API Key authentication." });
            }

            var result = await client.TestConnectionAsync(
                request.ControllerUrl,
                request.Username,
                request.Password ?? string.Empty,
                string.IsNullOrWhiteSpace(request.Site) ? "default" : request.Site,
                request.ApiKey,
                ct);

            return Results.Ok(result);
        });

        // --- Device and Port Management per Instance ---

        group.MapGet("/{id}/devices", async (
            string id,
            IUniFiClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, password, apiKey) = await factory.ResolveAsync(id, ct);
                var devices = await client.GetDevicesAsync(
                    config.ControllerUrl,
                    config.Username,
                    password,
                    config.Site,
                    apiKey,
                    ct);

                return Results.Ok(devices);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"UniFi controller '{id}' not found." });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500);
            }
        });

        group.MapPost("/{id}/devices/{deviceMac}/restart", async (
            string id,
            string deviceMac,
            [FromBody] UniFiDeviceRestartRequest? req,
            IUniFiClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, password, apiKey) = await factory.ResolveAsync(id, ct);
                var success = await client.RestartDeviceAsync(
                    config.ControllerUrl,
                    config.Username,
                    password,
                    deviceMac,
                    config.Site,
                    apiKey,
                    ct);

                return success
                    ? Results.Ok(new { message = $"Restart command sent to device {deviceMac}." })
                    : Results.BadRequest(new { message = $"Failed to restart device {deviceMac}." });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"UniFi controller '{id}' not found." });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500);
            }
        });

        group.MapPost("/{id}/devices/{deviceMac}/upgrade", async (
            string id,
            string deviceMac,
            [FromBody] UniFiDeviceUpgradeRequest? req,
            IUniFiClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, password, apiKey) = await factory.ResolveAsync(id, ct);
                var success = await client.UpgradeDeviceAsync(
                    config.ControllerUrl,
                    config.Username,
                    password,
                    deviceMac,
                    config.Site,
                    apiKey,
                    ct);

                return success
                    ? Results.Ok(new { message = $"Upgrade command sent to device {deviceMac}." })
                    : Results.BadRequest(new { message = $"Failed to trigger upgrade on device {deviceMac}." });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"UniFi controller '{id}' not found." });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500);
            }
        });

        group.MapPost("/{id}/devices/{deviceMac}/ports/{portIdx:int}/power-cycle", async (
            string id,
            string deviceMac,
            int portIdx,
            [FromBody] UniFiPortCycleRequest? req,
            IUniFiClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, password, apiKey) = await factory.ResolveAsync(id, ct);
                var delay = req?.DelaySeconds ?? 5;
                var result = await client.CyclePoEPortAsync(
                    config.ControllerUrl,
                    config.Username,
                    password,
                    deviceMac,
                    portIdx,
                    config.Site,
                    delay,
                    apiKey,
                    ct);

                return result.Success ? Results.Ok(result) : Results.BadRequest(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"UniFi controller '{id}' not found." });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500);
            }
        });

        group.MapGet("/{id}/clients", async (
            string id,
            IUniFiClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, password, apiKey) = await factory.ResolveAsync(id, ct);
                var clients = await client.GetActiveClientsAsync(
                    config.ControllerUrl,
                    config.Username,
                    password,
                    config.Site,
                    apiKey,
                    ct);

                return Results.Ok(clients);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"UniFi controller '{id}' not found." });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500);
            }
        });

        // --- Backward Compatibility Endpoints ---

        group.MapPost("/bounce-poe", async (
            UniFiPortBounceRequest request,
            IUniFiClient unifiClient,
            CancellationToken ct) =>
        {
            var result = await unifiClient.CyclePoEPortAsync(
                request.ControllerUrl,
                request.Username,
                request.Password,
                request.SwitchMac,
                request.PortNumber,
                request.Site,
                request.DelaySeconds,
                apiKey: null,
                ct);

            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        group.MapGet("/clients", async (
            string controllerUrl,
            string username,
            string password,
            string site = "default",
            IUniFiClient unifiClient = null!,
            CancellationToken ct = default) =>
        {
            var clients = await unifiClient.GetActiveClientsAsync(controllerUrl, username, password, site, apiKey: null, ct);
            return Results.Ok(clients);
        });

        return app;
    }
}
