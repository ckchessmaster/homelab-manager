import { useState, useMemo } from 'react'
import {
  GitFork,
  Play,
  Terminal,
  CheckCircle2,
  AlertCircle,
  Clock,
  Search,
  Filter,
  RefreshCw,
  Activity,
} from 'lucide-react'
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
import type { Host } from '../../api/hosts'
import type { JobSummary } from '../../api/jobs'

export function WorkflowsView() {
  const { data: jobs, isLoading, isFetching, refetch } = useJobs()
  const { data: hosts } = useHosts()
  const { data: pipelines } = usePipelines()

  const [searchTerm, setSearchTerm] = useState('')
  const [statusFilter, setStatusFilter] = useState<string>('all')
  const [pipelineFilter, setPipelineFilter] = useState<string>('all')
  const [isTriggerModalOpen, setIsTriggerModalOpen] = useState(false)

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

  // Aggregate metrics
  const totalJobs = jobs?.length || 0
  const runningJobs = jobs?.filter((j) => j.status === 'Running' || j.status === 'Verifying').length || 0
  const completedJobs = jobs?.filter((j) => j.status === 'Completed').length || 0
  const failedJobs = jobs?.filter((j) => j.status === 'Failed' || j.status === 'RolledBack').length || 0

  const handleOpenTerminalForJob = (job: JobSummary) => {
    const host = hostMap.get(job.targetHostId)
    if (!host) return
    setTerminalJobId(job.id)
    setAutoTriggerDag(false)
    setTerminalHost(host)
  }

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'Pending':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-amber-500/10 text-amber-300 border border-amber-500/20">
            <span className="w-1.5 h-1.5 rounded-full bg-amber-400 animate-pulse" />
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
          <Button
            variant="primary"
            size="sm"
            onClick={() => setIsTriggerModalOpen(true)}
            className="text-xs h-9 gap-1.5 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold shadow-md shadow-emerald-950/50"
          >
            <Play className="w-3.5 h-3.5 fill-current" />
            Launch Workflow
          </Button>
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
              <span className="h-2 w-2 rounded-full bg-sky-400 animate-ping inline-block" />
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

      {/* Filter and Search Bar */}
      <div className="p-3 bg-zinc-900/40 border border-zinc-800/80 rounded-xl flex flex-col sm:flex-row items-center justify-between gap-3">
        <div className="relative w-full sm:w-72">
          <Search className="absolute left-3 top-2.5 h-4 w-4 text-zinc-500" />
          <Input
            type="text"
            placeholder="Search host or Job ID..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="pl-9 bg-zinc-950 border-zinc-800 text-xs h-9"
          />
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
          </select>

          <Filter className="w-3.5 h-3.5 text-zinc-500 hidden sm:inline" />
          <div className="flex items-center bg-zinc-950 border border-zinc-800 rounded-lg p-0.5 text-xs">
            {['all', 'running', 'completed', 'failed'].map((st) => (
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

      {/* Jobs Table */}
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
              const pipelineName =
                pipelineMap.get(job.pipelineId || 'standard-os-upgrade') ||
                job.pipelineId ||
                'Standard Upgrade'

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
                    <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-[11px] font-medium bg-zinc-800/80 text-zinc-300 border border-zinc-700/60">
                      <GitFork className="w-3 h-3 text-emerald-400" />
                      <span className="truncate max-w-[120px]">{pipelineName}</span>
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
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => handleOpenTerminalForJob(job)}
                      className="text-xs h-7 px-2.5 gap-1.5 border-zinc-700 bg-zinc-800/60 hover:bg-zinc-800 text-sky-400 hover:text-sky-300 inline-flex items-center shrink-0"
                      title="Open streaming terminal console"
                    >
                      <Terminal className="w-3.5 h-3.5 shrink-0" />
                      Console
                    </Button>
                  </TableCell>
                </TableRow>
              )
            })
          )}
        </TableBody>
      </Table>

      {/* Modular Pipeline Launch Modal */}
      <LaunchWorkflowModal
        isOpen={isTriggerModalOpen}
        onClose={() => setIsTriggerModalOpen(false)}
        availableHosts={hosts || []}
        onWorkflowLaunched={(jobId, host) => {
          setIsTriggerModalOpen(false)
          setTerminalJobId(jobId)
          setAutoTriggerDag(false)
          setTerminalHost(host)
        }}
      />

      {/* Live Terminal Drawer */}
      {terminalHost && (
        <HostTerminalDrawer
          host={terminalHost}
          isOpen={Boolean(terminalHost)}
          initialJobId={terminalJobId}
          autoTriggerDag={autoTriggerDag}
          onClose={() => {
            setTerminalHost(null)
            setTerminalJobId(null)
            setAutoTriggerDag(false)
          }}
        />
      )}
    </div>
  )
}
