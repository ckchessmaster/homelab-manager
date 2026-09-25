using ControlPlane.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Features.Security.Tokens;

public static class ApiTokenEndpoints
{
    public static IEndpointRouteBuilder MapApiTokenEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/tokens")
            .WithTags("Personal Access Tokens")
            .RequireAuthorization(AuthConstants.RequireAdmin);

        group.MapGet("/", async (ApiTokenService tokenService, CancellationToken ct) =>
        {
            var tokens = await tokenService.ListTokensAsync(ct);
            return Results.Ok(tokens);
        })
        .WithName("ListApiTokens")
        .WithSummary("List all Personal Access Tokens and active API keys");

        group.MapPost("/", async (
            [FromBody] CreateApiTokenRequest request,
            ApiTokenService tokenService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "Token name is required." });
            }

            var created = await tokenService.CreateTokenAsync(request, ct);
            return Results.Created($"/api/v1/tokens/{created.Id}", created);
        })
        .WithName("CreateApiToken")
        .WithSummary("Generate a new Personal Access Token with assigned role and optional expiration");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ApiTokenService tokenService,
            CancellationToken ct) =>
        {
            var success = await tokenService.DeleteTokenAsync(id, ct);
            return success ? Results.NoContent() : Results.NotFound(new { message = "Token not found." });
        })
        .WithName("RevokeApiToken")
        .WithSummary("Revoke and remove a Personal Access Token");

        return routes;
    }
}
