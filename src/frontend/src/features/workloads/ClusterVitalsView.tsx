import { useState } from 'react'
import {
  Cpu,
  RefreshCw,
  ExternalLink,
  Activity,
  Layers,
  ShieldAlert,
  AlertTriangle,
  CheckCircle2,
  Loader2,
  Lock,
  Unlock,
  LogOut,
  ArrowUpCircle,
  Server,
  Box,
} from 'lucide-react'
import { Button } from '../../components/ui/button'
import { Badge } from '../../components/ui/badge'
import { Input } from '../../components/ui/input'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../components/ui/dialog'
import { useClusterVitals } from './useWorkloads'
import { useAuthUser } from '../auth/useAuthUser'
import {
  cordonClusterNode,
  uncordonClusterNode,
  drainClusterNode,
} from '../../api/kubernetes'

interface ClusterVitalsViewProps {
  clusterId: string
}

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 MB'
  const mib = bytes / (1024 * 1024)
  if (mib >= 1024) {
    return `${(mib / 1024).toFixed(1)} GB`
  }
  return `${mib.toFixed(0)} MB`
}

export function ClusterVitalsView({ clusterId }: ClusterVitalsViewProps) {
  const [grafanaUrl, setGrafanaUrl] = useState(() => localStorage.getItem('controlplane_grafana_url') || '')
  const [isEditingGrafana, setIsEditingGrafana] = useState(false)

  // Node Operations & Guarded Modal Confirmation
  const { isOperator } = useAuthUser()
  const [confirmAction, setConfirmAction] = useState<{
    type: 'cordon' | 'uncordon' | 'drain'
    nodeName: string
  } | null>(null)
  const [actionConfirmationTyped, setActionConfirmationTyped] = useState('')
  const [isSubmittingAction, setIsSubmittingAction] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [actionSuccessToast, setActionSuccessToast] = useState<string | null>(null)

  const {
    data: vitals,
    isLoading,
    refetch,
    isFetching,
  } = useClusterVitals(clusterId)

  const handleSaveGrafana = () => {
    localStorage.setItem('controlplane_grafana_url', grafanaUrl.trim())
    setIsEditingGrafana(false)
  }

  const handleExecuteNodeAction = async () => {
    if (!confirmAction) return
    setIsSubmittingAction(true)
    setActionError(null)
    try {
      if (confirmAction.type === 'cordon') {
        await cordonClusterNode(clusterId, confirmAction.nodeName)
        setActionSuccessToast(`Successfully cordoned node ${confirmAction.nodeName}`)
      } else if (confirmAction.type === 'uncordon') {
        await uncordonClusterNode(clusterId, confirmAction.nodeName)
        setActionSuccessToast(`Successfully uncordoned node ${confirmAction.nodeName}`)
      } else if (confirmAction.type === 'drain') {
        const res = await drainClusterNode(clusterId, confirmAction.nodeName)
        if (!res.success) {
          throw new Error(res.errorMessage || 'Drain failed on cluster node')
        }
        setActionSuccessToast(`Successfully drained ${res.evictedPodCount} pods from ${confirmAction.nodeName}`)
      }
      setConfirmAction(null)
      setActionConfirmationTyped('')
      refetch()
      setTimeout(() => setActionSuccessToast(null), 3500)
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Action failed')
    } finally {
      setIsSubmittingAction(false)
    }
  }

  const cpuUsedCores = ((vitals?.totalCpuUsageMillis || 0) / 1000).toFixed(2)
  const cpuAllocCores = ((vitals?.totalCpuAllocatableMillis || 0) / 1000).toFixed(1)
  const cpuPercent =
    (vitals?.totalCpuAllocatableMillis || 0) > 0
      ? Math.round(((vitals?.totalCpuUsageMillis || 0) / vitals!.totalCpuAllocatableMillis) * 100)
      : 0

  const memUsedBytes = vitals?.totalMemoryUsageBytes || 0
  const memAllocBytes = vitals?.totalMemoryAllocatableBytes || 0
  const memPercent =
    memAllocBytes > 0 ? Math.round((memUsedBytes / memAllocBytes) * 100) : 0

  const nodes = vitals?.nodes || []
  const topPods = vitals?.topPods || []

  return (
    <div className="space-y-6">
      {/* Top Controls & Grafana Banner */}
      <div className="flex flex-col md:flex-row gap-3 items-stretch md:items-center justify-between p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-md">
        <div className="flex items-center gap-3">
          <div className="h-9 w-9 rounded-lg bg-sky-950/80 border border-sky-800/80 flex items-center justify-center text-sky-400">
            <Activity className="h-5 w-5" />
          </div>
          <div>
            <h3 className="text-sm font-bold text-zinc-100 flex items-center gap-2">
              Cluster Vitals & Resource Metrics
              <Badge variant={vitals?.metricsServerAvailable ? 'success' : 'warning'} className="text-[10px]">
                {vitals?.metricsServerAvailable ? 'metrics-server Active' : 'metrics-server Offline'}
              </Badge>
            </h3>
            <p className="text-xs text-zinc-400">
              Real-time telemetry sourced via native Kubernetes Metrics API (`metrics.k8s.io`)
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          {grafanaUrl && !isEditingGrafana ? (
            <div className="flex items-center gap-1.5">
              <a
                href={grafanaUrl}
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-orange-950/40 border border-orange-800/60 text-orange-400 text-xs hover:bg-orange-950/80 transition-colors"
              >
                <span>Open in Grafana</span>
                <ExternalLink className="h-3 w-3" />
              </a>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setIsEditingGrafana(true)}
                className="h-7 text-[11px] text-zinc-400"
              >
                Edit URL
              </Button>
            </div>
          ) : isEditingGrafana ? (
            <div className="flex items-center gap-1.5">
              <Input
                placeholder="https://grafana.homelab.local"
                value={grafanaUrl}
                onChange={(e) => setGrafanaUrl(e.target.value)}
                className="h-7 text-xs bg-zinc-950/80 w-60"
              />
              <Button size="sm" onClick={handleSaveGrafana} className="h-7 text-xs">
                Save
              </Button>
            </div>
          ) : (
            <Button
              variant="outline"
              size="sm"
              onClick={() => setIsEditingGrafana(true)}
              className="text-xs h-8 text-zinc-400 gap-1"
            >
              <ExternalLink className="h-3.5 w-3.5" />
              Link Grafana
            </Button>
          )}

          <Button
            variant="outline"
            size="sm"
            onClick={() => refetch()}
            disabled={isFetching}
            className="gap-1.5 text-xs h-8"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} />
            Refresh
          </Button>
        </div>
      </div>

      {isLoading && (
        <div className="p-8 text-center border border-zinc-800 rounded-xl bg-zinc-900/30 text-xs text-zinc-400">
          <RefreshCw className="h-6 w-6 animate-spin mx-auto text-sky-400 mb-2" />
          Collecting cluster metrics and node vitals...
        </div>
      )}

      {/* Kubernetes Version & Cluster Overview */}
      {vitals?.serverVersion && (
        <div className="p-4 rounded-xl border border-zinc-800/80 bg-zinc-900/60 backdrop-blur-sm space-y-4">
          <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
            <div className="flex items-center gap-3">
              <div className="h-10 w-10 rounded-lg bg-sky-950/80 border border-sky-800/60 flex items-center justify-center text-sky-400 shrink-0">
                <Server className="h-5 w-5" />
              </div>
              <div>
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="text-sm font-bold text-zinc-100">Kubernetes</span>
                  <span className="font-mono text-sm font-bold text-sky-400">{vitals.serverVersion.gitVersion}</span>
                  {vitals.serverVersion.isOutdated ? (
                    <Badge variant="warning" className="text-[10px] uppercase px-1.5 py-0 font-mono gap-1">
                      <ArrowUpCircle className="h-2.5 w-2.5" />
                      {vitals.serverVersion.updateType || 'update'} available
                    </Badge>
                  ) : (
                    <Badge variant="success" className="text-[10px] uppercase px-1.5 py-0 font-mono gap-1">
                      <CheckCircle2 className="h-2.5 w-2.5" />
                      up to date
                    </Badge>
                  )}
                </div>
                <div className="flex items-center gap-3 mt-1 text-[11px] text-zinc-400">
                  {vitals.serverVersion.platform && (
                    <span className="font-mono">{vitals.serverVersion.platform}</span>
                  )}
                  <span>
                    {nodes.length} node{nodes.length !== 1 ? 's' : ''}
                  </span>
                </div>
              </div>
            </div>

            {vitals.serverVersion.isOutdated && vitals.serverVersion.latestStableVersion && (
              <div className="px-3 py-2 rounded-lg bg-amber-950/40 border border-amber-800/60 text-xs flex items-center gap-2.5 shrink-0">
                <ArrowUpCircle className="h-4 w-4 text-amber-400 shrink-0" />
                <div>
                  <span className="text-amber-200 font-semibold">Update Available</span>
                  <div className="text-amber-300/80 font-mono text-[11px] mt-0.5">
                    {vitals.serverVersion.gitVersion} &rarr;{' '}
                    <span className="text-emerald-300 font-semibold">{vitals.serverVersion.latestStableVersion}</span>
                  </div>
                </div>
              </div>
            )}
          </div>

          {/* Node Metadata Table */}
          {nodes.length > 0 && nodes.some(n => n.kubeletVersion) && (
            <div className="overflow-x-auto rounded-lg border border-zinc-800/60">
              <table className="w-full text-left text-[11px] text-zinc-300">
                <thead className="bg-zinc-950/70 text-[10px] uppercase tracking-wider text-zinc-500 border-b border-zinc-800/60">
                  <tr>
                    <th className="px-3 py-2">Node</th>
                    <th className="px-3 py-2">Kubelet</th>
                    <th className="px-3 py-2">OS Image</th>
                    <th className="px-3 py-2">Kernel</th>
                    <th className="px-3 py-2">Runtime</th>
                    <th className="px-3 py-2">Arch</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-800/40 font-mono">
                  {nodes.map((node) => (
                    <tr key={`meta-${node.nodeName}`} className="hover:bg-zinc-800/30 transition-colors">
                      <td className="px-3 py-2 font-semibold text-zinc-100">{node.nodeName}</td>
                      <td className="px-3 py-2">
                        <span className={
                          vitals.serverVersion && node.kubeletVersion && node.kubeletVersion !== vitals.serverVersion.gitVersion
                            ? 'text-amber-400'
                            : 'text-emerald-400'
                        }>
                          {node.kubeletVersion || '—'}
                        </span>
                      </td>
                      <td className="px-3 py-2 text-zinc-400">{node.osImage || '—'}</td>
                      <td className="px-3 py-2 text-zinc-400">{node.kernelVersion || '—'}</td>
                      <td className="px-3 py-2">
                        <span className="inline-flex items-center gap-1 text-zinc-300">
                          <Box className="h-3 w-3 text-sky-500 shrink-0" />
                          {node.containerRuntime || '—'}
                        </span>
                      </td>
                      <td className="px-3 py-2 text-zinc-400">{node.architecture || '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Cluster Total Utilization Gauges */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {/* CPU Gauge */}
        <div className="p-4 rounded-xl border border-zinc-800/80 bg-zinc-900/60 space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-zinc-400 flex items-center gap-1.5">
              <Cpu className="h-4 w-4 text-sky-400" />
              Cluster CPU Consumption
            </span>
            <span className="text-sm font-bold font-mono text-zinc-100">{cpuPercent}%</span>
          </div>

          <div className="h-2 w-full bg-zinc-800 rounded-full overflow-hidden">
            <div
              className={`h-full transition-all duration-500 ${
                cpuPercent > 85 ? 'bg-rose-500' : cpuPercent > 65 ? 'bg-amber-500' : 'bg-sky-500'
              }`}
              style={{ width: `${Math.min(100, Math.max(0, cpuPercent))}%` }}
            />
          </div>

          <div className="flex justify-between text-xs font-mono text-zinc-400">
            <span>Used: {cpuUsedCores} Cores</span>
            <span>Allocatable: {cpuAllocCores} Cores</span>
          </div>
        </div>

        {/* Memory Gauge */}
        <div className="p-4 rounded-xl border border-zinc-800/80 bg-zinc-900/60 space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-zinc-400 flex items-center gap-1.5">
              <Layers className="h-4 w-4 text-emerald-400" />
              Cluster Memory (RAM) Consumption
            </span>
            <span className="text-sm font-bold font-mono text-zinc-100">{memPercent}%</span>
          </div>

          <div className="h-2 w-full bg-zinc-800 rounded-full overflow-hidden">
            <div
              className={`h-full transition-all duration-500 ${
                memPercent > 85 ? 'bg-rose-500' : memPercent > 65 ? 'bg-amber-500' : 'bg-emerald-500'
              }`}
              style={{ width: `${Math.min(100, Math.max(0, memPercent))}%` }}
            />
          </div>

          <div className="flex justify-between text-xs font-mono text-zinc-400">
            <span>Used: {formatBytes(memUsedBytes)}</span>
            <span>Allocatable: {formatBytes(memAllocBytes)}</span>
          </div>
        </div>
      </div>

      {/* Node Vitals Grid */}
      <div className="space-y-3">
        <h3 className="text-xs font-bold text-zinc-300 uppercase tracking-wider flex items-center gap-2">
          <Cpu className="h-4 w-4 text-sky-400" />
          Node Pressure & Health Conditions
        </h3>

        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-3">
          {nodes.map((node) => {
            const nodeCpuPercent =
              node.cpuAllocatableMillis > 0
                ? Math.round((node.cpuUsageMillis / node.cpuAllocatableMillis) * 100)
                : 0
            const nodeMemPercent =
              node.memoryAllocatableBytes > 0
                ? Math.round((node.memoryUsageBytes / node.memoryAllocatableBytes) * 100)
                : 0

            return (
              <div
                key={node.nodeName}
                className={`p-4 rounded-xl border bg-zinc-900/60 space-y-3 transition-colors ${
                  node.unschedulable
                    ? 'border-purple-800/80 bg-purple-950/10'
                    : 'border-zinc-800/80'
                }`}
              >
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <div className="flex items-center gap-2 flex-wrap">
                      <h4 className="text-sm font-bold font-mono text-zinc-100">{node.nodeName}</h4>
                      {node.unschedulable && (
                        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10px] font-sans font-medium bg-purple-950/80 text-purple-300 border border-purple-800/80">
                          <ShieldAlert className="h-2.5 w-2.5 text-purple-400" />
                          Cordoned
                        </span>
                      )}
                    </div>
                    <div className="flex items-center gap-1.5 text-xs text-zinc-400 mt-1">
                      <span
                        className={`inline-block h-2 w-2 rounded-full ${
                          node.ready ? 'bg-emerald-400' : 'bg-rose-400'
                        }`}
                      />
                      <span className={node.ready ? 'text-zinc-300' : 'text-rose-400 font-semibold'}>
                        {node.ready ? 'Ready' : 'NotReady'}
                      </span>
                    </div>
                  </div>

                  {/* Condition Chips */}
                  <div className="flex flex-wrap items-center justify-end gap-1 max-w-[180px]">
                    {node.memoryPressure ? (
                      <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-rose-950 border border-rose-800 text-rose-300 text-[10px] font-semibold">
                        <AlertTriangle className="h-2.5 w-2.5" /> Mem Pressure
                      </span>
                    ) : (
                      <span className="text-[10px] px-1.5 py-0.5 rounded bg-zinc-950/70 border border-zinc-800/80 text-zinc-500 font-mono">
                        Mem: OK
                      </span>
                    )}

                    {node.diskPressure ? (
                      <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-rose-950 border border-rose-800 text-rose-300 text-[10px] font-semibold">
                        <AlertTriangle className="h-2.5 w-2.5" /> Disk Pressure
                      </span>
                    ) : (
                      <span className="text-[10px] px-1.5 py-0.5 rounded bg-zinc-950/70 border border-zinc-800/80 text-zinc-500 font-mono">
                        Disk: OK
                      </span>
                    )}

                    {node.pidPressure ? (
                      <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-rose-950 border border-rose-800 text-rose-300 text-[10px] font-semibold">
                        <AlertTriangle className="h-2.5 w-2.5" /> PID Pressure
                      </span>
                    ) : (
                      <span className="text-[10px] px-1.5 py-0.5 rounded bg-zinc-950/70 border border-zinc-800/80 text-zinc-500 font-mono">
                        PID: OK
                      </span>
                    )}
                  </div>
                </div>

                {/* Node Usage Bars */}
                <div className="space-y-2 pt-2 border-t border-zinc-800/60 text-xs font-mono">
                  <div>
                    <div className="flex justify-between text-zinc-400 mb-1">
                      <span>CPU:</span>
                      <span className="text-zinc-200 font-semibold">
                        {(node.cpuUsageMillis / 1000).toFixed(2)} / {(node.cpuAllocatableMillis / 1000).toFixed(1)} Cores ({nodeCpuPercent}%)
                      </span>
                    </div>
                    <div className="h-1.5 w-full bg-zinc-800 rounded-full overflow-hidden">
                      <div
                        className="h-full bg-sky-500"
                        style={{ width: `${Math.min(100, Math.max(0, nodeCpuPercent))}%` }}
                      />
                    </div>
                  </div>

                  <div>
                    <div className="flex justify-between text-zinc-400 mb-1">
                      <span>RAM:</span>
                      <span className="text-zinc-200 font-semibold">
                        {formatBytes(node.memoryUsageBytes)} / {formatBytes(node.memoryAllocatableBytes)} ({nodeMemPercent}%)
                      </span>
                    </div>
                    <div className="h-1.5 w-full bg-zinc-800 rounded-full overflow-hidden">
                      <div
                        className="h-full bg-emerald-500"
                        style={{ width: `${Math.min(100, Math.max(0, nodeMemPercent))}%` }}
                      />
                    </div>
                  </div>
                </div>

                {/* Node Operations (Operator-only) */}
                {isOperator && (
                  <div className="pt-2.5 border-t border-zinc-800/60 flex items-center justify-end gap-1.5">
                    {node.unschedulable ? (
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => {
                          setActionError(null)
                          setConfirmAction({ type: 'uncordon', nodeName: node.nodeName })
                        }}
                        className="h-7 px-2 text-xs border-purple-800/60 text-purple-300 hover:bg-purple-950/40 hover:text-purple-200 gap-1"
                        title="Mark node as schedulable"
                      >
                        <Unlock className="h-3 w-3" />
                        <span>Uncordon</span>
                      </Button>
                    ) : (
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => {
                          setActionError(null)
                          setConfirmAction({ type: 'cordon', nodeName: node.nodeName })
                        }}
                        className="h-7 px-2 text-xs border-zinc-700 text-zinc-300 hover:bg-zinc-800 hover:text-white gap-1"
                        title="Mark node as unschedulable"
                      >
                        <Lock className="h-3 w-3 text-amber-400" />
                        <span>Cordon</span>
                      </Button>
                    )}

                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => {
                        setActionError(null)
                        setActionConfirmationTyped('')
                        setConfirmAction({ type: 'drain', nodeName: node.nodeName })
                      }}
                      className="h-7 px-2 text-xs border-rose-900/60 text-rose-400 hover:bg-rose-950/40 hover:text-rose-300 gap-1"
                      title="Safely evict all workloads from node"
                    >
                      <LogOut className="h-3 w-3" />
                      <span>Drain</span>
                    </Button>
                  </div>
                )}
              </div>
            )
          })}
        </div>
      </div>

      {/* Top Consuming Pods Table */}
      {topPods.length > 0 && (
        <div className="space-y-3">
          <h3 className="text-xs font-bold text-zinc-300 uppercase tracking-wider flex items-center gap-2">
            <Activity className="h-4 w-4 text-emerald-400" />
            Top Resource-Consuming Pods
          </h3>

          <div className="overflow-x-auto rounded-xl border border-zinc-800/80 bg-zinc-900/60">
            <table className="w-full text-left text-xs text-zinc-300">
              <thead className="bg-zinc-950/80 text-[11px] uppercase tracking-wider text-zinc-400 border-b border-zinc-800">
                <tr>
                  <th className="p-3">Pod Name</th>
                  <th className="p-3">Namespace</th>
                  <th className="p-3">CPU Usage</th>
                  <th className="p-3">Memory Usage</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800/60 font-mono">
                {topPods.map((pod) => (
                  <tr key={`${pod.namespace}-${pod.podName}`} className="hover:bg-zinc-800/40 transition-colors">
                    <td className="p-3 font-semibold text-zinc-100">{pod.podName}</td>
                    <td className="p-3 text-amber-300">{pod.namespace}</td>
                    <td className="p-3 text-sky-400">{pod.cpuUsageMillis}m</td>
                    <td className="p-3 text-emerald-400">{formatBytes(pod.memoryUsageBytes)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Node Operation Guarded Confirmation Modal */}
      {confirmAction && (
        <Dialog
          open={true}
          onClose={() => {
            if (!isSubmittingAction) setConfirmAction(null)
          }}
          maxWidth="md"
        >
          <DialogHeader
            onClose={() => {
              if (!isSubmittingAction) setConfirmAction(null)
            }}
          >
            <div className="flex items-center gap-2">
              <AlertTriangle
                className={`h-5 w-5 shrink-0 ${
                  confirmAction.type === 'drain' ? 'text-rose-400' : 'text-amber-400'
                }`}
              />
              <DialogTitle className="text-zinc-100 font-bold">
                {confirmAction.type === 'drain'
                  ? `Drain Node: ${confirmAction.nodeName}`
                  : confirmAction.type === 'cordon'
                  ? `Cordon Node: ${confirmAction.nodeName}`
                  : `Uncordon Node: ${confirmAction.nodeName}`}
              </DialogTitle>
            </div>
          </DialogHeader>
          <DialogBody className="space-y-4">
            {actionError && (
              <div className="p-3 rounded-lg bg-rose-950/40 border border-rose-800 text-xs text-rose-300 flex items-center gap-2">
                <AlertTriangle className="h-4 w-4 shrink-0 text-rose-400" />
                <span>{actionError}</span>
              </div>
            )}

            {confirmAction.type === 'cordon' && (
              <p className="text-xs text-zinc-300 leading-relaxed">
                Marking <strong className="text-white font-mono">{confirmAction.nodeName}</strong> as unschedulable (cordon) will prevent Kubernetes from scheduling any new pods onto this node. Existing running pods will continue executing undisturbed.
              </p>
            )}

            {confirmAction.type === 'uncordon' && (
              <p className="text-xs text-zinc-300 leading-relaxed">
                Marking <strong className="text-white font-mono">{confirmAction.nodeName}</strong> as schedulable (uncordon) will allow Kubernetes to resume placing and balancing pods onto this node.
              </p>
            )}

            {confirmAction.type === 'drain' && (
              <div className="space-y-3">
                <div className="p-3 rounded-lg bg-rose-950/20 border border-rose-800/60 text-xs text-rose-300 leading-relaxed space-y-1.5">
                  <p className="font-semibold">Destructive Operation Warning:</p>
                  <p>
                    Draining cordons the node and safely evicts all running workloads (ignoring DaemonSets and purging emptyDir storage). Evicted pods will be rescheduled to other available nodes in the cluster.
                  </p>
                </div>
                <div className="space-y-1.5">
                  <label className="text-xs text-zinc-400">
                    Type <strong className="text-zinc-200 font-mono">{confirmAction.nodeName}</strong> to confirm:
                  </label>
                  <Input
                    value={actionConfirmationTyped}
                    onChange={(e) => setActionConfirmationTyped(e.target.value)}
                    placeholder={confirmAction.nodeName}
                    className="bg-zinc-950 font-mono text-xs"
                    autoFocus
                  />
                </div>
              </div>
            )}
          </DialogBody>
          <DialogFooter>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setConfirmAction(null)}
              disabled={isSubmittingAction}
            >
              Cancel
            </Button>
            <Button
              variant="primary"
              size="sm"
              onClick={handleExecuteNodeAction}
              disabled={
                isSubmittingAction ||
                (confirmAction.type === 'drain' && actionConfirmationTyped !== confirmAction.nodeName)
              }
              className={`font-semibold gap-1.5 ${
                confirmAction.type === 'drain'
                  ? 'bg-rose-600 hover:bg-rose-500 text-white'
                  : confirmAction.type === 'cordon'
                  ? 'bg-amber-600 hover:bg-amber-500 text-zinc-950 font-bold'
                  : 'bg-purple-600 hover:bg-purple-500 text-white'
              }`}
            >
              {isSubmittingAction ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : confirmAction.type === 'drain' ? (
                <LogOut className="h-4 w-4" />
              ) : confirmAction.type === 'cordon' ? (
                <Lock className="h-4 w-4" />
              ) : (
                <Unlock className="h-4 w-4" />
              )}
              <span>
                {confirmAction.type === 'drain'
                  ? 'Confirm Drain Node'
                  : confirmAction.type === 'cordon'
                  ? 'Confirm Cordon'
                  : 'Confirm Uncordon'}
              </span>
            </Button>
          </DialogFooter>
        </Dialog>
      )}

      {/* Action Success Toast */}
      {actionSuccessToast && (
        <div className="fixed bottom-5 right-5 z-50 px-4 py-2.5 rounded-xl bg-zinc-900 border border-emerald-500/50 shadow-2xl text-xs text-emerald-200 flex items-center gap-2 animate-in fade-in slide-in-from-bottom-2">
          <CheckCircle2 className="h-4 w-4 text-emerald-400 shrink-0" />
          <span>{actionSuccessToast}</span>
        </div>
      )}
    </div>
  )
}
