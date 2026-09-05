import { useState } from 'react'
import { Layout } from './components/layout/Layout'
import type { NavTab } from './components/layout/AppSidebar'
import { HostTable } from './features/hosts/HostTable'
import { AddHostModal } from './features/hosts/AddHostModal'
import { DiscoveryView } from './features/discovery/DiscoveryView'
import { ProxmoxProbeView } from './features/adapters/ProxmoxProbeView'
import { WorkflowsView } from './features/orchestration/WorkflowsView'
import { useHosts } from './features/hosts/useHosts'
import {
  Server,
  Database,
  ShieldCheck,
} from 'lucide-react'
import { Badge } from './components/ui/badge'
import { getApiKey } from './api/client'

export default function App() {
  const [activeTab, setActiveTab] = useState<NavTab>('hosts')
  const [addHostOpen, setAddHostOpen] = useState(false)

  const { data: allHosts } = useHosts()
  const totalHosts = allHosts?.length ?? 0
  const rebootPendingCount = allHosts?.filter((h) => h.agent?.pendingReboot).length ?? 0

  return (
    <Layout
      activeTab={activeTab}
      onSelectTab={setActiveTab}
      totalHosts={totalHosts}
      rebootPendingCount={rebootPendingCount}
    >
      {activeTab === 'hosts' && (
        <div className="space-y-6 w-full max-w-[1700px] mx-auto">
          {/* Quick Metrics Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
              <div className="flex items-center justify-between">
                <span className="text-xs font-medium text-zinc-400">Total Managed Nodes</span>
                <Server className="h-4 w-4 text-emerald-400" />
              </div>
              <div className="mt-2 text-2xl font-bold text-zinc-100">{totalHosts}</div>
              <p className="text-[11px] text-zinc-500 mt-0.5">Physical & virtual instances</p>
            </div>

            <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
              <div className="flex items-center justify-between">
                <span className="text-xs font-medium text-zinc-400">Agents Online</span>
                <span className="h-2 w-2 rounded-full bg-emerald-400 animate-ping inline-block" />
              </div>
              <div className="mt-2 text-2xl font-bold text-zinc-100">
                {allHosts?.filter((h) => h.agent?.installed).length ?? 0}
              </div>
              <p className="text-[11px] text-zinc-500 mt-0.5">Daemon heartbeats active</p>
            </div>

            <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
              <div className="flex items-center justify-between">
                <span className="text-xs font-medium text-zinc-400">Reboot Required</span>
                <Badge variant={rebootPendingCount > 0 ? 'warning' : 'default'}>
                  {rebootPendingCount}
                </Badge>
              </div>
              <div className="mt-2 text-2xl font-bold text-zinc-100">{rebootPendingCount}</div>
              <p className="text-[11px] text-zinc-500 mt-0.5">Kernel/package flags set</p>
            </div>

            <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
              <div className="flex items-center justify-between">
                <span className="text-xs font-medium text-zinc-400">Updates Pending</span>
                <Badge variant="info">
                  {allHosts?.reduce((acc, h) => acc + (h.agent?.upgradablePackagesCount || 0), 0) ?? 0}
                </Badge>
              </div>
              <div className="mt-2 text-2xl font-bold text-zinc-100">
                {allHosts?.reduce((acc, h) => acc + (h.agent?.upgradablePackagesCount || 0), 0) ?? 0}
              </div>
              <p className="text-[11px] text-zinc-500 mt-0.5">Packages across fleet</p>
            </div>
          </div>

          {/* Host Inventory Table */}
          <HostTable onOpenAddModal={() => setAddHostOpen(false)} />

          {/* Add Host Modal */}
          <AddHostModal open={addHostOpen} onClose={() => setAddHostOpen(false)} />
        </div>
      )}

      {activeTab === 'discovery' && (
        <DiscoveryView onSelectHost={() => setActiveTab('hosts')} />
      )}

      {activeTab === 'adapters' && (
        <div className="space-y-6">
          <ProxmoxProbeView />
        </div>
      )}

      {activeTab === 'workflows' && (
        <WorkflowsView />
      )}

      {activeTab === 'settings' && (
        <div className="max-w-3xl mx-auto space-y-6">
          <div className="p-6 bg-zinc-900/60 border border-zinc-800 rounded-xl space-y-4">
            <h3 className="text-base font-semibold text-zinc-100 flex items-center gap-2">
              <ShieldCheck className="h-5 w-5 text-emerald-400" />
              Authentication & Security Credentials
            </h3>
            <p className="text-xs text-zinc-400">
              The ControlPlane BFF enforces API key authentication for all data modification operations. In development mode, dev bypass can be enabled via <code>AUTH_BYPASS=true</code>.
            </p>

            <div className="p-3.5 bg-zinc-950/80 border border-zinc-800 rounded-lg space-y-1 font-mono text-xs">
              <div className="text-zinc-500">// Active Header Format</div>
              <div className="text-emerald-400">X-ControlPlane-Key: {getApiKey()}</div>
            </div>
          </div>

          <div className="p-6 bg-zinc-900/60 border border-zinc-800 rounded-xl space-y-4">
            <h3 className="text-base font-semibold text-zinc-100 flex items-center gap-2">
              <Database className="h-5 w-5 text-sky-400" />
              Dual-Topology Storage Architecture
            </h3>
            <p className="text-xs text-zinc-400 leading-relaxed">
              When operating inside Kubernetes, ControlPlane runs against PostgreSQL. During node reboot or maintenance windows, the autonomous <strong>Standby Runner</strong> takes over locally using SQLite and lease lock coordination (<code>GLOBAL_MAINTENANCE_LOCK</code>).
            </p>
          </div>
        </div>
      )}
    </Layout>
  )
}
