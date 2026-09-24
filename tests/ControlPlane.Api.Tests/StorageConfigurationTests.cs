using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlane.Api.Tests;

public class StorageConfigurationTests
{
    [Fact]
    public void ExplicitDatabaseHost_OverridesStaleDefaultConnectionString()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["ConnectionStrings:ControlPlaneDatabase"] = "Host=controlplane-postgres;Port=5432;Database=controlplane;Username=controlplane;Password=controlplane_prod_secret",
            ["Database__Host"] = "postgres-rw.cnpg-services.svc.cluster.local",
            ["Database__Database"] = "homelab_manager",
            ["Database__Username"] = "homelab_manager",
            ["Database__Port"] = "5432",
            ["password"] = "cnpg_secret_pass"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemory)
            .Build();

        var services = new ServiceCollection();
        services.AddControlPlaneStorage(config);

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var connectionString = context.Database.GetConnectionString();
        Assert.NotNull(connectionString);

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("postgres-rw.cnpg-services.svc.cluster.local", builder.Host);
        Assert.Equal("homelab_manager", builder.Database);
        Assert.Equal("homelab_manager", builder.Username);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("cnpg_secret_pass", builder.Password);
    }

    [Fact]
    public void StandbyMode_ConfiguresSqlite()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"standby-{Guid.NewGuid()}.db");
        try
        {
            var inMemory = new Dictionary<string, string?>
            {
                ["STANDBY_MODE"] = "true",
                ["STANDBY_DB_PATH"] = tempDb
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemory)
                .Build();

            var services = new ServiceCollection();
            services.AddControlPlaneStorage(config);

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

            var connectionString = context.Database.GetConnectionString();
            Assert.NotNull(connectionString);
            Assert.Contains(tempDb, connectionString);
        }
        finally
        {
            if (File.Exists(tempDb)) File.Delete(tempDb);
        }
    }
}
