import { useState } from 'react'
import {
  Layers,
  ShieldAlert,
  Trash2,
  Filter,
  Boxes,
  Plus,
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
import { isSystemCriticalNamespace } from '../../api/workloads'
import { DeleteNamespaceModal } from './DeleteNamespaceModal'
import { CreateNamespaceModal } from './CreateNamespaceModal'
import type { WorkloadSummary } from '../../api/workloads'

interface ManageNamespacesModalProps {
  open: boolean
  onClose: () => void
  clusterId: string
  namespaces: string[]
  workloads: WorkloadSummary[]
  onSelectNamespace: (ns: string) => void
}

export function ManageNamespacesModal({
  open,
  onClose,
  clusterId,
  namespaces,
  workloads,
  onSelectNamespace,
}: ManageNamespacesModalProps) {
  const [namespaceToDelete, setNamespaceToDelete] = useState<string | null>(null)
  const [isCreateOpen, setIsCreateOpen] = useState(false)

  const getWorkloadCountForNs = (ns: string) => {
    return workloads.filter((w) => w.namespace.toLowerCase() === ns.toLowerCase()).length
  }

  return (
    <>
      <Dialog open={open} onClose={onClose} maxWidth="lg">
        <DialogHeader onClose={onClose}>
          <div className="flex items-center gap-2 text-zinc-100">
            <Layers className="h-5 w-5 text-amber-400" />
            <DialogTitle>Cluster Namespaces: {clusterId}</DialogTitle>
          </div>
        </DialogHeader>

        <DialogBody className="space-y-4">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <p className="text-xs text-zinc-400">
              Manage workload partitions across your cluster. System-critical namespaces are protected
              against deletion to safeguard core cluster operations.
            </p>
            <Button
              type="button"
              size="sm"
              onClick={() => setIsCreateOpen(true)}
              className="bg-sky-600 hover:bg-sky-500 text-white text-xs shrink-0 self-start sm:self-auto"
            >
              <Plus className="h-3.5 w-3.5 mr-1" />
              New Namespace
            </Button>
          </div>

          <div className="rounded-xl border border-zinc-800/80 bg-zinc-950/60 overflow-hidden">
            <div className="max-h-[400px] overflow-y-auto divide-y divide-zinc-800/60">
              {namespaces.length === 0 ? (
                <div className="p-8 text-center text-xs text-zinc-500">
                  No namespaces discovered for cluster {clusterId}.
                </div>
              ) : (
                namespaces.map((ns) => {
                  const isProtected = isSystemCriticalNamespace(ns)
                  const count = getWorkloadCountForNs(ns)

                  return (
                    <div
                      key={ns}
                      className="flex items-center justify-between p-3.5 hover:bg-zinc-900/40 transition-colors gap-3"
                    >
                      <div className="flex items-center gap-3 min-w-0">
                        <div className="p-2 rounded-lg bg-zinc-900 border border-zinc-800 shrink-0">
                          {isProtected ? (
                            <ShieldAlert className="h-4 w-4 text-amber-400" />
                          ) : (
                            <Layers className="h-4 w-4 text-zinc-400" />
                          )}
                        </div>
                        <div className="min-w-0">
                          <div className="flex items-center gap-2">
                            <span className="font-mono text-xs font-semibold text-zinc-200 truncate">
                              {ns}
                            </span>
                            {isProtected ? (
                              <Badge
                                variant="outline"
                                className="bg-amber-950/40 text-amber-300 border-amber-800/60 text-[10px] py-0"
                              >
                                System-Critical
                              </Badge>
                            ) : (
                              <Badge
                                variant="outline"
                                className="bg-zinc-800/60 text-zinc-300 border-zinc-700/60 text-[10px] py-0"
                              >
                                User Namespace
                              </Badge>
                            )}
                          </div>
                          <div className="flex items-center gap-1 text-[11px] text-zinc-500 mt-0.5">
                            <Boxes className="h-3 w-3" />
                            <span>
                              {count} workload{count === 1 ? '' : 's'}
                            </span>
                          </div>
                        </div>
                      </div>

                      <div className="flex items-center gap-2 shrink-0">
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          onClick={() => {
                            onSelectNamespace(ns)
                            onClose()
                          }}
                          className="text-xs h-7 px-2.5 bg-zinc-900 hover:bg-zinc-800 text-zinc-300 border-zinc-700"
                        >
                          <Filter className="h-3 w-3 mr-1 text-zinc-400" />
                          Filter
                        </Button>

                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          disabled={isProtected}
                          onClick={() => setNamespaceToDelete(ns)}
                          className={`text-xs h-7 px-2.5 ${
                            isProtected
                              ? 'text-zinc-600 opacity-40 cursor-not-allowed'
                              : 'text-rose-400 hover:text-rose-300 hover:bg-rose-950/40'
                          }`}
                          title={isProtected ? 'System-critical namespace cannot be deleted' : 'Delete namespace'}
                        >
                          <Trash2 className="h-3.5 w-3.5 mr-1" />
                          Delete
                        </Button>
                      </div>
                    </div>
                  )
                })
              )}
            </div>
          </div>
        </DialogBody>

        <DialogFooter>
          <Button type="button" variant="outline" size="sm" onClick={onClose}>
            Done
          </Button>
        </DialogFooter>
      </Dialog>

      {/* Delete Confirmation Modal */}
      {namespaceToDelete && (
        <DeleteNamespaceModal
          open={Boolean(namespaceToDelete)}
          onClose={() => setNamespaceToDelete(null)}
          clusterId={clusterId}
          namespaceName={namespaceToDelete}
          workloadCount={getWorkloadCountForNs(namespaceToDelete)}
          onSuccess={() => {
            setNamespaceToDelete(null)
          }}
        />
      )}

      {/* Create Namespace Modal */}
      {isCreateOpen && (
        <CreateNamespaceModal
          open={isCreateOpen}
          onClose={() => setIsCreateOpen(false)}
          clusterId={clusterId}
          onSuccess={(created) => {
            onSelectNamespace(created)
            setIsCreateOpen(false)
          }}
        />
      )}
    </>
  )
}
