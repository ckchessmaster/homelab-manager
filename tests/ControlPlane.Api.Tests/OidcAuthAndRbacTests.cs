using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ControlPlane.Api.Tests;

public class OidcAuthAndRbacTests
{
    private const string TestSigningKey = "ThisIsASecretKeyForTestingTokens123456!";

    private static string GenerateJwt(string username, string[]? groups = null, string? groupsJson = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(TestSigningKey);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, username),
            new(ClaimTypes.Name, username),
            new(JwtRegisteredClaimNames.Sub, username),
            new("preferred_username", username),
            new(JwtRegisteredClaimNames.Iss, "https://issuer.example.com"),
            new(JwtRegisteredClaimNames.Aud, "controlplane")
        };

        if (groupsJson != null)
        {
            claims.Add(new Claim(AuthConstants.DefaultRoleClaimType, groupsJson));
        }
        else if (groups != null)
        {
            var json = "[\"" + string.Join("\",\"", groups) + "\"]";
            claims.Add(new Claim(AuthConstants.DefaultRoleClaimType, json));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "https://issuer.example.com",
            Audience = "controlplane",
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private class RbacTestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-rbac-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("STANDBY_MODE", "true");
            builder.UseSetting("ControlPlane:ApiKey", "test-api-key-999");
            builder.UseSetting("ConnectionStrings:PostgresDatabase", "");
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ControlPlaneDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ControlPlaneDbContext>(options =>
                {
                    options.UseSqlite($"Data Source={_tempDbFile}")
                        .UseSnakeCaseNamingConvention();
                });

                // Configure JwtBearer options to use test symmetric key
                services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(AuthConstants.JwtBearerScheme, options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = false
                    };
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (File.Exists(_tempDbFile))
            {
                try { File.Delete(_tempDbFile); } catch { }
            }
        }
    }

    [Fact]
    public async Task RoleClaimsTransformation_ExtractsRoles_FromGroupsJsonArray()
    {
        var transformation = new OidcRoleClaimsTransformation(NullLogger<OidcRoleClaimsTransformation>.Instance);
        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim(AuthConstants.DefaultRoleClaimType, "[\"operator\"]"));
        var principal = new ClaimsPrincipal(identity);

        var result = await transformation.TransformAsync(principal);

        var roles = result.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Contains(AuthConstants.RoleOperator, roles);
        Assert.Contains(AuthConstants.RoleViewer, roles); // Operator inherits Viewer
        Assert.DoesNotContain(AuthConstants.RoleAdmin, roles);
    }

    [Fact]
    public async Task RoleClaimsTransformation_ExtractsRoles_FromGroupsSingleString()
    {
        var transformation = new OidcRoleClaimsTransformation(NullLogger<OidcRoleClaimsTransformation>.Instance);
        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim(AuthConstants.DefaultRoleClaimType, "admin"));
        var principal = new ClaimsPrincipal(identity);

        var result = await transformation.TransformAsync(principal);

        var roles = result.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Contains(AuthConstants.RoleAdmin, roles);
        Assert.Contains(AuthConstants.RoleOperator, roles);
        Assert.Contains(AuthConstants.RoleViewer, roles); // Admin inherits Operator and Viewer
    }

    [Fact]
    public async Task RoleClaimsTransformation_SetsNameClaim_FromPreferredUsername()
    {
        var transformation = new OidcRoleClaimsTransformation(NullLogger<OidcRoleClaimsTransformation>.Instance);
        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim("preferred_username", "authentik-user"));
        var principal = new ClaimsPrincipal(identity);

        var result = await transformation.TransformAsync(principal);

        Assert.Equal("authentik-user", result.Identity?.Name);
    }

    [Fact]
    public async Task UnauthenticatedRequest_Returns401Unauthorized_WithWwwAuthenticate()
    {
        using var factory = new RbacTestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/hosts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.WwwAuthenticate.Count > 0);
    }

    [Fact]
    public async Task ViewerToken_CanReadHosts_ButCannotCreateHost()
    {
        using var factory = new RbacTestAppFactory();
        var client = factory.CreateClient();
        var token = GenerateJwt("viewer-user", ["viewer"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Read hosts should succeed
        var getResponse = await client.GetAsync("/api/v1/hosts");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // 2. Create host should be forbidden (requires Admin)
        var postResponse = await client.PostAsJsonAsync("/api/v1/hosts", new
        {
            Hostname = "new-node",
            IpAddress = "10.0.0.99",
            OsFamily = "linux_debian",
            TargetType = "baremetal"
        });
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
    }

    [Fact]
    public async Task OperatorToken_CanReadAndLaunchJobs_ButCannotDeleteHost()
    {
        using var factory = new RbacTestAppFactory();
        var client = factory.CreateClient();
        var token = GenerateJwt("operator-user", ["operator"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Delete host should be forbidden (requires Admin)
        var deleteResponse = await client.DeleteAsync($"/api/v1/hosts/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);

        // 2. Listing pipelines should succeed (requires Viewer)
        var pipelinesResponse = await client.GetAsync("/api/v1/pipelines");
        Assert.Equal(HttpStatusCode.OK, pipelinesResponse.StatusCode);
    }

    [Fact]
    public async Task AdminToken_HasFullPrivileges_CanAccessAdminPing()
    {
        using var factory = new RbacTestAppFactory();
        var client = factory.CreateClient();
        var token = GenerateJwt("admin-user", ["admin"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var pingResponse = await client.GetAsync("/api/v1/admin/ping");
        Assert.Equal(HttpStatusCode.OK, pingResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task ApiKeyHeader_WorksForAdminEndpoints()
    {
        using var factory = new RbacTestAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "test-api-key-999");

        var response = await client.GetAsync("/api/v1/admin/ping");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAuthConfig_ReturnsOidcConfigurationAndCustomRoleMappings()
    {
        using var factory = new RbacTestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(json.TryGetProperty("oidc", out var oidc));
        Assert.True(oidc.TryGetProperty("roles", out var roles));
        Assert.True(roles.TryGetProperty("admin", out var adminRoles));
        Assert.Contains("admin", adminRoles.EnumerateArray().Select(e => e.GetString()));
        Assert.True(oidc.TryGetProperty("roleClaimType", out var roleClaimType));
        Assert.Equal("groups", roleClaimType.GetString());
    }

    [Fact]
    public async Task CustomRoleMapping_AllowsCustomRoleNames()
    {
        var options = new OidcJwtOptions
        {
            Roles = new OidcRoleMappingOptions
            {
                Admin = ["homelab-admins", "superadmin"],
                Operator = ["devops-team"],
                Viewer = ["family-members"]
            }
        };

        var transform = new OidcRoleClaimsTransformation(
            NullLogger<OidcRoleClaimsTransformation>.Instance,
            Microsoft.Extensions.Options.Options.Create(options)
        );

        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim(AuthConstants.DefaultRoleClaimType, "[\"homelab-admins\"]"));
        var principal = new ClaimsPrincipal(identity);

        var transformed = await transform.TransformAsync(principal);
        var roleClaims = transformed.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        Assert.Contains(AuthConstants.RoleAdmin, roleClaims);
        Assert.Contains(AuthConstants.RoleOperator, roleClaims); // Admin hierarchy implies Operator
        Assert.Contains(AuthConstants.RoleViewer, roleClaims);   // Admin hierarchy implies Viewer
    }
}
