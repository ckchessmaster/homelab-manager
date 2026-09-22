import { useState } from 'react'
import { Layout } from './components/layout/Layout'
import type { NavTab } from './components/layout/AppSidebar'
import { HostTable } from './features/hosts/HostTable'
import { AddHostModal } from './features/hosts/AddHostModal'
import { DiscoveryView } from './features/discovery/DiscoveryView'
import { AdaptersView } from './features/adapters/AdaptersView'
import { WorkflowsView } from './features/orchestration/WorkflowsView'
import { WorkloadsPage } from './features/workloads/WorkloadsPage'
import { useHosts } from './features/hosts/useHosts'
import {
  Server,
  Database,
  ShieldCheck,
  RotateCcw,
  Sparkles,
} from 'lucide-react'
import { MetricStrip } from './components/ui/metric-strip'
import { getApiKey } from './api/client'
import { CallbackPage } from './features/auth/CallbackPage'
import { AuthGatePage } from './features/auth/AuthGatePage'
import { useAuthUser } from './features/auth/useAuthUser'
import { ErrorBoundary } from './components/ErrorBoundary'

function AuthenticatedApp() {
  const [activeTab, setActiveTab] = useState<NavTab>('hosts')
  const [addHostOpen, setAddHostOpen] = useState(false)
  const { authMode } = useAuthUser()

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
          {/* Sleek Top Metric Strip */}
          <MetricStrip
            items={[
              {
                id: 'total-nodes',
                label: 'Total Managed Nodes',
                value: totalHosts,
                icon: Server,
                iconColor: 'text-sky-400',
                subtext: 'Physical & virtual compute nodes',
              },
              {
                id: 'agents-online',
                label: 'Agents Online',
                value: allHosts?.filter((h) => h.agent?.installed).length ?? 0,
                icon: ShieldCheck,
                iconColor: 'text-emerald-400',
                badge: (allHosts?.filter((h) => h.agent?.installed).length ?? 0) === totalHosts && totalHosts > 0 ? 'All Active' : undefined,
                badgeVariant: 'success',
                subtext: 'Daemon heartbeats active',
              },
              {
                id: 'reboot-required',
                label: 'Reboot Required',
                value: rebootPendingCount,
                icon: RotateCcw,
                iconColor: rebootPendingCount > 0 ? 'text-amber-400' : 'text-zinc-500',
                badge: rebootPendingCount > 0 ? `${rebootPendingCount} Pending` : 'Clean',
                badgeVariant: rebootPendingCount > 0 ? 'warning' : 'default',
                badgeDot: rebootPendingCount > 0,
                subtext: 'Kernel or package flags set',
              },
              {
                id: 'updates-pending',
                label: 'Updates Pending',
                value: allHosts?.reduce((acc, h) => acc + (h.agent?.upgradablePackagesCount || 0), 0) ?? 0,
                icon: Sparkles,
                iconColor: (allHosts?.reduce((acc, h) => acc + (h.agent?.upgradablePackagesCount || 0), 0) ?? 0) > 0 ? 'text-sky-400' : 'text-zinc-500',
                badge: (allHosts?.reduce((acc, h) => acc + (h.agent?.upgradablePackagesCount || 0), 0) ?? 0) > 0 ? 'Upgrades' : 'Up-to-date',
                badgeVariant: (allHosts?.reduce((acc, h) => acc + (h.agent?.upgradablePackagesCount || 0), 0) ?? 0) > 0 ? 'info' : 'default',
                subtext: 'Packages across fleet',
              },
            ]}
          />

          {/* Host Inventory Table */}
          <HostTable onOpenAddModal={() => setAddHostOpen(true)} />

          {/* Add Host Modal */}
          <AddHostModal open={addHostOpen} onClose={() => setAddHostOpen(false)} />
        </div>
      )}

      {activeTab === 'workloads' && (
        <ErrorBoundary fallbackTitle="Applications & Workloads Encountered an Error">
          <WorkloadsPage />
        </ErrorBoundary>
      )}

      {activeTab === 'discovery' && (
        <ErrorBoundary fallbackTitle="Service Discovery Encountered an Error">
          <DiscoveryView onSelectHost={() => setActiveTab('hosts')} />
        </ErrorBoundary>
      )}

      {activeTab === 'adapters' && (
        <ErrorBoundary fallbackTitle="Infrastructure Adapters Encountered an Error">
          <AdaptersView />
        </ErrorBoundary>
      )}

      {activeTab === 'workflows' && (
        <ErrorBoundary fallbackTitle="Workflows & DAGs Encountered an Error">
          <WorkflowsView />
        </ErrorBoundary>
      )}

      {activeTab === 'settings' && (
        <div className="max-w-3xl mx-auto space-y-6">
          <div className="p-6 bg-zinc-900/60 border border-zinc-800 rounded-xl space-y-4">
            <h3 className="text-base font-semibold text-zinc-100 flex items-center gap-2">
              <ShieldCheck className="h-5 w-5 text-emerald-400" />
              Authentication & Security Credentials
            </h3>
            <p className="text-xs text-zinc-400">
              {authMode === 'api_key'
                ? 'ControlPlane is running in API Key mode with full administrative access.'
                : 'ControlPlane is running in OIDC (Single Sign-On) mode with role-based access control.'}
            </p>

            {authMode === 'api_key' && (
              <div className="p-3.5 bg-zinc-950/80 border border-zinc-800 rounded-lg space-y-1 font-mono text-xs">
                <div className="text-zinc-500">// Active Header Format</div>
                <div className="text-emerald-400">X-ControlPlane-Key: {getApiKey()}</div>
              </div>
            )}
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

export default function App() {
  const { isAuthenticated, isLoading } = useAuthUser()

  if (typeof window !== 'undefined' && window.location.pathname.startsWith('/auth/callback')) {
    return <CallbackPage />
  }

  if (isLoading) {
    return (
      <div className="min-h-screen bg-zinc-950 flex flex-col items-center justify-center p-4">
        <div className="flex items-center gap-3 text-zinc-300 font-medium text-sm">
          <div className="h-5 w-5 border-2 border-emerald-500 border-t-transparent rounded-full animate-spin" />
          <span>Authenticating with ControlPlane...</span>
        </div>
      </div>
    )
  }

  if (!isAuthenticated) {
    return <AuthGatePage />
  }

  return <AuthenticatedApp />
}
