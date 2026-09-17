import React, { useState } from 'react'
import { RotateCcw, Loader2, AlertTriangle, CheckCircle2, History } from 'lucide-react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../../components/ui/dialog'
import { Button } from '../../../components/ui/button'
import { useRollbackHelmRelease, useHelmReleaseHistory } from './useHelm'
import type { HelmReleaseSummary } from '../../../api/helm'

interface RollbackHelmModalProps {
  open: boolean
  onClose: () => void
  clusterId: string
  release: HelmReleaseSummary | null
  onSuccess?: () => void
}

export function RollbackHelmModal({
  open,
  onClose,
  clusterId,
  release,
  onSuccess,
}: RollbackHelmModalProps) {
  const [selectedRevision, setSelectedRevision] = useState<number | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const { data: history = [], isLoading: historyLoading } = useHelmReleaseHistory(
    clusterId,
    release?.namespace || '',
    release?.name || ''
  )

  const rollbackMutation = useRollbackHelmRelease(clusterId)

  // Filter out current active revision from rollback targets
  const candidateRevisions = history.filter((r) => r.revision !== release?.revision)

  const handleRollback = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!release || !selectedRevision || rollbackMutation.isPending) return
    setErrorMessage(null)

    try {
      const result = await rollbackMutation.mutateAsync({
        namespaceName: release.namespace,
        name: release.name,
        revision: selectedRevision,
      })

      if (result.success) {
        onSuccess?.()
        onClose()
      } else {
        setErrorMessage(result.message || 'Rollback failed.')
      }
    } catch (err: any) {
      setErrorMessage(err?.message || 'Rollback request failed.')
    }
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md">
      <DialogHeader>
        <div className="flex items-center gap-2">
          <div className="p-2 rounded-lg bg-amber-500/10 border border-amber-500/20 text-amber-400">
            <RotateCcw className="w-5 h-5" />
          </div>
          <div>
            <DialogTitle>Rollback Release</DialogTitle>
            <p className="text-xs text-zinc-400">
              Revert <span className="font-semibold text-zinc-200">{release?.name}</span> to a previous revision
            </p>
          </div>
        </div>
      </DialogHeader>

      <form onSubmit={handleRollback}>
        <DialogBody className="space-y-3">
          {errorMessage && (
            <div className="p-3 bg-red-950/40 border border-red-800/60 rounded-lg flex items-start gap-2 text-xs text-red-300">
              <AlertTriangle className="w-4 h-4 text-red-400 shrink-0 mt-0.5" />
              <div className="whitespace-pre-wrap break-all">{errorMessage}</div>
            </div>
          )}

          <div className="p-3 bg-zinc-900 border border-zinc-800 rounded-lg text-xs space-y-1.5">
            <div className="flex justify-between">
              <span className="text-zinc-400">Current Revision:</span>
              <span className="font-mono text-indigo-400 font-semibold">rev {release?.revision}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-zinc-400">Current Chart:</span>
              <span className="text-zinc-300">{release?.chart}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-zinc-400">Namespace:</span>
              <span className="text-zinc-300">{release?.namespace}</span>
            </div>
          </div>

          <div>
            <label className="block text-xs font-medium text-zinc-300 mb-1.5 flex items-center gap-1.5">
              <History className="w-3.5 h-3.5 text-zinc-400" />
              Target Revision to Restore:
            </label>

            {historyLoading ? (
              <div className="py-4 text-center text-xs text-zinc-500">Loading revision history...</div>
            ) : candidateRevisions.length === 0 ? (
              <div className="p-3 bg-zinc-900/50 border border-zinc-800 rounded-lg text-xs text-zinc-400 text-center">
                No previous revisions available to roll back to.
              </div>
            ) : (
              <div className="space-y-1.5 max-h-48 overflow-y-auto">
                {candidateRevisions.map((rev) => (
                  <label
                    key={rev.revision}
                    className={`p-2.5 rounded-lg border flex items-center justify-between cursor-pointer transition-all ${
                      selectedRevision === rev.revision
                        ? 'bg-amber-500/10 border-amber-500/40 text-zinc-100'
                        : 'bg-zinc-900/60 border-zinc-800 text-zinc-300 hover:bg-zinc-800/60'
                    }`}
                  >
                    <div className="flex items-center gap-2">
                      <input
                        type="radio"
                        name="rollback-revision"
                        value={rev.revision}
                        checked={selectedRevision === rev.revision}
                        onChange={() => setSelectedRevision(rev.revision)}
                        className="text-amber-500 focus:ring-amber-500 bg-zinc-900 border-zinc-700"
                      />
                      <div>
                        <div className="text-xs font-semibold">Revision {rev.revision}</div>
                        <div className="text-[10px] text-zinc-500">
                          {rev.chart} • {new Date(rev.updated).toLocaleString()}
                        </div>
                      </div>
                    </div>
                    <span className="text-[10px] uppercase font-mono px-1.5 py-0.5 rounded bg-zinc-800 text-zinc-400">
                      {rev.status}
                    </span>
                  </label>
                ))}
              </div>
            )}
          </div>
        </DialogBody>

        <DialogFooter className="flex items-center justify-between">
          <Button type="button" variant="outline" onClick={onClose}>
            Cancel
          </Button>

          <Button
            type="submit"
            disabled={!selectedRevision || rollbackMutation.isPending}
            className="bg-amber-600 hover:bg-amber-500 text-white flex items-center gap-1.5"
          >
            {rollbackMutation.isPending ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                Rolling Back...
              </>
            ) : (
              <>
                <CheckCircle2 className="w-4 h-4" />
                Rollback Release
              </>
            )}
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  )
}
