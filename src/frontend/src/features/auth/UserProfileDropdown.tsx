import { useState, useRef, useEffect } from 'react'
import { useAuthUser } from './useAuthUser'
import type { UserRole } from './AuthTypes'
import {
  User,
  LogOut,
  LogIn,
  Shield,
  ShieldAlert,
  ShieldCheck,
  ChevronDown,
  Sparkles,
} from 'lucide-react'

export function UserProfileDropdown() {
  const {
    user,
    isAuthenticated,
    isBypass,
    activeRole,
    setActiveRole,
    login,
    logout,
  } = useAuthUser()

  const [isOpen, setIsOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }
    if (isOpen) {
      document.addEventListener('mousedown', handleClickOutside)
    }
    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
    }
  }, [isOpen])

  if (!isAuthenticated) {
    return (
      <button
        onClick={() => login()}
        className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-xs font-medium text-white shadow-sm transition-colors cursor-pointer"
        title="Sign In with Zitadel"
      >
        <LogIn className="h-3.5 w-3.5" />
        <span>Sign In</span>
      </button>
    )
  }

  const roleColors: Record<UserRole, { badge: string; ring: string; icon: typeof Shield }> = {
    Admin: {
      badge: 'bg-purple-950/80 text-purple-300 border-purple-800/80',
      ring: 'border-purple-500/50 text-purple-300 bg-purple-950/40',
      icon: ShieldAlert,
    },
    Operator: {
      badge: 'bg-blue-950/80 text-blue-300 border-blue-800/80',
      ring: 'border-blue-500/50 text-blue-300 bg-blue-950/40',
      icon: ShieldCheck,
    },
    Viewer: {
      badge: 'bg-zinc-800 text-zinc-400 border-zinc-700',
      ring: 'border-zinc-700 text-zinc-400 bg-zinc-900',
      icon: Shield,
    },
  }

  const currentRoleConfig = roleColors[activeRole] || roleColors.Viewer
  const RoleIcon = currentRoleConfig.icon

  const initials = (user?.name || 'CP')
    .split(' ')
    .map((w) => w[0])
    .join('')
    .substring(0, 2)
    .toUpperCase()

  return (
    <div className="relative" ref={menuRef}>
      <button
        onClick={() => setIsOpen(!isOpen)}
        className="flex items-center gap-2 p-1.5 pr-2.5 rounded-lg bg-zinc-900/90 hover:bg-zinc-800/90 border border-zinc-800 text-xs text-zinc-200 transition-all cursor-pointer shadow-sm hover:border-zinc-700"
        aria-haspopup="true"
        aria-expanded={isOpen}
      >
        <div
          className={`h-6 w-6 rounded-full border flex items-center justify-center font-semibold text-[10px] ${currentRoleConfig.ring}`}
        >
          {initials}
        </div>
        <div className="flex flex-col items-start leading-none hidden md:flex">
          <span className="text-[11px] font-medium text-zinc-200 max-w-[110px] truncate">
            {user?.name || 'Authenticated'}
          </span>
          <span className="text-[9px] text-zinc-500 mt-0.5">{activeRole}</span>
        </div>
        <ChevronDown className="h-3 w-3 text-zinc-400" />
      </button>

      {isOpen && (
        <div className="absolute right-0 mt-2 w-64 rounded-xl bg-zinc-950 border border-zinc-800/90 shadow-2xl p-3 z-50 animate-in fade-in slide-in-from-top-1 duration-150 backdrop-blur-md">
          {/* User Info Header */}
          <div className="flex items-start gap-3 pb-3 border-b border-zinc-800/80">
            <div
              className={`h-9 w-9 rounded-full border flex items-center justify-center font-semibold text-xs shrink-0 ${currentRoleConfig.ring}`}
            >
              {initials}
            </div>
            <div className="flex-1 min-w-0">
              <div className="text-xs font-semibold text-zinc-100 truncate">
                {user?.name}
              </div>
              <div className="text-[11px] text-zinc-400 truncate mt-0.5">
                {user?.email || 'No email associated'}
              </div>
              <div className="mt-1.5 flex items-center gap-1.5">
                <span
                  className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10px] font-medium border ${currentRoleConfig.badge}`}
                >
                  <RoleIcon className="h-3 w-3" />
                  {activeRole}
                </span>
                {isBypass && (
                  <span className="px-1.5 py-0.5 rounded text-[9px] bg-amber-950/60 border border-amber-800/60 text-amber-300 font-mono">
                    Dev Bypass
                  </span>
                )}
              </div>
            </div>
          </div>

          {/* Dev Bypass Role Switcher */}
          {isBypass && setActiveRole && (
            <div className="py-2.5 border-b border-zinc-800/80">
              <div className="text-[10px] font-medium uppercase tracking-wider text-zinc-400 mb-1.5 flex items-center gap-1">
                <Sparkles className="h-3 w-3 text-amber-400" />
                Simulate Role (Dev Only)
              </div>
              <div className="grid grid-cols-3 gap-1">
                {(['Admin', 'Operator', 'Viewer'] as UserRole[]).map((r) => {
                  const isCurrent = activeRole === r
                  return (
                    <button
                      key={r}
                      onClick={() => setActiveRole(r)}
                      className={`px-2 py-1 rounded text-[11px] font-medium transition-colors cursor-pointer text-center ${
                        isCurrent
                          ? 'bg-zinc-800 text-zinc-100 border border-zinc-700 shadow-sm'
                          : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900'
                      }`}
                    >
                      {r}
                    </button>
                  )
                })}
              </div>
            </div>
          )}

          {/* Details / Actions */}
          <div className="pt-2 space-y-1">
            <div className="px-2 py-1 text-[10px] text-zinc-500">
              {isBypass
                ? 'Running in local dev/standby bypass mode'
                : 'Authenticated via Zitadel OIDC (PKCE)'}
            </div>

            {!isBypass ? (
              <button
                onClick={() => logout()}
                className="w-full flex items-center gap-2 px-2.5 py-1.5 rounded-lg text-xs text-rose-400 hover:bg-rose-950/30 hover:text-rose-300 transition-colors cursor-pointer"
              >
                <LogOut className="h-3.5 w-3.5" />
                <span>Sign Out</span>
              </button>
            ) : (
              <div className="px-2 py-1 text-[11px] text-zinc-400 flex items-center gap-1.5">
                <User className="h-3.5 w-3.5 text-zinc-500" />
                <span>Single-runner mode</span>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
