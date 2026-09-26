import React from 'react'
import type { Host } from '../../api/hosts'
import type { JobSummary } from '../../api/jobs'
import {
  OsBadge,
  TargetTypeBadge,
  AgentStatusBadge,
  RebootBadge,
  UpdatesBadge,
} from './HostStatusBadge'
import { HostVitalsBadge } from './HostVitalsBadge'
import {
  MoreVertical,
  Eye,
  Pencil,
  RotateCcw,
  Sparkles,
  Terminal,
  Trash2,
  Copy,
  Check,
  GitFork,
  Camera,
} from 'lucide-react'
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuLabel,
} from '../../components/ui/dropdown-menu'
import { isProxmoxHost } from '../../api/hosts'

interface HostMobileCardListProps {
  hosts: Host[]
  selectedHostIds: Set<string>
  onToggleSelect: (hostId: string) => void
  onInspect: (host: Host) => void
  onEdit: (host: Host) => void
  onDelete: (host: Host) => void
  onReboot: (host: Host) => void
  onOpenTerminal: (host: Host) => void
  onTriggerUpdate: (host: Host) => void
  onOpenSnapshots: (host: Host) => void
  onViewDag: (job: JobSummary) => void
  activeJobsByHost: Map<string, JobSummary>
  copiedIp: string | null
  onCopyIp: (ip: string, e: React.MouseEvent) => void
  isAdmin: boolean
  isOperator: boolean
}

export function HostMobileCardList({
  hosts,
  selectedHostIds,
  onToggleSelect,
  onInspect,
  onEdit,
  onDelete,
  onReboot,
  onOpenTerminal,
  onTriggerUpdate,
  onOpenSnapshots,
  onViewDag,
  activeJobsByHost,
  copiedIp,
  onCopyIp,
  isAdmin,
  isOperator,
}: HostMobileCardListProps) {
  return (
    <div className="space-y-3 md:hidden" data-testid="host-mobile-cards">
      {hosts.map((host) => {
        const activeJob = activeJobsByHost.get(host.id)
        const isSelected = selectedHostIds.has(host.id)

        return (
          <div
            key={host.id}
            onClick={() => onInspect(host)}
            className={`p-3.5 rounded-xl border transition-all cursor-pointer space-y-2.5 active:bg-zinc-800/60 shadow-xs ${
              isSelected
                ? 'bg-zinc-900 border-sky-500/50'
                : 'bg-zinc-900/60 border-zinc-800/80 hover:border-zinc-700/80'
            }`}
          >
            {/* Header: Checkbox, Hostname, Target type, and Kebab menu */}
            <div className="flex items-center justify-between gap-2">
              <div className="flex items-center gap-2.5 min-w-0">
                <input
                  type="checkbox"
                  aria-label={`Select ${host.hostname}`}
                  checked={isSelected}
                  onChange={() => onToggleSelect(host.id)}
                  onClick={(e) => e.stopPropagation()}
                  className="rounded border-zinc-700 bg-zinc-900 text-emerald-500 focus:ring-emerald-500/20 cursor-pointer h-4.5 w-4.5 shrink-0"
                />
                <div className="min-w-0">
                  <div className="flex items-center gap-1.5 flex-wrap">
                    <span className="font-semibold text-zinc-100 text-sm truncate">
                      {host.hostname}
                    </span>
                    <TargetTypeBadge type={host.targetType} />
                  </div>
                  {host.friendlyName && (
                    <div className="text-[11px] text-zinc-400 truncate">
                      {host.friendlyName}
                    </div>
                  )}
                </div>
              </div>

              {/* Kebab Action Menu */}
              <div onClick={(e) => e.stopPropagation()} className="shrink-0">
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <button
                      type="button"
                      className="p-2 text-zinc-400 hover:text-zinc-100 hover:bg-zinc-800 rounded-lg min-w-[40px] min-h-[40px] flex items-center justify-center transition-colors cursor-pointer"
                      aria-label={`Actions for ${host.hostname}`}
                    >
                      <MoreVertical className="w-4.5 h-4.5" />
                    </button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="right" className="w-48">
                    <DropdownMenuLabel className="text-xs text-zinc-400">
                      Node Operations
                    </DropdownMenuLabel>
                    <DropdownMenuItem
                      onClick={() => onInspect(host)}
                      className="gap-2 text-xs"
                    >
                      <Eye className="w-3.5 h-3.5 text-zinc-400" />
                      View Details
                    </DropdownMenuItem>

                    {isOperator && (
                      <>
                        <DropdownMenuItem
                          onClick={() => onOpenTerminal(host)}
                          className="gap-2 text-xs"
                        >
                          <Terminal className="w-3.5 h-3.5 text-sky-400" />
                          Open Terminal
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          onClick={() => onTriggerUpdate(host)}
                          className="gap-2 text-xs text-sky-400"
                        >
                          <Sparkles className="w-3.5 h-3.5" />
                          Trigger Upgrade DAG
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          onClick={() => onReboot(host)}
                          className="gap-2 text-xs text-amber-400"
                        >
                          <RotateCcw className="w-3.5 h-3.5" />
                          Reboot Node
                        </DropdownMenuItem>
                      </>
                    )}

                    {isProxmoxHost(host) && (
                      <DropdownMenuItem
                        onClick={() => onOpenSnapshots(host)}
                        className="gap-2 text-xs"
                      >
                        <Camera className="w-3.5 h-3.5 text-purple-400" />
                        Snapshots
                      </DropdownMenuItem>
                    )}

                    {isAdmin && (
                      <>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem
                          onClick={() => onEdit(host)}
                          className="gap-2 text-xs"
                        >
                          <Pencil className="w-3.5 h-3.5 text-zinc-400" />
                          Edit Host
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          onClick={() => onDelete(host)}
                          className="gap-2 text-xs text-rose-400 hover:text-rose-300"
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                          Delete Host
                        </DropdownMenuItem>
                      </>
                    )}
                  </DropdownMenuContent>
                </DropdownMenu>
              </div>
            </div>

            {/* Network, OS & Agent status */}
            <div className="flex items-center justify-between gap-2 text-xs pt-1 border-t border-zinc-800/60 flex-wrap">
              <div className="flex items-center gap-1.5 min-w-0">
                <span className="font-mono text-zinc-300 text-xs">
                  {host.ipAddress}
                </span>
                <button
                  type="button"
                  onClick={(e) => onCopyIp(host.ipAddress, e)}
                  className="p-1 rounded text-zinc-400 hover:text-zinc-200 transition-colors min-w-[28px] min-h-[28px] flex items-center justify-center"
                  title="Copy IP"
                  aria-label="Copy IP address"
                >
                  {copiedIp === host.ipAddress ? (
                    <Check className="h-3.5 w-3.5 text-emerald-400" />
                  ) : (
                    <Copy className="h-3.5 w-3.5" />
                  )}
                </button>
                <OsBadge osFamily={host.osFamily} />
              </div>

              <AgentStatusBadge agent={host.agent} />
            </div>

            {/* Vitals, Flags & Active DAG */}
            <div className="flex items-center justify-between gap-2 pt-1">
              <div className="flex items-center gap-1.5 flex-wrap">
                <HostVitalsBadge vitals={host.vitals} />
                {host.agent?.pendingReboot && <RebootBadge pending={true} />}
                {Boolean(host.agent?.upgradablePackagesCount) && (
                  <UpdatesBadge count={host.agent?.upgradablePackagesCount} />
                )}
              </div>

              {activeJob && (
                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation()
                    onViewDag(activeJob)
                  }}
                  className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-sky-500/15 text-sky-400 border border-sky-500/30 hover:bg-sky-500/25 transition-colors animate-pulse shrink-0 cursor-pointer"
                  title={`Active DAG: ${activeJob.activeStep || activeJob.status}. Tap to view DAG Canvas.`}
                >
                  <GitFork className="w-2.5 h-2.5" />
                  DAG Active
                </button>
              )}
            </div>
          </div>
        )
      })}
    </div>
  )
}
