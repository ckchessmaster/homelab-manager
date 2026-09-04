import {
  Server,
  GitFork,
  Radio,
  Settings,
  Layers,
  Database,
} from 'lucide-react'
import { Badge } from '../ui/badge'

export type NavTab = 'hosts' | 'workflows' | 'adapters' | 'settings'

interface AppSidebarProps {
  activeTab: NavTab
  onSelectTab: (tab: NavTab) => void
  totalHosts?: number
  rebootPendingCount?: number
}

export function AppSidebar({
  activeTab,
  onSelectTab,
  totalHosts = 0,
  rebootPendingCount = 0,
}: AppSidebarProps) {
  const navItems: {
    id: NavTab
    label: string
    icon: typeof Server
    badge?: string
    badgeVariant?: 'default' | 'success' | 'warning' | 'purple'
    badgeDot?: boolean
  }[] = [
    {
      id: 'hosts',
      label: 'Host Inventory',
      icon: Server,
      badge: totalHosts > 0 ? String(totalHosts) : undefined,
      badgeVariant: rebootPendingCount > 0 ? 'warning' : 'default',
      badgeDot: rebootPendingCount > 0,
    },
    {
      id: 'workflows',
      label: 'Workflows & DAGs',
      icon: GitFork,
      badge: 'M3',
      badgeVariant: 'default',
    },
    {
      id: 'adapters',
      label: 'Adapters & Probes',
      icon: Radio,
      badge: 'Proxmox',
      badgeVariant: 'purple',
    },
    {
      id: 'settings',
      label: 'Settings & Security',
      icon: Settings,
    },
  ]

  return (
    <aside className="w-64 bg-zinc-950/90 border-r border-zinc-800 flex flex-col shrink-0 h-screen sticky top-0">
      {/* Brand Header */}
      <div className="p-5 border-b border-zinc-800/80 flex items-center justify-between">
        <div className="flex items-center gap-2.5">
          <div className="h-8 w-8 rounded-lg bg-gradient-to-br from-emerald-500 to-teal-700 flex items-center justify-center shadow-md shadow-emerald-950">
            <Layers className="h-4 w-4 text-white" />
          </div>
          <div>
            <h1 className="text-sm font-bold text-zinc-100 tracking-tight flex items-center gap-1.5">
              ControlPlane
            </h1>
            <span className="text-[10px] text-zinc-500 font-mono">v0.1.0-alpha • M1</span>
          </div>
        </div>
      </div>

      {/* Navigation Links */}
      <nav className="p-3 space-y-1 flex-1 overflow-y-auto">
        <div className="px-3 py-1.5 text-[10px] font-semibold uppercase tracking-wider text-zinc-500">
          Management
        </div>

        {navItems.map((item) => {
          const Icon = item.icon
          const isActive = activeTab === item.id

          return (
            <button
              key={item.id}
              onClick={() => onSelectTab(item.id)}
              className={`w-full flex items-center justify-between px-3 py-2.5 rounded-lg text-xs font-medium transition-all cursor-pointer ${
                isActive
                  ? 'bg-zinc-800/90 text-white shadow-xs border border-zinc-700/60'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <Icon
                  className={`h-4 w-4 ${
                    isActive ? 'text-emerald-400' : 'text-zinc-500 group-hover:text-zinc-400'
                  }`}
                />
                <span>{item.label}</span>
              </div>

              {item.badge && (
                <Badge
                  variant={item.badgeVariant || 'default'}
                  dot={item.badgeDot}
                  className="text-[10px] px-1.5 py-0"
                >
                  {item.badge}
                </Badge>
              )}
            </button>
          )
        })}
      </nav>

      {/* Standby / Cluster Topology Indicator */}
      <div className="p-3 border-t border-zinc-800/80 bg-zinc-950">
        <div className="p-3 rounded-lg bg-zinc-900/60 border border-zinc-800 space-y-2">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-medium text-zinc-400 flex items-center gap-1.5">
              <Database className="h-3 w-3 text-emerald-400" />
              Storage Engine
            </span>
            <span className="text-[10px] font-mono text-emerald-400 bg-emerald-950/60 px-1.5 py-0.5 rounded border border-emerald-800/50">
              Active
            </span>
          </div>
          <p className="text-[10px] text-zinc-500 leading-tight">
            Dual-topology runtime with zero-downtime offline standby takeover.
          </p>
        </div>
      </div>
    </aside>
  )
}
