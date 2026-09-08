import type { ReactNode } from 'react'
import { useAuthUser } from './useAuthUser'
import type { UserRole } from './AuthTypes'

interface RoleGateProps {
  requiredRole: UserRole
  children: ReactNode
  mode?: 'hide' | 'disable'
  fallback?: ReactNode
}

export function RoleGate({
  requiredRole,
  children,
  mode = 'hide',
  fallback = null,
}: RoleGateProps) {
  const { hasRole } = useAuthUser()
  const allowed = hasRole(requiredRole)

  if (allowed) {
    return <>{children}</>
  }

  if (mode === 'disable') {
    return (
      <div
        className="inline-block opacity-50 cursor-not-allowed"
        title={`Requires ${requiredRole} permission or higher`}
      >
        <div className="pointer-events-none select-none">
          {children}
        </div>
      </div>
    )
  }

  return <>{fallback}</>
}
