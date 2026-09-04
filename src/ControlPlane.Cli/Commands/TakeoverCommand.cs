using System.CommandLine;
using ControlPlane.Api.Storage;
using ControlPlane.Cli.Synchronization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Cli.Commands;

public static class TakeoverCommand
{
    public static Command Create()
    {
        var command = new Command("takeover", "Pulls cluster snapshot and acquires maintenance lock");

        var clusterUrlOption = new Option<string>("--cluster-url", "Cluster base URL") { IsRequired = true };
        var apiKeyOption = new Option<string>("--api-key", () => "dev-secret-key-123", "Cluster API Key");
        var dbPathOption = new Option<string?>("--db-path", "Path to SQLite database");

        command.AddOption(clusterUrlOption);
        command.AddOption(apiKeyOption);
        command.AddOption(dbPathOption);

        command.SetHandler(async (clusterUrl, apiKey, dbPath) =>
        {
            var defaultDbDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".controlplane");
            Directory.CreateDirectory(defaultDbDir);
            var resolvedDbPath = string.IsNullOrWhiteSpace(dbPath)
                ? Path.Combine(defaultDbDir, "standby-state.db")
                : dbPath;

            var services = new ServiceCollection();
            services.AddLogging(b => b.AddConsole());
            services.AddDbContext<ControlPlaneDbContext>(options =>
                options.UseSqlite($"Data Source={resolvedDbPath}"));

            services.AddHttpClient("ClusterClient")
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                });

            services.AddTransient(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new SnapshotPuller(factory.CreateClient("ClusterClient"), sp.GetRequiredService<ILogger<SnapshotPuller>>());
            });

            services.AddTransient(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new LeaseManager(factory.CreateClient("ClusterClient"), sp.GetRequiredService<ILogger<LeaseManager>>());
            });

            var sp = services.BuildServiceProvider();
            var puller = sp.GetRequiredService<SnapshotPuller>();
            var leaseManager = sp.GetRequiredService<LeaseManager>();
            var db = sp.GetRequiredService<ControlPlaneDbContext>();

            Console.WriteLine($"[TAKEOVER] Pulling snapshot from {clusterUrl}...");
            var snapshot = await puller.PullSnapshotAsync(clusterUrl, apiKey);
            await puller.SeedLocalDatabaseAsync(snapshot, db);

            var holder = $"StandbyCli-{Environment.MachineName}";
            Console.WriteLine($"[TAKEOVER] Acquiring distributed lease...");
            var acquired = await leaseManager.AcquireLeaseAsync(clusterUrl, apiKey, holder);
            if (acquired)
            {
                Console.WriteLine("[TAKEOVER] Takeover successful. You may now run `serve`.");
            }
            else
            {
                Console.WriteLine("[ERROR] Takeover failed: lease acquisition rejected.");
            }
        }, clusterUrlOption, apiKeyOption, dbPathOption);

        return command;
    }
}
