using ControlPlane.Api.Features.Orchestration.Temporal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Temporalio.Client;
using Xunit;

namespace ControlPlane.Api.Tests;

public class TemporalConfigurationTests
{
    [Fact]
    public void AddTemporalOrchestration_WhenDisabled_DoesNotRegisterTemporalClient()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Temporal:Enabled"] = "false",
                ["Temporal:ServerUrl"] = "localhost:7233"
            })
            .Build();

        services.AddTemporalOrchestration(configuration);

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<ITemporalClient>();
        Assert.Null(client);
    }

    [Fact]
    public void AddTemporalOrchestration_WhenEnabled_RegistersClientAndConfiguresOptions()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Temporal:Enabled"] = "true",
                ["Temporal:ServerUrl"] = "http://127.0.0.1:7233",
                ["Temporal:Namespace"] = "test-namespace",
                ["Temporal:TaskQueue"] = "test-task-queue"
            })
            .Build();

        services.AddLogging();
        services.AddTemporalOrchestration(configuration);

        var provider = services.BuildServiceProvider();

        // Verify options bound
        var options = provider.GetRequiredService<IOptions<TemporalOptions>>().Value;
        Assert.True(options.Enabled);
        Assert.Equal("http://127.0.0.1:7233", options.ServerUrl);
        Assert.Equal("http://127.0.0.1:7233", options.Endpoint);
        Assert.Equal("test-namespace", options.Namespace);
        Assert.Equal("test-task-queue", options.TaskQueue);

        // Verify client registered in DI
        var client = provider.GetService<ITemporalClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void AddTemporalOrchestration_DefaultOptions_UsesHomelabManagerNamespaceAndTasksQueue()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Temporal:Enabled"] = "true"
            })
            .Build();

        services.AddLogging();
        services.AddTemporalOrchestration(configuration);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TemporalOptions>>().Value;

        Assert.Equal("homelab-manager", options.Namespace);
        Assert.Equal("homelab-manager-tasks", options.TaskQueue);
        Assert.Equal("localhost:7233", options.Endpoint);
        Assert.False(options.Auth.Enabled);
    }

    [Fact]
    public void TemporalOptions_Endpoint_PrefersAddressOverServerUrl()
    {
        var options = new TemporalOptions
        {
            ServerUrl = "localhost:7233",
            Address = "temporal-frontend.temporal.svc.cluster.local:7233"
        };

        Assert.Equal("temporal-frontend.temporal.svc.cluster.local:7233", options.Endpoint);
    }

    [Fact]
    public void AddTemporalOrchestration_WhenStandbyModeWithoutStandbyTemporal_SkipsRegistration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["STANDBY_MODE"] = "true",
                ["Temporal:Enabled"] = "true",
                ["Temporal:ServerUrl"] = "localhost:7233"
            })
            .Build();

        services.AddTemporalOrchestration(configuration);

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<ITemporalClient>();
        Assert.Null(client);
    }

    [Fact]
    public void AddTemporalOrchestration_WhenStandbyModeWithStandbyTemporal_RegistersClient()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["STANDBY_MODE"] = "true",
                ["STANDBY_TEMPORAL"] = "true",
                ["Temporal:Enabled"] = "true",
                ["Temporal:ServerUrl"] = "127.0.0.1:7233"
            })
            .Build();

        services.AddLogging();
        services.AddTemporalOrchestration(configuration);

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<ITemporalClient>();
        Assert.NotNull(client);
    }
}
