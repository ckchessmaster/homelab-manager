using System.Security.Cryptography;
using System.Text;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Features.Security.Tokens;

public class ApiTokenService
{
    private readonly ControlPlaneDbContext _db;
    private readonly ILogger<ApiTokenService> _logger;

    public ApiTokenService(ControlPlaneDbContext db, ILogger<ApiTokenService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApiTokenCreatedDto> CreateTokenAsync(CreateApiTokenRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Token name is required.", nameof(request));
        }

        var normalizedRole = request.Role?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedRole) ||
            (!normalizedRole.Equals("Admin", StringComparison.OrdinalIgnoreCase) &&
             !normalizedRole.Equals("Operator", StringComparison.OrdinalIgnoreCase) &&
             !normalizedRole.Equals("Viewer", StringComparison.OrdinalIgnoreCase)))
        {
            normalizedRole = "Admin";
        }

        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var hexToken = Convert.ToHexString(randomBytes).ToLowerInvariant();
        var rawToken = $"cp_pat_{hexToken}";
        var tokenPrefix = $"cp_pat_{hexToken.Substring(0, 8)}...";
        var tokenHash = HashToken(rawToken);

        DateTimeOffset? expiresAt = request.ExpiresInDays.HasValue && request.ExpiresInDays.Value > 0
            ? DateTimeOffset.UtcNow.AddDays(request.ExpiresInDays.Value)
            : null;

        var entity = new ApiToken
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            TokenHash = tokenHash,
            TokenPrefix = tokenPrefix,
            Role = normalizedRole,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            IsRevoked = false
        };

        _db.ApiTokens.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created new Personal Access Token '{Name}' (Role: {Role}, Prefix: {Prefix})",
            entity.Name, entity.Role, entity.TokenPrefix);

        return new ApiTokenCreatedDto(
            entity.Id,
            entity.Name,
            rawToken,
            entity.TokenPrefix,
            entity.Role,
            entity.CreatedAt,
            entity.ExpiresAt
        );
    }

    public async Task<List<ApiTokenSummaryDto>> ListTokensAsync(CancellationToken ct = default)
    {
        var tokens = await _db.ApiTokens
            .AsNoTracking()
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        return tokens
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new ApiTokenSummaryDto(
                t.Id,
                t.Name,
                t.TokenPrefix,
                t.Role,
                t.CreatedAt,
                t.ExpiresAt,
                t.LastUsedAt,
                t.IsRevoked,
                t.ExpiresAt.HasValue && t.ExpiresAt.Value < now
            )).ToList();
    }

    public async Task<bool> RevokeTokenAsync(Guid id, CancellationToken ct = default)
    {
        var token = await _db.ApiTokens.FindAsync(new object[] { id }, ct);
        if (token == null)
        {
            return false;
        }

        token.IsRevoked = true;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Revoked Personal Access Token '{Name}' ({Id})", token.Name, token.Id);
        return true;
    }

    public async Task<bool> DeleteTokenAsync(Guid id, CancellationToken ct = default)
    {
        var token = await _db.ApiTokens.FindAsync(new object[] { id }, ct);
        if (token == null)
        {
            return false;
        }

        _db.ApiTokens.Remove(token);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted Personal Access Token '{Name}' ({Id})", token.Name, token.Id);
        return true;
    }

    public async Task<ApiToken?> ValidateTokenAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var hash = HashToken(rawToken.Trim());
        var token = await _db.ApiTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && !t.IsRevoked, ct);

        if (token == null)
        {
            return null;
        }

        if (token.ExpiresAt.HasValue && token.ExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Personal Access Token '{Name}' has expired.", token.Name);
            return null;
        }

        // Update LastUsedAt timestamp
        token.LastUsedAt = DateTimeOffset.UtcNow;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update LastUsedAt for token '{Name}'.", token.Name);
        }

        return token;
    }

    public static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
