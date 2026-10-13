import { describe, it, expect } from 'vitest'
import { parseOidcRoles, getHighestRole, hasRequiredRole, ROLE_HIERARCHY } from './roleUtils'

describe('roleUtils - parseOidcRoles', () => {
  it('returns empty array when profile is undefined', () => {
    expect(parseOidcRoles(undefined)).toEqual([])
  })

  it('extracts roles from default "groups" claim array', () => {
    const profile = {
      sub: 'user-1',
      groups: ['admin', 'developers'],
    }
    const roles = parseOidcRoles(profile)
    expect(roles).toEqual(ROLE_HIERARCHY.Admin)
    expect(hasRequiredRole(roles, 'Admin')).toBe(true)
    expect(hasRequiredRole(roles, 'Operator')).toBe(true)
    expect(hasRequiredRole(roles, 'Viewer')).toBe(true)
    expect(getHighestRole(roles)).toBe('Admin')
  })

  it('extracts operator role from "groups" array', () => {
    const profile = {
      sub: 'user-2',
      groups: ['operator'],
    }
    const roles = parseOidcRoles(profile)
    expect(roles).toEqual(ROLE_HIERARCHY.Operator)
    expect(hasRequiredRole(roles, 'Admin')).toBe(false)
    expect(hasRequiredRole(roles, 'Operator')).toBe(true)
    expect(hasRequiredRole(roles, 'Viewer')).toBe(true)
    expect(getHighestRole(roles)).toBe('Operator')
  })

  it('supports single string in groups claim', () => {
    const profile = {
      sub: 'user-3',
      groups: 'viewer',
    }
    const roles = parseOidcRoles(profile)
    expect(roles).toEqual(ROLE_HIERARCHY.Viewer)
    expect(hasRequiredRole(roles, 'Admin')).toBe(false)
    expect(getHighestRole(roles)).toBe('Viewer')
  })

  it('supports custom roleClaimType and custom role mappings (Authentik groups)', () => {
    const profile = {
      sub: 'user-4',
      custom_roles: ['homelab-superadmins', 'infra-team'],
    }
    const roleMapping = {
      admin: ['homelab-superadmins'],
      operator: ['infra-team'],
      viewer: ['read-only'],
    }
    const roles = parseOidcRoles(profile, roleMapping, 'custom_roles')
    expect(roles).toEqual(ROLE_HIERARCHY.Admin)
  })

  it('falls back to Viewer for authenticated users without recognized roles', () => {
    const profile = {
      sub: 'user-5',
      groups: ['unrelated-group'],
    }
    const roles = parseOidcRoles(profile)
    expect(roles).toEqual(ROLE_HIERARCHY.Viewer)
  })

  it('checks standard roles claim if different from roleClaimType', () => {
    const profile = {
      sub: 'user-6',
      roles: ['operator'],
    }
    const roles = parseOidcRoles(profile, undefined, 'groups')
    expect(roles).toEqual(ROLE_HIERARCHY.Operator)
  })
})
