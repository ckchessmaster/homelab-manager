import { useState, useMemo, Fragment } from 'react'
import {
  Layers,
  Cpu,
  RefreshCw,
  AlertTriangle,
  CheckCircle2,
  Boxes,
  PlusCircle,
  Globe,
  HardDrive,
  Activity,
  Trash2,
  Package,
  KeyRound,
  LayoutList,
  LayoutGrid,
  Sliders,
  RotateCcw,
  Copy,
  Pencil,
  Terminal,
  MoreHorizontal,
  ChevronsUpDown,
  ArrowUpCircle,
  Sparkles,
} from 'lucide-react'
import { Select } from '../../components/ui/select'
import { Button } from '../../components/ui/button'
import { Badge } from '../../components/ui/badge'
import { MetricStrip } from '../../components/ui/metric-strip'
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
} from '../../components/ui/dropdown-menu'
import {
  TableToolbar,
  TableToolbarSearch,
  TableToolbarGroup,
  TableToolbarActions,
  TableToolbarSegment,
} from '../../components/ui/table-toolbar'
import { useWorkloads, useRestartWorkload, useCheckImageUpdates } from './useWorkloads'
import { WorkloadCard } from './WorkloadCard'
import { ScaleWorkloadModal } from './ScaleWorkloadModal'
import { PodDetailDrawer } from './PodDetailDrawer'
import { AppEditorDrawer } from './AppEditorDrawer'
import { DeleteAppModal } from './DeleteAppModal'
import { DeleteNamespaceModal } from './DeleteNamespaceModal'
import { CreateNamespaceModal } from './CreateNamespaceModal'
import { ManageNamespacesModal } from './ManageNamespacesModal'
import { NamespaceGroupHeader } from './NamespaceGroupHeader'
import { ClusterEventsDock } from './ClusterEventsDock'
import { NetworkIngressView } from './NetworkIngressView'
import { StorageView } from './StorageView'
import { ClusterVitalsView } from './ClusterVitalsView'
import { HelmReleasesView } from './helm/HelmReleasesView'
import { ConfigSecretsView } from './ConfigSecretsView'
import { CreateResourceModal } from './CreateResourceModal'
import { useAuthUser } from '../auth/useAuthUser'
import { isSystemCriticalNamespace, type WorkloadSummary } from '../../api/workloads'

const MAIN_KINDS = ['Deployment', 'StatefulSet', 'DaemonSet', 'CronJob', 'Job']
const STANDARD_KINDS = [
  'Deployment',
  'StatefulSet',
  'DaemonSet',
  'CronJob',
  'Job',
  'Pod',
  'Service',
  'Ingress',
  'ConfigMap',
  'Secret',
  'PersistentVolumeClaim',
]

function getResourceIcon(kind?: string) {
  switch (kind) {
    case 'Service':
      return <Globe className="h-4 w-4 text-emerald-400 shrink-0" />
    case 'Ingress':
      return <Globe className="h-4 w-4 text-sky-400 shrink-0" />
    case 'ConfigMap':
      return <Sliders className="h-4 w-4 text-amber-400 shrink-0" />
    case 'Secret':
      return <KeyRound className="h-4 w-4 text-purple-400 shrink-0" />
    case 'PersistentVolumeClaim':
      return <HardDrive className="h-4 w-4 text-indigo-400 shrink-0" />
    case 'Job':
      return <Activity className="h-4 w-4 text-blue-400 shrink-0" />
    case 'Pod':
      return <Boxes className="h-4 w-4 text-teal-400 shrink-0" />
    case 'DaemonSet':
      return <Layers className="h-4 w-4 text-indigo-400 shrink-0" />
    case 'StatefulSet':
      return <HardDrive className="h-4 w-4 text-purple-400 shrink-0" />
    case 'CronJob':
      return <Sparkles className="h-4 w-4 text-amber-400 shrink-0" />
    case 'Deployment':
      return <Boxes className="h-4 w-4 text-sky-400 shrink-0" />
    default:
      return <Sparkles className="h-4 w-4 text-fuchsia-400 shrink-0" />
  }
}

type SubTab = 'applications' | 'helm' | 'config' | 'network' | 'storage' | 'vitals'

export function WorkloadsPage() {
  const [currentSubTab, setCurrentSubTab] = useState<SubTab>('applications')
  const [searchTerm, setSearchTerm] = useState('')
  const [selectedCluster, setSelectedCluster] = useState('')
  const [workloadNamespace, setWorkloadNamespace] = useState('')
  const [kindFilter, setKindFilter] = useState<string>('main')
  const [statusFilter, setStatusFilter] = useState<'all' | 'ready' | 'degraded' | 'progressing' | 'scaleddown' | 'outdated'>('all')

  // Modals & Drawers
  const [scalingWorkload, setScalingWorkload] = useState<WorkloadSummary | null>(null)
  const [inspectPodsWorkload, setInspectPodsWorkload] = useState<WorkloadSummary | null>(null)
  const [drawerInitialTab, setDrawerInitialTab] = useState<'pods' | 'logs' | 'revisions' | 'env'>('pods')
  const [editorDrawerOpen, setEditorDrawerOpen] = useState(false)
  const [editingWorkload, setEditingWorkload] = useState<WorkloadSummary | null>(null)
  const [deletingWorkload, setDeletingWorkload] = useState<WorkloadSummary | null>(null)
  const [isDeleteNamespaceOpen, setIsDeleteNamespaceOpen] = useState(false)
  const [isManageNamespacesOpen, setIsManageNamespacesOpen] = useState(false)
  const [isCreateNamespaceOpen, setIsCreateNamespaceOpen] = useState(false)
  const [isCreateResourceOpen, setIsCreateResourceOpen] = useState(false)
  const [createResourceInitialTab, setCreateResourceInitialTab] = useState<'secret' | 'configmap' | 'yaml'>('secret')
  const [viewMode, setViewMode] = useState<'table' | 'cards'>('table')
  const [restartConfirmTarget, setRestartConfirmTarget] = useState<WorkloadSummary | null>(null)
  const [actionToast, setActionToast] = useState<string | null>(null)
  const [collapsedNamespaces, setCollapsedNamespaces] = useState<Set<string>>(new Set())
  const [eventWorkloadFilter, setEventWorkloadFilter] = useState<string | null>(null)

  const { isOperator } = useAuthUser()
  const restartMutation = useRestartWorkload()
  const checkImageUpdatesMutation = useCheckImageUpdates()

  const isIncludeAll = kindFilter !== 'main'
  const {
    data: result,
    isLoading,
    isError,
    error,
    refetch,
    isFetching,
  } = useWorkloads(selectedCluster || undefined, workloadNamespace || undefined, isIncludeAll)

  const items = useMemo(() => result?.items ?? [], [result?.items])
  const clusters = useMemo(() => result?.clusters ?? [], [result?.clusters])
  const namespaces = useMemo(() => result?.namespaces ?? [], [result?.namespaces])

  const activeClusterId = selectedCluster || clusters[0] || ''

  // Discover dynamic custom resource kinds returned by the cluster
  const customKinds = useMemo(() => {
    const set = new Set<string>()
    for (const item of items) {
      if (item.kind && !STANDARD_KINDS.includes(item.kind)) {
        set.add(item.kind)
      }
    }
    return Array.from(set).sort()
  }, [items])

  // Filter client-side by kind, search query, and status chip
  const filteredItems = useMemo(() => {
    return items.filter((w) => {
      const k = w.kind || 'Deployment'
      // Kind filter
      if (kindFilter === 'main') {
        if (!MAIN_KINDS.includes(k)) return false
      } else if (kindFilter === 'all') {
        // show all kinds
      } else if (kindFilter === 'crds') {
        if (STANDARD_KINDS.includes(k)) return false
      } else {
        if (k !== kindFilter) return false
      }

      // Status filter
      if (statusFilter === 'ready' && w.status !== 'Ready') return false
      if (statusFilter === 'degraded' && w.status !== 'Degraded') return false
      if (statusFilter === 'progressing' && w.status !== 'Progressing') return false
      if (statusFilter === 'scaleddown' && w.status !== 'ScaledDown') return false
      if (statusFilter === 'outdated' && !w.imageUpdate?.isOutdated) return false

      // Search query
      if (searchTerm.trim()) {
        const q = searchTerm.toLowerCase()
        const matchesName = w.name.toLowerCase().includes(q)
        const matchesNs = w.namespace.toLowerCase().includes(q)
        const matchesImg = (w.images || []).some((img) => img.toLowerCase().includes(q))
        const matchesKind = (w.kind || '').toLowerCase().includes(q)
        if (!matchesName && !matchesNs && !matchesImg && !matchesKind) return false
      }

      return true
    })
  }, [items, kindFilter, statusFilter, searchTerm])

  const outdatedCount = useMemo(() => items.filter((w) => w.imageUpdate?.isOutdated).length, [items])

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
    {
      id: 'outdated',
      label: 'Updates Available',
      color: 'bg-amber-950/80 text-amber-300 border-amber-700',
      count: outdatedCount,
    },
  ]

  const handleCheckUpdates = async () => {
    try {
      const res = await checkImageUpdatesMutation.mutateAsync({ force: true })
      const count = Object.values(res).filter((r) => r.isOutdated).length
      if (count > 0) {
        setActionToast(`Found ${count} container image update${count > 1 ? 's' : ''}!`)
      } else {
        setActionToast('All container images are up to date.')
      }
      setTimeout(() => setActionToast(null), 3500)
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to check container image updates')
    }
  }

  const groupedByNamespace = useMemo(() => {
    const map = new Map<string, WorkloadSummary[]>()
    for (const item of filteredItems) {
      const list = map.get(item.namespace) || []
      list.push(item)
      map.set(item.namespace, list)
    }
    return Array.from(map.entries())
  }, [filteredItems])

  const handleQuickRestart = async (workload: WorkloadSummary) => {
    try {
      await restartMutation.mutateAsync({
        clusterId: workload.clusterId,
        namespaceName: workload.namespace,
        name: workload.name,
        kind: workload.kind,
      })
      setActionToast(`Triggered rolling restart for ${workload.name}`)
      setTimeout(() => setActionToast(null), 3000)
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to trigger rollout restart')
    }
  }

  const toggleNamespace = (ns: string) => {
    setCollapsedNamespaces((prev) => {
      const next = new Set(prev)
      if (next.has(ns)) next.delete(ns)
      else next.add(ns)
      return next
    })
  }

  const toggleAllNamespaces = () => {
    if (collapsedNamespaces.size === groupedByNamespace.length) {
      setCollapsedNamespaces(new Set())
    } else {
      setCollapsedNamespaces(new Set(groupedByNamespace.map(([ns]) => ns)))
    }
  }

  return (
    <div className="space-y-6 w-full max-w-[1700px] mx-auto">
      {/* Top Header with Primary Sub-Navigation & "+ Create App" */}
      <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-4 p-4 rounded-2xl bg-zinc-900/60 border border-zinc-800/90 backdrop-blur-md">
        <div className="flex items-center gap-2 overflow-x-auto">
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
        </div>

        {isOperator && (
          <div className="flex flex-wrap items-center gap-2">
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
              <PlusCircle className="h-4 w-4" />
              Create Application
            </Button>
          </div>
        )}
      </div>

      {/* View Content based on active SubTab - persistent mounting keeps filters and state across tabs */}
      <div className={currentSubTab === 'helm' ? 'block' : 'hidden'}>
        <HelmReleasesView
          activeClusterId={activeClusterId}
          selectedNamespace={workloadNamespace}
          onNamespaceChange={setWorkloadNamespace}
          availableNamespaces={namespaces}
        />
      </div>

      <div className={currentSubTab === 'config' ? 'block' : 'hidden'}>
        <ConfigSecretsView
          clusterId={activeClusterId}
          selectedNamespace={workloadNamespace}
          onNamespaceChange={setWorkloadNamespace}
          availableNamespaces={namespaces}
          onOpenCreateResource={(tab) => {
            setCreateResourceInitialTab(tab || 'secret')
            setIsCreateResourceOpen(true)
          }}
        />
      </div>

      <div className={currentSubTab === 'network' ? 'block' : 'hidden'}>
        <NetworkIngressView
          clusterId={activeClusterId}
          selectedNamespace={workloadNamespace}
          onNamespaceChange={setWorkloadNamespace}
          availableNamespaces={namespaces}
        />
      </div>

      <div className={currentSubTab === 'storage' ? 'block' : 'hidden'}>
        <StorageView
          clusterId={activeClusterId}
          selectedNamespace={workloadNamespace}
          onNamespaceChange={setWorkloadNamespace}
          availableNamespaces={namespaces}
        />
      </div>

      <div className={currentSubTab === 'vitals' ? 'block' : 'hidden'}>
        <ClusterVitalsView clusterId={activeClusterId} />
      </div>

      <div className={currentSubTab === 'applications' ? 'block space-y-6' : 'hidden'}>
          {/* Sleek Top Metric Strip */}
          <MetricStrip
            items={[
              {
                id: 'total-workloads',
                label: kindFilter === 'main' ? 'Total Workloads' : 'Total Resources',
                value: kindFilter === 'main' ? (result?.totalDeployments ?? 0) : filteredItems.length,
                icon: Boxes,
                iconColor: 'text-sky-400',
                subtext: kindFilter === 'main' ? 'Deployments, StatefulSets, DaemonSets & CronJobs' : `Showing ${filteredItems.length} filtered resources`,
              },
              {
                id: 'healthy-workloads',
                label: 'Healthy Workloads',
                value: result?.healthyDeployments ?? 0,
                icon: CheckCircle2,
                iconColor: 'text-emerald-400',
                badge:
                  result?.totalDeployments && result.totalDeployments > 0
                    ? `${Math.round(((result.healthyDeployments || 0) / result.totalDeployments) * 100)}% Ready`
                    : undefined,
                badgeVariant: 'success',
                subtext: '100% ready replicas',
              },
              {
                id: 'connected-clusters',
                label: 'Connected Clusters',
                value: clusters.length,
                icon: Cpu,
                iconColor: 'text-purple-400',
                subtext: 'Active control planes',
              },
              {
                id: 'total-namespaces',
                label: 'Namespaces',
                value: namespaces.length,
                icon: Layers,
                iconColor: 'text-amber-400',
                badge: 'Manage',
                badgeVariant: 'warning',
                onClick: () => setIsManageNamespacesOpen(true),
                subtext: 'Click to inspect & manage namespaces',
              },
              {
                id: 'outdated-images',
                label: 'Image Updates',
                value: outdatedCount,
                icon: ArrowUpCircle,
                iconColor: outdatedCount > 0 ? 'text-amber-400' : 'text-emerald-400',
                badge: outdatedCount > 0 ? `${outdatedCount} Available` : 'Up to Date',
                badgeVariant: outdatedCount > 0 ? 'warning' : 'success',
                onClick: () => setStatusFilter(statusFilter === 'outdated' ? 'all' : 'outdated'),
                subtext: outdatedCount > 0 ? 'Click to filter workloads with updates' : 'All container images up to date',
              },
            ]}
          />

          {/* Unified TableToolbar */}
          <TableToolbar>
            <TableToolbarGroup className="flex-1">
              <TableToolbarSearch
                placeholder="Search deployment or image..."
                value={searchTerm}
                onChange={setSearchTerm}
              />

              {/* Cluster Filter */}
              <div className="w-36">
                <Select
                  value={selectedCluster}
                  onChange={(e) => setSelectedCluster(e.target.value)}
                  className="bg-zinc-950/80 text-xs h-9"
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
                <div className="w-36">
                  <Select
                    value={workloadNamespace}
                    onChange={(e) => setWorkloadNamespace(e.target.value)}
                    className="bg-zinc-950/80 text-xs h-9"
                  >
                    <option value="">All Namespaces</option>
                    {namespaces.map((ns) => (
                      <option key={ns} value={ns}>
                        {ns}
                      </option>
                    ))}
                  </Select>
                </div>
                {workloadNamespace && isOperator && (
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => setIsDeleteNamespaceOpen(true)}
                    className={`h-9 px-2 text-xs border ${
                      isSystemCriticalNamespace(workloadNamespace)
                        ? 'text-amber-400/80 border-amber-900/40 hover:bg-amber-950/30'
                        : 'text-rose-400 border-rose-900/50 hover:bg-rose-950/40 hover:text-rose-300'
                    }`}
                    title={
                      isSystemCriticalNamespace(workloadNamespace)
                        ? `Namespace '${workloadNamespace}' is system-critical`
                        : `Delete namespace '${workloadNamespace}'`
                    }
                  >
                    <Trash2 className="h-3.5 w-3.5 mr-1" />
                    Delete
                  </Button>
                )}
              </div>

              {/* Kind Filter */}
              <div className="w-44 sm:w-48">
                <Select
                  value={kindFilter}
                  onChange={(e) => setKindFilter(e.target.value)}
                  className="bg-zinc-950/80 text-xs font-mono h-9"
                >
                  <option value="main">Main Workloads</option>
                  <option value="all">All Resources (incl. CRDs)</option>
                  <optgroup label="Workloads">
                    <option value="Deployment">Deployments</option>
                    <option value="StatefulSet">StatefulSets</option>
                    <option value="DaemonSet">DaemonSets</option>
                    <option value="CronJob">CronJobs</option>
                    <option value="Job">Jobs</option>
                    <option value="Pod">Pods</option>
                  </optgroup>
                  <optgroup label="Networking">
                    <option value="Service">Services</option>
                    <option value="Ingress">Ingresses</option>
                  </optgroup>
                  <optgroup label="Config & Storage">
                    <option value="ConfigMap">ConfigMaps</option>
                    <option value="Secret">Secrets</option>
                    <option value="PersistentVolumeClaim">PVCs</option>
                  </optgroup>
                  <optgroup label="Custom Resources">
                    <option value="crds">All CRDs</option>
                    {customKinds.map((k) => (
                      <option key={k} value={k}>
                        {k}
                      </option>
                    ))}
                  </optgroup>
                </Select>
              </div>

              {/* Status Filter Chips */}
              <div className="flex items-center gap-1 p-0.5 bg-zinc-950/60 border border-zinc-800 rounded-lg">
                {statusChips.map((chip) => {
                  const isSelected = statusFilter === chip.id
                  return (
                    <button
                      key={chip.id}
                      type="button"
                      onClick={() => setStatusFilter(chip.id)}
                      className={`flex items-center gap-1 px-2 py-1 rounded-md text-xs font-medium border transition-all cursor-pointer select-none ${
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
            </TableToolbarGroup>

            <TableToolbarActions>
              {viewMode === 'table' && groupedByNamespace.length > 0 && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={toggleAllNamespaces}
                  className="gap-1.5 text-xs h-9 border-zinc-800 bg-zinc-950/80 hover:bg-zinc-800 text-zinc-300"
                  title={
                    collapsedNamespaces.size === groupedByNamespace.length
                      ? 'Expand all namespaces'
                      : 'Collapse all namespaces'
                  }
                >
                  <ChevronsUpDown className="h-3.5 w-3.5 text-zinc-400" />
                  <span>
                    {collapsedNamespaces.size === groupedByNamespace.length
                      ? 'Expand All'
                      : 'Collapse All'}
                  </span>
                </Button>
              )}

              {/* Segmented View Toggle: Dense Table vs Cards */}
              <TableToolbarSegment
                options={[
                  { id: 'table', label: 'Table', icon: LayoutList },
                  { id: 'cards', label: 'Cards', icon: LayoutGrid },
                ]}
                value={viewMode}
                onChange={setViewMode}
              />

              <Button
                variant="outline"
                size="sm"
                onClick={handleCheckUpdates}
                disabled={checkImageUpdatesMutation.isPending}
                className="gap-1.5 text-xs h-9 border-zinc-800 bg-zinc-950/80 hover:bg-zinc-800 text-zinc-300 hover:text-amber-300"
                title="Query registries for newer container image versions"
              >
                <Sparkles className={`h-3.5 w-3.5 text-amber-400 ${checkImageUpdatesMutation.isPending ? 'animate-spin' : ''}`} />
                {checkImageUpdatesMutation.isPending ? 'Checking...' : 'Check Updates'}
              </Button>

              <Button
                variant="outline"
                size="sm"
                onClick={() => refetch()}
                disabled={isFetching}
                className="gap-1.5 text-xs h-9"
                title="Refresh workloads from Kubernetes"
              >
                <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} />
                Refresh
              </Button>
            </TableToolbarActions>
          </TableToolbar>

          {/* Main Workload View: Single Root Dense Table (grouped by namespace) vs Cards */}
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
                {searchTerm || selectedCluster || workloadNamespace || statusFilter !== 'all' || kindFilter !== 'main'
                  ? 'No workloads match your active filters. Try adjusting your query or kind filter.'
                  : 'Connect your first Kubernetes cluster in Infrastructure Adapters or click "Create Application" to deploy your first container.'}
              </p>
            </div>
          ) : viewMode === 'table' ? (
            <div className="rounded-xl border border-zinc-800/80 bg-zinc-900/60 overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs text-zinc-300 border-collapse">
                  <thead className="bg-zinc-950/95 sticky top-0 z-10 backdrop-blur-md text-[11px] uppercase tracking-wider text-zinc-400 border-b border-zinc-800 shadow-xs">
                    <tr>
                      <th className="p-3 font-semibold">Workload / Resource</th>
                      <th className="p-3 font-semibold">Status</th>
                      <th className="p-3 font-semibold">Replicas / Info</th>
                      <th className="p-3 font-semibold">Primary Image</th>
                      <th className="p-3 font-semibold">Age / Created</th>
                      <th className="p-3 font-semibold text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-zinc-800/60 font-mono">
                    {groupedByNamespace.map(([ns, workloads]) => {
                      const isCollapsed = collapsedNamespaces.has(ns)
                      const statusSummary = {
                        ready: workloads.filter((w) => w.status === 'Ready').length,
                        degraded: workloads.filter((w) => w.status === 'Degraded').length,
                        progressing: workloads.filter((w) => w.status === 'Progressing').length,
                        scaledDown: workloads.filter((w) => w.status === 'ScaledDown').length,
                      }

                      return (
                        <Fragment key={ns}>
                          <NamespaceGroupHeader
                            name={ns}
                            clusterName={workloads[0]?.clusterName || workloads[0]?.clusterId}
                            count={workloads.length}
                            statusSummary={statusSummary}
                            isCollapsed={isCollapsed}
                            onToggle={() => toggleNamespace(ns)}
                            colSpan={6}
                          />

                          {!isCollapsed &&
                            workloads.map((w) => {
                              const isHealthy = w.status === 'Ready'
                              const isScaledDown = w.status === 'ScaledDown'
                              const isDegraded = w.status === 'Degraded'
                              const k = w.kind || ''
                              const isCronJob = k === 'CronJob'
                              const isPodWorkload = ['Deployment', 'StatefulSet', 'DaemonSet', 'CronJob', 'Job', 'Pod'].includes(k)
                              const primaryImage = w.images?.[0]

                              return (
                                <tr
                                  key={`${w.clusterId}-${w.namespace}-${w.name}`}
                                  onClick={() => {
                                    if (isPodWorkload) {
                                      setInspectPodsWorkload(w)
                                      setDrawerInitialTab('pods')
                                    } else if (isOperator) {
                                      setEditingWorkload(w)
                                      setEditorDrawerOpen(true)
                                    }
                                  }}
                                  className="hover:bg-zinc-800/40 cursor-pointer transition-colors group"
                                >
                                  <td className="p-3 font-sans">
                                    <div className="flex items-center gap-2">
                                      {getResourceIcon(w.kind)}
                                      <span className="font-semibold text-zinc-100 font-mono group-hover:text-sky-300 transition-colors">
                                        {w.name}
                                      </span>
                                      {w.kind && (
                                        <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-zinc-800 text-zinc-300 border border-zinc-700/50">
                                          {w.kind}
                                        </span>
                                      )}
                                      {w.isProtected && (
                                        <span className="text-[10px] font-sans px-1.5 py-0.2 rounded bg-purple-950/80 text-purple-300 border border-purple-800/60">
                                          Protected
                                        </span>
                                      )}
                                    </div>
                                  </td>

                                  <td className="p-3 font-sans">
                                    <Badge
                                      variant={
                                        isHealthy
                                          ? 'success'
                                          : isScaledDown
                                          ? 'default'
                                          : isDegraded
                                          ? 'destructive'
                                          : 'warning'
                                      }
                                      dot
                                      className="text-[10px]"
                                    >
                                      {w.status}
                                    </Badge>
                                  </td>

                                  <td className="p-3">
                                    {isCronJob ? (
                                      <span className="text-zinc-400 text-xs font-mono">
                                        {w.schedule || 'CronJob'}
                                      </span>
                                    ) : !isPodWorkload ? (
                                      <span className="text-zinc-500 text-xs font-mono">-</span>
                                    ) : (
                                      <span
                                        className={`font-semibold ${
                                          w.readyReplicas === w.desiredReplicas && (w.desiredReplicas ?? 0) > 0
                                            ? 'text-emerald-400'
                                            : (w.readyReplicas ?? 0) === 0 && (w.desiredReplicas ?? 0) > 0
                                            ? 'text-rose-400'
                                            : 'text-zinc-300'
                                        }`}
                                      >
                                        {w.readyReplicas ?? 0}/{w.desiredReplicas ?? 0} Ready
                                      </span>
                                    )}
                                  </td>

                                  <td className="p-3">
                                    {primaryImage ? (
                                      <div className="flex flex-col gap-0.5">
                                        <div className="flex items-center gap-1.5">
                                          <span
                                            className="max-w-[180px] sm:max-w-[220px] truncate px-2 py-0.5 rounded bg-zinc-950/80 border border-zinc-800 text-[11px] text-zinc-300 inline-block font-mono"
                                            title={primaryImage}
                                          >
                                            {primaryImage.split('/').pop() || primaryImage}
                                          </span>
                                          <button
                                            type="button"
                                            onClick={(e) => {
                                              e.stopPropagation()
                                              navigator.clipboard.writeText(primaryImage)
                                              setActionToast(`Copied image: ${primaryImage}`)
                                              setTimeout(() => setActionToast(null), 2000)
                                            }}
                                            className="p-1 text-zinc-500 hover:text-zinc-300 rounded cursor-pointer transition-colors"
                                            title="Copy full image tag"
                                          >
                                            <Copy className="h-3 w-3" />
                                          </button>
                                        </div>

                                        {w.imageUpdate?.isOutdated ? (
                                          <div className="flex items-center gap-1 mt-0.5">
                                            <span
                                              className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[10px] font-semibold bg-amber-950/80 text-amber-300 border border-amber-700/80 shadow-xs"
                                              title={w.imageUpdate.message || `Update available: ${w.imageUpdate.latestTag}`}
                                            >
                                              <ArrowUpCircle className="h-3 w-3 text-amber-400 shrink-0" />
                                              <span>Update: {w.imageUpdate.latestTag}</span>
                                              {w.imageUpdate.updateType && (
                                                <span className="text-[9px] uppercase px-1 py-0 rounded bg-amber-900/60 text-amber-200 font-mono">
                                                  {w.imageUpdate.updateType}
                                                </span>
                                              )}
                                            </span>
                                          </div>
                                        ) : w.imageUpdate && !w.imageUpdate.isOutdated && w.imageUpdate.updateType === 'floating' ? (
                                          <div className="flex items-center gap-1 mt-0.5">
                                            <span className="text-[10px] text-zinc-500 font-mono" title="Mutable floating tag (:latest)">
                                              :latest (floating)
                                            </span>
                                          </div>
                                        ) : w.imageUpdate && !w.imageUpdate.isOutdated && w.imageUpdate.latestTag ? (
                                          <div className="flex items-center gap-1 mt-0.5">
                                            <span className="text-[10px] text-emerald-400/90 font-sans flex items-center gap-1" title="Container image is up to date">
                                              <CheckCircle2 className="h-2.5 w-2.5 text-emerald-400" />
                                              Up to date
                                            </span>
                                          </div>
                                        ) : null}
                                      </div>
                                    ) : (
                                      <span className="text-zinc-600">-</span>
                                    )}
                                  </td>

                                  <td className="p-3 text-zinc-400 text-xs font-sans">
                                    {w.creationTimestamp
                                      ? new Date(w.creationTimestamp).toLocaleDateString()
                                      : 'Active'}
                                  </td>

                                  <td className="p-3 text-right">
                                    <div
                                      className="flex items-center justify-end gap-1 font-sans"
                                      onClick={(e) => e.stopPropagation()}
                                    >
                                      {/* Direct Pods & Scale Action Buttons */}
                                      {isPodWorkload && (
                                        <Button
                                          variant="outline"
                                          size="sm"
                                          onClick={(e) => {
                                            e.stopPropagation()
                                            setInspectPodsWorkload(w)
                                            setDrawerInitialTab('pods')
                                          }}
                                          className="h-7 text-xs px-2 text-zinc-300 hover:text-zinc-100 gap-1 border-zinc-700 hover:bg-zinc-800"
                                          title="Pods"
                                        >
                                          <Boxes className="h-3 w-3" />
                                          Pods
                                        </Button>
                                      )}

                                      {['Deployment', 'StatefulSet'].includes(k) && isOperator && (
                                        <Button
                                          variant="outline"
                                          size="sm"
                                          onClick={(e) => {
                                            e.stopPropagation()
                                            setScalingWorkload(w)
                                          }}
                                          className="h-7 text-xs px-2 text-sky-400 hover:text-sky-300 hover:bg-sky-950/40 border-sky-800/40 gap-1"
                                          title="Scale"
                                        >
                                          <Sliders className="h-3 w-3" />
                                          Scale
                                        </Button>
                                      )}

                                      {/* Quick Logs Primary Action Button */}
                                      {isPodWorkload && (
                                        <Button
                                          variant="ghost"
                                          size="sm"
                                          onClick={(e) => {
                                            e.stopPropagation()
                                            setInspectPodsWorkload(w)
                                            setDrawerInitialTab('logs')
                                          }}
                                          className="h-8 w-8 p-0 text-zinc-400 hover:text-sky-300 hover:bg-sky-950/40 rounded-lg"
                                          title="Quick Logs"
                                        >
                                          <Terminal className="h-4 w-4" />
                                        </Button>
                                      )}

                                      {/* Secondary More Actions Dropdown */}
                                      <DropdownMenu>
                                        <DropdownMenuTrigger asChild>
                                          <Button
                                            variant="ghost"
                                            size="sm"
                                            onClick={(e) => e.stopPropagation()}
                                            className="h-8 w-8 p-0 text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800 rounded-lg"
                                            title="More Actions"
                                          >
                                            <MoreHorizontal className="h-4 w-4" />
                                          </Button>
                                        </DropdownMenuTrigger>
                                        <DropdownMenuContent align="right" className="w-48">
                                          {['Deployment', 'StatefulSet'].includes(k) && isOperator && (
                                            <DropdownMenuItem
                                              onClick={() => setScalingWorkload(w)}
                                            >
                                              <Sliders className="h-3.5 w-3.5 text-sky-400" />
                                              <span>Scale Replicas</span>
                                            </DropdownMenuItem>
                                          )}

                                          {['Deployment', 'StatefulSet', 'DaemonSet'].includes(k) && isOperator && (
                                            <DropdownMenuItem
                                              onClick={() => setRestartConfirmTarget(w)}
                                            >
                                              <RotateCcw className="h-3.5 w-3.5 text-amber-400" />
                                              <span>Restart Rollout</span>
                                            </DropdownMenuItem>
                                          )}

                                          {isPodWorkload && (
                                            <>
                                              <DropdownMenuItem
                                                onClick={() => {
                                                  setInspectPodsWorkload(w)
                                                  setDrawerInitialTab('pods')
                                                }}
                                              >
                                                <Activity className="h-3.5 w-3.5 text-emerald-400" />
                                                <span>View Pods & Revisions</span>
                                              </DropdownMenuItem>
                                              <DropdownMenuItem
                                                onClick={() => {
                                                  setInspectPodsWorkload(w)
                                                  setDrawerInitialTab('env')
                                                }}
                                              >
                                                <KeyRound className="h-3.5 w-3.5 text-amber-400" />
                                                <span>View Environment Vars</span>
                                              </DropdownMenuItem>
                                            </>
                                          )}

                                          <DropdownMenuItem
                                            onClick={() => {
                                              setEventWorkloadFilter(w.name)
                                            }}
                                          >
                                            <Activity className="h-3.5 w-3.5 text-sky-400" />
                                            <span>Filter Events</span>
                                          </DropdownMenuItem>

                                          {isOperator && (
                                            <DropdownMenuItem
                                              onClick={() => {
                                                setEditingWorkload(w)
                                                setEditorDrawerOpen(true)
                                              }}
                                            >
                                              <Pencil className="h-3.5 w-3.5 text-purple-400" />
                                              <span>View YAML / Edit</span>
                                            </DropdownMenuItem>
                                          )}

                                          {isOperator && !w.isProtected && (
                                            <>
                                              <DropdownMenuSeparator />
                                              <DropdownMenuItem
                                                destructive
                                                onClick={() => setDeletingWorkload(w)}
                                              >
                                                <Trash2 className="h-3.5 w-3.5 text-rose-400" />
                                                <span>Delete Resource</span>
                                              </DropdownMenuItem>
                                            </>
                                          )}
                                        </DropdownMenuContent>
                                      </DropdownMenu>
                                    </div>
                                  </td>
                                </tr>
                              )
                            })}
                        </Fragment>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
              {filteredItems.map((workload) => (
                <WorkloadCard
                  key={`${workload.clusterId}-${workload.namespace}-${workload.name}`}
                  workload={workload}
                  onScale={(w) => setScalingWorkload(w)}
                  onOpenPods={(w, tab) => {
                    setInspectPodsWorkload(w)
                    setDrawerInitialTab(tab || 'pods')
                  }}
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

          {/* Cluster Events Stream Dock */}
          <ClusterEventsDock
            clusterId={activeClusterId}
            namespaceName={workloadNamespace || undefined}
            externalWorkloadFilter={eventWorkloadFilter}
            onClearExternalFilter={() => setEventWorkloadFilter(null)}
          />
      </div>

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
        initialTab={drawerInitialTab}
        onEditWorkload={(w) => {
          setEditingWorkload(w)
          setEditorDrawerOpen(true)
        }}
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
      {isDeleteNamespaceOpen && workloadNamespace && (
        <DeleteNamespaceModal
          open={isDeleteNamespaceOpen}
          onClose={() => setIsDeleteNamespaceOpen(false)}
          clusterId={activeClusterId}
          namespaceName={workloadNamespace}
          workloadCount={items.filter((w) => w.namespace.toLowerCase() === workloadNamespace.toLowerCase()).length}
          onSuccess={() => {
            setWorkloadNamespace('')
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
            setWorkloadNamespace(ns)
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
            setWorkloadNamespace(created)
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
          initialNamespace={workloadNamespace || 'default'}
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
      {/* Restart Workload Confirmation Modal Guard */}
      {restartConfirmTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-xs animate-in fade-in">
          <div className="w-full max-w-sm bg-zinc-900 border border-zinc-800 rounded-xl p-5 shadow-2xl space-y-4">
            <div className="flex items-center gap-2.5 text-amber-400 font-semibold text-sm">
              <RotateCcw className="h-4 w-4" />
              <span>Confirm Rolling Restart</span>
            </div>
            <p className="text-xs text-zinc-300 leading-relaxed">
              Are you sure you want to trigger a rolling rollout restart for{' '}
              <span className="font-mono font-bold text-zinc-100">{restartConfirmTarget.name}</span> in namespace{' '}
              <span className="font-mono text-amber-300">{restartConfirmTarget.namespace}</span>?
            </p>
            <div className="flex items-center justify-end gap-2 pt-2 border-t border-zinc-800">
              <Button
                variant="secondary"
                size="sm"
                onClick={() => setRestartConfirmTarget(null)}
                className="text-xs"
              >
                Cancel
              </Button>
              <Button
                variant="primary"
                size="sm"
                onClick={async () => {
                  const target = restartConfirmTarget
                  setRestartConfirmTarget(null)
                  await handleQuickRestart(target)
                }}
                className="text-xs bg-amber-500 hover:bg-amber-400 text-zinc-950 font-bold"
              >
                Restart Workload
              </Button>
            </div>
          </div>
        </div>
      )}

      {/* Action Feedback Toast */}
      {actionToast && (
        <div className="fixed bottom-5 right-5 z-50 px-4 py-2.5 rounded-xl bg-zinc-900 border border-sky-500/50 shadow-2xl text-xs text-sky-200 flex items-center gap-2 animate-in fade-in slide-in-from-bottom-2">
          <CheckCircle2 className="h-4 w-4 text-sky-400 shrink-0" />
          <span>{actionToast}</span>
        </div>
      )}
    </div>
  )
}
