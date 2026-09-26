import { useState } from 'react'
import {
  Server,
  Compass,
  GitFork,
  Radio,
  Settings,
  Layers,
  MoreHorizontal,
  Key,
  Database,
  ExternalLink,
} from 'lucide-react'
import { Sheet, SheetHeader, SheetTitle, SheetDescription, SheetBody } from '../ui/sheet'
import { Badge } from '../ui/badge'
import type { NavTab } from './AppSidebar'

interface MobileBottomNavProps {
  activeTab: NavTab
  onSelectTab: (tab: NavTab) => void
  totalHosts?: number
  rebootPendingCount?: number
  onOpenSettings?: () => void
}

export function MobileBottomNav({
  activeTab,
  onSelectTab,
  totalHosts = 0,
  rebootPendingCount = 0,
  onOpenSettings,
}: MobileBottomNavProps) {
  const [moreOpen, setMoreOpen] = useState(false)

  const primaryTabs: {
    id: NavTab
    label: string
    icon: typeof Server
    badgeDot?: boolean
  }[] = [
    {
      id: 'hosts',
      label: 'Hosts',
      icon: Server,
      badgeDot: rebootPendingCount > 0,
    },
    {
      id: 'workloads',
      label: 'Workloads',
      icon: Layers,
    },
    {
      id: 'discovery',
      label: 'Discovery',
      icon: Compass,
    },
    {
      id: 'workflows',
      label: 'Workflows',
      icon: GitFork,
    },
  ]

  const handleSelectTab = (tab: NavTab) => {
    onSelectTab(tab)
    setMoreOpen(false)
  }

  const isMoreActive = activeTab === 'adapters' || activeTab === 'settings'

  return (
    <>
      <nav
        aria-label="Mobile Navigation Bar"
        className="fixed bottom-0 inset-x-0 z-40 bg-zinc-950/95 backdrop-blur-md border-t border-zinc-800 md:hidden pb-[calc(env(safe-area-inset-bottom,0px)+0.25rem)] shadow-2xl"
      >
        <div className="grid grid-cols-5 h-14 items-center">
          {primaryTabs.map((tab) => {
            const Icon = tab.icon
            const isActive = activeTab === tab.id

            return (
              <button
                key={tab.id}
                type="button"
                onClick={() => handleSelectTab(tab.id)}
                className={`flex flex-col items-center justify-center h-full min-h-[44px] transition-colors relative cursor-pointer ${
                  isActive ? 'text-sky-400' : 'text-zinc-400 hover:text-zinc-200'
                }`}
                aria-current={isActive ? 'page' : undefined}
                aria-label={tab.label}
              >
                <div className="relative">
                  <Icon className="w-5 h-5" />
                  {tab.badgeDot && (
                    <span className="absolute -top-0.5 -right-0.5 w-2 h-2 rounded-full bg-amber-400 ring-2 ring-zinc-950 animate-pulse" />
                  )}
                </div>
                <span className="text-[10px] font-medium tracking-tight mt-0.5">
                  {tab.label}
                </span>
                {isActive && (
                  <span className="absolute top-0 inset-x-4 h-0.5 bg-sky-500 rounded-full" />
                )}
              </button>
            )
          })}

          {/* "More" Sheet Trigger */}
          <button
            type="button"
            onClick={() => setMoreOpen(true)}
            className={`flex flex-col items-center justify-center h-full min-h-[44px] transition-colors relative cursor-pointer ${
              isMoreActive ? 'text-sky-400' : 'text-zinc-400 hover:text-zinc-200'
            }`}
            aria-label="More views and options"
          >
            <MoreHorizontal className="w-5 h-5" />
            <span className="text-[10px] font-medium tracking-tight mt-0.5">
              More
            </span>
            {isMoreActive && (
              <span className="absolute top-0 inset-x-4 h-0.5 bg-sky-500 rounded-full" />
            )}
          </button>
        </div>
      </nav>

      {/* "More" Drawer / Sheet */}
      <Sheet open={moreOpen} onClose={() => setMoreOpen(false)} width="w-full sm:w-[380px]">
        <SheetHeader onClose={() => setMoreOpen(false)}>
          <div className="flex items-center gap-2">
            <div className="h-6 w-6 rounded-md bg-gradient-to-br from-sky-500 to-sky-700 flex items-center justify-center">
              <Layers className="h-3.5 w-3.5 text-white" />
            </div>
            <SheetTitle>ControlPlane Navigation</SheetTitle>
          </div>
          <SheetDescription>Additional homelab views, adapters & settings</SheetDescription>
        </SheetHeader>

        <SheetBody className="p-4 space-y-4">
          <div className="space-y-1">
            <div className="px-3 py-1 text-[10px] font-semibold uppercase tracking-wider text-zinc-500">
              Infrastructure Views
            </div>

            <button
              type="button"
              onClick={() => handleSelectTab('adapters')}
              className={`w-full flex items-center justify-between p-3 rounded-xl text-left transition-colors cursor-pointer border ${
                activeTab === 'adapters'
                  ? 'bg-zinc-800 text-white border-zinc-700'
                  : 'bg-zinc-900/60 text-zinc-300 border-zinc-800 hover:bg-zinc-800/60'
              }`}
            >
              <div className="flex items-center gap-3">
                <Radio className={`w-5 h-5 ${activeTab === 'adapters' ? 'text-sky-400' : 'text-zinc-400'}`} />
                <div>
                  <div className="text-xs font-semibold text-zinc-100">Infrastructure Adapters</div>
                  <div className="text-[11px] text-zinc-400">Proxmox, UniFi, OPNsense, Redfish & Home Assistant</div>
                </div>
              </div>
              <Badge variant="purple" className="text-[10px] shrink-0">Hub</Badge>
            </button>

            <button
              type="button"
              onClick={() => handleSelectTab('settings')}
              className={`w-full flex items-center justify-between p-3 rounded-xl text-left transition-colors cursor-pointer border mt-2 ${
                activeTab === 'settings'
                  ? 'bg-zinc-800 text-white border-zinc-700'
                  : 'bg-zinc-900/60 text-zinc-300 border-zinc-800 hover:bg-zinc-800/60'
              }`}
            >
              <div className="flex items-center gap-3">
                <Settings className={`w-5 h-5 ${activeTab === 'settings' ? 'text-sky-400' : 'text-zinc-400'}`} />
                <div>
                  <div className="text-xs font-semibold text-zinc-100">System & Settings</div>
                  <div className="text-[11px] text-zinc-400">Logs, Agent binaries, PAT tokens & config</div>
                </div>
              </div>
            </button>
          </div>

          {/* Quick Actions */}
          <div className="space-y-1 pt-2 border-t border-zinc-800">
            <div className="px-3 py-1 text-[10px] font-semibold uppercase tracking-wider text-zinc-500">
              Security & Environment
            </div>

            {onOpenSettings && (
              <button
                type="button"
                onClick={() => {
                  setMoreOpen(false)
                  onOpenSettings()
                }}
                className="w-full flex items-center justify-between p-3 rounded-xl bg-zinc-900/40 text-zinc-300 border border-zinc-800 hover:bg-zinc-800/50 transition-colors cursor-pointer"
              >
                <div className="flex items-center gap-2.5">
                  <Key className="w-4 h-4 text-emerald-400" />
                  <span className="text-xs font-medium">ControlPlane API Key</span>
                </div>
                <ExternalLink className="w-3.5 h-3.5 text-zinc-500" />
              </button>
            )}

            <div className="p-3 rounded-xl bg-zinc-900/40 border border-zinc-800 mt-2 space-y-1">
              <div className="flex items-center justify-between">
                <span className="text-xs font-medium text-zinc-300 flex items-center gap-1.5">
                  <Database className="w-3.5 h-3.5 text-sky-400" />
                  Storage Runtime
                </span>
                <span className="text-[10px] font-mono text-emerald-400 bg-emerald-950/60 px-1.5 py-0.5 rounded border border-emerald-800/50">
                  {totalHosts} Nodes
                </span>
              </div>
              <p className="text-[10px] text-zinc-400">
                Dual-topology active with automatic offline standby failover.
              </p>
            </div>
          </div>

          <div className="pt-2 text-center">
            <span className="text-[10px] font-mono text-zinc-400">ControlPlane v1.3.1</span>
          </div>
        </SheetBody>
      </Sheet>
    </>
  )
}
