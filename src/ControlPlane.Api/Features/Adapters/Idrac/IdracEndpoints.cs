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

            if (string.Equals(request.ConnectionMode, "agent", StringComparison.OrdinalIgnoreCase))
            {
                if (!request.HostId.HasValue)
                {
                    return Results.BadRequest(new { message = "Baremetal host selection is required when in agent mode." });
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.BmcUrl))
                {
                    return Results.BadRequest(new { message = "BMC URL is required." });
                }
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
            if (string.Equals(request.ConnectionMode, "agent", StringComparison.OrdinalIgnoreCase))
            {
                if (!request.HostId.HasValue)
                {
                    return Results.BadRequest(new { message = "Baremetal host selection is required for testing in agent mode." });
                }

                var result = await client.TestAgentConnectionAsync(request.HostId.Value, ct);
                return Results.Ok(result);
            }

            if (string.IsNullOrWhiteSpace(request.BmcUrl))
            {
                return Results.BadRequest(new { message = "BMC URL is required for testing." });
            }

            var res = await client.TestConnectionAsync(
                request.BmcUrl,
                request.Username ?? string.Empty,
                request.Password ?? string.Empty,
                request.AllowSelfSignedCert,
                ct);

            return Results.Ok(res);
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
                var result = await client.TestInstanceAsync(config, password, ct);
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
                var vitals = await client.GetInstanceVitalsAsync(config, password, ct);
                return Results.Ok(vitals);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = $"iDRAC instance '{id}' not found." });
            }
        });

        // --- Host Agent IPMI Auto-Provisioning ---

        group.MapPost("/hosts/{hostId:guid}/install-ipmitool", async (
            Guid hostId,
            AgentIpmiExecutor ipmiExecutor,
            CancellationToken ct) =>
        {
            var (success, message) = await ipmiExecutor.InstallIpmiToolAsync(hostId, ct);
            return success
                ? Results.Ok(new { success = true, message })
                : Results.BadRequest(new { success = false, message });
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
                var response = await client.ResetInstanceSystemAsync(
                    config,
                    password,
                    request.ResetType,
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

        // Power action directly by IP or Host (used from Host inventory view)
        group.MapPost("/power-action-by-ip", async (
            IdracPowerActionByIpRequest request,
            IIdracClientFactory factory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ResetType))
            {
                return Results.BadRequest(new { message = "ResetType is required." });
            }

            // 1. If HostId supplied directly or IdracIp is a Guid
            Guid? targetHostId = request.HostId;
            if (!targetHostId.HasValue && !string.IsNullOrWhiteSpace(request.IdracIp) && Guid.TryParse(request.IdracIp, out var parsedGuid))
            {
                targetHostId = parsedGuid;
            }

            if (targetHostId.HasValue)
            {
                var resolvedByHost = await factory.ResolveByHostIdAsync(targetHostId.Value, ct);
                if (resolvedByHost != null)
                {
                    var hostResult = await resolvedByHost.Value.Client.ResetInstanceSystemAsync(
                        resolvedByHost.Value.Config,
                        resolvedByHost.Value.Password,
                        request.ResetType,
                        ct);

                    return hostResult.Success ? Results.Ok(hostResult) : Results.BadRequest(hostResult);
                }
            }

            if (string.IsNullOrWhiteSpace(request.IdracIp))
            {
                return Results.BadRequest(new { message = "IdracIp or HostId is required." });
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

            if (string.IsNullOrWhiteSpace(username) && !bmcUrl.StartsWith("agent://", StringComparison.OrdinalIgnoreCase))
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

        // --- Fan Control ---

        group.MapPost("/instances/{id}/fan-control", async (
            string id,
            BmcFanControlRequest request,
            IIdracClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, password) = await factory.ResolveAsync(id, ct);
                var response = await client.SetInstanceFanControlAsync(
                    config,
                    password,
                    request.Mode,
                    request.Percentage,
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

        group.MapPost("/fan-control-by-ip", async (
            BmcFanControlByIpRequest request,
            IIdracClientFactory factory,
            CancellationToken ct) =>
        {
            Guid? targetHostId = request.HostId;
            if (!targetHostId.HasValue && !string.IsNullOrWhiteSpace(request.IdracIp) && Guid.TryParse(request.IdracIp, out var parsedGuid))
            {
                targetHostId = parsedGuid;
            }

            if (targetHostId.HasValue)
            {
                var resolvedByHost = await factory.ResolveByHostIdAsync(targetHostId.Value, ct);
                if (resolvedByHost != null)
                {
                    var hostResult = await resolvedByHost.Value.Client.SetInstanceFanControlAsync(
                        resolvedByHost.Value.Config,
                        resolvedByHost.Value.Password,
                        request.Mode,
                        request.Percentage,
                        ct);
                    return hostResult.Success ? Results.Ok(hostResult) : Results.BadRequest(hostResult);
                }
            }

            if (string.IsNullOrWhiteSpace(request.IdracIp))
            {
                return Results.BadRequest(new { message = "IdracIp or HostId is required." });
            }

            string username = request.Username ?? string.Empty;
            string password = request.Password ?? string.Empty;
            string bmcUrl = request.IdracIp;
            bool insecureTls = request.InsecureTls;

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

            if (string.IsNullOrWhiteSpace(username) && !bmcUrl.StartsWith("agent://", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { message = $"No configured credentials found for BMC IP '{request.IdracIp}'." });
            }

            var result = await factory.GetClient().SetFanControlAsync(
                bmcUrl,
                username,
                password,
                request.Mode,
                request.Percentage,
                insecureTls,
                ct);

            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        // --- Chassis Identify (Locator LED / UID) ---

        group.MapPost("/instances/{id}/identify", async (
            string id,
            BmcChassisIdentifyRequest request,
            IIdracClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, password) = await factory.ResolveAsync(id, ct);
                var response = await client.SetInstanceChassisIdentifyAsync(
                    config,
                    password,
                    request.State,
                    request.DurationSeconds,
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

        group.MapPost("/identify-by-ip", async (
            BmcChassisIdentifyByIpRequest request,
            IIdracClientFactory factory,
            CancellationToken ct) =>
        {
            Guid? targetHostId = request.HostId;
            if (!targetHostId.HasValue && !string.IsNullOrWhiteSpace(request.IdracIp) && Guid.TryParse(request.IdracIp, out var parsedGuid))
            {
                targetHostId = parsedGuid;
            }

            if (targetHostId.HasValue)
            {
                var resolvedByHost = await factory.ResolveByHostIdAsync(targetHostId.Value, ct);
                if (resolvedByHost != null)
                {
                    var hostResult = await resolvedByHost.Value.Client.SetInstanceChassisIdentifyAsync(
                        resolvedByHost.Value.Config,
                        resolvedByHost.Value.Password,
                        request.State,
                        request.DurationSeconds,
                        ct);
                    return hostResult.Success ? Results.Ok(hostResult) : Results.BadRequest(hostResult);
                }
            }

            if (string.IsNullOrWhiteSpace(request.IdracIp))
            {
                return Results.BadRequest(new { message = "IdracIp or HostId is required." });
            }

            string username = request.Username ?? string.Empty;
            string password = request.Password ?? string.Empty;
            string bmcUrl = request.IdracIp;
            bool insecureTls = request.InsecureTls;

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

            if (string.IsNullOrWhiteSpace(username) && !bmcUrl.StartsWith("agent://", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { message = $"No configured credentials found for BMC IP '{request.IdracIp}'." });
            }

            var result = await factory.GetClient().SetChassisIdentifyAsync(
                bmcUrl,
                username,
                password,
                request.State,
                request.DurationSeconds,
                insecureTls,
                ct);

            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        // --- One-Time Boot Device Override ---

        group.MapPost("/instances/{id}/boot-override", async (
            string id,
            BmcBootOverrideRequest request,
            IIdracClientFactory factory,
            CancellationToken ct) =>
        {
            try
            {
                var (client, config, password) = await factory.ResolveAsync(id, ct);
                var response = await client.SetInstanceBootOverrideAsync(
                    config,
                    password,
                    request.Target,
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

        group.MapPost("/boot-override-by-ip", async (
            BmcBootOverrideByIpRequest request,
            IIdracClientFactory factory,
            CancellationToken ct) =>
        {
            Guid? targetHostId = request.HostId;
            if (!targetHostId.HasValue && !string.IsNullOrWhiteSpace(request.IdracIp) && Guid.TryParse(request.IdracIp, out var parsedGuid))
            {
                targetHostId = parsedGuid;
            }

            if (targetHostId.HasValue)
            {
                var resolvedByHost = await factory.ResolveByHostIdAsync(targetHostId.Value, ct);
                if (resolvedByHost != null)
                {
                    var hostResult = await resolvedByHost.Value.Client.SetInstanceBootOverrideAsync(
                        resolvedByHost.Value.Config,
                        resolvedByHost.Value.Password,
                        request.Target,
                        ct);
                    return hostResult.Success ? Results.Ok(hostResult) : Results.BadRequest(hostResult);
                }
            }

            if (string.IsNullOrWhiteSpace(request.IdracIp))
            {
                return Results.BadRequest(new { message = "IdracIp or HostId is required." });
            }

            string username = request.Username ?? string.Empty;
            string password = request.Password ?? string.Empty;
            string bmcUrl = request.IdracIp;
            bool insecureTls = request.InsecureTls;

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

            if (string.IsNullOrWhiteSpace(username) && !bmcUrl.StartsWith("agent://", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { message = $"No configured credentials found for BMC IP '{request.IdracIp}'." });
            }

            var result = await factory.GetClient().SetBootOverrideAsync(
                bmcUrl,
                username,
                password,
                request.Target,
                insecureTls,
                ct);

            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        return app;
    }
}
