namespace ControlPlane.Api.Features.Adapters.UniFi;

public static class UniFiEndpoints
{
    public static IEndpointRouteBuilder MapUniFiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/adapters/unifi")
            .RequireAuthorization();

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
            var clients = await unifiClient.GetActiveClientsAsync(controllerUrl, username, password, site, ct);
            return Results.Ok(clients);
        });

        return app;
    }
}
