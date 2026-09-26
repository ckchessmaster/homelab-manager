import { useMemo } from 'react'
import {
  CheckCircle2,
  AlertCircle,
  Loader2,
  Circle,
  Terminal,
  RotateCcw,
  Shield,
  Layers,
  HardDrive,
  Sparkles,
} from 'lucide-react'
import { Button } from '../../../components/ui/button'
import type { WorkflowStateLike } from './layout/dagLayout'

interface MobileDagTimelineProps {
  state: WorkflowStateLike
  pipelineId?: string | null
  onApprove?: () => void
  onReject?: () => void
  isSubmittingApproval?: boolean
  isProxmoxHost?: boolean
  isK8sHost?: boolean
  onOpenTerminal?: () => void
}

interface StepItem {
  id: string
  label: string
  description?: string
  status: 'completed' | 'running' | 'approval' | 'pending' | 'failed'
  icon: typeof CheckCircle2
}

export function MobileDagTimeline({
  state,
  pipelineId = 'standard-os-upgrade',
  onApprove,
  onReject,
  isSubmittingApproval,
  isProxmoxHost,
  isK8sHost,
  onOpenTerminal,
}: MobileDagTimelineProps) {
  const steps: StepItem[] = useMemo(() => {
    const list: { id: string; label: string; desc: string; icon: any }[] = [
      { id: 'preflight', label: 'Preflight Verification', desc: 'Heartbeat, lock files & disk headroom', icon: Shield },
    ]

    if (isProxmoxHost) {
      list.push({ id: 'snapshot', label: 'Hypervisor Safety Snapshot', desc: 'Pre-flight atomic rollback point', icon: HardDrive })
    }

    if (isK8sHost) {
      list.push({ id: 'cordon-drain', label: 'Kubernetes Cordon & Drain', desc: 'Evacuate pods to standby nodes', icon: Layers })
    }

    list.push({ id: 'packages', label: 'Package Upgrade Execution', desc: 'Atomic OS package updates', icon: Sparkles })

    if (isK8sHost || state.activeStep?.toLowerCase().includes('reboot') || state.awaitingApproval) {
      list.push({ id: 'reboot-gate', label: 'Reboot & Kernel Verification', desc: 'Reboot approval gate and health check', icon: RotateCcw })
    }

    if (isK8sHost) {
      list.push({ id: 'uncordon', label: 'Kubernetes Uncordon', desc: 'Restore node to cluster schedulable pool', icon: Layers })
    }

    list.push({ id: 'postflight', label: 'Post-Flight Health Probes', desc: 'Service vitals & daemon verification', icon: CheckCircle2 })

    const completed = new Set((state.completedSteps || []).map((s) => s.toLowerCase()))
    const activeStepLower = (state.activeStep || '').toLowerCase()
    const isFailed = (state.status || '').toLowerCase() === 'failed'

    return list.map((item) => {
      let status: 'completed' | 'running' | 'approval' | 'pending' | 'failed' = 'pending'

      if (completed.has(item.id) || (state.completedSteps || []).some((s) => s.toLowerCase().includes(item.id))) {
        status = 'completed'
      } else if (item.id === 'reboot-gate' && state.awaitingApproval) {
        status = 'approval'
      } else if (activeStepLower.includes(item.id) || (activeStepLower && activeStepLower.includes(item.label.toLowerCase()))) {
        status = isFailed ? 'failed' : 'running'
      } else if (state.status === 'Completed') {
        status = 'completed'
      }

      return {
        id: item.id,
        label: item.label,
        description: item.desc,
        status,
        icon: item.icon,
      }
    })
  }, [state, isProxmoxHost, isK8sHost])

  return (
    <div className="flex flex-col h-full bg-zinc-950 p-4 overflow-y-auto space-y-4">
      {/* Workflow Summary Header */}
      <div className="p-3.5 rounded-xl bg-zinc-900/60 border border-zinc-800 space-y-1">
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold text-zinc-200">Execution Timeline</span>
          <span
            className={`text-[10px] font-mono font-semibold px-2 py-0.5 rounded-full border ${
              state.status === 'Completed'
                ? 'bg-emerald-950/60 text-emerald-400 border-emerald-800/60'
                : state.status === 'Failed'
                ? 'bg-rose-950/60 text-rose-400 border-rose-800/60'
                : 'bg-sky-950/60 text-sky-400 border-sky-800/60 animate-pulse'
            }`}
          >
            {state.status || 'Running'}
          </span>
        </div>
        <p className="text-[11px] text-zinc-400">
          Pipeline: <span className="font-mono text-zinc-300">{pipelineId}</span>
        </p>
      </div>

      {/* Vertical Steps */}
      <div className="space-y-3 relative before:absolute before:left-4 before:top-4 before:bottom-4 before:w-0.5 before:bg-zinc-800">
        {steps.map((step, idx) => (
          <div key={step.id} className="relative flex items-start gap-3.5 z-10">
              {/* Step Status Icon */}
              <div
                className={`w-8 h-8 rounded-full flex items-center justify-center shrink-0 border transition-all ${
                  step.status === 'completed'
                    ? 'bg-emerald-950/90 border-emerald-500 text-emerald-400 shadow-xs shadow-emerald-950'
                    : step.status === 'running'
                    ? 'bg-sky-950/90 border-sky-400 text-sky-400 animate-pulse ring-2 ring-sky-500/30'
                    : step.status === 'approval'
                    ? 'bg-amber-950/90 border-amber-400 text-amber-300 ring-2 ring-amber-500/30 animate-bounce'
                    : step.status === 'failed'
                    ? 'bg-rose-950/90 border-rose-500 text-rose-400'
                    : 'bg-zinc-900 border-zinc-700 text-zinc-500'
                }`}
              >
                {step.status === 'completed' ? (
                  <CheckCircle2 className="w-4 h-4" />
                ) : step.status === 'running' ? (
                  <Loader2 className="w-4 h-4 animate-spin" />
                ) : step.status === 'approval' ? (
                  <AlertCircle className="w-4 h-4" />
                ) : (
                  <Circle className="w-3.5 h-3.5" />
                )}
              </div>

              {/* Step Card Content */}
              <div
                className={`flex-1 p-3 rounded-xl border transition-all ${
                  step.status === 'running'
                    ? 'bg-zinc-900/90 border-sky-500/50 shadow-md shadow-sky-950/20'
                    : step.status === 'approval'
                    ? 'bg-amber-950/30 border-amber-700/60'
                    : step.status === 'completed'
                    ? 'bg-zinc-900/40 border-zinc-800/60 text-zinc-300'
                    : 'bg-zinc-900/20 border-zinc-800/40 text-zinc-500'
                }`}
              >
                <div className="flex items-center justify-between gap-2">
                  <h4 className="text-xs font-semibold text-zinc-100">{step.label}</h4>
                  <span className="text-[10px] uppercase font-mono tracking-wider text-zinc-400">
                    Step {idx + 1}
                  </span>
                </div>
                {step.description && (
                  <p className="text-[11px] text-zinc-400 mt-0.5">{step.description}</p>
                )}

                {/* Inline Approval Controls on Approval Gate */}
                {step.status === 'approval' && (
                  <div className="mt-3 pt-2.5 border-t border-amber-800/40 flex items-center gap-2 flex-wrap">
                    <Button
                      size="sm"
                      variant="primary"
                      onClick={onApprove}
                      disabled={isSubmittingApproval}
                      className="bg-amber-600 hover:bg-amber-500 text-white text-xs py-1 px-3 min-h-[36px]"
                    >
                      Approve Reboot
                    </Button>
                    <Button
                      size="sm"
                      variant="destructive"
                      onClick={onReject}
                      disabled={isSubmittingApproval}
                      className="text-xs py-1 px-3 min-h-[36px]"
                    >
                      Reject & Rollback
                    </Button>
                  </div>
                )}
            </div>
          </div>
        ))}
      </div>

      {/* Floating Action Controls */}
      {onOpenTerminal && (
        <div className="pt-2">
          <Button
            variant="outline"
            onClick={onOpenTerminal}
            className="w-full gap-2 border-zinc-700 bg-zinc-900 text-sky-400 hover:text-sky-300 min-h-[44px]"
          >
            <Terminal className="w-4 h-4" />
            <span>Open Real-Time Terminal Stream</span>
          </Button>
        </div>
      )}
    </div>
  )
}
