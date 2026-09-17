using System.Collections.Concurrent;
using System.Text;
using ControlPlane.Api.Features.Agents.Models;

namespace ControlPlane.Api.Features.Agents;

public class AgentCommandExecutor : IAgentCommandExecutor
{
    private class CommandExecutionState
    {
        public TaskCompletionSource<AgentCommandResult> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public StringBuilder StandardOutput { get; } = new();
        public StringBuilder StandardError { get; } = new();
    }

    private readonly ConcurrentDictionary<Guid, CommandExecutionState> _activeCommands = new();
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

        var state = new CommandExecutionState();
        _activeCommands[jobId] = state;

        using var ctr = cancellationToken.Register(() =>
        {
            if (_activeCommands.TryRemove(jobId, out var removedState))
            {
                removedState.Tcs.TrySetCanceled(cancellationToken);
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
            return await state.Tcs.Task;
        }
        catch (OperationCanceledException)
        {
            string stdoutStr, stderrStr;
            lock (state.StandardOutput) { stdoutStr = state.StandardOutput.ToString(); }
            lock (state.StandardError) { stderrStr = state.StandardError.ToString(); }
            return new AgentCommandResult(false, -1, "Command execution was canceled or timed out.", stdoutStr, stderrStr);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error executing command {Command} for job {JobId}", command, jobId);
            string stdoutStr, stderrStr;
            lock (state.StandardOutput) { stdoutStr = state.StandardOutput.ToString(); }
            lock (state.StandardError) { stderrStr = state.StandardError.ToString(); }
            return new AgentCommandResult(false, -1, ex.Message, stdoutStr, stderrStr);
        }
        finally
        {
            _activeCommands.TryRemove(jobId, out _);
        }
    }

    public void NotifyFrame(Guid hostId, AgentFrameData frame)
    {
        if (!_activeCommands.TryGetValue(frame.JobId, out var state))
        {
            return;
        }

        if (frame.StreamType == "stdout")
        {
            lock (state.StandardOutput)
            {
                state.StandardOutput.AppendLine(frame.LogLine);
            }
        }
        else if (frame.StreamType == "stderr")
        {
            lock (state.StandardError)
            {
                state.StandardError.AppendLine(frame.LogLine);
            }
        }
        else if (frame.StreamType == "system")
        {
            string stdoutStr, stderrStr;
            lock (state.StandardOutput) { stdoutStr = state.StandardOutput.ToString(); }
            lock (state.StandardError) { stderrStr = state.StandardError.ToString(); }

            if (frame.LogLine.Contains("completed successfully", StringComparison.OrdinalIgnoreCase))
            {
                state.Tcs.TrySetResult(new AgentCommandResult(true, 0, null, stdoutStr, stderrStr));
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

                state.Tcs.TrySetResult(new AgentCommandResult(false, exitCode, frame.LogLine, stdoutStr, stderrStr));
                _activeCommands.TryRemove(frame.JobId, out _);
            }
            else if (frame.LogLine.Contains("Process error", StringComparison.OrdinalIgnoreCase) ||
                     frame.LogLine.Contains("Failed to start process", StringComparison.OrdinalIgnoreCase) ||
                     frame.LogLine.Contains("Failed to acquire", StringComparison.OrdinalIgnoreCase))
            {
                state.Tcs.TrySetResult(new AgentCommandResult(false, -1, frame.LogLine, stdoutStr, stderrStr));
                _activeCommands.TryRemove(frame.JobId, out _);
            }
        }
    }
}
