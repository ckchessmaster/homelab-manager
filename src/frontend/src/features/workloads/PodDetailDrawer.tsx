import { useState } from 'react'
import {
  X,
  Boxes,
  Cpu,
  RefreshCw,
  Copy,
  Check,
  CheckCircle2,
  AlertCircle,
  Clock,
} from 'lucide-react'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { useWorkloadPods } from './useWorkloads'
import type { WorkloadSummary } from '../../api/workloads'

interface PodDetailDrawerProps {
  workload: WorkloadSummary | null
  isOpen: boolean
  onClose: () => void
}

export function PodDetailDrawer({
  workload,
  isOpen,
  onClose,
}: PodDetailDrawerProps) {
  const [copiedPod, setCopiedPod] = useState<string | null>(null)

  const {
    data: pods,
    isLoading,
    isRefetching,
    refetch,
  } = useWorkloadPods(
    workload?.clusterId,
    workload?.namespace,
    workload?.name,
    isOpen
  )

  if (!isOpen || !workload) return null

  const handleCopy = (text: string) => {
    navigator.clipboard.writeText(text)
    setCopiedPod(text)
    setTimeout(() => setCopiedPod(null), 2000)
  }

  const runningCount = pods?.filter((p) => p.phase === 'Running').length ?? 0
  const totalRestarts = pods?.reduce((acc, p) => acc + p.restartCount, 0) ?? 0

  return (
    <>
      {/* Backdrop */}
      <div
        className="fixed inset-0 z-40 bg-black/60 backdrop-blur-xs animate-in fade-in"
        onClick={onClose}
      />

      {/* Slide-over Drawer */}
      <div className="fixed inset-y-0 right-0 z-50 w-full max-w-xl bg-zinc-950 border-l border-zinc-800 shadow-2xl flex flex-col animate-in slide-in-from-right duration-200">
        {/* Header */}
        <div className="flex items-center justify-between p-4 border-b border-zinc-800 bg-zinc-900/60 shrink-0">
          <div className="flex items-center gap-3 min-w-0">
            <div className="p-2 rounded-lg bg-sky-500/10 text-sky-400 border border-sky-500/20 shrink-0">
              <Boxes className="w-5 h-5" />
            </div>
            <div className="min-w-0">
              <div className="flex items-center gap-2 flex-wrap">
                <h2 className="text-base font-semibold text-zinc-100 truncate">
                  {workload.name}
                </h2>
                <Badge variant="info" className="text-xs">
                  {workload.clusterName}
                </Badge>
              </div>
              <div className="flex items-center gap-2 text-xs text-zinc-400 mt-0.5">
                <span>Namespace:</span>
                <span className="font-mono text-zinc-200">{workload.namespace}</span>
                <span>•</span>
                <span>{workload.readyReplicas} / {workload.desiredReplicas} Ready</span>
              </div>
            </div>
          </div>

          <div className="flex items-center gap-1 shrink-0">
            <Button
              variant="ghost"
              size="sm"
              onClick={() => refetch()}
              className="text-zinc-400 hover:text-zinc-100 h-8 w-8 p-0"
              title="Refresh pods"
            >
              <RefreshCw className={`w-4 h-4 ${isRefetching ? 'animate-spin' : ''}`} />
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={onClose}
              className="text-zinc-400 hover:text-zinc-100 h-8 w-8 p-0"
              title="Close drawer"
            >
              <X className="w-5 h-5" />
            </Button>
          </div>
        </div>

        {/* Quick Stats Bar */}
        <div className="grid grid-cols-3 gap-2 p-3 border-b border-zinc-800 bg-zinc-900/30 text-center text-xs">
          <div className="p-2 bg-zinc-950/60 rounded-lg border border-zinc-800/80">
            <span className="text-zinc-500 block text-[10px]">Total Pods</span>
            <span className="text-base font-bold font-mono text-zinc-100">
              {pods?.length ?? 0}
            </span>
          </div>
          <div className="p-2 bg-zinc-950/60 rounded-lg border border-zinc-800/80">
            <span className="text-zinc-500 block text-[10px]">Running</span>
            <span className="text-base font-bold font-mono text-emerald-400">
              {runningCount}
            </span>
          </div>
          <div className="p-2 bg-zinc-950/60 rounded-lg border border-zinc-800/80">
            <span className="text-zinc-500 block text-[10px]">Total Restarts</span>
            <span className={`text-base font-bold font-mono ${totalRestarts > 0 ? 'text-amber-400' : 'text-zinc-300'}`}>
              {totalRestarts}
            </span>
          </div>
        </div>

        {/* Pods List */}
        <div className="flex-1 overflow-y-auto p-4 space-y-3">
          {isLoading ? (
            <div className="p-12 text-center text-zinc-400 space-y-2">
              <RefreshCw className="h-6 w-6 animate-spin mx-auto text-sky-400" />
              <p className="text-xs">Fetching active pods from {workload.clusterName}...</p>
            </div>
          ) : !pods || pods.length === 0 ? (
            <div className="p-12 text-center rounded-xl border border-zinc-800 bg-zinc-900/40">
              <Boxes className="h-10 w-10 text-zinc-600 mx-auto mb-2" />
              <h4 className="text-sm font-medium text-zinc-300">No active pods</h4>
              <p className="text-xs text-zinc-500 mt-1">
                The deployment has no running pods or might be scaled down to 0.
              </p>
            </div>
          ) : (
            pods.map((pod) => {
              const isRunning = pod.phase === 'Running'
              return (
                <div
                  key={pod.name}
                  className="p-3.5 rounded-xl border border-zinc-800/90 bg-zinc-900/60 space-y-2 hover:border-zinc-700 transition-all shadow-sm"
                >
                  {/* Pod Title & Status */}
                  <div className="flex items-center justify-between gap-2">
                    <div className="flex items-center gap-1.5 min-w-0">
                      <span className="text-xs font-mono font-semibold text-zinc-200 truncate">
                        {pod.name}
                      </span>
                      <button
                        type="button"
                        onClick={() => handleCopy(pod.name)}
                        className="text-zinc-500 hover:text-zinc-300 p-0.5 rounded transition-colors cursor-pointer"
                        title="Copy pod name"
                      >
                        {copiedPod === pod.name ? (
                          <Check className="h-3 w-3 text-emerald-400" />
                        ) : (
                          <Copy className="h-3 w-3" />
                        )}
                      </button>
                    </div>

                    <Badge
                      variant={
                        isRunning
                          ? 'success'
                          : pod.phase === 'Pending'
                          ? 'warning'
                          : 'destructive'
                      }
                      dot
                      className="text-[11px]"
                    >
                      {pod.phase}
                    </Badge>
                  </div>

                  {/* Pod Attributes */}
                  <div className="grid grid-cols-2 gap-2 text-[11px] text-zinc-400 pt-1 border-t border-zinc-800/60 font-mono">
                    <div className="flex items-center gap-1 truncate">
                      <Cpu className="h-3 w-3 text-zinc-500 shrink-0" />
                      <span className="truncate">{pod.nodeName || 'Unassigned'}</span>
                    </div>

                    <div className="flex items-center justify-end gap-1">
                      <span className="text-zinc-500">IP:</span>
                      <span>{pod.podIp || 'None'}</span>
                    </div>

                    <div className="flex items-center gap-1">
                      {pod.isReady ? (
                        <CheckCircle2 className="h-3 w-3 text-emerald-400 shrink-0" />
                      ) : (
                        <AlertCircle className="h-3 w-3 text-amber-400 shrink-0" />
                      )}
                      <span>Ready: {pod.isReady ? 'Yes' : 'No'}</span>
                    </div>

                    <div className="flex items-center justify-end gap-1">
                      <span className="text-zinc-500">Restarts:</span>
                      <span className={pod.restartCount > 0 ? 'text-amber-400 font-bold' : ''}>
                        {pod.restartCount}
                      </span>
                    </div>
                  </div>

                  {pod.startTime && (
                    <div className="flex items-center gap-1 text-[10px] text-zinc-500 pt-0.5">
                      <Clock className="h-3 w-3 text-zinc-600" />
                      <span>Started {new Date(pod.startTime).toLocaleString()}</span>
                    </div>
                  )}
                </div>
              )
            })
          )}
        </div>
      </div>
    </>
  )
}
