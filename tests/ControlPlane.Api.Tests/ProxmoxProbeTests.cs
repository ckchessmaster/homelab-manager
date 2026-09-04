using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Proxmox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ControlPlane.Api.Tests;

public class ProxmoxProbeTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private class MockHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public MockHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    [Fact]
    public async Task ProbeAsync_SuccessfulResponse_ReturnsVersionAndNodes()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api2/json/version"))
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("""
                    {
                        "data": {
                            "version": "8.2.4",
                            "release": "8.2",
                            "repoid": "abc1234"
                        }
                    }
                    """)
                };
            }

            if (req.RequestUri!.AbsolutePath.EndsWith("/api2/json/nodes"))
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("""
                    {
                        "data": [
                            {
                                "node": "pve-01",
                                "status": "online",
                                "cpu": 0.08,
                                "maxcpu": 32,
                                "mem": 16000000000,
                                "maxmem": 68719476736,
                                "uptime": 86400
                            }
                        ]
                    }
                    """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new HttpClient(mockHandler);
        var factory = new MockHttpClientFactory(client);

        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<ProxmoxProbeService>();
        var service = new ProxmoxProbeService(factory, logger);

        var request = new ProxmoxProbeRequest(
            BaseUrl: "https://pve.homelab.local:8006",
            ApiTokenId: "root@pam!token1",
            ApiTokenSecret: "secret-uuid",
            AllowSelfSignedCert: true
        );

        var result = await service.ProbeAsync(request);

        Assert.True(result.Success);
        Assert.Equal("8.2.4", result.Version);
        Assert.Equal("8.2", result.Release);
        Assert.Equal("abc1234", result.Repoid);
        Assert.NotNull(result.Nodes);
        Assert.Single(result.Nodes);
        Assert.Equal("pve-01", result.Nodes[0].Node);
        Assert.Equal("online", result.Nodes[0].Status);
    }

    [Fact]
    public async Task ProbeAsync_UnauthorizedResponse_ReturnsDiagnosticFailure()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Content = new StringContent("{\"message\":\"Permission check failed\"}")
        });

        var client = new HttpClient(mockHandler);
        var factory = new MockHttpClientFactory(client);

        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<ProxmoxProbeService>();
        var service = new ProxmoxProbeService(factory, logger);

        var request = new ProxmoxProbeRequest(
            BaseUrl: "https://pve.homelab.local:8006",
            ApiTokenId: "root@pam!badtoken",
            ApiTokenSecret: "bad-secret"
        );

        var result = await service.ProbeAsync(request);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("401", result.ErrorMessage);
    }

    [Fact]
    public async Task Endpoint_MissingRequiredFields_ReturnsBadRequest()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("STANDBY_MODE", "true");
                builder.UseSetting("AUTH_BYPASS", "true");
                builder.UseSetting("ConnectionStrings:PostgresDatabase", "");
            });

        var client = factory.CreateClient();

        var request = new ProxmoxProbeRequest(
            BaseUrl: "",
            ApiTokenId: "",
            ApiTokenSecret: ""
        );

        var response = await client.PostAsJsonAsync("/api/v1/adapters/proxmox/test-connection", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
