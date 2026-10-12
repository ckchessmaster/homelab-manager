import { useState } from 'react'
import {
  KeyRound,
  AlertTriangle,
  RotateCcw,
  CheckCircle2,
  Loader2,
  Clock,
  ShieldCheck,
  AlertOctagon,
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
import { useRearmWindowsLicense } from './useHosts'
import type { Host } from '../../api/hosts'

export interface RearmLicenseModalProps {
  host: Host | null
  open: boolean
  onClose: () => void
  onRearmSuccess?: (jobId?: string | null) => void
}

export function RearmLicenseModal({
  host,
  open,
  onClose,
  onRearmSuccess,
}: RearmLicenseModalProps) {
  const [rebootAfter, setRebootAfter] = useState(true)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)
  const [isSuccess, setIsSuccess] = useState(false)
  const rearmMutation = useRearmWindowsLicense()

  if (!open || !host) return null

  const license = host.windowsLicense
  const rearmsRemaining = license?.rearmsRemaining ?? 0
  const canRearm = rearmsRemaining > 0

  const handleRearm = async () => {
    if (!canRearm) return
    setErrorMsg(null)

    try {
      const res = await rearmMutation.mutateAsync({
        hostId: host.id,
        rebootAfter,
      })

      if (!res.success) {
        setErrorMsg(res.message || 'Failed to rearm Windows license')
        return
      }

      setIsSuccess(true)
      setTimeout(() => {
        setIsSuccess(false)
        onRearmSuccess?.(res.jobId)
        onClose()
      }, 1200)
    } catch (err: unknown) {
      const msg =
        err && typeof err === 'object' && 'response' in err
          ? (err as { response?: { data?: { message?: string } } }).response?.data?.message ??
            'Failed to dispatch Windows license rearm workflow'
          : 'Failed to dispatch Windows license rearm workflow'
      setErrorMsg(msg)
    }
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md">
      <DialogHeader>
        <div className="flex items-center gap-3">
          <div className="p-2.5 rounded-xl bg-sky-950/60 border border-sky-800/60 text-sky-400">
            <KeyRound className="h-5 w-5" />
          </div>
          <div>
            <DialogTitle className="text-base text-zinc-100 flex items-center gap-2">
              <span>Rearm Windows Evaluation License</span>
              <Badge variant="info" className="text-[10px] uppercase font-mono tracking-wider">
                Temporal Workflow
              </Badge>
            </DialogTitle>
            <p className="text-xs text-zinc-400 mt-0.5">
              Target Host: <span className="text-zinc-200 font-semibold">{host.hostname}</span>{' '}
              <span className="font-mono text-zinc-400">({host.ipAddress})</span>
            </p>
          </div>
        </div>
      </DialogHeader>

      <DialogBody className="space-y-4 py-2">
        {/* Success Banner */}
        {isSuccess && (
          <div className="p-3 bg-emerald-950/50 border border-emerald-600/60 rounded-xl flex items-center gap-2.5 text-xs text-emerald-300 animate-in fade-in zoom-in-95 duration-200">
            <CheckCircle2 className="h-4 w-4 text-emerald-400 shrink-0" />
            <span>Rearm workflow dispatched successfully! Refreshing license status...</span>
          </div>
        )}

        {/* Error Banner */}
        {errorMsg && (
          <div className="p-3 bg-rose-950/50 border border-rose-800/60 rounded-xl flex items-center gap-2.5 text-xs text-rose-300">
            <AlertTriangle className="h-4 w-4 text-rose-400 shrink-0" />
            <span>{errorMsg}</span>
          </div>
        )}

        {/* 0 Rearms Exhausted Warning */}
        {!canRearm && (
          <div className="p-3.5 bg-rose-950/60 border border-rose-700/60 rounded-xl text-xs space-y-2">
            <div className="flex items-center gap-2 text-rose-300 font-semibold">
              <AlertOctagon className="h-4 w-4 text-rose-400 shrink-0" />
              <span>Evaluation Rearm Limit Reached (0 Rearms Left)</span>
            </div>
            <p className="text-rose-200/90 leading-relaxed text-[11px]">
              This Windows Server installation has consumed all available rearm cycles. The evaluation period cannot be extended further. To maintain system functionality without automatic shutdowns, please activate Windows with a genuine KMS or Retail product key:
            </p>
            <div className="p-2 bg-zinc-950/80 rounded border border-rose-900/60 font-mono text-[11px] text-zinc-300 select-all">
              slmgr.vbs /ipk &lt;YOUR-PRODUCT-KEY&gt;
            </div>
          </div>
        )}

        {/* License Details Card */}
        <div className="p-3.5 bg-zinc-900/80 border border-zinc-800 rounded-xl space-y-3">
          <div className="flex items-center justify-between text-xs pb-2 border-b border-zinc-800">
            <span className="text-zinc-400">Windows Edition</span>
            <span className="font-medium text-zinc-200">
              {license?.edition || 'Windows Server'}
            </span>
          </div>

          <div className="grid grid-cols-2 gap-3 text-xs">
            <div className="p-2.5 rounded-lg bg-zinc-950/60 border border-zinc-800/60">
              <span className="text-[10px] text-zinc-400 uppercase tracking-wider flex items-center gap-1">
                <Clock className="w-3 h-3 text-sky-400" />
                Current Expiration
              </span>
              <p className="font-semibold text-zinc-100 mt-1">
                {license ? `${license.daysRemaining} days remaining` : 'Unknown'}
              </p>
              <p className="text-[10px] text-zinc-500 mt-0.5">
                {license?.expirationDate ? new Date(license.expirationDate).toLocaleDateString() : 'Evaluation Mode'}
              </p>
            </div>

            <div className="p-2.5 rounded-lg bg-zinc-950/60 border border-zinc-800/60">
              <span className="text-[10px] text-zinc-400 uppercase tracking-wider flex items-center gap-1">
                <KeyRound className="w-3 h-3 text-amber-400" />
                Rearms Remaining
              </span>
              <p className="font-semibold text-zinc-100 mt-1">
                {rearmsRemaining} {rearmsRemaining === 1 ? 'rearm' : 'rearms'} left
              </p>
              <p className="text-[10px] text-zinc-500 mt-0.5">
                {canRearm ? `Projected: ${rearmsRemaining - 1} left after rearm` : 'Limit reached'}
              </p>
            </div>
          </div>

          <div className="flex items-center justify-between text-xs pt-1 text-zinc-400">
            <span>License Channel</span>
            <span className="font-mono text-zinc-300">{license?.licenseStatus || 'TIMEBASED_EVAL'}</span>
          </div>
          {license?.partialProductKey && (
            <div className="flex items-center justify-between text-xs text-zinc-400">
              <span>Partial Key</span>
              <span className="font-mono text-zinc-300">...-{license.partialProductKey}</span>
            </div>
          )}
        </div>

        {/* Auto Reboot Option */}
        {canRearm && (
          <label className="flex items-start gap-3 p-3 bg-zinc-950/40 border border-zinc-800/60 rounded-xl cursor-pointer hover:bg-zinc-900/40 transition-colors">
            <input
              type="checkbox"
              checked={rebootAfter}
              onChange={(e) => setRebootAfter(e.target.checked)}
              className="mt-0.5 h-4 w-4 rounded border-zinc-700 bg-zinc-900 text-sky-500 focus:ring-sky-500/20"
            />
            <div className="space-y-0.5">
              <div className="flex items-center gap-1.5 text-xs font-medium text-zinc-200">
                <RotateCcw className="h-3.5 w-3.5 text-amber-400" />
                <span>Reboot host automatically after rearm (recommended)</span>
              </div>
              <p className="text-[11px] text-zinc-400 leading-relaxed">
                Windows requires a system restart for <code className="text-zinc-300 font-mono">slmgr.vbs /rearm</code> to activate the new 180-day grace period. The Temporal workflow will initiate a graceful reboot and await agent reconnection.
              </p>
            </div>
          </label>
        )}
      </DialogBody>

      <DialogFooter className="flex items-center justify-end gap-2 pt-2">
        <Button variant="ghost" size="sm" onClick={onClose} disabled={rearmMutation.isPending}>
          Cancel
        </Button>
        <Button
          variant="primary"
          size="sm"
          onClick={handleRearm}
          disabled={!canRearm || rearmMutation.isPending || isSuccess}
          className="bg-sky-600 hover:bg-sky-500 text-white gap-1.5 font-medium min-w-[120px]"
        >
          {rearmMutation.isPending ? (
            <>
              <Loader2 className="h-3.5 w-3.5 animate-spin" />
              <span>Rearming...</span>
            </>
          ) : (
            <>
              <ShieldCheck className="h-3.5 w-3.5" />
              <span>Rearm License</span>
            </>
          )}
        </Button>
      </DialogFooter>
    </Dialog>
  )
}
