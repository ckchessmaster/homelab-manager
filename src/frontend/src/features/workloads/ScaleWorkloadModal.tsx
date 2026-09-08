import { useState, useEffect } from 'react'
import {
  Layers,
  Minus,
  Plus,
  Loader2,
  AlertTriangle,
  CheckCircle2,
} from 'lucide-react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../components/ui/dialog'
import { Button } from '../../components/ui/button'
import { Badge } from '../../components/ui/badge'
import { useScaleWorkload } from './useWorkloads'
import type { WorkloadSummary } from '../../api/workloads'

interface ScaleWorkloadModalProps {
  workload: WorkloadSummary | null
  open: boolean
  onClose: () => void
}

export function ScaleWorkloadModal({
  workload,
  open,
  onClose,
}: ScaleWorkloadModalProps) {
  const [replicas, setReplicas] = useState(1)
  const [statusMsg, setStatusMsg] = useState<{ text: string; error: boolean } | null>(null)
  const scaleMutation = useScaleWorkload()

  useEffect(() => {
    if (workload) {
      setReplicas(workload.desiredReplicas)
      setStatusMsg(null)
    }
  }, [workload])

  if (!workload) return null

  const handleScale = async () => {
    setStatusMsg(null)
    try {
      await scaleMutation.mutateAsync({
        clusterId: workload.clusterId,
        namespaceName: workload.namespace,
        name: workload.name,
        replicas,
      })
      setStatusMsg({ text: `Successfully requested ${replicas} replicas!`, error: false })
      setTimeout(() => {
        onClose()
      }, 1200)
    } catch (err) {
      setStatusMsg({
        text: err instanceof Error ? err.message : 'Failed to scale deployment',
        error: true,
      })
    }
  }

  const delta = replicas - workload.desiredReplicas

  return (
    <Dialog open={open} onClose={onClose}>
      <DialogHeader>
        <DialogTitle className="flex items-center gap-2 text-zinc-100">
          <Layers className="h-5 w-5 text-sky-400" />
          Scale Deployment Replicas
        </DialogTitle>
      </DialogHeader>

      <DialogBody className="space-y-4">
        {/* Workload Info Header */}
        <div className="p-3.5 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2">
          <div className="flex items-center justify-between">
            <span className="text-sm font-semibold text-zinc-100">{workload.name}</span>
            <Badge variant="info" className="text-xs">
              {workload.clusterName}
            </Badge>
          </div>
          <div className="flex items-center gap-2 text-xs text-zinc-400">
            <span>Namespace:</span>
            <span className="font-mono text-zinc-200">{workload.namespace}</span>
            <span>•</span>
            <span>Current Desired:</span>
            <span className="font-mono text-zinc-200">{workload.desiredReplicas}</span>
          </div>
        </div>

        {/* Stepper & Slider */}
        <div className="p-4 bg-zinc-900/40 border border-zinc-800/60 rounded-xl space-y-4">
          <div className="text-xs text-zinc-400 font-medium">Desired Replicas</div>

          <div className="flex items-center justify-center gap-4">
            <button
              type="button"
              onClick={() => setReplicas((r) => Math.max(0, r - 1))}
              disabled={replicas <= 0 || scaleMutation.isPending}
              className="h-10 w-10 rounded-xl bg-zinc-800 hover:bg-zinc-700 border border-zinc-700/80 text-zinc-200 flex items-center justify-center disabled:opacity-30 disabled:pointer-events-none transition-colors cursor-pointer"
            >
              <Minus className="h-4 w-4" />
            </button>

            <div className="w-24 text-center">
              <span className="text-3xl font-extrabold font-mono text-zinc-100">
                {replicas}
              </span>
              <span className="block text-[11px] text-zinc-500 font-medium">
                {delta > 0 ? `+${delta}` : delta < 0 ? `${delta}` : 'no change'}
              </span>
            </div>

            <button
              type="button"
              onClick={() => setReplicas((r) => Math.min(50, r + 1))}
              disabled={replicas >= 50 || scaleMutation.isPending}
              className="h-10 w-10 rounded-xl bg-zinc-800 hover:bg-zinc-700 border border-zinc-700/80 text-zinc-200 flex items-center justify-center disabled:opacity-30 disabled:pointer-events-none transition-colors cursor-pointer"
            >
              <Plus className="h-4 w-4" />
            </button>
          </div>

          <input
            type="range"
            min="0"
            max="20"
            value={replicas}
            onChange={(e) => setReplicas(Number(e.target.value))}
            className="w-full accent-sky-500 cursor-pointer"
          />

          {replicas === 0 && (
            <div className="flex items-start gap-2 p-2.5 rounded-lg bg-amber-950/40 border border-amber-800/50 text-amber-300 text-xs">
              <AlertTriangle className="h-4 w-4 shrink-0 text-amber-400 mt-0.5" />
              <span>
                Setting replicas to 0 will gracefully shut down all active pods for this workload.
              </span>
            </div>
          )}
        </div>

        {/* Status / Feedback */}
        {statusMsg && (
          <div
            className={`p-3 rounded-xl border text-xs flex items-center gap-2 ${
              statusMsg.error
                ? 'bg-rose-950/40 border-rose-800 text-rose-300'
                : 'bg-emerald-950/40 border-emerald-800 text-emerald-300'
            }`}
          >
            {statusMsg.error ? (
              <AlertTriangle className="h-4 w-4 shrink-0 text-rose-400" />
            ) : (
              <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-400" />
            )}
            <span>{statusMsg.text}</span>
          </div>
        )}
      </DialogBody>

      <DialogFooter>
        <Button variant="outline" size="sm" onClick={onClose} disabled={scaleMutation.isPending}>
          Cancel
        </Button>
        <Button
          variant="primary"
          size="sm"
          onClick={handleScale}
          disabled={scaleMutation.isPending || replicas === workload.desiredReplicas}
          className="gap-1.5 bg-sky-600 hover:bg-sky-500 text-white"
        >
          {scaleMutation.isPending ? (
            <>
              <Loader2 className="h-3.5 w-3.5 animate-spin" />
              Scaling...
            </>
          ) : (
            'Apply Scale'
          )}
        </Button>
      </DialogFooter>
    </Dialog>
  )
}
