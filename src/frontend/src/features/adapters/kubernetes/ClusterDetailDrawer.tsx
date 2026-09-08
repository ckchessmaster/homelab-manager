import React, { useState } from 'react'
import {
  X,
  Layers,
  Server,
  Box,
  RotateCw,
  Sliders,
  ShieldAlert,
  CheckCircle2,
  XCircle,
  AlertTriangle,
  Play,
  Cpu,
} from 'lucide-react'
import { Button } from '../../../components/ui/button'
import { Badge } from '../../../components/ui/badge'
import { Input } from '../../../components/ui/input'
import {
  useClusterNodes,
  useClusterDeployments,
  useClusterNamespaces,
  useClusterPods,
  useCordonClusterNode,
  useUncordonClusterNode,
  useDrainClusterNode,
  useRestartClusterDeployment,
  useScaleClusterDeployment,
} from './useKubernetes'
import type { KubernetesClusterDto } from '../../../api/kubernetes'

interface ClusterDetailDrawerProps {
  cluster: KubernetesClusterDto | null
  open: boolean
  onClose: () => void
}

type TabType = 'nodes' | 'deployments' | 'pods'

export function ClusterDetailDrawer({
  cluster,
  open,
  onClose,
}: ClusterDetailDrawerProps) {
  const [activeTab, setActiveTab] = useState<TabType>('nodes')
  const [selectedNamespace, setSelectedNamespace] = useState<string>('')
  const [scalingDeployment, setScalingDeployment] = useState<{ namespace: string; name: string; replicas: number } | null>(null)
  const [drainingNode, setDrainingNode] = useState<string | null>(null)
  const [actionMessage, setActionMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)

  const clusterId = open && cluster ? cluster.id : null

  const { data: nodes, isLoading: loadingNodes } = useClusterNodes(clusterId)
  const { data: namespaces } = useClusterNamespaces(clusterId)
  const { data: deployments, isLoading: loadingDeployments } = useClusterDeployments(clusterId, selectedNamespace || undefined)
  const { data: pods, isLoading: loadingPods } = useClusterPods(clusterId, selectedNamespace || undefined)

  const cordonMutation = useCordonClusterNode()
  const uncordonMutation = useUncordonClusterNode()
  const drainMutation = useDrainClusterNode()
  const restartMutation = useRestartClusterDeployment()
  const scaleMutation = useScaleClusterDeployment()

  if (!open || !cluster) return null

  const handleCordon = async (nodeName: string) => {
    setActionMessage(null)
    try {
      await cordonMutation.mutateAsync({ clusterId: cluster.id, nodeName })
      setActionMessage({ type: 'success', text: `Node '${nodeName}' cordoned.` })
    } catch (err: any) {
      setActionMessage({ type: 'error', text: err.message || `Failed to cordon node ${nodeName}` })
    }
  }

  const handleUncordon = async (nodeName: string) => {
    setActionMessage(null)
    try {
      await uncordonMutation.mutateAsync({ clusterId: cluster.id, nodeName })
      setActionMessage({ type: 'success', text: `Node '${nodeName}' uncordoned.` })
    } catch (err: any) {
      setActionMessage({ type: 'error', text: err.message || `Failed to uncordon node ${nodeName}` })
    }
  }

  const handleDrain = async (nodeName: string) => {
    setActionMessage(null)
    setDrainingNode(nodeName)
    try {
      const res = await drainMutation.mutateAsync({ clusterId: cluster.id, nodeName, timeoutSeconds: 180 })
      if (res.success) {
        setActionMessage({ type: 'success', text: `Node '${nodeName}' drained successfully (${res.evictedPodCount} pods evicted).` })
      } else {
        setActionMessage({ type: 'error', text: res.errorMessage || `Failed to drain node ${nodeName}.` })
      }
    } catch (err: any) {
      setActionMessage({ type: 'error', text: err.message || `Failed to drain node ${nodeName}` })
    } finally {
      setDrainingNode(null)
    }
  }

  const handleRestartDeployment = async (namespace: string, name: string) => {
    setActionMessage(null)
    try {
      await restartMutation.mutateAsync({ clusterId: cluster.id, namespace, name })
      setActionMessage({ type: 'success', text: `Rollout restart initiated for '${namespace}/${name}'.` })
    } catch (err: any) {
      setActionMessage({ type: 'error', text: err.message || `Failed to restart ${name}` })
    }
  }

  const handleScaleDeployment = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!scalingDeployment) return
    setActionMessage(null)
    try {
      await scaleMutation.mutateAsync({
        clusterId: cluster.id,
        namespace: scalingDeployment.namespace,
        name: scalingDeployment.name,
        replicas: scalingDeployment.replicas,
      })
      setActionMessage({
        type: 'success',
        text: `Scaled '${scalingDeployment.namespace}/${scalingDeployment.name}' to ${scalingDeployment.replicas} replicas.`,
      })
      setScalingDeployment(null)
    } catch (err: any) {
      setActionMessage({ type: 'error', text: err.message || 'Failed to scale deployment' })
    }
  }

  return (
    <div className="fixed inset-0 z-50 overflow-hidden bg-black/60 backdrop-blur-sm flex justify-end animate-in fade-in duration-200">
      <div className="w-full max-w-4xl bg-zinc-950 border-l border-zinc-800 h-full flex flex-col shadow-2xl">
        {/* Header */}
        <div className="p-6 border-b border-zinc-800 flex items-center justify-between shrink-0 bg-zinc-900/40">
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-xl bg-sky-950/60 border border-sky-800/60 text-sky-400">
              <Layers className="h-6 w-6" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-lg font-bold text-zinc-100">{cluster.name}</h2>
                <Badge variant="purple" className="text-[10px] uppercase font-mono">
                  Kubernetes
                </Badge>
              </div>
              <p className="text-xs text-zinc-400 font-mono mt-0.5">
                {cluster.apiServerUrl || 'In-Cluster / Kubeconfig Direct'}
              </p>
            </div>
          </div>

          <button
            onClick={onClose}
            className="p-2 text-zinc-400 hover:text-zinc-100 hover:bg-zinc-800 rounded-lg transition-colors"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Action feedback message */}
        {actionMessage && (
          <div
            className={`mx-6 mt-4 p-3 rounded-xl border text-xs flex items-center justify-between ${
              actionMessage.type === 'success'
                ? 'bg-emerald-950/40 border-emerald-800/60 text-emerald-300'
                : 'bg-rose-950/40 border-rose-800/60 text-rose-300'
            }`}
          >
            <span>{actionMessage.text}</span>
            <button
              onClick={() => setActionMessage(null)}
              className="text-xs opacity-70 hover:opacity-100 ml-3"
            >
              ✕
            </button>
          </div>
        )}

        {/* Tab Switcher & Namespace Filter */}
        <div className="px-6 pt-4 border-b border-zinc-800 flex items-center justify-between gap-4">
          <div className="flex items-center gap-2">
            <button
              onClick={() => setActiveTab('nodes')}
              className={`flex items-center gap-2 px-3.5 py-2 rounded-t-lg text-xs font-medium border-b-2 transition-all ${
                activeTab === 'nodes'
                  ? 'border-sky-500 text-sky-400 bg-sky-950/20'
                  : 'border-transparent text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <Server className="h-4 w-4" />
              <span>Nodes ({nodes?.length || 0})</span>
            </button>

            <button
              onClick={() => setActiveTab('deployments')}
              className={`flex items-center gap-2 px-3.5 py-2 rounded-t-lg text-xs font-medium border-b-2 transition-all ${
                activeTab === 'deployments'
                  ? 'border-sky-500 text-sky-400 bg-sky-950/20'
                  : 'border-transparent text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <Box className="h-4 w-4" />
              <span>Deployments ({deployments?.length || 0})</span>
            </button>

            <button
              onClick={() => setActiveTab('pods')}
              className={`flex items-center gap-2 px-3.5 py-2 rounded-t-lg text-xs font-medium border-b-2 transition-all ${
                activeTab === 'pods'
                  ? 'border-sky-500 text-sky-400 bg-sky-950/20'
                  : 'border-transparent text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <Cpu className="h-4 w-4" />
              <span>Pods ({pods?.length || 0})</span>
            </button>
          </div>

          {namespaces && namespaces.length > 0 && (
            <div className="flex items-center gap-2 pb-2">
              <span className="text-[11px] text-zinc-400">Namespace:</span>
              <select
                value={selectedNamespace}
                onChange={(e) => setSelectedNamespace(e.target.value)}
                className="bg-zinc-900 border border-zinc-700/60 rounded-lg px-2.5 py-1 text-xs text-zinc-200 focus:outline-none focus:ring-1 focus:ring-sky-500"
              >
                <option value="">All Namespaces</option>
                {namespaces.map((ns) => (
                  <option key={ns} value={ns}>
                    {ns}
                  </option>
                ))}
              </select>
            </div>
          )}
        </div>

        {/* Tab Body */}
        <div className="flex-1 overflow-y-auto p-6 space-y-4">
          {/* --- NODES TAB --- */}
          {activeTab === 'nodes' && (
            <div className="space-y-3">
              {loadingNodes ? (
                <div className="p-8 text-center text-xs text-zinc-500 animate-pulse">
                  Querying cluster nodes from Kubernetes API...
                </div>
              ) : !nodes || nodes.length === 0 ? (
                <div className="p-8 text-center text-xs text-zinc-500 bg-zinc-900/30 rounded-xl border border-zinc-800">
                  No nodes returned from this cluster.
                </div>
              ) : (
                nodes.map((node) => (
                  <div
                    key={node.name}
                    className="p-4 rounded-xl bg-zinc-900/50 border border-zinc-800 hover:border-zinc-700/80 transition-all flex flex-col md:flex-row md:items-center justify-between gap-4"
                  >
                    <div className="space-y-1.5">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className="font-semibold text-sm text-zinc-100">{node.name}</span>
                        {node.isReady ? (
                          <Badge variant="success" className="text-[10px] px-1.5 py-0 flex items-center gap-1">
                            <CheckCircle2 className="h-3 w-3" />
                            Ready
                          </Badge>
                        ) : (
                          <Badge variant="destructive" className="text-[10px] px-1.5 py-0 flex items-center gap-1">
                            <XCircle className="h-3 w-3" />
                            Not Ready
                          </Badge>
                        )}

                        {node.unschedulable && (
                          <Badge variant="warning" className="text-[10px] px-1.5 py-0 flex items-center gap-1">
                            <ShieldAlert className="h-3 w-3" />
                            Cordoned
                          </Badge>
                        )}

                        {node.roles.map((r) => (
                          <Badge key={r} variant="default" className="text-[10px] px-1.5 py-0">
                            {r}
                          </Badge>
                        ))}
                      </div>

                      <div className="flex items-center gap-4 text-xs text-zinc-400 font-mono flex-wrap">
                        <span>IP: <strong className="text-zinc-300">{node.internalIp || 'N/A'}</strong></span>
                        <span>OS: <strong className="text-zinc-300">{node.osImage || 'Linux'}</strong></span>
                        <span>Runtime: <strong className="text-zinc-300">{node.containerRuntimeVersion || 'containerd'}</strong></span>
                      </div>
                    </div>

                    {/* Node Administrative Actions */}
                    <div className="flex items-center gap-2 shrink-0">
                      {node.unschedulable ? (
                        <Button
                          variant="outline"
                          onClick={() => handleUncordon(node.name)}
                          disabled={uncordonMutation.isPending}
                          className="text-xs border-zinc-700 hover:bg-zinc-800 text-emerald-400"
                        >
                          <Play className="h-3 w-3 mr-1" />
                          Uncordon
                        </Button>
                      ) : (
                        <Button
                          variant="outline"
                          onClick={() => handleCordon(node.name)}
                          disabled={cordonMutation.isPending}
                          className="text-xs border-zinc-700 hover:bg-zinc-800 text-amber-400"
                        >
                          <ShieldAlert className="h-3 w-3 mr-1" />
                          Cordon
                        </Button>
                      )}

                      <Button
                        variant="destructive"
                        onClick={() => handleDrain(node.name)}
                        disabled={drainMutation.isPending || drainingNode === node.name}
                        className="text-xs bg-rose-950/80 hover:bg-rose-900 border border-rose-800 text-rose-200"
                      >
                        <AlertTriangle className="h-3 w-3 mr-1" />
                        {drainingNode === node.name ? 'Draining...' : 'Drain Node'}
                      </Button>
                    </div>
                  </div>
                ))
              )}
            </div>
          )}

          {/* --- DEPLOYMENTS TAB --- */}
          {activeTab === 'deployments' && (
            <div className="space-y-3">
              {loadingDeployments ? (
                <div className="p-8 text-center text-xs text-zinc-500 animate-pulse">
                  Querying deployments...
                </div>
              ) : !deployments || deployments.length === 0 ? (
                <div className="p-8 text-center text-xs text-zinc-500 bg-zinc-900/30 rounded-xl border border-zinc-800">
                  No deployments found in this namespace.
                </div>
              ) : (
                deployments.map((dep) => {
                  const isHealthy = dep.readyReplicas === dep.desiredReplicas && dep.desiredReplicas > 0
                  return (
                    <div
                      key={`${dep.namespace}/${dep.name}`}
                      className="p-4 rounded-xl bg-zinc-900/50 border border-zinc-800 hover:border-zinc-700/80 transition-all flex flex-col md:flex-row md:items-center justify-between gap-4"
                    >
                      <div className="space-y-1.5">
                        <div className="flex items-center gap-2 flex-wrap">
                          <span className="font-semibold text-sm text-zinc-100">{dep.name}</span>
                          <Badge variant="default" className="text-[10px] font-mono">
                            ns: {dep.namespace}
                          </Badge>
                          <Badge
                            variant={isHealthy ? 'success' : dep.readyReplicas > 0 ? 'warning' : 'destructive'}
                            className="text-[10px] font-mono px-1.5 py-0"
                          >
                            {dep.readyReplicas}/{dep.desiredReplicas} Ready
                          </Badge>
                        </div>

                        <div className="text-xs text-zinc-400 font-mono">
                          Image: <span className="text-zinc-300">{dep.images.join(', ') || 'N/A'}</span>
                        </div>
                      </div>

                      <div className="flex items-center gap-2 shrink-0">
                        <Button
                          variant="outline"
                          onClick={() =>
                            setScalingDeployment({
                              namespace: dep.namespace,
                              name: dep.name,
                              replicas: dep.desiredReplicas,
                            })
                          }
                          className="text-xs border-zinc-700 hover:bg-zinc-800 text-sky-400"
                        >
                          <Sliders className="h-3 w-3 mr-1" />
                          Scale
                        </Button>

                        <Button
                          variant="outline"
                          onClick={() => handleRestartDeployment(dep.namespace, dep.name)}
                          disabled={restartMutation.isPending}
                          className="text-xs border-zinc-700 hover:bg-zinc-800 text-zinc-300"
                        >
                          <RotateCw className="h-3 w-3 mr-1" />
                          Restart Rollout
                        </Button>
                      </div>
                    </div>
                  )
                })
              )}
            </div>
          )}

          {/* --- PODS TAB --- */}
          {activeTab === 'pods' && (
            <div className="space-y-2">
              {loadingPods ? (
                <div className="p-8 text-center text-xs text-zinc-500 animate-pulse">
                  Querying pods...
                </div>
              ) : !pods || pods.length === 0 ? (
                <div className="p-8 text-center text-xs text-zinc-500 bg-zinc-900/30 rounded-xl border border-zinc-800">
                  No pods found.
                </div>
              ) : (
                pods.map((pod) => (
                  <div
                    key={`${pod.namespace}/${pod.name}`}
                    className="p-3 rounded-lg bg-zinc-900/40 border border-zinc-800/80 flex items-center justify-between text-xs font-mono"
                  >
                    <div className="space-y-0.5">
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-zinc-200">{pod.name}</span>
                        <Badge
                          variant={pod.phase === 'Running' ? 'success' : pod.phase === 'Pending' ? 'warning' : 'destructive'}
                          className="text-[9px] px-1 py-0 font-sans"
                        >
                          {pod.phase}
                        </Badge>
                        <span className="text-zinc-500 text-[10px]">ns: {pod.namespace}</span>
                      </div>
                      <div className="text-[11px] text-zinc-400 flex items-center gap-3">
                        <span>Node: <strong className="text-zinc-300">{pod.nodeName || 'Pending'}</strong></span>
                        <span>IP: <strong className="text-zinc-300">{pod.podIp || 'N/A'}</strong></span>
                        <span>Restarts: <strong className="text-zinc-300">{pod.restartCount}</strong></span>
                      </div>
                    </div>
                  </div>
                ))
              )}
            </div>
          )}
        </div>

        {/* Scale Deployment Modal Dialog */}
        {scalingDeployment && (
          <div className="fixed inset-0 z-60 bg-black/70 flex items-center justify-center p-4">
            <div className="bg-zinc-900 border border-zinc-800 rounded-xl max-w-sm w-full p-5 space-y-4 shadow-2xl">
              <div>
                <h3 className="text-sm font-bold text-zinc-100 flex items-center gap-2">
                  <Sliders className="h-4 w-4 text-sky-400" />
                  Scale Deployment
                </h3>
                <p className="text-xs text-zinc-400 font-mono mt-0.5">
                  {scalingDeployment.namespace}/{scalingDeployment.name}
                </p>
              </div>

              <form onSubmit={handleScaleDeployment} className="space-y-4">
                <div>
                  <label className="block text-xs text-zinc-300 mb-1 font-medium">Desired Replicas</label>
                  <Input
                    type="number"
                    min={0}
                    max={100}
                    value={scalingDeployment.replicas}
                    onChange={(e) =>
                      setScalingDeployment({
                        ...scalingDeployment,
                        replicas: parseInt(e.target.value, 10) || 0,
                      })
                    }
                    className="font-mono text-sm"
                    autoFocus
                  />
                </div>

                <div className="flex items-center justify-end gap-2 pt-2 border-t border-zinc-800">
                  <Button
                    type="button"
                    variant="ghost"
                    onClick={() => setScalingDeployment(null)}
                    className="text-xs"
                  >
                    Cancel
                  </Button>
                  <Button
                    type="submit"
                    variant="primary"
                    disabled={scaleMutation.isPending}
                    className="text-xs bg-sky-600 hover:bg-sky-500 text-white"
                  >
                    {scaleMutation.isPending ? 'Scaling...' : 'Apply Replicas'}
                  </Button>
                </div>
              </form>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
