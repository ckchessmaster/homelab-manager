import { useState } from 'react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../../components/ui/dialog'
import { Button } from '../../../components/ui/button'
import { Badge } from '../../../components/ui/badge'
import { Fan, Sparkles, Sliders, Check, Loader2, Info } from 'lucide-react'
import { useIdracFanControl, useIdracFanControlByIp } from './useIdrac'
import type { IdracFanReading } from '../../../api/idrac'

interface BmcFanControlModalProps {
  open: boolean
  onClose: () => void
  instanceId?: string
  hostId?: string
  idracIp?: string
  serverName: string
  currentFans?: IdracFanReading[]
}

export function BmcFanControlModal({
  open,
  onClose,
  instanceId,
  hostId,
  idracIp,
  serverName,
  currentFans = [],
}: BmcFanControlModalProps) {
  const [mode, setMode] = useState<'Auto' | 'Manual'>('Manual')
  const [percentage, setPercentage] = useState<number>(37) // Default 37% (0x25 from user script!)
  const [statusMsg, setStatusMsg] = useState<{ type: 'success' | 'error'; text: string } | null>(null)

  const fanMutation = useIdracFanControl(instanceId)
  const fanByIpMutation = useIdracFanControlByIp()

  if (!open) return null

  const isPending = fanMutation.isPending || fanByIpMutation.isPending

  const presets = [
    { label: 'Quiet', pct: 20, hex: '0x14', desc: 'Low noise' },
    { label: 'Optimal', pct: 37, hex: '0x25', desc: 'Script Default', star: true },
    { label: 'Balanced', pct: 45, hex: '0x2D', desc: 'Moderate cooling' },
    { label: 'Performance', pct: 60, hex: '0x3C', desc: 'High airflow' },
    { label: 'Max Blast', pct: 100, hex: '0x64', desc: 'Full 100%' },
  ]

  const handleApply = async () => {
    setStatusMsg(null)
    try {
      if (instanceId) {
        await fanMutation.mutateAsync({
          mode,
          percentage: mode === 'Manual' ? percentage : undefined,
        })
      } else {
        await fanByIpMutation.mutateAsync({
          hostId,
          idracIp,
          mode,
          percentage: mode === 'Manual' ? percentage : undefined,
        })
      }

      setStatusMsg({
        type: 'success',
        text:
          mode === 'Auto'
            ? 'Automatic BMC dynamic fan curve restored successfully.'
            : `Fan speed set to ${percentage}% (hex: 0x${percentage.toString(16).toUpperCase()}) successfully.`,
      })
      setTimeout(() => {
        onClose()
      }, 1400)
    } catch (err: unknown) {
      setStatusMsg({
        type: 'error',
        text: err instanceof Error ? err.message : 'Failed to apply fan settings.',
      })
    }
  }

  const hexValue = `0x${percentage.toString(16).toUpperCase().padStart(2, '0')}`

  return (
    <Dialog open={open} onClose={() => !isPending && onClose()} maxWidth="md">
      <DialogHeader onClose={() => !isPending && onClose()}>
        <div className="flex items-center gap-2.5">
          <div className="p-2 rounded-lg bg-sky-500/10 border border-sky-500/20 text-sky-400">
            <Fan className="h-5 w-5 animate-[spin_4s_linear_infinite]" />
          </div>
          <div>
            <DialogTitle>Hardware Fan Control</DialogTitle>
            <p className="text-xs text-zinc-400 mt-0.5">BMC thermal management for {serverName}</p>
          </div>
        </div>
      </DialogHeader>

      <DialogBody className="space-y-4">
        {/* Live Fan RPM telemetry if available */}
        {currentFans.length > 0 && (
          <div className="p-3 bg-zinc-950/60 border border-zinc-800/80 rounded-lg space-y-1.5">
            <div className="flex items-center justify-between text-[11px] text-zinc-400">
              <span className="font-medium">Active Fan Tachometers</span>
              <span className="text-zinc-500">{currentFans.length} detected</span>
            </div>
            <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
              {currentFans.map((fan) => (
                <div
                  key={fan.name}
                  className="flex items-center justify-between p-2 rounded bg-zinc-900/40 border border-zinc-800/50 text-xs"
                >
                  <span className="text-zinc-400 text-[11px] truncate">{fan.name}</span>
                  <span className="font-mono font-semibold text-zinc-200 text-[11px]">
                    {fan.readingRpm > 0 ? `${fan.readingRpm} RPM` : 'Idle'}
                  </span>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Mode Selector */}
        <div className="space-y-1.5">
          <label className="text-xs font-medium text-zinc-300">Control Mode</label>
          <div className="grid grid-cols-2 gap-2">
            <button
              type="button"
              onClick={() => setMode('Manual')}
              className={`p-3 rounded-lg border text-left transition-all ${
                mode === 'Manual'
                  ? 'bg-purple-950/30 border-purple-500/50 text-purple-200 ring-1 ring-purple-500/30'
                  : 'bg-zinc-900/40 border-zinc-800 text-zinc-400 hover:border-zinc-700'
              }`}
            >
              <div className="flex items-center justify-between">
                <span className="font-semibold text-xs text-zinc-200">Manual Override</span>
                <Sliders className="h-3.5 w-3.5 text-purple-400" />
              </div>
              <p className="text-[10px] text-zinc-400 mt-1">
                Static PWM speed via raw OEM IPMI (`0x30 0x30 0x01 0x00`)
              </p>
            </button>

            <button
              type="button"
              onClick={() => setMode('Auto')}
              className={`p-3 rounded-lg border text-left transition-all ${
                mode === 'Auto'
                  ? 'bg-emerald-950/30 border-emerald-500/50 text-emerald-200 ring-1 ring-emerald-500/30'
                  : 'bg-zinc-900/40 border-zinc-800 text-zinc-400 hover:border-zinc-700'
              }`}
            >
              <div className="flex items-center justify-between">
                <span className="font-semibold text-xs text-zinc-200">Automatic (Factory)</span>
                <Sparkles className="h-3.5 w-3.5 text-emerald-400" />
              </div>
              <p className="text-[10px] text-zinc-400 mt-1">
                Restore default dynamic thermal curve (`0x30 0x30 0x01 0x01`)
              </p>
            </button>
          </div>
        </div>

        {/* Manual Speed Options */}
        {mode === 'Manual' && (
          <div className="p-3.5 bg-zinc-950/40 border border-zinc-800/80 rounded-xl space-y-4">
            {/* Speed Presets */}
            <div className="space-y-1.5">
              <span className="text-xs font-medium text-zinc-300">Quick Presets</span>
              <div className="grid grid-cols-2 sm:grid-cols-5 gap-1.5">
                {presets.map((p) => {
                  const isSelected = percentage === p.pct
                  return (
                    <button
                      key={p.pct}
                      type="button"
                      onClick={() => setPercentage(p.pct)}
                      className={`p-2 rounded-lg border text-center transition-all relative ${
                        isSelected
                          ? 'bg-purple-600/20 border-purple-500 text-purple-200'
                          : 'bg-zinc-900/60 border-zinc-800 text-zinc-400 hover:border-zinc-700 hover:text-zinc-200'
                      }`}
                    >
                      {p.star && (
                        <span className="absolute -top-1.5 -right-1.5 bg-amber-500 text-[8px] text-zinc-950 px-1 py-0.2 rounded-full font-bold">
                          DEF
                        </span>
                      )}
                      <div className="font-bold text-xs">{p.pct}%</div>
                      <div className="text-[9px] text-zinc-400 font-mono">{p.hex}</div>
                      <div className="text-[9px] text-zinc-500 truncate">{p.label}</div>
                    </button>
                  )
                })}
              </div>
            </div>

            {/* Custom Slider */}
            <div className="space-y-2">
              <div className="flex items-center justify-between text-xs">
                <span className="text-zinc-300">Custom Speed Slider</span>
                <div className="flex items-center gap-1.5">
                  <Badge variant="outline" className="font-mono text-purple-300 border-purple-500/40 bg-purple-950/30">
                    {percentage}%
                  </Badge>
                  <span className="text-zinc-500 font-mono text-[11px]">({hexValue})</span>
                </div>
              </div>

              <input
                type="range"
                min={10}
                max={100}
                step={1}
                value={percentage}
                onChange={(e) => setPercentage(Number(e.target.value))}
                className="w-full h-1.5 bg-zinc-800 rounded-lg appearance-none cursor-pointer accent-purple-500"
              />

              <div className="flex justify-between text-[10px] text-zinc-500 font-mono">
                <span>10% (0x0A)</span>
                <span>37% (0x25)</span>
                <span>50% (0x32)</span>
                <span>100% (0x64)</span>
              </div>
            </div>

            <div className="flex items-start gap-2 p-2.5 rounded-lg bg-zinc-900/60 border border-zinc-800/60 text-[11px] text-zinc-400">
              <Info className="h-4 w-4 text-purple-400 shrink-0 mt-0.5" />
              <span>
                Sends `raw 0x30 0x30 0x01 0x00` followed by `raw 0x30 0x30 0x02 0xff {hexValue}` across all chassis fans.
              </span>
            </div>
          </div>
        )}

        {statusMsg && (
          <div
            className={`p-3 rounded-lg border text-xs flex items-center gap-2 ${
              statusMsg.type === 'success'
                ? 'bg-emerald-950/40 border-emerald-800 text-emerald-300'
                : 'bg-red-950/40 border-red-800 text-red-300'
            }`}
          >
            {statusMsg.type === 'success' ? (
              <Check className="h-4 w-4 text-emerald-400 shrink-0" />
            ) : (
              <Info className="h-4 w-4 text-red-400 shrink-0" />
            )}
            <span>{statusMsg.text}</span>
          </div>
        )}
      </DialogBody>

      <DialogFooter>
        <Button
          variant="outline"
          size="sm"
          onClick={onClose}
          disabled={isPending}
          className="border-zinc-700 text-zinc-300 hover:bg-zinc-800"
        >
          Cancel
        </Button>
        <Button
          variant="primary"
          size="sm"
          onClick={handleApply}
          disabled={isPending}
          className="gap-2 bg-purple-600 hover:bg-purple-500 text-white font-semibold shadow-xs"
        >
          {isPending ? (
            <>
              <Loader2 className="h-4 w-4 animate-spin" />
              Applying Fan Commands...
            </>
          ) : (
            <>
              <Fan className="h-4 w-4" />
              Apply {mode === 'Auto' ? 'Automatic Mode' : `${percentage}% Speed`}
            </>
          )}
        </Button>
      </DialogFooter>
    </Dialog>
  )
}
