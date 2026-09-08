using ControlPlane.Api.Features.Adapters.Config;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.Adapters.Idrac;

public static class IdracEndpoints
{
    public static IEndpointRouteBuilder MapIdracEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/adapters/idrac")
            .RequireAuthorization();

        // --- Instance Management ---

        group.MapGet("/instances", async (
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var instances = await configService.GetIdracInstancesAsync(ct);
            return Results.Ok(instances);
        });

        group.MapGet("/instances/{id}", async (
            string id,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var instance = await configService.GetIdracInstanceAsync(id, ct);
            return instance != null
                ? Results.Ok(instance)
                : Results.NotFound(new { message = $"iDRAC / BMC instance '{id}' not found." });
        });

        group.MapPost("/instances", async (
            SaveIdracInstanceRequest request,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "BMC instance name is required." });
            }

            if (string.IsNullOrWhiteSpace(request.BmcUrl))
            {
                return Results.BadRequest(new { message = "BMC URL is required." });
            }

            var saved = await configService.SaveIdracInstanceAsync(request, ct);
            return Results.Ok(saved);
        });

        group.MapDelete("/instances/{id}", async (
            string id,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var deleted = await configService.DeleteIdracInstanceAsync(id, ct);
            return deleted
                ? Results.Ok(new { message = $"iDRAC instance '{id}' deleted." })
                : Results.NotFound(new { message = $"iDRAC instance '{id}' not found." });
        });

        // --- Connectivity Testing ---

        // Pre-flight test without saving
        group.MapPost("/test-connection", async (
            SaveIdracInstanceRequest request,
            IIdracClient client,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.BmcUrl))
            {
                return Results.BadRequest(new { message = "BMC URL is required for testing." });
            }

            var result = await client.TestConnectionAsync(
                request.BmcUrl,
                request.Username,
                request.Password ?? string.Empty,
                request.AllowSelfSignedCert,
                ct);

            return Results.Ok(result);
        });

        // Test existing saved instance
        group.MapPost("/instances/{id}/test-connection", async (
            string id,
            IIdracClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, password) = await factory.ResolveAsync(id, ct);
                var result = await client.TestConnectionAsync(
                    config.BmcUrl,
                    config.Username,
                    password,
                    config.AllowSelfSignedCert,
                    ct);

                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"iDRAC instance '{id}' not found." });
            }
        });

        // --- Vitals & Telemetry ---

        group.MapGet("/instances/{id}/vitals", async (
            string id,
            IIdracClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, password) = await factory.ResolveAsync(id, ct);
                var vitals = await client.GetVitalsAsync(
                    config.BmcUrl,
                    config.Username,
                    password,
                    config.AllowSelfSignedCert,
                    ct);

                return Results.Ok(vitals);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"iDRAC instance '{id}' not found." });
            }
        });

        // --- Hardware Power Control ---

        group.MapPost("/instances/{id}/power", async (
            string id,
            IdracPowerControlRequest request,
            IIdracClientFactory factory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ResetType))
            {
                return Results.BadRequest(new { message = "ResetType is required (On, GracefulShutdown, ForceOff, PowerCycle)." });
            }

            try
            {
                var (client, config, password) = await factory.ResolveAsync(id, ct);
                var response = await client.ResetSystemAsync(
                    config.BmcUrl,
                    config.Username,
                    password,
                    request.ResetType,
                    config.AllowSelfSignedCert,
                    ct);

                return response.Success
                    ? Results.Ok(response)
                    : Results.BadRequest(response);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"iDRAC instance '{id}' not found." });
            }
        });

        // Power action directly by IP (used from Host inventory view)
        group.MapPost("/power-action-by-ip", async (
            IdracPowerActionByIpRequest request,
            IIdracClientFactory factory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.IdracIp))
            {
                return Results.BadRequest(new { message = "IdracIp is required." });
            }

            if (string.IsNullOrWhiteSpace(request.ResetType))
            {
                return Results.BadRequest(new { message = "ResetType is required." });
            }

            string username = request.Username ?? string.Empty;
            string password = request.Password ?? string.Empty;
            string bmcUrl = request.IdracIp;
            bool insecureTls = request.InsecureTls;

            // If credentials not supplied in request, attempt to resolve from configured instances
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                var resolved = await factory.ResolveByHostBmcIpAsync(request.IdracIp, ct);
                if (resolved != null)
                {
                    bmcUrl = resolved.Value.BmcUrl;
                    username = resolved.Value.Username;
                    password = resolved.Value.Password;
                    insecureTls = resolved.Value.AllowSelfSigned;
                }
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return Results.BadRequest(new { message = $"No configured credentials found for BMC IP '{request.IdracIp}'." });
            }

            var result = await factory.GetClient().ResetSystemAsync(
                bmcUrl,
                username,
                password,
                request.ResetType,
                insecureTls,
                ct);

            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        return app;
    }
}
