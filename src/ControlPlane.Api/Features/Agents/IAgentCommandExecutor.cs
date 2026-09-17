using ControlPlane.Api.Features.Agents.Models;

namespace ControlPlane.Api.Features.Agents;

public record AgentCommandResult(
    bool Success,
    int ExitCode,
    string? ErrorMessage,
    string? StandardOutput = null,
    string? StandardError = null);

public interface IAgentCommandExecutor
{
    Task<AgentCommandResult> ExecuteCommandAsync(
        Guid hostId,
        Guid jobId,
        string command,
        string[] args,
        CancellationToken cancellationToken = default);

    Task<AgentCommandResult> ExecuteCommandAsync(
        Guid hostId,
        string command,
        string[] args,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(hostId, Guid.NewGuid(), command, args, cancellationToken);

    void NotifyFrame(Guid hostId, AgentFrameData frame);
}
