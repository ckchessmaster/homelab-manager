using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Security;

/// <summary>
/// Transforms incoming Zitadel JWT claims into standard ASP.NET Core Role claims (ClaimTypes.Role).
/// Zitadel emits project roles under 'urn:zitadel:iam:org:project:roles' as either a JSON object or array.
/// </summary>
public class ZitadelRoleClaimsTransformation : IClaimsTransformation
{
    private readonly ILogger<ZitadelRoleClaimsTransformation> _logger;
    private readonly ZitadelJwtOptions _options;

    public ZitadelRoleClaimsTransformation(
        ILogger<ZitadelRoleClaimsTransformation> logger,
        IOptions<ZitadelJwtOptions>? options = null)
    {
        _logger = logger;
        _options = options?.Value ?? new ZitadelJwtOptions();
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

        // 1. Process Zitadel project roles claim: urn:zitadel:iam:org:project:roles or urn:zitadel:iam:org:project:{projectId}:roles
        foreach (var claim in principal.Claims.Where(c => 
            c.Type.StartsWith("urn:zitadel:iam:org:project", StringComparison.OrdinalIgnoreCase) &&
            c.Type.EndsWith("roles", StringComparison.OrdinalIgnoreCase)))
        {
            ExtractRolesFromClaimValue(claim.Value, extractedRoles);
        }

        // 2. Process standard role/roles claims if present
        foreach (var claim in principal.FindAll(c => c.Type.Equals("role", StringComparison.OrdinalIgnoreCase) ||
                                                     c.Type.Equals("roles", StringComparison.OrdinalIgnoreCase)))
        {
            ExtractRolesFromClaimValue(claim.Value, extractedRoles);
        }

        // 3. Inject normalized ClaimTypes.Role claims
        foreach (var rawRole in extractedRoles)
        {
            var normalized = NormalizeRoleName(rawRole);
            if (!string.IsNullOrEmpty(normalized) && existingRoles.Add(normalized))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, normalized));
                _logger.LogTrace("Added role claim '{Role}' to principal '{Name}'.", normalized, identity.Name);
            }
        }

        // 4. Role hierarchy: Admin implies Operator and Viewer; Operator implies Viewer
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

        return Task.FromResult(principal);
    }

    private void ExtractRolesFromClaimValue(string claimValue, HashSet<string> targetSet)
    {
        if (string.IsNullOrWhiteSpace(claimValue))
            return;

        var trimmed = claimValue.Trim();
        if (trimmed.StartsWith('{'))
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
        else if (trimmed.StartsWith('['))
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
        else
        {
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
