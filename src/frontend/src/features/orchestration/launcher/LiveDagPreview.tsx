import { useMemo } from 'react'
import { GitFork, Sparkles } from 'lucide-react'
import { WorkflowDagCanvas } from '../canvas/WorkflowDagCanvas'
import type { WorkflowStateLike } from '../canvas/layout/dagLayout'

export interface LiveDagPreviewProps {
  enableSnapshot: boolean
  enableK8sDrain: boolean
  requireApprovalBeforeReboot: boolean
  probeCount: number
  workflowMode?: 'upgrade' | 'reboot'
  height?: number | string
  className?: string
}

export function LiveDagPreview({
  enableSnapshot,
  enableK8sDrain,
  requireApprovalBeforeReboot,
  probeCount,
  workflowMode = 'upgrade',
  height = '100%',
  className = '',
}: LiveDagPreviewProps) {
  const previewState: WorkflowStateLike = useMemo(() => ({
    status: 'Pending',
    completedSteps: [],
  }), [])

  const isRebootMode = workflowMode === 'reboot'

  // Calculate dynamic step count
  const estimatedStepCount = useMemo(() => {
    if (isRebootMode) {
      let count = 3 // Preflight Heartbeat + Deterministic Reboot + Reconnection Wait
      if (enableK8sDrain) count += 2 // Cordon/Drain + Uncordon
      if (probeCount > 0) count += 1 // Health Probes
      return count
    }

    let count = 6 // Preflights (3) + Upgrade + Reboot + Reconnect
    if (enableSnapshot) count += 1
    if (enableK8sDrain) count += 2 // Cordon/Drain + Uncordon
    if (requireApprovalBeforeReboot) count += 1
    if (probeCount > 0) count += 1
    return count
  }, [isRebootMode, enableSnapshot, enableK8sDrain, requireApprovalBeforeReboot, probeCount])

  return (
    <div className={`flex-1 min-h-0 flex flex-col space-y-2.5 h-full ${className}`}>
      <div className="flex items-center justify-between px-1 shrink-0">
        <div className="flex items-center gap-2">
          <GitFork className="w-4 h-4 text-emerald-400" />
          <span className="text-xs font-semibold text-zinc-200">
            Real-Time Execution Graph Preview
          </span>
          <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 flex items-center gap-1">
            <Sparkles className="w-2.5 h-2.5" />
            {estimatedStepCount} Nodes Active
          </span>
        </div>
        <span className="text-[11px] text-zinc-500">
          Auto-updates with parameter toggles
        </span>
      </div>

      <div className="flex-1 min-h-[380px] w-full h-full relative">
        <WorkflowDagCanvas
          workflowId="preview-dag"
          state={previewState}
          pipelineId={isRebootMode ? 'safe-reboot-verify' : 'standard-os-upgrade'}
          isProxmoxHost={isRebootMode ? false : enableSnapshot}
          isK8sHost={enableK8sDrain}
          requireApproval={isRebootMode ? false : requireApprovalBeforeReboot}
          height={height}
          className="border-zinc-800/80 shadow-inner h-full w-full"
        />
      </div>
    </div>
  )
}
