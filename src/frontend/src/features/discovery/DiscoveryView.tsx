import React, { useState, useMemo } from 'react'
import {
  Compass,
  RefreshCw,
  CheckCircle2,
  Server,
  Cpu,
  Plus,
  ArrowRight,
  Layers,
  Box,
  Users,
  X,
  GitFork,
  Wifi,
  Shield,
} from 'lucide-react'
import { useDiscoveryScan } from './useDiscovery'
import { useSyncHostCorrelations } from '../hosts/useHosts'
import { ImportCandidateModal } from './ImportCandidateModal'
import { MassAdoptModal } from './MassAdoptModal'
import { Button } from '../../components/ui/button'
import { MetricStrip } from '../../components/ui/metric-strip'
import { useIsMobile } from '../../hooks/useMediaQuery'
import {
  TableToolbar,
  TableToolbarSearch,
  TableToolbarGroup,
} from '../../components/ui/table-toolbar'
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
  const isMobile = useIsMobile()
  const [searchTerm, setSearchTerm] = useState('')
  const [sourceFilter, setSourceFilter] = useState<'all' | 'Proxmox' | 'Kubernetes' | 'UniFi' | 'OPNsense'>('all')
  const [managementFilter, setManagementFilter] = useState<'all' | 'unmanaged' | 'managed'>('all')
  const [selectedCandidate, setSelectedCandidate] = useState<DiscoveredCandidate | null>(null)
  const [isImportModalOpen, setIsImportModalOpen] = useState(false)
  const [selectedCandidateKeys, setSelectedCandidateKeys] = useState<Set<string>>(new Set())
  const [isMassAdoptModalOpen, setIsMassAdoptModalOpen] = useState(false)
  const [successNotice, setSuccessNotice] = useState<{ message: string; hostId?: string } | null>(null)

  const { data: scanData, isLoading, isFetching, refetch } = useDiscoveryScan()
  const syncMutation = useSyncHostCorrelations()

  const handleSync = async () => {
    try {
      const res = await syncMutation.mutateAsync()
      setSuccessNotice({
        message: `Correlations saved to database: ${res.correlatedKubernetesNodes} Kubernetes node(s) and ${res.correlatedProxmoxHosts} Proxmox host(s) synchronized.`,
      })
      refetch()
    } catch {
      setSuccessNotice({
        message: 'Failed to synchronize host correlations across infrastructure adapters.',
      })
    }
  }

  const candidates = scanData?.candidates ?? []

  const activeSources = useMemo(() => {
    const set = new Set(candidates.map((c) => c.source))
    return Array.from(set)
  }, [candidates])

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

  const unmanagedInView = useMemo(
    () => filteredCandidates.filter((c) => !c.isManaged),
    [filteredCandidates]
  )

  const isAllSelected =
    unmanagedInView.length > 0 &&
    unmanagedInView.every((c) => selectedCandidateKeys.has(c.id || c.name))

  const toggleSelectAll = () => {
    if (isAllSelected) {
      setSelectedCandidateKeys(new Set())
    } else {
      const next = new Set<string>()
      unmanagedInView.forEach((c) => next.add(c.id || c.name))
      setSelectedCandidateKeys(next)
    }
  }

  const toggleSelectCandidate = (key: string) => {
    setSelectedCandidateKeys((prev) => {
      const next = new Set(prev)
      if (next.has(key)) {
        next.delete(key)
      } else {
        next.add(key)
      }
      return next
    })
  }

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
              Automatically scan hypervisors (Proxmox VE), cluster orchestrators (Kubernetes), and network appliances (UniFi, OPNsense) to discover and adopt unmanaged hosts.
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={handleSync}
            disabled={syncMutation.isPending}
            className="gap-2 border-sky-800/80 bg-sky-950/40 text-sky-300 hover:bg-sky-900/60 font-medium"
            title="Correlate existing inventory hosts with active Proxmox hypervisors and Kubernetes clusters and save to database"
          >
            <GitFork className={`h-4 w-4 ${syncMutation.isPending ? 'animate-spin' : ''}`} />
            {syncMutation.isPending ? 'Syncing...' : 'Sync Correlations'}
          </Button>

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

      {/* Sleek Top Metric Strip */}
      <MetricStrip
        items={[
          {
            id: 'discovered',
            label: 'Discovered Hosts',
            value: scanData?.totalDiscovered ?? 0,
            icon: Layers,
            iconColor: 'text-zinc-400',
            subtext: 'Across all infrastructure adapters',
          },
          {
            id: 'unmanaged',
            label: 'Unmanaged Targets',
            value: scanData?.unmanagedCount ?? 0,
            icon: Compass,
            iconColor: 'text-sky-400',
            badge: (scanData?.unmanagedCount ?? 0) > 0 ? `${scanData?.unmanagedCount} New` : undefined,
            badgeVariant: 'warning',
            subtext: 'Available for 1-click adoption',
          },
          {
            id: 'managed',
            label: 'Already Managed',
            value: scanData?.alreadyManaged ?? 0,
            icon: CheckCircle2,
            iconColor: 'text-emerald-400',
            badge: 'In Fleet',
            badgeVariant: 'success',
            subtext: 'Bound in host inventory',
          },
          {
            id: 'active-adapters',
            label: 'Active Adapters',
            value: activeSources.length > 0 ? activeSources.length : 4,
            icon: Box,
            iconColor: 'text-purple-400',
            subtext: activeSources.length > 0 ? activeSources.join(', ') : 'Proxmox, K8s, UniFi, OPNsense',
          },
        ]}
      />

      {/* Success Notification Banner */}
      {successNotice && (
        <div className="flex items-center justify-between p-3.5 bg-emerald-950/40 border border-emerald-800/60 rounded-xl text-xs text-emerald-300 animate-in fade-in">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-400" />
            <span>{successNotice.message}</span>
          </div>
          <div className="flex items-center gap-2">
            {successNotice.hostId && onSelectHost && (
              <Button
                variant="secondary"
                size="sm"
                onClick={() => onSelectHost(successNotice.hostId!)}
                className="text-xs h-7 px-2.5 text-emerald-200 border-emerald-800/60 hover:bg-emerald-900/40"
              >
                <span>View in Host Inventory</span>
                <ArrowRight className="h-3 w-3 ml-1" />
              </Button>
            )}
            <button
              onClick={() => setSuccessNotice(null)}
              className="text-emerald-400/70 hover:text-emerald-300 p-1 rounded"
              title="Dismiss"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          </div>
        </div>
      )}

      {/* Selection / Mass Adopt Bar */}
      {selectedCandidateKeys.size > 0 && (
        <div className="flex items-center justify-between p-3.5 bg-sky-950/50 border border-sky-800/60 rounded-xl text-xs text-sky-200 animate-in fade-in">
          <div className="flex items-center gap-2">
            <Users className="h-4 w-4 text-sky-400" />
            <span className="font-medium">
              {selectedCandidateKeys.size} unmanaged {selectedCandidateKeys.size === 1 ? 'host' : 'hosts'} selected
            </span>
          </div>
          <div className="flex items-center gap-2">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => setSelectedCandidateKeys(new Set())}
              className="text-xs h-7 text-zinc-400 hover:text-zinc-200"
            >
              Clear Selection
            </Button>
            <Button
              variant="primary"
              size="sm"
              onClick={() => setIsMassAdoptModalOpen(true)}
              className="text-xs h-7 gap-1.5 bg-sky-600 hover:bg-sky-500 text-white font-medium"
            >
              <Users className="h-3.5 w-3.5" />
              <span>Mass Adopt ({selectedCandidateKeys.size})</span>
            </Button>
          </div>
        </div>
      )}

      {/* Filter and Search Toolbar */}
      <TableToolbar>
        <TableToolbarGroup className="flex-1">
          <TableToolbarSearch
            placeholder="Search discovered hosts..."
            value={searchTerm}
            onChange={setSearchTerm}
          />

          <select
            value={sourceFilter}
            onChange={(e) => setSourceFilter(e.target.value as any)}
            className="h-9 bg-zinc-950/80 border border-zinc-800 rounded-lg px-3 text-xs text-zinc-300 focus:outline-none focus:border-sky-500/80 cursor-pointer"
          >
            <option value="all">All Sources</option>
            <option value="Proxmox">Proxmox VE</option>
            <option value="Kubernetes">Kubernetes</option>
            <option value="UniFi">UniFi Network</option>
            <option value="OPNsense">OPNsense Firewall</option>
          </select>

          <select
            value={managementFilter}
            onChange={(e) => setManagementFilter(e.target.value as any)}
            className="h-9 bg-zinc-950/80 border border-zinc-800 rounded-lg px-3 text-xs text-zinc-300 focus:outline-none focus:border-sky-500/80 cursor-pointer"
          >
            <option value="all">All Items</option>
            <option value="unmanaged">Unmanaged Only</option>
            <option value="managed">Already Managed</option>
          </select>
        </TableToolbarGroup>
      </TableToolbar>

      {/* Candidates List / Table */}
      {isMobile ? (
        <div className="space-y-3 md:hidden">
        {isLoading ? (
          <div className="p-8 text-center text-zinc-400 text-xs border border-zinc-800 rounded-xl bg-zinc-900/40">
            <RefreshCw className="h-5 w-5 animate-spin text-sky-400 mx-auto mb-2" />
            <span>Querying hypervisors and cluster endpoints...</span>
          </div>
        ) : filteredCandidates.length === 0 ? (
          <div className="p-8 text-center text-zinc-400 text-xs border border-zinc-800 rounded-xl bg-zinc-900/40">
            <Compass className="h-6 w-6 text-zinc-600 mx-auto mb-2" />
            <span>No matching candidates discovered.</span>
          </div>
        ) : (
          filteredCandidates.map((candidate) => {
            const isCandidateSelected = selectedCandidateKeys.has(candidate.id || candidate.name)
            return (
              <div
                key={candidate.id}
                className={`p-3.5 rounded-xl border transition-all space-y-2.5 shadow-xs ${
                  isCandidateSelected
                    ? 'bg-zinc-900 border-sky-500/50'
                    : 'bg-zinc-900/60 border-zinc-800/80 hover:border-zinc-700/80'
                }`}
              >
                <div className="flex items-center justify-between gap-2">
                  <div className="flex items-center gap-2.5 min-w-0">
                    {!candidate.isManaged ? (
                      <input
                        type="checkbox"
                        checked={isCandidateSelected}
                        onChange={() => toggleSelectCandidate(candidate.id || candidate.name)}
                        className="rounded border-zinc-700 bg-zinc-800 text-sky-500 focus:ring-sky-500 cursor-pointer h-4.5 w-4.5 shrink-0"
                        title={`Select ${candidate.name} for mass adoption`}
                      />
                    ) : (
                      <CheckCircle2 className="h-4 w-4 text-emerald-500 shrink-0" />
                    )}
                    <div className="min-w-0">
                      <span className="font-semibold text-zinc-100 text-sm truncate block">{candidate.name}</span>
                      <span className="text-[11px] text-zinc-400 font-mono">
                        {candidate.source} {candidate.proxmoxVmid ? `• VM ${candidate.proxmoxVmid}` : ''}
                      </span>
                    </div>
                  </div>
                  <span className={`text-[10px] font-mono px-2 py-0.5 rounded-full border ${
                    candidate.status === 'running' || candidate.status === 'Ready' || candidate.status === 'online' || candidate.status === 'active'
                      ? 'bg-emerald-950/40 text-emerald-400 border-emerald-800/50'
                      : 'bg-zinc-800 text-zinc-400 border-zinc-700'
                  }`}>
                    {candidate.status}
                  </span>
                </div>

                <div className="flex items-center justify-between gap-2 text-xs pt-1 border-t border-zinc-800/60 flex-wrap">
                  <span className="font-mono text-zinc-300">{candidate.ipAddress || '—'}</span>
                  <div>
                    {!candidate.isManaged ? (
                      <Button
                        variant="primary"
                        size="sm"
                        onClick={() => handleImport(candidate)}
                        className="gap-1 text-xs py-1 px-2.5 min-h-[36px]"
                      >
                        <Plus className="h-3.5 w-3.5" />
                        Import
                      </Button>
                    ) : candidate.existingHostId && onSelectHost ? (
                      <button
                        type="button"
                        onClick={() => onSelectHost(candidate.existingHostId!)}
                        className="text-xs text-sky-400 hover:text-sky-300 font-medium flex items-center gap-1 cursor-pointer min-h-[36px] px-2"
                      >
                        <span>View Host</span>
                        <ArrowRight className="h-3.5 w-3.5" />
                      </button>
                    ) : (
                      <span className="text-xs text-emerald-400 font-medium">Managed</span>
                    )}
                  </div>
                </div>
              </div>
            )
          })
        )}
      </div>
      ) : (
        /* Candidates Table (Desktop >=md) */
        <div className="hidden md:block border border-zinc-800 rounded-xl overflow-hidden bg-zinc-900/40">
        <Table>
          <TableHeader>
            <TableRow className="border-zinc-800 hover:bg-transparent">
              <TableHead className="w-10 text-center">
                <input
                  type="checkbox"
                  checked={isAllSelected}
                  onChange={toggleSelectAll}
                  disabled={unmanagedInView.length === 0}
                  className="rounded border-zinc-700 bg-zinc-800 text-sky-500 focus:ring-sky-500 cursor-pointer"
                  title="Select all unmanaged hosts"
                />
              </TableHead>
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
                <TableCell colSpan={8} className="h-32 text-center text-zinc-400 text-xs">
                  <div className="flex flex-col items-center justify-center gap-2">
                    <RefreshCw className="h-5 w-5 animate-spin text-sky-400" />
                    <span>Querying hypervisors and cluster endpoints...</span>
                  </div>
                </TableCell>
              </TableRow>
            ) : filteredCandidates.length === 0 ? (
              <TableRow>
                <TableCell colSpan={8} className="h-32 text-center text-zinc-400 text-xs">
                  <div className="flex flex-col items-center justify-center gap-2">
                    <Compass className="h-6 w-6 text-zinc-600" />
                    <span>No matching candidates discovered.</span>
                    <span className="text-[11px] text-zinc-500">
                      Check your Proxmox, Kubernetes, UniFi, and OPNsense adapter credentials in Settings.
                    </span>
                  </div>
                </TableCell>
              </TableRow>
            ) : (
              filteredCandidates.map((candidate) => (
                <TableRow key={candidate.id} className="border-zinc-800/60 hover:bg-zinc-800/30">
                  <TableCell className="w-10 text-center">
                    {!candidate.isManaged ? (
                      <input
                        type="checkbox"
                        checked={selectedCandidateKeys.has(candidate.id || candidate.name)}
                        onChange={() => toggleSelectCandidate(candidate.id || candidate.name)}
                        className="rounded border-zinc-700 bg-zinc-800 text-sky-500 focus:ring-sky-500 cursor-pointer"
                        title={`Select ${candidate.name} for mass adoption`}
                      />
                    ) : (
                      <span className="text-zinc-600" title="Already managed">
                        —
                      </span>
                    )}
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      {candidate.source === 'Proxmox' ? (
                        <span className="inline-flex items-center gap-1 text-[11px] font-medium text-purple-300 bg-purple-950/60 border border-purple-800/50 px-2 py-0.5 rounded-md">
                          <Server className="h-3 w-3" />
                          Proxmox
                        </span>
                      ) : candidate.source === 'Kubernetes' ? (
                        <span className="inline-flex items-center gap-1 text-[11px] font-medium text-sky-300 bg-sky-950/60 border border-sky-800/50 px-2 py-0.5 rounded-md">
                          <Cpu className="h-3 w-3" />
                          Kubernetes
                        </span>
                      ) : candidate.source === 'UniFi' ? (
                        <span className="inline-flex items-center gap-1 text-[11px] font-medium text-cyan-300 bg-cyan-950/60 border border-cyan-800/50 px-2 py-0.5 rounded-md">
                          <Wifi className="h-3 w-3" />
                          UniFi
                        </span>
                      ) : candidate.source === 'OPNsense' ? (
                        <span className="inline-flex items-center gap-1 text-[11px] font-medium text-amber-300 bg-amber-950/60 border border-amber-800/50 px-2 py-0.5 rounded-md">
                          <Shield className="h-3 w-3" />
                          OPNsense
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 text-[11px] font-medium text-zinc-300 bg-zinc-800 border border-zinc-700/60 px-2 py-0.5 rounded-md">
                          {candidate.source}
                        </span>
                      )}
                      <span className="text-[11px] text-zinc-400">
                        {candidate.targetType === 'proxmox_lxc'
                          ? 'LXC'
                          : candidate.targetType === 'proxmox_vm'
                          ? 'QEMU'
                          : candidate.source === 'Kubernetes'
                          ? 'Node'
                          : candidate.source === 'UniFi'
                          ? candidate.roles?.includes('network-client')
                            ? 'Client'
                            : candidate.targetType === 'switch'
                            ? 'Switch'
                            : candidate.targetType === 'access_point'
                            ? 'AP'
                            : candidate.targetType === 'gateway'
                            ? 'Gateway'
                            : 'Device'
                          : candidate.source === 'OPNsense'
                          ? candidate.roles?.includes('firewall')
                            ? 'Firewall'
                            : candidate.roles?.includes('dhcp-lease')
                            ? 'DHCP'
                            : 'Lease'
                          : candidate.targetType === 'baremetal'
                          ? 'Baremetal'
                          : candidate.targetType || 'Host'}
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
                        : candidate.k8sNodeName
                        ? candidate.k8sNodeName
                        : candidate.source === 'UniFi'
                        ? candidate.roles?.includes('network-device')
                          ? (candidate.targetType === 'switch' ? 'UniFi Switch' : candidate.targetType === 'access_point' ? 'UniFi Access Point' : candidate.targetType === 'gateway' ? 'UniFi Gateway' : 'UniFi Device')
                          : 'UniFi Client'
                        : candidate.source === 'OPNsense'
                        ? candidate.roles?.includes('firewall')
                          ? 'OPNsense Firewall'
                          : 'DHCP Lease'
                        : '—'}
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
      )}

      {/* Import Modal */}
      <ImportCandidateModal
        candidate={selectedCandidate}
        open={isImportModalOpen}
        onClose={() => {
          setIsImportModalOpen(false)
          setSelectedCandidate(null)
        }}
        onSuccess={(hostId) => {
          setSuccessNotice({
            message: `Host "${selectedCandidate?.name}" successfully imported into inventory.`,
            hostId,
          })
          setIsImportModalOpen(false)
          setSelectedCandidate(null)
          refetch()
        }}
      />

      {/* Mass Adopt Modal */}
      <MassAdoptModal
        candidates={candidates.filter((c) => selectedCandidateKeys.has(c.id || c.name))}
        open={isMassAdoptModalOpen}
        onClose={() => {
          setIsMassAdoptModalOpen(false)
          setSelectedCandidateKeys(new Set())
        }}
        onSuccess={(succeededCount) => {
          setSuccessNotice({
            message: `Batch adoption complete: successfully imported ${succeededCount} hosts into inventory.`,
          })
          refetch()
        }}
      />
    </div>
  )
}
