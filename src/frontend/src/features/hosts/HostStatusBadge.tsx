import { Server, Box, Layers, AlertTriangle, ArrowUpCircle } from 'lucide-react'
import { Badge } from '../../components/ui/badge'
import type { AgentState } from '../../api/hosts'

export function TargetTypeBadge({ type }: { type: string }) {
  switch (type.toLowerCase()) {
    case 'baremetal':
      return (
        <span className="inline-flex items-center gap-1 text-xs font-medium text-zinc-300 bg-zinc-800/60 px-2 py-0.5 rounded border border-zinc-700/50">
          <Server className="h-3 w-3 text-zinc-400" />
          Bare-Metal
        </span>
      )
    case 'proxmox_vm':
      return (
        <span className="inline-flex items-center gap-1 text-xs font-medium text-purple-300 bg-purple-950/40 px-2 py-0.5 rounded border border-purple-800/40">
          <Box className="h-3 w-3 text-purple-400" />
          Proxmox VM
        </span>
      )
    case 'proxmox_lxc':
      return (
        <span className="inline-flex items-center gap-1 text-xs font-medium text-sky-300 bg-sky-950/40 px-2 py-0.5 rounded border border-sky-800/40">
          <Layers className="h-3 w-3 text-sky-400" />
          Proxmox LXC
        </span>
      )
    default:
      return (
        <span className="inline-flex items-center gap-1 text-xs font-medium text-zinc-400 bg-zinc-800/40 px-2 py-0.5 rounded">
          {type}
        </span>
      )
  }
}

export function OsBadge({ osFamily }: { osFamily: string }) {
  const normalized = osFamily.toLowerCase()
  if (normalized.includes('debian')) {
    return <Badge variant="destructive" className="bg-rose-950/30 text-rose-300 border-rose-800/40 font-mono text-[11px]">Debian</Badge>
  }
  if (normalized.includes('ubuntu')) {
    return <Badge variant="warning" className="bg-orange-950/30 text-orange-300 border-orange-800/40 font-mono text-[11px]">Ubuntu</Badge>
  }
  if (normalized.includes('rhel') || normalized.includes('rocky') || normalized.includes('fedora')) {
    return <Badge variant="info" className="bg-blue-950/30 text-blue-300 border-blue-800/40 font-mono text-[11px]">RHEL / Rocky</Badge>
  }
  if (normalized.includes('windows')) {
    return <Badge variant="info" className="bg-sky-950/30 text-sky-300 border-sky-800/40 font-mono text-[11px]">Windows</Badge>
  }
  return <Badge variant="outline" className="font-mono text-[11px]">{osFamily}</Badge>
}

function checkIsOnline(lastSeenAt?: string | null): boolean {
  if (!lastSeenAt) return false
  return Date.now() - new Date(lastSeenAt).getTime() < 5 * 60 * 1000
}

export function AgentStatusBadge({
  agent,
  targetVersion,
}: {
  agent: AgentState
  targetVersion?: string
}) {
  if (!agent.installed) {
    return (
      <Badge variant="outline" className="text-zinc-500 border-dashed border-zinc-700">
        No Agent
      </Badge>
    )
  }

  const isOnline = checkIsOnline(agent.lastSeenAt)
  const isOutdated = Boolean(
    targetVersion &&
    agent.version &&
    agent.version.replace(/^v/, '') !== targetVersion.replace(/^v/, '')
  )

  if (isOnline) {
    return (
      <div className="inline-flex items-center gap-1.5 flex-wrap">
        <Badge variant="success" dot pulse>
          Online {agent.version ? `(v${agent.version.replace(/^v/, '')})` : ''}
        </Badge>
        {isOutdated && (
          <span className="inline-flex items-center gap-1 text-[10px] font-medium text-amber-300 bg-amber-950/60 px-1.5 py-0.5 rounded-md border border-amber-700/50">
            <ArrowUpCircle className="w-2.5 h-2.5 text-amber-400" />
            v{targetVersion?.replace(/^v/, '')} avail
          </span>
        )}
      </div>
    )
  }

  return (
    <Badge variant="default" dot className="text-zinc-400 border-zinc-800">
      Offline
    </Badge>
  )
}

export function RebootBadge({ pending }: { pending: boolean }) {
  if (!pending) return null
  return (
    <span className="inline-flex items-center gap-1 text-[11px] font-semibold text-amber-300 bg-amber-950/80 px-2 py-0.5 rounded-full border border-amber-600/60 shadow-xs shadow-amber-900/40 animate-pulse">
      <AlertTriangle className="h-3 w-3 text-amber-400 shrink-0" />
      Reboot Pending
    </span>
  )
}

export function UpdatesBadge({ count }: { count: number }) {
  if (count <= 0) return null
  return (
    <span className="inline-flex items-center gap-1 text-[11px] font-medium text-sky-300 bg-sky-950/60 px-2 py-0.5 rounded-full border border-sky-700/50">
      <ArrowUpCircle className="h-3 w-3 text-sky-400 shrink-0" />
      {count} {count === 1 ? 'update' : 'updates'}
    </span>
  )
}
