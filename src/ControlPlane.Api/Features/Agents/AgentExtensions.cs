namespace ControlPlane.Api.Features.Agents;

public static class AgentExtensions
{
    public static IServiceCollection AddAgentHubServices(this IServiceCollection services)
    {
        services.AddSingleton<AgentConnectionManager>();
        services.AddSingleton<AgentHeartbeatHandler>();
        services.AddHostedService<AgentLivenessBackgroundService>();
        return services;
    }

    public static IApplicationBuilder UseAgentHub(this IApplicationBuilder app)
    {
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(15)
        });
        app.UseMiddleware<AgentHubMiddleware>();
        return app;
    }
}
