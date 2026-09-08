using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Storage;

public static class DependencyInjection
{
    public static IServiceCollection AddControlPlaneStorage(this IServiceCollection services, IConfiguration config)
    {
        var isStandby = config.GetValue<bool>("STANDBY_MODE", false);

        services.AddDbContext<ControlPlaneDbContext>(options =>
        {
            if (isStandby)
            {
                var dbPath = config.GetValue<string>("STANDBY_DB_PATH")
                    ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".controlplane",
                        "standby-state.db"
                    );
                var directory = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                options.UseSqlite($"Data Source={dbPath}")
                    .UseSnakeCaseNamingConvention();
            }
            else
            {
                var connectionString = config.GetConnectionString("ControlPlaneDatabase")
                    ?? config.GetConnectionString("PostgresDatabase")
                    ?? throw new InvalidOperationException("Connection string 'ControlPlaneDatabase' or 'PostgresDatabase' not found. Ensure Aspire has referenced the database resource or provide ConnectionStrings:ControlPlaneDatabase.");

                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
                }).UseSnakeCaseNamingConvention();
            }
        });

        return services;
    }

    public static async Task InitializeDatabaseAsync(this IApplicationBuilder app, CancellationToken cancellationToken = default)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ControlPlaneDbContext>>();

        if (context.Database.IsSqlite())
        {
            logger.LogInformation("Standby mode active: initializing SQLite database.");
            await context.Database.EnsureCreatedAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS system_settings (key TEXT NOT NULL PRIMARY KEY, value_json TEXT NOT NULL, updated_at TEXT NOT NULL);",
                cancellationToken);
            try
            {
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE hosts ADD COLUMN proxmox_instance_id TEXT;", cancellationToken);
            }
            catch
            {
                // Ignored if column already exists
            }
            try
            {
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE hosts ADD COLUMN k8s_cluster_id TEXT;", cancellationToken);
            }
            catch
            {
                // Ignored if column already exists
            }
            try
            {
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE hosts ADD COLUMN k8s_node_name TEXT;", cancellationToken);
            }
            catch
            {
                // Ignored if column already exists
            }
            await DbSeeder.SeedStandbyAsync(context, cancellationToken);
            logger.LogInformation("SQLite database schema ensured and seeded.");
        }
        else
        {
            logger.LogInformation("Cluster mode active: ensuring 'controlplane' schema and applying PostgreSQL migrations.");
            await context.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS controlplane;", cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("PostgreSQL migrations applied successfully.");
        }
    }
}
