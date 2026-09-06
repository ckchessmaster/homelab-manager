using System.CommandLine;
using System.Text.Json.Serialization;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Adapters.Redfish;
using ControlPlane.Api.Features.Adapters.UniFi;
using ControlPlane.Api.Features.Adoption;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Cluster;
using ControlPlane.Api.Features.Discovery;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Features.Jobs;
using ControlPlane.Api.Features.Orchestration;
using ControlPlane.Api.Features.Orchestration.Pipelines;
using ControlPlane.Api.Features.Orchestration.Temporal;
using ControlPlane.Api.Features.Orchestration.Temporal.Endpoints;
using ControlPlane.Api.Features.Security;
using ControlPlane.Api.Hubs;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using ControlPlane.Cli.Synchronization;
using ControlPlane.Cli.Temporal;
using k8s;
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
using Microsoft.Extensions.Options;

namespace ControlPlane.Cli.Commands;

public static class ServeCommand
{
    public static Command Create()
    {
        var command = new Command("serve", "Starts the standalone ControlPlane runner with embedded dashboard and local Temporal orchestration");

        var portOption = new Option<int>("--port", () => 5200, "Port to listen on");
        var takeoverOption = new Option<bool>("--takeover", () => false, "Perform cluster takeover prior to starting");
        var clusterUrlOption = new Option<string?>("--cluster-url", "Cluster base URL (e.g. https://k8s.homelab.local)");
        var apiKeyOption = new Option<string?>("--api-key", () => "dev-secret-key-123", "API key for cluster authentication");
        var dbPathOption = new Option<string?>("--db-path", "Path to local SQLite database (default ~/.controlplane/standby-state.db)");

        // Temporal Dev Server Options
        var startTemporalOption = new Option<bool>("--start-temporal", () => true, "Automatically start managed local Temporal dev-server with SQLite persistence");
        var noTemporalOption = new Option<bool>("--no-temporal", () => false, "Disable Temporal orchestration and run in fallback legacy mode");
        var temporalPortOption = new Option<int>("--temporal-port", () => 7233, "Temporal gRPC port");
        var temporalUiPortOption = new Option<int>("--temporal-ui-port", () => 8233, "Temporal Web UI port");
        var temporalDbOption = new Option<string?>("--temporal-db", "Path to SQLite database for Temporal history (default ~/.controlplane/temporal-standby.db)");
        var temporalUrlOption = new Option<string?>("--temporal-url", "Direct connection address for Temporal server (e.g. 127.0.0.1:7233)");
        var temporalBinOption = new Option<string?>("--temporal-bin", "Custom path to temporal CLI binary");

        command.AddOption(portOption);
        command.AddOption(takeoverOption);
        command.AddOption(clusterUrlOption);
        command.AddOption(apiKeyOption);
        command.AddOption(dbPathOption);

        command.AddOption(startTemporalOption);
        command.AddOption(noTemporalOption);
        command.AddOption(temporalPortOption);
        command.AddOption(temporalUiPortOption);
        command.AddOption(temporalDbOption);
        command.AddOption(temporalUrlOption);
        command.AddOption(temporalBinOption);

        command.SetHandler(async (context) =>
        {
            var port = context.ParseResult.GetValueForOption(portOption);
            var takeover = context.ParseResult.GetValueForOption(takeoverOption);
            var clusterUrl = context.ParseResult.GetValueForOption(clusterUrlOption);
            var apiKey = context.ParseResult.GetValueForOption(apiKeyOption);
            var dbPath = context.ParseResult.GetValueForOption(dbPathOption);

            var startTemporal = context.ParseResult.GetValueForOption(startTemporalOption);
            var noTemporal = context.ParseResult.GetValueForOption(noTemporalOption);
            var temporalPort = context.ParseResult.GetValueForOption(temporalPortOption);
            var temporalUiPort = context.ParseResult.GetValueForOption(temporalUiPortOption);
            var temporalDb = context.ParseResult.GetValueForOption(temporalDbOption);
            var temporalUrl = context.ParseResult.GetValueForOption(temporalUrlOption);
            var temporalBin = context.ParseResult.GetValueForOption(temporalBinOption);

            await RunServerAsync(
                port, takeover, clusterUrl, apiKey, dbPath,
                startTemporal, noTemporal, temporalPort, temporalUiPort, temporalDb, temporalUrl, temporalBin,
                CancellationToken.None);
        });

        return command;
    }

    public static async Task RunServerAsync(
        int port,
        bool takeover,
        string? clusterUrl,
        string? apiKey,
        string? dbPath,
        bool startTemporal = true,
        bool noTemporal = false,
        int temporalPort = 7233,
        int temporalUiPort = 8233,
        string? temporalDb = null,
        string? temporalUrl = null,
        string? temporalBin = null,
        CancellationToken cancellationToken = default)
    {
        var defaultDbDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".controlplane");
        Directory.CreateDirectory(defaultDbDir);
        var resolvedDbPath = string.IsNullOrWhiteSpace(dbPath)
            ? Path.Combine(defaultDbDir, "standby-state.db")
            : dbPath;

        var resolvedTemporalDb = string.IsNullOrWhiteSpace(temporalDb)
            ? Path.Combine(defaultDbDir, "temporal-standby.db")
            : temporalDb;

        var resolvedTemporalUrl = string.IsNullOrWhiteSpace(temporalUrl)
            ? $"127.0.0.1:{temporalPort}"
            : temporalUrl;

        // Manage Temporal Dev Server
        var useTemporal = !noTemporal;
        ITemporalDevServerManager? devServerManager = null;

        if (useTemporal && startTemporal)
        {
            devServerManager = new TemporalDevServerManager();
            var devServerConfig = new TemporalDevServerConfig(
                Enabled: true,
                GrpcPort: temporalPort,
                UiPort: temporalUiPort,
                DbFilename: resolvedTemporalDb,
                Ip: "127.0.0.1",
                CustomBinaryPath: temporalBin
            );

            Console.WriteLine($"[TEMPORAL] Initializing Standby Temporal Dev Server (SQLite: {resolvedTemporalDb})...");
            var started = await devServerManager.EnsureRunningAsync(devServerConfig, cancellationToken);
            if (!started)
            {
                Console.WriteLine("[WARNING] Temporal dev server could not be started. Falling back to legacy orchestration mode.");
                useTemporal = false;
            }
            else
            {
                Console.WriteLine($"[TEMPORAL] Temporal Dev Server active on {resolvedTemporalUrl} (Web UI: http://127.0.0.1:{temporalUiPort})");
            }
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

        // Configure Storage & Standby Mode
        var connectionString = $"Data Source={resolvedDbPath}";
        builder.Configuration["Storage:Provider"] = "Sqlite";
        builder.Configuration["ConnectionStrings:Sqlite"] = connectionString;
        builder.Configuration["STANDBY_MODE"] = "true";
        builder.Configuration["Auth:DevBypass"] = "true";

        if (useTemporal)
        {
            builder.Configuration["STANDBY_TEMPORAL"] = "true";
            builder.Configuration["Temporal:Enabled"] = "true";
            builder.Configuration["Temporal:ServerUrl"] = resolvedTemporalUrl;
        }
        else
        {
            builder.Configuration["STANDBY_TEMPORAL"] = "false";
            builder.Configuration["Temporal:Enabled"] = "false";
        }

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
        builder.Services.AddSingleton<IPipelineCatalog, PipelineCatalog>();
        builder.Services.AddSingleton<JobOrchestratorService>();

        // Register Temporal Orchestration if enabled
        builder.Services.AddTemporalOrchestration(builder.Configuration);

        // Core business & adoption services
        builder.Services.AddScoped<ISshBootstrapper, SshBootstrapper>();
        builder.Services.AddScoped<NodeAdoptionService>();
        builder.Services.AddScoped<HostService>();
        builder.Services.AddSingleton<AgentBinaryService>();
        builder.Services.AddScoped<MassAgentUpdateService>();
        builder.Services.AddScoped<ProxmoxProbeService>();

        // Infrastructure Adapters for Standby Mode
        builder.Services.Configure<ProxmoxOptions>(builder.Configuration.GetSection(ProxmoxOptions.SectionName));
        builder.Services.Configure<SnapshotRetentionOptions>(builder.Configuration.GetSection(SnapshotRetentionOptions.SectionName));
        builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
        builder.Services.AddSingleton<ISecurityKeyProvider, EnvironmentOrFileKeyProvider>();
        builder.Services.AddSingleton<ISecretEncryptionService, SecretEncryptionService>();
        builder.Services.AddScoped<IAdapterConfigService, AdapterConfigService>();
        builder.Services.AddScoped<ProxmoxTaskPoller>();
        builder.Services.AddScoped<IProxmoxClient, ProxmoxClient>();
        builder.Services.AddScoped<ISnapshotRetentionService, SnapshotRetentionService>();
        builder.Services.AddScoped<IRedfishClient, RedfishClient>();
        builder.Services.AddScoped<IUniFiClient, UniFiClient>();
        builder.Services.AddScoped<IDiscoveryService, DiscoveryService>();

        builder.Services.Configure<KubernetesConfigOptions>(builder.Configuration.GetSection(KubernetesConfigOptions.SectionName));
        builder.Services.AddSingleton<IKubernetes>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<KubernetesConfigOptions>>().Value;
            KubernetesClientConfiguration config;

            if (opts.InClusterConfig)
            {
                config = KubernetesClientConfiguration.InClusterConfig();
            }
            else if (!string.IsNullOrWhiteSpace(opts.KubeConfigPath) && File.Exists(opts.KubeConfigPath))
            {
                config = KubernetesClientConfiguration.BuildConfigFromConfigFile(opts.KubeConfigPath);
            }
            else
            {
                try
                {
                    config = KubernetesClientConfiguration.BuildDefaultConfig();
                }
                catch
                {
                    config = new KubernetesClientConfiguration { Host = opts.MasterUri ?? "http://localhost:8080" };
                }
            }

            return new Kubernetes(config);
        });
        builder.Services.AddScoped<IKubernetesAdapter, KubernetesAdapter>();
        builder.Services.AddHttpClient(ProxmoxProbeService.StandardHttpClientName);

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
                if (devServerManager != null)
                {
                    await devServerManager.StopAsync(CancellationToken.None);
                }
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
            temporal = useTemporal ? "active" : "disabled",
            temporalServer = useTemporal ? resolvedTemporalUrl : null,
            temporalUi = useTemporal ? $"http://127.0.0.1:{temporalUiPort}" : null,
            timestamp = DateTimeOffset.UtcNow
        }));

        // Map all API and Adapter Endpoints for 100% Feature Parity
        app.MapHostEndpoints();
        app.MapProxmoxEndpoints();
        app.MapSnapshotRetentionEndpoints();
        app.MapNodeAdoptionEndpoints();
        app.MapAgentManagementEndpoints();
        app.MapJobEndpoints();
        app.MapJobLogEndpoints();
        app.MapClusterEndpoints();
        app.MapRedfishEndpoints();
        app.MapUniFiEndpoints();
        app.MapKubernetesEndpoints();
        app.MapDiscoveryEndpoints();
        app.MapSecurityEndpoints();
        app.MapTemporalWorkflowEndpoints();
        app.MapHub<JobLogHub>("/hubs/jobs");

        // SPA fallback
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = embeddedProvider });

        Console.WriteLine($"[STANDBY] ControlPlane Standby Runner active on http://localhost:{port}");
        if (useTemporal)
        {
            Console.WriteLine($"[STANDBY] Temporal Web UI accessible at http://127.0.0.1:{temporalUiPort}");
        }

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

        if (devServerManager != null)
        {
            Console.WriteLine("[TEMPORAL] Stopping managed Temporal dev server...");
            await devServerManager.StopAsync(CancellationToken.None);
            await devServerManager.DisposeAsync();
        }

        await app.StopAsync(CancellationToken.None);
    }
}
