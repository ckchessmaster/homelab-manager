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

        // Check if disabled explicitly or in standby mode unless enabled
        var isStandby = configuration.GetValue<bool>("STANDBY_MODE", false);
        if (isStandby || !options.Enabled)
        {
            return services;
        }

        services.Configure<TemporalOptions>(configuration.GetSection(TemporalOptions.SectionName));

        var targetHost = options.ServerUrl;
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

        services.AddTemporalClient(targetHost, options.Namespace);
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

        return services;
    }
}

[Workflow]
public class SystemPingWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync() => Task.FromResult("pong");
}
