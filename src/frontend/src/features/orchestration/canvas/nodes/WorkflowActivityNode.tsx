import { memo } from 'react'
import { Handle, Position } from '@xyflow/react'
import type { NodeProps } from '@xyflow/react'
import {
  Radio,
  HardDrive,
  Lock,
  Camera,
  Layers,
  ArrowUpCircle,
  RotateCcw,
  Activity,
  CheckCircle2,
  AlertCircle,
  Clock,
  Undo2,
  Loader2,
} from 'lucide-react'
import type { ActivityNodeData, ActivityStatus } from './WorkflowNodeTypes'

function getCategoryIcon(category: string, label: string) {
  const l = label.toLowerCase()
  if (l.includes('heartbeat')) return <Radio className="w-4 h-4 text-sky-400" />
  if (l.includes('disk') || l.includes('headroom')) return <HardDrive className="w-4 h-4 text-cyan-400" />
  if (l.includes('lock')) return <Lock className="w-4 h-4 text-amber-400" />
  if (category === 'proxmox' || l.includes('snapshot')) return <Camera className="w-4 h-4 text-purple-400" />
  if (category === 'kubernetes' || l.includes('cordon') || l.includes('drain'))
    return <Layers className="w-4 h-4 text-indigo-400" />
  if (l.includes('upgrade') || l.includes('package')) return <ArrowUpCircle className="w-4 h-4 text-emerald-400" />
  if (l.includes('reboot')) return <RotateCcw className="w-4 h-4 text-amber-400" />
  if (l.includes('probe') || category === 'health') return <Activity className="w-4 h-4 text-emerald-400" />
  if (l.includes('rollback') || l.includes('compensat')) return <Undo2 className="w-4 h-4 text-rose-400" />
  return <Activity className="w-4 h-4 text-zinc-400" />
}

function getStatusBadge(status: ActivityStatus) {
  switch (status) {
    case 'running':
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-sky-500/20 text-sky-300 border border-sky-500/30">
          <Loader2 className="w-3 h-3 animate-spin text-sky-400" />
          Running
        </span>
      )
    case 'completed':
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
          <CheckCircle2 className="w-3 h-3 text-emerald-400" />
          Done
        </span>
      )
    case 'failed':
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-rose-500/20 text-rose-300 border border-rose-500/30">
          <AlertCircle className="w-3 h-3 text-rose-400" />
          Failed
        </span>
      )
    case 'compensated':
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-orange-500/20 text-orange-300 border border-orange-500/30">
          <Undo2 className="w-3 h-3 text-orange-400" />
          Rolled Back
        </span>
      )
    case 'skipped':
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-medium bg-zinc-800 text-zinc-500 border border-zinc-700/50">
          Skipped
        </span>
      )
    default:
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-medium bg-zinc-800/80 text-zinc-400 border border-zinc-700/50">
          <Clock className="w-2.5 h-2.5 text-zinc-500" />
          Pending
        </span>
      )
  }
}

export const WorkflowActivityNode: React.FC<NodeProps> = memo(({ data }) => {
  const nodeData = data as unknown as ActivityNodeData
  const {
    label,
    category,
    stepNumber,
    status,
    duration,
    description,
    errorMessage,
    isCompensation,
  } = nodeData

  const isRunning = status === 'running'
  const isFailed = status === 'failed'
  const isCompleted = status === 'completed'

  let borderStyle = 'border-zinc-800/80'
  let glowStyle = ''

  if (isRunning) {
    borderStyle = 'border-sky-500/60 ring-2 ring-sky-500/30'
    glowStyle = 'shadow-[0_0_20px_rgba(14,165,233,0.25)]'
  } else if (isFailed) {
    borderStyle = 'border-rose-500/60 ring-1 ring-rose-500/40'
    glowStyle = 'shadow-[0_0_20px_rgba(244,63,94,0.2)]'
  } else if (isCompleted) {
    borderStyle = 'border-emerald-500/40'
    glowStyle = 'shadow-[0_0_12px_rgba(16,185,129,0.15)]'
  } else if (isCompensation) {
    borderStyle = 'border-dashed border-orange-500/60'
  }

  return (
    <div
      className={`relative min-w-[240px] max-w-[280px] rounded-xl bg-zinc-900/95 backdrop-blur-md border ${borderStyle} ${glowStyle} p-3.5 transition-all duration-200 select-none shadow-xl`}
    >
      {/* React Flow Handles */}
      <Handle
        type="target"
        position={Position.Left}
        className="!w-2.5 !h-2.5 !bg-zinc-700 !border-2 !border-zinc-900 transition-colors"
      />
      <Handle
        type="source"
        position={Position.Right}
        className="!w-2.5 !h-2.5 !bg-zinc-700 !border-2 !border-zinc-900 transition-colors"
      />

      {/* Header with Step Number & Status */}
      <div className="flex items-center justify-between gap-2 mb-2">
        <div className="flex items-center gap-1.5">
          <span className="text-[10px] font-mono uppercase tracking-wider text-zinc-400 font-semibold px-1.5 py-0.5 rounded bg-zinc-800/80 border border-zinc-700/50">
            {isCompensation ? 'Rollback' : `Step ${stepNumber}`}
          </span>
          {isCompensation && (
            <span className="text-[9px] font-bold text-orange-400 uppercase tracking-widest px-1 py-0.5 rounded bg-orange-500/10 border border-orange-500/20">
              Saga
            </span>
          )}
        </div>
        {getStatusBadge(status)}
      </div>

      {/* Activity Label & Icon */}
      <div className="flex items-start gap-2.5 my-1.5">
        <div
          className={`p-2 rounded-lg shrink-0 border transition-colors ${
            isRunning
              ? 'bg-sky-500/15 border-sky-500/30'
              : isCompleted
              ? 'bg-emerald-500/10 border-emerald-500/20'
              : isFailed
              ? 'bg-rose-500/10 border-rose-500/20'
              : 'bg-zinc-800/60 border-zinc-700/40'
          }`}
        >
          {getCategoryIcon(category, label)}
        </div>
        <div className="min-w-0 flex-1">
          <h4 className="text-xs font-semibold text-zinc-100 leading-snug break-words">
            {label}
          </h4>
          {description && (
            <p className="text-[11px] text-zinc-400 mt-0.5 leading-relaxed line-clamp-2">
              {description}
            </p>
          )}
        </div>
      </div>

      {/* Footer Info: Duration / Error */}
      <div className="mt-2.5 pt-2 border-t border-zinc-800/80 flex items-center justify-between text-[11px] text-zinc-400 font-mono">
        <span className="capitalize text-[10px] text-zinc-500 font-sans">
          {category}
        </span>
        {duration ? (
          <span className="flex items-center gap-1 text-zinc-300">
            <Clock className="w-3 h-3 text-zinc-500" />
            {duration}
          </span>
        ) : isRunning ? (
          <span className="flex items-center gap-1 text-sky-400 animate-pulse">
            <Clock className="w-3 h-3" />
            In progress...
          </span>
        ) : (
          <span className="text-zinc-600">—</span>
        )}
      </div>

      {/* Error message card if failed */}
      {errorMessage && (
        <div className="mt-2 p-2 rounded bg-rose-950/40 border border-rose-800/60 text-[10px] font-mono text-rose-300 break-words">
          {errorMessage}
        </div>
      )}
    </div>
  )
})

WorkflowActivityNode.displayName = 'WorkflowActivityNode'
