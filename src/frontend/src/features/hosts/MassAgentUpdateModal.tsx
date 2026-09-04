import { useState } from 'react'
import {
  ArrowUpCircle,
  AlertTriangle,
  Server,
  Loader2,
  CheckCircle2,
  Radio,
  ShieldCheck,
  RefreshCw,
  XCircle,
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
import { useAgentVersionInfo, useTriggerMassUpdate } from './useAgentUpdates'
import type { MassUpdateBatchResult } from '../../api/agents'

interface MassAgentUpdateModalProps {
  open: boolean
  onClose: () => void
  onSuccess?: () => void
}

export function MassAgentUpdateModal({
  open,
  onClose,
  onSuccess,
}: MassAgentUpdateModalProps) {
  const { data: versionInfo, isLoading, refetch, isFetching } = useAgentVersionInfo()
  const massUpdateMutation = useTriggerMassUpdate()

  const [selectedHostIds, setSelectedHostIds] = useState<Set<string>>(new Set())
  const [useAllOutdated, setUseAllOutdated] = useState(true)
  const [batchResult, setBatchResult] = useState<MassUpdateBatchResult | null>(null)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

  const outdatedHosts = versionInfo?.outdatedHosts ?? []
  const serverVersion = versionInfo?.serverVersion ?? '1.1.0'

  const toggleSelectHost = (hostId: string) => {
    setUseAllOutdated(false)
    setSelectedHostIds((prev) => {
      const next = new Set(prev)
      if (next.has(hostId)) {
        next.delete(hostId)
      } else {
        next.add(hostId)
      }
      return next
    })
  }

  const handleSelectAll = () => {
    if (useAllOutdated) {
      setUseAllOutdated(false)
      setSelectedHostIds(new Set())
    } else {
      setUseAllOutdated(true)
      setSelectedHostIds(new Set(outdatedHosts.map((h) => h.hostId)))
    }
  }

  const handleTriggerUpdate = async () => {
    setErrorMsg(null)
    try {
      const payload = useAllOutdated
        ? { allOutdated: true }
        : {
            allOutdated: false,
            hostIds: Array.from(selectedHostIds),
          }

      const res = await massUpdateMutation.mutateAsync(payload)
      setBatchResult(res)
      if (onSuccess) onSuccess()
    } catch (err: unknown) {
      const msg =
        err instanceof Error
          ? err.message
          : 'Failed to initiate mass agent update'
      setErrorMsg(msg)
    }
  }

  const handleClose = () => {
    setBatchResult(null)
    setErrorMsg(null)
    setSelectedHostIds(new Set())
    setUseAllOutdated(true)
    onClose()
  }

  if (!open) return null

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="lg">
      <DialogHeader>
        <div className="flex items-center gap-3">
          <div className="p-2.5 rounded-xl bg-sky-500/10 border border-sky-500/20 text-sky-400">
            <ArrowUpCircle className="w-5 h-5" />
          </div>
          <div>
            <DialogTitle>Mass Agent In-Band Update</DialogTitle>
            <p className="text-xs text-zinc-400 mt-0.5">
              Upgrade daemon to v{serverVersion} across online compute nodes without re-adoption
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

        {/* Status Metrics Banner */}
        <div className="grid grid-cols-3 gap-3">
          <div className="p-3 bg-zinc-900/60 border border-zinc-800/80 rounded-xl">
            <span className="text-[11px] text-zinc-500 font-medium block">Target Version</span>
            <span className="text-base font-bold text-sky-400 font-mono">v{serverVersion}</span>
          </div>
          <div className="p-3 bg-zinc-900/60 border border-zinc-800/80 rounded-xl">
            <span className="text-[11px] text-zinc-500 font-medium block">Outdated Agents</span>
            <span className="text-base font-bold text-amber-400">
              {versionInfo?.outdatedAgentsCount ?? 0}
            </span>
          </div>
          <div className="p-3 bg-zinc-900/60 border border-zinc-800/80 rounded-xl">
            <span className="text-[11px] text-zinc-500 font-medium block">Ready Online</span>
            <span className="text-base font-bold text-emerald-400">
              {versionInfo?.onlineOutdatedCount ?? 0}
            </span>
          </div>
        </div>

        {batchResult ? (
          /* Execution Results View */
          <div className="space-y-3">
            <div className="p-3 bg-emerald-950/30 border border-emerald-800/40 rounded-xl text-xs text-emerald-300 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 text-emerald-400" />
                <span>
                  Update dispatched to <strong>{batchResult.dispatchedCount}</strong> host(s).{' '}
                  {batchResult.skippedOfflineCount > 0 && (
                    <span className="text-zinc-400">
                      ({batchResult.skippedOfflineCount} offline hosts skipped)
                    </span>
                  )}
                </span>
              </div>
              <Badge variant="success">Dispatched</Badge>
            </div>

            <div className="border border-zinc-800/80 rounded-xl bg-zinc-950/50 p-3 divide-y divide-zinc-800/60 max-h-64 overflow-y-auto">
              {batchResult.details.map((item) => (
                <div key={item.hostId} className="py-2.5 first:pt-0 last:pb-0 flex items-center justify-between">
                  <div className="flex items-center gap-2.5">
                    <Server className="w-4 h-4 text-zinc-500 shrink-0" />
                    <div>
                      <span className="text-xs font-medium text-zinc-200">{item.hostname}</span>
                      <div className="flex items-center gap-1.5 text-[11px] text-zinc-400 font-mono">
                        <span>v{item.currentVersion}</span>
                        <span>→</span>
                        <span className="text-sky-400">v{item.targetVersion}</span>
                      </div>
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    {item.status === 'Dispatched' && (
                      <span className="inline-flex items-center gap-1 text-[11px] text-emerald-300 bg-emerald-950/60 px-2 py-0.5 rounded-full border border-emerald-700/50">
                        <Radio className="w-3 h-3 text-emerald-400 animate-pulse" />
                        In-Band Sent
                      </span>
                    )}
                    {item.status === 'SkippedOffline' && (
                      <span className="inline-flex items-center gap-1 text-[11px] text-zinc-400 bg-zinc-900/60 px-2 py-0.5 rounded-full border border-zinc-700/50">
                        Offline
                      </span>
                    )}
                    {item.status === 'Failed' && (
                      <span className="inline-flex items-center gap-1 text-[11px] text-rose-300 bg-rose-950/60 px-2 py-0.5 rounded-full border border-rose-700/50">
                        <XCircle className="w-3 h-3 text-rose-400" />
                        Failed
                      </span>
                    )}
                  </div>
                </div>
              ))}
            </div>

            <p className="text-[11px] text-zinc-500">
              Note: Connected agents will download the target binary into <code className="text-zinc-400">/tmp</code>, verify checksums, atomically replace <code className="text-zinc-400">/usr/local/bin/controlplane-agent</code>, and gracefully restart systemd. They will reconnect within 5-10 seconds.
            </p>
          </div>
        ) : (
          /* Pre-Flight Confirmation View */
          <div className="space-y-3">
            <div className="p-3 bg-zinc-900/40 border border-zinc-800/60 rounded-xl space-y-1.5">
              <div className="flex items-center gap-2 text-xs font-semibold text-zinc-200">
                <ShieldCheck className="w-4 h-4 text-sky-400 shrink-0" />
                <span>Zero-Downtime Safe Protocol</span>
              </div>
              <p className="text-xs text-zinc-400 leading-relaxed">
                Existing active connections are preserved while the new executable replaces the local file inode. A graceful restart transfers monitoring immediately.
              </p>
            </div>

            <div className="flex items-center justify-between text-xs text-zinc-400 px-1">
              <span className="font-medium">Target Node Selection</span>
              <button
                type="button"
                onClick={handleSelectAll}
                className="text-sky-400 hover:text-sky-300 transition-colors cursor-pointer"
              >
                {useAllOutdated ? 'Deselect All' : 'Select All Outdated'}
              </button>
            </div>

            {isLoading ? (
              <div className="p-8 flex items-center justify-center text-zinc-500">
                <Loader2 className="w-5 h-5 animate-spin" />
              </div>
            ) : outdatedHosts.length === 0 ? (
              <div className="p-6 text-center border border-zinc-800/80 rounded-xl bg-zinc-950/40 space-y-2">
                <CheckCircle2 className="w-8 h-8 text-emerald-400 mx-auto" />
                <p className="text-xs font-medium text-zinc-300">All agent daemons are up-to-date!</p>
                <p className="text-[11px] text-zinc-500">
                  Every monitored host is already running version v{serverVersion}.
                </p>
              </div>
            ) : (
              <div className="border border-zinc-800/80 rounded-xl bg-zinc-950/50 p-2 divide-y divide-zinc-800/60 max-h-56 overflow-y-auto">
                {outdatedHosts.map((h) => {
                  const isChecked = useAllOutdated || selectedHostIds.has(h.hostId)
                  return (
                    <div
                      key={h.hostId}
                      onClick={() => toggleSelectHost(h.hostId)}
                      className={`p-2 rounded-lg flex items-center justify-between cursor-pointer transition-colors ${
                        isChecked ? 'bg-sky-950/20' : 'hover:bg-zinc-900/40'
                      }`}
                    >
                      <div className="flex items-center gap-3">
                        <input
                          type="checkbox"
                          checked={isChecked}
                          onChange={() => {}}
                          className="rounded border-zinc-700 bg-zinc-900 text-sky-500 focus:ring-sky-500/20"
                        />
                        <div>
                          <span className="text-xs font-medium text-zinc-200">{h.hostname}</span>
                          <span className="text-[11px] text-zinc-500 ml-2 font-mono">
                            v{h.currentVersion}
                          </span>
                        </div>
                      </div>
                      <div className="flex items-center gap-2">
                        {h.isOnline ? (
                          <Badge variant="success" dot>
                            Online
                          </Badge>
                        ) : (
                          <Badge variant="default" className="text-zinc-500 border-zinc-800">
                            Offline
                          </Badge>
                        )}
                      </div>
                    </div>
                  )
                })}
              </div>
            )}
          </div>
        )}
      </DialogBody>

      <DialogFooter>
        <div className="flex items-center justify-between w-full">
          <Button
            variant="ghost"
            size="sm"
            onClick={() => refetch()}
            disabled={isFetching}
            className="gap-1 text-zinc-400 hover:text-zinc-200"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isFetching ? 'animate-spin' : ''}`} />
            Check Updates
          </Button>

          <div className="flex items-center gap-2">
            <Button variant="ghost" size="sm" onClick={handleClose}>
              {batchResult ? 'Close' : 'Cancel'}
            </Button>

            {!batchResult && outdatedHosts.length > 0 && (
              <Button
                variant="primary"
                size="sm"
                onClick={handleTriggerUpdate}
                disabled={massUpdateMutation.isPending || (versionInfo?.onlineOutdatedCount ?? 0) === 0}
                className="gap-1.5 bg-sky-600 hover:bg-sky-500 text-white border-sky-500"
              >
                {massUpdateMutation.isPending ? (
                  <>
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                    Dispatching...
                  </>
                ) : (
                  <>
                    <ArrowUpCircle className="w-3.5 h-3.5" />
                    Update {useAllOutdated ? `${versionInfo?.onlineOutdatedCount ?? 0} Agents` : `${selectedHostIds.size} Selected`}
                  </>
                )}
              </Button>
            )}
          </div>
        </div>
      </DialogFooter>
    </Dialog>
  )
}
