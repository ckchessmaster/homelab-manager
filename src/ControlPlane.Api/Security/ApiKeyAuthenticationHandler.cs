using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Security;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiKeyAuthenticationHandler> _logger;

    private readonly IServiceScopeFactory _scopeFactory;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
        _logger = logger.CreateLogger<ApiKeyAuthenticationHandler>();
        _scopeFactory = scopeFactory;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var bypassAuth = _configuration.GetValue<bool>("AUTH_BYPASS", false) || Options.BypassAuth;
        if (bypassAuth)
        {
            _logger.LogDebug("AUTH_BYPASS is active. Granting DevAdmin credentials.");

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "DevAdmin"),
                new Claim(ClaimTypes.Name, "DevAdmin"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Role, "Operator"),
                new Claim(ClaimTypes.Role, "Viewer"),
                new Claim("auth_method", "bypass")
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }

        string? providedKey = null;

        var headerName = Options.HeaderName;
        if (Request.Headers.TryGetValue(headerName, out var customHeader) && !string.IsNullOrWhiteSpace(customHeader))
        {
            providedKey = customHeader.ToString().Trim();
        }
        else if (Request.Headers.TryGetValue("X-ControlPlane-Key", out var altHeader) && !string.IsNullOrWhiteSpace(altHeader))
        {
            providedKey = altHeader.ToString().Trim();
        }
        else if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var authStr = authHeader.ToString().Trim();
            if (authStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                providedKey = authStr.Substring("Bearer ".Length).Trim();
            }
        }

        if (string.IsNullOrEmpty(providedKey))
        {
            return AuthenticateResult.Fail($"Missing '{headerName}', 'X-ControlPlane-Key', or 'Authorization: Bearer' token.");
        }

        // 1. Verify against static Master ApiKey if configured
        var expectedApiKey = _configuration["ControlPlane:ApiKey"] ?? Options.ApiKey;
        if (!string.IsNullOrEmpty(expectedApiKey))
        {
            var providedBytes = Encoding.UTF8.GetBytes(providedKey);
            var expectedBytes = Encoding.UTF8.GetBytes(expectedApiKey);

            if (providedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
            {
                var apiClaims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "ApiKeyUser"),
                    new Claim(ClaimTypes.Name, "ApiKeyUser"),
                    new Claim(ClaimTypes.Role, "Admin"),
                    new Claim(ClaimTypes.Role, "Operator"),
                    new Claim(ClaimTypes.Role, "Viewer"),
                    new Claim("auth_method", "api_key")
                };

                var apiIdentity = new ClaimsIdentity(apiClaims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
                var apiPrincipal = new ClaimsPrincipal(apiIdentity);
                var apiTicket = new AuthenticationTicket(apiPrincipal, Scheme.Name);

                return AuthenticateResult.Success(apiTicket);
            }
        }

        // 2. Verify against database Personal Access Tokens
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var tokenService = scope.ServiceProvider.GetRequiredService<Features.Security.Tokens.ApiTokenService>();
            var pat = await tokenService.ValidateTokenAsync(providedKey);

            if (pat != null)
            {
                var roleList = new List<string> { pat.Role };
                if (pat.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                {
                    roleList.Add("Operator");
                    roleList.Add("Viewer");
                }
                else if (pat.Role.Equals("Operator", StringComparison.OrdinalIgnoreCase))
                {
                    roleList.Add("Viewer");
                }

                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, pat.Id.ToString()),
                    new(ClaimTypes.Name, pat.Name),
                    new("auth_method", "personal_access_token"),
                    new("token_id", pat.Id.ToString())
                };

                foreach (var r in roleList.Distinct())
                {
                    claims.Add(new Claim(ClaimTypes.Role, r));
                }

                var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
                var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

                _logger.LogDebug("Successfully authenticated Personal Access Token '{Name}' (Role: {Role})", pat.Name, pat.Role);
                return AuthenticateResult.Success(ticket);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error occurred validating personal access token.");
        }

        _logger.LogWarning("Invalid API key or token received from {RemoteIpAddress}.", Request.HttpContext.Connection.RemoteIpAddress);
        return AuthenticateResult.Fail("Invalid API key or Personal Access Token.");
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.Append("WWW-Authenticate", $"{Scheme.Name} realm=\"ControlPlane\"");

        if (!Response.HasStarted)
        {
            await Response.WriteAsJsonAsync(new
            {
                error = "Unauthorized",
                message = "Invalid or missing API key."
            });
        }
    }
}
