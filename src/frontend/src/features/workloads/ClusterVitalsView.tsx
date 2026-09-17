import { useState } from 'react'
import {
  Cpu,
  RefreshCw,
  ExternalLink,
  Activity,
  Layers,
} from 'lucide-react'
import { Button } from '../../components/ui/button'
import { Badge } from '../../components/ui/badge'
import { Input } from '../../components/ui/input'
import { useClusterVitals } from './useWorkloads'

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
                className="p-4 rounded-xl border border-zinc-800/80 bg-zinc-900/60 space-y-3"
              >
                <div className="flex items-start justify-between">
                  <div>
                    <h4 className="text-sm font-bold font-mono text-zinc-100">{node.nodeName}</h4>
                    <div className="flex items-center gap-1.5 text-xs text-zinc-400 mt-0.5">
                      <span className="inline-block h-2 w-2 rounded-full bg-emerald-400" />
                      <span>Ready</span>
                    </div>
                  </div>

                  {/* Pressure Badges */}
                  <div className="flex flex-col items-end gap-1">
                    {node.diskPressure ? (
                      <span className="text-[10px] px-1.5 py-0.5 rounded bg-rose-950 border border-rose-800 text-rose-300 font-semibold">
                        Disk Pressure
                      </span>
                    ) : (
                      <span className="text-[10px] px-1.5 py-0.5 rounded bg-zinc-950 text-zinc-500 font-mono">
                        Disk: Normal
                      </span>
                    )}

                    {node.memoryPressure ? (
                      <span className="text-[10px] px-1.5 py-0.5 rounded bg-rose-950 border border-rose-800 text-rose-300 font-semibold">
                        Memory Pressure
                      </span>
                    ) : (
                      <span className="text-[10px] px-1.5 py-0.5 rounded bg-zinc-950 text-zinc-500 font-mono">
                        Mem: Normal
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
    </div>
  )
}
