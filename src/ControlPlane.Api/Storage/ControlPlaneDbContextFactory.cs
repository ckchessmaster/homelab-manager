using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ControlPlane.Api.Storage;

public class ControlPlaneDbContextFactory : IDesignTimeDbContextFactory<ControlPlaneDbContext>
{
    public ControlPlaneDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var isStandby = configuration.GetValue<bool>("STANDBY_MODE", false);
        var optionsBuilder = new DbContextOptionsBuilder<ControlPlaneDbContext>();

        if (isStandby)
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".controlplane",
                "standby-state.db"
            );
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            optionsBuilder.UseSqlite($"Data Source={dbPath}").UseSnakeCaseNamingConvention();
        }
        else
        {
            var connectionString = configuration.GetConnectionString("PostgresDatabase")
                ?? "Host=localhost;Database=controlplane_designtime";

            optionsBuilder.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
            }).UseSnakeCaseNamingConvention();
        }

        return new ControlPlaneDbContext(optionsBuilder.Options);
    }
}
