using System.Text.Json;
using ControlPlane.Api.Features.Agents.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ControlPlane.Api.Features.Agents.Messaging;

public class PostgresAgentMessageBus : IAgentMessageBus, IHostedService
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresAgentMessageBus> _logger;
    private readonly NpgsqlDataSource _dataSource;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    public string PodId { get; }

    public event Func<AgentPresencePayload, Task>? OnPresenceReceived;
    public event Func<AgentCommandPayload, Task>? OnCommandReceived;
    public event Func<JobLogPayload, Task>? OnJobLogReceived;
    public event Func<JobStatusPayload, Task>? OnJobStatusReceived;

    public PostgresAgentMessageBus(
        string connectionString,
        ILogger<PostgresAgentMessageBus> logger)
    {
        _connectionString = connectionString;
        _logger = logger;

        var podName = Environment.GetEnvironmentVariable("POD_NAME")
            ?? Environment.GetEnvironmentVariable("HOSTNAME");
        PodId = !string.IsNullOrWhiteSpace(podName)
            ? podName
            : $"{Environment.MachineName}_{Guid.NewGuid():N}"[..12];

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
        _dataSource = dataSourceBuilder.Build();
    }

    public async Task PublishPresenceAsync(Guid hostId, string state, CancellationToken ct = default)
    {
        var payload = new AgentPresencePayload(hostId, state, PodId, DateTimeOffset.UtcNow);
        var envelope = new InterPodEnvelope("Presence", PodId, JsonSerializer.Serialize(payload, _jsonOptions), DateTimeOffset.UtcNow);
        await SendNotifyAsync(envelope, ct);
    }

    public async Task PublishCommandAsync(Guid targetHostId, AgentCommandEnvelope command, CancellationToken ct = default)
    {
        var payload = new AgentCommandPayload(targetHostId, command, PodId);
        var envelope = new InterPodEnvelope("Command", PodId, JsonSerializer.Serialize(payload, _jsonOptions), DateTimeOffset.UtcNow);
        await SendNotifyAsync(envelope, ct);
    }

    public async Task PublishJobLogAsync(Guid jobId, long sequenceId, string streamType, string logLine, DateTimeOffset timestamp, CancellationToken ct = default)
    {
        // Guard against PostgreSQL 8000-byte notification limit
        var safeLine = logLine.Length > 6500 ? logLine[..6500] + " [truncated for bus]" : logLine;
        var payload = new JobLogPayload(jobId, sequenceId, streamType, safeLine, timestamp);
        var envelope = new InterPodEnvelope("JobLog", PodId, JsonSerializer.Serialize(payload, _jsonOptions), DateTimeOffset.UtcNow);
        await SendNotifyAsync(envelope, ct);
    }

    public async Task PublishJobStatusAsync(Guid jobId, string status, string? activeStep, CancellationToken ct = default)
    {
        var payload = new JobStatusPayload(jobId, status, activeStep);
        var envelope = new InterPodEnvelope("JobStatus", PodId, JsonSerializer.Serialize(payload, _jsonOptions), DateTimeOffset.UtcNow);
        await SendNotifyAsync(envelope, ct);
    }

    private async Task SendNotifyAsync(InterPodEnvelope envelope, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(envelope, _jsonOptions);
            await using var cmd = _dataSource.CreateCommand("SELECT pg_notify(@channel, @payload)");
            cmd.Parameters.AddWithValue("channel", AgentBusChannels.ControlPlaneBus);
            cmd.Parameters.AddWithValue("payload", json);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish inter-pod notification {Type} from pod {PodId}", envelope.Type, PodId);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting PostgreSQL inter-pod bus listener on pod {PodId}...", PodId);
        _listenerCts = new CancellationTokenSource();
        _listenerTask = ListenLoopAsync(_listenerCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping PostgreSQL inter-pod bus listener on pod {PodId}...", PodId);
        if (_listenerCts != null)
        {
            _listenerCts.Cancel();
            if (_listenerTask != null)
            {
                try
                {
                    await Task.WhenAny(_listenerTask, Task.Delay(2000, cancellationToken));
                }
                catch { }
            }
            _listenerCts.Dispose();
        }
        await _dataSource.DisposeAsync();
    }

    private async Task ListenLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync(stoppingToken);

                conn.Notification += async (sender, args) =>
                {
                    try
                    {
                        if (args.Channel == AgentBusChannels.ControlPlaneBus && !string.IsNullOrWhiteSpace(args.Payload))
                        {
                            await HandleIncomingPayloadAsync(args.Payload, stoppingToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error handling incoming inter-pod message");
                    }
                };

                await using (var cmd = new NpgsqlCommand($"LISTEN {AgentBusChannels.ControlPlaneBus}", conn))
                {
                    await cmd.ExecuteNonQueryAsync(stoppingToken);
                }

                _logger.LogInformation("Listening on PostgreSQL channel '{Channel}' (Pod: {PodId})", AgentBusChannels.ControlPlaneBus, PodId);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await conn.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PostgreSQL listener connection interrupted; reconnecting in 5s...");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleIncomingPayloadAsync(string rawJson, CancellationToken ct)
    {
        var envelope = JsonSerializer.Deserialize<InterPodEnvelope>(rawJson, _jsonOptions);
        if (envelope == null || envelope.OriginPodId == PodId)
        {
            // Ignore messages from this own pod to avoid feedback loops
            return;
        }

        switch (envelope.Type)
        {
            case "Presence":
                var presence = JsonSerializer.Deserialize<AgentPresencePayload>(envelope.PayloadJson, _jsonOptions);
                if (presence != null && OnPresenceReceived != null)
                {
                    await OnPresenceReceived.Invoke(presence);
                }
                break;

            case "Command":
                var cmd = JsonSerializer.Deserialize<AgentCommandPayload>(envelope.PayloadJson, _jsonOptions);
                if (cmd != null && OnCommandReceived != null)
                {
                    await OnCommandReceived.Invoke(cmd);
                }
                break;

            case "JobLog":
                var log = JsonSerializer.Deserialize<JobLogPayload>(envelope.PayloadJson, _jsonOptions);
                if (log != null && OnJobLogReceived != null)
                {
                    await OnJobLogReceived.Invoke(log);
                }
                break;

            case "JobStatus":
                var status = JsonSerializer.Deserialize<JobStatusPayload>(envelope.PayloadJson, _jsonOptions);
                if (status != null && OnJobStatusReceived != null)
                {
                    await OnJobStatusReceived.Invoke(status);
                }
                break;
        }
    }
}
