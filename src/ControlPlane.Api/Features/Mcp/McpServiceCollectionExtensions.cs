using ModelContextProtocol.Server;

namespace ControlPlane.Api.Features.Mcp;

public static class McpServiceCollectionExtensions
{
    public const string EnableMcpServerKey = "ENABLE_MCP_SERVER";

    public static bool IsMcpServerEnabled(IConfiguration configuration)
    {
        var configVal = configuration[EnableMcpServerKey];
        if (!string.IsNullOrWhiteSpace(configVal) && bool.TryParse(configVal, out var parsed))
        {
            return parsed;
        }

        var envVal = Environment.GetEnvironmentVariable(EnableMcpServerKey);
        if (!string.IsNullOrWhiteSpace(envVal) && bool.TryParse(envVal, out var envParsed))
        {
            return envParsed;
        }

        return false;
    }

    public static IServiceCollection AddControlPlaneMcpServer(this IServiceCollection services, IConfiguration configuration)
    {
        if (!IsMcpServerEnabled(configuration))
        {
            return services;
        }

        services.AddScoped<ControlPlaneMcpTools>();

        services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "ControlPlane.McpServer",
                Version = "1.0.0"
            };
        })
        .WithHttpTransport()
        .WithTools<ControlPlaneMcpTools>();

        return services;
    }

    public static IEndpointRouteBuilder MapControlPlaneMcp(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        if (!IsMcpServerEnabled(configuration))
        {
            return endpoints;
        }

        endpoints.MapMcp("/mcp");

        return endpoints;
    }
}
