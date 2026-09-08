import { useState } from 'react'
import {
  Shield,
  Plus,
  Radio,
  Trash2,
  Edit2,
  RefreshCw,
  Server,
  RotateCw,
  Search,
  ExternalLink,
  Copy,
  Check,
  UserPlus,
  Network,
  Activity,
  Zap,
} from 'lucide-react'
import { Button } from '../../../components/ui/button'
import { Badge } from '../../../components/ui/badge'
import { Input } from '../../../components/ui/input'
import {
  useOPNsenseInstances,
  useDeleteOPNsenseInstance,
  useOPNsenseTelemetry,
  useRestartOPNsenseService,
  useOPNsenseDhcpLeases,
} from './useOPNsense'
import { AddOPNsenseModal } from './AddOPNsenseModal'
import { AdoptNodeModal } from '../../hosts/AdoptNodeModal'
import type { OPNsenseInstanceDto, OPNsenseService, OPNsenseDhcpLease } from '../../../api/opnsense'
import type { Host } from '../../../api/hosts'

export function OPNsenseAdaptersView() {
  const { data: instances, isLoading, refetch } = useOPNsenseInstances()
  const deleteMutation = useDeleteOPNsenseInstance()

  const [selectedInstanceId, setSelectedInstanceId] = useState<string | null>(null)
  const [modalOpen, setModalOpen] = useState(false)
  const [editingInstance, setEditingInstance] = useState<OPNsenseInstanceDto | null>(null)

  // Active firewall selection
  const activeInstanceId = selectedInstanceId || (instances && instances.length > 0 ? instances[0].id : null)
  const activeInstance = instances?.find((i) => i.id === activeInstanceId)

  // Sub-view tabs
  const [activeSubTab, setActiveSubTab] = useState<'telemetry' | 'services' | 'dhcp'>('telemetry')
  const [serviceSearch, setServiceSearch] = useState('')
  const [dhcpSearch, setDhcpSearch] = useState('')

  // Telemetry & DHCP Queries
  const { data: telemetry, refetch: refetchTelemetry } = useOPNsenseTelemetry(activeInstanceId)
  const { data: leases, isLoading: isLeasesLoading, refetch: refetchLeases } = useOPNsenseDhcpLeases(activeInstanceId)
  const restartServiceMutation = useRestartOPNsenseService(activeInstanceId || '')

  // Quick Adopt Modal State
  const [adoptTarget, setAdoptTarget] = useState<Host | null>(null)
  const [adoptModalOpen, setAdoptModalOpen] = useState(false)

  // Copy helper
  const [copiedMac, setCopiedMac] = useState<string | null>(null)
  const handleCopy = (text: string) => {
    navigator.clipboard.writeText(text)
    setCopiedMac(text)
    setTimeout(() => setCopiedMac(null), 2000)
  }

  const handleDelete = async (id: string, name: string) => {
    if (confirm(`Are you sure you want to remove OPNsense firewall '${name}'?`)) {
      await deleteMutation.mutateAsync(id)
      if (selectedInstanceId === id) {
        setSelectedInstanceId(null)
      }
    }
  }

  const handleRestartService = async (service: OPNsenseService) => {
    if (confirm(`Restart service '${service.name}' (${service.description}) on ${activeInstance?.name}?`)) {
      await restartServiceMutation.mutateAsync(service.name)
    }
  }

  const handleAdoptLease = (lease: OPNsenseDhcpLease) => {
    const pseudoHost: Host = {
      id: `lease-${lease.ip.replace(/\./g, '-')}`,
      hostname: lease.hostname || `dhcp-${lease.ip.replace(/\./g, '-')}`,
      friendlyName: lease.hostname || `DHCP Host (${lease.ip})`,
      ipAddress: lease.ip,
      osFamily: 'linux_debian',
      targetType: 'baremetal',
      agent: {
        installed: false,
        version: null,
        lastSeenAt: null,
        pendingReboot: false,
        upgradablePackagesCount: 0,
      },
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }
    setAdoptTarget(pseudoHost)
    setAdoptModalOpen(true)
  }

  // Filtered lists
  const filteredServices = (telemetry?.services || []).filter((s) => {
    const q = serviceSearch.toLowerCase()
    return s.name.toLowerCase().includes(q) || s.description.toLowerCase().includes(q)
  })

  const filteredLeases = (leases || []).filter((l) => {
    const q = dhcpSearch.toLowerCase()
    return (
      l.ip.toLowerCase().includes(q) ||
      l.mac.toLowerCase().includes(q) ||
      (l.hostname && l.hostname.toLowerCase().includes(q))
    )
  })

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12 text-zinc-500 text-sm">
        <RefreshCw className="h-5 w-5 animate-spin mr-2 text-orange-400" />
        Loading OPNsense firewalls...
      </div>
    )
  }

  if (!instances || instances.length === 0) {
    return (
      <div className="text-center p-12 bg-zinc-900/40 border border-zinc-800 rounded-xl max-w-xl mx-auto space-y-4 animate-in fade-in">
        <div className="p-3 bg-orange-500/10 border border-orange-500/20 rounded-full w-12 h-12 flex items-center justify-center mx-auto text-orange-400">
          <Shield className="h-6 w-6" />
        </div>
        <div>
          <h3 className="text-base font-semibold text-zinc-100">No OPNsense Firewalls Connected</h3>
          <p className="text-xs text-zinc-400 mt-1.5 max-w-md mx-auto leading-relaxed">
            Connect your OPNsense firewall to monitor gateway health, public WAN status, service states, and discover DHCP leases for instant node adoption.
          </p>
        </div>
        <Button
          variant="primary"
          size="sm"
          onClick={() => {
            setEditingInstance(null)
            setModalOpen(true)
          }}
          className="gap-2 bg-orange-600 hover:bg-orange-500 text-white font-semibold shadow-xs"
        >
          <Plus className="h-4 w-4" />
          Connect OPNsense Firewall
        </Button>
        {modalOpen && (
          <AddOPNsenseModal
            open={modalOpen}
            onClose={() => setModalOpen(false)}
            initialInstance={editingInstance}
          />
        )}
      </div>
    )
  }

  return (
    <div className="space-y-6">
      {/* Top Bar: Firewall Selector & Actions */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 bg-zinc-900/60 p-4 border border-zinc-800/80 rounded-xl">
        <div className="flex items-center gap-3 overflow-x-auto pb-1 sm:pb-0">
          <div className="flex items-center gap-2 pr-2 border-r border-zinc-800">
            <Shield className="h-5 w-5 text-orange-400 shrink-0" />
            <span className="text-xs font-semibold text-zinc-200">Firewalls</span>
          </div>

          <div className="flex items-center gap-1.5">
            {instances.map((inst) => {
              const isSelected = inst.id === activeInstanceId
              return (
                <button
                  key={inst.id}
                  onClick={() => setSelectedInstanceId(inst.id)}
                  className={`flex items-center gap-2 px-3 py-1.5 rounded-lg text-xs font-medium transition-all ${
                    isSelected
                      ? 'bg-orange-600/20 text-orange-300 border border-orange-500/40 shadow-xs'
                      : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60 border border-transparent'
                  }`}
                >
                  <Server className={`h-3.5 w-3.5 ${isSelected ? 'text-orange-400' : 'text-zinc-500'}`} />
                  <span>{inst.name}</span>
                </button>
              )
            })}
          </div>
        </div>

        <div className="flex items-center gap-2">
          {activeInstance && (
            <>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => {
                  refetch()
                  refetchTelemetry()
                  refetchLeases()
                }}
                className="text-zinc-400 hover:text-zinc-200"
                title="Refresh Telemetry"
              >
                <RefreshCw className="h-3.5 w-3.5" />
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  setEditingInstance(activeInstance)
                  setModalOpen(true)
                }}
                className="gap-1.5 text-xs"
              >
                <Edit2 className="h-3.5 w-3.5" />
                Settings
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => handleDelete(activeInstance.id, activeInstance.name)}
                className="text-red-400 hover:text-red-300 hover:bg-red-950/30 text-xs"
                title="Remove Firewall"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </Button>
            </>
          )}

          <Button
            variant="primary"
            size="sm"
            onClick={() => {
              setEditingInstance(null)
              setModalOpen(true)
            }}
            className="gap-1.5 text-xs bg-orange-600 hover:bg-orange-500 text-white font-semibold shadow-xs ml-2"
          >
            <Plus className="h-3.5 w-3.5" />
            Add Firewall
          </Button>
        </div>
      </div>

      {activeInstance && (
        <div className="space-y-6">
          {/* Firewall Telemetry Header Banner */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <div className="p-4 bg-zinc-900/50 border border-zinc-800 rounded-xl space-y-1">
              <span className="text-[11px] text-zinc-400 font-medium flex items-center gap-1.5">
                <Radio className="h-3.5 w-3.5 text-orange-400" />
                Firewall State
              </span>
              <div className="flex items-center gap-2 pt-0.5">
                <span className="text-lg font-bold text-zinc-100 font-mono">
                  {telemetry?.status || 'Online'}
                </span>
                <Badge variant="success" className="text-[10px]">
                  {telemetry?.version || 'OPNsense'}
                </Badge>
              </div>
              <p className="text-[11px] text-zinc-500 font-mono truncate">
                {telemetry?.hostname || activeInstance.baseUrl}
              </p>
            </div>

            <div className="p-4 bg-zinc-900/50 border border-zinc-800 rounded-xl space-y-1">
              <span className="text-[11px] text-zinc-400 font-medium flex items-center gap-1.5">
                <Activity className="h-3.5 w-3.5 text-emerald-400" />
                Primary Gateway
              </span>
              {telemetry?.gateways && telemetry.gateways.length > 0 ? (
                <div className="pt-0.5">
                  <div className="flex items-center gap-2">
                    <span className="text-lg font-bold text-zinc-100 font-mono">
                      {telemetry.gateways[0].name}
                    </span>
                    <Badge variant={telemetry.gateways[0].status.toLowerCase().includes('online') ? 'success' : 'warning'} className="text-[10px]">
                      {telemetry.gateways[0].status}
                    </Badge>
                  </div>
                  <div className="flex items-center gap-2 text-[11px] text-zinc-400 pt-0.5">
                    <span>Ping: <strong className="text-emerald-400 font-mono">{telemetry.gateways[0].latencyMs ? `${telemetry.gateways[0].latencyMs}ms` : 'N/A'}</strong></span>
                    <span>Loss: <strong className="text-zinc-200 font-mono">{telemetry.gateways[0].lossPercentage ?? 0}%</strong></span>
                  </div>
                </div>
              ) : (
                <p className="text-xs text-zinc-500 pt-2">No active gateways reported</p>
              )}
            </div>

            <div className="p-4 bg-zinc-900/50 border border-zinc-800 rounded-xl space-y-1">
              <span className="text-[11px] text-zinc-400 font-medium flex items-center gap-1.5">
                <Zap className="h-3.5 w-3.5 text-amber-400" />
                Active Core Services
              </span>
              <div className="flex items-center gap-2 pt-0.5">
                <span className="text-lg font-bold text-zinc-100 font-mono">
                  {telemetry?.services ? telemetry.services.filter((s) => s.running).length : 0}
                </span>
                <span className="text-xs text-zinc-500">
                  / {telemetry?.services?.length || 0} configured
                </span>
              </div>
              <p className="text-[11px] text-zinc-500">
                DNS, WireGuard, Firewall Rules & Daemons
              </p>
            </div>

            <div className="p-4 bg-zinc-900/50 border border-zinc-800 rounded-xl space-y-1">
              <span className="text-[11px] text-zinc-400 font-medium flex items-center gap-1.5">
                <Network className="h-3.5 w-3.5 text-sky-400" />
                Active DHCP Leases
              </span>
              <div className="flex items-center gap-2 pt-0.5">
                <span className="text-lg font-bold text-zinc-100 font-mono">
                  {leases?.length || 0}
                </span>
                <Badge variant="purple" className="text-[10px]">
                  Ready for Adoption
                </Badge>
              </div>
              <p className="text-[11px] text-zinc-500">
                Automated network device discovery
              </p>
            </div>
          </div>

          {/* Sub-view navigation tabs */}
          <div className="flex items-center justify-between border-b border-zinc-800 pb-2">
            <div className="flex items-center gap-2">
              <button
                onClick={() => setActiveSubTab('telemetry')}
                className={`px-3 py-1.5 text-xs font-medium rounded-lg transition-colors ${
                  activeSubTab === 'telemetry'
                    ? 'bg-zinc-800 text-zinc-100'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                Gateways & Interfaces
              </button>
              <button
                onClick={() => setActiveSubTab('services')}
                className={`px-3 py-1.5 text-xs font-medium rounded-lg transition-colors flex items-center gap-1.5 ${
                  activeSubTab === 'services'
                    ? 'bg-zinc-800 text-zinc-100'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <span>Core Services</span>
                {telemetry?.services && (
                  <Badge variant="default" className="text-[10px] py-0 px-1">
                    {telemetry.services.length}
                  </Badge>
                )}
              </button>
              <button
                onClick={() => setActiveSubTab('dhcp')}
                className={`px-3 py-1.5 text-xs font-medium rounded-lg transition-colors flex items-center gap-1.5 ${
                  activeSubTab === 'dhcp'
                    ? 'bg-zinc-800 text-zinc-100'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <span>DHCP Leases</span>
                {leases && (
                  <Badge variant="purple" className="text-[10px] py-0 px-1">
                    {leases.length}
                  </Badge>
                )}
              </button>
            </div>

            <a
              href={activeInstance.baseUrl}
              target="_blank"
              rel="noreferrer"
              className="text-xs text-orange-400 hover:text-orange-300 flex items-center gap-1"
            >
              <span>Open WebGUI</span>
              <ExternalLink className="h-3 w-3" />
            </a>
          </div>

          {/* Tab 1: Gateways & Interfaces */}
          {activeSubTab === 'telemetry' && (
            <div className="space-y-6 animate-in fade-in">
              {/* Gateways Grid */}
              <div className="space-y-3">
                <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider">
                  Routing Gateways & Latency
                </h4>
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                  {telemetry?.gateways && telemetry.gateways.length > 0 ? (
                    telemetry.gateways.map((gw) => (
                      <div
                        key={gw.name}
                        className="p-3.5 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2"
                      >
                        <div className="flex items-center justify-between">
                          <span className="font-semibold text-xs text-zinc-100 font-mono">
                            {gw.name}
                          </span>
                          <Badge
                            variant={gw.status.toLowerCase().includes('online') ? 'success' : 'warning'}
                            className="text-[10px]"
                          >
                            {gw.status}
                          </Badge>
                        </div>
                        <div className="grid grid-cols-2 gap-2 text-xs text-zinc-400 pt-1">
                          <div>
                            <span className="text-zinc-500 text-[10px] block">Interface</span>
                            <span className="font-mono text-zinc-200">{gw.interface}</span>
                          </div>
                          <div>
                            <span className="text-zinc-500 text-[10px] block">IP Address</span>
                            <span className="font-mono text-zinc-200 truncate">{gw.address || 'Auto'}</span>
                          </div>
                          <div>
                            <span className="text-zinc-500 text-[10px] block">Latency</span>
                            <span className="font-mono text-emerald-400">{gw.latencyMs ? `${gw.latencyMs} ms` : 'N/A'}</span>
                          </div>
                          <div>
                            <span className="text-zinc-500 text-[10px] block">Packet Loss</span>
                            <span className="font-mono text-zinc-200">{gw.lossPercentage ?? 0}%</span>
                          </div>
                        </div>
                      </div>
                    ))
                  ) : (
                    <p className="text-xs text-zinc-500 italic">No gateways discovered</p>
                  )}
                </div>
              </div>

              {/* Interfaces Table */}
              <div className="space-y-3">
                <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider">
                  Physical & Virtual Interfaces
                </h4>
                <div className="overflow-x-auto rounded-xl border border-zinc-800 bg-zinc-950/50">
                  <table className="w-full text-left text-xs text-zinc-300">
                    <thead className="bg-zinc-900/80 text-zinc-400 font-medium uppercase text-[10px] tracking-wider border-b border-zinc-800">
                      <tr>
                        <th className="px-4 py-2.5">Interface</th>
                        <th className="px-4 py-2.5">Device</th>
                        <th className="px-4 py-2.5">IP Address</th>
                        <th className="px-4 py-2.5">Status</th>
                        <th className="px-4 py-2.5">Media</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-zinc-800/60 font-mono">
                      {telemetry?.interfaces && telemetry.interfaces.length > 0 ? (
                        telemetry.interfaces.map((iface) => (
                          <tr key={iface.name} className="hover:bg-zinc-900/40">
                            <td className="px-4 py-2.5 font-sans font-medium text-zinc-100">
                              {iface.name.toUpperCase()}
                            </td>
                            <td className="px-4 py-2.5 text-zinc-400">{iface.device}</td>
                            <td className="px-4 py-2.5 text-zinc-200">{iface.ipAddress || 'None'}</td>
                            <td className="px-4 py-2.5">
                              <Badge
                                variant={iface.status.toLowerCase() === 'up' ? 'success' : 'default'}
                                className="text-[10px] py-0"
                              >
                                {iface.status}
                              </Badge>
                            </td>
                            <td className="px-4 py-2.5 text-zinc-500 truncate max-w-xs">{iface.media || 'Ethernet'}</td>
                          </tr>
                        ))
                      ) : (
                        <tr>
                          <td colSpan={5} className="px-4 py-6 text-center text-zinc-500 italic">
                            No interface data available.
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          )}

          {/* Tab 2: Core Services */}
          {activeSubTab === 'services' && (
            <div className="space-y-4 animate-in fade-in">
              <div className="flex items-center justify-between gap-4">
                <div className="relative flex-1 max-w-sm">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-500" />
                  <Input
                    placeholder="Search services (unbound, wireguard, suricata)..."
                    value={serviceSearch}
                    onChange={(e) => setServiceSearch(e.target.value)}
                    className="pl-8 text-xs h-8"
                  />
                </div>
                <span className="text-xs text-zinc-500">
                  {filteredServices.length} of {telemetry?.services?.length || 0} services
                </span>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                {filteredServices.length > 0 ? (
                  filteredServices.map((service) => (
                    <div
                      key={service.id || service.name}
                      className="p-3.5 bg-zinc-950/60 border border-zinc-800/80 rounded-xl flex items-center justify-between gap-3 hover:border-zinc-700/60 transition-colors"
                    >
                      <div className="min-w-0 flex-1 space-y-1">
                        <div className="flex items-center gap-2">
                          <span className="font-semibold text-xs text-zinc-100 truncate font-mono">
                            {service.name}
                          </span>
                          <Badge
                            variant={service.running ? 'success' : 'default'}
                            className="text-[10px] py-0"
                          >
                            {service.running ? 'Running' : 'Stopped'}
                          </Badge>
                        </div>
                        <p className="text-[11px] text-zinc-400 truncate">
                          {service.description || 'OPNsense System Daemon'}
                        </p>
                      </div>

                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => handleRestartService(service)}
                        disabled={restartServiceMutation.isPending}
                        className="h-8 px-2.5 text-xs text-zinc-300 hover:text-white hover:bg-zinc-800 shrink-0 gap-1"
                        title="Restart Service"
                      >
                        <RotateCw className="h-3 w-3 text-orange-400" />
                        <span>Restart</span>
                      </Button>
                    </div>
                  ))
                ) : (
                  <div className="col-span-full text-center p-8 text-zinc-500 text-xs italic">
                    No services found matching search query.
                  </div>
                )}
              </div>
            </div>
          )}

          {/* Tab 3: Live DHCP Leases */}
          {activeSubTab === 'dhcp' && (
            <div className="space-y-4 animate-in fade-in">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                <div className="relative flex-1 max-w-sm">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-500" />
                  <Input
                    placeholder="Search by IP, hostname, or MAC address..."
                    value={dhcpSearch}
                    onChange={(e) => setDhcpSearch(e.target.value)}
                    className="pl-8 text-xs h-8"
                  />
                </div>
                <div className="flex items-center gap-2">
                  <span className="text-xs text-zinc-500">
                    {filteredLeases.length} active leases
                  </span>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => refetchLeases()}
                    className="h-8 text-zinc-400 hover:text-zinc-200"
                  >
                    <RefreshCw className="h-3.5 w-3.5" />
                  </Button>
                </div>
              </div>

              <div className="overflow-x-auto rounded-xl border border-zinc-800 bg-zinc-950/50">
                <table className="w-full text-left text-xs text-zinc-300">
                  <thead className="bg-zinc-900/80 text-zinc-400 font-medium uppercase text-[10px] tracking-wider border-b border-zinc-800">
                    <tr>
                      <th className="px-4 py-2.5">IP Address</th>
                      <th className="px-4 py-2.5">Hostname</th>
                      <th className="px-4 py-2.5">MAC Address</th>
                      <th className="px-4 py-2.5">Status</th>
                      <th className="px-4 py-2.5 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-zinc-800/60 font-mono">
                    {filteredLeases.length > 0 ? (
                      filteredLeases.map((lease) => (
                        <tr key={lease.ip + lease.mac} className="hover:bg-zinc-900/40">
                          <td className="px-4 py-2.5 font-bold text-zinc-100 font-mono">
                            {lease.ip}
                          </td>
                          <td className="px-4 py-2.5 font-sans font-medium text-zinc-300">
                            {lease.hostname || (
                              <span className="text-zinc-500 italic font-mono text-[11px]">unnamed</span>
                            )}
                          </td>
                          <td className="px-4 py-2.5 text-zinc-400">
                            <div className="flex items-center gap-1.5">
                              <span>{lease.mac}</span>
                              <button
                                onClick={() => handleCopy(lease.mac)}
                                className="p-1 rounded text-zinc-500 hover:text-zinc-300 transition-colors"
                                title="Copy MAC"
                              >
                                {copiedMac === lease.mac ? (
                                  <Check className="h-3 w-3 text-emerald-400" />
                                ) : (
                                  <Copy className="h-3 w-3" />
                                )}
                              </button>
                            </div>
                          </td>
                          <td className="px-4 py-2.5">
                            <Badge variant="success" className="text-[10px] py-0">
                              {lease.status}
                            </Badge>
                          </td>
                          <td className="px-4 py-2.5 text-right">
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => handleAdoptLease(lease)}
                              className="h-7 px-2 text-[11px] font-sans gap-1 text-sky-400 border-sky-600/30 hover:bg-sky-950/30 hover:text-sky-300"
                            >
                              <UserPlus className="h-3 w-3" />
                              <span>Adopt Host</span>
                            </Button>
                          </td>
                        </tr>
                      ))
                    ) : (
                      <tr>
                        <td colSpan={5} className="px-4 py-8 text-center text-zinc-500 italic font-sans">
                          {isLeasesLoading ? 'Querying OPNsense DHCP daemon...' : 'No DHCP leases found.'}
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Edit / Add Modal */}
      {modalOpen && (
        <AddOPNsenseModal
          open={modalOpen}
          onClose={() => setModalOpen(false)}
          initialInstance={editingInstance}
        />
      )}

      {/* Quick Adopt Modal */}
      {adoptModalOpen && (
        <AdoptNodeModal
          isOpen={adoptModalOpen}
          onClose={() => {
            setAdoptModalOpen(false)
            setAdoptTarget(null)
          }}
          host={adoptTarget}
        />
      )}
    </div>
  )
}
