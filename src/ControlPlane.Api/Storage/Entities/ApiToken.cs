namespace ControlPlane.Api.Storage.Entities;

public class ApiToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public string TokenPrefix { get; set; } = string.Empty;

    public string Role { get; set; } = "Admin";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public bool IsRevoked { get; set; }
}
