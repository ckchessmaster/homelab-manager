import React, { useState } from 'react'
import { Trash2, Loader2, AlertTriangle, AlertOctagon } from 'lucide-react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../../components/ui/dialog'
import { Button } from '../../../components/ui/button'
import { Input } from '../../../components/ui/input'
import { useUninstallHelmRelease } from './useHelm'
import type { HelmReleaseSummary } from '../../../api/helm'

interface UninstallHelmModalProps {
  open: boolean
  onClose: () => void
  clusterId: string
  release: HelmReleaseSummary | null
  onSuccess?: () => void
}

export function UninstallHelmModal({
  open,
  onClose,
  clusterId,
  release,
  onSuccess,
}: UninstallHelmModalProps) {
  const [confirmedName, setConfirmedName] = useState('')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const uninstallMutation = useUninstallHelmRelease(clusterId)

  const isConfirmed = confirmedName.trim() === release?.name

  const handleUninstall = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!release || !isConfirmed || uninstallMutation.isPending) return
    setErrorMessage(null)

    try {
      const result = await uninstallMutation.mutateAsync({
        namespaceName: release.namespace,
        name: release.name,
      })

      if (result.success) {
        onSuccess?.()
        onClose()
      } else {
        setErrorMessage(result.message || 'Uninstall failed.')
      }
    } catch (err: any) {
      setErrorMessage(err?.message || 'Failed to uninstall release.')
    }
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md">
      <DialogHeader>
        <div className="flex items-center gap-2">
          <div className="p-2 rounded-lg bg-red-500/10 border border-red-500/20 text-red-400">
            <Trash2 className="w-5 h-5" />
          </div>
          <div>
            <DialogTitle>Uninstall Helm Release</DialogTitle>
            <p className="text-xs text-zinc-400">
              Permanently delete <span className="font-semibold text-zinc-200">{release?.name}</span>
            </p>
          </div>
        </div>
      </DialogHeader>

      <form onSubmit={handleUninstall}>
        <DialogBody className="space-y-4">
          {errorMessage && (
            <div className="p-3 bg-red-950/40 border border-red-800/60 rounded-lg flex items-start gap-2 text-xs text-red-300">
              <AlertTriangle className="w-4 h-4 text-red-400 shrink-0 mt-0.5" />
              <div className="whitespace-pre-wrap break-all">{errorMessage}</div>
            </div>
          )}

          <div className="p-3 bg-red-950/20 border border-red-900/40 rounded-lg flex items-start gap-2.5">
            <AlertOctagon className="w-4 h-4 text-red-400 shrink-0 mt-0.5" />
            <p className="text-xs text-red-300 leading-relaxed">
              This action will run <code className="font-mono text-zinc-200">helm uninstall</code> on release{' '}
              <strong>{release?.name}</strong> in namespace <strong>{release?.namespace}</strong>. All associated Deployments, Pods, and Services managed by this release will be terminated.
            </p>
          </div>

          <div>
            <label className="block text-xs font-medium text-zinc-300 mb-1.5">
              Type <span className="font-mono font-semibold text-red-400">{release?.name}</span> to confirm:
            </label>
            <Input
              placeholder={release?.name || ''}
              value={confirmedName}
              onChange={(e) => setConfirmedName(e.target.value)}
              className="bg-zinc-900 border-zinc-700 text-zinc-100"
            />
          </div>
        </DialogBody>

        <DialogFooter className="flex items-center justify-between">
          <Button type="button" variant="outline" onClick={onClose}>
            Cancel
          </Button>

          <Button
            type="submit"
            disabled={!isConfirmed || uninstallMutation.isPending}
            className="bg-red-600 hover:bg-red-500 text-white flex items-center gap-1.5"
          >
            {uninstallMutation.isPending ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                Uninstalling...
              </>
            ) : (
              <>
                <Trash2 className="w-4 h-4" />
                Uninstall Release
              </>
            )}
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  )
}
