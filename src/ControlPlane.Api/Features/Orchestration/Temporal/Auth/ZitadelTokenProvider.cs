using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Auth;

public class ZitadelTokenProvider : IZitadelTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<TemporalOptions> _options;
    private readonly ILogger<ZitadelTokenProvider> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public ZitadelTokenProvider(
        HttpClient httpClient,
        IOptions<TemporalOptions> options,
        ILogger<ZitadelTokenProvider> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var auth = _options.Value.Auth;
        if (!auth.Enabled)
        {
            return null;
        }

        // Return cached token if valid with at least 60s safety buffer
        if (!string.IsNullOrWhiteSpace(_cachedToken) && DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-60))
        {
            return _cachedToken;
        }

        await _semaphore.WaitAsync(ct);
        try
        {
            // Double check inside lock
            if (!string.IsNullOrWhiteSpace(_cachedToken) && DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-60))
            {
                return _cachedToken;
            }

            if (string.IsNullOrWhiteSpace(auth.TokenUrl))
            {
                _logger.LogError("Temporal Zitadel auth is enabled but TokenUrl is not configured.");
                return null;
            }

            _logger.LogInformation("Requesting fresh M2M client credentials token from Zitadel ({TokenUrl})...", auth.TokenUrl);

            var parameters = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = auth.ClientId,
                ["client_secret"] = auth.ClientSecret
            };

            if (!string.IsNullOrWhiteSpace(auth.Scopes))
            {
                parameters["scope"] = auth.Scopes;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, auth.TokenUrl)
            {
                Content = new FormUrlEncodedContent(parameters)
            };

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Failed to obtain Zitadel M2M token. Status: {StatusCode}, Body: {Body}", response.StatusCode, errorBody);
                return null;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
            if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                _logger.LogError("Zitadel token response did not contain an access_token.");
                return null;
            }

            _cachedToken = tokenResponse.AccessToken;
            var expiresIn = tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 3600;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            _logger.LogInformation("Successfully acquired Zitadel M2M token for Temporal (expires in {ExpiresIn}s).", expiresIn);
            return _cachedToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving Zitadel M2M token from {TokenUrl}", auth.TokenUrl);
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string? TokenType
    );
}
