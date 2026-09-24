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
                    ?? config.GetConnectionString("PostgresDatabase");

                var uri = config["uri"] ?? config["URI"] ?? config["DATABASE_URL"] ?? config["POSTGRES_URL"];

                var explicitHost = config["Database__Host"] ?? config["DB_HOST"] ?? config["POSTGRES_HOST"];
                var explicitPort = config["Database__Port"] ?? config["DB_PORT"] ?? config["POSTGRES_PORT"];
                var explicitDb = config["Database__Database"] ?? config["Database__Name"] ?? config["DB_NAME"] ?? config["POSTGRES_DB"];
                var explicitUser = config["Database__Username"] ?? config["Database__User"] ?? config["DB_USER"] ?? config["POSTGRES_USER"];
                var pass = config["Database__Password"]
                    ?? config["DB_PASSWORD"]
                    ?? config["POSTGRES_PASSWORD"]
                    ?? config["password"]
                    ?? config["PASSWORD"]
                    ?? config["postgres-password"]
                    ?? config["db-password"];

                if (!string.IsNullOrWhiteSpace(uri) && string.IsNullOrWhiteSpace(explicitHost))
                {
                    connectionString = uri;
                }
                else if (!string.IsNullOrWhiteSpace(explicitHost))
                {
                    // An explicit host was provided (e.g. from ConfigMap or environment).
                    // Even if an existing connection string was found (e.g. from a default secret),
                    // the explicit configuration must take precedence.
                    Npgsql.NpgsqlConnectionStringBuilder builder;
                    try
                    {
                        builder = !string.IsNullOrWhiteSpace(connectionString)
                            ? new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
                            : new Npgsql.NpgsqlConnectionStringBuilder();
                    }
                    catch
                    {
                        builder = new Npgsql.NpgsqlConnectionStringBuilder();
                    }

                    builder.Host = explicitHost;
                    if (!string.IsNullOrWhiteSpace(explicitPort) && int.TryParse(explicitPort, out var p))
                    {
                        builder.Port = p;
                    }
                    else if (builder.Port <= 0)
                    {
                        builder.Port = 5432;
                    }

                    if (!string.IsNullOrWhiteSpace(explicitDb))
                    {
                        builder.Database = explicitDb;
                    }
                    else if (string.IsNullOrWhiteSpace(builder.Database))
                    {
                        builder.Database = "controlplane";
                    }

                    if (!string.IsNullOrWhiteSpace(explicitUser))
                    {
                        builder.Username = explicitUser;
                    }
                    else if (string.IsNullOrWhiteSpace(builder.Username))
                    {
                        builder.Username = "controlplane";
                    }

                    if (!string.IsNullOrWhiteSpace(pass))
                    {
                        builder.Password = pass;
                    }

                    connectionString = builder.ConnectionString;
                }
                else
                {
                    // No explicit host provided, fallback to defaults or existing connection string
                    var host = "controlplane-postgres";
                    var port = "5432";
                    var db = "controlplane";
                    var user = "controlplane";

                    if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("Password=;") || connectionString.EndsWith("Password="))
                    {
                        if (!string.IsNullOrWhiteSpace(pass))
                        {
                            connectionString = $"Host={host};Port={port};Database={db};Username={user};Password={pass}";
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(pass))
                    {
                        try
                        {
                            var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
                            if (string.IsNullOrWhiteSpace(builder.Password))
                            {
                                builder.Password = pass;
                                connectionString = builder.ConnectionString;
                            }
                        }
                        catch
                        {
                            // Keep connectionString as-is if parsing fails
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException("Connection string 'ControlPlaneDatabase' or 'PostgresDatabase' not found. Ensure Aspire has referenced the database resource or provide ConnectionStrings:ControlPlaneDatabase.");
                }

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
