import { useState } from 'react'
import {
  Wifi,
  Plus,
  Radio,
  Trash2,
  Edit2,
  ShieldCheck,
  CheckCircle2,
  XCircle,
  AlertCircle,
  Network,
  RotateCcw,
  ArrowUpCircle,
  Users,
  HardDrive,
  ChevronRight,
  ChevronDown,
} from 'lucide-react'
import { Button } from '../../../components/ui/button'
import { Badge } from '../../../components/ui/badge'
import {
  useUniFiInstances,
  useDeleteUniFiInstance,
  useTestUniFiConnection,
  useUniFiDevices,
  useUniFiClients,
  useRestartUniFiDevice,
  useUpgradeUniFiDevice,
} from './useUniFi'
import { AddUniFiModal } from './AddUniFiModal'
import { SwitchPortVisualizer } from './SwitchPortVisualizer'
import type {
  UniFiInstanceDto,
  UniFiTestResult,
  UniFiDeviceDto,
} from '../../../api/unifi'

export function UniFiAdaptersView() {
  const { data: instances, isLoading, isError, error } = useUniFiInstances()
  const deleteMutation = useDeleteUniFiInstance()
  const testMutation = useTestUniFiConnection()

  const [modalOpen, setModalOpen] = useState(false)
  const [editingInstance, setEditingInstance] = useState<UniFiInstanceDto | null>(null)
  const [selectedInstanceId, setSelectedInstanceId] = useState<string | null>(null)
  const [testResults, setTestResults] = useState<Record<string, UniFiTestResult>>({})
  const [testingId, setTestingId] = useState<string | null>(null)
  const [expandedSwitchMac, setExpandedSwitchMac] = useState<string | null>(null)
  const [activeTab, setActiveTab] = useState<'devices' | 'clients'>('devices')

  const { data: devices, isLoading: loadingDevices } = useUniFiDevices(selectedInstanceId)
  const { data: clients, isLoading: loadingClients } = useUniFiClients(selectedInstanceId)

  const restartMutation = useRestartUniFiDevice(selectedInstanceId || '')
  const upgradeMutation = useUpgradeUniFiDevice(selectedInstanceId || '')

  const handleTestConnection = async (inst: UniFiInstanceDto) => {
    setTestingId(inst.id)
    try {
      const res = await testMutation.mutateAsync(inst.id)
      setTestResults((prev) => ({ ...prev, [inst.id]: res }))
    } catch (err: any) {
      setTestResults((prev) => ({
        ...prev,
        [inst.id]: {
          success: false,
          latencyMs: 0,
          message: err.message || 'Connection test failed',
        },
      }))
    } finally {
      setTestingId(null)
    }
  }

  const handleDelete = async (inst: UniFiInstanceDto) => {
    if (confirm(`Are you sure you want to remove UniFi controller '${inst.name}'?`)) {
      try {
        await deleteMutation.mutateAsync(inst.id)
        if (selectedInstanceId === inst.id) {
          setSelectedInstanceId(null)
        }
      } catch (err: any) {
        alert(err.message || 'Failed to remove controller')
      }
    }
  }

  const handleRestartDevice = async (device: UniFiDeviceDto) => {
    if (!selectedInstanceId) return
    if (confirm(`Restart device ${device.name || device.model} (${device.mac})?`)) {
      try {
        await restartMutation.mutateAsync({ deviceMac: device.mac })
      } catch (err: any) {
        alert(err.message || 'Failed to trigger restart')
      }
    }
  }

  const handleUpgradeDevice = async (device: UniFiDeviceDto) => {
    if (!selectedInstanceId) return
    if (confirm(`Trigger rolling firmware upgrade for ${device.name || device.model} (${device.mac})?`)) {
      try {
        await upgradeMutation.mutateAsync(device.mac)
      } catch (err: any) {
        alert(err.message || 'Failed to trigger upgrade')
      }
    }
  }

  return (
    <div className="space-y-6">
      {/* Header bar */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 rounded-xl border border-blue-500/20 bg-gradient-to-r from-blue-950/20 via-slate-900/60 to-slate-900/20">
        <div className="flex items-center gap-3.5">
          <div className="p-2.5 rounded-xl bg-blue-500/10 border border-blue-500/30 text-blue-400">
            <Wifi className="h-6 w-6" />
          </div>
          <div>
            <h2 className="text-lg font-bold text-slate-100 flex items-center gap-2">
              Ubiquiti UniFi Controllers
              <Badge variant="outline" className="border-blue-500/30 text-blue-400 font-mono text-[11px]">
                {instances?.length ?? 0} {instances?.length === 1 ? 'Controller' : 'Controllers'}
              </Badge>
            </h2>
            <p className="text-xs text-muted-foreground mt-0.5">
              Manage UniFi OS Server and Network Application controllers, PoE switch power recycling, rolling firmware updates, and client discovery.
            </p>
          </div>
        </div>

        <Button
          onClick={() => {
            setEditingInstance(null)
            setModalOpen(true)
          }}
          className="bg-blue-600 hover:bg-blue-500 text-white gap-2 shrink-0 self-start sm:self-auto"
        >
          <Plus className="h-4 w-4" />
          <span>Add UniFi Controller</span>
        </Button>
      </div>

      {/* Controller Cards */}
      {isLoading && (
        <div className="p-8 text-center text-sm text-muted-foreground flex items-center justify-center gap-2">
          <Radio className="h-4 w-4 animate-spin text-blue-400" />
          <span>Loading UniFi controllers...</span>
        </div>
      )}

      {isError && (
        <div className="p-4 bg-red-950/40 border border-red-800/50 rounded-lg text-red-300 text-sm flex items-center gap-3">
          <AlertCircle className="h-5 w-5 text-red-400 shrink-0" />
          <span>Failed to load UniFi controllers: {(error as Error).message}</span>
        </div>
      )}

      {!isLoading && (!instances || instances.length === 0) && (
        <div className="p-12 text-center border border-dashed border-slate-800 rounded-xl bg-slate-900/20">
          <Wifi className="h-10 w-10 text-slate-600 mx-auto mb-3" />
          <h3 className="text-sm font-semibold text-slate-300">No UniFi Controllers Configured</h3>
          <p className="text-xs text-muted-foreground mt-1 max-w-md mx-auto">
            Connect your UniFi OS Server (API Key) or legacy Network Application container to enable hardware PoE power-cycling, rolling updates, and switch management.
          </p>
          <Button
            onClick={() => {
              setEditingInstance(null)
              setModalOpen(true)
            }}
            variant="outline"
            className="mt-4 gap-2 border-slate-700"
          >
            <Plus className="h-4 w-4" />
            <span>Add First Controller</span>
          </Button>
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {instances?.map((inst) => {
          const isTesting = testingId === inst.id
          const testResult = testResults[inst.id]
          const isSelected = selectedInstanceId === inst.id
          const isApiKey = inst.authType === 'api_key' || inst.hasApiKey

          return (
            <div
              key={inst.id}
              className={`p-5 rounded-xl border transition-all ${
                isSelected
                  ? 'bg-slate-900/90 border-blue-500/50 shadow-md ring-1 ring-blue-500/20'
                  : 'bg-slate-900/40 border-slate-800/80 hover:border-slate-700/80'
              }`}
            >
              <div className="flex items-start justify-between gap-3 mb-3">
                <div>
                  <div className="flex items-center gap-2 flex-wrap">
                    <h3 className="text-base font-semibold text-slate-100">{inst.name}</h3>
                    <Badge variant="outline" className="text-[10px] border-slate-700 text-slate-400 font-mono">
                      Site: {inst.site}
                    </Badge>
                    {isApiKey ? (
                      <Badge variant="outline" className="text-[10px] border-blue-500/30 text-blue-400 bg-blue-500/10">
                        UniFi OS (API Key)
                      </Badge>
                    ) : (
                      <Badge variant="outline" className="text-[10px] border-amber-500/30 text-amber-400 bg-amber-500/10">
                        Legacy (User/Pass)
                      </Badge>
                    )}
                  </div>
                  <div className="text-xs font-mono text-muted-foreground mt-1 truncate max-w-sm">
                    {inst.controllerUrl}
                  </div>
                </div>

                <div className="flex items-center gap-1.5">
                  <Button
                    size="sm"
                    variant="ghost"
                    onClick={() => {
                      setEditingInstance(inst)
                      setModalOpen(true)
                    }}
                    className="h-8 w-8 p-0 text-slate-400 hover:text-slate-100"
                    title="Edit Controller Settings"
                  >
                    <Edit2 className="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    size="sm"
                    variant="ghost"
                    onClick={() => handleDelete(inst)}
                    className="h-8 w-8 p-0 text-red-400/80 hover:text-red-300 hover:bg-red-950/30"
                    title="Delete Controller"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </Button>
                </div>
              </div>

              {/* Status & Test Results */}
              {testResult && (
                <div
                  className={`p-2.5 rounded-lg border text-xs mb-3.5 flex items-center justify-between ${
                    testResult.success
                      ? 'bg-emerald-950/20 border-emerald-800/40 text-emerald-300'
                      : 'bg-red-950/20 border-red-800/40 text-red-300'
                  }`}
                >
                  <div className="flex items-center gap-2 truncate">
                    {testResult.success ? (
                      <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-400" />
                    ) : (
                      <XCircle className="h-4 w-4 shrink-0 text-red-400" />
                    )}
                    <span className="truncate">
                      {testResult.success
                        ? `${testResult.controllerVersion || 'Connected'} (${testResult.deviceCount ?? 0} devices)`
                        : testResult.message}
                    </span>
                  </div>
                  <span className="font-mono text-[10px] opacity-80 shrink-0 ml-2">
                    {testResult.latencyMs}ms
                  </span>
                </div>
              )}

              {/* Card Footer Actions */}
              <div className="flex items-center justify-between pt-3 border-t border-slate-800/60 text-xs">
                <Button
                  size="sm"
                  variant="outline"
                  disabled={isTesting}
                  onClick={() => handleTestConnection(inst)}
                  className="h-7 text-xs gap-1.5 border-slate-700 bg-slate-800/30 hover:bg-slate-800"
                >
                  {isTesting ? (
                    <Radio className="h-3.5 w-3.5 animate-spin text-blue-400" />
                  ) : (
                    <ShieldCheck className="h-3.5 w-3.5 text-blue-400" />
                  )}
                  <span>Test Connection</span>
                </Button>

                <Button
                  size="sm"
                  variant={isSelected ? 'primary' : 'secondary'}
                  onClick={() => setSelectedInstanceId(isSelected ? null : inst.id)}
                  className={`h-7 text-xs gap-1.5 ${
                    isSelected ? 'bg-blue-600 hover:bg-blue-500 text-white' : ''
                  }`}
                >
                  <Network className="h-3.5 w-3.5" />
                  <span>{isSelected ? 'Hide Devices' : 'Inspect Devices'}</span>
                </Button>
              </div>
            </div>
          )
        })}
      </div>

      {/* Selected Controller Inspection Workspace */}
      {selectedInstanceId && (
        <div className="p-6 rounded-xl border border-slate-800 bg-slate-900/60 backdrop-blur-sm space-y-6">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-slate-800 pb-4">
            <div>
              <h3 className="text-base font-bold text-slate-100 flex items-center gap-2">
                <HardDrive className="h-5 w-5 text-blue-400" />
                <span>Adopted Devices & Infrastructure</span>
              </h3>
              <p className="text-xs text-muted-foreground mt-0.5">
                Switches, access points, and active network stations under this controller.
              </p>
            </div>

            <div className="flex items-center gap-2">
              <Button
                size="sm"
                variant={activeTab === 'devices' ? 'primary' : 'outline'}
                onClick={() => setActiveTab('devices')}
                className="text-xs gap-1.5"
              >
                <HardDrive className="h-3.5 w-3.5" />
                <span>Devices ({devices?.length ?? 0})</span>
              </Button>
              <Button
                size="sm"
                variant={activeTab === 'clients' ? 'primary' : 'outline'}
                onClick={() => setActiveTab('clients')}
                className="text-xs gap-1.5"
              >
                <Users className="h-3.5 w-3.5" />
                <span>Active Clients ({clients?.length ?? 0})</span>
              </Button>
            </div>
          </div>

          {activeTab === 'devices' && (
            <div className="space-y-4">
              {loadingDevices && (
                <div className="p-8 text-center text-sm text-muted-foreground flex items-center justify-center gap-2">
                  <Radio className="h-4 w-4 animate-spin text-blue-400" />
                  <span>Querying UniFi device telemetry...</span>
                </div>
              )}

              {!loadingDevices && (!devices || devices.length === 0) && (
                <div className="p-8 text-center text-sm text-muted-foreground border border-dashed border-slate-800 rounded-lg">
                  No devices adopted under this UniFi controller site.
                </div>
              )}

              {devices?.map((dev) => {
                const isExpanded = expandedSwitchMac === dev.mac
                const isSwitch = dev.type === 'usw' || (dev.ports && dev.ports.length > 0)

                return (
                  <div
                    key={dev.mac}
                    className="p-4 rounded-xl border border-slate-800 bg-slate-950/50 space-y-4"
                  >
                    <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                      <div className="flex items-center gap-3">
                        <div className="p-2 rounded-lg bg-blue-500/10 text-blue-400 border border-blue-500/20">
                          <Network className="h-4 w-4" />
                        </div>
                        <div>
                          <div className="flex items-center gap-2">
                            <span className="text-sm font-bold text-slate-100">
                              {dev.name || dev.model}
                            </span>
                            <Badge variant="outline" className="text-[10px] font-mono border-slate-700">
                              {dev.model}
                            </Badge>
                            <Badge
                              variant="outline"
                              className={`text-[10px] ${
                                dev.state === 'Connected'
                                  ? 'bg-emerald-500/10 border-emerald-500/30 text-emerald-300'
                                  : 'bg-slate-800 border-slate-700 text-slate-400'
                              }`}
                            >
                              {dev.state}
                            </Badge>
                            {dev.upgradeAvailable && (
                              <Badge className="bg-amber-500/10 border-amber-500/30 text-amber-300 text-[10px] flex items-center gap-1">
                                <ArrowUpCircle className="h-3 w-3 text-amber-400" />
                                <span>Upgrade Available</span>
                              </Badge>
                            )}
                          </div>
                          <div className="flex items-center gap-3 text-xs text-muted-foreground mt-1">
                            <span className="font-mono">{dev.ip || 'No IP'}</span>
                            <span>•</span>
                            <span className="font-mono">{dev.mac}</span>
                            {dev.version && (
                              <>
                                <span>•</span>
                                <span>v{dev.version}</span>
                              </>
                            )}
                            {dev.temperature && (
                              <>
                                <span>•</span>
                                <span>{dev.temperature.toFixed(0)}°C</span>
                              </>
                            )}
                          </div>
                        </div>
                      </div>

                      <div className="flex items-center gap-2">
                        {isSwitch && (
                          <Button
                            size="sm"
                            variant="outline"
                            onClick={() => setExpandedSwitchMac(isExpanded ? null : dev.mac)}
                            className="h-8 text-xs gap-1.5 border-slate-700"
                          >
                            {isExpanded ? (
                              <ChevronDown className="h-3.5 w-3.5" />
                            ) : (
                              <ChevronRight className="h-3.5 w-3.5" />
                            )}
                            <span>{isExpanded ? 'Hide Ports' : `Switch Ports (${dev.ports.length})`}</span>
                          </Button>
                        )}

                        <Button
                          size="sm"
                          variant="outline"
                          disabled={restartMutation.isPending}
                          onClick={() => handleRestartDevice(dev)}
                          className="h-8 text-xs gap-1.5 border-slate-700 text-slate-300 hover:text-white"
                          title="Restart hardware"
                        >
                          <RotateCcw className="h-3.5 w-3.5 text-slate-400" />
                          <span>Restart</span>
                        </Button>

                        {dev.upgradeAvailable && (
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={upgradeMutation.isPending}
                            onClick={() => handleUpgradeDevice(dev)}
                            className="h-8 text-xs gap-1.5 border-amber-500/30 bg-amber-500/10 text-amber-300 hover:bg-amber-500/20"
                            title="Trigger rolling firmware update"
                          >
                            <ArrowUpCircle className="h-3.5 w-3.5 text-amber-400" />
                            <span>Upgrade</span>
                          </Button>
                        )}
                      </div>
                    </div>

                    {/* Expandable Switch Port Visualizer */}
                    {isSwitch && isExpanded && (
                      <div className="pt-3 border-t border-slate-800/80">
                        <SwitchPortVisualizer
                          instanceId={selectedInstanceId}
                          deviceMac={dev.mac}
                          deviceName={dev.name || dev.model}
                          ports={dev.ports}
                        />
                      </div>
                    )}
                  </div>
                )
              })}
            </div>
          )}

          {activeTab === 'clients' && (
            <div className="space-y-4">
              {loadingClients && (
                <div className="p-8 text-center text-sm text-muted-foreground flex items-center justify-center gap-2">
                  <Radio className="h-4 w-4 animate-spin text-blue-400" />
                  <span>Loading active UniFi network clients...</span>
                </div>
              )}

              {!loadingClients && (!clients || clients.length === 0) && (
                <div className="p-8 text-center text-sm text-muted-foreground border border-dashed border-slate-800 rounded-lg">
                  No active clients reported by UniFi.
                </div>
              )}

              {clients && clients.length > 0 && (
                <div className="overflow-x-auto rounded-lg border border-slate-800">
                  <table className="w-full text-left text-xs">
                    <thead className="bg-slate-900/80 text-slate-400 font-semibold border-b border-slate-800">
                      <tr>
                        <th className="p-3">Client Hostname</th>
                        <th className="p-3">IP Address</th>
                        <th className="p-3">MAC Address</th>
                        <th className="p-3">Last Seen</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-800/60">
                      {clients.map((c) => (
                        <tr key={c.mac} className="hover:bg-slate-900/40">
                          <td className="p-3 font-medium text-slate-200">
                            {c.hostname || 'Unknown Client'}
                          </td>
                          <td className="p-3 font-mono text-slate-300">
                            {c.ip || '—'}
                          </td>
                          <td className="p-3 font-mono text-slate-400">
                            {c.mac}
                          </td>
                          <td className="p-3 text-slate-400">
                            {c.lastSeen ? new Date(c.lastSeen).toLocaleTimeString() : 'Active'}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* Add / Edit UniFi Controller Modal */}
      <AddUniFiModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        initialInstance={editingInstance}
      />
    </div>
  )
}
