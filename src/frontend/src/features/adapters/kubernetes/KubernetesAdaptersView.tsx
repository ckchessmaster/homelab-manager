import { useState } from 'react'
import {
  Layers,
  Plus,
  Radio,
  Trash2,
  Edit2,
  ShieldCheck,
  CheckCircle2,
  XCircle,
  AlertCircle,
  ExternalLink,
  Activity,
} from 'lucide-react'
import { Button } from '../../../components/ui/button'
import { Badge } from '../../../components/ui/badge'
import {
  useKubernetesClusters,
  useDeleteKubernetesCluster,
  useTestKubernetesClusterConnection,
  useKubernetesClusterVitals,
} from './useKubernetes'
import { AddKubernetesModal } from './AddKubernetesModal'
import { ClusterDetailDrawer } from './ClusterDetailDrawer'
import type {
  KubernetesClusterDto,
  KubernetesClusterTestResult,
} from '../../../api/kubernetes'

export function KubernetesAdaptersView() {
  const { data: clusters, isLoading, isError, error } = useKubernetesClusters()
  const deleteMutation = useDeleteKubernetesCluster()
  const testMutation = useTestKubernetesClusterConnection()

  const [modalOpen, setModalOpen] = useState(false)
  const [editingCluster, setEditingCluster] = useState<KubernetesClusterDto | null>(null)
  const [inspectingCluster, setInspectingCluster] = useState<KubernetesClusterDto | null>(null)
  const [testResults, setTestResults] = useState<Record<string, KubernetesClusterTestResult>>({})
  const [testingId, setTestingId] = useState<string | null>(null)

  const handleTestConnection = async (cluster: KubernetesClusterDto) => {
    setTestingId(cluster.id)
    try {
      const res = await testMutation.mutateAsync(cluster.id)
      setTestResults((prev) => ({ ...prev, [cluster.id]: res }))
    } catch (err: any) {
      setTestResults((prev) => ({
        ...prev,
        [cluster.id]: {
          success: false,
          nodeCount: 0,
          latencyMs: 0,
          message: err.message || 'Connection test failed',
        },
      }))
    } finally {
      setTestingId(null)
    }
  }

  const handleDelete = async (cluster: KubernetesClusterDto) => {
    if (confirm(`Are you sure you want to remove cluster '${cluster.name}'?`)) {
      try {
        await deleteMutation.mutateAsync(cluster.id)
      } catch (err: any) {
        alert(err.message || 'Failed to remove cluster')
      }
    }
  }

  return (
    <div className="space-y-6">
      {/* Top Header & Actions */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 bg-zinc-900/40 p-6 rounded-2xl border border-zinc-800/80 backdrop-blur-md">
        <div className="space-y-1">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-xl bg-sky-950/60 border border-sky-800/60 text-sky-400">
              <Layers className="h-5 w-5" />
            </div>
            <h2 className="text-lg font-bold text-zinc-100 tracking-tight">
              Kubernetes Clusters
            </h2>
            <Badge variant="purple" className="text-[10px] font-mono">
              Multi-Cluster Engine
            </Badge>
          </div>
          <p className="text-xs text-zinc-400 max-w-2xl">
            Agentless management for k3s, RKE2, Talos, and Kubernetes clusters. Monitor cluster health, perform zero-downtime node cordon & drain, and orchestrate application rollouts.
          </p>
        </div>

        <Button
          variant="primary"
          onClick={() => {
            setEditingCluster(null)
            setModalOpen(true)
          }}
          className="flex items-center gap-2 bg-sky-600 hover:bg-sky-500 text-white text-xs font-semibold px-4 py-2.5 rounded-xl shadow-lg shadow-sky-950/40 transition-all shrink-0"
        >
          <Plus className="h-4 w-4" />
          <span>Connect Cluster</span>
        </Button>
      </div>

      {/* Error state */}
      {isError && (
        <div className="p-4 rounded-xl bg-rose-950/30 border border-rose-800/50 text-rose-300 text-xs flex items-center gap-3">
          <AlertCircle className="h-4 w-4 shrink-0 text-rose-400" />
          <span>Failed to load Kubernetes cluster configurations: {(error as any)?.message || 'Unknown error'}</span>
        </div>
      )}

      {/* Clusters List */}
      {isLoading ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {[1, 2].map((i) => (
            <div
              key={i}
              className="h-48 rounded-2xl bg-zinc-900/30 border border-zinc-800/60 animate-pulse"
            />
          ))}
        </div>
      ) : !clusters || clusters.length === 0 ? (
        /* Empty State */
        <div className="text-center py-16 px-6 bg-zinc-900/20 rounded-2xl border border-dashed border-zinc-800 space-y-4">
          <div className="w-12 h-12 rounded-2xl bg-sky-950/40 border border-sky-800/40 text-sky-400 flex items-center justify-center mx-auto">
            <Layers className="h-6 w-6" />
          </div>
          <div className="space-y-1 max-w-sm mx-auto">
            <h3 className="text-sm font-semibold text-zinc-200">No Kubernetes Clusters Connected</h3>
            <p className="text-xs text-zinc-500">
              Connect your first Kubernetes cluster by uploading a kubeconfig YAML or entering your API server endpoint.
            </p>
          </div>
          <Button
            variant="primary"
            onClick={() => {
              setEditingCluster(null)
              setModalOpen(true)
            }}
            className="text-xs bg-sky-600 hover:bg-sky-500 text-white"
          >
            <Plus className="h-3.5 w-3.5 mr-1.5" />
            Connect Cluster
          </Button>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
          {clusters.map((cluster) => (
            <KubernetesClusterCard
              key={cluster.id}
              cluster={cluster}
              testResult={testResults[cluster.id]}
              isTesting={testingId === cluster.id}
              onEdit={(c) => {
                setEditingCluster(c)
                setModalOpen(true)
              }}
              onDelete={handleDelete}
              onTest={handleTestConnection}
              onInspect={setInspectingCluster}
            />
          ))}
        </div>
      )}

      {/* Add / Edit Cluster Modal */}
      <AddKubernetesModal
        open={modalOpen}
        onClose={() => {
          setModalOpen(false)
          setEditingCluster(null)
        }}
        initialCluster={editingCluster}
      />

      {/* Cluster Detail / Workload Drawer */}
      <ClusterDetailDrawer
        cluster={inspectingCluster}
        open={Boolean(inspectingCluster)}
        onClose={() => setInspectingCluster(null)}
      />
    </div>
  )
}

interface KubernetesClusterCardProps {
  cluster: KubernetesClusterDto
  testResult?: KubernetesClusterTestResult
  isTesting: boolean
  onEdit: (cluster: KubernetesClusterDto) => void
  onDelete: (cluster: KubernetesClusterDto) => void
  onTest: (cluster: KubernetesClusterDto) => void
  onInspect: (cluster: KubernetesClusterDto) => void
}

function KubernetesClusterCard({
  cluster,
  testResult,
  isTesting,
  onEdit,
  onDelete,
  onTest,
  onInspect,
}: KubernetesClusterCardProps) {
  const { data: vitals } = useKubernetesClusterVitals(cluster.id)

  return (
    <div className="flex flex-col justify-between p-5 rounded-2xl bg-zinc-900/40 border border-zinc-800/80 hover:border-zinc-700 transition-all shadow-lg backdrop-blur-sm group space-y-4">
      <div className="space-y-4">
        {/* Top info */}
        <div className="flex items-start justify-between gap-3">
          <div className="space-y-1">
            <div className="flex items-center gap-2">
              <h3 className="font-bold text-sm text-zinc-100 group-hover:text-sky-400 transition-colors">
                {cluster.name}
              </h3>
              <Badge variant="success" className="text-[10px] px-1.5 py-0 font-mono">
                Ready
              </Badge>
            </div>
            <p className="text-xs text-zinc-400 font-mono truncate max-w-[240px]">
              {cluster.apiServerUrl || (cluster.hasKubeConfig ? 'Kubeconfig File' : 'Default In-Cluster')}
            </p>
          </div>

          <div className="flex items-center gap-1 opacity-80 group-hover:opacity-100 transition-opacity">
            <button
              onClick={() => onEdit(cluster)}
              className="p-1.5 text-zinc-400 hover:text-zinc-100 hover:bg-zinc-800 rounded-lg transition-colors"
              title="Edit Cluster"
            >
              <Edit2 className="h-3.5 w-3.5" />
            </button>
            <button
              onClick={() => onDelete(cluster)}
              className="p-1.5 text-zinc-400 hover:text-rose-400 hover:bg-rose-950/40 rounded-lg transition-colors"
              title="Remove Cluster"
            >
              <Trash2 className="h-3.5 w-3.5" />
            </button>
          </div>
        </div>

        {/* Badges / Metrics */}
        <div className="flex flex-wrap items-center gap-2 text-xs">
          {cluster.contextName && (
            <Badge variant="default" className="text-[10px] font-mono text-zinc-400">
              ctx: {cluster.contextName}
            </Badge>
          )}
          {cluster.skipTlsVerify && (
            <span className="flex items-center gap-1 text-[11px] text-emerald-400 font-medium">
              <ShieldCheck className="h-3.5 w-3.5" />
              Insecure TLS
            </span>
          )}
        </div>

        {/* Cluster Vitals Strip */}
        {vitals && (
          <div className="p-3 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2.5">
            <div className="flex items-center justify-between text-xs">
              <span className="text-zinc-400 font-medium flex items-center gap-1.5">
                <Activity className="h-3.5 w-3.5 text-sky-400" />
                Cluster Vitals
              </span>
              <span className="text-[10px] font-mono text-zinc-500">
                {vitals.latencyMs}ms latency
              </span>
            </div>

            <div className="grid grid-cols-2 gap-2 text-xs">
              <div className="p-2 bg-zinc-900/50 rounded-lg border border-zinc-800/60 space-y-0.5">
                <span className="text-[10px] text-zinc-500 uppercase tracking-wider">Nodes</span>
                <div className="flex items-center gap-1.5">
                  <span className="font-mono font-semibold text-zinc-100">
                    {vitals.readyNodes} / {vitals.totalNodes}
                  </span>
                  <span
                    className={`h-1.5 w-1.5 rounded-full ${
                      vitals.readyNodes === vitals.totalNodes ? 'bg-emerald-400' : 'bg-amber-400'
                    }`}
                  />
                  <span className="text-[10px] text-zinc-400">Ready</span>
                </div>
              </div>

              <div className="p-2 bg-zinc-900/50 rounded-lg border border-zinc-800/60 space-y-0.5">
                <span className="text-[10px] text-zinc-500 uppercase tracking-wider">Pods</span>
                <div className="flex items-center gap-1.5">
                  <span className="font-mono font-semibold text-zinc-100">
                    {vitals.runningPods} / {vitals.totalPods}
                  </span>
                  <span className="text-[10px] text-zinc-400">Running</span>
                </div>
              </div>

              <div className="p-2 bg-zinc-900/50 rounded-lg border border-zinc-800/60 space-y-0.5">
                <span className="text-[10px] text-zinc-500 uppercase tracking-wider">Namespaces</span>
                <p className="font-mono font-semibold text-zinc-100">
                  {vitals.totalNamespaces}
                </p>
              </div>

              <div className="p-2 bg-zinc-900/50 rounded-lg border border-zinc-800/60 space-y-0.5">
                <span className="text-[10px] text-zinc-500 uppercase tracking-wider">Latency</span>
                <p className="font-mono font-semibold text-sky-400">
                  {vitals.latencyMs} ms
                </p>
              </div>
            </div>
          </div>
        )}

        {/* Test Connection Banner */}
        {testResult && (
          <div
            className={`p-2.5 rounded-xl border text-xs space-y-1 ${
              testResult.success
                ? 'bg-emerald-950/30 border-emerald-800/60 text-emerald-200'
                : 'bg-rose-950/30 border-rose-800/60 text-rose-200'
            }`}
          >
            <div className="flex items-center justify-between font-medium">
              <span className="flex items-center gap-1.5">
                {testResult.success ? (
                  <CheckCircle2 className="h-3.5 w-3.5 text-emerald-400" />
                ) : (
                  <XCircle className="h-3.5 w-3.5 text-rose-400" />
                )}
                {testResult.success ? 'API Online' : 'Failed'}
              </span>
              <span className="text-[10px] font-mono opacity-80">{testResult.latencyMs}ms</span>
            </div>
            {testResult.success ? (
              <div className="text-[11px] opacity-90">
                k8s {testResult.serverVersion} • {testResult.nodeCount} nodes
              </div>
            ) : (
              <p className="text-[11px] line-clamp-2 opacity-90">{testResult.message}</p>
            )}
          </div>
        )}
      </div>

      {/* Card Footer Actions */}
      <div className="pt-4 mt-4 border-t border-zinc-800/60 flex items-center justify-between gap-2">
        <Button
          variant="outline"
          onClick={() => onTest(cluster)}
          disabled={isTesting}
          className="text-xs h-8 px-2.5 border-zinc-700 hover:bg-zinc-800 text-zinc-300"
        >
          <Radio className={`h-3 w-3 mr-1.5 ${isTesting ? 'animate-spin text-sky-400' : 'text-zinc-400'}`} />
          <span>{isTesting ? 'Pinging...' : 'Test API'}</span>
        </Button>

        <Button
          variant="outline"
          onClick={() => onInspect(cluster)}
          className="text-xs h-8 px-3 border-sky-800/60 bg-sky-950/30 hover:bg-sky-900/40 text-sky-300 font-medium"
        >
          <ExternalLink className="h-3 w-3 mr-1.5" />
          <span>Manage Workloads</span>
        </Button>
      </div>
    </div>
  )
}
