import type { RoleMappingConfig, UserRole } from './AuthTypes'

export const ROLE_HIERARCHY: Record<UserRole, UserRole[]> = {
  Admin: ['Admin', 'Operator', 'Viewer'],
  Operator: ['Operator', 'Viewer'],
  Viewer: ['Viewer'],
}

export function parseOidcRoles(
  profile?: Record<string, unknown>,
  roleMapping?: RoleMappingConfig,
  roleClaimType = 'groups'
): UserRole[] {
  if (!profile) return []

  const found = new Set<string>()

  // Check the configured role/group claim (default: "groups")
  const claimValue = profile[roleClaimType]
  if (Array.isArray(claimValue)) {
    claimValue.forEach((r) => typeof r === 'string' && found.add(r.toLowerCase()))
  } else if (typeof claimValue === 'string') {
    found.add(claimValue.toLowerCase())
  }

  // Also check standard "roles" claim if distinct from roleClaimType
  if (roleClaimType !== 'roles') {
    const directRoles = profile['roles']
    if (Array.isArray(directRoles)) {
      directRoles.forEach((r) => typeof r === 'string' && found.add(r.toLowerCase()))
    } else if (typeof directRoles === 'string') {
      found.add(directRoles.toLowerCase())
    }
  }

  const adminMatch = roleMapping?.admin?.length
    ? roleMapping.admin.some((r) => found.has(r.toLowerCase()))
    : found.has('admin')

  if (adminMatch || found.has('admin')) {
    return ROLE_HIERARCHY.Admin
  }

  const operatorMatch = roleMapping?.operator?.length
    ? roleMapping.operator.some((r) => found.has(r.toLowerCase()))
    : found.has('operator')

  if (operatorMatch || found.has('operator')) {
    return ROLE_HIERARCHY.Operator
  }

  const viewerMatch = roleMapping?.viewer?.length
    ? roleMapping.viewer.some((r) => found.has(r.toLowerCase()))
    : found.has('viewer')

  if (viewerMatch || found.has('viewer')) {
    return ROLE_HIERARCHY.Viewer
  }

  // Default authenticated fallback
  return ROLE_HIERARCHY.Viewer
}

export function hasRequiredRole(effectiveRoles: UserRole[], required: UserRole): boolean {
  return effectiveRoles.includes(required)
}

export function getHighestRole(roles: UserRole[]): UserRole {
  if (roles.includes('Admin')) return 'Admin'
  if (roles.includes('Operator')) return 'Operator'
  return 'Viewer'
}
