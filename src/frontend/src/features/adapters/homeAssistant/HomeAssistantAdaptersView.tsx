import { useState } from 'react'
import {
  Home,
  Plus,
  Trash2,
  Edit2,
  RefreshCw,
  Server,
  RotateCw,
  ExternalLink,
  ShieldCheck,
  AlertTriangle,
  HardDrive,
  Cpu,
  Package,
  CheckCircle2,
  Archive,
  Download,
} from 'lucide-react'
import { Button } from '../../../components/ui/button'
import { Badge } from '../../../components/ui/badge'
import {
  useHomeAssistantInstances,
  useDeleteHomeAssistantInstance,
  useHomeAssistantOverview,
  useHomeAssistantBackups,
  useCheckHomeAssistantConfig,
  useRestartHomeAssistantCore,
  useRebootHomeAssistantHost,
  useUpdateHomeAssistantOs,
  useCreateHomeAssistantBackup,
} from './useHomeAssistant'
import { AddHomeAssistantModal } from './AddHomeAssistantModal'
import type { HomeAssistantInstanceDto } from '../../../api/homeAssistant'

export function HomeAssistantAdaptersView() {
  const { data: instances, isLoading, refetch } = useHomeAssistantInstances()
  const deleteMutation = useDeleteHomeAssistantInstance()

  const [selectedInstanceId, setSelectedInstanceId] = useState<string | null>(null)
  const [modalOpen, setModalOpen] = useState(false)
  const [editingInstance, setEditingInstance] = useState<HomeAssistantInstanceDto | null>(null)

  const activeInstanceId = selectedInstanceId || (instances && instances.length > 0 ? instances[0].id : null)
  const activeInstance = instances?.find((i) => i.id === activeInstanceId)

  // Sub-view Tab: 'overview' | 'backups'
  const [activeTab, setActiveTab] = useState<'overview' | 'backups'>('overview')

  // Telemetry & Backups
  const { data: overview, isLoading: isOverviewLoading, refetch: refetchOverview } = useHomeAssistantOverview(activeInstanceId)
  const { data: backups, isLoading: isBackupsLoading, refetch: refetchBackups } = useHomeAssistantBackups(activeInstanceId)

  // Mutations
  const checkConfigMutation = useCheckHomeAssistantConfig()
  const restartCoreMutation = useRestartHomeAssistantCore()
  const rebootHostMutation = useRebootHomeAssistantHost()
  const updateOsMutation = useUpdateHomeAssistantOs()
  const createBackupMutation = useCreateHomeAssistantBackup()

  // Config check result local state
  const [configCheckResult, setConfigCheckResult] = useState<{ isValid: boolean; message: string } | null>(null)

  const handleDelete = async (id: string, name: string) => {
    if (confirm(`Are you sure you want to disconnect Home Assistant appliance '${name}'?`)) {
      await deleteMutation.mutateAsync(id)
      if (selectedInstanceId === id) {
        setSelectedInstanceId(null)
      }
    }
  }

  const handleCheckConfig = async () => {
    if (!activeInstanceId) return
    try {
      const res = await checkConfigMutation.mutateAsync(activeInstanceId)
      setConfigCheckResult({
        isValid: res.isValid,
        message: res.isValid ? 'Configuration is valid! Safe to update or restart.' : (res.errors || 'Configuration validation failed.'),
      })
    } catch (err: unknown) {
      setConfigCheckResult({
        isValid: false,
        message: err instanceof Error ? err.message : 'Failed to validate configuration.',
      })
    }
  }

  const handleRestartCore = async () => {
    if (!activeInstanceId) return
    if (confirm(`Restart Home Assistant Core on ${activeInstance?.name}?`)) {
      await restartCoreMutation.mutateAsync(activeInstanceId)
    }
  }

  const handleRebootHost = async () => {
    if (!activeInstanceId) return
    if (confirm(`Trigger a clean Host OS reboot on ${activeInstance?.name}? Any running add-ons will gracefully shut down.`)) {
      await rebootHostMutation.mutateAsync(activeInstanceId)
    }
  }

  const handleUpdateOs = async () => {
    if (!activeInstanceId) return
    const latest = overview?.os?.versionLatest || 'latest'
    if (confirm(`Trigger official Home Assistant OS OTA update to ${latest}? A host reboot will occur automatically.`)) {
      await updateOsMutation.mutateAsync(activeInstanceId)
    }
  }

  const handleCreateBackup = async () => {
    if (!activeInstanceId) return
    const backupName = prompt('Enter a name for the full backup snapshot:', `ControlPlane-${new Date().toISOString().slice(0, 10)}`)
    if (backupName !== null) {
      await createBackupMutation.mutateAsync({ id: activeInstanceId, request: { name: backupName.trim() || undefined } })
    }
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12 text-zinc-500 text-xs">
        <RefreshCw className="h-4 w-4 animate-spin mr-2" />
        Loading Home Assistant instances...
      </div>
    )
  }

  if (!instances || instances.length === 0) {
    return (
      <div className="border border-dashed border-zinc-800 rounded-2xl p-12 text-center max-w-xl mx-auto space-y-4">
        <div className="p-3 bg-emerald-500/10 text-emerald-400 rounded-2xl w-fit mx-auto border border-emerald-500/20">
          <Home className="h-8 w-8" />
        </div>
        <div className="space-y-1">
          <h3 className="text-base font-medium text-zinc-100">No Home Assistant Appliances Connected</h3>
          <p className="text-xs text-zinc-400 max-w-md mx-auto">
            Connect your Home Assistant OS (HAOS), Supervised, or Container VM using a Long-Lived Access Token to monitor host vitals, validate configurations, and automate backups.
          </p>
        </div>
        <Button
          onClick={() => {
            setEditingInstance(null)
            setModalOpen(true)
          }}
          className="gap-2 text-xs bg-emerald-600 hover:bg-emerald-500 text-white"
        >
          <Plus className="h-3.5 w-3.5" />
          Connect Home Assistant
        </Button>

        <AddHomeAssistantModal
          open={modalOpen}
          onClose={() => setModalOpen(false)}
          initialInstance={null}
        />
      </div>
    )
  }

  return (
    <div className="space-y-6">
      {/* Top Header: Instance Selector & Actions */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 bg-zinc-900/60 p-4 border border-zinc-800 rounded-2xl">
        <div className="flex items-center gap-3 overflow-x-auto pb-1 sm:pb-0">
          {instances.map((inst) => {
            const isSelected = inst.id === activeInstanceId
            return (
              <button
                key={inst.id}
                onClick={() => setSelectedInstanceId(inst.id)}
                className={`flex items-center gap-2.5 px-3.5 py-2 rounded-xl text-xs font-medium transition-all shrink-0 ${
                  isSelected
                    ? 'bg-zinc-800 text-zinc-100 shadow-sm border border-zinc-700'
                    : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/40 border border-transparent'
                }`}
              >
                <Home className={`h-4 w-4 ${isSelected ? 'text-emerald-400' : 'text-zinc-500'}`} />
                <span>{inst.name}</span>
                <span className="text-[10px] font-mono text-zinc-500">{inst.baseUrl}</span>
              </button>
            )
          })}
        </div>

        <div className="flex items-center gap-2 shrink-0">
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              refetch()
              refetchOverview()
              refetchBackups()
            }}
            className="text-xs border-zinc-700 text-zinc-300 gap-1.5"
          >
            <RefreshCw className="h-3.5 w-3.5 text-zinc-400" />
            Refresh
          </Button>

          {activeInstance && (
            <>
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  setEditingInstance(activeInstance)
                  setModalOpen(true)
                }}
                className="text-xs border-zinc-700 text-zinc-300 gap-1.5"
              >
                <Edit2 className="h-3.5 w-3.5 text-zinc-400" />
                Edit
              </Button>

              <Button
                variant="outline"
                size="sm"
                onClick={() => handleDelete(activeInstance.id, activeInstance.name)}
                className="text-xs border-zinc-700 text-rose-400 hover:text-rose-300 hover:bg-rose-950/20 gap-1.5"
              >
                <Trash2 className="h-3.5 w-3.5" />
                Disconnect
              </Button>
            </>
          )}

          <Button
            size="sm"
            onClick={() => {
              setEditingInstance(null)
              setModalOpen(true)
            }}
            className="text-xs bg-emerald-600 hover:bg-emerald-500 text-white gap-1.5"
          >
            <Plus className="h-3.5 w-3.5" />
            Connect Appliance
          </Button>
        </div>
      </div>

      {activeInstance && (
        <div className="space-y-6">
          {/* Main Appliance Overview Banner */}
          <div className="bg-gradient-to-r from-zinc-900 to-zinc-900/80 border border-zinc-800 rounded-2xl p-5 space-y-4">
            <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
              <div className="flex items-center gap-3.5">
                <div className="p-3 bg-emerald-500/10 text-emerald-400 rounded-xl border border-emerald-500/20 shrink-0">
                  <Home className="h-6 w-6" />
                </div>
                <div>
                  <div className="flex items-center gap-2">
                    <h2 className="text-base font-semibold text-zinc-100">{activeInstance.name}</h2>
                    <Badge variant="success" className="text-[10px] px-1.5 py-0 gap-1">
                      <CheckCircle2 className="h-3 w-3" />
                      Connected
                    </Badge>
                    {overview?.host?.rebootRequired && (
                      <Badge variant="destructive" className="text-[10px] px-1.5 py-0">
                        Reboot Required
                      </Badge>
                    )}
                  </div>
                  <div className="flex items-center gap-3 text-xs text-zinc-400 mt-1">
                    <a
                      href={activeInstance.baseUrl}
                      target="_blank"
                      rel="noreferrer"
                      className="font-mono text-sky-400 hover:underline flex items-center gap-1"
                    >
                      {activeInstance.baseUrl}
                      <ExternalLink className="h-3 w-3" />
                    </a>
                    <span>&bull;</span>
                    <span>Hostname: <span className="font-mono text-zinc-300">{overview?.host?.hostname || 'homeassistant'}</span></span>
                    {overview?.latencyMs !== undefined && (
                      <>
                        <span>&bull;</span>
                        <span className="font-mono text-zinc-400">{overview.latencyMs} ms latency</span>
                      </>
                    )}
                  </div>
                </div>
              </div>

              {/* Correlated Host Tag */}
              {overview?.correlatedHostName && (
                <div className="px-3 py-1.5 bg-zinc-950 border border-zinc-800 rounded-xl flex items-center gap-2 text-xs">
                  <Server className="h-3.5 w-3.5 text-indigo-400" />
                  <span className="text-zinc-400">Correlated Host:</span>
                  <span className="font-medium text-zinc-200 font-mono">{overview.correlatedHostName}</span>
                </div>
              )}
            </div>

            {/* Quick Action Toolbar */}
            <div className="flex flex-wrap items-center gap-2 pt-2 border-t border-zinc-800/80">
              <Button
                variant="outline"
                size="sm"
                onClick={handleCheckConfig}
                disabled={checkConfigMutation.isPending}
                className="text-xs border-zinc-700 text-zinc-200 hover:bg-zinc-800 gap-1.5"
              >
                <ShieldCheck className={`h-3.5 w-3.5 text-emerald-400 ${checkConfigMutation.isPending ? 'animate-spin' : ''}`} />
                {checkConfigMutation.isPending ? 'Checking...' : 'Check Config'}
              </Button>

              <Button
                variant="outline"
                size="sm"
                onClick={handleCreateBackup}
                disabled={createBackupMutation.isPending}
                className="text-xs border-zinc-700 text-zinc-200 hover:bg-zinc-800 gap-1.5"
              >
                <Archive className={`h-3.5 w-3.5 text-indigo-400 ${createBackupMutation.isPending ? 'animate-spin' : ''}`} />
                {createBackupMutation.isPending ? 'Initiating...' : 'Create Backup'}
              </Button>

              <Button
                variant="outline"
                size="sm"
                onClick={handleRestartCore}
                disabled={restartCoreMutation.isPending}
                className="text-xs border-zinc-700 text-amber-400 hover:bg-amber-950/20 gap-1.5"
              >
                <RotateCw className={`h-3.5 w-3.5 ${restartCoreMutation.isPending ? 'animate-spin' : ''}`} />
                Restart Core
              </Button>

              <Button
                variant="outline"
                size="sm"
                onClick={handleRebootHost}
                disabled={rebootHostMutation.isPending}
                className="text-xs border-zinc-700 text-rose-400 hover:bg-rose-950/20 gap-1.5"
              >
                <RotateCw className={`h-3.5 w-3.5 ${rebootHostMutation.isPending ? 'animate-spin' : ''}`} />
                Reboot Host OS
              </Button>

              {overview?.os?.updateAvailable && (
                <Button
                  size="sm"
                  onClick={handleUpdateOs}
                  disabled={updateOsMutation.isPending}
                  className="text-xs bg-amber-600 hover:bg-amber-500 text-white gap-1.5 ml-auto"
                >
                  <Download className="h-3.5 w-3.5" />
                  Upgrade to {overview.os.versionLatest}
                </Button>
              )}
            </div>

            {/* Config Check Alert Banner */}
            {configCheckResult && (
              <div
                className={`p-3 rounded-xl border text-xs flex items-start gap-2.5 ${
                  configCheckResult.isValid
                    ? 'bg-emerald-950/30 border-emerald-800/50 text-emerald-300'
                    : 'bg-rose-950/40 border-rose-800/60 text-rose-300'
                }`}
              >
                {configCheckResult.isValid ? (
                  <CheckCircle2 className="h-4 w-4 text-emerald-400 shrink-0 mt-0.5" />
                ) : (
                  <AlertTriangle className="h-4 w-4 text-rose-400 shrink-0 mt-0.5" />
                )}
                <div className="space-y-0.5">
                  <p className="font-semibold">{configCheckResult.isValid ? 'Configuration Valid' : 'Configuration Errors Detected'}</p>
                  <p className="font-mono text-[11px] opacity-90 whitespace-pre-wrap">{configCheckResult.message}</p>
                </div>
              </div>
            )}
          </div>

          {/* Sub-Tabs: Overview vs Backups */}
          <div className="flex items-center gap-3 border-b border-zinc-800 text-xs">
            <button
              onClick={() => setActiveTab('overview')}
              className={`pb-2.5 font-medium transition-colors border-b-2 ${
                activeTab === 'overview'
                  ? 'border-emerald-500 text-zinc-100'
                  : 'border-transparent text-zinc-400 hover:text-zinc-200'
              }`}
            >
              Appliance Telemetry
            </button>
            <button
              onClick={() => setActiveTab('backups')}
              className={`pb-2.5 font-medium transition-colors border-b-2 flex items-center gap-1.5 ${
                activeTab === 'backups'
                  ? 'border-emerald-500 text-zinc-100'
                  : 'border-transparent text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <span>Backups & Recovery</span>
              {backups && backups.length > 0 && (
                <Badge variant="default" className="text-[10px] px-1 py-0">
                  {backups.length}
                </Badge>
              )}
            </button>
          </div>

          {/* Tab Content: Telemetry */}
          {activeTab === 'overview' && (
            <div className="space-y-6">
              {isOverviewLoading ? (
                <div className="flex items-center justify-center p-12 text-zinc-500 text-xs">
                  <RefreshCw className="h-4 w-4 animate-spin mr-2" />
                  Reading Home Assistant vitals...
                </div>
              ) : (
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                  {/* Host OS Card */}
                  <div className="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-3">
                    <div className="flex items-center justify-between">
                      <span className="text-xs text-zinc-400 font-medium">Operating System</span>
                      <Server className="h-4 w-4 text-emerald-400" />
                    </div>
                    <div>
                      <p className="text-sm font-semibold text-zinc-100">{overview?.os?.version || overview?.host?.operatingSystem || 'HAOS'}</p>
                      <p className="text-[11px] font-mono text-zinc-500 truncate mt-0.5">
                        Kernel: {overview?.host?.kernel || 'Linux'}
                      </p>
                    </div>
                    <div className="pt-2 border-t border-zinc-800/80 text-[11px] text-zinc-400 flex items-center justify-between">
                      <span>Boot Slot: <span className="font-mono text-zinc-300 font-semibold">{overview?.os?.bootSlot || 'A'}</span></span>
                      <span>Chassis: <span className="font-mono text-zinc-300 capitalize">{overview?.host?.chassis || 'VM'}</span></span>
                    </div>
                  </div>

                  {/* Core Card */}
                  <div className="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-3">
                    <div className="flex items-center justify-between">
                      <span className="text-xs text-zinc-400 font-medium">Home Assistant Core</span>
                      <Cpu className="h-4 w-4 text-sky-400" />
                    </div>
                    <div>
                      <p className="text-sm font-semibold text-zinc-100">{overview?.core?.version || 'Unknown'}</p>
                      <p className="text-[11px] text-zinc-400 mt-0.5 flex items-center gap-1.5">
                        <span className="inline-block w-2 h-2 rounded-full bg-emerald-400"></span>
                        <span className="uppercase font-mono text-[10px]">{overview?.core?.state || 'RUNNING'}</span>
                      </p>
                    </div>
                    <div className="pt-2 border-t border-zinc-800/80 text-[11px] text-zinc-400 flex items-center justify-between">
                      <span>Arch: <span className="font-mono text-zinc-300">{overview?.core?.arch || 'amd64'}</span></span>
                      {overview?.core?.updateAvailable ? (
                        <span className="text-amber-400 font-medium">Update to {overview.core.versionLatest}</span>
                      ) : (
                        <span className="text-emerald-400">Up to date</span>
                      )}
                    </div>
                  </div>

                  {/* Supervisor Card */}
                  <div className="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-3">
                    <div className="flex items-center justify-between">
                      <span className="text-xs text-zinc-400 font-medium">Supervisor</span>
                      <Package className="h-4 w-4 text-purple-400" />
                    </div>
                    <div>
                      <p className="text-sm font-semibold text-zinc-100">{overview?.supervisor?.version || 'N/A'}</p>
                      <p className="text-[11px] text-zinc-400 mt-0.5">
                        Channel: <span className="text-zinc-300 capitalize font-medium">{overview?.supervisor?.channel || 'stable'}</span>
                      </p>
                    </div>
                    <div className="pt-2 border-t border-zinc-800/80 text-[11px] text-zinc-400 flex items-center justify-between">
                      <span>Health: <span className="text-emerald-400 font-medium">{overview?.supervisor?.healthy ? 'Healthy' : 'Unhealthy'}</span></span>
                      <span>Supported: <span className="text-emerald-400 font-medium">{overview?.supervisor?.supported ? 'Yes' : 'No'}</span></span>
                    </div>
                  </div>

                  {/* Disk Storage Card */}
                  <div className="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-3">
                    <div className="flex items-center justify-between">
                      <span className="text-xs text-zinc-400 font-medium">Data Storage</span>
                      <HardDrive className="h-4 w-4 text-amber-400" />
                    </div>
                    <div>
                      <div className="flex items-baseline justify-between">
                        <p className="text-sm font-semibold text-zinc-100">{overview?.host?.diskUsedGb?.toFixed(1) || '0'} GB used</p>
                        <span className="text-xs text-zinc-500 font-mono">of {overview?.host?.diskTotalGb?.toFixed(1) || '0'} GB</span>
                      </div>
                      {/* Storage Bar */}
                      {overview?.host?.diskTotalGb && overview.host.diskTotalGb > 0 && (
                        <div className="w-full bg-zinc-800 rounded-full h-1.5 mt-2 overflow-hidden">
                          <div
                            className="bg-emerald-500 h-1.5 rounded-full"
                            style={{
                              width: `${Math.min(100, Math.round(((overview.host.diskUsedGb || 0) / overview.host.diskTotalGb) * 100))}%`,
                            }}
                          />
                        </div>
                      )}
                    </div>
                    <div className="pt-2 border-t border-zinc-800/80 text-[11px] text-zinc-400 flex items-center justify-between">
                      <span>Free Space:</span>
                      <span className="font-mono text-zinc-200 font-medium">{overview?.host?.diskFreeGb?.toFixed(1) || '0'} GB</span>
                    </div>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Tab Content: Backups */}
          {activeTab === 'backups' && (
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <h3 className="text-sm font-medium text-zinc-200">Appliance Backups</h3>
                  <p className="text-xs text-zinc-500">Stored tarball snapshot archives created by Home Assistant Supervisor</p>
                </div>
                <Button
                  size="sm"
                  onClick={handleCreateBackup}
                  disabled={createBackupMutation.isPending}
                  className="text-xs bg-emerald-600 hover:bg-emerald-500 text-white gap-1.5"
                >
                  <Archive className="h-3.5 w-3.5" />
                  New Full Backup
                </Button>
              </div>

              {isBackupsLoading ? (
                <div className="flex items-center justify-center p-12 text-zinc-500 text-xs">
                  <RefreshCw className="h-4 w-4 animate-spin mr-2" />
                  Loading backups...
                </div>
              ) : !backups || backups.length === 0 ? (
                <div className="p-8 text-center text-zinc-500 text-xs border border-dashed border-zinc-800 rounded-xl">
                  No backups found for this Home Assistant instance.
                </div>
              ) : (
                <div className="border border-zinc-800 rounded-2xl overflow-hidden bg-zinc-900/60">
                  <table className="w-full text-left text-xs">
                    <thead className="bg-zinc-950/60 border-b border-zinc-800 text-zinc-400">
                      <tr>
                        <th className="py-3 px-4 font-medium">Backup Name</th>
                        <th className="py-3 px-4 font-medium">Type</th>
                        <th className="py-3 px-4 font-medium">Created Date</th>
                        <th className="py-3 px-4 font-medium">Size</th>
                        <th className="py-3 px-4 font-medium text-right">Identifier</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-zinc-800/60">
                      {backups.map((b) => (
                        <tr key={b.slug} className="hover:bg-zinc-800/30 transition-colors">
                          <td className="py-3 px-4 font-medium text-zinc-200 flex items-center gap-2">
                            <Archive className="h-3.5 w-3.5 text-indigo-400 shrink-0" />
                            <span>{b.name}</span>
                            {b.protected && (
                              <Badge variant="purple" className="text-[10px] px-1 py-0">
                                Protected
                              </Badge>
                            )}
                          </td>
                          <td className="py-3 px-4">
                            <Badge variant={b.type === 'full' ? 'success' : 'default'} className="capitalize text-[10px]">
                              {b.type}
                            </Badge>
                          </td>
                          <td className="py-3 px-4 text-zinc-400 font-mono text-[11px]">
                            {new Date(b.date).toLocaleString()}
                          </td>
                          <td className="py-3 px-4 font-mono text-zinc-300">
                            {b.sizeMb.toFixed(1)} MB
                          </td>
                          <td className="py-3 px-4 text-right font-mono text-[11px] text-zinc-500">
                            {b.slug}
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

      {/* Edit / Add Modal */}
      <AddHomeAssistantModal
        open={modalOpen}
        onClose={() => {
          setModalOpen(false)
          setEditingInstance(null)
        }}
        initialInstance={editingInstance}
      />
    </div>
  )
}
