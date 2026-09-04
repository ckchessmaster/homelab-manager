namespace ControlPlane.Api.Features.Adapters.Redfish;

public static class RedfishEndpoints
{
    public static IEndpointRouteBuilder MapRedfishEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/adapters/redfish")
            .RequireAuthorization();

        group.MapGet("/system-info", async (
            string idracIp,
            string username,
            string password,
            IRedfishClient redfishClient,
            CancellationToken ct) =>
        {
            var info = await redfishClient.GetSystemInfoAsync(idracIp, username, password, insecureTls: true, ct);
            return Results.Ok(info);
        });

        group.MapGet("/power-state", async (
            string idracIp,
            string username,
            string password,
            IRedfishClient redfishClient,
            CancellationToken ct) =>
        {
            var info = await redfishClient.GetSystemInfoAsync(idracIp, username, password, insecureTls: true, ct);
            return Results.Ok(new
            {
                idracIp,
                powerState = info.PowerState,
                status = info.HealthStatus
            });
        });

        group.MapGet("/thermal", async (
            string idracIp,
            string username,
            string password,
            IRedfishClient redfishClient,
            CancellationToken ct) =>
        {
            var vitals = await redfishClient.GetThermalVitalsAsync(idracIp, username, password, insecureTls: true, ct);
            return Results.Ok(vitals);
        });

        group.MapPost("/power-action", async (
            RedfishPowerActionRequest request,
            IRedfishClient redfishClient,
            CancellationToken ct) =>
        {
            var result = await redfishClient.ResetSystemAsync(
                request.IdracIp,
                request.Username,
                request.Password,
                request.ResetType,
                request.InsecureTls,
                ct);

            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        return app;
    }
}
