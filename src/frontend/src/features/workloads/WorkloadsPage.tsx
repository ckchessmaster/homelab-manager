import { useState, useMemo } from 'react'
import {
  Layers,
  Cpu,
  Search,
  RefreshCw,
  AlertTriangle,
  CheckCircle2,
  Boxes,
} from 'lucide-react'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { Button } from '../../components/ui/button'
import { useWorkloads } from './useWorkloads'
import { WorkloadCard } from './WorkloadCard'
import { ScaleWorkloadModal } from './ScaleWorkloadModal'
import { PodDetailDrawer } from './PodDetailDrawer'
import { useAuthUser } from '../auth/useAuthUser'
import type { WorkloadSummary } from '../../api/workloads'

export function WorkloadsPage() {
  const [searchTerm, setSearchTerm] = useState('')
  const [selectedCluster, setSelectedCluster] = useState('')
  const [selectedNamespace, setSelectedNamespace] = useState('')
  const [statusFilter, setStatusFilter] = useState<'all' | 'ready' | 'degraded' | 'progressing' | 'scaleddown'>('all')

  const [scalingWorkload, setScalingWorkload] = useState<WorkloadSummary | null>(null)
  const [inspectPodsWorkload, setInspectPodsWorkload] = useState<WorkloadSummary | null>(null)

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

  // Filter client-side by search query and status chip
  const filteredItems = useMemo(() => {
    return items.filter((w) => {
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
  }, [items, statusFilter, searchTerm])

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
      {/* Metric Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Total Deployments</span>
            <Boxes className="h-4 w-4 text-sky-400" />
          </div>
          <div className="mt-2 text-2xl font-bold text-zinc-100">
            {result?.totalDeployments ?? 0}
          </div>
          <p className="text-[11px] text-zinc-500 mt-0.5">Across all connected clusters</p>
        </div>

        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Healthy Deployments</span>
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
          <p className="text-[11px] text-zinc-500 mt-0.5">Kubernetes control planes</p>
        </div>

        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Total Namespaces</span>
            <Layers className="h-4 w-4 text-amber-400" />
          </div>
          <div className="mt-2 text-2xl font-bold text-zinc-100">
            {namespaces.length}
          </div>
          <p className="text-[11px] text-zinc-500 mt-0.5">Workload partitions</p>
        </div>
      </div>

      {/* Toolbar & Filters */}
      <div className="flex flex-col md:flex-row gap-3 items-stretch md:items-center justify-between p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-md">
        <div className="flex flex-1 flex-wrap items-center gap-3">
          {/* Search Box */}
          <div className="relative min-w-[220px] flex-1 max-w-sm">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-zinc-500" />
            <Input
              placeholder="Search deployment or image..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="pl-9 bg-zinc-950/80"
            />
          </div>

          {/* Cluster Filter */}
          <div className="w-44">
            <Select
              value={selectedCluster}
              onChange={(e) => setSelectedCluster(e.target.value)}
              className="bg-zinc-950/80"
            >
              <option value="">All Clusters</option>
              {clusters.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </Select>
          </div>

          {/* Namespace Filter */}
          <div className="w-44">
            <Select
              value={selectedNamespace}
              onChange={(e) => setSelectedNamespace(e.target.value)}
              className="bg-zinc-950/80"
            >
              <option value="">All Namespaces</option>
              {namespaces.map((ns) => (
                <option key={ns} value={ns}>
                  {ns}
                </option>
              ))}
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
            className="gap-1.5"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} />
            Refresh
          </Button>
        </div>
      </div>

      {/* Main Grid */}
      {isLoading ? (
        <div className="p-16 text-center border border-zinc-800 rounded-2xl bg-zinc-900/30 space-y-3">
          <RefreshCw className="h-8 w-8 animate-spin mx-auto text-sky-400" />
          <h4 className="text-sm font-semibold text-zinc-200">
            Querying Kubernetes Clusters...
          </h4>
          <p className="text-xs text-zinc-400 max-w-sm mx-auto">
            Aggregating deployment states and running pods across your infrastructure.
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
            {searchTerm || selectedCluster || selectedNamespace || statusFilter !== 'all'
              ? 'No deployments match your active filters. Try adjusting your query or selecting a different namespace.'
              : 'Connect your first Kubernetes cluster in the Infrastructure Adapters hub to discover and manage container workloads.'}
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
              isOperator={isOperator}
            />
          ))}
        </div>
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
    </div>
  )
}
