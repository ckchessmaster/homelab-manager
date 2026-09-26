import {
  Server,
  Cloud,
  Cpu,
  Layers,
  AlertTriangle,
  Package,
  CheckCircle2,
  LayoutList,
  LayoutGrid,
} from 'lucide-react'
import type { Host } from '../../api/hosts'
import { isBaremetalHost, isProxmoxHost, isKubernetesHost } from '../../api/hosts'

export type PlatformFilter = 'all' | 'proxmox' | 'kubernetes' | 'baremetal'
export type HealthFilter = 'all' | 'reboot' | 'updates' | 'healthy'
export type ViewMode = 'flat' | 'grouped'

interface HostFilterPillsProps {
  hosts: Host[]
  selectedPlatform: PlatformFilter
  onSelectPlatform: (platform: PlatformFilter) => void
  selectedHealth: HealthFilter
  onSelectHealth: (health: HealthFilter) => void
  viewMode: ViewMode
  onToggleViewMode: (mode: ViewMode) => void
}

export function HostFilterPills({
  hosts,
  selectedPlatform,
  onSelectPlatform,
  selectedHealth,
  onSelectHealth,
  viewMode,
  onToggleViewMode,
}: HostFilterPillsProps) {
  const counts = {
    all: hosts.length,
    proxmox: hosts.filter(isProxmoxHost).length,
    kubernetes: hosts.filter(isKubernetesHost).length,
    baremetal: hosts.filter(isBaremetalHost).length,
    reboot: hosts.filter((h) => h.agent?.pendingReboot).length,
    updates: hosts.filter((h) => (h.agent?.upgradablePackagesCount || 0) > 0).length,
    healthy: hosts.filter(
      (h) =>
        h.agent?.installed &&
        !h.agent.pendingReboot &&
        (h.agent.upgradablePackagesCount || 0) === 0
    ).length,
  }

  const platformChips: {
    id: PlatformFilter
    label: string
    icon: typeof Server
    count: number
    activeClasses: string
  }[] = [
    {
      id: 'all',
      label: 'All Platforms',
      icon: Layers,
      count: counts.all,
      activeClasses: 'bg-zinc-800 text-zinc-100 border-zinc-600 shadow-sm shadow-zinc-950/50',
    },
    {
      id: 'proxmox',
      label: 'Proxmox PVE',
      icon: Cloud,
      count: counts.proxmox,
      activeClasses: 'bg-purple-950/80 text-purple-300 border-purple-700 shadow-sm shadow-purple-950/50',
    },
    {
      id: 'kubernetes',
      label: 'Kubernetes Nodes',
      icon: Cpu,
      count: counts.kubernetes,
      activeClasses: 'bg-sky-950/80 text-sky-300 border-sky-700 shadow-sm shadow-sky-950/50',
    },
    {
      id: 'baremetal',
      label: 'Baremetal / Physical',
      icon: Server,
      count: counts.baremetal,
      activeClasses: 'bg-amber-950/80 text-amber-300 border-amber-700 shadow-sm shadow-amber-950/50',
    },
  ]

  const healthChips: {
    id: HealthFilter
    label: string
    icon: typeof CheckCircle2
    count: number
    activeClasses: string
    dotColor?: string
  }[] = [
    {
      id: 'all',
      label: 'All Health',
      icon: Layers,
      count: counts.all,
      activeClasses: 'bg-zinc-800 text-zinc-100 border-zinc-600',
    },
    {
      id: 'reboot',
      label: 'Reboot Required',
      icon: AlertTriangle,
      count: counts.reboot,
      activeClasses: 'bg-amber-950/80 text-amber-300 border-amber-600',
      dotColor: 'bg-amber-400',
    },
    {
      id: 'updates',
      label: 'Updates Available',
      icon: Package,
      count: counts.updates,
      activeClasses: 'bg-blue-950/80 text-blue-300 border-blue-600',
      dotColor: 'bg-blue-400',
    },
    {
      id: 'healthy',
      label: 'Healthy',
      icon: CheckCircle2,
      count: counts.healthy,
      activeClasses: 'bg-emerald-950/80 text-emerald-300 border-emerald-600',
      dotColor: 'bg-emerald-400',
    },
  ]

  return (
    <div className="flex flex-col lg:flex-row items-stretch lg:items-center justify-between gap-4 p-3 bg-zinc-900/50 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
      {/* Platform & Health Facets */}
      <div className="flex flex-wrap items-center gap-3">
        {/* Platform Segmented Group */}
        <div className="flex items-center gap-1.5 p-1 bg-zinc-950/60 border border-zinc-800/80 rounded-lg overflow-x-auto max-w-full scrollbar-none">
          {platformChips.map((chip) => {
            const Icon = chip.icon
            const isSelected = selectedPlatform === chip.id
            return (
              <button
                key={chip.id}
                type="button"
                onClick={() => onSelectPlatform(chip.id)}
                className={`flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs font-medium border transition-all cursor-pointer select-none ${
                  isSelected
                    ? chip.activeClasses
                    : 'border-transparent text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900/60'
                }`}
              >
                <Icon className="h-3.5 w-3.5" />
                <span>{chip.label}</span>
                <span
                  className={`text-[10px] px-1.5 py-0.2 rounded-full font-mono font-semibold ${
                    isSelected
                      ? 'bg-zinc-950/80 text-zinc-200'
                      : 'bg-zinc-800/80 text-zinc-400'
                  }`}
                >
                  {chip.count}
                </span>
              </button>
            )
          })}
        </div>

        {/* Health Segmented Group */}
        <div className="flex items-center gap-1.5 p-1 bg-zinc-950/60 border border-zinc-800/80 rounded-lg overflow-x-auto max-w-full scrollbar-none">
          {healthChips.map((chip) => {
            const Icon = chip.icon
            const isSelected = selectedHealth === chip.id
            return (
              <button
                key={chip.id}
                type="button"
                onClick={() => onSelectHealth(chip.id)}
                className={`flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs font-medium border transition-all cursor-pointer select-none ${
                  isSelected
                    ? chip.activeClasses
                    : 'border-transparent text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900/60'
                }`}
              >
                {chip.dotColor ? (
                  <span className={`h-1.5 w-1.5 rounded-full ${chip.dotColor}`} />
                ) : (
                  <Icon className="h-3.5 w-3.5" />
                )}
                <span>{chip.label}</span>
                <span
                  className={`text-[10px] px-1.5 py-0.2 rounded-full font-mono font-semibold ${
                    isSelected
                      ? 'bg-zinc-950/80 text-zinc-200'
                      : 'bg-zinc-800/80 text-zinc-400'
                  }`}
                >
                  {chip.count}
                </span>
              </button>
            )
          })}
        </div>
      </div>

      {/* View Mode Toggle: Flat List vs Grouped */}
      <div className="flex items-center gap-1 p-1 bg-zinc-950/70 border border-zinc-800 rounded-lg shrink-0 self-start lg:self-center">
        <button
          type="button"
          onClick={() => onToggleViewMode('flat')}
          className={`flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs font-medium transition-all cursor-pointer ${
            viewMode === 'flat'
              ? 'bg-emerald-600 text-white shadow-sm font-semibold'
              : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900'
          }`}
          title="Flat table view"
        >
          <LayoutList className="h-3.5 w-3.5" />
          <span>Flat List</span>
        </button>
        <button
          type="button"
          onClick={() => onToggleViewMode('grouped')}
          className={`flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs font-medium transition-all cursor-pointer ${
            viewMode === 'grouped'
              ? 'bg-emerald-600 text-white shadow-sm font-semibold'
              : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900'
          }`}
          title="Group by platform clusters"
        >
          <LayoutGrid className="h-3.5 w-3.5" />
          <span>Grouped</span>
        </button>
      </div>
    </div>
  )
}
