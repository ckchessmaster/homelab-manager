using ControlPlane.Api.Features.Agents.Models;

namespace ControlPlane.Api.Features.Agents;

public record AgentCommandResult(bool Success, int ExitCode, string? ErrorMessage);

public interface IAgentCommandExecutor
{
    Task<AgentCommandResult> ExecuteCommandAsync(
        Guid hostId,
        Guid jobId,
        string command,
        string[] args,
        CancellationToken cancellationToken = default);

    void NotifyFrame(Guid hostId, AgentFrameData frame);
}
