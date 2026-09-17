import React, { useState } from 'react'
import {
  AlertTriangle,
  Trash2,
  ShieldAlert,
  Loader2,
  HardDrive,
  Network,
  Boxes,
} from 'lucide-react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../components/ui/dialog'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { useDeleteAppBundle } from './useWorkloads'
import type { WorkloadSummary } from '../../api/workloads'

interface DeleteAppModalProps {
  workload: WorkloadSummary | null
  open: boolean
  onClose: () => void
}

export function DeleteAppModal({ workload, open, onClose }: DeleteAppModalProps) {
  const [deleteWorkload, setDeleteWorkload] = useState(true)
  const [deleteService, setDeleteService] = useState(true)
  const [deleteIngress, setDeleteIngress] = useState(true)
  const [deletePvc, setDeletePvc] = useState(false)
  const [confirmationInput, setConfirmationInput] = useState('')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const deleteMutation = useDeleteAppBundle()

  if (!workload) return null

  const isProtected = workload.isProtected || [
    'kube-system',
    'ingress-nginx',
    'cilium',
    'cert-manager',
    'longhorn-system',
    'controlplane',
    'monitoring',
  ].includes(workload.namespace)

  const isConfirmationValid = !isProtected || confirmationInput.trim() === workload.name.trim()

  const handleDelete = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!isConfirmationValid) return

    setErrorMessage(null)
    try {
      await deleteMutation.mutateAsync({
        clusterId: workload.clusterId,
        namespaceName: workload.namespace,
        name: workload.name,
        options: {
          deleteWorkload,
          deleteService,
          deleteIngress,
          deletePvc,
          confirmedName: confirmationInput.trim() || undefined,
        },
      })
      onClose()
      setConfirmationInput('')
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : 'Failed to delete application setup')
    }
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md">
      <form onSubmit={handleDelete}>
        <DialogHeader onClose={onClose}>
          <div className="flex items-center gap-2 text-rose-400">
            {isProtected ? <ShieldAlert className="h-5 w-5 text-rose-500" /> : <Trash2 className="h-5 w-5" />}
            <DialogTitle>
              {isProtected ? 'Delete Protected System App' : 'Delete Application Setup'}
            </DialogTitle>
          </div>
        </DialogHeader>

        <DialogBody className="space-y-4">
          {/* Target App Card */}
          <div className="p-3.5 rounded-xl bg-zinc-950/80 border border-zinc-800 space-y-1">
            <div className="text-xs text-zinc-400">Application target:</div>
            <div className="text-sm font-mono font-bold text-zinc-100 flex items-center gap-2">
              <span className="text-sky-400">{workload.name}</span>
              <span className="text-zinc-600">in</span>
              <span className="text-amber-400">{workload.namespace}</span>
              <span className="text-zinc-600">({workload.clusterName})</span>
            </div>
          </div>

          {/* Protection Warning */}
          {isProtected && (
            <div className="p-3.5 rounded-xl bg-rose-950/30 border border-rose-800/60 text-rose-300 text-xs space-y-2">
              <div className="flex items-center gap-2 font-semibold">
                <AlertTriangle className="h-4 w-4 text-rose-400 shrink-0" />
                <span>System-Critical Service Protection Triggered</span>
              </div>
              <p className="text-rose-300/90 leading-relaxed">
                This service resides in a protected namespace or has critical system annotations. Deleting it may impact cluster stability. To confirm deletion, you must type the exact name below.
              </p>
            </div>
          )}

          {/* Cascading Resource Selection */}
          <div className="space-y-2">
            <label className="text-xs font-semibold text-zinc-300">Cascading Components to Remove:</label>
            <div className="space-y-2 text-xs">
              <label className="flex items-center gap-2.5 p-2.5 rounded-lg bg-zinc-950/60 border border-zinc-800/80 cursor-pointer hover:bg-zinc-900/60 transition-colors">
                <input
                  type="checkbox"
                  checked={deleteWorkload}
                  onChange={(e) => setDeleteWorkload(e.target.checked)}
                  className="rounded border-zinc-700 text-sky-500 focus:ring-0"
                />
                <Boxes className="h-4 w-4 text-sky-400 shrink-0" />
                <div className="flex-1 text-zinc-200">
                  <span className="font-medium">Workload</span> ({workload.kind || 'Deployment'}/{workload.name})
                </div>
              </label>

              <label className="flex items-center gap-2.5 p-2.5 rounded-lg bg-zinc-950/60 border border-zinc-800/80 cursor-pointer hover:bg-zinc-900/60 transition-colors">
                <input
                  type="checkbox"
                  checked={deleteService}
                  onChange={(e) => setDeleteService(e.target.checked)}
                  className="rounded border-zinc-700 text-sky-500 focus:ring-0"
                />
                <Network className="h-4 w-4 text-emerald-400 shrink-0" />
                <div className="flex-1 text-zinc-200">
                  <span className="font-medium">Kubernetes Service</span> (Service/{workload.name})
                </div>
              </label>

              <label className="flex items-center gap-2.5 p-2.5 rounded-lg bg-zinc-950/60 border border-zinc-800/80 cursor-pointer hover:bg-zinc-900/60 transition-colors">
                <input
                  type="checkbox"
                  checked={deleteIngress}
                  onChange={(e) => setDeleteIngress(e.target.checked)}
                  className="rounded border-zinc-700 text-sky-500 focus:ring-0"
                />
                <Network className="h-4 w-4 text-purple-400 shrink-0" />
                <div className="flex-1 text-zinc-200">
                  <span className="font-medium">NGINX Ingress Route</span> (Ingress/{workload.name})
                </div>
              </label>

              <label className="flex items-start gap-2.5 p-2.5 rounded-lg bg-zinc-950/60 border border-zinc-800/80 cursor-pointer hover:bg-rose-950/20 transition-colors">
                <input
                  type="checkbox"
                  checked={deletePvc}
                  onChange={(e) => setDeletePvc(e.target.checked)}
                  className="rounded border-zinc-700 text-rose-500 focus:ring-0 mt-0.5"
                />
                <HardDrive className="h-4 w-4 text-rose-400 shrink-0 mt-0.5" />
                <div className="flex-1 text-zinc-200">
                  <div className="font-medium text-rose-300">Delete PersistentVolumeClaims (Longhorn Storage)</div>
                  <div className="text-[11px] text-zinc-400 mt-0.5">
                    Destructive: Associated Longhorn storage volumes will be permanently wiped. Unchecked by default.
                  </div>
                </div>
              </label>
            </div>
          </div>

          {/* Typed Name Confirmation Input for Protected Apps */}
          {isProtected && (
            <div className="space-y-1.5 pt-2 border-t border-zinc-800">
              <label className="text-xs font-semibold text-zinc-300">
                Type <span className="text-rose-400 font-mono font-bold select-all">{workload.name}</span> to confirm deletion:
              </label>
              <Input
                placeholder={workload.name}
                value={confirmationInput}
                onChange={(e) => setConfirmationInput(e.target.value)}
                className="font-mono text-sm bg-zinc-950/80 border-rose-900/80 focus:border-rose-500"
              />
            </div>
          )}

          {errorMessage && (
            <div className="p-3 rounded-lg bg-rose-950/40 border border-rose-800 text-rose-300 text-xs">
              {errorMessage}
            </div>
          )}
        </DialogBody>

        <DialogFooter>
          <Button type="button" variant="outline" size="sm" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            variant="destructive"
            size="sm"
            disabled={!isConfirmationValid || deleteMutation.isPending}
            className="gap-1.5"
          >
            {deleteMutation.isPending ? (
              <Loader2 className="h-3.5 w-3.5 animate-spin" />
            ) : (
              <Trash2 className="h-3.5 w-3.5" />
            )}
            Delete App Setup
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  )
}
