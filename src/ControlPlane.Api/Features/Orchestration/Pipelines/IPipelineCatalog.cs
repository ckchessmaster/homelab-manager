namespace ControlPlane.Api.Features.Orchestration.Pipelines;

public interface IPipelineCatalog
{
    /// <summary>
    /// Gets all registered pipeline profiles available in the system.
    /// </summary>
    IReadOnlyList<PipelineProfile> GetProfiles();

    /// <summary>
    /// Gets a single pipeline profile by its identifier.
    /// </summary>
    PipelineProfile? GetProfile(string id);

    /// <summary>
    /// Determines the recommended pipeline profile for a given target host.
    /// </summary>
    string GetRecommendedProfileId(string? targetType, string? osFamily);

    /// <summary>
    /// Constructs a DAG execution pipeline for the given profile ID.
    /// </summary>
    DagExecutionPipeline BuildPipeline(string pipelineId, IServiceProvider serviceProvider);
}
