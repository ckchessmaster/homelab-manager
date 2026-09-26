using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ControlPlane.Api.Tests;

public class AgentBinarySyncTests : IDisposable
{
    private readonly string _tempDir;

    public AgentBinarySyncTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agent-sync-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler);
        }
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }

    private static byte[] CreateSampleTarGz(string entryFileName, string content)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gz))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, entryFileName);
            entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            tar.WriteEntry(entry);
        }
        return ms.ToArray();
    }

    private static byte[] CreateSampleZip(string entryFileName, string content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(entryFileName);
            using var entryStream = entry.Open();
            entryStream.Write(Encoding.UTF8.GetBytes(content));
        }
        return ms.ToArray();
    }

    [Fact]
    public void AgentBinaryService_ResolvesConfiguredDistDirectory()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["ControlPlane:AgentDistDir"] = _tempDir
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var service = new AgentBinaryService(config);
        var distDir = service.GetDistDirectory();

        Assert.Equal(_tempDir, distDir);
    }

    [Fact]
    public void AgentBinaryService_ReadsTargetVersion_FromVersionJson()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["ControlPlane:AgentDistDir"] = _tempDir
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var versionFile = Path.Combine(_tempDir, "version.json");
        File.WriteAllText(versionFile, JsonSerializer.Serialize(new { version = "v1.4.2" }));

        var service = new AgentBinaryService(config);
        var version = service.GetTargetVersion();

        Assert.Equal("1.4.2", version);
    }

    [Fact]
    public void AgentBinaryService_FallsBackToDefaultVersion_WhenNoVersionJson()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["ControlPlane:AgentDistDir"] = _tempDir
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var service = new AgentBinaryService(config);
        var version = service.GetTargetVersion();

        Assert.Equal(AgentBinaryService.DefaultAgentVersion, version);
    }

    [Fact]
    public async Task AgentBinarySyncService_HandlesGitHubErrorGracefully()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["ControlPlane:AgentDistDir"] = _tempDir,
            ["ControlPlane:AgentBinaryRepo"] = "fake-owner/fake-repo"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        var binaryService = new AgentBinaryService(config);
        var options = Options.Create(new AgentBinarySyncOptions());

        var handler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                ReasonPhrase = "API rate limit exceeded"
            });

        var clientFactory = new FakeHttpClientFactory(handler);
        var syncService = new AgentBinarySyncService(
            clientFactory,
            binaryService,
            options,
            config,
            NullLogger<AgentBinarySyncService>.Instance
        );

        var result = await syncService.SyncBinariesAsync(force: true);

        Assert.False(result.Success);
        Assert.Contains("rate limit", result.Message, StringComparison.OrdinalIgnoreCase);

        var status = await syncService.GetStatusAsync();
        Assert.Equal("Failed", status.Status);
        Assert.NotNull(status.LastError);
    }

    [Fact]
    public async Task AgentBinarySyncService_DownloadsAndExtractsArchivesSuccessfully()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["ControlPlane:AgentDistDir"] = _tempDir,
            ["ControlPlane:AgentBinaryRepo"] = "ckchessmaster/homelab-manager"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        var binaryService = new AgentBinaryService(config);
        var options = Options.Create(new AgentBinarySyncOptions
        {
            Repository = "ckchessmaster/homelab-manager"
        });

        var linuxAmd64Tar = CreateSampleTarGz("controlplane-agent-linux-amd64", "LINUX_AMD64_BINARY_DATA");
        var linuxArm64Tar = CreateSampleTarGz("controlplane-agent-linux-arm64", "LINUX_ARM64_BINARY_DATA");
        var windowsZip = CreateSampleZip("controlplane-agent-windows-amd64.exe", "WINDOWS_EXE_DATA");

        var releaseResponse = new
        {
            tag_name = "v1.3.1",
            assets = new[]
            {
                new
                {
                    name = "controlplane-agent_v1.3.1_linux_amd64.tar.gz",
                    browser_download_url = "https://github.com/mock/download/linux-amd64.tar.gz",
                    size = linuxAmd64Tar.Length
                },
                new
                {
                    name = "controlplane-agent_v1.3.1_linux_arm64.tar.gz",
                    browser_download_url = "https://github.com/mock/download/linux-arm64.tar.gz",
                    size = linuxArm64Tar.Length
                },
                new
                {
                    name = "controlplane-agent_v1.3.1_windows_amd64.zip",
                    browser_download_url = "https://github.com/mock/download/windows-amd64.zip",
                    size = windowsZip.Length
                }
            }
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("/releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(releaseResponse), Encoding.UTF8, "application/json")
                };
            }
            if (url.Contains("linux-amd64.tar.gz"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(linuxAmd64Tar)
                };
            }
            if (url.Contains("linux-arm64.tar.gz"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(linuxArm64Tar)
                };
            }
            if (url.Contains("windows-amd64.zip"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(windowsZip)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var clientFactory = new FakeHttpClientFactory(handler);
        var syncService = new AgentBinarySyncService(
            clientFactory,
            binaryService,
            options,
            config,
            NullLogger<AgentBinarySyncService>.Instance
        );

        var result = await syncService.SyncBinariesAsync(force: false);

        Assert.True(result.Success);
        Assert.Equal("v1.3.1", result.Version);
        Assert.Equal(3, result.UpdatedBinaries.Count);

        // Verify extracted files exist in _tempDir
        var linuxAmd64Path = Path.Combine(_tempDir, "controlplane-agent-linux-amd64");
        var linuxArm64Path = Path.Combine(_tempDir, "controlplane-agent-linux-arm64");
        var windowsPath = Path.Combine(_tempDir, "controlplane-agent-windows-amd64.exe");

        Assert.True(File.Exists(linuxAmd64Path));
        Assert.True(File.Exists(linuxArm64Path));
        Assert.True(File.Exists(windowsPath));

        Assert.Equal("LINUX_AMD64_BINARY_DATA", File.ReadAllText(linuxAmd64Path));
        Assert.Equal("WINDOWS_EXE_DATA", File.ReadAllText(windowsPath));

        // Verify version.json was created
        var versionJsonPath = Path.Combine(_tempDir, "version.json");
        Assert.True(File.Exists(versionJsonPath));
        Assert.Contains("v1.3.1", File.ReadAllText(versionJsonPath));

        // Verify subsequent sync without force returns up to date
        var secondResult = await syncService.SyncBinariesAsync(force: false);
        Assert.True(secondResult.Success);
        Assert.Contains("already up to date", secondResult.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(secondResult.UpdatedBinaries);
    }

    [Fact]
    public async Task AgentBinarySyncService_ForcesDownload_WhenForceIsTrue()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["ControlPlane:AgentDistDir"] = _tempDir,
            ["ControlPlane:AgentBinaryRepo"] = "ckchessmaster/homelab-manager"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        var binaryService = new AgentBinaryService(config);
        var options = Options.Create(new AgentBinarySyncOptions());

        var linuxAmd64Tar = CreateSampleTarGz("controlplane-agent-linux-amd64", "DATA_V2");
        var releaseResponse = new
        {
            tag_name = "v1.3.0",
            assets = new[]
            {
                new
                {
                    name = "controlplane-agent_v1.3.0_linux_amd64.tar.gz",
                    browser_download_url = "https://github.com/mock/download/linux-amd64.tar.gz",
                    size = linuxAmd64Tar.Length
                }
            }
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("/releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(releaseResponse), Encoding.UTF8, "application/json")
                };
            }
            if (url.Contains("linux-amd64.tar.gz"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(linuxAmd64Tar)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var clientFactory = new FakeHttpClientFactory(handler);
        var syncService = new AgentBinarySyncService(
            clientFactory,
            binaryService,
            options,
            config,
            NullLogger<AgentBinarySyncService>.Instance
        );

        // First run
        var r1 = await syncService.SyncBinariesAsync(force: false);
        Assert.True(r1.Success);

        // Force run
        var r2 = await syncService.SyncBinariesAsync(force: true);
        Assert.True(r2.Success);
        Assert.Contains("controlplane-agent-linux-amd64", r2.UpdatedBinaries);
    }

    private class FakeAgentBinarySyncService : IAgentBinarySyncService
    {
        public int SyncCallCount { get; private set; }

        public Task<AgentBinaryStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentBinaryStatusDto(
                CurrentInstalledVersion: "1.3.0",
                LatestAvailableVersion: "v1.3.0",
                LastCheckedAtUtc: DateTimeOffset.UtcNow,
                LastDownloadedAtUtc: DateTimeOffset.UtcNow,
                Status: "Success",
                LastError: null,
                IsSyncing: false,
                AutoSyncEnabled: true,
                SyncIntervalHours: 6,
                Repository: "ckchessmaster/homelab-manager",
                Platforms: Array.Empty<AgentBinaryPlatformDto>()
            ));
        }

        public Task<AgentBinarySyncResultDto> SyncBinariesAsync(bool force = false, CancellationToken cancellationToken = default)
        {
            SyncCallCount++;
            return Task.FromResult(new AgentBinarySyncResultDto(
                Success: true,
                Version: "v1.3.0",
                Message: "OK",
                UpdatedBinaries: Array.Empty<string>(),
                Status: null!
            ));
        }
    }

    [Fact]
    public async Task AgentBinaryBackgroundService_ExecutesStartupCheck()
    {
        var fakeSync = new FakeAgentBinarySyncService();
        var options = Options.Create(new AgentBinarySyncOptions
        {
            Enabled = true,
            CheckOnStartup = true,
            SyncIntervalHours = 1
        });

        var backgroundService = new AgentBinaryBackgroundService(
            fakeSync,
            options,
            NullLogger<AgentBinaryBackgroundService>.Instance
        )
        {
            StartupDelay = TimeSpan.FromMilliseconds(20)
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await backgroundService.StartAsync(cts.Token);
        await Task.Delay(100);
        await backgroundService.StopAsync(CancellationToken.None);

        Assert.True(fakeSync.SyncCallCount >= 1);
    }
}
