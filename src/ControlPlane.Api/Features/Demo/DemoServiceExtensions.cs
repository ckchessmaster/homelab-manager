using ControlPlane.Api.Features.Adapters.HomeAssistant;
using ControlPlane.Api.Features.Adapters.Idrac;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Kubernetes.Helm;
using ControlPlane.Api.Features.Adapters.OPNsense;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Adapters.Redfish;
using ControlPlane.Api.Features.Adapters.UniFi;
using ControlPlane.Api.Features.Demo.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ControlPlane.Api.Features.Demo;

public static class DemoServiceExtensions
{
    public static IServiceCollection AddControlPlaneDemoMode(this IServiceCollection services, IConfiguration configuration)
    {
        var demoSection = configuration.GetSection(DemoOptions.SectionName);
        var isDemo = string.Equals(Environment.GetEnvironmentVariable("DEMO_MODE"), "true", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(configuration["DEMO_MODE"], "true", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(configuration["demo-mode"], "true", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(configuration["demoMode"], "true", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(configuration["demo"], "true", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(configuration["Parameters:demo-mode"], "true", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(configuration["ControlPlane:DemoMode"], "true", StringComparison.OrdinalIgnoreCase) ||
                     demoSection.GetValue<bool>("Enabled", false);

        services.Configure<DemoOptions>(options =>
        {
            options.Enabled = isDemo;
            options.SimulationTickSeconds = demoSection.GetValue<int>("SimulationTickSeconds", 3);
            options.AutoSeed = demoSection.GetValue<bool>("AutoSeed", true);
        });

        services.AddScoped<DemoDataSeeder>();

        if (isDemo)
        {
            // Register Mock Adapters (Strict Sandbox Override)
            services.AddSingleton<MockProxmoxClient>();
            services.AddScoped<IProxmoxClient>(sp => sp.GetRequiredService<MockProxmoxClient>());
            services.AddScoped<IProxmoxClientFactory, MockProxmoxClientFactory>();

            services.AddSingleton<MockKubernetesAdapter>();
            services.AddScoped<IKubernetesAdapter>(sp => sp.GetRequiredService<MockKubernetesAdapter>());
            services.AddScoped<IKubernetesClientFactory, MockKubernetesClientFactory>();

            services.AddSingleton<MockUniFiClient>();
            services.AddScoped<IUniFiClient>(sp => sp.GetRequiredService<MockUniFiClient>());
            services.AddScoped<IUniFiClientFactory, MockUniFiClientFactory>();

            services.AddSingleton<MockIdracClient>();
            services.AddScoped<IIdracClient>(sp => sp.GetRequiredService<MockIdracClient>());
            services.AddScoped<IIdracClientFactory, MockIdracClientFactory>();

            services.AddSingleton<IRedfishClient, MockRedfishClient>();

            services.AddSingleton<MockOpnsenseClient>();
            services.AddScoped<IOPNsenseClient>(sp => sp.GetRequiredService<MockOpnsenseClient>());
            services.AddScoped<IOPNsenseClientFactory, MockOpnsenseClientFactory>();

            services.AddSingleton<MockHomeAssistantClient>();
            services.AddScoped<IHomeAssistantClient>(sp => sp.GetRequiredService<MockHomeAssistantClient>());
            services.AddScoped<IHomeAssistantClientFactory, MockHomeAssistantClientFactory>();

            services.AddSingleton<IHelmClient, MockHelmClient>();

            // Register Background Simulation Engine
            services.AddHostedService<DemoSimulationWorker>();
            services.AddSingleton<DemoSimulationWorker>(sp => sp.GetServices<IHostedService>().OfType<DemoSimulationWorker>().First());
        }

        return services;
    }
}
