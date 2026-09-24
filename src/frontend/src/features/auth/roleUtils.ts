import type { RoleMappingConfig, UserRole } from './AuthTypes'

export const ROLE_HIERARCHY: Record<UserRole, UserRole[]> = {
  Admin: ['Admin', 'Operator', 'Viewer'],
  Operator: ['Operator', 'Viewer'],
  Viewer: ['Viewer'],
}

export function parseZitadelRoles(
  profile?: Record<string, unknown>,
  roleMapping?: RoleMappingConfig
): UserRole[] {
  if (!profile) return []

  const directRoles = profile['roles'] as string[] | undefined
  const found = new Set<string>()

  // Check any project roles claim (e.g. urn:zitadel:iam:org:project:roles or urn:zitadel:iam:org:project:{projectId}:roles)
  Object.keys(profile).forEach((key) => {
    if (key.startsWith('urn:zitadel:iam:org:project') && key.endsWith('roles')) {
      const val = profile[key]
      if (val && typeof val === 'object' && !Array.isArray(val)) {
        Object.keys(val).forEach((r) => found.add(r.toLowerCase()))
      } else if (Array.isArray(val)) {
        val.forEach((r) => typeof r === 'string' && found.add(r.toLowerCase()))
      }
    }
  })

  if (Array.isArray(directRoles)) {
    directRoles.forEach((r) => typeof r === 'string' && found.add(r.toLowerCase()))
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
