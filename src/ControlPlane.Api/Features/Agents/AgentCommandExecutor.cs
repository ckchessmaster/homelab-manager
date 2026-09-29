using System.Collections.Concurrent;
using System.Text;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Jobs;

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
    private readonly IServiceProvider? _serviceProvider;

    public AgentCommandExecutor(
        AgentConnectionManager connectionManager,
        ILogger<AgentCommandExecutor> logger,
        IServiceProvider? serviceProvider = null)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<AgentCommandResult> ExecuteCommandAsync(
        Guid hostId,
        Guid jobId,
        string command,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        var session = _connectionManager.GetSession(hostId);
        if (session != null && session.IsSimulated)
        {
            return await ExecuteSimulatedCommandAsync(hostId, jobId, command, args, cancellationToken);
        }

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

    private async Task<AgentCommandResult> ExecuteSimulatedCommandAsync(
        Guid hostId,
        Guid jobId,
        string command,
        string[] args,
        CancellationToken ct)
    {
        var consumer = _serviceProvider != null
            ? (IStepLogConsumer?)_serviceProvider.GetService(typeof(IStepLogConsumer))
            : null;

        var cmdLine = $"{command} {string.Join(" ", args)}".Trim();
        var simulatedLines = GetSimulatedLines(cmdLine);

        long seq = 1;
        foreach (var line in simulatedLines)
        {
            if (ct.IsCancellationRequested) break;

            var frame = new AgentFrameData
            {
                JobId = jobId,
                SequenceId = seq++,
                StreamType = "stdout",
                LogLine = line,
                Timestamp = DateTimeOffset.UtcNow
            };

            if (consumer != null)
            {
                await consumer.ConsumeFrameAsync(hostId, frame, ct);
            }
            await Task.Delay(100, ct);
        }

        var completedFrame = new AgentFrameData
        {
            JobId = jobId,
            SequenceId = seq++,
            StreamType = "system",
            LogLine = "Command completed successfully with code 0",
            Timestamp = DateTimeOffset.UtcNow
        };

        if (consumer != null)
        {
            await consumer.ConsumeFrameAsync(hostId, completedFrame, ct);
        }

        return new AgentCommandResult(true, 0, null, string.Join(Environment.NewLine, simulatedLines), "");
    }

    private static List<string> GetSimulatedLines(string cmdLine)
    {
        var lower = cmdLine.ToLowerInvariant();
        if (lower.Contains("apt") || lower.Contains("upgrade") || lower.Contains("update"))
        {
            return new List<string>
            {
                "[INFO] Checking repositories for package updates...",
                "Hit:1 http://archive.ubuntu.com/ubuntu noble InRelease",
                "Hit:2 http://archive.ubuntu.com/ubuntu noble-updates InRelease",
                "Hit:3 http://security.ubuntu.com/ubuntu noble-security InRelease",
                "Reading package lists... Done",
                "Building dependency tree... Done",
                "[INFO] 3 packages will be upgraded: libssl3, linux-firmware, systemd",
                "Preparing to unpack .../libssl3_3.0.13-0ubuntu3.4_amd64.deb ...",
                "Unpacking libssl3:amd64 (3.0.13-0ubuntu3.4) over (3.0.13-0ubuntu3.3) ...",
                "Setting up libssl3:amd64 (3.0.13-0ubuntu3.4) ...",
                "Setting up systemd (255.4-1ubuntu8.4) ...",
                "Processing triggers for man-db (2.12.0-4build2) ...",
                "[SUCCESS] Package upgrade batch completed successfully."
            };
        }

        if (lower.Contains("reboot"))
        {
            return new List<string>
            {
                "[INFO] Initiating graceful system reboot...",
                "[INFO] Stopping running services and flushing disk buffers...",
                "[INFO] Sending SIGTERM to active processes...",
                "[INFO] System restart commencing now."
            };
        }

        return new List<string>
        {
            $"[INFO] Executing simulated command: {cmdLine}",
            $"[INFO] Target node environment initialized.",
            $"[SUCCESS] Execution finished without warnings."
        };
    }
}
