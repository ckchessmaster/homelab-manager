import React from 'react'
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
} from 'lucide-react'

interface PipelineStepPreviewProps {
  steps: PipelineStepSummary[]
  activeStepIndex?: number
  className?: string
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
}) => {
  if (!steps || steps.length === 0) {
    return (
      <div className="text-xs text-zinc-500 italic py-2">
        No step definitions available for this pipeline.
      </div>
    )
  }

  return (
    <div className={`space-y-2 ${className}`}>
      <div className="flex items-center justify-between text-xs font-medium text-zinc-400 pb-1">
        <span>Execution Pipeline Sequence ({steps.length} Steps)</span>
        <span className="text-[11px] text-zinc-500">Atomic Rollback Protected</span>
      </div>

      <div className="relative pl-3 border-l-2 border-zinc-800/80 space-y-3.5 my-2">
        {steps.map((step, idx) => {
          const isCurrent = activeStepIndex !== undefined && idx === activeStepIndex
          const isCompleted = activeStepIndex !== undefined && idx < activeStepIndex

          return (
            <div key={idx} className="relative group">
              {/* Timeline marker bullet */}
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

              {/* Step content card */}
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
    </div>
  )
}
