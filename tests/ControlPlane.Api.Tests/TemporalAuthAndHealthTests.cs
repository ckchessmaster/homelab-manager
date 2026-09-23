using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ControlPlane.Api.Features.Orchestration.Temporal;
using ControlPlane.Api.Features.Orchestration.Temporal.Auth;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ControlPlane.Api.Tests;

public class TemporalAuthAndHealthTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        private readonly HttpResponseMessage _response;

        public MockHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }

    [Fact]
    public async Task ZitadelTokenProvider_WhenAuthDisabled_ReturnsNull()
    {
        var options = Options.Create(new TemporalOptions
        {
            Auth = new TemporalAuthOptions { Enabled = false }
        });

        var provider = new ZitadelTokenProvider(
            new HttpClient(),
            options,
            NullLogger<ZitadelTokenProvider>.Instance);

        var token = await provider.GetAccessTokenAsync();
        Assert.Null(token);
    }

    [Fact]
    public async Task ZitadelTokenProvider_WhenAuthEnabled_FetchesTokenAndCachesResponse()
    {
        var jsonPayload = JsonSerializer.Serialize(new
        {
            access_token = "mock-zitadel-m2m-token-12345",
            token_type = "Bearer",
            expires_in = 3600
        });

        var mockHandler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new TemporalOptions
        {
            Auth = new TemporalAuthOptions
            {
                Enabled = true,
                TokenUrl = "https://auth.homelab.local/oauth/v2/token",
                ClientId = "client-id-abc",
                ClientSecret = "client-secret-xyz",
                Scopes = "openid urn:zitadel:iam:org:projects:roles"
            }
        });

        var provider = new ZitadelTokenProvider(
            httpClient,
            options,
            NullLogger<ZitadelTokenProvider>.Instance);

        // First call should execute HTTP request
        var token1 = await provider.GetAccessTokenAsync();
        Assert.Equal("mock-zitadel-m2m-token-12345", token1);
        Assert.Equal(1, mockHandler.CallCount);

        // Second call within expiry should return cached token without second HTTP request
        var token2 = await provider.GetAccessTokenAsync();
        Assert.Equal("mock-zitadel-m2m-token-12345", token2);
        Assert.Equal(1, mockHandler.CallCount);
    }

    [Fact]
    public async Task TemporalHealthCheck_WhenTemporalDisabled_ReturnsHealthy()
    {
        var options = Options.Create(new TemporalOptions { Enabled = false });
        var healthCheck = new TemporalHealthCheck(
            options,
            NullLogger<TemporalHealthCheck>.Instance,
            client: null);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("disabled", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TemporalHealthCheck_WhenClientMissing_ReturnsUnhealthy()
    {
        var options = Options.Create(new TemporalOptions
        {
            Enabled = true,
            Address = "localhost:7233",
            Namespace = "homelab-manager"
        });

        var healthCheck = new TemporalHealthCheck(
            options,
            NullLogger<TemporalHealthCheck>.Instance,
            client: null);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("not registered", result.Description, StringComparison.OrdinalIgnoreCase);
    }
}
