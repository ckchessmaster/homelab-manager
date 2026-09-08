using System.Net;
using ControlPlane.Api.Storage;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlane.Api.Tests;

public class HealthProbeTests
{
    private class HealthTestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-health-{Guid.NewGuid():N}.db");
        private readonly string _environment;

        public HealthTestAppFactory(string environment = "Production")
        {
            _environment = environment;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("STANDBY_MODE", "true");
            builder.UseSetting("ControlPlane:ApiKey", "super-secret-key");
            builder.UseSetting("ConnectionStrings:PostgresDatabase", "");
            builder.UseEnvironment(_environment);

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
    public async Task HealthzEndpoint_InProduction_Returns200Ok_WithoutAuth()
    {
        using var factory = new HealthTestAppFactory("Production");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task HealthzEndpoint_InDevelopment_Returns200Ok()
    {
        using var factory = new HealthTestAppFactory("Development");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }
}
