using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

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

        services.Configure<ZitadelJwtOptions>(configuration.GetSection(ZitadelJwtOptions.SectionName));
        services.AddTransient<IClaimsTransformation, ZitadelRoleClaimsTransformation>();
        services.AddScoped<Features.Security.Tokens.ApiTokenService>();

        var zitadelOptions = configuration.GetSection(ZitadelJwtOptions.SectionName).Get<ZitadelJwtOptions>() ?? new ZitadelJwtOptions();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = AuthConstants.CompositeScheme;
            options.DefaultChallengeScheme = AuthConstants.CompositeScheme;
        })
        .AddScheme<AuthenticationSchemeOptions, CompositeAuthenticationHandler>(
            AuthConstants.CompositeScheme,
            _ => { })
        .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            AuthConstants.ApiKeyScheme,
            options =>
            {
                options.ApiKey = configuration["ControlPlane:ApiKey"];
                options.BypassAuth = configuration.GetValue<bool>("AUTH_BYPASS", false);
            })
        .AddJwtBearer(AuthConstants.JwtBearerScheme, options =>
        {
            if (!string.IsNullOrWhiteSpace(zitadelOptions.Authority))
            {
                options.Authority = zitadelOptions.Authority;
                options.RequireHttpsMetadata = zitadelOptions.RequireHttpsMetadata;
                options.Audience = zitadelOptions.Audience;
                if (!string.IsNullOrWhiteSpace(zitadelOptions.MetadataAddress))
                {
                    options.MetadataAddress = zitadelOptions.MetadataAddress;
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = zitadelOptions.ValidIssuer ?? zitadelOptions.Authority,
                    ValidateAudience = !string.IsNullOrWhiteSpace(zitadelOptions.Audience) && zitadelOptions.Audience != "*",
                    ValidAudience = zitadelOptions.Audience != "*" ? zitadelOptions.Audience : null,
                    ValidAudiences = zitadelOptions.ValidAudiences,
                    ValidateLifetime = true,
                    AudienceValidator = (audiences, _, _) =>
                    {
                        if (string.IsNullOrWhiteSpace(zitadelOptions.Audience) || zitadelOptions.Audience == "*")
                            return true;

                        if (audiences == null || !audiences.Any())
                            return false;

                        return audiences.Any(aud =>
                            string.Equals(aud, zitadelOptions.Audience, StringComparison.OrdinalIgnoreCase) ||
                            (zitadelOptions.ValidAudiences?.Any(v => string.Equals(v, aud, StringComparison.OrdinalIgnoreCase)) ?? false) ||
                            aud.Contains("controlplane", StringComparison.OrdinalIgnoreCase)
                        );
                    }
                };
            }
            else
            {
                options.RequireHttpsMetadata = false;
            }

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthConstants.RequireAdmin, policy => policy.RequireRole(AuthConstants.RoleAdmin));
            options.AddPolicy(AuthConstants.AdminPolicy, policy => policy.RequireRole(AuthConstants.RoleAdmin));
            options.AddPolicy(AuthConstants.RequireOperator, policy => policy.RequireRole(AuthConstants.RoleAdmin, AuthConstants.RoleOperator));
            options.AddPolicy(AuthConstants.OperatorPolicy, policy => policy.RequireRole(AuthConstants.RoleAdmin, AuthConstants.RoleOperator));
            options.AddPolicy(AuthConstants.RequireViewer, policy => policy.RequireRole(AuthConstants.RoleAdmin, AuthConstants.RoleOperator, AuthConstants.RoleViewer));
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
