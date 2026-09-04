using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Api.Tests;

public class ApiKeyAuthenticationTests
{
    private class TestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-auth-{Guid.NewGuid():N}.db");
        private readonly Dictionary<string, string?> _configOverrides;

        public TestAppFactory(Dictionary<string, string?>? configOverrides = null)
        {
            _configOverrides = configOverrides ?? new Dictionary<string, string?>();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("STANDBY_MODE", "true");
            builder.UseSetting("ControlPlane:ApiKey", "dev-secret-key-123");
            builder.UseSetting("ConnectionStrings:PostgresDatabase", "");
            builder.UseEnvironment("Development");

            foreach (var (k, v) in _configOverrides)
            {
                builder.UseSetting(k, v);
            }

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
    public async Task GetProtectedEndpoint_WithoutKey_Returns401Unauthorized()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"));
        var authHeader = response.Headers.GetValues("WWW-Authenticate").FirstOrDefault();
        Assert.Contains("ApiKey", authHeader);
    }

    [Fact]
    public async Task GetProtectedEndpoint_WithInvalidKey_Returns401Unauthorized()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "wrong-secret-key");

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProtectedEndpoint_WithValidKey_Returns200OkAndHasAdminClaims()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal("ApiKeyUser", body.GetProperty("name").GetString());

        var roles = body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains("Admin", roles);
        Assert.Contains("Operator", roles);
    }

    [Fact]
    public async Task GetAdminEndpoint_WithValidKey_SucceedsWithAdminPolicy()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");

        var response = await client.GetAsync("/api/v1/admin/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pong", body.GetProperty("message").GetString());
        Assert.Equal("Admin", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task GetProtectedEndpoint_WithAuthBypassTrue_Returns200OkAsDevAdmin()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?>
        {
            ["AUTH_BYPASS"] = "true"
        });
        var client = factory.CreateClient();

        // No X-ControlPlane-Key header provided
        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal("DevAdmin", body.GetProperty("name").GetString());

        var roles = body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains("Admin", roles);
        Assert.Contains("Operator", roles);
    }

    [Fact]
    public async Task SecurityHeadersMiddleware_AddsExpectedSecurityHeaders()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").FirstOrDefault());
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").FirstOrDefault());
        Assert.True(response.Headers.Contains("X-XSS-Protection"));
        Assert.Equal("1; mode=block", response.Headers.GetValues("X-XSS-Protection").FirstOrDefault());
        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").FirstOrDefault());
    }

    [Fact]
    public async Task OpenApiEndpoint_ReturnsDocumentWithSecurityScheme()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("X-ControlPlane-Key", json);
        Assert.Contains("ApiKey", json);
    }
}
