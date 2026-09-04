using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ControlPlane.Api.Features.Agents;

public static class AgentManagementEndpoints
{
    public static IEndpointRouteBuilder MapAgentManagementEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous binary download endpoint for agents to fetch static binaries
        app.MapGet("/api/v1/agents/binaries/{arch}", (string arch, AgentBinaryService binaryService) =>
        {
            var binaryPath = binaryService.GetBinaryPath(arch);
            if (string.IsNullOrEmpty(binaryPath) || !File.Exists(binaryPath))
            {
                return Results.NotFound(new { message = $"Agent binary for architecture '{arch}' not found." });
            }

            var filename = Path.GetFileName(binaryPath);
            return Results.File(binaryPath, "application/octet-stream", fileDownloadName: filename, enableRangeProcessing: true);
        }).AllowAnonymous();

        var group = app.MapGroup("/api/v1/agents")
            .RequireAuthorization();

        group.MapGet("/version-info", async (MassAgentUpdateService service, CancellationToken ct) =>
        {
            var info = await service.GetVersionInfoAsync(ct);
            return Results.Ok(info);
        });

        group.MapPost("/mass-update", async (
            MassUpdateRequest request,
            HttpRequest httpRequest,
            MassAgentUpdateService service,
            CancellationToken ct) =>
        {
            var serverBaseUrl = $"{httpRequest.Scheme}://{httpRequest.Host}";
            var result = await service.TriggerMassUpdateAsync(request, serverBaseUrl, ct);
            return Results.Accepted($"/api/v1/agents/mass-update/{result.BatchId}", result);
        });

        group.MapGet("/mass-update/{batchId:guid}", (Guid batchId, MassAgentUpdateService service) =>
        {
            var batch = service.GetBatchStatus(batchId);
            return batch != null
                ? Results.Ok(batch)
                : Results.NotFound(new { message = $"Mass update batch '{batchId}' not found." });
        });

        return app;
    }
}
