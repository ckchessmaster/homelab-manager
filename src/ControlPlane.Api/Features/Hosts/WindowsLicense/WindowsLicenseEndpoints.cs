using System.Security.Claims;
using ControlPlane.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.Hosts.WindowsLicense;

public static class WindowsLicenseEndpoints
{
    public static IEndpointRouteBuilder MapWindowsLicenseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/hosts/{id:guid}/windows-license")
            .WithTags("Windows License")
            .RequireAuthorization(AuthConstants.RequireViewer);

        group.MapGet("/", async (
            Guid id,
            [FromQuery] bool? refresh,
            IWindowsLicenseService licenseService,
            CancellationToken ct) =>
        {
            var license = await licenseService.GetWindowsLicenseAsync(id, refresh ?? false, ct);
            return license == null
                ? Results.NotFound(new { message = $"Windows license information for host ID '{id}' was not found or host is not Windows." })
                : Results.Ok(license);
        })
        .WithName("GetWindowsLicense")
        .WithSummary("Retrieve Windows Server evaluation/activation license status and remaining rearms");

        group.MapPost("/rearm", async (
            Guid id,
            [FromBody] RearmWindowsLicenseRequest? request,
            IWindowsLicenseService licenseService,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var initiatedBy = user.Identity?.Name ?? "Operator";
            var result = await licenseService.RearmWindowsLicenseAsync(
                id,
                request?.RebootAfter ?? false,
                initiatedBy,
                ct);

            if (!result.Success && result.JobId == Guid.Empty)
            {
                return Results.BadRequest(new { message = result.Message });
            }

            if (!result.Success)
            {
                return Results.Conflict(new { message = result.Message, jobId = result.JobId });
            }

            return Results.Accepted($"/api/v1/jobs/{result.JobId}", result);
        })
        .WithName("RearmWindowsLicense")
        .WithSummary("Initiate a Temporal workflow to execute slmgr.vbs /rearm and optionally reboot the host")
        .RequireAuthorization(AuthConstants.RequireOperator);

        return endpoints;
    }
}
