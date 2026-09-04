using System.Net.WebSockets;
using ControlPlane.Api.Features.Adoption;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Tests;

public class NodeAdoptionTests
{
    private class FakeSshBootstrapper : ISshBootstrapper
    {
        public string ArchitectureToReturn { get; set; } = "x86_64";
        public bool ShouldThrowOnProbe { get; set; } = false;
        public List<(string Local, string Remote)> UploadedBinaries { get; } = new();
        public List<(string Content, string Remote)> UploadedTexts { get; } = new();
        public List<string> ExecutedCommands { get; } = new();
        public Action? OnBinaryUploaded { get; set; }

        public Task<string> ProbeArchitectureAsync(AdoptNodeRequest request, CancellationToken cancellationToken = default)
        {
            if (ShouldThrowOnProbe)
            {
                throw new InvalidOperationException("Connection refused or auth failed");
            }
            return Task.FromResult(ArchitectureToReturn);
        }

        public Task UploadBinaryAsync(AdoptNodeRequest request, string localBinaryPath, string remotePath, CancellationToken cancellationToken = default)
        {
            UploadedBinaries.Add((localBinaryPath, remotePath));
            OnBinaryUploaded?.Invoke();
            return Task.CompletedTask;
        }

        public Task UploadTextAsync(AdoptNodeRequest request, string content, string remotePath, CancellationToken cancellationToken = default)
        {
            UploadedTexts.Add((content, remotePath));
            return Task.CompletedTask;
        }

        public Task<string> ExecuteRemoteCommandAsync(AdoptNodeRequest request, string command, CancellationToken cancellationToken = default)
        {
            ExecutedCommands.Add(command);
            return Task.FromResult("ok");
        }

        public Task<string> ExecutePrivilegedCommandAsync(AdoptNodeRequest request, string command, CancellationToken cancellationToken = default)
        {
            ExecutedCommands.Add(command);
            return Task.FromResult("ok");
        }
    }

    private class DummyWebSocket : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => WebSocketCloseStatus.NormalClosure;
        public override string? CloseStatusDescription => "Closed";
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task AdoptNodeAsync_SuccessfulWorkflow_RegistersHostAndCompletesAllSteps()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"cp-test-adopt-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<ControlPlaneDbContext>(opt =>
                opt.UseSqlite($"Data Source={tempDb}").UseSnakeCaseNamingConvention());
            var serviceProvider = services.BuildServiceProvider();

            using (var initScope = serviceProvider.CreateScope())
            {
                var db = initScope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            var connectionManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
            var fakeBootstrapper = new FakeSshBootstrapper();

            var hostId = Guid.NewGuid();
            fakeBootstrapper.OnBinaryUploaded = () =>
            {
                // Simulate agent connecting back immediately once binary is deployed
                connectionManager.Register(hostId, "10.0.0.99", new DummyWebSocket());
            };

            var apiKeyOptions = Options.Create(new ApiKeyAuthenticationOptions { ApiKey = "test-token" });
            var mockOptionsMonitor = new TestOptionsMonitor<ApiKeyAuthenticationOptions>(apiKeyOptions.Value);
            var inMemoryConfig = new ConfigurationBuilder().AddInMemoryCollection().Build();

            var service = new NodeAdoptionService(
                fakeBootstrapper,
                connectionManager,
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                mockOptionsMonitor,
                inMemoryConfig,
                NullLogger<NodeAdoptionService>.Instance
            );

            var request = new AdoptNodeRequest(
                HostId: hostId,
                Hostname: "srv-homelab-01",
                TargetHost: "10.0.0.99",
                Port: 22,
                Username: "root",
                Password: "password123"
            );

            var emittedSteps = new List<AdoptionStepEvent>();
            var response = await service.AdoptNodeAsync(request, ev => emittedSteps.Add(ev));

            Assert.True(response.Success);
            Assert.Equal(hostId, response.HostId);
            Assert.Contains(response.Steps, s => s.StepKey == "SSH_CONNECTING" && s.Status == AdoptionStepStatus.Completed);
            Assert.Contains(response.Steps, s => s.StepKey == "ARCH_DETECTED" && s.Status == AdoptionStepStatus.Completed);
            Assert.Contains(response.Steps, s => s.StepKey == "BINARY_STREAMING" && s.Status == AdoptionStepStatus.Completed);
            Assert.Contains(response.Steps, s => s.StepKey == "SERVICE_STARTING" && s.Status == AdoptionStepStatus.Completed);
            Assert.Contains(response.Steps, s => s.StepKey == "HANDSHAKE_VERIFIED" && s.Status == AdoptionStepStatus.Completed);

            // Verify DB state
            using var verifyScope = serviceProvider.CreateScope();
            var dbVerify = verifyScope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var savedHost = await dbVerify.Hosts.FindAsync(hostId);
            Assert.NotNull(savedHost);
            Assert.True(savedHost.Agent.Installed);
            Assert.NotNull(savedHost.Agent.LastSeenAt);
        }
        finally
        {
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }

    [Fact]
    public async Task AdoptNodeAsync_SshFailure_ReturnsFailureResponse()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"cp-test-adopt-fail-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<ControlPlaneDbContext>(opt =>
                opt.UseSqlite($"Data Source={tempDb}").UseSnakeCaseNamingConvention());
            var serviceProvider = services.BuildServiceProvider();

            using (var initScope = serviceProvider.CreateScope())
            {
                var db = initScope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            var connectionManager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
            var fakeBootstrapper = new FakeSshBootstrapper
            {
                ShouldThrowOnProbe = true
            };

            var apiKeyOptions = Options.Create(new ApiKeyAuthenticationOptions { ApiKey = "test-token" });
            var mockOptionsMonitor = new TestOptionsMonitor<ApiKeyAuthenticationOptions>(apiKeyOptions.Value);
            var inMemoryConfig = new ConfigurationBuilder().AddInMemoryCollection().Build();

            var service = new NodeAdoptionService(
                fakeBootstrapper,
                connectionManager,
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                mockOptionsMonitor,
                inMemoryConfig,
                NullLogger<NodeAdoptionService>.Instance
            );

            var request = new AdoptNodeRequest(
                HostId: null,
                Hostname: "srv-fail",
                TargetHost: "192.168.1.50",
                Port: 22,
                Username: "root",
                Password: "bad-password"
            );

            var response = await service.AdoptNodeAsync(request);

            Assert.False(response.Success);
            Assert.Contains(response.Steps, s => s.StepKey == "SSH_CONNECTING" && s.Status == AdoptionStepStatus.Failed);
        }
        finally
        {
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }

    private class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;

        public T CurrentValue { get; }
    }
}
