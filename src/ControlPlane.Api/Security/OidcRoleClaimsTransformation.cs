using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Security;

/// <summary>
/// Transforms incoming OIDC JWT claims into standard ASP.NET Core Role claims (ClaimTypes.Role) and Name claims (ClaimTypes.Name).
/// Uses strict configurable claim types: RoleClaimType (defaults to 'groups') and NameClaimType (defaults to 'preferred_username').
/// </summary>
public class OidcRoleClaimsTransformation : IClaimsTransformation
{
    private readonly ILogger<OidcRoleClaimsTransformation> _logger;
    private readonly OidcJwtOptions _options;

    public OidcRoleClaimsTransformation(
        ILogger<OidcRoleClaimsTransformation> logger,
        IOptions<OidcJwtOptions>? options = null)
    {
        _logger = logger;
        _options = options?.Value ?? new OidcJwtOptions();
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        var existingRoles = new HashSet<string>(
            principal.FindAll(ClaimTypes.Role).Select(c => c.Value),
            StringComparer.OrdinalIgnoreCase
        );

        var extractedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Process strict configured RoleClaimType (defaults to "groups")
        var roleClaimType = string.IsNullOrWhiteSpace(_options.RoleClaimType)
            ? AuthConstants.DefaultRoleClaimType
            : _options.RoleClaimType;

        foreach (var claim in principal.FindAll(c => c.Type.Equals(roleClaimType, StringComparison.OrdinalIgnoreCase)))
        {
            ExtractRolesFromClaimValue(claim.Value, extractedRoles);
        }

        // 2. Inject normalized ClaimTypes.Role claims
        foreach (var rawRole in extractedRoles)
        {
            var normalized = NormalizeRoleName(rawRole);
            if (!string.IsNullOrEmpty(normalized) && existingRoles.Add(normalized))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, normalized));
                _logger.LogTrace("Added role claim '{Role}' to principal '{Name}'.", normalized, identity.Name);
            }
        }

        // 3. Role hierarchy: Admin implies Operator and Viewer; Operator implies Viewer
        if (existingRoles.Contains(AuthConstants.RoleAdmin))
        {
            if (existingRoles.Add(AuthConstants.RoleOperator))
                identity.AddClaim(new Claim(ClaimTypes.Role, AuthConstants.RoleOperator));
            if (existingRoles.Add(AuthConstants.RoleViewer))
                identity.AddClaim(new Claim(ClaimTypes.Role, AuthConstants.RoleViewer));
        }
        else if (existingRoles.Contains(AuthConstants.RoleOperator))
        {
            if (existingRoles.Add(AuthConstants.RoleViewer))
                identity.AddClaim(new Claim(ClaimTypes.Role, AuthConstants.RoleViewer));
        }

        // 4. Map NameClaimType (defaults to "preferred_username") if ClaimTypes.Name is not set
        var nameClaimType = string.IsNullOrWhiteSpace(_options.NameClaimType)
            ? AuthConstants.DefaultNameClaimType
            : _options.NameClaimType;

        if (principal.FindFirst(ClaimTypes.Name) == null)
        {
            var preferredNameClaim = principal.FindFirst(c => c.Type.Equals(nameClaimType, StringComparison.OrdinalIgnoreCase))
                                  ?? principal.FindFirst("preferred_username")
                                  ?? principal.FindFirst("email");
            if (preferredNameClaim != null && !string.IsNullOrWhiteSpace(preferredNameClaim.Value))
            {
                identity.AddClaim(new Claim(ClaimTypes.Name, preferredNameClaim.Value));
            }
        }

        return Task.FromResult(principal);
    }

    private void ExtractRolesFromClaimValue(string claimValue, HashSet<string> targetSet)
    {
        if (string.IsNullOrWhiteSpace(claimValue))
            return;

        var trimmed = claimValue.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        if (elem.ValueKind == JsonValueKind.String)
                        {
                            var role = elem.GetString();
                            if (!string.IsNullOrWhiteSpace(role))
                            {
                                targetSet.Add(role);
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse JSON array in role claim: {ClaimValue}", claimValue);
            }
        }
        else if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (!string.IsNullOrWhiteSpace(prop.Name))
                        {
                            targetSet.Add(prop.Name);
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse JSON object in role claim: {ClaimValue}", claimValue);
            }
        }
        else
        {
            // Plain single string (e.g. "admin" or "authentik Admins")
            targetSet.Add(trimmed);
        }
    }

    private string NormalizeRoleName(string role)
    {
        var mapping = _options.Roles;

        if (mapping.Admin.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return AuthConstants.RoleAdmin;
        }

        if (mapping.Operator.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(role, "operator", StringComparison.OrdinalIgnoreCase))
        {
            return AuthConstants.RoleOperator;
        }

        if (mapping.Viewer.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(role, "viewer", StringComparison.OrdinalIgnoreCase))
        {
            return AuthConstants.RoleViewer;
        }

        if (role.Length > 1)
            return char.ToUpperInvariant(role[0]) + role[1..];

        return role.ToUpperInvariant();
    }
}
