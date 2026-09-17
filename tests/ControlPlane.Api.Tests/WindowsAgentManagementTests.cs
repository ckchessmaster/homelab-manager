using System.Net;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Storage;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlane.Api.Tests;

public class WindowsAgentManagementTests
{
    private class WindowsAgentAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-win-agents-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("STANDBY_MODE", "true");
            builder.UseSetting("ControlPlane:ApiKey", "dev-secret-key-123");
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
    public void AgentBinaryService_ResolvesWindowsAmd64Binary()
    {
        var service = new AgentBinaryService();
        var binaryPath = service.GetBinaryPath("windows-amd64");
        Assert.NotNull(binaryPath);
        Assert.EndsWith("controlplane-agent-windows-amd64.exe", binaryPath);

        var archs = service.GetAvailableArchitectures();
        Assert.Contains("windows-amd64", archs);
    }

    [Fact]
    public async Task AgentEndpoints_ServesPowerShellInstallScript_Anonymously()
    {
        using var factory = new WindowsAgentAppFactory();
        var client = factory.CreateClient();

        var installResp = await client.GetAsync("/api/v1/agents/install.ps1");
        Assert.Equal(HttpStatusCode.OK, installResp.StatusCode);

        var scriptContent = await installResp.Content.ReadAsStringAsync();
        Assert.Contains("ControlPlaneAgent", scriptContent);
        Assert.Contains("ControlPlane Compute Node Agent", scriptContent);

        var bootstrapResp = await client.GetAsync("/api/v1/agents/bootstrap.ps1");
        Assert.Equal(HttpStatusCode.OK, bootstrapResp.StatusCode);
    }

    [Fact]
    public async Task AgentEndpoints_ServesWindowsBinary_Anonymously()
    {
        using var factory = new WindowsAgentAppFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/agents/binaries/windows-amd64");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/octet-stream", resp.Content.Headers.ContentType?.MediaType);
    }
}
