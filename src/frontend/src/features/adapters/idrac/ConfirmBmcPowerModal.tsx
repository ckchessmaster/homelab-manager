import { useState } from 'react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../../components/ui/dialog'
import { Button } from '../../../components/ui/button'
import { AlertTriangle, Power, PowerOff, RotateCcw, Loader2, ShieldAlert } from 'lucide-react'

interface ConfirmBmcPowerModalProps {
  open: boolean
  onClose: () => void
  targetName: string
  targetIp?: string | null
  action: string
  actionLabel: string
  onConfirm: () => Promise<void>
}

export function ConfirmBmcPowerModal({
  open,
  onClose,
  targetName,
  targetIp,
  action,
  actionLabel,
  onConfirm,
}: ConfirmBmcPowerModalProps) {
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

  if (!open) return null

  const isForceOff = action.toLowerCase() === 'forceoff' || action.toLowerCase() === 'off'
  const isPowerCycle = action.toLowerCase() === 'powercycle' || action.toLowerCase() === 'forcerestart' || action.toLowerCase() === 'reset'
  const isShutdown = action.toLowerCase() === 'gracefulshutdown' || action.toLowerCase() === 'shutdown'
  const isPowerOn = action.toLowerCase() === 'on' || action.toLowerCase() === 'poweron'

  const handleProceed = async () => {
    setErrorMsg(null)
    setIsSubmitting(true)
    try {
      await onConfirm()
      setIsSubmitting(false)
      onClose()
    } catch (err: unknown) {
      setIsSubmitting(false)
      setErrorMsg(err instanceof Error ? err.message : 'Hardware power action failed.')
    }
  }

  return (
    <Dialog open={open} onClose={() => !isSubmitting && onClose()} maxWidth="md">
      <DialogHeader onClose={() => !isSubmitting && onClose()}>
        <div className="flex items-center gap-2.5">
          <div
            className={`p-2 rounded-lg border ${
              isForceOff
                ? 'bg-red-500/10 border-red-500/30 text-red-400'
                : isPowerCycle
                ? 'bg-amber-500/10 border-amber-500/30 text-amber-400'
                : isShutdown
                ? 'bg-amber-500/10 border-amber-500/30 text-amber-400'
                : 'bg-emerald-500/10 border-emerald-500/30 text-emerald-400'
            }`}
          >
            {isForceOff ? (
              <AlertTriangle className="h-5 w-5" />
            ) : isPowerCycle ? (
              <RotateCcw className="h-5 w-5" />
            ) : isShutdown ? (
              <PowerOff className="h-5 w-5" />
            ) : (
              <Power className="h-5 w-5" />
            )}
          </div>
          <div>
            <DialogTitle>Confirm Hardware Action</DialogTitle>
            <p className="text-xs text-zinc-400 mt-0.5">Out-of-band management controller signal</p>
          </div>
        </div>
      </DialogHeader>

      <DialogBody className="space-y-4">
        {/* Target server card */}
        <div className="p-3 bg-zinc-950/60 border border-zinc-800 rounded-lg flex items-center justify-between gap-3 text-xs">
          <div>
            <span className="text-[10px] text-zinc-500 uppercase tracking-wider block">Target Server</span>
            <span className="font-semibold text-zinc-100 text-sm">{targetName}</span>
          </div>
          {targetIp && (
            <div className="text-right">
              <span className="text-[10px] text-zinc-500 uppercase tracking-wider block">BMC / Host IP</span>
              <span className="font-mono text-zinc-300">{targetIp}</span>
            </div>
          )}
        </div>

        {/* Warning callout */}
        <div
          className={`p-3.5 rounded-lg border text-xs leading-relaxed space-y-1.5 ${
            isForceOff
              ? 'bg-red-950/20 border-red-900/50 text-red-200'
              : isPowerCycle
              ? 'bg-amber-950/20 border-amber-900/50 text-amber-200'
              : isShutdown
              ? 'bg-amber-950/20 border-amber-900/40 text-amber-200'
              : 'bg-emerald-950/20 border-emerald-900/40 text-emerald-200'
          }`}
        >
          <div className="flex items-center gap-1.5 font-semibold">
            <ShieldAlert className="h-4 w-4" />
            <span>
              {isForceOff
                ? 'Immediate Force Off Warning'
                : isPowerCycle
                ? 'Hardware Power Cycle Warning'
                : isShutdown
                ? 'Graceful OS Shutdown'
                : 'Server Power On'}
            </span>
          </div>
          <p className="text-[11px] opacity-90">
            {isForceOff &&
              'This command will instantly cut electricity to the motherboard and storage drives. Operating system filesystems and dirty write caches will not be flushed. Unsaved data will be lost.'}
            {isPowerCycle &&
              'This command will cold-cycle physical chassis power. Operating system workloads, virtual machines, and network connections will be abruptly disrupted until reboot completes.'}
            {isShutdown &&
              'This sends an ACPI shutdown signal to the host operating system, allowing Linux/Windows to stop running services and safely unmount filesystems.'}
            {isPowerOn &&
              'This will send a remote BMC power on command to boot up the physical hardware.'}
          </p>
        </div>

        {errorMsg && (
          <div className="p-3 bg-red-950/40 border border-red-800 rounded-lg text-xs text-red-300">
            {errorMsg}
          </div>
        )}
      </DialogBody>

      <DialogFooter>
        <Button
          variant="outline"
          size="sm"
          onClick={onClose}
          disabled={isSubmitting}
          className="border-zinc-700 text-zinc-300 hover:bg-zinc-800"
        >
          Cancel
        </Button>
        <Button
          variant="primary"
          size="sm"
          onClick={handleProceed}
          disabled={isSubmitting}
          className={`gap-2 text-white font-semibold shadow-xs ${
            isForceOff
              ? 'bg-red-600 hover:bg-red-500'
              : isPowerCycle
              ? 'bg-amber-600 hover:bg-amber-500'
              : isShutdown
              ? 'bg-amber-600 hover:bg-amber-500'
              : 'bg-emerald-600 hover:bg-emerald-500'
          }`}
        >
          {isSubmitting ? (
            <>
              <Loader2 className="h-4 w-4 animate-spin" />
              Dispatching...
            </>
          ) : (
            <>
              {isForceOff ? (
                <AlertTriangle className="h-4 w-4" />
              ) : isPowerCycle ? (
                <RotateCcw className="h-4 w-4" />
              ) : isShutdown ? (
                <PowerOff className="h-4 w-4" />
              ) : (
                <Power className="h-4 w-4" />
              )}
              Confirm {actionLabel}
            </>
          )}
        </Button>
      </DialogFooter>
    </Dialog>
  )
}
