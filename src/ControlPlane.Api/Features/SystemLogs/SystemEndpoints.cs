using System.Diagnostics;
using System.Runtime.InteropServices;
using ControlPlane.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.SystemLogs;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/system")
            .WithTags("System & Diagnostics");

        group.MapGet("/logs", (
            ISystemLogBuffer buffer,
            [FromQuery] string? level,
            [FromQuery] string? minLevel,
            [FromQuery] string? category,
            [FromQuery] string? search,
            [FromQuery] long? sinceId,
            [FromQuery] int limit = 200,
            [FromQuery] bool tail = true) =>
        {
            var response = buffer.Query(level, minLevel, category, search, sinceId, limit, tail);
            return Results.Ok(response);
        })
        .WithName("GetSystemLogs")
        .WithSummary("Retrieve in-memory backend application logs with optional level, category, search, and sequence filters")
        .RequireAuthorization(AuthConstants.RequireViewer);

        group.MapGet("/logs/stats", (ISystemLogBuffer buffer) =>
        {
            return Results.Ok(buffer.GetStats());
        })
        .WithName("GetSystemLogStats")
        .WithSummary("Retrieve count summaries for system logs")
        .RequireAuthorization(AuthConstants.RequireViewer);

        group.MapDelete("/logs", (ISystemLogBuffer buffer) =>
        {
            buffer.Clear();
            return Results.Ok(new { message = "System log buffer cleared successfully." });
        })
        .WithName("ClearSystemLogs")
        .WithSummary("Clear the in-memory system log buffer")
        .RequireAuthorization(AuthConstants.RequireOperator);

        group.MapGet("/info", () =>
        {
            using var currentProcess = Process.GetCurrentProcess();
            var uptime = DateTime.UtcNow - currentProcess.StartTime.ToUniversalTime();

            var info = new SystemInfoDto(
                OsDescription: RuntimeInformation.OSDescription,
                FrameworkDescription: RuntimeInformation.FrameworkDescription,
                MachineName: Environment.MachineName,
                ProcessorCount: Environment.ProcessorCount,
                Uptime: uptime,
                WorkingSetBytes: currentProcess.WorkingSet64,
                ServerTimeUtc: DateTimeOffset.UtcNow,
                EnvironmentName: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
            );

            return Results.Ok(info);
        })
        .WithName("GetSystemInfo")
        .WithSummary("Retrieve host and runtime diagnostic information")
        .RequireAuthorization(AuthConstants.RequireViewer);

        return app;
    }
}
