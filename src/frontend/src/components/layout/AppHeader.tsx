import { useQuery } from '@tanstack/react-query'
import { apiClient } from '../../api/client'
import {
  Database,
  AlertTriangle,
  Server,
  Layers,
} from 'lucide-react'
import type { NavTab } from './AppSidebar'
import { useAuthUser } from '../../features/auth/useAuthUser'
import { ApiKeyHeaderMenu } from '../../features/auth/ApiKeyHeaderMenu'
import { UserProfileDropdown } from '../../features/auth/UserProfileDropdown'

interface StorageStatus {
  provider?: string
  hostCount?: number
  leaseCount?: number
  timestamp?: string
}

interface AppHeaderProps {
  activeTab: NavTab
  onOpenSettings: () => void
  totalHosts?: number
  rebootPendingCount?: number
}

export function AppHeader({
  activeTab,
  onOpenSettings,
  totalHosts = 0,
  rebootPendingCount = 0,
}: AppHeaderProps) {
  const { authMode } = useAuthUser()
  const { data: storageStatus } = useQuery<StorageStatus>({
    queryKey: ['storageStatus'],
    queryFn: () => apiClient<StorageStatus>('/api/storage/status'),
    refetchInterval: 15000,
  })

  const tabTitles: Record<NavTab, { title: string; desc: string }> = {
    hosts: {
      title: 'Host Inventory',
      desc: 'Managed physical servers, virtual machines, and Proxmox containers.',
    },
    workloads: {
      title: 'Applications & Workloads',
      desc: 'Multi-cluster Kubernetes deployments, rolling rollouts, and replica autoscaling.',
    },
    discovery: {
      title: 'Service Discovery & Adoption',
      desc: 'Discover hypervisor VMs, LXC containers, and Kubernetes cluster nodes with 1-click import.',
    },
    workflows: {
      title: 'Update Workflows & DAGs',
      desc: 'Directed acyclic graph orchestration with pre-flight checks and rollback.',
    },
    adapters: {
      title: 'Infrastructure Adapters',
      desc: 'Agentless hypervisor, BMC Redfish, network switch, and Kubernetes cluster connectors.',
    },
    settings: {
      title: 'Settings & Security',
      desc: 'Authentication credentials, API keys, and standby synchronization.',
    },
  }

  const currentTab = tabTitles[activeTab]
  const dbProvider = storageStatus?.provider?.includes('Sqlite')
    ? 'SQLite (Standby)'
    : storageStatus?.provider?.includes('Npgsql')
    ? 'PostgreSQL (Cluster)'
    : 'Database Online'

  return (
    <header className="h-14 sm:h-16 border-b border-zinc-800/80 bg-zinc-950/80 backdrop-blur-md px-3 sm:px-6 flex items-center justify-between sticky top-0 z-20">
      <div className="flex items-center gap-2.5 min-w-0">
        <div className="md:hidden h-7 w-7 rounded-lg bg-gradient-to-br from-sky-500 to-sky-700 flex items-center justify-center shrink-0 shadow-xs shadow-sky-950">
          <Layers className="h-4 w-4 text-white" />
        </div>
        <div className="min-w-0">
          <h2 className="text-sm font-semibold text-zinc-100 truncate">{currentTab.title}</h2>
          <p className="text-[11px] text-zinc-400 hidden sm:block truncate">{currentTab.desc}</p>
        </div>
      </div>

      <div className="flex items-center gap-3">
        {/* Vitals Pills */}
        <div className="hidden lg:flex items-center gap-2">
          {/* Storage Engine */}
          <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-zinc-900 border border-zinc-800 text-[11px] text-zinc-300">
            <Database className="h-3 w-3 text-emerald-400" />
            <span>{dbProvider}</span>
          </div>

          {/* Hosts Count */}
          <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-zinc-900 border border-zinc-800 text-[11px] text-zinc-300">
            <Server className="h-3 w-3 text-zinc-400" />
            <span>{totalHosts} Hosts</span>
          </div>

          {/* Reboot Pending Indicator */}
          {rebootPendingCount > 0 && (
            <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-amber-950/60 border border-amber-800/80 text-[11px] text-amber-300 font-medium">
              <AlertTriangle className="h-3 w-3 text-amber-400" />
              <span>{rebootPendingCount} Reboot Pending</span>
            </div>
          )}
        </div>

        {/* Strictly segregated auth mode widgets: API Key mode OR OIDC mode, never both */}
        {authMode === 'api_key' ? (
          <ApiKeyHeaderMenu onOpenSettings={onOpenSettings} />
        ) : (
          <UserProfileDropdown />
        )}
      </div>
    </header>
  )
}
