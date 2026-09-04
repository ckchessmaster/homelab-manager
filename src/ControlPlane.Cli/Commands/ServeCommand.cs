using System.CommandLine;
using System.Text.Json.Serialization;
using ControlPlane.Api.Features.Adoption;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Cluster;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Features.Jobs;
using ControlPlane.Api.Features.Orchestration;
using ControlPlane.Api.Hubs;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using ControlPlane.Cli.Synchronization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Cli.Commands;

public static class ServeCommand
{
    public static Command Create()
    {
        var command = new Command("serve", "Starts the standalone ControlPlane runner with embedded dashboard");

        var portOption = new Option<int>("--port", () => 5200, "Port to listen on");
        var takeoverOption = new Option<bool>("--takeover", () => false, "Perform cluster takeover prior to starting");
        var clusterUrlOption = new Option<string?>("--cluster-url", "Cluster base URL (e.g. https://k8s.homelab.local)");
        var apiKeyOption = new Option<string?>("--api-key", () => "dev-secret-key-123", "API key for cluster authentication");
        var dbPathOption = new Option<string?>("--db-path", "Path to local SQLite database (default ~/.controlplane/standby-state.db)");

        command.AddOption(portOption);
        command.AddOption(takeoverOption);
        command.AddOption(clusterUrlOption);
        command.AddOption(apiKeyOption);
        command.AddOption(dbPathOption);

        command.SetHandler(async (port, takeover, clusterUrl, apiKey, dbPath) =>
        {
            await RunServerAsync(port, takeover, clusterUrl, apiKey, dbPath, CancellationToken.None);
        }, portOption, takeoverOption, clusterUrlOption, apiKeyOption, dbPathOption);

        return command;
    }

    public static async Task RunServerAsync(
        int port,
        bool takeover,
        string? clusterUrl,
        string? apiKey,
        string? dbPath,
        CancellationToken cancellationToken)
    {
        var defaultDbDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".controlplane");
        Directory.CreateDirectory(defaultDbDir);
        var resolvedDbPath = string.IsNullOrWhiteSpace(dbPath)
            ? Path.Combine(defaultDbDir, "standby-state.db")
            : dbPath;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

        // Configure SQLite
        var connectionString = $"Data Source={resolvedDbPath}";
        builder.Configuration["Storage:Provider"] = "Sqlite";
        builder.Configuration["ConnectionStrings:Sqlite"] = connectionString;
        builder.Configuration["Auth:DevBypass"] = "true";

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services.AddControlPlaneStorage(builder.Configuration);
        builder.Services.AddControlPlaneSecurity(builder.Configuration);
        builder.Services.AddSignalR();
        builder.Services.AddAgentHubServices();
        builder.Services.AddSingleton<ClusterState>();
        builder.Services.AddSingleton<IAgentCommandExecutor, AgentCommandExecutor>();
        builder.Services.AddSingleton<IStepLogConsumer, StepLogStreamConsumer>();
        builder.Services.AddSingleton<JobOrchestratorService>();

        builder.Services.AddScoped<ISshBootstrapper, SshBootstrapper>();
        builder.Services.AddScoped<NodeAdoptionService>();
        builder.Services.AddScoped<HostService>();

        // HTTP Client handler allowing self-signed certificates in homelab
        builder.Services.AddHttpClient("ClusterClient")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });

        builder.Services.AddTransient(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient("ClusterClient");
            var logger = sp.GetRequiredService<ILogger<SnapshotPuller>>();
            return new SnapshotPuller(client, logger);
        });

        builder.Services.AddTransient(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient("ClusterClient");
            var logger = sp.GetRequiredService<ILogger<LeaseManager>>();
            return new LeaseManager(client, logger);
        });

        builder.Services.AddTransient(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient("ClusterClient");
            var leaseManager = sp.GetRequiredService<LeaseManager>();
            var logger = sp.GetRequiredService<ILogger<DeltaSyncPusher>>();
            return new DeltaSyncPusher(client, leaseManager, logger);
        });

        var app = builder.Build();

        await app.InitializeDatabaseAsync();

        // Perform takeover if requested
        var holderIdentifier = $"StandbyCli-{Environment.MachineName}";
        var takeoverTimestamp = DateTimeOffset.UtcNow;

        if (takeover && !string.IsNullOrWhiteSpace(clusterUrl) && !string.IsNullOrWhiteSpace(apiKey))
        {
            using var scope = app.Services.CreateScope();
            var puller = scope.ServiceProvider.GetRequiredService<SnapshotPuller>();
            var leaseManager = scope.ServiceProvider.GetRequiredService<LeaseManager>();
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

            Console.WriteLine($"[TAKEOVER] Pulling snapshot from {clusterUrl}...");
            var snapshot = await puller.PullSnapshotAsync(clusterUrl, apiKey, cancellationToken);
            await puller.SeedLocalDatabaseAsync(snapshot, db, cancellationToken);

            Console.WriteLine($"[TAKEOVER] Acquiring distributed lease on cluster...");
            var acquired = await leaseManager.AcquireLeaseAsync(clusterUrl, apiKey, holderIdentifier, 60, cancellationToken);
            if (!acquired)
            {
                Console.WriteLine("[ERROR] Could not acquire cluster lease. Aborting takeover.");
                return;
            }
            Console.WriteLine("[TAKEOVER] Cluster lease acquired. Operating in Standby Runner Mode.");
        }

        app.UseControlPlaneSecurity();
        app.UseAgentHub();

        // Embedded static file provider for React SPA
        var embeddedProvider = new ManifestEmbeddedFileProvider(typeof(ServeCommand).Assembly, "wwwroot");
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = embeddedProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = embeddedProvider });

        app.MapGet("/api/status", () => Results.Ok(new
        {
            status = "healthy",
            mode = "StandbyRunner",
            port,
            database = resolvedDbPath,
            timestamp = DateTimeOffset.UtcNow
        }));

        app.MapHostEndpoints();
        app.MapJobEndpoints();
        app.MapJobLogEndpoints();
        app.MapClusterEndpoints();
        app.MapHub<JobLogHub>("/hubs/jobs");

        // SPA fallback
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = embeddedProvider });

        Console.WriteLine($"[STANDBY] ControlPlane Standby Runner active on http://localhost:{port}");

        await app.StartAsync(cancellationToken);

        // Wait for shutdown signal
        var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var tcs = new TaskCompletionSource();
        using var reg = appLifetime.ApplicationStopping.Register(() => tcs.TrySetResult());
        await tcs.Task;

        // Post-shutdown reconciliation if takeover was active
        if (takeover && !string.IsNullOrWhiteSpace(clusterUrl) && !string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("[RECONCILE] Initiating post-maintenance delta synchronization...");
            using var scope = app.Services.CreateScope();
            var pusher = scope.ServiceProvider.GetRequiredService<DeltaSyncPusher>();
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

            var success = await pusher.ReconcileAndReleaseAsync(clusterUrl, apiKey, holderIdentifier, db, takeoverTimestamp, CancellationToken.None);
            if (success)
            {
                Console.WriteLine("[RECONCILE] Delta sync completed and maintenance lease released.");
            }
            else
            {
                Console.WriteLine("[WARNING] Delta reconciliation could not complete cleanly. Lease held for safety.");
            }
        }

        await app.StopAsync(CancellationToken.None);
    }
}
