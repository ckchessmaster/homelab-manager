using ControlPlane.Api.Features.Orchestration;

namespace ControlPlane.Api.Features.Orchestration.Pipelines;

/// <summary>
/// Human-readable summary of a single step within a pipeline profile.
/// </summary>
public record PipelineStepSummary(
    string Name,
    string Description
);

/// <summary>
/// Defines a named, modular pipeline profile with metadata and step definitions.
/// </summary>
public class PipelineProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public required IReadOnlyList<string> CompatibleTargetTypes { get; init; }
    public required IReadOnlyList<PipelineStepSummary> Steps { get; init; }
    public required Func<IServiceProvider, IEnumerable<IJobStep>> StepFactory { get; init; }
}
