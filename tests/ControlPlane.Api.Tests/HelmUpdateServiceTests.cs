using System.Net;
using System.Text;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Kubernetes.Helm;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ControlPlane.Api.Tests;

public class HelmUpdateServiceTests
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
    public async Task CheckChartUpdateAsync_DetectsOutdatedChart_FromArtifactHub()
    {
        // Mock Artifact Hub response for cert-manager with version 1.16.0
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri != null && req.RequestUri.AbsoluteUri.Contains("artifacthub.io"))
            {
                var responseJson = JsonSerializer.Serialize(new
                {
                    packages = new[]
                    {
                        new
                        {
                            name = "cert-manager",
                            version = "1.16.0",
                            app_version = "v1.16.0",
                            official = true,
                            stars = 100,
                            repository = new
                            {
                                name = "jetstack",
                                url = "https://charts.jetstack.io",
                                verified_publisher = true
                            }
                        }
                    }
                });

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FakeHttpClientFactory(handler);
        var service = new HelmUpdateService(factory, cache, NullLogger<HelmUpdateService>.Instance);

        // Current version is 1.12.0, newer is 1.16.0 -> minor update
        var result = await service.CheckChartUpdateAsync("cert-manager", "1.12.0", "v1.12.0");

        Assert.True(result.IsOutdated);
        Assert.Equal("1.16.0", result.LatestVersion);
        Assert.Equal("minor", result.UpdateType);
        Assert.Equal("v1.16.0", result.LatestAppVersion);
        Assert.Contains("Minor update available", result.Message);
    }

    [Fact]
    public async Task CheckChartUpdateAsync_ReportsUpToDate_WhenSameOrOlder()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var responseJson = JsonSerializer.Serialize(new
            {
                packages = new[]
                {
                    new
                    {
                        name = "ingress-nginx",
                        version = "4.11.2",
                        app_version = "1.11.2",
                        official = true,
                        stars = 500,
                        repository = new
                        {
                            name = "ingress-nginx",
                            url = "https://kubernetes.github.io/ingress-nginx",
                            verified_publisher = true
                        }
                    }
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FakeHttpClientFactory(handler);
        var service = new HelmUpdateService(factory, cache, NullLogger<HelmUpdateService>.Instance);

        var result = await service.CheckChartUpdateAsync("ingress-nginx", "4.11.2", "1.11.2");

        Assert.False(result.IsOutdated);
        Assert.Equal("4.11.2", result.LatestVersion);
        Assert.Null(result.UpdateType);
        Assert.Equal("Chart is up to date", result.Message);
    }

    [Fact]
    public async Task CheckChartUpdateAsync_SkipsPreReleaseCandidates_ForStableCurrent()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var responseJson = JsonSerializer.Serialize(new
            {
                packages = new[]
                {
                    new
                    {
                        name = "longhorn",
                        version = "1.8.0-rc.1",
                        app_version = "v1.8.0-rc.1",
                        official = true,
                        stars = 300,
                        repository = new
                        {
                            name = "longhorn",
                            url = "https://charts.longhorn.io",
                            verified_publisher = true
                        }
                    }
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FakeHttpClientFactory(handler);
        var service = new HelmUpdateService(factory, cache, NullLogger<HelmUpdateService>.Instance);

        var result = await service.CheckChartUpdateAsync("longhorn", "1.7.2", "v1.7.2");

        Assert.False(result.IsOutdated);
        Assert.Contains("candidate is pre-release", result.Message);
    }

    [Fact]
    public async Task CheckChartUpdateAsync_FallsBackToIndexYaml_WhenArtifactHubReturnsEmpty()
    {
        var sampleIndexYaml = """
        apiVersion: v1
        entries:
          custom-app:
            - version: 2.5.0
              appVersion: 1.0.0
              created: 2026-01-01T00:00:00Z
            - version: 2.4.0
              appVersion: 0.9.0
        """;

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri != null && req.RequestUri.AbsoluteUri.Contains("index.yaml"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sampleIndexYaml, Encoding.UTF8, "text/yaml")
                };
            }

            // Artifact Hub returns empty packages
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"packages\":[]}", Encoding.UTF8, "application/json")
            };
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FakeHttpClientFactory(handler);
        var service = new HelmUpdateService(factory, cache, NullLogger<HelmUpdateService>.Instance);

        var result = await service.CheckChartUpdateAsync("custom-app", "2.4.0", "0.9.0", "https://charts.homelab.local");

        Assert.True(result.IsOutdated);
        Assert.Equal("2.5.0", result.LatestVersion);
        Assert.Equal("minor", result.UpdateType);
    }

    [Fact]
    public async Task CheckReleasesAsync_ChecksAllReleasesAndCachesResults()
    {
        int networkCalls = 0;
        var handler = new MockHttpMessageHandler(req =>
        {
            Interlocked.Increment(ref networkCalls);
            var responseJson = JsonSerializer.Serialize(new
            {
                packages = new[]
                {
                    new
                    {
                        name = "pihole",
                        version = "2.30.0",
                        app_version = "2024.10.0",
                        official = false,
                        stars = 50,
                        repository = new
                        {
                            name = "mojo2600",
                            url = "https://mojo2600.github.io/pihole-kubernetes",
                            verified_publisher = true
                        }
                    }
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FakeHttpClientFactory(handler);
        var service = new HelmUpdateService(factory, cache, NullLogger<HelmUpdateService>.Instance);

        var releases = new List<HelmReleaseSummaryDto>
        {
            new("pihole-primary", "pihole", 1, DateTimeOffset.UtcNow, "deployed", "pihole-2.28.0", "pihole", "2.28.0", "2024.07.0"),
            new("pihole-secondary", "pihole-sec", 2, DateTimeOffset.UtcNow, "deployed", "pihole-2.28.0", "pihole", "2.28.0", "2024.07.0")
        };

        var results = await service.CheckReleasesAsync(releases);

        Assert.Equal(2, results.Count);
        Assert.True(results["pihole/pihole-primary"].IsOutdated);
        Assert.Equal("2.30.0", results["pihole/pihole-primary"].LatestVersion);
        Assert.True(results["pihole-sec/pihole-secondary"].IsOutdated);

        // Due to grouping by ChartName + Version, only 1 network call should have occurred
        Assert.Equal(1, networkCalls);

        // Cached lookup immediately returns the item
        var cached = service.GetCached("pihole", "2.28.0");
        Assert.NotNull(cached);
        Assert.Equal("2.30.0", cached.LatestVersion);
    }

    [Fact]
    public async Task CheckChartUpdateAsync_DetectsOciChartUpdate_FromOciRegistryTags()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var uri = req.RequestUri?.AbsoluteUri ?? "";

            // 1. Artifact Hub returns empty
            if (uri.Contains("artifacthub.io"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"packages\":[]}", Encoding.UTF8, "application/json")
                };
            }

            // 2. Token request
            if (uri.Contains("ghcr.io/token"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token\":\"fake-oci-token\"}", Encoding.UTF8, "application/json")
                };
            }

            // 3. Tags list without auth -> 401 challenge
            if (uri.Contains("tags/list") && req.Headers.Authorization == null)
            {
                var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                unauthorized.Headers.Add("Www-Authenticate", "Bearer realm=\"https://ghcr.io/token\",service=\"ghcr.io\",scope=\"repository:ckchessmaster/charts/controlplane:pull\"");
                return unauthorized;
            }

            // 4. Tags list with Bearer auth -> return tag list
            if (uri.Contains("tags/list") && req.Headers.Authorization?.Parameter == "fake-oci-token")
            {
                var tagsJson = JsonSerializer.Serialize(new
                {
                    name = "ckchessmaster/charts/controlplane",
                    tags = new[] { "1.0.0", "1.2.0", "1.2.3" }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(tagsJson, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FakeHttpClientFactory(handler);
        var service = new HelmUpdateService(factory, cache, NullLogger<HelmUpdateService>.Instance);

        var result = await service.CheckChartUpdateAsync(
            "controlplane",
            "1.2.0",
            "1.2.0",
            "oci://ghcr.io/ckchessmaster/charts/controlplane");

        Assert.True(result.IsOutdated);
        Assert.Equal("1.2.3", result.LatestVersion);
        Assert.Equal("patch", result.UpdateType);
        Assert.Contains("Patch update available: 1.2.0 -> 1.2.3", result.Message);
        Assert.NotNull(result.AvailableVersions);
        Assert.Contains("1.2.3", result.AvailableVersions!);
    }

    [Fact]
    public async Task CheckChartUpdateAsync_ResolvesControlPlaneFromCuratedCatalog_WhenRepoUrlIsNull()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var uri = req.RequestUri?.AbsoluteUri ?? "";
            if (uri.Contains("artifacthub.io"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"packages\":[]}", Encoding.UTF8, "application/json")
                };
            }

            if (uri.Contains("ghcr.io/token"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token\":\"fake-oci-token\"}", Encoding.UTF8, "application/json")
                };
            }

            if (uri.Contains("tags/list") && req.Headers.Authorization == null)
            {
                var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                unauthorized.Headers.Add("Www-Authenticate", "Bearer realm=\"https://ghcr.io/token\",service=\"ghcr.io\",scope=\"repository:ckchessmaster/charts/controlplane:pull\"");
                return unauthorized;
            }

            if (uri.Contains("tags/list") && req.Headers.Authorization?.Parameter == "fake-oci-token")
            {
                var tagsJson = JsonSerializer.Serialize(new
                {
                    name = "ckchessmaster/charts/controlplane",
                    tags = new[] { "1.0.0", "1.2.0", "1.2.3" }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(tagsJson, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FakeHttpClientFactory(handler);
        var service = new HelmUpdateService(factory, cache, NullLogger<HelmUpdateService>.Instance);

        // repoUrl is null, but "controlplane" is in CuratedCatalog with oci://ghcr.io/ckchessmaster/charts/controlplane
        var result = await service.CheckChartUpdateAsync("controlplane", "1.2.0", "1.2.0", repoUrl: null);

        Assert.True(result.IsOutdated);
        Assert.Equal("1.2.3", result.LatestVersion);
        Assert.Equal("oci://ghcr.io/ckchessmaster/charts/controlplane", result.RepoUrl);
    }
}
