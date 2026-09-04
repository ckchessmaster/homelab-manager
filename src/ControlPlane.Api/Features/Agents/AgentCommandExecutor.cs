using System.Collections.Concurrent;
using ControlPlane.Api.Features.Agents.Models;

namespace ControlPlane.Api.Features.Agents;

public class AgentCommandExecutor : IAgentCommandExecutor
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<AgentCommandResult>> _activeCommands = new();
    private readonly AgentConnectionManager _connectionManager;
    private readonly ILogger<AgentCommandExecutor> _logger;

    public AgentCommandExecutor(
        AgentConnectionManager connectionManager,
        ILogger<AgentCommandExecutor> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<AgentCommandResult> ExecuteCommandAsync(
        Guid hostId,
        Guid jobId,
        string command,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        if (!_connectionManager.IsOnline(hostId))
        {
            return new AgentCommandResult(false, -1, "Target host agent is offline.");
        }

        var tcs = new TaskCompletionSource<AgentCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _activeCommands[jobId] = tcs;

        using var ctr = cancellationToken.Register(() =>
        {
            if (_activeCommands.TryRemove(jobId, out var removedTcs))
            {
                removedTcs.TrySetCanceled(cancellationToken);
            }
        });

        var envelope = new AgentCommandEnvelope
        {
            Type = "EXECUTE_COMMAND",
            JobId = jobId,
            Command = command,
            Args = args
        };

        var dispatched = await _connectionManager.SendCommandAsync(hostId, envelope, cancellationToken);
        if (!dispatched)
        {
            _activeCommands.TryRemove(jobId, out _);
            return new AgentCommandResult(false, -1, "Failed to dispatch command envelope to agent.");
        }

        try
        {
            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            return new AgentCommandResult(false, -1, "Command execution was canceled or timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error executing command {Command} for job {JobId}", command, jobId);
            return new AgentCommandResult(false, -1, ex.Message);
        }
        finally
        {
            _activeCommands.TryRemove(jobId, out _);
        }
    }

    public void NotifyFrame(Guid hostId, AgentFrameData frame)
    {
        if (!_activeCommands.TryGetValue(frame.JobId, out var tcs))
        {
            return;
        }

        if (frame.StreamType == "system")
        {
            if (frame.LogLine.Contains("completed successfully", StringComparison.OrdinalIgnoreCase))
            {
                tcs.TrySetResult(new AgentCommandResult(true, 0, null));
                _activeCommands.TryRemove(frame.JobId, out _);
            }
            else if (frame.LogLine.Contains("exited with code", StringComparison.OrdinalIgnoreCase))
            {
                var exitCode = 1;
                var idx = frame.LogLine.IndexOf("code ", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && int.TryParse(frame.LogLine[(idx + 5)..].Trim(), out var parsed))
                {
                    exitCode = parsed;
                }

                tcs.TrySetResult(new AgentCommandResult(false, exitCode, frame.LogLine));
                _activeCommands.TryRemove(frame.JobId, out _);
            }
            else if (frame.LogLine.Contains("Process error", StringComparison.OrdinalIgnoreCase) ||
                     frame.LogLine.Contains("Failed to start process", StringComparison.OrdinalIgnoreCase) ||
                     frame.LogLine.Contains("Failed to acquire", StringComparison.OrdinalIgnoreCase))
            {
                tcs.TrySetResult(new AgentCommandResult(false, -1, frame.LogLine));
                _activeCommands.TryRemove(frame.JobId, out _);
            }
        }
    }
}
