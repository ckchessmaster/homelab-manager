import { ChevronDown, ChevronRight, Layers } from 'lucide-react'

export interface NamespaceStatusSummary {
  ready: number
  degraded: number
  progressing: number
  scaledDown: number
}

export interface NamespaceGroupHeaderProps {
  name: string
  count: number
  statusSummary: NamespaceStatusSummary
  clusterName?: string
  isCollapsed: boolean
  onToggle: () => void
  colSpan?: number
}

export function NamespaceGroupHeader({
  name,
  count,
  statusSummary,
  clusterName,
  isCollapsed,
  onToggle,
  colSpan = 6,
}: NamespaceGroupHeaderProps) {
  return (
    <tr
      onClick={onToggle}
      className="cursor-pointer select-none border-y border-zinc-800 bg-zinc-950/90 hover:bg-zinc-900/90 transition-colors"
    >
      <td colSpan={colSpan} className="px-4 py-2.5">
        <div className="flex items-center justify-between gap-3 text-xs">
          <div className="flex items-center gap-2.5 min-w-0">
            <button
              type="button"
              className="p-0.5 rounded hover:bg-zinc-800 text-zinc-400 hover:text-zinc-200 transition-colors"
              title={isCollapsed ? 'Expand namespace' : 'Collapse namespace'}
              aria-label={isCollapsed ? 'Expand namespace' : 'Collapse namespace'}
            >
              {isCollapsed ? (
                <ChevronRight className="h-4 w-4 text-zinc-400 shrink-0" />
              ) : (
                <ChevronDown className="h-4 w-4 text-zinc-400 shrink-0" />
              )}
            </button>

            <Layers className="h-3.5 w-3.5 text-amber-400 shrink-0" />
            <span className="font-semibold text-zinc-300">Namespace:</span>
            <span className="font-mono text-amber-300 font-semibold truncate">{name}</span>

            {clusterName && (
              <>
                <span className="text-zinc-600 hidden sm:inline">•</span>
                <span className="text-[11px] text-zinc-400 font-mono hidden sm:inline">
                  {clusterName}
                </span>
              </>
            )}
          </div>

          <div className="flex items-center gap-2 shrink-0">
            {/* Status Summary Pills */}
            <div className="hidden sm:flex items-center gap-1.5">
              {statusSummary.ready > 0 && (
                <span className="px-1.5 py-0.5 rounded bg-emerald-950/80 border border-emerald-800/60 text-emerald-300 text-[10px] font-mono">
                  {statusSummary.ready} ready
                </span>
              )}
              {statusSummary.degraded > 0 && (
                <span className="px-1.5 py-0.5 rounded bg-rose-950/80 border border-rose-800/60 text-rose-300 text-[10px] font-mono">
                  {statusSummary.degraded} degraded
                </span>
              )}
              {statusSummary.progressing > 0 && (
                <span className="px-1.5 py-0.5 rounded bg-amber-950/80 border border-amber-800/60 text-amber-300 text-[10px] font-mono">
                  {statusSummary.progressing} progressing
                </span>
              )}
              {statusSummary.scaledDown > 0 && (
                <span className="px-1.5 py-0.5 rounded bg-zinc-900 border border-zinc-700/60 text-zinc-400 text-[10px] font-mono">
                  {statusSummary.scaledDown} stopped
                </span>
              )}
            </div>

            <span className="text-[11px] font-mono text-zinc-400 px-2 py-0.5 rounded bg-zinc-800/80 border border-zinc-700/60">
              {count} {count === 1 ? 'workload' : 'workloads'}
            </span>
          </div>
        </div>
      </td>
    </tr>
  )
}
