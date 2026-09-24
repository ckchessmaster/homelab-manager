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

public class ZitadelAuthAndRbacTests
{
    private const string TestSigningKey = "ThisIsASecretKeyForTestingTokens123456!";

    private static string GenerateJwt(string username, string[]? zitadelRoles = null, string? zitadelRolesJson = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(TestSigningKey);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, username),
            new(ClaimTypes.Name, username),
            new(JwtRegisteredClaimNames.Sub, username),
            new(JwtRegisteredClaimNames.Iss, "https://issuer.example.com"),
            new(JwtRegisteredClaimNames.Aud, "controlplane")
        };

        if (zitadelRolesJson != null)
        {
            claims.Add(new Claim(AuthConstants.ZitadelRolesClaimType, zitadelRolesJson));
        }
        else if (zitadelRoles != null)
        {
            var json = "[\"" + string.Join("\",\"", zitadelRoles) + "\"]";
            claims.Add(new Claim(AuthConstants.ZitadelRolesClaimType, json));
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
    public async Task RoleClaimsTransformation_ExtractsRoles_FromZitadelJsonObject()
    {
        var transformation = new ZitadelRoleClaimsTransformation(NullLogger<ZitadelRoleClaimsTransformation>.Instance);
        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim(AuthConstants.ZitadelRolesClaimType, "{\"admin\": {\"123\": \"org\"}, \"operator\": {\"123\": \"org\"}}"));
        var principal = new ClaimsPrincipal(identity);

        var result = await transformation.TransformAsync(principal);

        var roles = result.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Contains(AuthConstants.RoleAdmin, roles);
        Assert.Contains(AuthConstants.RoleOperator, roles);
        Assert.Contains(AuthConstants.RoleViewer, roles); // Admin inherits Viewer
    }

    [Fact]
    public async Task RoleClaimsTransformation_ExtractsRoles_FromZitadelJsonArray()
    {
        var transformation = new ZitadelRoleClaimsTransformation(NullLogger<ZitadelRoleClaimsTransformation>.Instance);
        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim(AuthConstants.ZitadelRolesClaimType, "[\"operator\"]"));
        var principal = new ClaimsPrincipal(identity);

        var result = await transformation.TransformAsync(principal);

        var roles = result.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Contains(AuthConstants.RoleOperator, roles);
        Assert.Contains(AuthConstants.RoleViewer, roles); // Operator inherits Viewer
        Assert.DoesNotContain(AuthConstants.RoleAdmin, roles);
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
        var token = GenerateJwt("viewer-user", new[] { "viewer" });
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
        var token = GenerateJwt("operator-user", new[] { "operator" });
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
        var token = GenerateJwt("admin-user", new[] { "admin" });
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
    public async Task GetAuthConfig_ReturnsZitadelConfigurationAndCustomRoleMappings()
    {
        using var factory = new RbacTestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(json.TryGetProperty("zitadel", out var zitadel));
        Assert.True(zitadel.TryGetProperty("roles", out var roles));
        Assert.True(roles.TryGetProperty("admin", out var adminRoles));
        Assert.Contains("admin", adminRoles.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task CustomRoleMapping_AllowsCustomRoleNames()
    {
        var options = new ZitadelJwtOptions
        {
            Roles = new ZitadelRoleMappingOptions
            {
                Admin = ["homelab-admins", "superadmin"],
                Operator = ["devops-team"],
                Viewer = ["family-members"]
            }
        };

        var transform = new ZitadelRoleClaimsTransformation(
            NullLogger<ZitadelRoleClaimsTransformation>.Instance,
            Microsoft.Extensions.Options.Options.Create(options)
        );

        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim(AuthConstants.ZitadelRolesClaimType, "[\"homelab-admins\"]"));
        var principal = new ClaimsPrincipal(identity);

        var transformed = await transform.TransformAsync(principal);
        var roleClaims = transformed.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        Assert.Contains(AuthConstants.RoleAdmin, roleClaims);
        Assert.Contains(AuthConstants.RoleOperator, roleClaims); // Admin hierarchy implies Operator
        Assert.Contains(AuthConstants.RoleViewer, roleClaims);   // Admin hierarchy implies Viewer
    }
}
