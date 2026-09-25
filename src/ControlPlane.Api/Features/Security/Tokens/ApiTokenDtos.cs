namespace ControlPlane.Api.Features.Security.Tokens;

public record CreateApiTokenRequest(
    string Name,
    string Role = "Admin",
    int? ExpiresInDays = null
);

public record ApiTokenCreatedDto(
    Guid Id,
    string Name,
    string Token,
    string TokenPrefix,
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt
);

public record ApiTokenSummaryDto(
    Guid Id,
    string Name,
    string TokenPrefix,
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    bool IsRevoked,
    bool IsExpired
);
