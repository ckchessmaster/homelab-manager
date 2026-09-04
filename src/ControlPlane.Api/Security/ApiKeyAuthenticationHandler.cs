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

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
        _logger = logger.CreateLogger<ApiKeyAuthenticationHandler>();
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
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
                new Claim("auth_method", "bypass")
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        var headerName = Options.HeaderName;
        if (!Request.Headers.TryGetValue(headerName, out var providedKeyHeader) || string.IsNullOrWhiteSpace(providedKeyHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail($"Missing '{headerName}' header."));
        }

        var expectedApiKey = _configuration["ControlPlane:ApiKey"] ?? Options.ApiKey;
        if (string.IsNullOrEmpty(expectedApiKey))
        {
            _logger.LogError("ControlPlane:ApiKey is not configured. Rejecting request.");
            return Task.FromResult(AuthenticateResult.Fail("API key is not configured on the server."));
        }

        var providedKey = providedKeyHeader.ToString();
        var providedBytes = Encoding.UTF8.GetBytes(providedKey);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedApiKey);

        if (providedBytes.Length != expectedBytes.Length || !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            _logger.LogWarning("Invalid API key received from {RemoteIpAddress}.", Request.HttpContext.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var apiClaims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "ApiKeyUser"),
            new Claim(ClaimTypes.Name, "ApiKeyUser"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.Role, "Operator"),
            new Claim("auth_method", "api_key")
        };

        var apiIdentity = new ClaimsIdentity(apiClaims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        var apiPrincipal = new ClaimsPrincipal(apiIdentity);
        var apiTicket = new AuthenticationTicket(apiPrincipal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(apiTicket));
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
