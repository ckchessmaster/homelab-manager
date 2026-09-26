using System.Net;
using System.Net.Http.Json;
using ControlPlane.Api.Features.SystemLogs;
using ControlPlane.Api.Storage;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ControlPlane.Api.Tests;

public class SystemLogTests
{
    [Fact]
    public void SystemLogBuffer_EnqueuesAndMaintainsCapacity()
    {
        var buffer = new SystemLogBuffer(capacity: 5);

        for (int i = 1; i <= 10; i++)
        {
            buffer.Enqueue(LogLevel.Information, new EventId(i), "TestCategory", $"Message {i}", null);
        }

        var stats = buffer.GetStats();
        Assert.Equal(5, stats.TotalCount);
        Assert.Equal(5, stats.InfoCount);
        Assert.Equal(0, stats.ErrorCount);

        var query = buffer.Query(limit: 10);
        Assert.Equal(5, query.Logs.Count);
        // The last 5 should be 6 to 10
        Assert.Equal("Message 6", query.Logs.First().Message);
        Assert.Equal("Message 10", query.Logs.Last().Message);
    }

    [Fact]
    public void SystemLogBuffer_FiltersByLevel_Search_AndCategory()
    {
        var buffer = new SystemLogBuffer(capacity: 100);

        buffer.Enqueue(LogLevel.Debug, new EventId(1), "App.Auth", "User login attempt", null);
        buffer.Enqueue(LogLevel.Information, new EventId(2), "App.Adapter.UniFi", "Connected to switch", null);
        buffer.Enqueue(LogLevel.Warning, new EventId(3), "App.Adapter.Proxmox", "High CPU usage", null);
        buffer.Enqueue(LogLevel.Error, new EventId(4), "App.Database", "Connection timeout", new InvalidOperationException("DB error"));

        // Exact Level filter
        var errors = buffer.Query(level: "Error");
        Assert.Single(errors.Logs);
        Assert.Equal("Connection timeout", errors.Logs[0].Message);
        Assert.NotNull(errors.Logs[0].Exception);

        // MinLevel filter (Warning & above -> Warning + Error)
        var warningsAndAbove = buffer.Query(minLevel: "Warning");
        Assert.Equal(2, warningsAndAbove.Logs.Count);

        // Category filter
        var unifiLogs = buffer.Query(category: "UniFi");
        Assert.Single(unifiLogs.Logs);
        Assert.Equal("Connected to switch", unifiLogs.Logs[0].Message);

        // Text Search
        var searchLogs = buffer.Query(search: "timeout");
        Assert.Single(searchLogs.Logs);
        Assert.Equal("App.Database", searchLogs.Logs[0].Category);

        // SinceId filter
        var lastId = errors.Logs[0].Id;
        buffer.Enqueue(LogLevel.Information, new EventId(5), "App.System", "System healthy", null);
        var afterLogs = buffer.Query(sinceId: lastId);
        Assert.Single(afterLogs.Logs);
        Assert.Equal("System healthy", afterLogs.Logs[0].Message);
    }

    [Fact]
    public void SystemLogBuffer_Clear_ResetsAllEntriesAndCounters()
    {
        var buffer = new SystemLogBuffer(capacity: 50);
        buffer.Enqueue(LogLevel.Error, new EventId(1), "App", "Error 1", null);
        buffer.Enqueue(LogLevel.Warning, new EventId(2), "App", "Warn 1", null);

        var beforeStats = buffer.GetStats();
        Assert.Equal(2, beforeStats.TotalCount);

        buffer.Clear();

        var afterStats = buffer.GetStats();
        Assert.Equal(0, afterStats.TotalCount);
        Assert.Equal(0, afterStats.ErrorCount);
        Assert.Equal(0, afterStats.WarningCount);
        Assert.Empty(buffer.Query().Logs);
    }

    private class SystemTestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-syslogs-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("STANDBY_MODE", "true");
            builder.UseSetting("ControlPlane:ApiKey", "dev-secret-key-123");
            builder.UseSetting("ConnectionStrings:PostgresDatabase", "");
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ControlPlaneDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ControlPlaneDbContext>(options =>
                {
                    options.UseSqlite($"Data Source={_tempDbFile}")
                        .UseSnakeCaseNamingConvention();
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (File.Exists(_tempDbFile))
            {
                try { File.Delete(_tempDbFile); } catch { }
            }
        }
    }

    [Fact]
    public async Task SystemEndpoints_ReturnsLogsStatsAndInfo()
    {
        using var factory = new SystemTestAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");

        // 1. Trigger some log messages
        var logger = factory.Services.GetRequiredService<ILogger<SystemLogTests>>();
        logger.LogInformation("Integration test initiated for system logs endpoint");
        logger.LogWarning("Sample test warning for query verification");

        // 2. Query logs endpoint
        var logsResp = await client.GetAsync("/api/v1/system/logs?limit=50");
        Assert.Equal(HttpStatusCode.OK, logsResp.StatusCode);

        var logResponse = await logsResp.Content.ReadFromJsonAsync<SystemLogResponse>();
        Assert.NotNull(logResponse);
        Assert.NotEmpty(logResponse.Logs);
        Assert.Contains(logResponse.Logs, l => l.Message.Contains("Integration test initiated"));

        // 3. Query stats endpoint
        var statsResp = await client.GetAsync("/api/v1/system/logs/stats");
        Assert.Equal(HttpStatusCode.OK, statsResp.StatusCode);
        var stats = await statsResp.Content.ReadFromJsonAsync<SystemLogStats>();
        Assert.NotNull(stats);
        Assert.True(stats.TotalCount > 0);

        // 4. Query system info endpoint
        var infoResp = await client.GetAsync("/api/v1/system/info");
        Assert.Equal(HttpStatusCode.OK, infoResp.StatusCode);
        var info = await infoResp.Content.ReadFromJsonAsync<SystemInfoDto>();
        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info.MachineName));
        Assert.False(string.IsNullOrWhiteSpace(info.OsDescription));
        Assert.True(info.ProcessorCount > 0);

        // 5. Clear logs endpoint
        var deleteResp = await client.DeleteAsync("/api/v1/system/logs");
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

        var afterStatsResp = await client.GetAsync("/api/v1/system/logs/stats");
        var afterStats = await afterStatsResp.Content.ReadFromJsonAsync<SystemLogStats>();
        Assert.NotNull(afterStats);
        Assert.Equal(0, afterStats.TotalCount);
    }
}
