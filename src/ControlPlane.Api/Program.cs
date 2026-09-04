using System.Security.Claims;
using System.Text.Json.Serialization;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Adoption;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Features.Jobs;
using ControlPlane.Api.Features.Orchestration;
using ControlPlane.Api.Hubs;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
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
builder.Services.AddSingleton<IAgentCommandExecutor, AgentCommandExecutor>();
builder.Services.AddSingleton<IStepLogConsumer, StepLogStreamConsumer>();
builder.Services.AddSingleton<JobOrchestratorService>();

builder.Services.AddScoped<ISshBootstrapper, SshBootstrapper>();
builder.Services.AddScoped<NodeAdoptionService>();
builder.Services.AddScoped<HostService>();
builder.Services.AddScoped<ProxmoxProbeService>();
builder.Services.AddHttpClient(ProxmoxProbeService.StandardHttpClientName);
builder.Services.AddHttpClient(ProxmoxProbeService.InsecureHttpClientName)
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
app.MapNodeAdoptionEndpoints();
app.MapJobEndpoints();
app.MapJobLogEndpoints();
app.MapHub<JobLogHub>("/hubs/jobs");

app.Run();

public partial class Program { }
