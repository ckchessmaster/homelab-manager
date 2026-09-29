import { useState } from 'react'
import {
  Shield,
  Plus,
  Radio,
  Trash2,
  Edit2,
  RefreshCw,
  Server,
  ExternalLink,
  Activity,
  Zap,
  Network,
  ShieldAlert,
  Globe,
} from 'lucide-react'
import { Button } from '../../../components/ui/button'
import { Badge } from '../../../components/ui/badge'
import {
  useOPNsenseInstances,
  useDeleteOPNsenseInstance,
  useOPNsenseTelemetry,
  useOPNsenseDhcpLeases,
  useOPNsenseVitals,
} from './useOPNsense'
import { AddOPNsenseModal } from './AddOPNsenseModal'
import { OPNsenseFirmwareBanner } from './OPNsenseFirmwareBanner'
import { OPNsenseVitalsCard } from './OPNsenseVitalsCard'
import { OPNsenseInterfacesTable } from './OPNsenseInterfacesTable'
import { OPNsenseSecurityView } from './OPNsenseSecurityView'
import { OPNsenseProxyServicesView } from './OPNsenseProxyServicesView'
import { OPNsenseNetworkView } from './OPNsenseNetworkView'
import type { OPNsenseInstanceDto } from '../../../api/opnsense'

type OPNsenseTab = 'overview' | 'security' | 'services' | 'network'

export function OPNsenseAdaptersView() {
  const { data: instances, isLoading, refetch } = useOPNsenseInstances()
  const deleteMutation = useDeleteOPNsenseInstance()

  const [selectedInstanceId, setSelectedInstanceId] = useState<string | null>(null)
  const [modalOpen, setModalOpen] = useState(false)
  const [editingInstance, setEditingInstance] = useState<OPNsenseInstanceDto | null>(null)

  // Active firewall selection
  const activeInstanceId = selectedInstanceId || (instances && instances.length > 0 ? instances[0].id : null)
  const activeInstance = instances?.find((i) => i.id === activeInstanceId)

  // Sub-view 4-tab selection
  const [activeTab, setActiveTab] = useState<OPNsenseTab>('overview')

  // Telemetry & DHCP Queries
  const { data: telemetry, refetch: refetchTelemetry } = useOPNsenseTelemetry(activeInstanceId)
  const { data: leases, refetch: refetchLeases } = useOPNsenseDhcpLeases(activeInstanceId)
  const { refetch: refetchVitals } = useOPNsenseVitals(activeInstanceId)

  const handleDelete = async (id: string, name: string) => {
    if (confirm(`Are you sure you want to remove OPNsense firewall '${name}'?`)) {
      await deleteMutation.mutateAsync(id)
      if (selectedInstanceId === id) {
        setSelectedInstanceId(null)
      }
    }
  }

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
            Connect your OPNsense firewall to monitor hardware vitals, firmware updates, gateway health, public WAN status, HAProxy/ACME services, and discover network hosts for instant adoption.
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
                  refetchVitals()
                }}
                className="text-zinc-400 hover:text-zinc-200"
                title="Refresh All Telemetry"
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

          {/* 4-Tab Navigation Header */}
          <div className="flex items-center justify-between border-b border-zinc-800 pb-2">
            <div className="flex items-center gap-1 sm:gap-2 overflow-x-auto">
              <button
                onClick={() => setActiveTab('overview')}
                className={`px-3 py-1.5 text-xs font-medium rounded-lg transition-colors flex items-center gap-1.5 shrink-0 ${
                  activeTab === 'overview'
                    ? 'bg-zinc-800 text-zinc-100'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <Activity className="h-3.5 w-3.5 text-orange-400" />
                <span>Overview & Vitals</span>
              </button>

              <button
                onClick={() => setActiveTab('security')}
                className={`px-3 py-1.5 text-xs font-medium rounded-lg transition-colors flex items-center gap-1.5 shrink-0 ${
                  activeTab === 'security'
                    ? 'bg-zinc-800 text-zinc-100'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <ShieldAlert className="h-3.5 w-3.5 text-amber-400" />
                <span>Firewall & Security</span>
              </button>

              <button
                onClick={() => setActiveTab('services')}
                className={`px-3 py-1.5 text-xs font-medium rounded-lg transition-colors flex items-center gap-1.5 shrink-0 ${
                  activeTab === 'services'
                    ? 'bg-zinc-800 text-zinc-100'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <Globe className="h-3.5 w-3.5 text-emerald-400" />
                <span>Services & Proxy</span>
                {telemetry?.services && (
                  <Badge variant="default" className="text-[10px] py-0 px-1">
                    {telemetry.services.length}
                  </Badge>
                )}
              </button>

              <button
                onClick={() => setActiveTab('network')}
                className={`px-3 py-1.5 text-xs font-medium rounded-lg transition-colors flex items-center gap-1.5 shrink-0 ${
                  activeTab === 'network'
                    ? 'bg-zinc-800 text-zinc-100'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <Network className="h-3.5 w-3.5 text-sky-400" />
                <span>DHCP & Network</span>
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
              className="text-xs text-orange-400 hover:text-orange-300 flex items-center gap-1 shrink-0"
            >
              <span>Open WebGUI</span>
              <ExternalLink className="h-3 w-3" />
            </a>
          </div>

          {/* TAB 1: Overview & Vitals */}
          {activeTab === 'overview' && (
            <div className="space-y-6 animate-in fade-in">
              {/* Firmware & Updates Banner */}
              <OPNsenseFirmwareBanner
                instanceId={activeInstance.id}
                baseUrl={activeInstance.baseUrl}
              />

              {/* Hardware Vitals & System Resources */}
              <OPNsenseVitalsCard instanceId={activeInstance.id} />

              {/* Routing Gateways & Latency */}
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

              {/* Physical & Virtual Interfaces Table */}
              <OPNsenseInterfacesTable interfaces={telemetry?.interfaces} />
            </div>
          )}

          {/* TAB 2: Firewall & Security */}
          {activeTab === 'security' && (
            <OPNsenseSecurityView instanceId={activeInstance.id} />
          )}

          {/* TAB 3: Services & Proxy */}
          {activeTab === 'services' && (
            <OPNsenseProxyServicesView
              instanceId={activeInstance.id}
              baseUrl={activeInstance.baseUrl}
              services={telemetry?.services}
            />
          )}

          {/* TAB 4: DHCP & Network */}
          {activeTab === 'network' && (
            <OPNsenseNetworkView instanceId={activeInstance.id} />
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
    </div>
  )
}
