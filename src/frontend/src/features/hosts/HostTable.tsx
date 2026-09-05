import { useState, useMemo } from 'react'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '../../components/ui/table'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import {
  OsBadge,
  TargetTypeBadge,
  AgentStatusBadge,
  RebootBadge,
  UpdatesBadge,
} from './HostStatusBadge'
import { HostDetailsModal } from './HostDetailsModal'
import { EditHostModal } from './EditHostModal'
import { HostTerminalDrawer } from './HostTerminalDrawer'
import { AdoptNodeModal } from './AdoptNodeModal'
import { MassAgentUpdateModal } from './MassAgentUpdateModal'
import { useDeleteHost, useHosts } from './useHosts'
import { useAgentVersionInfo } from './useAgentUpdates'
import {
  Search,
  Plus,
  RefreshCw,
  Trash2,
  Eye,
  Pencil,
  Copy,
  Check,
  Server,
  AlertTriangle,
  ArrowUpCircle,
  HardDrive,
  Terminal,
  Shield,
  Sparkles,
  MoreVertical,
  RotateCcw,
  ChevronLeft,
  ChevronRight,
  Camera,
} from 'lucide-react'
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuLabel,
} from '../../components/ui/dropdown-menu'
import { RebootHostModal } from './RebootHostModal'
import { LaunchWorkflowModal } from '../orchestration/LaunchWorkflowModal'
import { SnapshotManagementModal } from '../snapshots/SnapshotManagementModal'
import { createJob } from '../../api/jobs'
import type { Host, HostFilterParams } from '../../api/hosts'

interface HostTableProps {
  onOpenAddModal: () => void
}

export function HostTable({ onOpenAddModal }: HostTableProps) {
  const [searchTerm, setSearchTerm] = useState('')
  const [selectedOs, setSelectedOs] = useState('')
  const [selectedTarget, setSelectedTarget] = useState('')
  const [onlyReboot, setOnlyReboot] = useState(false)
  const [onlyUpdates, setOnlyUpdates] = useState(false)

  const [inspectHost, setInspectHost] = useState<Host | null>(null)
  const [hostToEdit, setHostToEdit] = useState<Host | null>(null)
  const [hostToDelete, setHostToDelete] = useState<Host | null>(null)
  const [terminalHost, setTerminalHost] = useState<Host | null>(null)
  const [adoptHost, setAdoptHost] = useState<Host | null>(null)
  const [isAdoptModalOpen, setIsAdoptModalOpen] = useState(false)
  const [copiedIp, setCopiedIp] = useState<string | null>(null)
  const [terminalJobId, setTerminalJobId] = useState<string | null>(null)
  const [autoTriggerUpdate, setAutoTriggerUpdate] = useState(false)
  const [isMassUpdateModalOpen, setIsMassUpdateModalOpen] = useState(false)

  const { data: agentVersionInfo } = useAgentVersionInfo()

  // Selection & Reboot state
  const [selectedHostIds, setSelectedHostIds] = useState<Set<string>>(new Set())
  const [rebootModalHost, setRebootModalHost] = useState<Host | null>(null)
  const [rebootBulkHosts, setRebootBulkHosts] = useState<Host[] | null>(null)
  const [workflowModalHost, setWorkflowModalHost] = useState<Host | null>(null)
  const [snapshotModalHost, setSnapshotModalHost] = useState<Host | null>(null)
  const [isSnapshotModalOpen, setIsSnapshotModalOpen] = useState(false)

  // Pagination state
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [prevFilterKey, setPrevFilterKey] = useState('')

  const currentFilterKey = `${searchTerm}|${selectedOs}|${selectedTarget}|${onlyReboot}|${onlyUpdates}|${pageSize}`
  if (prevFilterKey !== currentFilterKey) {
    setPrevFilterKey(currentFilterKey)
    setPage(1)
  }

  const handleTriggerUpdate = (host: Host) => {
    setWorkflowModalHost(host)
  }

  const handleWorkflowLaunched = (jobId: string, host: Host) => {
    setTerminalJobId(jobId)
    setAutoTriggerUpdate(false)
    setTerminalHost(host)
  }

  const filters: HostFilterParams = {
    search: searchTerm || undefined,
    osFamily: selectedOs || undefined,
    targetType: selectedTarget || undefined,
    pendingReboot: onlyReboot ? true : undefined,
    hasUpdates: onlyUpdates ? true : undefined,
  }

  const { data: hosts, isLoading, isError, error, refetch, isFetching } = useHosts(filters)
  const deleteMutation = useDeleteHost()

  const totalHosts = hosts?.length ?? 0
  const totalPages = Math.max(1, Math.ceil(totalHosts / pageSize))
  const startIndex = (page - 1) * pageSize
  const endIndex = Math.min(startIndex + pageSize, totalHosts)

  const paginatedHosts = useMemo(() => {
    if (!hosts) return []
    return hosts.slice(startIndex, endIndex)
  }, [hosts, startIndex, endIndex])

  const toggleSelectAll = () => {
    if (!paginatedHosts || paginatedHosts.length === 0) return
    const allPageSelected = paginatedHosts.every((h) => selectedHostIds.has(h.id))
    setSelectedHostIds((prev) => {
      const next = new Set(prev)
      if (allPageSelected) {
        paginatedHosts.forEach((h) => next.delete(h.id))
      } else {
        paginatedHosts.forEach((h) => next.add(h.id))
      }
      return next
    })
  }

  const toggleSelectHost = (id: string) => {
    setSelectedHostIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }

  const handleBulkReboot = () => {
    if (!hosts) return
    const selected = hosts.filter((h) => selectedHostIds.has(h.id))
    if (selected.length === 0) return
    setRebootBulkHosts(selected)
  }

  const handleBulkUpdate = async () => {
    if (!hosts) return
    const selected = hosts.filter((h) => selectedHostIds.has(h.id) && h.agent.installed)
    if (selected.length === 0) return
    for (const h of selected) {
      try {
        await createJob(h.id)
      } catch {
        // continue
      }
    }
    setSelectedHostIds(new Set())
    handleTriggerUpdate(selected[0])
  }

  const handleCopyIp = (ip: string, e: React.MouseEvent) => {
    e.stopPropagation()
    navigator.clipboard.writeText(ip)
    setCopiedIp(ip)
    setTimeout(() => setCopiedIp(null), 2000)
  }

  const handleDeleteConfirm = async () => {
    if (!hostToDelete) return
    try {
      await deleteMutation.mutateAsync(hostToDelete.id)
      setHostToDelete(null)
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to delete host')
    }
  }

  return (
    <div className="space-y-4">
      {/* Controls / Filter Bar */}
      <div className="flex flex-col md:flex-row gap-3 items-stretch md:items-center justify-between p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-md">
        <div className="flex flex-1 flex-wrap items-center gap-3">
          {/* Search Box */}
          <div className="relative min-w-[220px] flex-1 max-w-sm">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-zinc-500" />
            <Input
              placeholder="Search hostname, IP, friendly name..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="pl-9 bg-zinc-950/80"
            />
          </div>

          {/* OS Filter */}
          <div className="w-36">
            <Select
              value={selectedOs}
              onChange={(e) => setSelectedOs(e.target.value)}
              className="bg-zinc-950/80"
            >
              <option value="">All OS Families</option>
              <option value="linux_debian">Debian</option>
              <option value="linux_ubuntu">Ubuntu</option>
              <option value="linux_rhel">RHEL / Rocky</option>
              <option value="windows">Windows</option>
            </Select>
          </div>

          {/* Target Type Filter */}
          <div className="w-36">
            <Select
              value={selectedTarget}
              onChange={(e) => setSelectedTarget(e.target.value)}
              className="bg-zinc-950/80"
            >
              <option value="">All Types</option>
              <option value="baremetal">Bare-Metal</option>
              <option value="proxmox_vm">Proxmox VM</option>
              <option value="proxmox_lxc">Proxmox LXC</option>
            </Select>
          </div>

          {/* Quick Filter Toggles */}
          <div className="flex items-center gap-1.5">
            <button
              type="button"
              onClick={() => setOnlyReboot(!onlyReboot)}
              className={`inline-flex items-center gap-1 text-xs px-2.5 py-1.5 rounded-lg border transition-colors cursor-pointer ${
                onlyReboot
                  ? 'bg-amber-950/80 border-amber-600 text-amber-300 font-medium'
                  : 'bg-zinc-950/40 border-zinc-800 text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <AlertTriangle className="h-3 w-3" />
              Reboot Pending
            </button>

            <button
              type="button"
              onClick={() => setOnlyUpdates(!onlyUpdates)}
              className={`inline-flex items-center gap-1 text-xs px-2.5 py-1.5 rounded-lg border transition-colors cursor-pointer ${
                onlyUpdates
                  ? 'bg-sky-950/80 border-sky-600 text-sky-300 font-medium'
                  : 'bg-zinc-950/40 border-zinc-800 text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <ArrowUpCircle className="h-3 w-3" />
              Updates Available
            </button>
          </div>
        </div>

        {/* Action Buttons */}
        <div className="flex items-center gap-2 self-end md:self-auto">
          <Button
            variant="outline"
            size="sm"
            onClick={() => refetch()}
            disabled={isFetching}
            title="Refresh hosts list"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} />
          </Button>

          <Button
            variant="outline"
            size="sm"
            onClick={() => setIsMassUpdateModalOpen(true)}
            className={`gap-1.5 transition-colors ${
              (agentVersionInfo?.outdatedAgentsCount ?? 0) > 0
                ? 'border-amber-700/80 bg-amber-950/40 text-amber-300 hover:bg-amber-900/60 shadow-xs shadow-amber-900/20'
                : 'border-zinc-800 bg-zinc-950/40 text-zinc-300 hover:bg-zinc-900/60'
            }`}
          >
            <ArrowUpCircle className={`h-4 w-4 ${(agentVersionInfo?.outdatedAgentsCount ?? 0) > 0 ? 'text-amber-400' : 'text-zinc-400'}`} />
            <span>Agent Updates</span>
            {(agentVersionInfo?.outdatedAgentsCount ?? 0) > 0 && (
              <span className="ml-0.5 px-1.5 py-0.2 rounded-full text-[10px] font-bold bg-amber-500/20 text-amber-300 border border-amber-500/30">
                {agentVersionInfo?.outdatedAgentsCount}
              </span>
            )}
          </Button>

          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              setSnapshotModalHost(null)
              setIsSnapshotModalOpen(true)
            }}
            className="gap-1.5 border-purple-800/80 bg-purple-950/40 text-purple-300 hover:bg-purple-900/60"
            title="Manage Proxmox hypervisor snapshots & retention"
          >
            <Camera className="h-4 w-4" />
            <span>Snapshots</span>
          </Button>

          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              setAdoptHost(null)
              setIsAdoptModalOpen(true)
            }}
            className="gap-1.5 border-sky-800/80 bg-sky-950/40 text-sky-300 hover:bg-sky-900/60"
          >
            <Shield className="h-4 w-4" />
            Adopt Server
          </Button>

          <Button
            variant="primary"
            size="sm"
            onClick={onOpenAddModal}
            className="gap-1.5"
          >
            <Plus className="h-4 w-4" />
            Add Host
          </Button>
        </div>
      </div>

      {/* Outdated Agents Banner */}
      {(agentVersionInfo?.onlineOutdatedCount ?? 0) > 0 && (
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 bg-gradient-to-r from-amber-950/50 via-zinc-900/70 to-zinc-900/50 border border-amber-800/50 rounded-xl backdrop-blur-md shadow-lg shadow-amber-950/10 animate-in fade-in">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-amber-500/10 border border-amber-500/20 text-amber-400 shrink-0">
              <ArrowUpCircle className="w-5 h-5 animate-pulse" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xs font-semibold text-zinc-200">
                  {agentVersionInfo?.onlineOutdatedCount} Agent Daemon{agentVersionInfo?.onlineOutdatedCount === 1 ? '' : 's'} Ready for In-Band Upgrade
                </span>
                <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-300 border border-amber-500/20">
                  Target v{agentVersionInfo?.serverVersion}
                </span>
              </div>
              <p className="text-[11px] text-zinc-400 mt-0.5">
                Dispatch atomic self-update commands over active WebSocket connections with zero service downtime.
              </p>
            </div>
          </div>
          <Button
            size="sm"
            variant="primary"
            onClick={() => setIsMassUpdateModalOpen(true)}
            className="gap-1.5 bg-amber-600 hover:bg-amber-500 text-white border-amber-500 text-xs shrink-0 self-start sm:self-auto"
          >
            <Sparkles className="w-3.5 h-3.5" />
            Update All ({agentVersionInfo?.onlineOutdatedCount})
          </Button>
        </div>
      )}

      {/* Main Table */}
      {isLoading ? (
        <div className="p-12 text-center border border-zinc-800 rounded-xl bg-zinc-900/30">
          <RefreshCw className="h-6 w-6 animate-spin mx-auto text-emerald-400 mb-2" />
          <p className="text-sm text-zinc-400">Loading host inventory...</p>
        </div>
      ) : isError ? (
        <div className="p-8 text-center border border-rose-800/60 rounded-xl bg-rose-950/20 text-rose-300">
          <AlertTriangle className="h-6 w-6 mx-auto text-rose-400 mb-2" />
          <p className="text-sm font-medium">Failed to load hosts</p>
          <p className="text-xs text-rose-400 mt-1">
            {error instanceof Error ? error.message : 'Network error'}
          </p>
          <Button variant="outline" size="sm" onClick={() => refetch()} className="mt-4">
            Try Again
          </Button>
        </div>
      ) : !hosts || hosts.length === 0 ? (
        <div className="p-12 text-center border border-zinc-800 rounded-xl bg-zinc-900/30">
          <Server className="h-10 w-10 mx-auto text-zinc-600 mb-3" />
          <h3 className="text-base font-medium text-zinc-200">No hosts found</h3>
          <p className="text-xs text-zinc-400 max-w-sm mx-auto mt-1 mb-4">
            {searchTerm || selectedOs || selectedTarget || onlyReboot || onlyUpdates
              ? 'No managed hosts match your current filter query. Try clearing the filters.'
              : 'Your homelab inventory is empty. Register your first managed node to get started.'}
          </p>
          <Button variant="primary" size="sm" onClick={onOpenAddModal}>
            Register a Host
          </Button>
        </div>
      ) : (
        <div className="space-y-3">
          <Table containerClassName="min-h-[340px]">
          <TableHeader>
            <TableRow>
              <TableHead className="w-10 text-center">
                <input
                  type="checkbox"
                  aria-label="Select all hosts"
                  checked={paginatedHosts.length > 0 && paginatedHosts.every((h) => selectedHostIds.has(h.id))}
                  onChange={toggleSelectAll}
                  className="rounded border-zinc-700 bg-zinc-900 text-emerald-500 focus:ring-emerald-500/20 cursor-pointer h-4 w-4"
                />
              </TableHead>
              <TableHead>Host & Platform</TableHead>
              <TableHead>IP & Network</TableHead>
              <TableHead>OS Family</TableHead>
              <TableHead>Agent Status</TableHead>
              <TableHead>Vitals & Flags</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {paginatedHosts.map((host) => (
              <TableRow
                key={host.id}
                className="cursor-pointer"
                onClick={() => setInspectHost(host)}
              >
                {/* Select Checkbox */}
                <TableCell className="w-10 text-center" onClick={(e) => e.stopPropagation()}>
                  <input
                    type="checkbox"
                    aria-label={`Select ${host.hostname}`}
                    checked={selectedHostIds.has(host.id)}
                    onChange={() => toggleSelectHost(host.id)}
                    className="rounded border-zinc-700 bg-zinc-900 text-emerald-500 focus:ring-emerald-500/20 cursor-pointer h-4 w-4"
                  />
                </TableCell>

                {/* Host Column */}
                <TableCell>
                  <div className="flex items-center gap-3">
                    <div className="p-2 rounded-lg bg-zinc-800/60 border border-zinc-700/50 text-zinc-300">
                      <HardDrive className="h-4 w-4" />
                    </div>
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-zinc-100">{host.hostname}</span>
                        <TargetTypeBadge type={host.targetType} />
                      </div>
                      {host.friendlyName && (
                        <div className="text-xs text-zinc-400 mt-0.5">{host.friendlyName}</div>
                      )}
                    </div>
                  </div>
                </TableCell>

                {/* IP & Network */}
                <TableCell>
                  <div className="space-y-1">
                    <div className="flex items-center gap-1.5">
                      <span className="font-mono text-xs text-zinc-200">{host.ipAddress}</span>
                      <button
                        type="button"
                        onClick={(e) => handleCopyIp(host.ipAddress, e)}
                        className="p-1 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-800 transition-colors"
                        title="Copy IP"
                      >
                        {copiedIp === host.ipAddress ? (
                          <Check className="h-3 w-3 text-emerald-400" />
                        ) : (
                          <Copy className="h-3 w-3" />
                        )}
                      </button>
                    </div>
                    {host.networkPort && (
                      <div className="text-[11px] text-zinc-500">
                        UniFi Port #{host.networkPort.portNumber}
                      </div>
                    )}
                  </div>
                </TableCell>

                {/* OS */}
                <TableCell>
                  <OsBadge osFamily={host.osFamily} />
                </TableCell>

                {/* Agent Status */}
                <TableCell>
                  <AgentStatusBadge agent={host.agent} targetVersion={agentVersionInfo?.serverVersion} />
                </TableCell>

                {/* Vitals */}
                <TableCell>
                  <div className="flex flex-wrap items-center gap-2">
                    <span
                      onClick={(e) => {
                        if (host.agent.pendingReboot) {
                          e.stopPropagation()
                          setRebootModalHost(host)
                        }
                      }}
                      className={host.agent.pendingReboot ? 'cursor-pointer hover:opacity-80 transition-opacity' : ''}
                      title={host.agent.pendingReboot ? 'Click to Reboot Node' : undefined}
                    >
                      <RebootBadge pending={host.agent.pendingReboot} />
                    </span>
                    <span
                      onClick={(e) => {
                        if (host.agent.installed && host.agent.upgradablePackagesCount > 0) {
                          e.stopPropagation()
                          handleTriggerUpdate(host)
                        }
                      }}
                      className={host.agent.installed && host.agent.upgradablePackagesCount > 0 ? 'cursor-pointer hover:opacity-80 transition-opacity' : ''}
                      title={host.agent.installed ? 'Click to run DAG Update' : undefined}
                    >
                      <UpdatesBadge count={host.agent.upgradablePackagesCount} />
                    </span>
                    {!host.agent.pendingReboot && host.agent.upgradablePackagesCount === 0 && (
                      <span className="text-xs text-zinc-500">Clean</span>
                    )}
                  </div>
                </TableCell>

                {/* Consolidated Actions */}
                <TableCell className="text-right" onClick={(e) => e.stopPropagation()}>
                  <div className="flex items-center justify-end gap-1">
                    {/* Contextual Quick Action */}
                    {!host.agent.installed ? (
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-8 w-8 text-sky-400 hover:text-sky-300 hover:bg-sky-950/50"
                        onClick={() => {
                          setAdoptHost(host)
                          setIsAdoptModalOpen(true)
                        }}
                        title="Adopt Server via SSH"
                      >
                        <Shield className="h-4 w-4" />
                      </Button>
                    ) : host.agent.pendingReboot ? (
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-8 w-8 text-amber-400 hover:text-amber-300 hover:bg-amber-950/50"
                        onClick={() => setRebootModalHost(host)}
                        title="Reboot Node (Kernel Pending)"
                      >
                        <RotateCcw className="h-4 w-4" />
                      </Button>
                    ) : host.agent.upgradablePackagesCount > 0 ? (
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-8 w-8 text-emerald-400 hover:text-emerald-300 hover:bg-emerald-950/50"
                        onClick={() => handleTriggerUpdate(host)}
                        title="Run DAG Update Pipeline"
                      >
                        <Sparkles className="h-4 w-4" />
                      </Button>
                    ) : null}

                    {/* Console button */}
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8 text-zinc-400 hover:text-sky-400 hover:bg-zinc-800/60"
                      onClick={() => {
                        setTerminalJobId(null)
                        setAutoTriggerUpdate(false)
                        setTerminalHost(host)
                      }}
                      title="Open Terminal Console"
                    >
                      <Terminal className="h-4 w-4" />
                    </Button>

                    {/* Dropdown Menu */}
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button
                          variant="ghost"
                          size="icon"
                          className="h-8 w-8 text-zinc-400 hover:text-zinc-100 hover:bg-zinc-800/60"
                          title="More Actions"
                        >
                          <MoreVertical className="h-4 w-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="right" className="w-52">
                        <DropdownMenuLabel>Operations</DropdownMenuLabel>
                        <DropdownMenuItem
                          disabled={!host.agent.installed}
                          onClick={() => handleTriggerUpdate(host)}
                          className="text-emerald-400 hover:text-emerald-300"
                        >
                          <Sparkles className="h-3.5 w-3.5" />
                          <span>Run DAG Update</span>
                          {host.agent.upgradablePackagesCount > 0 && (
                            <span className="ml-auto text-[10px] font-mono bg-emerald-950 text-emerald-300 px-1.5 py-0.5 rounded">
                              {host.agent.upgradablePackagesCount}
                            </span>
                          )}
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          disabled={!host.agent.installed}
                          onClick={() => setRebootModalHost(host)}
                          className="text-amber-400 hover:text-amber-300"
                        >
                          <RotateCcw className="h-3.5 w-3.5" />
                          <span>Reboot Node</span>
                          {host.agent.pendingReboot && (
                            <span className="ml-auto text-[10px] font-mono bg-amber-950 text-amber-300 px-1.5 py-0.5 rounded">
                              Pending
                            </span>
                          )}
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          onClick={() => {
                            setTerminalJobId(null)
                            setAutoTriggerUpdate(false)
                            setTerminalHost(host)
                          }}
                        >
                          <Terminal className="h-3.5 w-3.5" />
                          <span>Terminal Console</span>
                        </DropdownMenuItem>

                        <DropdownMenuSeparator />

                        <DropdownMenuLabel>Configuration</DropdownMenuLabel>
                        <DropdownMenuItem onClick={() => setInspectHost(host)}>
                          <Eye className="h-3.5 w-3.5" />
                          <span>View Details</span>
                        </DropdownMenuItem>
                        <DropdownMenuItem onClick={() => setHostToEdit(host)}>
                          <Pencil className="h-3.5 w-3.5" />
                          <span>Edit Host</span>
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          onClick={() => {
                            setAdoptHost(host)
                            setIsAdoptModalOpen(true)
                          }}
                        >
                          <Shield className="h-3.5 w-3.5" />
                          <span>Adopt via SSH</span>
                        </DropdownMenuItem>
                        {(host.targetType === 'proxmox_vm' || host.targetType === 'proxmox_lxc' || host.proxmox) && (
                          <DropdownMenuItem
                            onClick={() => {
                              setSnapshotModalHost(host)
                              setIsSnapshotModalOpen(true)
                            }}
                          >
                            <Camera className="h-3.5 w-3.5 text-purple-400" />
                            <span>Proxmox Snapshots</span>
                          </DropdownMenuItem>
                        )}

                        <DropdownMenuSeparator />

                        <DropdownMenuLabel>Danger Zone</DropdownMenuLabel>
                        <DropdownMenuItem
                          destructive
                          onClick={() => setHostToDelete(host)}
                        >
                          <Trash2 className="h-3.5 w-3.5" />
                          <span>Delete Host</span>
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>

        {/* Pagination Controls */}
        {totalHosts > 0 && (
          <div className="flex flex-col sm:flex-row items-center justify-between gap-3 px-4 py-3 bg-zinc-900/60 border border-zinc-800/80 rounded-xl text-xs text-zinc-400 backdrop-blur-sm">
            <div className="flex items-center gap-2">
              <span>
                Showing <strong className="text-zinc-200 font-mono">{totalHosts === 0 ? 0 : startIndex + 1}</strong> to{' '}
                <strong className="text-zinc-200 font-mono">{endIndex}</strong> of{' '}
                <strong className="text-zinc-200 font-mono">{totalHosts}</strong> hosts
              </span>
              <div className="h-3 w-px bg-zinc-800 mx-1 hidden sm:block" />
              <div className="flex items-center gap-1.5">
                <span className="text-zinc-500">Rows per page:</span>
                <select
                  value={pageSize}
                  onChange={(e) => setPageSize(Number(e.target.value))}
                  aria-label="Rows per page"
                  className="bg-zinc-950 border border-zinc-800 rounded px-2 py-1 text-zinc-300 text-xs focus:ring-1 focus:ring-emerald-500/30 cursor-pointer"
                >
                  <option value={5}>5</option>
                  <option value={10}>10</option>
                  <option value={25}>25</option>
                  <option value={50}>50</option>
                </select>
              </div>
            </div>

            <div className="flex items-center gap-1.5">
              <Button
                variant="outline"
                size="sm"
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={page <= 1}
                className="h-8 px-2.5 text-xs text-zinc-300 disabled:opacity-40 gap-1"
              >
                <ChevronLeft className="h-3.5 w-3.5" />
                Previous
              </Button>
              <span className="px-2 font-mono text-zinc-300">
                Page {page} of {totalPages}
              </span>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                disabled={page >= totalPages}
                className="h-8 px-2.5 text-xs text-zinc-300 disabled:opacity-40 gap-1"
              >
                Next
                <ChevronRight className="h-3.5 w-3.5" />
              </Button>
            </div>
          </div>
        )}
        </div>
      )}

      {/* Host Details Dialog */}
      <HostDetailsModal
        host={inspectHost}
        open={Boolean(inspectHost)}
        onClose={() => setInspectHost(null)}
        onEdit={(h) => setHostToEdit(h)}
        onAdopt={(h) => {
          setAdoptHost(h)
          setIsAdoptModalOpen(true)
        }}
        onTriggerUpdate={(h) => handleTriggerUpdate(h)}
        onReboot={(h) => setRebootModalHost(h)}
      />

      {/* Reboot Host Modal */}
      <RebootHostModal
        host={rebootModalHost}
        bulkHosts={rebootBulkHosts}
        open={Boolean(rebootModalHost || rebootBulkHosts)}
        onClose={() => {
          setRebootModalHost(null)
          setRebootBulkHosts(null)
        }}
        onRebootSuccess={(jobId, h) => {
          setTerminalJobId(jobId)
          setAutoTriggerUpdate(false)
          setTerminalHost(h)
        }}
      />

      {/* Edit Host Dialog */}
      <EditHostModal
        host={hostToEdit}
        open={Boolean(hostToEdit)}
        onClose={() => setHostToEdit(null)}
      />

      {/* Mass Agent Update Modal */}
      <MassAgentUpdateModal
        open={isMassUpdateModalOpen}
        onClose={() => setIsMassUpdateModalOpen(false)}
        onSuccess={() => refetch()}
      />

      {/* Modular Pipeline Launch Modal */}
      <LaunchWorkflowModal
        isOpen={Boolean(workflowModalHost)}
        onClose={() => setWorkflowModalHost(null)}
        host={workflowModalHost}
        availableHosts={hosts || []}
        onWorkflowLaunched={handleWorkflowLaunched}
      />

      {/* Proxmox Snapshots Management Modal */}
      <SnapshotManagementModal
        isOpen={isSnapshotModalOpen}
        onClose={() => {
          setIsSnapshotModalOpen(false)
          setSnapshotModalHost(null)
        }}
        selectedHost={snapshotModalHost}
      />

      {/* Host Terminal Drawer */}
      {terminalHost && (
        <HostTerminalDrawer
          host={terminalHost}
          isOpen={Boolean(terminalHost)}
          initialJobId={terminalJobId}
          autoTriggerDag={autoTriggerUpdate}
          onClose={() => {
            setTerminalHost(null)
            setTerminalJobId(null)
            setAutoTriggerUpdate(false)
          }}
        />
      )}

      {/* Adopt Node Modal */}
      {isAdoptModalOpen && (
        <AdoptNodeModal
          key={adoptHost?.id ?? 'new-adoption'}
          isOpen={isAdoptModalOpen}
          onClose={() => {
            setIsAdoptModalOpen(false)
            setAdoptHost(null)
          }}
          host={adoptHost}
        />
      )}

      {/* Delete Confirmation Dialog */}
      {hostToDelete && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/75 backdrop-blur-sm animate-in fade-in">
          <div className="w-full max-w-sm bg-zinc-900 border border-zinc-800 rounded-xl p-6 shadow-2xl space-y-4">
            <h3 className="text-base font-semibold text-zinc-100">Delete Host</h3>
            <p className="text-xs text-zinc-400">
              Are you sure you want to remove <strong className="text-zinc-200">{hostToDelete.hostname}</strong> from inventory? This action cannot be undone.
            </p>
            <div className="flex items-center justify-end gap-2 pt-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => setHostToDelete(null)}
              >
                Cancel
              </Button>
              <Button
                variant="destructive"
                size="sm"
                onClick={handleDeleteConfirm}
                isLoading={deleteMutation.isPending}
              >
                Delete Host
              </Button>
            </div>
          </div>
        </div>
      )}

      {/* Floating Bulk Actions Bar */}
      {selectedHostIds.size > 0 && (
        <div className="fixed bottom-6 left-1/2 -translate-x-1/2 z-40 flex items-center gap-3 px-5 py-3 rounded-2xl bg-zinc-950/95 border border-zinc-700/80 shadow-2xl backdrop-blur-xl animate-in fade-in slide-in-from-bottom-4">
          <div className="flex items-center gap-2 pr-3 border-r border-zinc-800">
            <div className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
            <span className="text-xs font-semibold text-zinc-200">
              {selectedHostIds.size} {selectedHostIds.size === 1 ? 'host' : 'hosts'} selected
            </span>
          </div>

          <Button
            size="sm"
            variant="primary"
            className="gap-1.5 bg-emerald-600 hover:bg-emerald-500 text-white text-xs h-8"
            onClick={handleBulkUpdate}
          >
            <Sparkles className="h-3.5 w-3.5" />
            Update Selected
          </Button>

          <Button
            size="sm"
            variant="outline"
            className="gap-1.5 border-amber-600/50 text-amber-300 hover:bg-amber-950/50 text-xs h-8"
            onClick={handleBulkReboot}
          >
            <RotateCcw className="h-3.5 w-3.5" />
            Reboot Selected
          </Button>

          <Button
            size="sm"
            variant="ghost"
            className="text-xs text-zinc-400 hover:text-zinc-200 h-8"
            onClick={() => setSelectedHostIds(new Set())}
          >
            Deselect
          </Button>
        </div>
      )}
    </div>
  )
}
