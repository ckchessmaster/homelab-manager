import React, { useState } from 'react'
import {
  Camera,
  Trash2,
  RefreshCw,
  Clock,
  ShieldCheck,
  Server,
  CheckCircle2,
  Layers,
  Sparkles,
  Search,
} from 'lucide-react'
import { Dialog, DialogHeader, DialogTitle, DialogBody, DialogFooter } from '../../components/ui/dialog'
import { Button } from '../../components/ui/button'
import { Badge } from '../../components/ui/badge'
import { useSnapshots, usePruneSnapshots, useDeleteSnapshot } from './useSnapshots'
import type { Host } from '../../api/hosts'
import type { HostSnapshotItem, SnapshotPruneResult } from '../../api/snapshots'

interface SnapshotManagementModalProps {
  isOpen: boolean
  onClose: () => void
  selectedHost?: Host | null
}

export const SnapshotManagementModal: React.FC<SnapshotManagementModalProps> = ({
  isOpen,
  onClose,
  selectedHost = null,
}) => {
  const hostId = selectedHost?.id
  const { data: snapshots = [], isLoading, refetch, isFetching } = useSnapshots(hostId)
  const pruneMutation = usePruneSnapshots()
  const deleteMutation = useDeleteSnapshot()

  const [confirmDeleteSnap, setConfirmDeleteSnap] = useState<HostSnapshotItem | null>(null)
  const [pruneSummary, setPruneSummary] = useState<SnapshotPruneResult | null>(null)
  const [searchTerm, setSearchTerm] = useState('')

  const filteredSnapshots = snapshots.filter((s) => {
    if (!searchTerm) return true
    const term = searchTerm.toLowerCase()
    return (
      s.name.toLowerCase().includes(term) ||
      s.hostname.toLowerCase().includes(term) ||
      (s.description && s.description.toLowerCase().includes(term)) ||
      s.vmid.toString().includes(term)
    )
  })

  const totalCount = snapshots.length
  const expiredCount = snapshots.filter((s) => s.canPrune).length
  const protectedCount = snapshots.filter((s) => s.isProtectedByActiveJob).length
  const activeSafetyCount = snapshots.filter((s) => s.isControlPlaneSnapshot && !s.isExpired).length

  const handlePrune = async (dryRun: boolean) => {
    try {
      const result = await pruneMutation.mutateAsync({
        hostId,
        dryRun,
      })
      setPruneSummary(result)
    } catch (err) {
      console.error('Failed to prune snapshots:', err)
    }
  }

  const handleDelete = async (snap: HostSnapshotItem) => {
    try {
      await deleteMutation.mutateAsync({
        hostId: snap.hostId,
        snapshotName: snap.name,
      })
      setConfirmDeleteSnap(null)
    } catch (err) {
      console.error('Failed to delete snapshot:', err)
    }
  }

  return (
    <Dialog open={isOpen} onClose={onClose} maxWidth="xl">
      <DialogHeader onClose={onClose}>
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
              <div className="p-2.5 rounded-xl bg-purple-500/10 border border-purple-500/20 text-purple-400">
                <Camera className="w-6 h-6" />
              </div>
              <div>
                <DialogTitle className="text-xl font-semibold text-slate-100 flex items-center gap-2">
                  Proxmox Hypervisor Snapshots
                  {selectedHost && (
                    <Badge variant="outline" className="text-xs font-mono border-purple-500/30 text-purple-300">
                      {selectedHost.hostname}
                    </Badge>
                  )}
                </DialogTitle>
                <p className="text-xs text-slate-400 mt-0.5">
                  Inspect active VM/LXC snapshots, evaluate 24-hour retention safety windows, and prune expired storage.
                </p>
              </div>
            </div>

            <Button
              variant="outline"
              size="sm"
              onClick={() => refetch()}
              disabled={isFetching}
              className="border-slate-800 hover:bg-slate-800/60 text-slate-300 gap-1.5"
            >
              <RefreshCw className={`w-3.5 h-3.5 ${isFetching ? 'animate-spin' : ''}`} />
              Refresh
            </Button>
          </div>
        </DialogHeader>

        <DialogBody className="space-y-4 max-h-[70vh] overflow-y-auto pr-1">
          {/* Metrics Overview Cards */}
          <div className="grid grid-cols-4 gap-3">
            <div className="p-3 rounded-xl bg-slate-900/60 border border-slate-800 flex items-center gap-3">
              <div className="p-2 rounded-lg bg-slate-800 text-slate-300">
                <Layers className="w-4 h-4" />
              </div>
              <div>
                <div className="text-xl font-bold text-slate-100">{totalCount}</div>
                <div className="text-xs text-slate-400">Total Snapshots</div>
              </div>
            </div>

            <div className="p-3 rounded-xl bg-slate-900/60 border border-slate-800 flex items-center gap-3">
              <div className="p-2 rounded-lg bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                <ShieldCheck className="w-4 h-4" />
              </div>
              <div>
                <div className="text-xl font-bold text-emerald-400">{activeSafetyCount}</div>
                <div className="text-xs text-slate-400">Active (&lt;24h)</div>
              </div>
            </div>

            <div className="p-3 rounded-xl bg-slate-900/60 border border-slate-800 flex items-center gap-3">
              <div className="p-2 rounded-lg bg-amber-500/10 text-amber-400 border border-amber-500/20">
                <Clock className="w-4 h-4" />
              </div>
              <div>
                <div className="text-xl font-bold text-amber-400">{expiredCount}</div>
                <div className="text-xs text-slate-400">Expired (&gt;24h)</div>
              </div>
            </div>

            <div className="p-3 rounded-xl bg-slate-900/60 border border-slate-800 flex items-center gap-3">
              <div className="p-2 rounded-lg bg-sky-500/10 text-sky-400 border border-sky-500/20">
                <ShieldCheck className="w-4 h-4" />
              </div>
              <div>
                <div className="text-xl font-bold text-sky-400">{protectedCount}</div>
                <div className="text-xs text-slate-400">Job Protected</div>
              </div>
            </div>
          </div>

          {/* Action & Feedback Banner */}
          {pruneSummary && (
            <div className="p-3 rounded-xl bg-slate-900/80 border border-slate-700 text-xs text-slate-200 flex items-start justify-between">
              <div className="space-y-1">
                <div className="font-semibold flex items-center gap-1.5 text-slate-100">
                  <CheckCircle2 className="w-4 h-4 text-emerald-400" />
                  {pruneSummary.dryRun ? 'Dry-Run Prune Simulation Complete' : 'Snapshot Retention Prune Complete'}
                </div>
                <div className="text-slate-400">
                  Scanned: <span className="text-slate-200 font-medium">{pruneSummary.totalScanned}</span> |
                  Eligible Expired: <span className="text-amber-400 font-medium">{pruneSummary.expiredCount}</span> |
                  {pruneSummary.dryRun ? ' Would Prune: ' : ' Pruned: '}
                  <span className="text-emerald-400 font-medium">{pruneSummary.prunedCount}</span> |
                  Skipped/Protected: <span className="text-sky-400 font-medium">{pruneSummary.skippedCount}</span>
                </div>
                {pruneSummary.errors.length > 0 && (
                  <div className="text-rose-400 pt-1">
                    Errors: {pruneSummary.errors.join('; ')}
                  </div>
                )}
              </div>
              <Button
                variant="ghost"
                size="sm"
                className="h-6 text-xs text-slate-400 hover:text-slate-200"
                onClick={() => setPruneSummary(null)}
              >
                Dismiss
              </Button>
            </div>
          )}

          {/* Table Controls */}
          <div className="flex items-center justify-between gap-3 pt-1">
            <div className="relative flex-1 max-w-sm">
              <Search className="absolute left-3 top-2.5 w-4 h-4 text-slate-500" />
              <input
                type="text"
                placeholder="Filter by snapshot name, host, VMID..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                className="w-full bg-slate-900/60 border border-slate-800 rounded-lg pl-9 pr-3 py-1.5 text-xs text-slate-200 focus:outline-none focus:border-purple-500/50"
              />
            </div>

            <div className="flex items-center gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => handlePrune(true)}
                disabled={pruneMutation.isPending || expiredCount === 0}
                className="border-slate-800 hover:bg-slate-800/60 text-slate-300 text-xs gap-1.5"
              >
                <Sparkles className="w-3.5 h-3.5 text-purple-400" />
                Dry-Run Scan
              </Button>

              <Button
                variant="primary"
                size="sm"
                onClick={() => handlePrune(false)}
                disabled={pruneMutation.isPending || expiredCount === 0}
                className="bg-amber-600 hover:bg-amber-500 text-slate-950 font-medium text-xs gap-1.5"
              >
                <Trash2 className="w-3.5 h-3.5" />
                {pruneMutation.isPending ? 'Pruning...' : `Prune Expired (${expiredCount})`}
              </Button>
            </div>
          </div>

          {/* Snapshots Table */}
          <div className="border border-slate-800 rounded-xl overflow-hidden bg-slate-950/40">
            <table className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="bg-slate-900/80 border-b border-slate-800 text-slate-400 font-medium">
                  <th className="py-2.5 px-3">Host / Target</th>
                  <th className="py-2.5 px-3">Snapshot Name</th>
                  <th className="py-2.5 px-3">Created / Age</th>
                  <th className="py-2.5 px-3">Retention Policy</th>
                  <th className="py-2.5 px-3 text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60">
                {isLoading ? (
                  <tr>
                    <td colSpan={5} className="py-8 text-center text-slate-400">
                      <RefreshCw className="w-5 h-5 animate-spin mx-auto mb-2 text-purple-400" />
                      Loading Proxmox hypervisor snapshots...
                    </td>
                  </tr>
                ) : filteredSnapshots.length === 0 ? (
                  <tr>
                    <td colSpan={5} className="py-8 text-center text-slate-500">
                      No snapshots found matching criteria.
                    </td>
                  </tr>
                ) : (
                  filteredSnapshots.map((snap) => (
                    <tr key={`${snap.hostId}-${snap.name}`} className="hover:bg-slate-900/40 transition-colors">
                      <td className="py-2.5 px-3">
                        <div className="flex items-center gap-2">
                          <Server className="w-3.5 h-3.5 text-slate-500" />
                          <div>
                            <div className="font-medium text-slate-200">{snap.hostname}</div>
                            <div className="text-[10px] text-slate-500 font-mono">
                              {snap.node} / {snap.isLxc ? 'LXC' : 'VM'} {snap.vmid}
                            </div>
                          </div>
                        </div>
                      </td>

                      <td className="py-2.5 px-3">
                        <div className="font-mono text-slate-200">{snap.name}</div>
                        {snap.description && (
                          <div className="text-[10px] text-slate-400 truncate max-w-xs">{snap.description}</div>
                        )}
                      </td>

                      <td className="py-2.5 px-3">
                        <div className="text-slate-300 font-medium">
                          {snap.ageHours >= 24
                            ? `${snap.ageHours.toFixed(1)} hrs (${Math.floor(snap.ageHours / 24)}d)`
                            : `${snap.ageHours.toFixed(1)} hrs`}
                        </div>
                        {snap.createdAt && (
                          <div className="text-[10px] text-slate-500">
                            {new Date(snap.createdAt).toLocaleDateString()} {new Date(snap.createdAt).toLocaleTimeString()}
                          </div>
                        )}
                      </td>

                      <td className="py-2.5 px-3">
                        {snap.isProtectedByActiveJob ? (
                          <Badge className="bg-sky-500/10 text-sky-400 border border-sky-500/20 text-[10px] gap-1">
                            <ShieldCheck className="w-3 h-3" />
                            Locked (Running Job)
                          </Badge>
                        ) : snap.canPrune ? (
                          <Badge className="bg-amber-500/10 text-amber-400 border border-amber-500/20 text-[10px] gap-1">
                            <Clock className="w-3 h-3" />
                            Expired (&gt;24h)
                          </Badge>
                        ) : snap.isControlPlaneSnapshot ? (
                          <Badge className="bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 text-[10px] gap-1">
                            <CheckCircle2 className="w-3 h-3" />
                            Active Safety (&lt;24h)
                          </Badge>
                        ) : (
                          <Badge className="bg-purple-500/10 text-purple-400 border border-purple-500/20 text-[10px]">
                            Manual Snapshot
                          </Badge>
                        )}
                      </td>

                      <td className="py-2.5 px-3 text-right">
                        {confirmDeleteSnap?.name === snap.name && confirmDeleteSnap.hostId === snap.hostId ? (
                          <div className="flex items-center justify-end gap-1.5">
                            <span className="text-[10px] text-rose-400">Confirm?</span>
                            <Button
                              variant="destructive"
                              size="sm"
                              className="h-6 px-2 text-[10px] bg-rose-600 hover:bg-rose-500"
                              onClick={() => handleDelete(snap)}
                              disabled={deleteMutation.isPending}
                            >
                              Yes, Delete
                            </Button>
                            <Button
                              variant="ghost"
                              size="sm"
                              className="h-6 px-1.5 text-[10px] text-slate-400"
                              onClick={() => setConfirmDeleteSnap(null)}
                            >
                              Cancel
                            </Button>
                          </div>
                        ) : (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setConfirmDeleteSnap(snap)}
                            disabled={snap.isProtectedByActiveJob || deleteMutation.isPending}
                            className="h-7 w-7 p-0 text-slate-400 hover:text-rose-400 hover:bg-rose-500/10"
                            title={snap.isProtectedByActiveJob ? 'Protected by active update job' : 'Delete Snapshot'}
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </Button>
                        )}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </DialogBody>

        <DialogFooter className="flex items-center justify-between border-t border-slate-800 pt-3">
          <div className="text-[11px] text-slate-500">
            Snapshots are automatically pruned when age exceeds 24 hours unless protected by an active job.
          </div>
          <Button variant="outline" size="sm" onClick={onClose} className="border-slate-800 text-slate-300">
            Close
          </Button>
        </DialogFooter>
    </Dialog>
  )
}
