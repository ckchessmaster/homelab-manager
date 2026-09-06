using System.Security.Claims;
using System.Text.Json.Serialization;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Adapters.Redfish;
using ControlPlane.Api.Features.Adapters.UniFi;
using ControlPlane.Api.Features.Adoption;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Cluster;
using ControlPlane.Api.Features.Discovery;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Features.Jobs;
using ControlPlane.Api.Features.Orchestration;
using ControlPlane.Api.Features.Orchestration.Pipelines;
using ControlPlane.Api.Features.Security;
using ControlPlane.Api.Hubs;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using k8s;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
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

builder.Services.AddScoped<ISshBootstrapper, SshBootstrapper>();
builder.Services.AddScoped<NodeAdoptionService>();
builder.Services.AddScoped<HostService>();
builder.Services.AddSingleton<AgentBinaryService>();
builder.Services.AddScoped<MassAgentUpdateService>();
builder.Services.AddScoped<ProxmoxProbeService>();
builder.Services.Configure<ProxmoxOptions>(builder.Configuration.GetSection(ProxmoxOptions.SectionName));
builder.Services.Configure<SnapshotRetentionOptions>(builder.Configuration.GetSection(SnapshotRetentionOptions.SectionName));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
builder.Services.AddSingleton<ISecurityKeyProvider, EnvironmentOrFileKeyProvider>();
builder.Services.AddSingleton<ISecretEncryptionService, SecretEncryptionService>();
builder.Services.AddHostedService<SecretsMigrationWorker>();
builder.Services.AddScoped<IAdapterConfigService, AdapterConfigService>();
builder.Services.AddScoped<ProxmoxTaskPoller>();
builder.Services.AddScoped<IProxmoxClient, ProxmoxClient>();
builder.Services.AddScoped<ISnapshotRetentionService, SnapshotRetentionService>();
builder.Services.AddHostedService<SnapshotRetentionWorker>();
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
builder.Services.AddHttpClient(ProxmoxProbeService.InsecureHttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
builder.Services.AddHttpClient(RedfishClient.InsecureHttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
builder.Services.AddHttpClient(UniFiClient.InsecureHttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });


builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = ApiKeyAuthenticationOptions.DefaultHeaderName,
            In = ParameterLocation.Header,
            Description = "API key authentication using the X-ControlPlane-Key header."
        };
        document.Components.SecuritySchemes.Add(ApiKeyAuthenticationOptions.DefaultScheme, scheme);

        var requirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(ApiKeyAuthenticationOptions.DefaultScheme, document)] = new List<string>()
        };
        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(requirement);

        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseControlPlaneSecurity();
app.UseAgentHub();

await app.InitializeDatabaseAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/", () => Results.Ok(new
{
    status = "healthy",
    service = "ControlPlane.Api",
    timestamp = DateTimeOffset.UtcNow
})).AllowAnonymous();

app.MapGet("/api/storage/status", async (ControlPlaneDbContext db) =>
{
    var provider = db.Database.ProviderName;
    var hostCount = await db.Hosts.CountAsync();
    var leaseCount = await db.ClusterLeases.CountAsync();

    return Results.Ok(new
    {
        provider,
        hostCount,
        leaseCount,
        timestamp = DateTimeOffset.UtcNow
    });
}).AllowAnonymous();

app.MapGet("/api/v1/auth/me", (ClaimsPrincipal user) =>
{
    var identity = user.Identity;
    var claims = user.Claims.Select(c => new { c.Type, c.Value }).ToList();

    return Results.Ok(new
    {
        isAuthenticated = identity?.IsAuthenticated ?? false,
        name = identity?.Name,
        roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
        claims
    });
}).RequireAuthorization();

app.MapGet("/api/v1/admin/ping", (ClaimsPrincipal user) => Results.Ok(new
{
    message = "pong",
    user = user.Identity?.Name,
    role = "Admin"
})).RequireAuthorization("RequireAdmin");

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
app.MapHub<JobLogHub>("/hubs/jobs");

app.MapPost("/api/v1/debug/test-reboot", async (
    DebugRebootRequest request,
    ControlPlaneDbContext db,
    AgentConnectionManager connectionManager,
    CancellationToken ct) =>
{
    var host = await db.Hosts.FindAsync(new object[] { request.HostId }, ct);
    if (host == null)
    {
        return Results.NotFound(new { message = $"Host with ID '{request.HostId}' not found." });
    }

    if (!connectionManager.IsOnline(host.Id))
    {
        return Results.BadRequest(new { message = $"Host '{host.Hostname}' agent is currently offline." });
    }

    var jobId = Guid.NewGuid();
    var job = new UpdateJob
    {
        Id = jobId,
        TargetHostId = host.Id,
        InitiatedBy = "DebugOperator",
        Status = UpdateJobState.AwaitingReconnect,
        ActiveStep = "Deterministic Host Reboot",
        StartedAt = DateTimeOffset.UtcNow
    };

    db.UpdateJobs.Add(job);
    await db.SaveChangesAsync(ct);

    var envelope = new AgentCommandEnvelope
    {
        Type = "CMD_REBOOT",
        JobId = jobId,
        Command = "systemctl",
        Args = new[] { "reboot" }
    };

    var sent = await connectionManager.SendCommandAsync(host.Id, envelope, ct);
    if (!sent)
    {
        job.Status = UpdateJobState.Failed;
        job.FailureReason = "Failed to dispatch reboot command";
        job.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    return Results.Accepted($"/api/v1/jobs/{jobId}", new
    {
        jobId,
        hostId = host.Id,
        status = UpdateJobState.AwaitingReconnect,
        message = $"Deterministic reboot initiated for {host.Hostname}"
    });
}).RequireAuthorization();

app.Run();

public record DebugRebootRequest(Guid HostId);

public partial class Program { }
