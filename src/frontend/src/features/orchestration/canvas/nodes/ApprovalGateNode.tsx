import { memo } from 'react'
import { Handle, Position } from '@xyflow/react'
import type { NodeProps } from '@xyflow/react'
import {
  ShieldAlert,
  CheckCircle2,
  XCircle,
  Clock,
  Loader2,
  Check,
  X,
} from 'lucide-react'
import type { ApprovalGateNodeData } from './WorkflowNodeTypes'

export const ApprovalGateNode: React.FC<NodeProps> = memo(({ data }) => {
  const gateData = data as unknown as ApprovalGateNodeData
  const {
    label,
    status,
    onApprove,
    onReject,
    isSubmitting,
    timeoutCountdown,
  } = gateData

  const isWaiting = status === 'waiting'
  const isApproved = status === 'approved'
  const isRejected = status === 'rejected'

  let borderStyle = 'border-amber-500/50'
  let glowStyle = 'shadow-[0_0_24px_rgba(245,158,11,0.2)] ring-1 ring-amber-500/30'

  if (isApproved) {
    borderStyle = 'border-emerald-500/60'
    glowStyle = 'shadow-[0_0_15px_rgba(16,185,129,0.2)]'
  } else if (isRejected) {
    borderStyle = 'border-rose-500/60'
    glowStyle = 'shadow-[0_0_15px_rgba(244,63,94,0.2)]'
  }

  return (
    <div
      className={`relative min-w-[260px] max-w-[300px] rounded-xl bg-zinc-900/95 backdrop-blur-md border ${borderStyle} ${glowStyle} p-4 transition-all duration-200 select-none shadow-2xl`}
    >
      <Handle
        type="target"
        position={Position.Left}
        className="!w-2.5 !h-2.5 !bg-amber-500 !border-2 !border-zinc-900"
      />
      <Handle
        type="source"
        position={Position.Right}
        className="!w-2.5 !h-2.5 !bg-amber-500 !border-2 !border-zinc-900"
      />

      {/* Header */}
      <div className="flex items-center justify-between gap-2 mb-2.5">
        <span className="text-[10px] font-mono uppercase tracking-wider text-amber-400 font-bold px-2 py-0.5 rounded bg-amber-500/10 border border-amber-500/30 flex items-center gap-1">
          <ShieldAlert className="w-3 h-3" />
          Human Gate
        </span>

        {isWaiting && (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-amber-500/20 text-amber-300 border border-amber-500/30 animate-pulse">
            <Clock className="w-2.5 h-2.5" />
            Action Required
          </span>
        )}
        {isApproved && (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
            <CheckCircle2 className="w-2.5 h-2.5" />
            Approved
          </span>
        )}
        {isRejected && (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-rose-500/20 text-rose-300 border border-rose-500/30">
            <XCircle className="w-2.5 h-2.5" />
            Rejected
          </span>
        )}
      </div>

      {/* Title & Description */}
      <div className="my-2">
        <h4 className="text-xs font-bold text-zinc-100 leading-tight">
          {label}
        </h4>
        <p className="text-[11px] text-zinc-400 mt-1 leading-relaxed">
          {isWaiting
            ? 'Workflow paused at safe state. Operator approval required before node restarts.'
            : isApproved
            ? 'Reboot signal verified. Safe system restart proceeding.'
            : 'Operator rejected reboot. Compensations initiating.'}
        </p>
      </div>

      {/* Timeout Countdown if present */}
      {timeoutCountdown && isWaiting && (
        <div className="text-[10px] font-mono text-amber-400/90 mb-3 flex items-center gap-1">
          <Clock className="w-3 h-3" />
          Auto-timeout in: {timeoutCountdown}
        </div>
      )}

      {/* Interactive Action Buttons */}
      {isWaiting && (
        <div className="mt-3 pt-3 border-t border-zinc-800 flex items-center gap-2">
          <button
            type="button"
            onClick={onApprove}
            disabled={isSubmitting}
            className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 active:bg-emerald-700 text-white font-semibold text-xs transition-colors shadow-md shadow-emerald-950/40 disabled:opacity-50 cursor-pointer"
          >
            {isSubmitting ? (
              <Loader2 className="w-3.5 h-3.5 animate-spin" />
            ) : (
              <Check className="w-3.5 h-3.5" />
            )}
            Approve
          </button>
          <button
            type="button"
            onClick={onReject}
            disabled={isSubmitting}
            className="inline-flex items-center justify-center gap-1 px-2.5 py-1.5 rounded-lg bg-zinc-800 hover:bg-rose-900/60 hover:text-rose-200 text-zinc-300 border border-zinc-700 text-xs transition-colors disabled:opacity-50 cursor-pointer"
            title="Reject and abort workflow"
          >
            <X className="w-3.5 h-3.5" />
            Reject
          </button>
        </div>
      )}
    </div>
  )
})

ApprovalGateNode.displayName = 'ApprovalGateNode'
