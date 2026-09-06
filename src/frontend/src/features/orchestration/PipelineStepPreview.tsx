import { useState, useMemo } from 'react'
import type { PipelineStepSummary } from '../../api/pipelines'
import {
  ShieldCheck,
  HardDrive,
  Lock,
  Camera,
  Layers,
  ArrowUpCircle,
  RotateCcw,
  Radio,
  Activity,
  CheckCircle2,
  GitFork,
  List,
} from 'lucide-react'
import { WorkflowDagCanvas } from './canvas/WorkflowDagCanvas'
import type { WorkflowStateLike } from './canvas/layout/dagLayout'

interface PipelineStepPreviewProps {
  steps: PipelineStepSummary[]
  activeStepIndex?: number
  className?: string
  defaultView?: 'list' | 'graph'
}

function getStepIcon(name: string) {
  const lower = name.toLowerCase()
  if (lower.includes('heartbeat')) return <Radio className="w-4 h-4 text-sky-400" />
  if (lower.includes('disk') || lower.includes('headroom')) return <HardDrive className="w-4 h-4 text-cyan-400" />
  if (lower.includes('lock')) return <Lock className="w-4 h-4 text-amber-400" />
  if (lower.includes('snapshot')) return <Camera className="w-4 h-4 text-purple-400" />
  if (lower.includes('cordon') || lower.includes('drain') || lower.includes('uncordon'))
    return <Layers className="w-4 h-4 text-indigo-400" />
  if (lower.includes('upgrade')) return <ArrowUpCircle className="w-4 h-4 text-emerald-400" />
  if (lower.includes('reboot')) return <RotateCcw className="w-4 h-4 text-amber-400" />
  if (lower.includes('reconnection')) return <Radio className="w-4 h-4 text-teal-400" />
  if (lower.includes('health') || lower.includes('probe')) return <Activity className="w-4 h-4 text-emerald-400" />
  return <ShieldCheck className="w-4 h-4 text-zinc-400" />
}

export const PipelineStepPreview: React.FC<PipelineStepPreviewProps> = ({
  steps,
  activeStepIndex,
  className = '',
  defaultView = 'list',
}) => {
  const [viewMode, setViewMode] = useState<'list' | 'graph'>(defaultView)

  const previewState: WorkflowStateLike = useMemo(() => {
    const completedSteps: string[] = []
    let activeStep: string | undefined = undefined

    if (activeStepIndex !== undefined && steps) {
      steps.forEach((s, idx) => {
        if (idx < activeStepIndex) completedSteps.push(s.name)
        if (idx === activeStepIndex) activeStep = s.name
      })
    }

    return {
      status: activeStepIndex !== undefined ? 'Running' : 'Pending',
      activeStep,
      completedSteps,
    }
  }, [steps, activeStepIndex])

  if (!steps || steps.length === 0) {
    return (
      <div className="text-xs text-zinc-500 italic py-2">
        No step definitions available for this pipeline.
      </div>
    )
  }

  return (
    <div className={`space-y-2 ${className}`}>
      {/* Header with View Toggle */}
      <div className="flex items-center justify-between text-xs font-medium text-zinc-400 pb-1 border-b border-zinc-800/60">
        <div className="flex items-center gap-2">
          <span>Execution Pipeline Sequence ({steps.length} Steps)</span>
          <span className="text-[10px] px-1.5 py-0.5 rounded bg-zinc-800 text-zinc-400">
            Atomic Rollback
          </span>
        </div>

        <div className="flex items-center bg-zinc-900 border border-zinc-800 rounded-md p-0.5 text-[11px]">
          <button
            type="button"
            onClick={() => setViewMode('list')}
            className={`inline-flex items-center gap-1 px-2 py-0.5 rounded transition-colors ${
              viewMode === 'list'
                ? 'bg-zinc-800 text-zinc-100 font-medium'
                : 'text-zinc-500 hover:text-zinc-300'
            }`}
          >
            <List className="w-3 h-3" />
            List
          </button>
          <button
            type="button"
            onClick={() => setViewMode('graph')}
            className={`inline-flex items-center gap-1 px-2 py-0.5 rounded transition-colors ${
              viewMode === 'graph'
                ? 'bg-zinc-800 text-emerald-400 font-medium'
                : 'text-zinc-500 hover:text-zinc-300'
            }`}
          >
            <GitFork className="w-3 h-3" />
            DAG Graph
          </button>
        </div>
      </div>

      {/* Graph Canvas View */}
      {viewMode === 'graph' ? (
        <div className="mt-2">
          <WorkflowDagCanvas
            workflowId="preview-pipeline"
            state={previewState}
            height={260}
            className="border-zinc-800/80"
          />
        </div>
      ) : (
        /* Vertical Step List View */
        <div className="relative pl-3 border-l-2 border-zinc-800/80 space-y-3 my-2">
          {steps.map((step, idx) => {
            const isCurrent = activeStepIndex !== undefined && idx === activeStepIndex
            const isCompleted = activeStepIndex !== undefined && idx < activeStepIndex

            return (
              <div key={idx} className="relative group">
                <div
                  className={`absolute -left-[19px] top-0.5 w-3.5 h-3.5 rounded-full border-2 flex items-center justify-center transition-colors ${
                    isCurrent
                      ? 'bg-emerald-500 border-emerald-300 ring-2 ring-emerald-500/40 animate-pulse'
                      : isCompleted
                      ? 'bg-emerald-600 border-emerald-500'
                      : 'bg-zinc-900 border-zinc-700 group-hover:border-zinc-500'
                  }`}
                >
                  {isCompleted ? (
                    <CheckCircle2 className="w-2.5 h-2.5 text-white" />
                  ) : (
                    <span className="w-1.5 h-1.5 rounded-full bg-zinc-400" />
                  )}
                </div>

                <div className="ml-1 p-2 rounded-md bg-zinc-900/50 border border-zinc-800/60 hover:border-zinc-700/80 transition-colors">
                  <div className="flex items-center gap-2">
                    <span className="p-1 rounded bg-zinc-800/80 text-zinc-300 shrink-0">
                      {getStepIcon(step.name)}
                    </span>
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-semibold text-zinc-200 truncate">
                          {step.name}
                        </span>
                        <span className="text-[10px] text-zinc-500 font-mono">
                          Step {idx + 1}
                        </span>
                      </div>
                      <p className="text-[11px] text-zinc-400 mt-0.5 leading-relaxed">
                        {step.description}
                      </p>
                    </div>
                  </div>
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
