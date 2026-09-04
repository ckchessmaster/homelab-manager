using ControlPlane.Api.Features.Agents.Models;

namespace ControlPlane.Api.Features.Jobs;

public interface IStepLogConsumer
{
    Task ConsumeFrameAsync(Guid hostId, AgentFrameData frame, CancellationToken cancellationToken = default);
}
