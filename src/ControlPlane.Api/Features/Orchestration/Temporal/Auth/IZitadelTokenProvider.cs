namespace ControlPlane.Api.Features.Orchestration.Temporal.Auth;

public interface IZitadelTokenProvider
{
    /// <summary>
    /// Obtains an active OAuth2 access token, requesting a fresh token if expired or nearing expiry.
    /// </summary>
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);
}
