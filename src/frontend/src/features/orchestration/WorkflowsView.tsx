import { useState, useMemo } from 'react'
import {
  GitFork,
  Play,
  Terminal,
  CheckCircle2,
  AlertCircle,
  XCircle,
  Clock,
  Search,
  Filter,
  RefreshCw,
  Activity,
  Layers,
  Trash2,
  Loader2,
  ArrowUpRight,
} from 'lucide-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useJobs } from './useJobs'
import { usePipelines } from './usePipelines'
import { LaunchWorkflowModal } from './LaunchWorkflowModal'
import { useHosts } from '../hosts/useHosts'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Badge } from '../../components/ui/badge'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '../../components/ui/table'
import { HostTerminalDrawer } from '../hosts/HostTerminalDrawer'
import { WorkflowCanvasModal } from './canvas/WorkflowCanvasModal'
import { FleetRollingLauncherModal, FleetRollingDashboardModal } from './fleet'
import { RoleGate } from '../auth/RoleGate'
import { deleteJob, purgeJobs, type JobSummary } from '../../api/jobs'
import { listRollingBatches } from '../../api/temporal'
import type { Host } from '../../api/hosts'

export function WorkflowsView() {
  const { data: jobs, isLoading, isFetching, refetch } = useJobs()
  const { data: hosts } = useHosts()
  const { data: pipelines } = usePipelines()

  const queryClient = useQueryClient()
  const [searchTerm, setSearchTerm] = useState('')
  const [statusFilter, setStatusFilter] = useState<string>('all')
  const [pipelineFilter, setPipelineFilter] = useState<string>('all')
  const [isTriggerModalOpen, setIsTriggerModalOpen] = useState(false)
  const [isPurging, setIsPurging] = useState(false)
  const [deletingJobId, setDeletingJobId] = useState<string | null>(null)
  const [viewMode, setViewMode] = useState<'workflows' | 'fleet'>('workflows')

  // Query persisted & active rolling batches
  const { data: rollingBatches, refetch: refetchRollingBatches } = useQuery({
    queryKey: ['rollingBatches'],
    queryFn: listRollingBatches,
    refetchInterval: 3000,
  })

  // Ensure rollingBatches is always an array
  const batchesList = useMemo(() => {
    return Array.isArray(rollingBatches) ? rollingBatches : []
  }, [rollingBatches])

  // Detect currently active rolling batch
  const activeRollingBatch = useMemo(() => {
    return batchesList.find((b) => b.status === 'Running' || b.status === 'Paused') || null
  }, [batchesList])

  // Fleet rolling modal state
  const [isFleetLauncherOpen, setIsFleetLauncherOpen] = useState(false)
  const [activeFleetBatchId, setActiveFleetBatchId] = useState<string | null>(null)
  const [activeFleetHosts, setActiveFleetHosts] = useState<Host[]>([])
  const [isFleetDashboardOpen, setIsFleetDashboardOpen] = useState(false)

  // Resolve target hosts and open dashboard for a fleet batch
  const handleOpenFleetDashboard = useMemo(() => {
    return (batch: { batchId: string; hostIds?: string[]; hostnames?: string[] }) => {
      setActiveFleetBatchId(batch.batchId)
      const resolvedHosts: Host[] = (batch.hostIds || []).map((id, index) => {
        const found = hosts?.find((h) => h.id === id)
        if (found) return found
        const hostname = batch.hostnames?.[index] || `host-${id.slice(0, 8)}`
        return {
          id,
          hostname,
          friendlyName: hostname,
          ipAddress: '—',
          osFamily: 'linux',
          targetType: 'server',
          proxmox: null,
          kubernetes: null,
          idrac: null,
          networkPort: null,
          agent: {
            installed: true,
            version: '1.0',
            lastSeenAt: null,
            pendingReboot: false,
            upgradablePackagesCount: 0,
            isOnline: true,
          },
          createdAt: '',
          updatedAt: '',
        } as Host
      })
      setActiveFleetHosts(resolvedHosts)
      setIsFleetDashboardOpen(true)
    }
  }, [hosts])

  // Canvas modal state
  const [canvasJob, setCanvasJob] = useState<JobSummary | null>(null)
  const [isCanvasModalOpen, setIsCanvasModalOpen] = useState(false)

  // Terminal drawer state
  const [terminalHost, setTerminalHost] = useState<Host | null>(null)
  const [terminalJobId, setTerminalJobId] = useState<string | null>(null)
  const [autoTriggerDag, setAutoTriggerDag] = useState(false)

  // Map host lookup for fast details
  const hostMap = useMemo(() => {
    const map = new Map<string, Host>()
    hosts?.forEach((h) => map.set(h.id, h))
    return map
  }, [hosts])

  // Pipeline lookup map
  const pipelineMap = useMemo(() => {
    const map = new Map<string, string>()
    pipelines?.forEach((p) => map.set(p.id, p.name))
    return map
  }, [pipelines])

  // Filtered jobs
  const filteredJobs = useMemo(() => {
    if (!jobs) return []
    return jobs.filter((job) => {
      const host = hostMap.get(job.targetHostId)
      const hostname = host?.hostname?.toLowerCase() || ''
      const ip = host?.ipAddress?.toLowerCase() || ''
      const matchesSearch =
        !searchTerm ||
        hostname.includes(searchTerm.toLowerCase()) ||
        ip.includes(searchTerm.toLowerCase()) ||
        job.id.toLowerCase().includes(searchTerm.toLowerCase())

      const matchesStatus =
        statusFilter === 'all' || job.status.toLowerCase() === statusFilter.toLowerCase()

      const pId = job.pipelineId || 'standard-os-upgrade'
      const matchesPipeline =
        pipelineFilter === 'all' || pId.toLowerCase() === pipelineFilter.toLowerCase()

      return matchesSearch && matchesStatus && matchesPipeline
    })
  }, [jobs, hostMap, searchTerm, statusFilter, pipelineFilter])

  // Aggregate metrics (excluding standalone ad-hoc shell commands)
  const workflowJobs = useMemo(
    () => jobs?.filter((j) => j.pipelineId !== 'adhoc-command' && j.pipelineId !== 'command') || [],
    [jobs]
  )
  const totalJobs = workflowJobs.length
  const runningJobs = workflowJobs.filter((j) => j.status === 'Running' || j.status === 'Verifying').length
  const completedJobs = workflowJobs.filter((j) => j.status === 'Completed').length
  const failedJobs = workflowJobs.filter((j) => j.status === 'Failed' || j.status === 'RolledBack').length

  const handleOpenTerminalForJob = (job: JobSummary) => {
    const host = hostMap.get(job.targetHostId)
    if (!host) return
    setTerminalJobId(job.id)
    setAutoTriggerDag(false)
    setTerminalHost(host)
  }

  const handlePurgeFinished = async () => {
    if (!window.confirm('Are you sure you want to clear all completed, failed, and rolled back workflows from history?')) {
      return
    }
    setIsPurging(true)
    try {
      await purgeJobs()
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      queryClient.invalidateQueries({ queryKey: ['temporalWorkflow'] })
    } finally {
      setIsPurging(false)
    }
  }

  const handleDeleteJob = async (jobId: string) => {
    setDeletingJobId(jobId)
    try {
      await deleteJob(jobId)
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      queryClient.invalidateQueries({ queryKey: ['temporalWorkflow'] })
    } finally {
      setDeletingJobId(null)
    }
  }

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'Pending':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-amber-500/10 text-amber-300 border border-amber-500/20">
            <span className="w-1.5 h-1.5 rounded-full bg-amber-400" />
            Pending
          </span>
        )
      case 'Running':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-sky-500/10 text-sky-300 border border-sky-500/20">
            <span className="w-1.5 h-1.5 rounded-full bg-sky-400 animate-spin" />
            Running
          </span>
        )
      case 'Verifying':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-purple-500/10 text-purple-300 border border-purple-500/20">
            <span className="w-1.5 h-1.5 rounded-full bg-purple-400 animate-pulse" />
            Verifying
          </span>
        )
      case 'Completed':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-500/10 text-emerald-300 border border-emerald-500/20">
            <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" />
            Completed
          </span>
        )
      case 'Failed':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-rose-500/10 text-rose-300 border border-rose-500/20">
            <AlertCircle className="w-3.5 h-3.5 text-rose-400" />
            Failed
          </span>
        )
      case 'RolledBack':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-orange-500/10 text-orange-300 border border-orange-500/20">
            <AlertCircle className="w-3.5 h-3.5 text-orange-400" />
            Rolled Back
          </span>
        )
      case 'Cancelled':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-zinc-800 text-zinc-300 border border-zinc-700">
            <XCircle className="w-3.5 h-3.5 text-zinc-400" />
            Cancelled
          </span>
        )
      default:
        return (
          <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-zinc-800 text-zinc-400">
            {status}
          </span>
        )
    }
  }

  const formatDuration = (startedAt?: string | null, completedAt?: string | null) => {
    if (!startedAt) return '—'
    const start = new Date(startedAt).getTime()
    if (isNaN(start)) return '—'
    if (!completedAt) return 'In progress'
    const end = new Date(completedAt).getTime()
    const diffSec = Math.max(0, Math.round((end - start) / 1000))
    if (diffSec < 60) return `${diffSec}s`
    const mins = Math.floor(diffSec / 60)
    const secs = diffSec % 60
    return `${mins}m ${secs}s`
  }

  return (
    <div className="space-y-6 w-full max-w-[1700px] mx-auto">
      {/* Header Banner */}
      <div className="p-6 bg-gradient-to-r from-zinc-900/90 via-zinc-900/70 to-emerald-950/30 border border-zinc-800 rounded-xl backdrop-blur-sm flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div className="flex items-start gap-4">
          <div className="p-3 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 shadow-sm">
            <GitFork className="w-6 h-6" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <h2 className="text-xl font-bold text-zinc-100">DAG Update Orchestration Engine</h2>
              <Badge variant="success" className="text-[10px]">Active Engine</Badge>
            </div>
            <p className="text-xs text-zinc-400 mt-1 max-w-2xl leading-relaxed">
              Durable, directed acyclic graph pipeline executing pre-flight safety gates (heartbeat freshness &lt; 15s, disk headroom &gt; 20%, lock inspection), non-interactive package upgrades, and real-time streaming over SignalR.
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2 shrink-0">
          <Button
            variant="outline"
            size="sm"
            onClick={() => refetch()}
            disabled={isFetching}
            className="text-xs h-9 gap-1.5 border-zinc-700 bg-zinc-900 text-zinc-300 hover:text-zinc-100"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isFetching ? 'animate-spin' : ''}`} />
            Refresh
          </Button>

          {completedJobs + failedJobs > 0 && (
            <RoleGate requiredRole="Operator" mode="disable">
              <Button
                variant="outline"
                size="sm"
                onClick={handlePurgeFinished}
                disabled={isPurging}
                className="text-xs h-9 gap-1.5 border-zinc-700 bg-zinc-900 text-zinc-300 hover:text-rose-300 hover:border-rose-800 transition-colors"
                title="Clear all completed, failed, and rolled-back workflows from history"
              >
                {isPurging ? (
                  <Loader2 className="w-3.5 h-3.5 animate-spin text-rose-400" />
                ) : (
                  <Trash2 className="w-3.5 h-3.5 text-zinc-400" />
                )}
                Clear Finished
              </Button>
            </RoleGate>
          )}

          {activeRollingBatch ? (
            <Button
              variant="outline"
              size="sm"
              onClick={() => handleOpenFleetDashboard(activeRollingBatch)}
              className="text-xs h-9 gap-1.5 border-indigo-500 bg-indigo-950/80 hover:bg-indigo-900/80 text-indigo-200 hover:text-white font-medium shadow-md shadow-indigo-950/50"
              title="View live active rolling fleet upgrade progress"
            >
              <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
              <Layers className="w-3.5 h-3.5 text-indigo-300" />
              Active Fleet ({activeRollingBatch.completedHosts}/{activeRollingBatch.totalHosts})
            </Button>
          ) : (
            <RoleGate requiredRole="Operator" mode="disable">
              <Button
                variant="outline"
                size="sm"
                onClick={() => setIsFleetLauncherOpen(true)}
                className="text-xs h-9 gap-1.5 border-indigo-700/80 bg-indigo-950/40 hover:bg-indigo-900/60 text-indigo-300 hover:text-white font-medium shadow-md shadow-indigo-950/40"
              >
                <Layers className="w-3.5 h-3.5 text-indigo-400" />
                Rolling Fleet Upgrade
              </Button>
            </RoleGate>
          )}

          <RoleGate requiredRole="Operator" mode="disable">
            <Button
              variant="primary"
              size="sm"
              onClick={() => setIsTriggerModalOpen(true)}
              className="text-xs h-9 gap-1.5 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold shadow-md shadow-emerald-950/50"
            >
              <Play className="w-3.5 h-3.5 fill-current" />
              Launch Workflow
            </Button>
          </RoleGate>
        </div>
      </div>

      {/* KPI Metric Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Total Update Jobs</span>
            <Activity className="h-4 w-4 text-zinc-400" />
          </div>
          <div className="mt-2 text-2xl font-bold text-zinc-100">{totalJobs}</div>
          <p className="text-[11px] text-zinc-500 mt-0.5">Recorded pipeline executions</p>
        </div>

        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Currently Running</span>
            {runningJobs > 0 ? (
              <span className="h-2 w-2 rounded-full bg-sky-400 inline-block" />
            ) : (
              <span className="h-2 w-2 rounded-full bg-zinc-600 inline-block" />
            )}
          </div>
          <div className="mt-2 text-2xl font-bold text-sky-400">{runningJobs}</div>
          <p className="text-[11px] text-zinc-500 mt-0.5">Active DAG state machines</p>
        </div>

        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Successful Upgrades</span>
            <CheckCircle2 className="h-4 w-4 text-emerald-400" />
          </div>
          <div className="mt-2 text-2xl font-bold text-emerald-400">{completedJobs}</div>
          <p className="text-[11px] text-zinc-500 mt-0.5">Passed pre-flight & verification</p>
        </div>

        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Safety Blocked / Failed</span>
            <AlertCircle className="h-4 w-4 text-rose-400" />
          </div>
          <div className="mt-2 text-2xl font-bold text-rose-400">{failedJobs}</div>
          <p className="text-[11px] text-zinc-500 mt-0.5">Prevented downtime or rolled back</p>
        </div>
      </div>

      {/* Active Rolling Fleet Upgrade Live Banner */}
      {activeRollingBatch && (
        <div className="p-4 bg-gradient-to-r from-indigo-950/70 via-zinc-900/80 to-indigo-950/40 border border-indigo-700/60 rounded-xl flex flex-col md:flex-row md:items-center justify-between gap-4 shadow-lg shadow-indigo-950/30">
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-xl bg-indigo-500/20 text-indigo-400 border border-indigo-500/30 shrink-0">
              <Layers className="w-5 h-5 animate-pulse" />
            </div>
            <div>
              <div className="flex items-center gap-2 flex-wrap">
                <span className="text-xs font-bold text-indigo-300 uppercase tracking-wider">Active Fleet Upgrade</span>
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-sky-500/10 text-sky-300 border border-sky-500/20">
                  <span className="w-1.5 h-1.5 rounded-full bg-sky-400 animate-ping" />
                  {activeRollingBatch.status}
                </span>
                {activeRollingBatch.activeHostname && (
                  <span className="text-xs text-zinc-300 font-mono">
                    | Currently Upgrading: <strong className="text-sky-400">{activeRollingBatch.activeHostname}</strong>
                  </span>
                )}
              </div>
              <div className="flex items-center gap-3 text-xs text-zinc-400 mt-1 flex-wrap">
                <span>Progress: <strong className="text-zinc-200">{activeRollingBatch.completedHosts} of {activeRollingBatch.totalHosts}</strong> nodes completed</span>
                <span className="text-zinc-600">•</span>
                <span>Batch: <code className="text-zinc-300 font-mono">{activeRollingBatch.batchId.slice(0, 8)}...</code></span>
                <span className="text-zinc-600">•</span>
                <span>Started: {new Date(activeRollingBatch.startedAt).toLocaleTimeString()}</span>
              </div>
            </div>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <Button
              variant="outline"
              size="sm"
              onClick={() => handleOpenFleetDashboard(activeRollingBatch)}
              className="text-xs h-8 px-3 gap-1.5 border-indigo-500/60 bg-indigo-950/70 hover:bg-indigo-900/80 text-indigo-200 hover:text-white font-medium"
            >
              <ArrowUpRight className="w-3.5 h-3.5" />
              Open Fleet Dashboard
            </Button>
          </div>
        </div>
      )}

      {/* Filter and Search Bar */}
      <div className="p-3 bg-zinc-900/40 border border-zinc-800/80 rounded-xl flex flex-col sm:flex-row items-center justify-between gap-3">
        <div className="flex items-center gap-3 w-full sm:w-auto flex-wrap">
          {/* View Mode Switcher: Host Workflows vs Fleet Upgrades */}
          <div className="flex items-center bg-zinc-950 border border-zinc-800 rounded-lg p-0.5 text-xs">
            <button
              type="button"
              onClick={() => setViewMode('workflows')}
              className={`px-3 py-1 rounded-md transition-colors ${
                viewMode === 'workflows'
                  ? 'bg-zinc-800 text-zinc-100 font-medium'
                  : 'text-zinc-400 hover:text-zinc-200'
              }`}
            >
              Host Workflows ({totalJobs})
            </button>
            <button
              type="button"
              onClick={() => setViewMode('fleet')}
              className={`px-3 py-1 rounded-md transition-colors flex items-center gap-1.5 ${
                viewMode === 'fleet'
                  ? 'bg-indigo-950 text-indigo-300 border border-indigo-700/60 font-medium'
                  : 'text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <Layers className="w-3 h-3 text-indigo-400" />
              Fleet Upgrades ({batchesList.length})
              {activeRollingBatch && (
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse" />
              )}
            </button>
          </div>

          <div className="relative w-full sm:w-64">
            <Search className="absolute left-3 top-2.5 h-4 w-4 text-zinc-500" />
            <Input
              type="text"
              placeholder="Search host or Job ID..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="pl-9 bg-zinc-950 border-zinc-800 text-xs h-8"
            />
          </div>
        </div>

        <div className="flex items-center gap-2 w-full sm:w-auto flex-wrap sm:flex-nowrap">
          {/* Pipeline filter dropdown */}
          <select
            value={pipelineFilter}
            onChange={(e) => setPipelineFilter(e.target.value)}
            className="px-2.5 py-1 text-xs bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-300 focus:outline-none focus:ring-1 focus:ring-emerald-500 h-8"
          >
            <option value="all">All Pipelines</option>
            {pipelines?.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
            <option value="adhoc-command">Ad-hoc Shell Commands</option>
          </select>

          <Filter className="w-3.5 h-3.5 text-zinc-500 hidden sm:inline" />
          <div className="flex items-center bg-zinc-950 border border-zinc-800 rounded-lg p-0.5 text-xs">
            {['all', 'running', 'completed', 'failed', 'cancelled'].map((st) => (
              <button
                key={st}
                type="button"
                onClick={() => setStatusFilter(st)}
                className={`px-3 py-1 rounded-md capitalize transition-colors ${
                  statusFilter === st
                    ? 'bg-zinc-800 text-zinc-100 font-medium'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                {st}
              </button>
            ))}
          </div>
        </div>
      </div>

      {/* Table view based on viewMode */}
      {viewMode === 'fleet' ? (
      <Table>
        <TableHeader>
          <TableRow className="border-zinc-800 bg-zinc-900/60 hover:bg-zinc-900/60">
            <TableHead className="min-w-[180px] text-zinc-400 font-medium text-xs whitespace-nowrap">Fleet Batch</TableHead>
            <TableHead className="min-w-[110px] text-zinc-400 font-medium text-xs whitespace-nowrap">Status</TableHead>
            <TableHead className="min-w-[180px] text-zinc-400 font-medium text-xs">Progress</TableHead>
            <TableHead className="min-w-[150px] text-zinc-400 font-medium text-xs whitespace-nowrap">Active Node</TableHead>
            <TableHead className="min-w-[200px] text-zinc-400 font-medium text-xs">Target Hosts</TableHead>
            <TableHead className="hidden xl:table-cell min-w-[100px] text-zinc-400 font-medium text-xs whitespace-nowrap">Operator</TableHead>
            <TableHead className="min-w-[100px] text-zinc-400 font-medium text-xs whitespace-nowrap">Started</TableHead>
            <TableHead className="w-[120px] min-w-[120px] text-right text-zinc-400 font-medium text-xs whitespace-nowrap">Action</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {batchesList.length === 0 ? (
            <TableRow>
              <TableCell colSpan={8} className="h-32 text-center text-zinc-500 text-xs">
                No rolling fleet upgrade executions recorded yet. Click &quot;Rolling Fleet Upgrade&quot; above to launch one.
              </TableCell>
            </TableRow>
          ) : (
            batchesList.map((batch) => {
              const percent = batch.totalHosts > 0 ? Math.round((batch.completedHosts / batch.totalHosts) * 100) : 0
              return (
                <TableRow key={batch.batchId} className="border-zinc-800/60 hover:bg-zinc-800/30 transition-colors">
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <Layers className="w-4 h-4 text-indigo-400 shrink-0" />
                      <span className="font-mono text-xs text-zinc-200">
                        {batch.batchId.slice(0, 8)}...
                      </span>
                    </div>
                  </TableCell>
                  <TableCell>
                    {getStatusBadge(batch.status)}
                  </TableCell>
                  <TableCell>
                    <div className="space-y-1 max-w-[160px]">
                      <div className="flex justify-between text-[11px] text-zinc-400">
                        <span>{batch.completedHosts} / {batch.totalHosts} completed</span>
                        <span className="font-mono">{percent}%</span>
                      </div>
                      <div className="w-full h-1.5 rounded-full bg-zinc-800 overflow-hidden">
                        <div
                          className={`h-full transition-all duration-300 ${
                            batch.status === 'Failed'
                              ? 'bg-rose-500'
                              : batch.status === 'Completed'
                              ? 'bg-emerald-500'
                              : 'bg-indigo-500'
                          }`}
                          style={{ width: `${percent}%` }}
                        />
                      </div>
                    </div>
                  </TableCell>
                  <TableCell>
                    {batch.activeHostname ? (
                      <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-xs font-mono bg-sky-500/10 text-sky-400 border border-sky-500/20">
                        <span className="w-1.5 h-1.5 rounded-full bg-sky-400 animate-pulse" />
                        {batch.activeHostname}
                      </span>
                    ) : (
                      <span className="text-xs text-zinc-500">—</span>
                    )}
                  </TableCell>
                  <TableCell>
                    <span className="text-xs text-zinc-300 truncate block max-w-xs" title={batch.hostnames?.join(', ')}>
                      {batch.hostnames?.join(', ') || `${batch.totalHosts} hosts`}
                    </span>
                  </TableCell>
                  <TableCell className="hidden xl:table-cell">
                    <span className="text-xs text-zinc-400">{batch.initiatedBy}</span>
                  </TableCell>
                  <TableCell>
                    <span className="text-xs text-zinc-400 font-mono">
                      {batch.startedAt ? new Date(batch.startedAt).toLocaleTimeString() : '—'}
                    </span>
                  </TableCell>
                  <TableCell className="text-right whitespace-nowrap">
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => handleOpenFleetDashboard(batch)}
                      className="text-xs h-7 px-2.5 gap-1.5 border-indigo-700/80 bg-indigo-950/40 hover:bg-indigo-900/60 text-indigo-300 hover:text-white inline-flex items-center"
                    >
                      <Layers className="w-3.5 h-3.5" />
                      Dashboard
                    </Button>
                  </TableCell>
                </TableRow>
              )
            })
          )}
        </TableBody>
      </Table>
      ) : (
      /* Jobs Table */
      <Table>
        <TableHeader>
          <TableRow className="border-zinc-800 bg-zinc-900/60 hover:bg-zinc-900/60">
            <TableHead className="min-w-[150px] text-zinc-400 font-medium text-xs whitespace-nowrap">Target Host</TableHead>
            <TableHead className="min-w-[140px] text-zinc-400 font-medium text-xs whitespace-nowrap">Pipeline</TableHead>
            <TableHead className="min-w-[110px] text-zinc-400 font-medium text-xs whitespace-nowrap">Status</TableHead>
            <TableHead className="min-w-[200px] text-zinc-400 font-medium text-xs">Active / Last Step</TableHead>
            <TableHead className="hidden xl:table-cell min-w-[100px] text-zinc-400 font-medium text-xs whitespace-nowrap">Operator</TableHead>
            <TableHead className="min-w-[100px] text-zinc-400 font-medium text-xs whitespace-nowrap">Started</TableHead>
            <TableHead className="min-w-[90px] text-zinc-400 font-medium text-xs whitespace-nowrap">Duration</TableHead>
            <TableHead className="w-[110px] min-w-[110px] text-right text-zinc-400 font-medium text-xs whitespace-nowrap">Action</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading ? (
            <TableRow>
              <TableCell colSpan={8} className="h-32 text-center text-zinc-500 text-xs">
                Loading update pipelines...
              </TableCell>
            </TableRow>
          ) : filteredJobs.length === 0 ? (
            <TableRow>
              <TableCell colSpan={8} className="h-32 text-center text-zinc-500 text-xs">
                No update jobs found. Click &quot;Launch Workflow&quot; above or start a workflow from the Host Inventory.
              </TableCell>
            </TableRow>
          ) : (
            filteredJobs.map((job) => {
              const host = hostMap.get(job.targetHostId)
              const isAdhoc = job.pipelineId === 'adhoc-command' || job.pipelineId === 'command'
              const pipelineName = isAdhoc
                ? 'Ad-hoc Shell Command'
                : (pipelineMap.get(job.pipelineId || 'standard-os-upgrade') || job.pipelineId || 'Standard Upgrade')

              return (
                <TableRow
                  key={job.id}
                  className="border-zinc-800/60 hover:bg-zinc-800/30 transition-colors"
                >
                  {/* Target Host */}
                  <TableCell>
                    {host ? (
                      <div>
                        <div className="font-semibold text-xs text-zinc-200">{host.hostname}</div>
                        <div className="text-[11px] font-mono text-zinc-500">{host.ipAddress}</div>
                      </div>
                    ) : (
                      <span className="font-mono text-xs text-zinc-400 truncate block max-w-[140px]">
                        {job.targetHostId}
                      </span>
                    )}
                  </TableCell>

                  {/* Pipeline Profile */}
                  <TableCell>
                    <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-[11px] font-medium ${
                      isAdhoc
                        ? 'bg-sky-500/10 text-sky-400 border border-sky-500/20'
                        : 'bg-zinc-800/80 text-zinc-300 border border-zinc-700/60'
                    }`}>
                      {isAdhoc ? (
                        <Terminal className="w-3 h-3 text-sky-400" />
                      ) : (
                        <GitFork className="w-3 h-3 text-emerald-400" />
                      )}
                      <span className="truncate max-w-[140px]">{pipelineName}</span>
                    </span>
                  </TableCell>

                  {/* Status */}
                  <TableCell>{getStatusBadge(job.status)}</TableCell>

                  {/* Active Step */}
                  <TableCell>
                    <div className="space-y-0.5">
                      <span className="text-xs font-mono text-zinc-300">
                        {job.activeStep || (job.status === 'Completed' ? 'All steps completed' : '—')}
                      </span>
                      {job.failureReason && (
                        <p className="text-[11px] font-mono text-rose-400 truncate max-w-xs md:max-w-md" title={job.failureReason}>
                          {job.failureReason}
                        </p>
                      )}
                    </div>
                  </TableCell>

                  {/* Initiated By */}
                  <TableCell className="hidden xl:table-cell">
                    <span className="text-xs text-zinc-400 font-medium">{job.initiatedBy}</span>
                  </TableCell>

                  {/* Started At */}
                  <TableCell>
                    <span className="text-xs text-zinc-400 flex items-center gap-1">
                      <Clock className="w-3 h-3 text-zinc-500" />
                      {job.startedAt ? new Date(job.startedAt).toLocaleTimeString() : '—'}
                    </span>
                  </TableCell>

                  {/* Duration */}
                  <TableCell>
                    <span className="text-xs font-mono text-zinc-400">
                      {formatDuration(job.startedAt, job.completedAt)}
                    </span>
                  </TableCell>

                  {/* Actions */}
                  <TableCell className="text-right whitespace-nowrap">
                    <div className="flex items-center justify-end gap-1.5">
                      {!isAdhoc && (
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => {
                            setCanvasJob(job)
                            setIsCanvasModalOpen(true)
                          }}
                          className="text-xs h-7 px-2.5 gap-1.5 border-zinc-700 bg-zinc-800/60 hover:bg-zinc-800 text-emerald-400 hover:text-emerald-300 inline-flex items-center shrink-0"
                          title="Open interactive DAG canvas"
                        >
                          <GitFork className="w-3.5 h-3.5 shrink-0" />
                          Visual DAG
                        </Button>
                      )}
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => handleOpenTerminalForJob(job)}
                        className="text-xs h-7 px-2.5 gap-1.5 border-zinc-700 bg-zinc-800/60 hover:bg-zinc-800 text-sky-400 hover:text-sky-300 inline-flex items-center shrink-0"
                        title="Open streaming terminal console"
                      >
                        <Terminal className="w-3.5 h-3.5 shrink-0" />
                        {isAdhoc ? 'Terminal Log' : 'Console'}
                      </Button>
                      {(job.status === 'Completed' || job.status === 'Failed' || job.status === 'RolledBack' || job.status === 'Cancelled') && (
                        <RoleGate requiredRole="Operator" mode="disable">
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => handleDeleteJob(job.id)}
                            disabled={deletingJobId === job.id}
                            className="text-xs h-7 w-7 p-0 border-zinc-700/80 bg-zinc-800/40 hover:bg-rose-950/40 hover:border-rose-800 text-zinc-400 hover:text-rose-300 inline-flex items-center justify-center shrink-0 transition-colors"
                            title="Delete this workflow from history"
                          >
                            {deletingJobId === job.id ? (
                              <Loader2 className="w-3 h-3 animate-spin text-rose-400" />
                            ) : (
                              <Trash2 className="w-3 h-3" />
                            )}
                          </Button>
                        </RoleGate>
                      )}
                    </div>
                  </TableCell>
                </TableRow>
              )
            })
          )}
        </TableBody>
      </Table>
      )}

      {/* Modular Pipeline Launch Modal */}
      <LaunchWorkflowModal
        isOpen={isTriggerModalOpen}
        onClose={() => setIsTriggerModalOpen(false)}
        availableHosts={hosts || []}
        onWorkflowLaunched={(jobId, host, workflowId) => {
          setIsTriggerModalOpen(false)
          refetch()
          setCanvasJob({
            id: workflowId || jobId,
            targetHostId: host.id,
            pipelineId: 'temporal-host-upgrade',
            status: 'Running',
            activeStep: 'Preflight Checks',
            initiatedBy: 'Operator',
            startedAt: new Date().toISOString(),
            completedAt: null,
            failureReason: null,
          })
          setIsCanvasModalOpen(true)
        }}
      />

      {/* Live Terminal Drawer */}
      {terminalHost && (
        <HostTerminalDrawer
          host={terminalHost}
          isOpen={Boolean(terminalHost)}
          initialJobId={terminalJobId}
          autoTriggerDag={autoTriggerDag}
          isWorkflow={Boolean(
            (terminalJobId &&
              jobs?.find((j) => j.id === terminalJobId)?.pipelineId !== 'adhoc-command' &&
              jobs?.find((j) => j.id === terminalJobId)?.pipelineId !== 'command') ||
            autoTriggerDag
          )}
          onClose={() => {
            setTerminalHost(null)
            setTerminalJobId(null)
            setAutoTriggerDag(false)
          }}
        />
      )}

      {/* Interactive Visual DAG Canvas Modal */}
      {canvasJob && (
        <WorkflowCanvasModal
          isOpen={isCanvasModalOpen}
          onClose={() => {
            setIsCanvasModalOpen(false)
            setCanvasJob(null)
          }}
          job={canvasJob}
          host={hostMap.get(canvasJob.targetHostId) || null}
          onOpenTerminal={(job) => {
            handleOpenTerminalForJob(job)
          }}
        />
      )}

      {/* Fleet Rolling Launcher Modal */}
      <FleetRollingLauncherModal
        isOpen={isFleetLauncherOpen}
        onClose={() => setIsFleetLauncherOpen(false)}
        availableHosts={hosts || []}
        onFleetLaunched={(batchId, targetHosts) => {
          setIsFleetLauncherOpen(false)
          setActiveFleetBatchId(batchId)
          setActiveFleetHosts(targetHosts)
          setIsFleetDashboardOpen(true)
          refetchRollingBatches()
        }}
      />

      {/* Fleet Rolling Dashboard Modal */}
      <FleetRollingDashboardModal
        isOpen={isFleetDashboardOpen}
        onClose={() => {
          setIsFleetDashboardOpen(false)
          setActiveFleetBatchId(null)
          setActiveFleetHosts([])
          refetchRollingBatches()
        }}
        batchId={activeFleetBatchId}
        targetHosts={activeFleetHosts}
        onOpenTerminalForJob={handleOpenTerminalForJob}
      />
    </div>
  )
}
