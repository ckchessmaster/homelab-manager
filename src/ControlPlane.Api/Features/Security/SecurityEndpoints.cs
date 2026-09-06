using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ControlPlane.Api.Features.Security;

public static class SecurityEndpoints
{
    public static IEndpointRouteBuilder MapSecurityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/security")
            .WithTags("Security");

        group.MapGet("/status", (ISecurityKeyProvider keyProvider, ISecretEncryptionService encryptionService) =>
        {
            var testSecret = "healthcheck-" + Guid.NewGuid();
            var encrypted = encryptionService.Encrypt(testSecret);
            var decrypted = encryptionService.Decrypt(encrypted);
            var isHealthy = (decrypted == testSecret);

            return Results.Ok(new SecurityStatusDto(
                Algorithm: "AES-256-GCM",
                EnvelopeVersion: "v1",
                KeySource: keyProvider.KeySource,
                KeyFilePath: keyProvider.KeyFilePath,
                IsHealthy: isHealthy,
                Timestamp: DateTimeOffset.UtcNow
            ));
        }).RequireAuthorization("RequireAdmin");

        return app;
    }
}

public record SecurityStatusDto(
    string Algorithm,
    string EnvelopeVersion,
    string KeySource,
    string? KeyFilePath,
    bool IsHealthy,
    DateTimeOffset Timestamp
);
