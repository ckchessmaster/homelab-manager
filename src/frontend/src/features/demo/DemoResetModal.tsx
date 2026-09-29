import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogBody,
  DialogFooter,
} from '../../components/ui/dialog'
import { resetDemoData } from '../../api/system'
import { Sparkles, RotateCcw, AlertTriangle, CheckCircle2 } from 'lucide-react'

interface DemoResetModalProps {
  open: boolean
  onClose: () => void
}

export function DemoResetModal({ open, onClose }: DemoResetModalProps) {
  const queryClient = useQueryClient()
  const [successMessage, setSuccessMessage] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: () => resetDemoData(),
    onSuccess: (data) => {
      setSuccessMessage(`Reset complete! ${data.hostCount} demo hosts restored.`)
      // Invalidate all react-query caches to refresh hosts, workloads, adapters, jobs, etc.
      queryClient.invalidateQueries()
      setTimeout(() => {
        setSuccessMessage(null)
        onClose()
      }, 1400)
    },
  })

  const handleClose = () => {
    if (mutation.isPending) return
    setSuccessMessage(null)
    mutation.reset()
    onClose()
  }

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="md">
      <DialogHeader onClose={handleClose}>
        <div className="flex items-center gap-2">
          <div className="h-7 w-7 rounded-lg bg-amber-500/10 border border-amber-500/30 flex items-center justify-center">
            <Sparkles className="h-4 w-4 text-amber-400" />
          </div>
          <div>
            <DialogTitle>Reset Demo Environment</DialogTitle>
            <DialogDescription>
              Restore simulated homelab fleet and adapter mocks to initial state.
            </DialogDescription>
          </div>
        </div>
      </DialogHeader>

      <DialogBody>
        <div className="space-y-3">
          <p className="text-zinc-300 text-xs leading-relaxed">
            Resetting demo data will wipe all in-memory hosts, active or completed update jobs, step logs,
            and recreate the default 5-node homelab fleet:
          </p>

          <div className="p-3 bg-zinc-950/60 rounded-lg border border-zinc-800/80 text-xs text-zinc-400 space-y-1.5 font-mono">
            <div>• <span className="text-zinc-200">pve-node-01</span> (Proxmox VE + iDRAC9 BMC)</div>
            <div>• <span className="text-zinc-200">k8s-cp-01</span> (Kubernetes Control Plane)</div>
            <div>• <span className="text-zinc-200">k8s-worker-01</span> & <span className="text-zinc-200">k8s-worker-02</span> (Worker Nodes)</div>
            <div>• <span className="text-zinc-200">storage-nas-01</span> (TrueNAS Storage Node)</div>
            <div>• Mock Adapters: Proxmox, K8s, UniFi, OPNsense, Redfish, Home Assistant</div>
          </div>

          {mutation.isError && (
            <div className="flex items-center gap-2 p-2.5 bg-rose-950/40 border border-rose-800/80 rounded-lg text-xs text-rose-300">
              <AlertTriangle className="h-4 w-4 shrink-0 text-rose-400" />
              <span>
                {mutation.error instanceof Error ? mutation.error.message : 'Failed to reset demo data.'}
              </span>
            </div>
          )}

          {successMessage && (
            <div className="flex items-center gap-2 p-2.5 bg-emerald-950/40 border border-emerald-800/80 rounded-lg text-xs text-emerald-300">
              <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-400" />
              <span>{successMessage}</span>
            </div>
          )}
        </div>
      </DialogBody>

      <DialogFooter>
        <button
          type="button"
          onClick={handleClose}
          disabled={mutation.isPending}
          className="px-3.5 py-1.5 text-xs text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60 rounded-lg transition-colors disabled:opacity-50"
        >
          Cancel
        </button>
        <button
          type="button"
          onClick={() => mutation.mutate()}
          disabled={mutation.isPending}
          className="flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-medium bg-amber-600 hover:bg-amber-500 text-white rounded-lg transition-colors shadow-xs disabled:opacity-50"
        >
          <RotateCcw className={`h-3.5 w-3.5 ${mutation.isPending ? 'animate-spin' : ''}`} />
          <span>{mutation.isPending ? 'Resetting Fleet...' : 'Reset to Baseline'}</span>
        </button>
      </DialogFooter>
    </Dialog>
  )
}
