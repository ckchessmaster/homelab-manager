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
            options.BypassAuth = configuration.GetValue<bool>("AUTH_BYPASS", false) || configuration.GetValue<bool>("DEMO_MODE", false);
        });

        services.Configure<OidcJwtOptions>(configuration.GetSection(OidcJwtOptions.SectionName));
        services.AddTransient<IClaimsTransformation, OidcRoleClaimsTransformation>();
        services.AddScoped<Features.Security.Tokens.ApiTokenService>();

        var oidcOptions = configuration.GetSection(OidcJwtOptions.SectionName).Get<OidcJwtOptions>() ?? new OidcJwtOptions();

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
                options.BypassAuth = configuration.GetValue<bool>("AUTH_BYPASS", false) || configuration.GetValue<bool>("DEMO_MODE", false);
            })
        .AddJwtBearer(AuthConstants.JwtBearerScheme, options =>
        {
            if (!string.IsNullOrWhiteSpace(oidcOptions.Authority))
            {
                options.Authority = oidcOptions.Authority;
                options.RequireHttpsMetadata = oidcOptions.RequireHttpsMetadata;
                options.Audience = oidcOptions.Audience;
                if (!string.IsNullOrWhiteSpace(oidcOptions.MetadataAddress))
                {
                    options.MetadataAddress = oidcOptions.MetadataAddress;
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = oidcOptions.ValidIssuer ?? oidcOptions.Authority,
                    ValidateAudience = !string.IsNullOrWhiteSpace(oidcOptions.Audience) && oidcOptions.Audience != "*",
                    ValidAudience = oidcOptions.Audience != "*" ? oidcOptions.Audience : null,
                    ValidAudiences = oidcOptions.ValidAudiences,
                    ValidateLifetime = true,
                    AudienceValidator = (audiences, _, _) =>
                    {
                        if (string.IsNullOrWhiteSpace(oidcOptions.Audience) || oidcOptions.Audience == "*")
                            return true;

                        if (audiences == null || !audiences.Any())
                            return false;

                        var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { oidcOptions.Audience };
                        if (!string.IsNullOrWhiteSpace(oidcOptions.ClientId))
                            accepted.Add(oidcOptions.ClientId);
                        if (oidcOptions.ValidAudiences != null)
                        {
                            foreach (var a in oidcOptions.ValidAudiences)
                                accepted.Add(a);
                        }

                        return audiences.Any(aud => accepted.Contains(aud) || aud.Contains("controlplane", StringComparison.OrdinalIgnoreCase));
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
