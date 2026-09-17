import React, { useState } from 'react'
import {
  AlertTriangle,
  Trash2,
  ShieldAlert,
  Loader2,
  HardDrive,
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
import { useDeleteNamespace } from './useWorkloads'
import { isSystemCriticalNamespace } from '../../api/workloads'

interface DeleteNamespaceModalProps {
  open: boolean
  onClose: () => void
  clusterId: string
  namespaceName: string
  workloadCount?: number
  pvcCount?: number
  onSuccess?: () => void
}

export function DeleteNamespaceModal({
  open,
  onClose,
  clusterId,
  namespaceName,
  workloadCount = 0,
  pvcCount = 0,
  onSuccess,
}: DeleteNamespaceModalProps) {
  const [confirmationInput, setConfirmationInput] = useState('')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const deleteMutation = useDeleteNamespace()
  const isProtected = isSystemCriticalNamespace(namespaceName)
  const isConfirmationValid = !isProtected && confirmationInput.trim() === namespaceName.trim()

  const handleDelete = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!isConfirmationValid || isProtected) return

    setErrorMessage(null)
    try {
      await deleteMutation.mutateAsync({
        clusterId,
        namespaceName,
        confirmedName: confirmationInput.trim(),
      })
      setConfirmationInput('')
      onSuccess?.()
      onClose()
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : 'Failed to delete namespace')
    }
  }

  const handleClose = () => {
    setConfirmationInput('')
    setErrorMessage(null)
    onClose()
  }

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="md">
      <form onSubmit={handleDelete}>
        <DialogHeader onClose={handleClose}>
          <div className="flex items-center gap-2 text-rose-400">
            {isProtected ? (
              <ShieldAlert className="h-5 w-5 text-rose-500" />
            ) : (
              <Trash2 className="h-5 w-5 text-rose-500" />
            )}
            <DialogTitle>
              {isProtected ? 'System-Critical Namespace Protected' : 'Delete Entire Namespace'}
            </DialogTitle>
          </div>
        </DialogHeader>

        <DialogBody className="space-y-4">
          {errorMessage && (
            <div className="p-3 bg-rose-950/80 border border-rose-800 rounded-lg text-rose-300 text-xs flex items-center gap-2">
              <AlertTriangle className="h-4 w-4 shrink-0 text-rose-400" />
              <span>{errorMessage}</span>
            </div>
          )}

          {isProtected ? (
            <div className="space-y-3">
              <div className="p-3.5 bg-rose-950/40 border border-rose-800/80 rounded-xl space-y-2">
                <div className="flex items-center gap-2 text-rose-300 text-sm font-semibold">
                  <ShieldAlert className="h-4 w-4 text-rose-400" />
                  Deletion Forbidden
                </div>
                <p className="text-xs text-rose-300/80 leading-relaxed">
                  The namespace <span className="font-mono font-bold text-rose-200">{namespaceName}</span> is
                  flagged as system-critical. It houses essential cluster operations (such as core control plane,
                  ingress controllers, CNI networking, storage drivers, or monitoring).
                </p>
                <p className="text-xs text-rose-400 font-medium">
                  To protect the cluster against accidental outage, system-critical namespaces cannot be deleted.
                </p>
              </div>
            </div>
          ) : (
            <div className="space-y-4">
              {/* Warning Banner */}
              <div className="p-3.5 bg-rose-950/40 border border-rose-800/80 rounded-xl space-y-2">
                <div className="flex items-center gap-2 text-rose-300 text-sm font-semibold">
                  <AlertTriangle className="h-4 w-4 text-rose-400" />
                  Destructive Action: Irreversible Deletion
                </div>
                <p className="text-xs text-rose-300/80 leading-relaxed">
                  You are about to delete namespace{' '}
                  <span className="font-mono font-bold text-rose-200">{namespaceName}</span> in cluster{' '}
                  <span className="font-mono font-semibold text-zinc-300">{clusterId}</span>.
                </p>
              </div>

              {/* Resource Impact Summary */}
              <div className="p-3 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2.5">
                <span className="text-[11px] font-semibold text-zinc-400 uppercase tracking-wider">
                  Resource Impact in This Namespace
                </span>
                <div className="grid grid-cols-2 gap-2 text-xs">
                  <div className="flex items-center gap-2 p-2 rounded-lg bg-zinc-900/80 border border-zinc-800">
                    <Boxes className="h-4 w-4 text-sky-400" />
                    <div>
                      <div className="font-semibold text-zinc-200">{workloadCount} Workloads</div>
                      <div className="text-[10px] text-zinc-500">Deployments, Sets, CronJobs</div>
                    </div>
                  </div>
                  <div className="flex items-center gap-2 p-2 rounded-lg bg-zinc-900/80 border border-zinc-800">
                    <HardDrive className="h-4 w-4 text-amber-400" />
                    <div>
                      <div className="font-semibold text-zinc-200">{pvcCount} Volume Claims</div>
                      <div className="text-[10px] text-zinc-500">Longhorn / Storage PVCs</div>
                    </div>
                  </div>
                </div>
              </div>

              {/* Specific consequences */}
              <ul className="text-xs text-zinc-400 space-y-1.5 list-disc list-inside">
                <li>All active pods will be stopped and their containers terminated.</li>
                <li>All Kubernetes Services, Ingresses, Secrets, and ConfigMaps will be erased.</li>
                <li>Attached PVCs will be deleted and underlying volumes reclaimed.</li>
                <li>Namespace finalizers will execute on the apiserver.</li>
              </ul>

              {/* Typed Confirmation Input */}
              <div className="space-y-1.5 pt-2 border-t border-zinc-800/80">
                <label className="text-xs font-medium text-zinc-300">
                  To confirm, type <span className="font-mono font-bold text-rose-400">{namespaceName}</span> below:
                </label>
                <Input
                  value={confirmationInput}
                  onChange={(e) => setConfirmationInput(e.target.value)}
                  placeholder={namespaceName}
                  className="bg-zinc-950 font-mono text-xs border-rose-900/50 focus:border-rose-500"
                  autoFocus
                />
              </div>
            </div>
          )}
        </DialogBody>

        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={handleClose}
            disabled={deleteMutation.isPending}
          >
            {isProtected ? 'Close' : 'Cancel'}
          </Button>

          {!isProtected && (
            <Button
              type="submit"
              variant="destructive"
              size="sm"
              disabled={!isConfirmationValid || deleteMutation.isPending}
              className="bg-rose-600 hover:bg-rose-700 text-white font-medium"
            >
              {deleteMutation.isPending ? (
                <>
                  <Loader2 className="h-3.5 w-3.5 animate-spin mr-1.5" />
                  Deleting Namespace...
                </>
              ) : (
                <>
                  <Trash2 className="h-3.5 w-3.5 mr-1.5" />
                  Delete Entire Namespace
                </>
              )}
            </Button>
          )}
        </DialogFooter>
      </form>
    </Dialog>
  )
}
