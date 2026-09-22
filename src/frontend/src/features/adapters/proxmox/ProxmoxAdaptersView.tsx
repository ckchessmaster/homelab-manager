import { useState } from 'react'
import { Button } from '../../../components/ui/button'
import { Badge } from '../../../components/ui/badge'
import {
  Server,
  Plus,
  Radio,
  Edit2,
  Trash2,
  ShieldCheck,
  CheckCircle2,
  XCircle,
  RotateCw,
  ExternalLink,
  AlertCircle,
  Activity,
  Cpu,
  HardDrive,
  Clock,
} from 'lucide-react'
import {
  useProxmoxInstances,
  useDeleteProxmoxInstance,
  useTestProxmoxInstance,
  useProxmoxVitals,
} from '../useAdapters'
import { AddProxmoxModal } from './AddProxmoxModal'
import type { ProxmoxInstanceDto } from '../../../api/adapters'
import type { ProxmoxProbeResult } from '../../../api/hosts'

function formatBytes(bytes?: number | null): string {
  if (!bytes) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let val = bytes
  let i = 0
  while (val >= 1024 && i < units.length - 1) {
    val /= 1024
    i++
  }
  return `${val.toFixed(1)} ${units[i]}`
}

function formatUptime(seconds?: number | null): string {
  if (!seconds || seconds <= 0) return '—'
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor((seconds % 86400) / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  if (days > 0) return `${days}d ${hours}h`
  if (hours > 0) return `${hours}h ${minutes}m`
  return `${seconds}s`
}

export function ProxmoxAdaptersView() {
  const { data: instances, isLoading, refetch } = useProxmoxInstances()
  const deleteMutation = useDeleteProxmoxInstance()
  const testMutation = useTestProxmoxInstance()

  const [modalOpen, setModalOpen] = useState(false)
  const [editingInstance, setEditingInstance] = useState<ProxmoxInstanceDto | null>(null)

  // Map of instanceId -> latest ProxmoxProbeResult
  const [testResults, setTestResults] = useState<Record<string, ProxmoxProbeResult>>({})
  const [testingId, setTestingId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const handleOpenAdd = () => {
    setEditingInstance(null)
    setModalOpen(true)
  }

  const handleOpenEdit = (inst: ProxmoxInstanceDto) => {
    setEditingInstance(inst)
    setModalOpen(true)
  }

  const handleDelete = async (inst: ProxmoxInstanceDto) => {
    if (!window.confirm(`Are you sure you want to remove the Proxmox instance '${inst.name}' (${inst.id})?`)) {
      return
    }
    setActionError(null)
    try {
      await deleteMutation.mutateAsync(inst.id)
      const nextResults = { ...testResults }
      delete nextResults[inst.id]
      setTestResults(nextResults)
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Failed to delete instance.')
    }
  }

  const handleTestConnection = async (instId: string) => {
    setActionError(null)
    setTestingId(instId)
    try {
      const result = await testMutation.mutateAsync(instId)
      setTestResults((prev) => ({ ...prev, [instId]: result }))
    } catch (err) {
      setTestResults((prev) => ({
        ...prev,
        [instId]: {
          success: false,
          errorMessage: err instanceof Error ? err.message : 'Connection probe failed.',
        },
      }))
    } finally {
      setTestingId(null)
    }
  }

  return (
    <div className="space-y-6">
      {/* View Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 bg-zinc-900/60 border border-zinc-800 rounded-xl backdrop-blur-sm">
        <div>
          <div className="flex items-center gap-2">
            <h2 className="text-lg font-semibold text-zinc-100">Proxmox VE Hypervisors</h2>
            <Badge variant="purple">Multi-Cluster</Badge>
          </div>
          <p className="text-xs text-zinc-400 mt-1">
            Manage API credentials, cluster discovery, and safety snapshot coordination across multiple Proxmox VE environments.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => refetch()}
            disabled={isLoading}
            className="text-xs"
          >
            <RotateCw className={`h-3.5 w-3.5 mr-1.5 ${isLoading ? 'animate-spin' : ''}`} />
            Refresh
          </Button>

          <Button
            variant="primary"
            size="sm"
            onClick={handleOpenAdd}
            className="text-xs bg-emerald-600 hover:bg-emerald-500 text-white"
          >
            <Plus className="h-3.5 w-3.5 mr-1.5" />
            Add Proxmox Instance
          </Button>
        </div>
      </div>

      {actionError && (
        <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-lg text-xs text-rose-400 flex items-center gap-2">
          <AlertCircle className="h-4 w-4 shrink-0" />
          <span>{actionError}</span>
        </div>
      )}

      {/* Loading Skeleton */}
      {isLoading && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          {[1, 2].map((i) => (
            <div
              key={i}
              className="p-5 bg-zinc-900/40 border border-zinc-800/80 rounded-xl animate-pulse h-48"
            />
          ))}
        </div>
      )}

      {/* Empty State */}
      {!isLoading && (!instances || instances.length === 0) && (
        <div className="p-8 text-center bg-zinc-900/40 border border-dashed border-zinc-800 rounded-xl max-w-xl mx-auto space-y-4">
          <div className="p-3 bg-orange-500/10 border border-orange-500/20 rounded-full w-12 h-12 flex items-center justify-center mx-auto text-orange-400">
            <Server className="h-6 w-6" />
          </div>
          <div>
            <h3 className="text-sm font-semibold text-zinc-200">No Proxmox Instances Configured</h3>
            <p className="text-xs text-zinc-400 mt-1 max-w-sm mx-auto">
              Add your primary Proxmox VE cluster or standalone nodes to enable snapshot safety guarantees, guest IP resolution, and automatic VM discovery.
            </p>
          </div>
          <Button
            variant="primary"
            size="sm"
            onClick={handleOpenAdd}
            className="bg-emerald-600 hover:bg-emerald-500 text-white text-xs"
          >
            <Plus className="h-3.5 w-3.5 mr-1.5" />
            Add First Instance
          </Button>
        </div>
      )}

      {/* Instance Cards Grid */}
      {!isLoading && instances && instances.length > 0 && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
          {instances.map((inst) => (
            <ProxmoxInstanceCard
              key={inst.id}
              inst={inst}
              testResult={testResults[inst.id]}
              isTesting={testingId === inst.id}
              onEdit={handleOpenEdit}
              onDelete={handleDelete}
              onTest={handleTestConnection}
            />
          ))}
        </div>
      )}

      {/* Modal Dialog */}
      <AddProxmoxModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        initialInstance={editingInstance}
      />
    </div>
  )
}

interface ProxmoxInstanceCardProps {
  inst: ProxmoxInstanceDto
  testResult?: ProxmoxProbeResult
  isTesting: boolean
  onEdit: (inst: ProxmoxInstanceDto) => void
  onDelete: (inst: ProxmoxInstanceDto) => void
  onTest: (id: string) => void
}

function ProxmoxInstanceCard({
  inst,
  testResult,
  isTesting,
  onEdit,
  onDelete,
  onTest,
}: ProxmoxInstanceCardProps) {
  const { data: vitals } = useProxmoxVitals(inst.id)

  return (
    <div className="bg-zinc-900/60 border border-zinc-800/90 rounded-xl p-5 flex flex-col justify-between space-y-4 hover:border-zinc-700/80 transition-all shadow-lg">
      {/* Card Top */}
      <div className="space-y-4">
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-orange-500/10 border border-orange-500/20 rounded-lg text-orange-400">
              <Server className="h-5 w-5" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h3 className="text-base font-semibold text-zinc-100">{inst.name}</h3>
                <Badge variant="outline" className="text-[10px] font-mono">
                  {inst.id}
                </Badge>
              </div>
              <a
                href={inst.baseUrl}
                target="_blank"
                rel="noreferrer"
                className="text-xs text-zinc-400 hover:text-emerald-400 flex items-center gap-1 mt-0.5 transition-colors"
              >
                <span>{inst.baseUrl}</span>
                <ExternalLink className="h-3 w-3" />
              </a>
            </div>
          </div>

          <div className="flex items-center gap-1">
            <Button
              variant="ghost"
              size="sm"
              onClick={() => onEdit(inst)}
              className="h-8 w-8 p-0 text-zinc-400 hover:text-zinc-100"
              title="Edit Instance"
            >
              <Edit2 className="h-3.5 w-3.5" />
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => onDelete(inst)}
              className="h-8 w-8 p-0 text-zinc-400 hover:text-rose-400"
              title="Delete Instance"
            >
              <Trash2 className="h-3.5 w-3.5" />
            </Button>
          </div>
        </div>

        {/* Metadata Chips */}
        <div className="flex flex-wrap items-center gap-2 text-[11px]">
          <div className="flex items-center gap-1 text-zinc-400 bg-zinc-950/50 px-2 py-1 rounded border border-zinc-800">
            <ShieldCheck className="h-3 w-3 text-emerald-400" />
            <span className="font-mono text-zinc-300">{inst.apiTokenId}</span>
          </div>

          {inst.allowSelfSignedCert ? (
            <Badge variant="warning" className="text-[10px]">
              Self-Signed Allowed
            </Badge>
          ) : (
            <Badge variant="outline" className="text-[10px]">
              Strict TLS
            </Badge>
          )}

          {inst.updatedAt && (
            <span className="text-zinc-500 text-[10px] ml-auto">
              Updated {new Date(inst.updatedAt).toLocaleDateString()}
            </span>
          )}
        </div>

        {/* Live Cluster & Node Vitals */}
        {vitals && vitals.nodes && vitals.nodes.length > 0 && (
          <div className="p-3 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2.5">
            <div className="flex items-center justify-between text-xs">
              <span className="text-zinc-400 font-medium flex items-center gap-1.5">
                <Activity className="h-3.5 w-3.5 text-orange-400" />
                Cluster Node Vitals
              </span>
              <Badge
                variant={vitals.onlineNodes === vitals.totalNodes ? 'success' : 'warning'}
                className="text-[10px] font-mono py-0"
              >
                {vitals.onlineNodes}/{vitals.totalNodes} Online
              </Badge>
            </div>

            <div className="space-y-2">
              {vitals.nodes.map((n) => {
                const isOnline = n.status === 'online'
                const cpuPct = n.cpuUsagePct ?? 0
                const memPct = n.memoryUsagePct ?? 0

                return (
                  <div
                    key={n.node}
                    className="p-2.5 bg-zinc-900/60 rounded-lg border border-zinc-800/60 space-y-1.5 text-xs"
                  >
                    <div className="flex items-center justify-between">
                      <span className="font-mono font-medium text-zinc-200">{n.node}</span>
                      <div className="flex items-center gap-2">
                        {isOnline && n.uptimeSeconds != null && (
                          <span className="text-[10px] text-zinc-500 font-mono flex items-center gap-1">
                            <Clock className="h-2.5 w-2.5" />
                            up {formatUptime(n.uptimeSeconds)}
                          </span>
                        )}
                        <Badge
                          variant={isOnline ? 'success' : 'warning'}
                          className="text-[9px] py-0 font-mono"
                        >
                          {n.status}
                        </Badge>
                      </div>
                    </div>

                    {isOnline && (
                      <div className="grid grid-cols-2 gap-2 pt-1 border-t border-zinc-800/40 text-[11px]">
                        {/* CPU */}
                        <div className="space-y-1">
                          <div className="flex items-center justify-between text-[10px] text-zinc-400">
                            <span className="flex items-center gap-1">
                              <Cpu className="h-2.5 w-2.5 text-zinc-500" />
                              CPU
                            </span>
                            <span
                              className={`font-mono font-semibold ${
                                cpuPct > 90
                                  ? 'text-rose-400'
                                  : cpuPct > 75
                                  ? 'text-amber-400'
                                  : 'text-emerald-400'
                              }`}
                            >
                              {cpuPct.toFixed(1)}%
                            </span>
                          </div>
                          <div className="h-1 bg-zinc-800 rounded-full overflow-hidden">
                            <div
                              className={`h-full rounded-full ${
                                cpuPct > 90
                                  ? 'bg-rose-500'
                                  : cpuPct > 75
                                  ? 'bg-amber-500'
                                  : 'bg-emerald-500'
                              }`}
                              style={{ width: `${Math.min(100, Math.max(0, cpuPct))}%` }}
                            />
                          </div>
                        </div>

                        {/* RAM */}
                        <div className="space-y-1">
                          <div className="flex items-center justify-between text-[10px] text-zinc-400">
                            <span className="flex items-center gap-1">
                              <HardDrive className="h-2.5 w-2.5 text-zinc-500" />
                              RAM
                            </span>
                            <span
                              className={`font-mono font-semibold ${
                                memPct > 90
                                  ? 'text-rose-400'
                                  : memPct > 75
                                  ? 'text-amber-400'
                                  : 'text-emerald-400'
                              }`}
                              title={n.memoryMaxBytes ? `${formatBytes(n.memoryUsedBytes)} / ${formatBytes(n.memoryMaxBytes)}` : undefined}
                            >
                              {memPct.toFixed(0)}%{n.memoryMaxBytes ? ` (${formatBytes(n.memoryUsedBytes)})` : ''}
                            </span>
                          </div>
                          <div className="h-1 bg-zinc-800 rounded-full overflow-hidden">
                            <div
                              className={`h-full rounded-full ${
                                memPct > 90
                                  ? 'bg-rose-500'
                                  : memPct > 75
                                  ? 'bg-amber-500'
                                  : 'bg-emerald-500'
                              }`}
                              style={{ width: `${Math.min(100, Math.max(0, memPct))}%` }}
                            />
                          </div>
                        </div>
                      </div>
                    )}
                  </div>
                )
              })}
            </div>
          </div>
        )}

        {/* Inline Test Connection Result */}
        {testResult && (
          <div
            className={`p-3 rounded-lg border text-xs animate-in fade-in ${
              testResult.success
                ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-300'
                : 'bg-rose-500/10 border-rose-500/20 text-rose-300'
            }`}
          >
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                {testResult.success ? (
                  <CheckCircle2 className="h-4 w-4 text-emerald-400" />
                ) : (
                  <XCircle className="h-4 w-4 text-rose-400" />
                )}
                <span className="font-semibold">
                  {testResult.success ? 'Connected Successfully' : 'Connection Failed'}
                </span>
              </div>
              {testResult.version && (
                <Badge variant="success" className="text-[10px]">
                  PVE {testResult.version}
                </Badge>
              )}
            </div>

            {testResult.errorMessage && (
              <p className="mt-1.5 text-[11px] text-rose-400/90">{testResult.errorMessage}</p>
            )}
          </div>
        )}
      </div>

      {/* Card Action Footer */}
      <div className="pt-3 border-t border-zinc-800/80 flex items-center justify-between">
        <span className="text-[11px] text-zinc-500">
          Snapshots & Discovery Ready
        </span>

        <Button
          variant="outline"
          size="sm"
          onClick={() => onTest(inst.id)}
          isLoading={isTesting}
          className="text-xs"
        >
          <Radio className="h-3.5 w-3.5 mr-1.5 text-orange-400" />
          Test Connection
        </Button>
      </div>
    </div>
  )
}
