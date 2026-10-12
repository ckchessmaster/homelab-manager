using System.Collections.Concurrent;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Messaging;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Jobs;
using ControlPlane.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ControlPlane.Api.Tests;

public class ActiveActiveAgentBusTests
{
    private class TestMessageBus : IAgentMessageBus
    {
        public string PodId { get; }
        private readonly TestMessageBusBroker _broker;

        public event Func<AgentPresencePayload, Task>? OnPresenceReceived;
        public event Func<AgentCommandPayload, Task>? OnCommandReceived;
        public event Func<JobLogPayload, Task>? OnJobLogReceived;
        public event Func<JobStatusPayload, Task>? OnJobStatusReceived;

        public TestMessageBus(string podId, TestMessageBusBroker broker)
        {
            PodId = podId;
            _broker = broker;
            _broker.Register(this);
        }

        public async Task PublishPresenceAsync(Guid hostId, string state, CancellationToken ct = default)
        {
            await _broker.BroadcastPresenceAsync(PodId, new AgentPresencePayload(hostId, state, PodId, DateTimeOffset.UtcNow));
        }

        public async Task PublishCommandAsync(Guid targetHostId, AgentCommandEnvelope command, CancellationToken ct = default)
        {
            await _broker.BroadcastCommandAsync(PodId, new AgentCommandPayload(targetHostId, command, PodId));
        }

        public async Task PublishJobLogAsync(Guid jobId, long sequenceId, string streamType, string logLine, DateTimeOffset timestamp, CancellationToken ct = default)
        {
            await _broker.BroadcastJobLogAsync(PodId, new JobLogPayload(jobId, sequenceId, streamType, logLine, timestamp));
        }

        public async Task PublishJobStatusAsync(Guid jobId, string status, string? activeStep, CancellationToken ct = default)
        {
            await _broker.BroadcastJobStatusAsync(PodId, new JobStatusPayload(jobId, status, activeStep));
        }

        internal async Task ReceivePresenceAsync(AgentPresencePayload payload)
        {
            if (OnPresenceReceived != null) await OnPresenceReceived.Invoke(payload);
        }

        internal async Task ReceiveCommandAsync(AgentCommandPayload payload)
        {
            if (OnCommandReceived != null) await OnCommandReceived.Invoke(payload);
        }

        internal async Task ReceiveJobLogAsync(JobLogPayload payload)
        {
            if (OnJobLogReceived != null) await OnJobLogReceived.Invoke(payload);
        }

        internal async Task ReceiveJobStatusAsync(JobStatusPayload payload)
        {
            if (OnJobStatusReceived != null) await OnJobStatusReceived.Invoke(payload);
        }
    }

    private class TestMessageBusBroker
    {
        private readonly List<TestMessageBus> _buses = new();

        public void Register(TestMessageBus bus) => _buses.Add(bus);

        public async Task BroadcastPresenceAsync(string originPod, AgentPresencePayload payload)
        {
            foreach (var b in _buses.Where(b => b.PodId != originPod))
            {
                await b.ReceivePresenceAsync(payload);
            }
        }

        public async Task BroadcastCommandAsync(string originPod, AgentCommandPayload payload)
        {
            foreach (var b in _buses.Where(b => b.PodId != originPod))
            {
                await b.ReceiveCommandAsync(payload);
            }
        }

        public async Task BroadcastJobLogAsync(string originPod, JobLogPayload payload)
        {
            foreach (var b in _buses.Where(b => b.PodId != originPod))
            {
                await b.ReceiveJobLogAsync(payload);
            }
        }

        public async Task BroadcastJobStatusAsync(string originPod, JobStatusPayload payload)
        {
            foreach (var b in _buses.Where(b => b.PodId != originPod))
            {
                await b.ReceiveJobStatusAsync(payload);
            }
        }
    }

    private class RecordingJobClient : IJobClient
    {
        public List<(Guid JobId, long Seq, string Stream, string Line)> Logs { get; } = new();
        public List<(Guid JobId, string Status, string? Step)> Statuses { get; } = new();

        public Task ReceiveLogLine(Guid jobId, long sequenceId, string streamType, string logLine, DateTimeOffset timestamp)
        {
            Logs.Add((jobId, sequenceId, streamType, logLine));
            return Task.CompletedTask;
        }

        public Task JobStatusChanged(Guid jobId, string status, string? activeStep)
        {
            Statuses.Add((jobId, status, activeStep));
            return Task.CompletedTask;
        }
    }

    private class RecordingHubClients : IHubClients<IJobClient>
    {
        private readonly RecordingJobClient _client;

        public RecordingHubClients(RecordingJobClient client)
        {
            _client = client;
        }

        public IJobClient All => _client;
        public IJobClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => _client;
        public IJobClient Client(string connectionId) => _client;
        public IJobClient Clients(IReadOnlyList<string> connectionIds) => _client;
        public IJobClient Group(string groupName) => _client;
        public IJobClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _client;
        public IJobClient Groups(IReadOnlyList<string> groupNames) => _client;
        public IJobClient User(string userId) => _client;
        public IJobClient Users(IReadOnlyList<string> userIds) => _client;
    }

    private class RecordingHubContext : IHubContext<JobLogHub, IJobClient>
    {
        public RecordingJobClient Client { get; } = new();
        public IHubClients<IJobClient> Clients { get; }
        public IGroupManager Groups { get; } = null!;

        public RecordingHubContext()
        {
            Clients = new RecordingHubClients(Client);
        }
    }

    [Fact]
    public void MultiPod_PresenceSynchronizedAcrossInstances()
    {
        var broker = new TestMessageBusBroker();
        var busA = new TestMessageBus("pod-a", broker);
        var busB = new TestMessageBus("pod-b", broker);

        var managerA = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance, busA);
        var managerB = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance, busB);

        var hostId = Guid.NewGuid();

        // Register simulated agent on Pod A
        managerA.RegisterSimulated(hostId, "node-1");

        // Verify Pod A sees host online locally
        Assert.True(managerA.IsOnline(hostId));

        // Verify Pod B sees host online remotely through message bus
        Assert.True(managerB.IsOnline(hostId));
        Assert.Contains(hostId, managerB.GetOnlineHostIds());

        // Unregister on Pod A
        managerA.Unregister(hostId);

        // Verify Pod B now reflects host offline
        Assert.False(managerB.IsOnline(hostId));
        Assert.DoesNotContain(hostId, managerB.GetOnlineHostIds());
    }

    [Fact]
    public async Task MultiPod_CommandForwardedFromPeerPodToOwningPod()
    {
        var broker = new TestMessageBusBroker();
        var busA = new TestMessageBus("pod-a", broker);
        var busB = new TestMessageBus("pod-b", broker);

        var managerA = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance, busA);
        var managerB = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance, busB);

        var hostId = Guid.NewGuid();

        // Agent is connected to Pod A
        managerA.RegisterSimulated(hostId, "node-1");

        // An operator sends a command to Pod B (where the agent has no local socket)
        var command = new AgentCommandEnvelope
        {
            Type = "EXECUTE_COMMAND",
            JobId = Guid.NewGuid(),
            Command = "hostname",
            Args = Array.Empty<string>()
        };

        var result = await managerB.SendCommandAsync(hostId, command);

        // Should return true because it forwarded to the owning pod via message bus
        Assert.True(result);
    }

    [Fact]
    public async Task MultiPod_RebootCommencingWaitersUnblockedAcrossPods()
    {
        var broker = new TestMessageBusBroker();
        var busA = new TestMessageBus("pod-a", broker);
        var busB = new TestMessageBus("pod-b", broker);

        var managerA = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance, busA);
        var managerB = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance, busB);

        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid().ToString();

        // Pod B awaits reboot commencing acknowledgment
        var waitTask = managerB.WaitForRebootCommencingAsync(hostId, TimeSpan.FromSeconds(5));

        // Agent connects to Pod A and notifies reboot commencing
        managerA.NotifyRebootCommencing(hostId, jobId);

        // Pod B's waiter should unblock promptly
        var commenced = await waitTask;
        Assert.True(commenced);
    }

    [Fact]
    public async Task MultiPod_JobLogHubBridgeBroadcastsRemoteFrames()
    {
        var broker = new TestMessageBusBroker();
        var busA = new TestMessageBus("pod-a", broker);
        var busB = new TestMessageBus("pod-b", broker);

        var hubContext = new RecordingHubContext();

        var bridgeB = new JobLogHubBridge(busB, hubContext, NullLogger<JobLogHubBridge>.Instance);
        await bridgeB.StartAsync(CancellationToken.None);

        var jobId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;

        // Pod A publishes a job log frame
        await busA.PublishJobLogAsync(jobId, 1, "stdout", "System upgrade in progress", timestamp);

        // Pod B SignalR clients should receive the log line
        Assert.Single(hubContext.Client.Logs);
        Assert.Equal("System upgrade in progress", hubContext.Client.Logs[0].Line);

        // Pod A publishes job status changed
        await busA.PublishJobStatusAsync(jobId, "Completed", "Done");

        Assert.Single(hubContext.Client.Statuses);
        Assert.Equal("Completed", hubContext.Client.Statuses[0].Status);
    }

    [Fact]
    public void MultiPod_SyncRemotePresenceSeedsColdStartedPod()
    {
        var manager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var hostId1 = Guid.NewGuid();
        var hostId2 = Guid.NewGuid();

        var recentTime = DateTimeOffset.UtcNow.AddSeconds(-10);
        var staleTime = DateTimeOffset.UtcNow.AddSeconds(-90);

        manager.SyncRemotePresence(new[]
        {
            (hostId1, recentTime),
            (hostId2, staleTime)
        });

        // hostId1 was seen 10s ago (< 45s threshold) -> online
        Assert.True(manager.IsOnline(hostId1));

        // hostId2 was seen 90s ago (> 45s threshold) -> offline
        Assert.False(manager.IsOnline(hostId2));
    }

    [Fact]
    public async Task MultiPod_AgentCommandExecutor_ReceivesRemoteFrames_AndResolvesCommand()
    {
        var broker = new TestMessageBusBroker();
        var busA = new TestMessageBus("pod-a", broker);
        var busB = new TestMessageBus("pod-b", broker);

        var managerA = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance, busA);
        var managerB = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance, busB);

        var executorA = new AgentCommandExecutor(managerA, NullLogger<AgentCommandExecutor>.Instance, null, busA);

        var hostId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        // Agent is connected to Pod B
        managerB.RegisterSimulated(hostId, "worker-node");

        // Manager A knows host is online via bus
        managerA.SyncRemotePresence(new[] { (hostId, DateTimeOffset.UtcNow) });

        // Executor A (on Pod A) dispatches command
        var execTask = executorA.ExecuteCommandAsync(hostId, jobId, "apt", new[] { "upgrade", "-y" }, CancellationToken.None);

        // Remote Pod B publishes stdout log from agent
        await busB.PublishJobLogAsync(jobId, 1, "stdout", "Upgraded libssl3", DateTimeOffset.UtcNow);

        // Remote Pod B publishes system completion frame
        await busB.PublishJobLogAsync(jobId, 2, "system", "Command completed successfully with code 0", DateTimeOffset.UtcNow);

        // Executor A should successfully resolve without timing out!
        var result = await execTask;
        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Upgraded libssl3", result.StandardOutput);
    }
}
