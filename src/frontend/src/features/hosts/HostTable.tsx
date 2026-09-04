import { useState } from 'react'
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
import { useDeleteHost, useHosts } from './useHosts'
import {
  Search,
  Plus,
  RefreshCw,
  Trash2,
  Eye,
  Copy,
  Check,
  Server,
  AlertTriangle,
  ArrowUpCircle,
  HardDrive,
} from 'lucide-react'
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
  const [hostToDelete, setHostToDelete] = useState<Host | null>(null)
  const [copiedIp, setCopiedIp] = useState<string | null>(null)

  const filters: HostFilterParams = {
    search: searchTerm || undefined,
    osFamily: selectedOs || undefined,
    targetType: selectedTarget || undefined,
    pendingReboot: onlyReboot ? true : undefined,
    hasUpdates: onlyUpdates ? true : undefined,
  }

  const { data: hosts, isLoading, isError, error, refetch, isFetching } = useHosts(filters)
  const deleteMutation = useDeleteHost()

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
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Host & Platform</TableHead>
              <TableHead>IP & Network</TableHead>
              <TableHead>OS Family</TableHead>
              <TableHead>Agent Status</TableHead>
              <TableHead>Vitals & Flags</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {hosts.map((host) => (
              <TableRow
                key={host.id}
                className="cursor-pointer"
                onClick={() => setInspectHost(host)}
              >
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
                  <AgentStatusBadge agent={host.agent} />
                </TableCell>

                {/* Vitals */}
                <TableCell>
                  <div className="flex flex-wrap items-center gap-2">
                    <RebootBadge pending={host.agent.pendingReboot} />
                    <UpdatesBadge count={host.agent.upgradablePackagesCount} />
                    {!host.agent.pendingReboot && host.agent.upgradablePackagesCount === 0 && (
                      <span className="text-xs text-zinc-500">Clean</span>
                    )}
                  </div>
                </TableCell>

                {/* Actions */}
                <TableCell className="text-right" onClick={(e) => e.stopPropagation()}>
                  <div className="flex items-center justify-end gap-1.5">
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8 text-zinc-400 hover:text-zinc-100"
                      onClick={() => setInspectHost(host)}
                      title="View Details"
                    >
                      <Eye className="h-4 w-4" />
                    </Button>

                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8 text-zinc-400 hover:text-rose-400"
                      onClick={() => setHostToDelete(host)}
                      title="Delete Host"
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      {/* Host Details Dialog */}
      <HostDetailsModal
        host={inspectHost}
        open={Boolean(inspectHost)}
        onClose={() => setInspectHost(null)}
      />

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
    </div>
  )
}
