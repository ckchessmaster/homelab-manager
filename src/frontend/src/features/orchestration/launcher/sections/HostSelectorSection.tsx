import { Server, CheckCircle2, AlertCircle, ArrowUpCircle, GitFork } from 'lucide-react'
import type { Host } from '../../../../api/hosts'
import type { JobSummary } from '../../../../api/jobs'
import { Badge } from '../../../../components/ui/badge'

export interface HostSelectorSectionProps {
  availableHosts: Host[]
  selectedHostId: string
  onSelectHost: (hostId: string) => void
  disabled?: boolean
  initialHost?: Host | null
  activeJobsByHost?: Map<string, JobSummary>
}

export function HostSelectorSection({
  availableHosts,
  selectedHostId,
  onSelectHost,
  disabled = false,
  initialHost = null,
  activeJobsByHost,
}: HostSelectorSectionProps) {
  const selectedHost = initialHost || availableHosts.find((h) => h.id === selectedHostId)
  const initialActiveJob = initialHost ? activeJobsByHost?.get(initialHost.id) : null

  return (
    <div className="p-4 rounded-xl bg-zinc-950/80 border border-zinc-800 space-y-3">
      <div className="flex items-center justify-between">
        <label className="text-xs font-semibold text-zinc-200 flex items-center gap-1.5">
          <Server className="w-4 h-4 text-emerald-400" />
          Target Managed Host
        </label>
        {selectedHost && (
          <div className="flex items-center gap-1.5">
            <Badge variant="outline" className="text-[10px] uppercase font-mono tracking-wider">
              {selectedHost.targetType || 'host'}
            </Badge>
            {selectedHost.proxmox && (
              <Badge variant="outline" className="text-[10px] font-mono text-purple-400 border-purple-500/30">
                PVE VM {selectedHost.proxmox.vmid}
              </Badge>
            )}
          </div>
        )}
      </div>

      {initialHost ? (
        <div className="p-2.5 rounded-lg bg-zinc-900/80 border border-zinc-800/80 flex items-center justify-between text-xs">
          <div>
            <div className="font-semibold text-zinc-100 flex items-center gap-1.5">
              <span>{initialHost.hostname}</span>
              <span className="font-mono text-zinc-500 font-normal">({initialHost.ipAddress})</span>
            </div>
            <div className="text-[11px] text-zinc-400 mt-0.5">
              {initialHost.osFamily} &bull; {initialHost.agent?.upgradablePackagesCount ?? 0} updates pending
            </div>
          </div>
          {initialActiveJob ? (
            <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-semibold bg-sky-500/15 text-sky-400 border border-sky-500/30 animate-pulse">
              <GitFork className="w-3 h-3 text-sky-400" />
              DAG in Progress
            </span>
          ) : (
            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
              <CheckCircle2 className="w-3 h-3" />
              Agent Online
            </span>
          )}
        </div>
      ) : (
        <div className="space-y-1.5">
          <select
            value={selectedHostId}
            onChange={(e) => onSelectHost(e.target.value)}
            disabled={disabled}
            className="w-full px-3 py-2 text-xs bg-zinc-900 border border-zinc-700/80 rounded-lg text-zinc-100 focus:outline-none focus:ring-1 focus:ring-emerald-500 cursor-pointer disabled:opacity-50"
          >
            <option value="">-- Choose Target Managed Host --</option>
            {availableHosts.map((h) => {
              const isOnline = h.agent?.isOnline ?? h.agent?.installed
              const activeJob = activeJobsByHost?.get(h.id)
              const updatesCount = h.agent?.upgradablePackagesCount || 0
              return (
                <option key={h.id} value={h.id} disabled={Boolean(activeJob)}>
                  {h.hostname} ({h.ipAddress}) — {h.osFamily} [{h.targetType || 'host'}]{activeJob ? ` [DAG IN PROGRESS: ${activeJob.activeStep || activeJob.status}]` : updatesCount > 0 ? ` (${updatesCount} updates)` : ''}{!isOnline ? ' [OFFLINE]' : ''}
                </option>
              )
            })}
          </select>

          {selectedHost && (
            <div className="flex items-center justify-between px-1 pt-1 text-[11px] text-zinc-400">
              <span className="flex items-center gap-1">
                {selectedHost.agent?.installed ? (
                  <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" />
                ) : (
                  <AlertCircle className="w-3.5 h-3.5 text-amber-400" />
                )}
                Agent status: <strong className="text-zinc-300 font-normal">{selectedHost.agent?.installed ? 'Active' : 'Offline / Uninstalled'}</strong>
              </span>

              <span className="flex items-center gap-1 text-emerald-400 font-mono">
                <ArrowUpCircle className="w-3.5 h-3.5" />
                {selectedHost.agent?.upgradablePackagesCount || 0} packages ready
              </span>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
