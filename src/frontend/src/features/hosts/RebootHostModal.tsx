import { useState } from 'react'
import {
  RotateCcw,
  AlertTriangle,
  Server,
  Loader2,
  CheckCircle2,
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
import { useRebootHost } from './useHosts'
import type { Host } from '../../api/hosts'

interface RebootHostModalProps {
  host?: Host | null
  bulkHosts?: Host[] | null
  open: boolean
  onClose: () => void
  onRebootSuccess?: (jobId: string, host: Host) => void
}

export function RebootHostModal({
  host,
  bulkHosts,
  open,
  onClose,
  onRebootSuccess,
}: RebootHostModalProps) {
  const rebootMutation = useRebootHost()
  const [errorMsg, setErrorMsg] = useState<string | null>(null)
  const [isSuccess, setIsSuccess] = useState(false)

  const targetHosts = bulkHosts && bulkHosts.length > 0 ? bulkHosts : host ? [host] : []
  const isBulk = targetHosts.length > 1

  const handleConfirm = async () => {
    setErrorMsg(null)
    if (targetHosts.length === 0) return

    try {
      for (const h of targetHosts) {
        const res = await rebootMutation.mutateAsync(h.id)
        if (!isBulk && onRebootSuccess) {
          onRebootSuccess(res.jobId, h)
        }
      }
      setIsSuccess(true)
      setTimeout(() => {
        setIsSuccess(false)
        onClose()
      }, 1200)
    } catch (err: unknown) {
      const msg =
        err && typeof err === 'object' && 'response' in err
          ? (err as { response?: { data?: { message?: string } } }).response?.data?.message ??
            'Failed to dispatch reboot command'
          : 'Failed to dispatch reboot command'
      setErrorMsg(msg)
    }
  }

  if (!open || targetHosts.length === 0) return null

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md">
      <DialogHeader>
        <div className="flex items-center gap-3">
          <div className="p-2.5 rounded-xl bg-amber-500/10 border border-amber-500/20 text-amber-400">
            <RotateCcw className="w-5 h-5" />
          </div>
          <div>
            <DialogTitle>
              {isBulk ? `Reboot ${targetHosts.length} Hosts` : `Reboot ${targetHosts[0].hostname}`}
            </DialogTitle>
            <p className="text-xs text-zinc-400 mt-0.5">
              {isBulk
                ? 'Issue graceful system restart across multiple nodes'
                : 'Gracefully restart system and reload running kernel'}
            </p>
          </div>
        </div>
      </DialogHeader>

      <DialogBody className="space-y-4 pt-2">
        {errorMsg && (
          <div className="p-3 bg-rose-950/40 border border-rose-800/50 rounded-lg text-xs text-rose-300 flex items-start gap-2">
            <AlertTriangle className="w-4 h-4 text-rose-400 shrink-0 mt-0.5" />
            <span>{errorMsg}</span>
          </div>
        )}

        {isSuccess && (
          <div className="p-3 bg-emerald-950/40 border border-emerald-800/50 rounded-lg text-xs text-emerald-300 flex items-center gap-2">
            <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
            <span>Reboot command successfully dispatched!</span>
          </div>
        )}

        {/* Warning Banner */}
        <div className="p-3.5 bg-amber-950/30 border border-amber-800/40 rounded-xl space-y-2">
          <div className="flex items-center gap-2 text-amber-300 text-xs font-semibold">
            <AlertTriangle className="w-4 h-4 text-amber-400 shrink-0" />
            <span>Service Interruption Warning</span>
          </div>
          <p className="text-xs text-zinc-400 leading-relaxed">
            The agent daemon will issue <code className="text-amber-300 bg-amber-950/60 px-1 py-0.5 rounded text-[11px]">systemctl reboot</code>.
            Workloads and running services on {isBulk ? 'these nodes' : 'this node'} will be temporarily stopped until the operating system completes its reboot cycle.
          </p>
        </div>

        {/* Target Hosts Summary */}
        <div className="border border-zinc-800/80 rounded-xl bg-zinc-950/50 p-3 divide-y divide-zinc-800/60 max-h-48 overflow-y-auto">
          {targetHosts.map((h) => (
            <div key={h.id} className="py-2 first:pt-0 last:pb-0 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Server className="w-4 h-4 text-zinc-500 shrink-0" />
                <div>
                  <span className="text-xs font-medium text-zinc-200">{h.hostname}</span>
                  <span className="text-[11px] text-zinc-500 ml-2 font-mono">{h.ipAddress}</span>
                </div>
              </div>
              <div className="flex items-center gap-1.5">
                {h.agent.pendingReboot && (
                  <Badge variant="warning" className="text-[10px] px-1.5 py-0.5">
                    Kernel Pending
                  </Badge>
                )}
                {!h.agent.installed && (
                  <Badge variant="destructive" className="text-[10px] px-1.5 py-0.5">
                    Offline
                  </Badge>
                )}
              </div>
            </div>
          ))}
        </div>
      </DialogBody>

      <DialogFooter>
        <Button variant="ghost" size="sm" onClick={onClose} disabled={rebootMutation.isPending}>
          Cancel
        </Button>
        <Button
          variant="primary"
          size="sm"
          onClick={handleConfirm}
          disabled={rebootMutation.isPending || isSuccess}
          className="gap-1.5 bg-amber-600 hover:bg-amber-500 text-white border-amber-500"
        >
          {rebootMutation.isPending ? (
            <>
              <Loader2 className="w-3.5 h-3.5 animate-spin" />
              Rebooting...
            </>
          ) : (
            <>
              <RotateCcw className="w-3.5 h-3.5" />
              {isBulk ? `Reboot ${targetHosts.length} Hosts` : 'Reboot Node'}
            </>
          )}
        </Button>
      </DialogFooter>
    </Dialog>
  )
}
