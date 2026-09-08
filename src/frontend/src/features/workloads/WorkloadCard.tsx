import { useState } from 'react'
import {
  Boxes,
  RotateCcw,
  Sliders,
  CheckCircle2,
  Clock,
  Loader2,
  Tag,
} from 'lucide-react'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { useRestartWorkload } from './useWorkloads'
import type { WorkloadSummary } from '../../api/workloads'

interface WorkloadCardProps {
  workload: WorkloadSummary
  onScale: (workload: WorkloadSummary) => void
  onOpenPods: (workload: WorkloadSummary) => void
  isOperator: boolean
}

export function WorkloadCard({
  workload,
  onScale,
  onOpenPods,
  isOperator,
}: WorkloadCardProps) {
  const restartMutation = useRestartWorkload()
  const [restartSuccess, setRestartSuccess] = useState(false)

  const handleRestart = async (e: React.MouseEvent) => {
    e.stopPropagation()
    if (!confirm(`Are you sure you want to trigger a rollout restart for '${workload.name}'?`)) {
      return
    }

    try {
      await restartMutation.mutateAsync({
        clusterId: workload.clusterId,
        namespaceName: workload.namespace,
        name: workload.name,
      })
      setRestartSuccess(true)
      setTimeout(() => setRestartSuccess(false), 2500)
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to trigger rollout restart')
    }
  }

  const isHealthy = workload.status === 'Ready'
  const isScaledDown = workload.status === 'ScaledDown'
  const isDegraded = workload.status === 'Degraded'

  const percentReady =
    workload.desiredReplicas > 0
      ? Math.round((workload.readyReplicas / workload.desiredReplicas) * 100)
      : isScaledDown
      ? 100
      : 0

  return (
    <div className="flex flex-col justify-between p-4 rounded-2xl border border-zinc-800/90 bg-zinc-900/60 hover:bg-zinc-800/50 hover:border-zinc-700/80 transition-all shadow-md group">
      {/* Top Section */}
      <div className="space-y-3">
        {/* Header: Title & Badges */}
        <div className="flex items-start justify-between gap-2">
          <div className="flex items-center gap-2.5 min-w-0">
            <div className="h-8 w-8 rounded-lg bg-sky-950/60 border border-sky-800/60 flex items-center justify-center shrink-0">
              <Boxes className="h-4 w-4 text-sky-400" />
            </div>
            <div className="min-w-0">
              <h3 className="text-sm font-bold text-zinc-100 truncate group-hover:text-sky-300 transition-colors">
                {workload.name}
              </h3>
              <div className="flex items-center gap-1.5 text-xs text-zinc-400 mt-0.5">
                <span className="font-mono text-zinc-300">{workload.namespace}</span>
                <span>•</span>
                <span className="text-zinc-500">{workload.clusterName}</span>
              </div>
            </div>
          </div>

          <Badge
            variant={
              isHealthy
                ? 'success'
                : isScaledDown
                ? 'default'
                : isDegraded
                ? 'destructive'
                : 'warning'
            }
            dot
            className="text-[11px] shrink-0"
          >
            {workload.status}
          </Badge>
        </div>

        {/* Replica Progress & Ratio */}
        <div className="space-y-1.5 p-3 rounded-xl bg-zinc-950/60 border border-zinc-800/80">
          <div className="flex items-center justify-between text-xs">
            <span className="text-zinc-400">Replicas:</span>
            <span className="font-mono font-semibold text-zinc-200">
              {workload.readyReplicas} / {workload.desiredReplicas} Ready
            </span>
          </div>

          {/* Mini progress bar */}
          <div className="h-1.5 w-full bg-zinc-800 rounded-full overflow-hidden">
            <div
              className={`h-full transition-all duration-300 ${
                isHealthy
                  ? 'bg-emerald-500'
                  : isScaledDown
                  ? 'bg-zinc-600'
                  : isDegraded
                  ? 'bg-rose-500'
                  : 'bg-amber-500'
              }`}
              style={{ width: `${Math.min(100, Math.max(0, percentReady))}%` }}
            />
          </div>
        </div>

        {/* Container Images */}
        {workload.images && workload.images.length > 0 && (
          <div className="space-y-1">
            <div className="flex items-center gap-1 text-[10px] text-zinc-500 font-medium">
              <Tag className="h-3 w-3" />
              <span>Images</span>
            </div>
            <div className="flex flex-wrap gap-1">
              {workload.images.map((img) => (
                <span
                  key={img}
                  className="text-[10px] font-mono px-2 py-0.5 rounded-md bg-zinc-950 border border-zinc-800 text-zinc-300 max-w-full truncate"
                  title={img}
                >
                  {img.split('/').pop() || img}
                </span>
              ))}
            </div>
          </div>
        )}
      </div>

      {/* Footer / Actions */}
      <div className="pt-4 mt-3 border-t border-zinc-800/60 flex items-center justify-between gap-2">
        <div className="flex items-center gap-1 text-[11px] text-zinc-500 font-mono">
          <Clock className="h-3 w-3" />
          <span>
            {workload.creationTimestamp
              ? new Date(workload.creationTimestamp).toLocaleDateString()
              : 'Unknown'}
          </span>
        </div>

        <div className="flex items-center gap-1.5">
          <Button
            variant="outline"
            size="sm"
            onClick={() => onOpenPods(workload)}
            className="h-7 text-xs px-2 text-zinc-300 hover:text-zinc-100 gap-1"
          >
            <Boxes className="h-3 w-3" />
            Pods
          </Button>

          {isOperator && (
            <>
              <Button
                variant="outline"
                size="sm"
                onClick={() => onScale(workload)}
                className="h-7 text-xs px-2 text-sky-400 hover:text-sky-300 hover:bg-sky-950/40 border-sky-800/40 gap-1"
                title="Scale replica count"
              >
                <Sliders className="h-3 w-3" />
                Scale
              </Button>

              <Button
                variant="outline"
                size="sm"
                onClick={handleRestart}
                disabled={restartMutation.isPending}
                className={`h-7 text-xs px-2 gap-1 ${
                  restartSuccess
                    ? 'border-emerald-700 bg-emerald-950/40 text-emerald-300'
                    : 'text-zinc-300 hover:text-emerald-400 hover:bg-zinc-800'
                }`}
                title="Rollout restart"
              >
                {restartMutation.isPending ? (
                  <Loader2 className="h-3 w-3 animate-spin" />
                ) : restartSuccess ? (
                  <CheckCircle2 className="h-3 w-3 text-emerald-400" />
                ) : (
                  <RotateCcw className="h-3 w-3" />
                )}
                {restartSuccess ? 'Restarted' : 'Restart'}
              </Button>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
