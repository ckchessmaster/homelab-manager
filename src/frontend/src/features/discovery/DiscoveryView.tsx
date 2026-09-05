import React, { useState, useMemo } from 'react'
import {
  Compass,
  RefreshCw,
  Search,
  CheckCircle2,
  AlertCircle,
  Server,
  Cpu,
  Plus,
  ArrowRight,
  Layers,
  Box,
} from 'lucide-react'
import { useDiscoveryScan } from './useDiscovery'
import { ImportCandidateModal } from './ImportCandidateModal'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '../../components/ui/table'
import type { DiscoveredCandidate } from '../../api/discovery'

interface DiscoveryViewProps {
  onSelectHost?: (hostId: string) => void
}

export const DiscoveryView: React.FC<DiscoveryViewProps> = ({ onSelectHost }) => {
  const [searchTerm, setSearchTerm] = useState('')
  const [sourceFilter, setSourceFilter] = useState<'all' | 'Proxmox' | 'Kubernetes'>('all')
  const [managementFilter, setManagementFilter] = useState<'all' | 'unmanaged' | 'managed'>('all')
  const [selectedCandidate, setSelectedCandidate] = useState<DiscoveredCandidate | null>(null)
  const [isImportModalOpen, setIsImportModalOpen] = useState(false)

  const { data: scanData, isLoading, isFetching, refetch } = useDiscoveryScan()

  const candidates = scanData?.candidates ?? []

  const filteredCandidates = useMemo(() => {
    return candidates.filter((c) => {
      const matchesSearch =
        !searchTerm ||
        c.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
        (c.ipAddress && c.ipAddress.toLowerCase().includes(searchTerm.toLowerCase())) ||
        (c.proxmoxNode && c.proxmoxNode.toLowerCase().includes(searchTerm.toLowerCase())) ||
        (c.k8sNodeName && c.k8sNodeName.toLowerCase().includes(searchTerm.toLowerCase()))

      const matchesSource = sourceFilter === 'all' || c.source === sourceFilter

      const matchesManagement =
        managementFilter === 'all' ||
        (managementFilter === 'unmanaged' && !c.isManaged) ||
        (managementFilter === 'managed' && c.isManaged)

      return matchesSearch && matchesSource && matchesManagement
    })
  }, [candidates, searchTerm, sourceFilter, managementFilter])

  const handleImport = (candidate: DiscoveredCandidate) => {
    setSelectedCandidate(candidate)
    setIsImportModalOpen(true)
  }

  return (
    <div className="space-y-6 w-full max-w-[1700px] mx-auto">
      {/* Header Banner */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 p-6 bg-zinc-900/60 border border-zinc-800 rounded-xl backdrop-blur-md">
        <div className="flex items-center gap-3">
          <div className="p-3 bg-sky-950/60 border border-sky-800/50 rounded-lg text-sky-400">
            <Compass className="h-6 w-6" />
          </div>
          <div>
            <h2 className="text-lg font-semibold text-zinc-100">Service Discovery & Adoption Hub</h2>
            <p className="text-xs text-zinc-400 mt-0.5">
              Automatically scan hypervisors (Proxmox VE) and cluster orchestrators (Kubernetes) to discover and adopt unmanaged hosts.
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant="primary"
            size="sm"
            onClick={() => refetch()}
            disabled={isFetching}
            className="gap-2 bg-sky-600 hover:bg-sky-500 text-white font-medium"
          >
            <RefreshCw className={`h-4 w-4 ${isFetching ? 'animate-spin' : ''}`} />
            {isFetching ? 'Scanning Fleet...' : 'Scan Infrastructure'}
          </Button>
        </div>
      </div>

      {/* Metric Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Discovered Targets</span>
            <Layers className="h-4 w-4 text-sky-400" />
          </div>
          <div className="mt-2 text-2xl font-bold text-zinc-100">
            {scanData?.totalDiscovered ?? 0}
          </div>
          <p className="text-[11px] text-zinc-500 mt-0.5">VMs, containers & nodes found</p>
        </div>

        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">New / Unmanaged</span>
            <span className="h-2 w-2 rounded-full bg-emerald-400 animate-pulse" />
          </div>
          <div className="mt-2 text-2xl font-bold text-emerald-400">
            {scanData?.unmanagedCount ?? 0}
          </div>
          <p className="text-[11px] text-zinc-500 mt-0.5">Available for 1-click import</p>
        </div>

        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Already In Inventory</span>
            <CheckCircle2 className="h-4 w-4 text-zinc-400" />
          </div>
          <div className="mt-2 text-2xl font-bold text-zinc-100">
            {scanData?.alreadyManaged ?? 0}
          </div>
          <p className="text-[11px] text-zinc-500 mt-0.5">Correlated & managed</p>
        </div>

        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Connector Status</span>
            <Box className="h-4 w-4 text-purple-400" />
          </div>
          <div className="mt-2 text-sm font-semibold text-zinc-200">
            {scanData?.errors && scanData.errors.length > 0 ? (
              <span className="text-amber-400">{scanData.errors.length} Warning(s)</span>
            ) : (
              <span className="text-emerald-400">Connected</span>
            )}
          </div>
          <p className="text-[11px] text-zinc-500 mt-0.5">Proxmox & K8s APIs</p>
        </div>
      </div>

      {/* Warnings / Errors Banner if any */}
      {scanData?.errors && scanData.errors.length > 0 && (
        <div className="p-3 bg-amber-950/40 border border-amber-800/60 rounded-xl text-xs text-amber-300 space-y-1">
          <div className="font-semibold flex items-center gap-1.5">
            <AlertCircle className="h-4 w-4 shrink-0 text-amber-400" />
            <span>Discovery Notes:</span>
          </div>
          {scanData.errors.map((err, idx) => (
            <div key={idx} className="pl-5 text-amber-300/80">• {err}</div>
          ))}
        </div>
      )}

      {/* Filters and Search Bar */}
      <div className="p-4 bg-zinc-900/60 border border-zinc-800 rounded-xl space-y-3">
        <div className="flex flex-col sm:flex-row items-center gap-3">
          <div className="relative flex-1 w-full">
            <Search className="absolute left-3 top-2.5 h-4 w-4 text-zinc-500" />
            <Input
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              placeholder="Filter by name, IP, node, or role..."
              className="pl-9 h-9 text-xs"
            />
          </div>

          <div className="flex items-center gap-2 w-full sm:w-auto">
            <select
              value={sourceFilter}
              onChange={(e) => setSourceFilter(e.target.value as any)}
              className="h-9 bg-zinc-900 border border-zinc-800 rounded-md px-3 text-xs text-zinc-300 focus:outline-none focus:border-zinc-700"
            >
              <option value="all">All Sources</option>
              <option value="Proxmox">Proxmox VE</option>
              <option value="Kubernetes">Kubernetes</option>
            </select>

            <select
              value={managementFilter}
              onChange={(e) => setManagementFilter(e.target.value as any)}
              className="h-9 bg-zinc-900 border border-zinc-800 rounded-md px-3 text-xs text-zinc-300 focus:outline-none focus:border-zinc-700"
            >
              <option value="all">All Items</option>
              <option value="unmanaged">Unmanaged Only</option>
              <option value="managed">Already Managed</option>
            </select>
          </div>
        </div>
      </div>

      {/* Candidates Table */}
      <div className="border border-zinc-800 rounded-xl overflow-hidden bg-zinc-900/40">
        <Table>
          <TableHeader>
            <TableRow className="border-zinc-800 hover:bg-transparent">
              <TableHead className="text-zinc-400 text-xs font-semibold">Source & Type</TableHead>
              <TableHead className="text-zinc-400 text-xs font-semibold">Name & Hypervisor ID</TableHead>
              <TableHead className="text-zinc-400 text-xs font-semibold">IP Address</TableHead>
              <TableHead className="text-zinc-400 text-xs font-semibold">Roles / Tags</TableHead>
              <TableHead className="text-zinc-400 text-xs font-semibold">Status</TableHead>
              <TableHead className="text-zinc-400 text-xs font-semibold">Management</TableHead>
              <TableHead className="text-zinc-400 text-xs font-semibold text-right">Action</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading ? (
              <TableRow>
                <TableCell colSpan={7} className="h-32 text-center text-zinc-400 text-xs">
                  <div className="flex flex-col items-center justify-center gap-2">
                    <RefreshCw className="h-5 w-5 animate-spin text-sky-400" />
                    <span>Querying hypervisors and cluster endpoints...</span>
                  </div>
                </TableCell>
              </TableRow>
            ) : filteredCandidates.length === 0 ? (
              <TableRow>
                <TableCell colSpan={7} className="h-32 text-center text-zinc-400 text-xs">
                  <div className="flex flex-col items-center justify-center gap-2">
                    <Compass className="h-6 w-6 text-zinc-600" />
                    <span>No matching candidates discovered.</span>
                    <span className="text-[11px] text-zinc-500">
                      Check your Proxmox and Kubernetes adapter credentials in Settings.
                    </span>
                  </div>
                </TableCell>
              </TableRow>
            ) : (
              filteredCandidates.map((candidate) => (
                <TableRow key={candidate.id} className="border-zinc-800/60 hover:bg-zinc-800/30">
                  <TableCell>
                    <div className="flex items-center gap-2">
                      {candidate.source === 'Proxmox' ? (
                        <span className="inline-flex items-center gap-1 text-[11px] font-medium text-purple-300 bg-purple-950/60 border border-purple-800/50 px-2 py-0.5 rounded-md">
                          <Server className="h-3 w-3" />
                          Proxmox
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 text-[11px] font-medium text-sky-300 bg-sky-950/60 border border-sky-800/50 px-2 py-0.5 rounded-md">
                          <Cpu className="h-3 w-3" />
                          Kubernetes
                        </span>
                      )}
                      <span className="text-[11px] text-zinc-400">
                        {candidate.targetType === 'proxmox_lxc' ? 'LXC' : candidate.targetType === 'proxmox_vm' ? 'QEMU' : 'Node'}
                      </span>
                    </div>
                  </TableCell>

                  <TableCell>
                    <div className="font-medium text-zinc-200 text-xs">
                      {candidate.name}
                    </div>
                    <div className="text-[11px] font-mono text-zinc-500">
                      {candidate.proxmoxNode && candidate.proxmoxVmid
                        ? `${candidate.proxmoxNode} : #${candidate.proxmoxVmid}`
                        : candidate.k8sNodeName || '—'}
                    </div>
                  </TableCell>

                  <TableCell>
                    {candidate.ipAddress ? (
                      <span className="font-mono text-xs text-zinc-300">{candidate.ipAddress}</span>
                    ) : (
                      <span className="text-[11px] text-amber-400/90 italic">Guest IP not reported</span>
                    )}
                  </TableCell>

                  <TableCell>
                    <div className="flex flex-wrap gap-1">
                      {candidate.roles && candidate.roles.length > 0 ? (
                        candidate.roles.map((r, i) => (
                          <span key={i} className="text-[10px] bg-zinc-800 text-zinc-400 px-1.5 py-0.5 rounded border border-zinc-700/50">
                            {r}
                          </span>
                        ))
                      ) : (
                        <span className="text-zinc-600 text-xs">—</span>
                      )}
                    </div>
                  </TableCell>

                  <TableCell>
                    <span
                      className={`inline-flex items-center gap-1 text-[11px] font-medium px-2 py-0.5 rounded-full ${
                        candidate.status === 'running' || candidate.status === 'Ready'
                          ? 'bg-emerald-950/60 text-emerald-300 border border-emerald-800/50'
                          : 'bg-zinc-800 text-zinc-400'
                      }`}
                    >
                      <span className={`h-1.5 w-1.5 rounded-full ${
                        candidate.status === 'running' || candidate.status === 'Ready' ? 'bg-emerald-400' : 'bg-zinc-500'
                      }`} />
                      {candidate.status}
                    </span>
                  </TableCell>

                  <TableCell>
                    {candidate.isManaged ? (
                      <span className="inline-flex items-center gap-1 text-[11px] text-emerald-400 bg-emerald-950/40 border border-emerald-800/40 px-2 py-0.5 rounded-md">
                        <CheckCircle2 className="h-3 w-3" />
                        Managed
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1 text-[11px] text-sky-400 bg-sky-950/40 border border-sky-800/40 px-2 py-0.5 rounded-md">
                        <Compass className="h-3 w-3" />
                        New Target
                      </span>
                    )}
                  </TableCell>

                  <TableCell className="text-right">
                    {candidate.isManaged ? (
                      <Button
                        variant="secondary"
                        size="sm"
                        className="gap-1 text-zinc-300 hover:text-zinc-100 text-xs py-1 px-2"
                        onClick={() => candidate.existingHostId && onSelectHost?.(candidate.existingHostId)}
                      >
                        <span>View</span>
                        <ArrowRight className="h-3 w-3" />
                      </Button>
                    ) : (
                      <Button
                        variant="primary"
                        size="sm"
                        onClick={() => handleImport(candidate)}
                        className="gap-1 bg-emerald-600 hover:bg-emerald-500 text-white font-medium text-xs py-1 px-2"
                      >
                        <Plus className="h-3 w-3" />
                        <span>Import</span>
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      {/* Import Modal */}
      <ImportCandidateModal
        candidate={selectedCandidate}
        open={isImportModalOpen}
        onClose={() => {
          setIsImportModalOpen(false)
          setSelectedCandidate(null)
        }}
        onSuccess={(hostId) => {
          onSelectHost?.(hostId)
        }}
      />
    </div>
  )
}
