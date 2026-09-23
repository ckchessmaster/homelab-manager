using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Temporalio.Client;
using Temporalio.Extensions.Hosting;
using Temporalio.Workflows;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities;
using ControlPlane.Api.Features.Orchestration.Temporal.Workflows;

namespace ControlPlane.Api.Features.Orchestration.Temporal;

public static class TemporalServiceCollectionExtensions
{
    public static IServiceCollection AddTemporalOrchestration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(TemporalOptions.SectionName).Get<TemporalOptions>() ?? new TemporalOptions();

        // In standby mode, Temporal is only enabled if explicitly configured with STANDBY_TEMPORAL = true
        var isStandby = configuration.GetValue<bool>("STANDBY_MODE", false);
        var standbyTemporal = configuration.GetValue<bool>("STANDBY_TEMPORAL", false);

        if (isStandby && !standbyTemporal)
        {
            return services;
        }

        if (!options.Enabled)
        {
            return services;
        }

        services.Configure<TemporalOptions>(configuration.GetSection(TemporalOptions.SectionName));

        var targetHost = options.Endpoint;
        if (string.IsNullOrWhiteSpace(targetHost))
        {
            targetHost = "localhost:7233";
        }
        // Normalize if targetHost contains protocol
        if (targetHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            targetHost = targetHost["http://".Length..];
        }
        else if (targetHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            targetHost = targetHost["https://".Length..];
        }

        services.AddSingleton<IWorkflowLogEmitter, WorkflowLogEmitter>();
        services.AddScoped<IPreflightActivities, PreflightActivities>();
        services.AddScoped<IProxmoxActivities, ProxmoxActivities>();
        services.AddScoped<IKubernetesActivities, KubernetesActivities>();
        services.AddScoped<IAgentActivities, AgentActivities>();
        services.AddScoped<IHealthProbeActivities, HealthProbeActivities>();
        services.AddScoped<IUpdateJobActivities, UpdateJobActivities>();

        // Register Zitadel M2M Auth services if enabled
        if (options.Auth.Enabled)
        {
            services.AddHttpClient<ControlPlane.Api.Features.Orchestration.Temporal.Auth.IZitadelTokenProvider, ControlPlane.Api.Features.Orchestration.Temporal.Auth.ZitadelTokenProvider>();
            services.AddHostedService<ControlPlane.Api.Features.Orchestration.Temporal.Auth.TemporalTokenRefreshService>();
        }

        services.AddTemporalClient(clientOptions =>
        {
            clientOptions.TargetHost = targetHost;
            clientOptions.Namespace = options.Namespace;
        });

        services.AddHostedService<TemporalStartupDiagnosticsService>();

        services.AddHostedTemporalWorker(options.TaskQueue)
            .AddWorkflow<Workflows.HostUpgradeWorkflow>()
            .AddWorkflow<Workflows.RollingUpgradeWorkflow>()
            .AddWorkflow<SystemPingWorkflow>()
            .AddScopedActivities<Activities.PreflightActivities>()
            .AddScopedActivities<Activities.ProxmoxActivities>()
            .AddScopedActivities<Activities.KubernetesActivities>()
            .AddScopedActivities<Activities.AgentActivities>()
            .AddScopedActivities<Activities.HealthProbeActivities>()
            .AddScopedActivities<Activities.UpdateJobActivities>();

        services.AddHealthChecks().AddCheck<TemporalHealthCheck>("temporal", tags: ["ready"]);

        return services;
    }
}

[Workflow]
public class SystemPingWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync() => Task.FromResult("pong");
}
