import React from 'react'
import { CheckCircle2, Loader2, AlertCircle, Circle, Terminal, Cpu, HardDrive, Radio, ShieldCheck } from 'lucide-react'
import type { AdoptionStepEvent, AdoptionStepStatus } from '../../api/hosts'

interface AdoptionStepProgressProps {
  steps: AdoptionStepEvent[]
  isAdopting: boolean
  error?: string | null
}

const DEFAULT_STEPS = [
  {
    key: 'SSH_CONNECTING',
    title: 'Probing Architecture via SSH',
    icon: Terminal,
  },
  {
    key: 'ARCH_DETECTED',
    title: 'Selecting Static Agent Binary',
    icon: Cpu,
  },
  {
    key: 'BINARY_STREAMING',
    title: 'Streaming Binary to Target Node',
    icon: HardDrive,
  },
  {
    key: 'SERVICE_STARTING',
    title: 'Configuring & Starting systemd Unit',
    icon: ShieldCheck,
  },
  {
    key: 'HANDSHAKE_VERIFIED',
    title: 'Verifying Outbound WebSocket Handshake',
    icon: Radio,
  },
]

export const AdoptionStepProgress: React.FC<AdoptionStepProgressProps> = ({
  steps,
  isAdopting,
  error,
}) => {
  const getStepStatus = (key: string): AdoptionStepStatus => {
    const found = steps.find((s) => s.stepKey === key)
    if (found) {
      if (typeof found.status === 'number') {
        const map: Record<number, AdoptionStepStatus> = {
          0: 'Pending',
          1: 'Running',
          2: 'Completed',
          3: 'Failed',
        }
        return map[found.status] || 'Pending'
      }
      return found.status
    }
    if (isAdopting && key === 'SSH_CONNECTING') {
      return 'Running'
    }
    if (error && key === 'SSH_CONNECTING' && steps.length === 0) {
      return 'Failed'
    }
    return 'Pending'
  }

  const getStepMessage = (key: string): string | undefined => {
    const found = steps.find((s) => s.stepKey === key)
    return found?.message || undefined
  }

  return (
    <div className="space-y-3 bg-zinc-950/60 border border-zinc-800/80 rounded-xl p-4">
      <div className="flex items-center justify-between border-b border-zinc-800/60 pb-2 mb-3">
        <h4 className="text-xs font-semibold text-zinc-300 uppercase tracking-wider">
          Adoption Lifecycle
        </h4>
        {isAdopting && (
          <div className="flex items-center gap-1.5 text-xs text-sky-400 font-medium">
            <Loader2 className="w-3.5 h-3.5 animate-spin" />
            <span>Executing...</span>
          </div>
        )}
      </div>

      <div className="space-y-3">
        {DEFAULT_STEPS.map((stepDef, idx) => {
          const status = getStepStatus(stepDef.key)
          const message = getStepMessage(stepDef.key)
          const Icon = stepDef.icon

          let statusIcon = <Circle className="w-4 h-4 text-zinc-600" />
          let stepBg = 'text-zinc-400'
          let titleColor = 'text-zinc-400'

          if (status === 'Completed') {
            statusIcon = <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
            stepBg = 'text-emerald-400'
            titleColor = 'text-zinc-200 font-medium'
          } else if (status === 'Running') {
            statusIcon = <Loader2 className="w-4 h-4 text-sky-400 animate-spin shrink-0" />
            stepBg = 'text-sky-400'
            titleColor = 'text-sky-200 font-semibold'
          } else if (status === 'Failed') {
            statusIcon = <AlertCircle className="w-4 h-4 text-rose-400 shrink-0" />
            stepBg = 'text-rose-400'
            titleColor = 'text-rose-200 font-medium'
          }

          return (
            <div
              key={stepDef.key}
              className={`flex items-start gap-3 text-sm p-2 rounded-lg transition-colors ${
                status === 'Running'
                  ? 'bg-sky-500/10 border border-sky-500/20'
                  : status === 'Failed'
                  ? 'bg-rose-500/10 border border-rose-500/20'
                  : 'bg-zinc-900/40 border border-zinc-800/40'
              }`}
            >
              <div className="pt-0.5">{statusIcon}</div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <Icon className={`w-3.5 h-3.5 ${stepBg}`} />
                  <span className={`text-xs ${titleColor}`}>
                    Step {idx + 1}: {stepDef.title}
                  </span>
                </div>
                {message && (
                  <p
                    className={`mt-1 text-xs break-all font-mono ${
                      status === 'Failed'
                        ? 'text-rose-400'
                        : status === 'Completed'
                        ? 'text-emerald-400/90'
                        : 'text-zinc-400'
                    }`}
                  >
                    {message}
                  </p>
                )}
              </div>
            </div>
          )
        })}
      </div>

      {error && (
        <div className="p-3 bg-rose-950/40 border border-rose-800/60 rounded-lg text-xs text-rose-300 flex items-start gap-2 mt-3">
          <AlertCircle className="w-4 h-4 text-rose-400 shrink-0 mt-0.5" />
          <div className="space-y-1">
            <p className="font-semibold">Adoption Procedure Failed</p>
            <p className="font-mono text-[11px] text-rose-300/90">{error}</p>
          </div>
        </div>
      )}
    </div>
  )
}
