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
} from 'lucide-react'
import {
  useProxmoxInstances,
  useDeleteProxmoxInstance,
  useTestProxmoxInstance,
} from '../useAdapters'
import { AddProxmoxModal } from './AddProxmoxModal'
import type { ProxmoxInstanceDto } from '../../../api/adapters'
import type { ProxmoxProbeResult } from '../../../api/hosts'

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
          {instances.map((inst) => {
            const result = testResults[inst.id]
            const isTesting = testingId === inst.id

            return (
              <div
                key={inst.id}
                className="bg-zinc-900/60 border border-zinc-800/90 rounded-xl p-5 flex flex-col justify-between space-y-4 hover:border-zinc-700/80 transition-all shadow-lg"
              >
                {/* Card Top */}
                <div>
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
                        onClick={() => handleOpenEdit(inst)}
                        className="h-8 w-8 p-0 text-zinc-400 hover:text-zinc-100"
                        title="Edit Instance"
                      >
                        <Edit2 className="h-3.5 w-3.5" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => handleDelete(inst)}
                        className="h-8 w-8 p-0 text-zinc-400 hover:text-rose-400"
                        title="Delete Instance"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </Button>
                    </div>
                  </div>

                  {/* Metadata Chips */}
                  <div className="flex flex-wrap items-center gap-2 mt-4 text-[11px]">
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
                </div>

                {/* Inline Test Connection Result */}
                {result && (
                  <div
                    className={`p-3 rounded-lg border text-xs animate-in fade-in ${
                      result.success
                        ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-300'
                        : 'bg-rose-500/10 border-rose-500/20 text-rose-300'
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2">
                        {result.success ? (
                          <CheckCircle2 className="h-4 w-4 text-emerald-400" />
                        ) : (
                          <XCircle className="h-4 w-4 text-rose-400" />
                        )}
                        <span className="font-semibold">
                          {result.success ? 'Connected Successfully' : 'Connection Failed'}
                        </span>
                      </div>
                      {result.version && (
                        <Badge variant="success" className="text-[10px]">
                          PVE {result.version}
                        </Badge>
                      )}
                    </div>

                    {result.nodes && result.nodes.length > 0 && (
                      <div className="mt-2.5 pt-2 border-t border-emerald-500/20">
                        <span className="text-[11px] text-zinc-400 font-medium">Cluster Nodes ({result.nodes.length}):</span>
                        <div className="grid grid-cols-2 gap-2 mt-1.5">
                          {result.nodes.map((n) => (
                            <div
                              key={n.node}
                              className="p-1.5 bg-zinc-950/60 rounded border border-zinc-800/80 flex items-center justify-between text-[11px]"
                            >
                              <span className="font-mono text-zinc-200">{n.node}</span>
                              <Badge variant={n.status === 'online' ? 'success' : 'warning'} className="text-[9px] py-0">
                                {n.status}
                              </Badge>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}

                    {result.errorMessage && (
                      <p className="mt-1.5 text-[11px] text-rose-400/90">{result.errorMessage}</p>
                    )}
                  </div>
                )}

                {/* Card Action Footer */}
                <div className="pt-3 border-t border-zinc-800/80 flex items-center justify-between">
                  <span className="text-[11px] text-zinc-500">
                    Snapshots & Discovery Ready
                  </span>

                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => handleTestConnection(inst.id)}
                    isLoading={isTesting}
                    className="text-xs"
                  >
                    <Radio className="h-3.5 w-3.5 mr-1.5 text-orange-400" />
                    Test Connection
                  </Button>
                </div>
              </div>
            )
          })}
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
