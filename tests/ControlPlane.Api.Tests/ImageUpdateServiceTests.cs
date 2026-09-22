using System.Net;
using System.Text;
using System.Text.Json;
using ControlPlane.Api.Features.Workloads.ImageUpdates;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ControlPlane.Api.Tests;

public class ImageUpdateServiceTests
{
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

    [Fact]
    public void ParseImageRef_HandlesVariousFormats()
    {
        // Docker official image with tag
        var p1 = ImageUpdateService.ParseImageRef("nginx:1.24.0-alpine");
        Assert.Equal("docker.io", p1.Registry);
        Assert.Equal("library/nginx", p1.Repository);
        Assert.Equal("1.24.0-alpine", p1.Tag);
        Assert.Null(p1.Digest);

        // Docker official image with no tag (defaults to latest)
        var p2 = ImageUpdateService.ParseImageRef("redis");
        Assert.Equal("docker.io", p2.Registry);
        Assert.Equal("library/redis", p2.Repository);
        Assert.Equal("latest", p2.Tag);

        // Docker Hub organization image
        var p3 = ImageUpdateService.ParseImageRef("bitnami/postgresql:16.1.0");
        Assert.Equal("docker.io", p3.Registry);
        Assert.Equal("bitnami/postgresql", p3.Repository);
        Assert.Equal("16.1.0", p3.Tag);

        // GHCR image
        var p4 = ImageUpdateService.ParseImageRef("ghcr.io/home-assistant/home-assistant:2024.1.0");
        Assert.Equal("ghcr.io", p4.Registry);
        Assert.Equal("home-assistant/home-assistant", p4.Repository);
        Assert.Equal("2024.1.0", p4.Tag);

        // Quay image
        var p5 = ImageUpdateService.ParseImageRef("quay.io/coreos/flannel:v0.22.0");
        Assert.Equal("quay.io", p5.Registry);
        Assert.Equal("coreos/flannel", p5.Repository);
        Assert.Equal("v0.22.0", p5.Tag);

        // Custom registry with port and digest
        var p6 = ImageUpdateService.ParseImageRef("registry.homelab.local:5000/apps/web:1.2.3@sha256:abcdef");
        Assert.Equal("registry.homelab.local:5000", p6.Registry);
        Assert.Equal("apps/web", p6.Repository);
        Assert.Equal("1.2.3", p6.Tag);
        Assert.Equal("sha256:abcdef", p6.Digest);
    }

    [Theory]
    [InlineData("", "", true)]
    [InlineData("-alpine", "-alpine", true)]
    [InlineData("-alpine3.18", "-alpine3.20", true)]
    [InlineData("-alpine", "-alpine3.20", true)]
    [InlineData("-slim", "-slim", true)]
    [InlineData("", "-alpine", false)]
    [InlineData("-alpine", "", false)]
    [InlineData("-alpine", "-bookworm", false)]
    [InlineData("-slim", "-alpine", false)]
    public void IsFlavorCompatible_MatchesExpected(string curFlavor, string candFlavor, bool expected)
    {
        var actual = ImageUpdateService.IsFlavorCompatible(curFlavor, candFlavor);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task CheckImageAsync_DetectsMinorUpdate_OnDockerHub()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri != null && req.RequestUri.AbsoluteUri.Contains("hub.docker.com/v2/repositories/library/nginx/tags"))
            {
                var json = JsonSerializer.Serialize(new
                {
                    results = new[]
                    {
                        new { name = "1.28.0-rc1-alpine" }, // Prerelease should be skipped
                        new { name = "1.27.4-alpine" },     // Latest stable minor
                        new { name = "1.27.4" },            // Incompatible flavor (no -alpine)
                        new { name = "1.24.0-alpine" },     // Current
                        new { name = "1.22.0-alpine" }      // Older
                    }
                });

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FakeHttpClientFactory(mockHandler);
        var service = new ImageUpdateService(factory, cache, NullLogger<ImageUpdateService>.Instance);

        var result = await service.CheckImageAsync("nginx:1.24.0-alpine");

        Assert.True(result.IsOutdated);
        Assert.Equal("1.27.4-alpine", result.LatestTag);
        Assert.Equal("minor", result.UpdateType);
        Assert.Contains("Minor update available", result.Message);

        // Verify it was cached
        var cached = service.GetCached("nginx:1.24.0-alpine");
        Assert.NotNull(cached);
        Assert.Equal("1.27.4-alpine", cached.LatestTag);
    }

    [Fact]
    public async Task CheckImageAsync_DetectsMajorUpdate()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var json = JsonSerializer.Serialize(new
            {
                results = new[]
                {
                    new { name = "2.0.1" },
                    new { name = "1.9.0" },
                    new { name = "1.0.0" }
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FakeHttpClientFactory(mockHandler);
        var service = new ImageUpdateService(factory, cache, NullLogger<ImageUpdateService>.Instance);

        var result = await service.CheckImageAsync("library/testapp:1.0.0");

        Assert.True(result.IsOutdated);
        Assert.Equal("2.0.1", result.LatestTag);
        Assert.Equal("major", result.UpdateType);
    }

    [Fact]
    public async Task CheckImageAsync_ReportsUpToDate_WhenAlreadyLatest()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var json = JsonSerializer.Serialize(new
            {
                results = new[]
                {
                    new { name = "1.24.0-alpine" },
                    new { name = "1.23.0-alpine" }
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FakeHttpClientFactory(mockHandler);
        var service = new ImageUpdateService(factory, cache, NullLogger<ImageUpdateService>.Instance);

        var result = await service.CheckImageAsync("nginx:1.24.0-alpine");

        Assert.False(result.IsOutdated);
        Assert.Equal("1.24.0-alpine", result.LatestTag);
        Assert.Equal("Image is up to date", result.Message);
    }

    [Fact]
    public async Task CheckImageAsync_FloatingLatestTag_HandledCleanly()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FakeHttpClientFactory(mockHandler);
        var service = new ImageUpdateService(factory, cache, NullLogger<ImageUpdateService>.Instance);

        var result = await service.CheckImageAsync("redis:latest");

        Assert.False(result.IsOutdated);
        Assert.Equal("latest", result.CurrentTag);
        Assert.Equal("floating", result.UpdateType);
    }

    [Fact]
    public async Task CheckImageAsync_HandlesNetworkFailure_GracefullyWithoutThrowing()
    {
        var mockHandler = new MockHttpMessageHandler(_ =>
        {
            throw new HttpRequestException("Network connection refused");
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FakeHttpClientFactory(mockHandler);
        var service = new ImageUpdateService(factory, cache, NullLogger<ImageUpdateService>.Instance);

        var result = await service.CheckImageAsync("nginx:1.24.0-alpine");

        Assert.False(result.IsOutdated);
        Assert.Contains("Could not retrieve remote tags", result.Message);
    }
}
