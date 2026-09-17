import { useState, useMemo } from 'react'
import {
  Layers,
  Cpu,
  Search,
  RefreshCw,
  AlertTriangle,
  CheckCircle2,
  Boxes,
  Plus,
  Globe,
  HardDrive,
  Activity,
  Trash2,
  Package,
  KeyRound,
} from 'lucide-react'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { Button } from '../../components/ui/button'
import { useWorkloads } from './useWorkloads'
import { WorkloadCard } from './WorkloadCard'
import { ScaleWorkloadModal } from './ScaleWorkloadModal'
import { PodDetailDrawer } from './PodDetailDrawer'
import { AppEditorDrawer } from './AppEditorDrawer'
import { DeleteAppModal } from './DeleteAppModal'
import { DeleteNamespaceModal } from './DeleteNamespaceModal'
import { CreateNamespaceModal } from './CreateNamespaceModal'
import { ManageNamespacesModal } from './ManageNamespacesModal'
import { NetworkIngressView } from './NetworkIngressView'
import { StorageView } from './StorageView'
import { ClusterVitalsView } from './ClusterVitalsView'
import { HelmReleasesView } from './helm/HelmReleasesView'
import { ConfigSecretsView } from './ConfigSecretsView'
import { CreateResourceModal } from './CreateResourceModal'
import { useAuthUser } from '../auth/useAuthUser'
import { isSystemCriticalNamespace, type WorkloadSummary } from '../../api/workloads'

type SubTab = 'applications' | 'helm' | 'config' | 'network' | 'storage' | 'vitals'

export function WorkloadsPage() {
  const [currentSubTab, setCurrentSubTab] = useState<SubTab>('applications')
  const [searchTerm, setSearchTerm] = useState('')
  const [selectedCluster, setSelectedCluster] = useState('')
  const [selectedNamespace, setSelectedNamespace] = useState('')
  const [kindFilter, setKindFilter] = useState<'all' | 'Deployment' | 'StatefulSet' | 'DaemonSet' | 'CronJob'>('all')
  const [statusFilter, setStatusFilter] = useState<'all' | 'ready' | 'degraded' | 'progressing' | 'scaleddown'>('all')

  // Modals & Drawers
  const [scalingWorkload, setScalingWorkload] = useState<WorkloadSummary | null>(null)
  const [inspectPodsWorkload, setInspectPodsWorkload] = useState<WorkloadSummary | null>(null)
  const [editorDrawerOpen, setEditorDrawerOpen] = useState(false)
  const [editingWorkload, setEditingWorkload] = useState<WorkloadSummary | null>(null)
  const [deletingWorkload, setDeletingWorkload] = useState<WorkloadSummary | null>(null)
  const [isDeleteNamespaceOpen, setIsDeleteNamespaceOpen] = useState(false)
  const [isManageNamespacesOpen, setIsManageNamespacesOpen] = useState(false)
  const [isCreateNamespaceOpen, setIsCreateNamespaceOpen] = useState(false)
  const [isCreateResourceOpen, setIsCreateResourceOpen] = useState(false)
  const [createResourceInitialTab, setCreateResourceInitialTab] = useState<'secret' | 'configmap' | 'yaml'>('secret')

  const { isOperator } = useAuthUser()

  const {
    data: result,
    isLoading,
    isError,
    error,
    refetch,
    isFetching,
  } = useWorkloads(selectedCluster || undefined, selectedNamespace || undefined)

  const items = result?.items ?? []
  const clusters = result?.clusters ?? []
  const namespaces = result?.namespaces ?? []

  const activeClusterId = selectedCluster || clusters[0] || ''

  // Filter client-side by kind, search query, and status chip
  const filteredItems = useMemo(() => {
    return items.filter((w) => {
      // Kind filter
      if (kindFilter !== 'all' && w.kind !== kindFilter) return false

      // Status filter
      if (statusFilter === 'ready' && w.status !== 'Ready') return false
      if (statusFilter === 'degraded' && w.status !== 'Degraded') return false
      if (statusFilter === 'progressing' && w.status !== 'Progressing') return false
      if (statusFilter === 'scaleddown' && w.status !== 'ScaledDown') return false

      // Search query
      if (searchTerm.trim()) {
        const q = searchTerm.toLowerCase()
        const matchesName = w.name.toLowerCase().includes(q)
        const matchesNs = w.namespace.toLowerCase().includes(q)
        const matchesImg = w.images.some((img) => img.toLowerCase().includes(q))
        if (!matchesName && !matchesNs && !matchesImg) return false
      }

      return true
    })
  }, [items, kindFilter, statusFilter, searchTerm])

  const statusChips: {
    id: typeof statusFilter
    label: string
    color: string
    count: number
  }[] = [
    { id: 'all', label: 'All', color: 'bg-zinc-800 text-zinc-200', count: items.length },
    {
      id: 'ready',
      label: 'Ready',
      color: 'bg-emerald-950/80 text-emerald-300 border-emerald-700',
      count: items.filter((w) => w.status === 'Ready').length,
    },
    {
      id: 'degraded',
      label: 'Degraded',
      color: 'bg-rose-950/80 text-rose-300 border-rose-700',
      count: items.filter((w) => w.status === 'Degraded').length,
    },
    {
      id: 'progressing',
      label: 'Progressing',
      color: 'bg-amber-950/80 text-amber-300 border-amber-700',
      count: items.filter((w) => w.status === 'Progressing').length,
    },
    {
      id: 'scaleddown',
      label: 'Scaled Down',
      color: 'bg-zinc-900 text-zinc-400 border-zinc-700',
      count: items.filter((w) => w.status === 'ScaledDown').length,
    },
  ]

  return (
    <div className="space-y-6 w-full max-w-[1700px] mx-auto">
      {/* Top Header with Primary Sub-Navigation & "+ Create App" */}
      <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-4 p-4 rounded-2xl bg-zinc-900/60 border border-zinc-800/90 backdrop-blur-md">
        <div className="flex items-center gap-2 overflow-x-auto">
          <button
            type="button"
            onClick={() => setCurrentSubTab('applications')}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer ${
              currentSubTab === 'applications'
                ? 'bg-sky-600 text-white shadow-md'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60'
            }`}
          >
            <Boxes className="h-4 w-4" />
            <span>Applications & Workloads</span>
          </button>

          <button
            type="button"
            onClick={() => setCurrentSubTab('helm')}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer ${
              currentSubTab === 'helm'
                ? 'bg-indigo-600 text-white shadow-md'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60'
            }`}
          >
            <Package className="h-4 w-4" />
            <span>Helm Releases</span>
          </button>

          <button
            type="button"
            onClick={() => setCurrentSubTab('config')}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer ${
              currentSubTab === 'config'
                ? 'bg-purple-600 text-white shadow-md'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60'
            }`}
          >
            <KeyRound className="h-4 w-4" />
            <span>Config & Secrets</span>
          </button>

          <button
            type="button"
            onClick={() => setCurrentSubTab('network')}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer ${
              currentSubTab === 'network'
                ? 'bg-sky-600 text-white shadow-md'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60'
            }`}
          >
            <Globe className="h-4 w-4" />
            <span>Network & Ingress</span>
          </button>

          <button
            type="button"
            onClick={() => setCurrentSubTab('storage')}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer ${
              currentSubTab === 'storage'
                ? 'bg-sky-600 text-white shadow-md'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60'
            }`}
          >
            <HardDrive className="h-4 w-4" />
            <span>Storage & Volumes (Longhorn)</span>
          </button>

          <button
            type="button"
            onClick={() => setCurrentSubTab('vitals')}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer ${
              currentSubTab === 'vitals'
                ? 'bg-sky-600 text-white shadow-md'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60'
            }`}
          >
            <Activity className="h-4 w-4" />
            <span>Cluster Vitals & Nodes</span>
          </button>
        </div>

        {isOperator && (
          <div className="flex items-center gap-2 shrink-0">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setIsCreateNamespaceOpen(true)}
              className="border-zinc-700 bg-zinc-900/80 hover:bg-zinc-800 text-zinc-300 text-xs gap-1.5"
            >
              <Layers className="h-4 w-4 text-amber-400" />
              New Namespace
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                setCreateResourceInitialTab('secret')
                setIsCreateResourceOpen(true)
              }}
              className="border-purple-700/60 bg-zinc-900/80 hover:bg-zinc-800 text-purple-300 hover:text-purple-200 text-xs gap-1.5"
            >
              <KeyRound className="h-4 w-4 text-purple-400" />
              New Resource
            </Button>
            <Button
              size="sm"
              onClick={() => {
                setEditingWorkload(null)
                setEditorDrawerOpen(true)
              }}
              className="bg-sky-600 hover:bg-sky-500 text-white text-xs gap-1.5"
            >
              <Plus className="h-4 w-4" />
              Create Application
            </Button>
          </div>
        )}
      </div>

      {/* View Content based on active SubTab */}
      {currentSubTab === 'helm' && (
        <HelmReleasesView
          activeClusterId={activeClusterId}
          selectedNamespace={selectedNamespace || undefined}
          availableNamespaces={namespaces}
        />
      )}

      {currentSubTab === 'config' && (
        <ConfigSecretsView
          clusterId={activeClusterId}
          selectedNamespace={selectedNamespace || undefined}
          availableNamespaces={namespaces}
          onOpenCreateResource={(tab) => {
            setCreateResourceInitialTab(tab || 'secret')
            setIsCreateResourceOpen(true)
          }}
        />
      )}

      {currentSubTab === 'network' && (
        <NetworkIngressView
          clusterId={activeClusterId}
          selectedNamespace={selectedNamespace || undefined}
        />
      )}

      {currentSubTab === 'storage' && (
        <StorageView
          clusterId={activeClusterId}
          selectedNamespace={selectedNamespace || undefined}
        />
      )}

      {currentSubTab === 'vitals' && (
        <ClusterVitalsView clusterId={activeClusterId} />
      )}

      {currentSubTab === 'applications' && (
        <>
          {/* Metric Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
              <div className="flex items-center justify-between">
                <span className="text-xs font-medium text-zinc-400">Total Workloads</span>
                <Boxes className="h-4 w-4 text-sky-400" />
              </div>
              <div className="mt-2 text-2xl font-bold text-zinc-100">
                {result?.totalDeployments ?? 0}
              </div>
              <p className="text-[11px] text-zinc-500 mt-0.5">Deployments, StatefulSets & CronJobs</p>
            </div>

            <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
              <div className="flex items-center justify-between">
                <span className="text-xs font-medium text-zinc-400">Healthy Workloads</span>
                <CheckCircle2 className="h-4 w-4 text-emerald-400" />
              </div>
              <div className="mt-2 text-2xl font-bold text-emerald-400">
                {result?.healthyDeployments ?? 0}
              </div>
              <p className="text-[11px] text-zinc-500 mt-0.5">100% ready replicas</p>
            </div>

            <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
              <div className="flex items-center justify-between">
                <span className="text-xs font-medium text-zinc-400">Connected Clusters</span>
                <Cpu className="h-4 w-4 text-purple-400" />
              </div>
              <div className="mt-2 text-2xl font-bold text-zinc-100">
                {clusters.length}
              </div>
              <p className="text-[11px] text-zinc-500 mt-0.5">Active control planes</p>
            </div>

            <div
              onClick={() => setIsManageNamespacesOpen(true)}
              className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm cursor-pointer hover:border-amber-500/40 hover:bg-zinc-900/80 transition-all group"
              title="Click to view and manage namespaces"
            >
              <div className="flex items-center justify-between">
                <span className="text-xs font-medium text-zinc-400 group-hover:text-amber-300 transition-colors">Total Namespaces</span>
                <Layers className="h-4 w-4 text-amber-400 group-hover:scale-110 transition-transform" />
              </div>
              <div className="mt-2 text-2xl font-bold text-zinc-100">
                {namespaces.length}
              </div>
              <p className="text-[11px] text-zinc-500 mt-0.5">Click to inspect & manage</p>
            </div>
          </div>

          {/* Toolbar & Filters */}
          <div className="flex flex-col md:flex-row gap-3 items-stretch md:items-center justify-between p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-md">
            <div className="flex flex-1 flex-wrap items-center gap-3">
              {/* Search Box */}
              <div className="relative min-w-[200px] flex-1 max-w-sm">
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-zinc-500" />
                <Input
                  placeholder="Search workload or image..."
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                  className="pl-9 bg-zinc-950/80 text-xs"
                />
              </div>

              {/* Cluster Filter */}
              <div className="w-40">
                <Select
                  value={selectedCluster}
                  onChange={(e) => setSelectedCluster(e.target.value)}
                  className="bg-zinc-950/80 text-xs"
                >
                  <option value="">All Clusters</option>
                  {clusters.map((c) => (
                    <option key={c} value={c}>
                      {c}
                    </option>
                  ))}
                </Select>
              </div>

              {/* Namespace Filter & Action */}
              <div className="flex items-center gap-1.5">
                <div className="w-40">
                  <Select
                    value={selectedNamespace}
                    onChange={(e) => setSelectedNamespace(e.target.value)}
                    className="bg-zinc-950/80 text-xs"
                  >
                    <option value="">All Namespaces</option>
                    {namespaces.map((ns) => (
                      <option key={ns} value={ns}>
                        {ns}
                      </option>
                    ))}
                  </Select>
                </div>
                {selectedNamespace && isOperator && (
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => setIsDeleteNamespaceOpen(true)}
                    className={`h-8 px-2 text-xs border ${
                      isSystemCriticalNamespace(selectedNamespace)
                        ? 'text-amber-400/80 border-amber-900/40 hover:bg-amber-950/30'
                        : 'text-rose-400 border-rose-900/50 hover:bg-rose-950/40 hover:text-rose-300'
                    }`}
                    title={
                      isSystemCriticalNamespace(selectedNamespace)
                        ? `Namespace '${selectedNamespace}' is system-critical`
                        : `Delete namespace '${selectedNamespace}'`
                    }
                  >
                    <Trash2 className="h-3.5 w-3.5 mr-1" />
                    Delete
                  </Button>
                )}
              </div>

              {/* Kind Filter */}
              <div className="w-36">
                <Select
                  value={kindFilter}
                  onChange={(e) => setKindFilter(e.target.value as typeof kindFilter)}
                  className="bg-zinc-950/80 text-xs font-mono"
                >
                  <option value="all">All Kinds</option>
                  <option value="Deployment">Deployments</option>
                  <option value="StatefulSet">StatefulSets</option>
                  <option value="DaemonSet">DaemonSets</option>
                  <option value="CronJob">CronJobs</option>
                </Select>
              </div>

              {/* Status Filter Chips */}
              <div className="flex items-center gap-1.5 p-1 bg-zinc-950/60 border border-zinc-800/80 rounded-lg">
                {statusChips.map((chip) => {
                  const isSelected = statusFilter === chip.id
                  return (
                    <button
                      key={chip.id}
                      type="button"
                      onClick={() => setStatusFilter(chip.id)}
                      className={`flex items-center gap-1 px-2.5 py-1 rounded-md text-xs font-medium border transition-all cursor-pointer select-none ${
                        isSelected
                          ? chip.color
                          : 'border-transparent text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900/60'
                      }`}
                    >
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

            {/* Refresh Button */}
            <div className="flex items-center gap-2 shrink-0">
              <Button
                variant="outline"
                size="sm"
                onClick={() => refetch()}
                disabled={isFetching}
                className="gap-1.5 text-xs"
              >
                <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} />
                Refresh
              </Button>
            </div>
          </div>

          {/* Main Workload Grid */}
          {isLoading ? (
            <div className="p-16 text-center border border-zinc-800 rounded-2xl bg-zinc-900/30 space-y-3">
              <RefreshCw className="h-8 w-8 animate-spin mx-auto text-sky-400" />
              <h4 className="text-sm font-semibold text-zinc-200">
                Querying Kubernetes Clusters...
              </h4>
              <p className="text-xs text-zinc-400 max-w-sm mx-auto">
                Aggregating Deployments, StatefulSets, DaemonSets, and CronJobs.
              </p>
            </div>
          ) : isError ? (
            <div className="p-8 text-center border border-rose-800/60 rounded-xl bg-rose-950/20 text-rose-300 space-y-3">
              <AlertTriangle className="h-8 w-8 mx-auto text-rose-400" />
              <h4 className="text-sm font-bold">Failed to load workloads</h4>
              <p className="text-xs text-rose-400 max-w-md mx-auto">
                {error instanceof Error ? error.message : 'Error communicating with Kubernetes API'}
              </p>
              <Button variant="outline" size="sm" onClick={() => refetch()} className="mt-2">
                Try Again
              </Button>
            </div>
          ) : filteredItems.length === 0 ? (
            <div className="p-16 text-center border border-zinc-800 rounded-2xl bg-zinc-900/40 space-y-3">
              <Boxes className="h-12 w-12 mx-auto text-zinc-600" />
              <h3 className="text-base font-bold text-zinc-200">No workloads found</h3>
              <p className="text-xs text-zinc-400 max-w-md mx-auto">
                {searchTerm || selectedCluster || selectedNamespace || statusFilter !== 'all' || kindFilter !== 'all'
                  ? 'No workloads match your active filters. Try adjusting your query or kind filter.'
                  : 'Connect your first Kubernetes cluster in Infrastructure Adapters or click "Create Application" to deploy your first container.'}
              </p>
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
              {filteredItems.map((workload) => (
                <WorkloadCard
                  key={`${workload.clusterId}-${workload.namespace}-${workload.name}`}
                  workload={workload}
                  onScale={(w) => setScalingWorkload(w)}
                  onOpenPods={(w) => setInspectPodsWorkload(w)}
                  onEdit={(w) => {
                    setEditingWorkload(w)
                    setEditorDrawerOpen(true)
                  }}
                  onDelete={(w) => setDeletingWorkload(w)}
                  isOperator={isOperator}
                />
              ))}
            </div>
          )}
        </>
      )}

      {/* Scale Workload Modal */}
      <ScaleWorkloadModal
        workload={scalingWorkload}
        open={Boolean(scalingWorkload)}
        onClose={() => setScalingWorkload(null)}
      />

      {/* Pod Detail Drawer */}
      <PodDetailDrawer
        workload={inspectPodsWorkload}
        isOpen={Boolean(inspectPodsWorkload)}
        onClose={() => setInspectPodsWorkload(null)}
      />

      {/* Unified App Editor Drawer (Visual Form + Drop-Down YAML) */}
      <AppEditorDrawer
        open={editorDrawerOpen}
        onClose={() => {
          setEditorDrawerOpen(false)
          setEditingWorkload(null)
        }}
        initialWorkload={editingWorkload}
        availableClusters={clusters}
        availableNamespaces={namespaces}
      />

      {/* Cascading Deletion Modal with PVC Safeguard & Protection Confirmation */}
      <DeleteAppModal
        workload={deletingWorkload}
        open={Boolean(deletingWorkload)}
        onClose={() => setDeletingWorkload(null)}
      />

      {/* Delete Namespace Modal */}
      {isDeleteNamespaceOpen && selectedNamespace && (
        <DeleteNamespaceModal
          open={isDeleteNamespaceOpen}
          onClose={() => setIsDeleteNamespaceOpen(false)}
          clusterId={activeClusterId}
          namespaceName={selectedNamespace}
          workloadCount={items.filter((w) => w.namespace.toLowerCase() === selectedNamespace.toLowerCase()).length}
          onSuccess={() => {
            setSelectedNamespace('')
            setIsDeleteNamespaceOpen(false)
          }}
        />
      )}

      {/* Manage Namespaces Modal */}
      {isManageNamespacesOpen && (
        <ManageNamespacesModal
          open={isManageNamespacesOpen}
          onClose={() => setIsManageNamespacesOpen(false)}
          clusterId={activeClusterId}
          namespaces={namespaces}
          workloads={items}
          onSelectNamespace={(ns) => {
            setSelectedNamespace(ns)
            setIsManageNamespacesOpen(false)
          }}
        />
      )}

      {/* Create Namespace Modal */}
      {isCreateNamespaceOpen && (
        <CreateNamespaceModal
          open={isCreateNamespaceOpen}
          onClose={() => setIsCreateNamespaceOpen(false)}
          clusterId={activeClusterId}
          availableClusters={clusters}
          onSuccess={(created) => {
            setSelectedNamespace(created)
            setIsCreateNamespaceOpen(false)
          }}
        />
      )}

      {/* Create Resource (Secret, ConfigMap, Raw YAML) Modal */}
      {isCreateResourceOpen && (
        <CreateResourceModal
          open={isCreateResourceOpen}
          onClose={() => setIsCreateResourceOpen(false)}
          initialClusterId={activeClusterId}
          initialNamespace={selectedNamespace || 'default'}
          initialTab={createResourceInitialTab}
          availableClusters={clusters}
          availableNamespaces={namespaces}
          onSuccess={(_name, kind) => {
            if (kind === 'Secret' || kind === 'ConfigMap') {
              setCurrentSubTab('config')
            }
          }}
        />
      )}
    </div>
  )
}
