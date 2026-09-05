using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.Adapters.Proxmox;

public static class SnapshotRetentionEndpoints
{
    public static RouteGroupBuilder MapSnapshotRetentionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/adapters/proxmox/snapshots")
            .WithTags("Proxmox Snapshots")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] Guid? hostId,
            ISnapshotRetentionService retentionService,
            CancellationToken ct) =>
        {
            var snapshots = await retentionService.GetSnapshotsAsync(hostId, ct);
            return Results.Ok(snapshots);
        })
        .WithName("ListProxmoxSnapshots")
        .WithSummary("List hypervisor snapshots with retention and expiration status");

        group.MapPost("/prune", async (
            [FromBody] SnapshotPruneRequest? request,
            ISnapshotRetentionService retentionService,
            CancellationToken ct) =>
        {
            var req = request ?? new SnapshotPruneRequest();
            var result = await retentionService.PruneExpiredSnapshotsAsync(req.HostId, req.DryRun, ct);
            return Results.Ok(result);
        })
        .WithName("PruneProxmoxSnapshots")
        .WithSummary("Trigger manual pruning sweep for expired Proxmox safety snapshots");

        group.MapDelete("/{snapshotName}", async (
            string snapshotName,
            [FromQuery] Guid hostId,
            ISnapshotRetentionService retentionService,
            CancellationToken ct) =>
        {
            if (hostId == Guid.Empty)
            {
                return Results.BadRequest(new { message = "Valid 'hostId' query parameter is required." });
            }

            try
            {
                await retentionService.DeleteSnapshotAsync(hostId, snapshotName, ct);
                return Results.Ok(new { message = $"Snapshot '{snapshotName}' deleted successfully." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("DeleteProxmoxSnapshot")
        .WithSummary("Manually delete a specific Proxmox hypervisor snapshot");

        return group;
    }
}
