namespace ControlPlane.Api.Security;

/// <summary>
/// Defines standard constants for authentication schemes, roles, and authorization policies.
/// </summary>
public static class AuthConstants
{
    // Authentication Scheme Names
    public const string CompositeScheme = "Composite";
    public const string ApiKeyScheme = "ApiKey";
    public const string JwtBearerScheme = "Bearer";

    // Standard ControlPlane Roles
    public const string RoleAdmin = "Admin";
    public const string RoleOperator = "Operator";
    public const string RoleViewer = "Viewer";

    // Authorization Policies
    public const string RequireAdmin = "RequireAdmin";
    public const string RequireOperator = "RequireOperator";
    public const string RequireViewer = "RequireViewer";

    // Legacy Policy Aliases
    public const string AdminPolicy = "AdminPolicy";
    public const string OperatorPolicy = "OperatorPolicy";

    // Zitadel Project Roles Claim Type
    public const string ZitadelRolesClaimType = "urn:zitadel:iam:org:project:roles";
}
