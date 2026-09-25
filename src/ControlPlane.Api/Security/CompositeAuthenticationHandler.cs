using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Security;

/// <summary>
/// Composite authentication handler that inspects incoming requests and delegates authentication to:
/// 1. Bearer JWT authentication (Zitadel OpenID Connect) if an Authorization: Bearer token or ?access_token= is present.
/// 2. API Key authentication (X-ControlPlane-Key) if an API key header is present.
/// 3. Offline Dev/Standby bypass if AUTH_BYPASS or Standby mode is active.
/// </summary>
public class CompositeAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CompositeAuthenticationHandler> _logger;

    public CompositeAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
        _logger = logger.CreateLogger<CompositeAuthenticationHandler>();
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 0. Skip on /agent-hub (handled directly by AgentHubMiddleware)
        if (Request.Path.StartsWithSegments("/agent-hub"))
        {
            return AuthenticateResult.NoResult();
        }

        // 1. Check for development bypass mode
        var bypassAuth = _configuration.GetValue<bool>("AUTH_BYPASS", false);
        if (bypassAuth)
        {
            _logger.LogDebug("AUTH_BYPASS is active. Delegating to ApiKey bypass handler.");
            return await Context.AuthenticateAsync(AuthConstants.ApiKeyScheme);
        }

        // 2. Check for Bearer token (Header or WebSocket query string)
        var authHeader = Request.Headers.Authorization.ToString();
        var hasBearerHeader = !string.IsNullOrWhiteSpace(authHeader) &&
                              authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        var hasAccessTokenQuery = Request.Query.ContainsKey("access_token") &&
                                  Request.Path.StartsWithSegments("/hubs");

        if (hasBearerHeader)
        {
            var bearerToken = authHeader.Substring("Bearer ".Length).Trim();
            if (bearerToken.StartsWith("cp_pat_", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Personal Access Token detected in Bearer header. Delegating to ApiKey scheme.");
                return await Context.AuthenticateAsync(AuthConstants.ApiKeyScheme);
            }
        }

        if (hasBearerHeader || hasAccessTokenQuery)
        {
            _logger.LogDebug("Bearer credentials detected. Delegating to JwtBearer scheme.");
            var jwtResult = await Context.AuthenticateAsync(AuthConstants.JwtBearerScheme);
            if (jwtResult.Succeeded)
            {
                return jwtResult;
            }

            // Fallback to ApiKey scheme in case the Bearer token was a PAT or API key
            if (hasBearerHeader)
            {
                var apiKeyResult = await Context.AuthenticateAsync(AuthConstants.ApiKeyScheme);
                if (apiKeyResult.Succeeded)
                {
                    return apiKeyResult;
                }
            }

            return jwtResult;
        }

        // 3. Check for API key header
        var headerName = ApiKeyAuthenticationOptions.DefaultHeaderName;
        if (Request.Headers.ContainsKey(headerName) || Request.Headers.ContainsKey("X-ControlPlane-Key"))
        {
            _logger.LogDebug("API Key header detected. Delegating to ApiKey scheme.");
            return await Context.AuthenticateAsync(AuthConstants.ApiKeyScheme);
        }

        // 4. No credentials presented
        return AuthenticateResult.NoResult();
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.Append("WWW-Authenticate", "ApiKey realm=\"ControlPlane\"");
        Response.Headers.Append("WWW-Authenticate", "Bearer error=\"invalid_token\", error_description=\"Zitadel access token required\"");

        if (!Response.HasStarted)
        {
            await Response.WriteAsJsonAsync(new
            {
                error = "Unauthorized",
                message = "Authentication required. Provide an Authorization Bearer token or X-ControlPlane-Key header."
            });
        }
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;

        if (!Response.HasStarted)
        {
            await Response.WriteAsJsonAsync(new
            {
                error = "Forbidden",
                message = "Access denied: insufficient role privileges for this resource."
            });
        }
    }
}
