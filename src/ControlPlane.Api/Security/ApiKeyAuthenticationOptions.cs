using Microsoft.AspNetCore.Authentication;

namespace ControlPlane.Api.Security;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ApiKey";
    public const string DefaultHeaderName = "X-ControlPlane-Key";

    public string HeaderName { get; set; } = DefaultHeaderName;
    public string? ApiKey { get; set; }
    public bool BypassAuth { get; set; }
}
