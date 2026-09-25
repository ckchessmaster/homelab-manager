using System.Security.Claims;
using ControlPlane.Api.Features.Security.Tokens;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Tests;

public class ApiTokenTests
{
    private (ControlPlaneDbContext Db, ApiTokenService Service, string TempDb) CreateService()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"cp-test-tokens-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContext<ControlPlaneDbContext>(opt =>
            opt.UseSqlite($"Data Source={tempDb}").UseSnakeCaseNamingConvention());
        services.AddLogging();
        services.AddScoped<ApiTokenService>();

        var sp = services.BuildServiceProvider();
        var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        db.Database.EnsureCreated();

        var logger = NullLogger<ApiTokenService>.Instance;
        var service = new ApiTokenService(db, logger);

        return (db, service, tempDb);
    }

    [Fact]
    public async Task CreateTokenAsync_GeneratesSecureToken_AndHashesInDb()
    {
        var (db, service, tempDb) = CreateService();
        try
        {
            var req = new CreateApiTokenRequest("Test MCP Token", "Operator", 30);
            var created = await service.CreateTokenAsync(req);

            Assert.NotNull(created);
            Assert.Equal("Test MCP Token", created.Name);
            Assert.Equal("Operator", created.Role);
            Assert.StartsWith("cp_pat_", created.Token);
            Assert.StartsWith("cp_pat_", created.TokenPrefix);
            Assert.NotNull(created.ExpiresAt);

            // Verify DB entity
            var entity = await db.ApiTokens.FindAsync(created.Id);
            Assert.NotNull(entity);
            Assert.Equal(ApiTokenService.HashToken(created.Token), entity.TokenHash);
            Assert.False(entity.IsRevoked);
        }
        finally
        {
            try { File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_ReturnsEntityAndUpdatesLastUsed()
    {
        var (db, service, tempDb) = CreateService();
        try
        {
            var req = new CreateApiTokenRequest("Agent Runner", "Admin");
            var created = await service.CreateTokenAsync(req);

            var validated = await service.ValidateTokenAsync(created.Token);
            Assert.NotNull(validated);
            Assert.Equal(created.Id, validated.Id);
            Assert.NotNull(validated.LastUsedAt);
        }
        finally
        {
            try { File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public async Task ValidateTokenAsync_RevokedToken_ReturnsNull()
    {
        var (db, service, tempDb) = CreateService();
        try
        {
            var req = new CreateApiTokenRequest("Revoked App", "Viewer");
            var created = await service.CreateTokenAsync(req);

            var revoked = await service.RevokeTokenAsync(created.Id);
            Assert.True(revoked);

            var validated = await service.ValidateTokenAsync(created.Token);
            Assert.Null(validated);
        }
        finally
        {
            try { File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public async Task ValidateTokenAsync_ExpiredToken_ReturnsNull()
    {
        var (db, service, tempDb) = CreateService();
        try
        {
            var req = new CreateApiTokenRequest("Expired App", "Admin", 0);
            var created = await service.CreateTokenAsync(req);

            // Manually expire in DB
            var entity = await db.ApiTokens.FindAsync(created.Id);
            Assert.NotNull(entity);
            entity.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();

            var validated = await service.ValidateTokenAsync(created.Token);
            Assert.Null(validated);
        }
        finally
        {
            try { File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public async Task ListTokensAsync_ReturnsSummaryWithExpirationState()
    {
        var (db, service, tempDb) = CreateService();
        try
        {
            await service.CreateTokenAsync(new CreateApiTokenRequest("Token A", "Admin"));
            await service.CreateTokenAsync(new CreateApiTokenRequest("Token B", "Viewer", 7));

            var list = await service.ListTokensAsync();
            Assert.Equal(2, list.Count);
            Assert.Contains(list, t => t.Name == "Token A");
            Assert.Contains(list, t => t.Name == "Token B");
        }
        finally
        {
            try { File.Delete(tempDb); } catch { }
        }
    }
}
