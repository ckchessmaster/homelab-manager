using ControlPlane.Api.Features.Agents.Models;

namespace ControlPlane.Api.Features.Agents;

public interface IAgentBinarySyncService
{
    Task<AgentBinaryStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<AgentBinarySyncResultDto> SyncBinariesAsync(bool force = false, CancellationToken cancellationToken = default);
}
