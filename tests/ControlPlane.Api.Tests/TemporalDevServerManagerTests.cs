using ControlPlane.Cli.Temporal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ControlPlane.Api.Tests;

public class TemporalDevServerManagerTests
{
    [Fact]
    public void BuildCommandLineArgs_DefaultConfig_GeneratesExpectedArgs()
    {
        var manager = new TemporalDevServerManager(NullLogger<TemporalDevServerManager>.Instance);
        var config = new TemporalDevServerConfig(
            Enabled: true,
            GrpcPort: 7233,
            UiPort: 8233,
            DbFilename: "",
            Ip: "127.0.0.1"
        );

        var args = manager.BuildCommandLineArgs(config);

        Assert.Equal("server start-dev --ip 127.0.0.1 --port 7233 --ui-port 8233 --namespace homelab-manager", args);
    }

    [Fact]
    public void BuildCommandLineArgs_WithDbAndHeadless_IncludesDbFilenameAndHeadlessFlags()
    {
        var manager = new TemporalDevServerManager(NullLogger<TemporalDevServerManager>.Instance);
        var config = new TemporalDevServerConfig(
            Enabled: true,
            GrpcPort: 7999,
            UiPort: 8999,
            DbFilename: "/tmp/test-temporal.db",
            Ip: "0.0.0.0",
            Headless: true
        );

        var args = manager.BuildCommandLineArgs(config);

        Assert.Contains("--ip 0.0.0.0", args);
        Assert.Contains("--port 7999", args);
        Assert.Contains("--ui-port 8999", args);
        Assert.Contains("--namespace homelab-manager", args);
        Assert.Contains("--db-filename \"/tmp/test-temporal.db\"", args);
        Assert.Contains("--headless", args);
    }

    [Fact]
    public async Task IsServerReachableAsync_WhenNoServiceListening_ReturnsFalse()
    {
        var manager = new TemporalDevServerManager(NullLogger<TemporalDevServerManager>.Instance);
        // Using an unlikely-to-be-bound high port
        var reachable = await manager.IsServerReachableAsync("127.0.0.1", 59482);
        Assert.False(reachable);
    }

    [Fact]
    public async Task EnsureRunningAsync_WhenConfigDisabled_ReturnsFalse()
    {
        var manager = new TemporalDevServerManager(NullLogger<TemporalDevServerManager>.Instance);
        var config = new TemporalDevServerConfig(Enabled: false);

        var result = await manager.EnsureRunningAsync(config);
        Assert.False(result);
    }

    [Fact]
    public async Task ResolveOrDownloadBinaryAsync_WhenCustomBinaryExists_ReturnsCustomPath()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var manager = new TemporalDevServerManager(NullLogger<TemporalDevServerManager>.Instance);
            var resolved = await manager.ResolveOrDownloadBinaryAsync(tempFile);
            Assert.Equal(tempFile, resolved);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
