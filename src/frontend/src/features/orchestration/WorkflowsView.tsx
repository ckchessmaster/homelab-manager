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
  ShieldCheck,
  Sparkles,
} from 'lucide-react'
import { useJobs, useCreateJob } from './useJobs'
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
import { Dialog, DialogHeader, DialogTitle, DialogBody, DialogFooter } from '../../components/ui/dialog'
import { HostTerminalDrawer } from '../hosts/HostTerminalDrawer'
import type { Host } from '../../api/hosts'
import type { JobSummary } from '../../api/jobs'

export function WorkflowsView() {
  const { data: jobs, isLoading, isFetching, refetch } = useJobs()
  const { data: hosts } = useHosts()
  const createJobMutation = useCreateJob()

  const [searchTerm, setSearchTerm] = useState('')
  const [statusFilter, setStatusFilter] = useState<string>('all')
  const [isTriggerModalOpen, setIsTriggerModalOpen] = useState(false)
  const [selectedHostId, setSelectedHostId] = useState<string>('')

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

      return matchesSearch && matchesStatus
    })
  }, [jobs, hostMap, searchTerm, statusFilter])

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

  const handleTriggerNewJob = async () => {
    if (!selectedHostId) return
    const host = hostMap.get(selectedHostId)
    if (!host) return

    try {
      const job = await createJobMutation.mutateAsync(selectedHostId)
      setIsTriggerModalOpen(false)
      setSelectedHostId('')
      setTerminalJobId(job.id)
      setAutoTriggerDag(false)
      setTerminalHost(host)
    } catch {
      // handled by mutation
    }
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
    <div className="space-y-6 max-w-7xl mx-auto">
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
            <Sparkles className="w-3.5 h-3.5" />
            Trigger DAG Update
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

        <div className="flex items-center gap-2 w-full sm:w-auto">
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
      <div className="border border-zinc-800 rounded-xl overflow-hidden bg-zinc-900/40 backdrop-blur-sm">
        <Table>
          <TableHeader>
            <TableRow className="border-zinc-800 bg-zinc-900/60 hover:bg-zinc-900/60">
              <TableHead className="w-[200px] text-zinc-400 font-medium text-xs">Target Host</TableHead>
              <TableHead className="w-[130px] text-zinc-400 font-medium text-xs">Status</TableHead>
              <TableHead className="text-zinc-400 font-medium text-xs">Active / Last Step</TableHead>
              <TableHead className="w-[100px] text-zinc-400 font-medium text-xs">Operator</TableHead>
              <TableHead className="w-[140px] text-zinc-400 font-medium text-xs">Started</TableHead>
              <TableHead className="w-[100px] text-zinc-400 font-medium text-xs">Duration</TableHead>
              <TableHead className="w-[120px] text-right text-zinc-400 font-medium text-xs">Action</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading ? (
              <TableRow>
                <TableCell colSpan={7} className="h-32 text-center text-zinc-500 text-xs">
                  Loading update pipelines...
                </TableCell>
              </TableRow>
            ) : filteredJobs.length === 0 ? (
              <TableRow>
                <TableCell colSpan={7} className="h-32 text-center text-zinc-500 text-xs">
                  No update jobs found. Click &quot;Trigger DAG Update&quot; above or run an update from the Host Inventory.
                </TableCell>
              </TableRow>
            ) : (
              filteredJobs.map((job) => {
                const host = hostMap.get(job.targetHostId)
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
                        <span className="font-mono text-xs text-zinc-400 truncate block max-w-[160px]">
                          {job.targetHostId}
                        </span>
                      )}
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
                          <p className="text-[11px] font-mono text-rose-400 truncate max-w-md" title={job.failureReason}>
                            {job.failureReason}
                          </p>
                        )}
                      </div>
                    </TableCell>

                    {/* Initiated By */}
                    <TableCell>
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
                    <TableCell className="text-right">
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => handleOpenTerminalForJob(job)}
                        className="text-xs h-7 px-2.5 gap-1.5 border-zinc-700 bg-zinc-800/60 hover:bg-zinc-800 text-sky-400 hover:text-sky-300"
                        title="Open streaming terminal console"
                      >
                        <Terminal className="w-3.5 h-3.5" />
                        Console
                      </Button>
                    </TableCell>
                  </TableRow>
                )
              })
            )}
          </TableBody>
        </Table>
      </div>

      {/* Trigger DAG Update Modal */}
      {isTriggerModalOpen && (
        <Dialog open={isTriggerModalOpen} onClose={() => setIsTriggerModalOpen(false)} maxWidth="md">
          <DialogHeader onClose={() => setIsTriggerModalOpen(false)}>
            <div className="flex items-center gap-2">
              <div className="p-1.5 rounded-lg bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                <Sparkles className="w-4 h-4" />
              </div>
              <DialogTitle>Trigger DAG Host Upgrade</DialogTitle>
            </div>
          </DialogHeader>

          <DialogBody className="space-y-4">
            <p className="text-xs text-zinc-400">
              Select a managed target node to initiate the durable update pipeline. ControlPlane will run pre-flight safety checks, verify package locks, stream package upgrades, and monitor health.
            </p>

            <div className="space-y-2">
              <label className="text-xs font-medium text-zinc-300">Target Managed Node</label>
              <select
                value={selectedHostId}
                onChange={(e) => setSelectedHostId(e.target.value)}
                className="w-full px-3 py-2 text-xs bg-zinc-950 border border-zinc-800 rounded-md text-zinc-200 focus:outline-none focus:ring-1 focus:ring-emerald-500"
              >
                <option value="">-- Choose Host --</option>
                {hosts
                  ?.filter((h) => h.agent?.installed)
                  .map((h) => (
                    <option key={h.id} value={h.id}>
                      {h.hostname} ({h.ipAddress}) — {h.agent?.upgradablePackagesCount || 0} packages upgradable
                    </option>
                  ))}
              </select>
            </div>

            {/* Preflight Safety Gates Preview */}
            <div className="p-3.5 bg-zinc-950/70 border border-zinc-800/80 rounded-lg space-y-2 text-xs">
              <span className="font-semibold text-zinc-300 flex items-center gap-1.5">
                <ShieldCheck className="w-4 h-4 text-emerald-400" />
                Durable Pre-Flight Safety Protocol:
              </span>
              <ul className="space-y-1.5 text-zinc-400 text-[11px] pl-5 list-disc">
                <li>Heartbeat Freshness: Verifies agent WebSocket heartbeat &lt; 15 seconds.</li>
                <li>Disk Headroom Check: Validates root filesystem free space &gt; 20%.</li>
                <li>Package Lock Inspection: Ensures no stale apt/dnf locks exist.</li>
                <li>Non-Interactive Package Upgrade: Executes dist-upgrade with log streaming.</li>
              </ul>
            </div>
          </DialogBody>

          <DialogFooter>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => setIsTriggerModalOpen(false)}
            >
              Cancel
            </Button>
            <Button
              variant="primary"
              size="sm"
              disabled={!selectedHostId || createJobMutation.isPending}
              onClick={handleTriggerNewJob}
              className="gap-1.5 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold"
            >
              <Play className="w-3.5 h-3.5 fill-current" />
              {createJobMutation.isPending ? 'Starting...' : 'Launch Upgrade DAG'}
            </Button>
          </DialogFooter>
        </Dialog>
      )}

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
