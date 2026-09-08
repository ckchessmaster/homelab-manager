using ControlPlane.Api.Features.Adapters.Config;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.Adapters.OPNsense;

public static class OPNsenseEndpoints
{
    public static IEndpointRouteBuilder MapOPNsenseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/adapters/opnsense")
            .RequireAuthorization();

        // --- Instance Management ---

        group.MapGet("/instances", async (
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var instances = await configService.GetOPNsenseInstancesAsync(ct);
            return Results.Ok(instances);
        });

        group.MapGet("/instances/{id}", async (
            string id,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var instance = await configService.GetOPNsenseInstanceAsync(id, ct);
            return instance != null
                ? Results.Ok(instance)
                : Results.NotFound(new { message = $"OPNsense firewall '{id}' not found." });
        });

        group.MapPost("/instances", async (
            SaveOPNsenseInstanceRequest request,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "Firewall name is required." });
            }

            if (string.IsNullOrWhiteSpace(request.BaseUrl))
            {
                return Results.BadRequest(new { message = "Firewall base URL is required." });
            }

            var saved = await configService.SaveOPNsenseInstanceAsync(request, ct);
            return Results.Ok(saved);
        });

        group.MapDelete("/instances/{id}", async (
            string id,
            IAdapterConfigService configService,
            CancellationToken ct) =>
        {
            var deleted = await configService.DeleteOPNsenseInstanceAsync(id, ct);
            return deleted
                ? Results.Ok(new { message = $"OPNsense instance '{id}' deleted." })
                : Results.NotFound(new { message = $"OPNsense instance '{id}' not found." });
        });

        // --- Connectivity Testing ---

        // Pre-flight test without saving
        group.MapPost("/test-connection", async (
            SaveOPNsenseInstanceRequest request,
            IOPNsenseClient client,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.BaseUrl))
            {
                return Results.BadRequest(new { message = "Base URL is required for testing." });
            }

            var result = await client.TestConnectionAsync(
                request.BaseUrl,
                request.ApiKey,
                request.ApiSecret ?? string.Empty,
                request.AllowSelfSignedCert,
                ct);

            return Results.Ok(result);
        });

        // Test existing saved instance
        group.MapPost("/instances/{id}/test-connection", async (
            string id,
            IOPNsenseClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, secret) = await factory.ResolveAsync(id, ct);
                var result = await client.TestConnectionAsync(
                    config.BaseUrl,
                    config.ApiKey,
                    secret,
                    config.AllowSelfSignedCert,
                    ct);

                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"OPNsense instance '{id}' not found." });
            }
        });

        // --- Telemetry & Status ---

        group.MapGet("/instances/{id}/telemetry", async (
            string id,
            IOPNsenseClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, secret) = await factory.ResolveAsync(id, ct);
                var telemetry = await client.GetTelemetryAsync(
                    config.BaseUrl,
                    config.ApiKey,
                    secret,
                    config.AllowSelfSignedCert,
                    ct);

                return Results.Ok(telemetry);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"OPNsense instance '{id}' not found." });
            }
        });

        group.MapGet("/instances/{id}/gateways", async (
            string id,
            IOPNsenseClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, secret) = await factory.ResolveAsync(id, ct);
                var gateways = await client.GetGatewaysAsync(
                    config.BaseUrl,
                    config.ApiKey,
                    secret,
                    config.AllowSelfSignedCert,
                    ct);

                return Results.Ok(gateways);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"OPNsense instance '{id}' not found." });
            }
        });

        group.MapGet("/instances/{id}/interfaces", async (
            string id,
            IOPNsenseClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, secret) = await factory.ResolveAsync(id, ct);
                var interfaces = await client.GetInterfacesAsync(
                    config.BaseUrl,
                    config.ApiKey,
                    secret,
                    config.AllowSelfSignedCert,
                    ct);

                return Results.Ok(interfaces);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"OPNsense instance '{id}' not found." });
            }
        });

        // --- Services Management ---

        group.MapGet("/instances/{id}/services", async (
            string id,
            IOPNsenseClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, secret) = await factory.ResolveAsync(id, ct);
                var services = await client.GetServicesAsync(
                    config.BaseUrl,
                    config.ApiKey,
                    secret,
                    config.AllowSelfSignedCert,
                    ct);

                return Results.Ok(services);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"OPNsense instance '{id}' not found." });
            }
        });

        group.MapPost("/instances/{id}/services/{serviceName}/restart", async (
            string id,
            string serviceName,
            IOPNsenseClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, secret) = await factory.ResolveAsync(id, ct);
                var result = await client.RestartServiceAsync(
                    config.BaseUrl,
                    config.ApiKey,
                    secret,
                    serviceName,
                    config.AllowSelfSignedCert,
                    ct);

                return result.Success
                    ? Results.Ok(result)
                    : Results.BadRequest(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"OPNsense instance '{id}' not found." });
            }
        });

        // --- DHCP Leases ---

        group.MapGet("/instances/{id}/dhcp/leases", async (
            string id,
            IOPNsenseClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, secret) = await factory.ResolveAsync(id, ct);
                var leases = await client.GetDhcpLeasesAsync(
                    config.BaseUrl,
                    config.ApiKey,
                    secret,
                    config.AllowSelfSignedCert,
                    ct);

                return Results.Ok(leases);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"OPNsense instance '{id}' not found." });
            }
        });

        // --- Firmware ---

        group.MapGet("/instances/{id}/firmware", async (
            string id,
            IOPNsenseClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, secret) = await factory.ResolveAsync(id, ct);
                var fw = await client.GetFirmwareStatusAsync(
                    config.BaseUrl,
                    config.ApiKey,
                    secret,
                    config.AllowSelfSignedCert,
                    ct);

                return Results.Ok(fw);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"OPNsense instance '{id}' not found." });
            }
        });

        return app;
    }
}
