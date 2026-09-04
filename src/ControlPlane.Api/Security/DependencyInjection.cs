using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace ControlPlane.Api.Security;

public static class DependencyInjection
{
    public static IServiceCollection AddControlPlaneSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiKeyAuthenticationOptions>(options =>
        {
            options.ApiKey = configuration["ControlPlane:ApiKey"];
            options.BypassAuth = configuration.GetValue<bool>("AUTH_BYPASS", false);
        });

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = ApiKeyAuthenticationOptions.DefaultScheme;
            options.DefaultChallengeScheme = ApiKeyAuthenticationOptions.DefaultScheme;
        })
        .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationOptions.DefaultScheme,
            options =>
            {
                options.ApiKey = configuration["ControlPlane:ApiKey"];
                options.BypassAuth = configuration.GetValue<bool>("AUTH_BYPASS", false);
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
            options.AddPolicy("AdminPolicy", policy => policy.RequireRole("Admin"));
            options.AddPolicy("RequireOperator", policy => policy.RequireRole("Admin", "Operator"));
            options.AddPolicy("OperatorPolicy", policy => policy.RequireRole("Admin", "Operator"));
        });

        return services;
    }

    public static IApplicationBuilder UseControlPlaneSecurity(this IApplicationBuilder app)
    {
        app.UseSecurityHeaders();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
