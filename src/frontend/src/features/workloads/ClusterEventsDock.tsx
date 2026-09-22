import { useState, useMemo } from 'react'
import {
  Activity,
  AlertTriangle,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  RefreshCw,
  Search,
  X,
  Filter,
} from 'lucide-react'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { useClusterEvents } from './useWorkloads'

interface ClusterEventsDockProps {
  clusterId?: string
  namespaceName?: string
  externalWorkloadFilter?: string | null
  onClearExternalFilter?: () => void
}

export function ClusterEventsDock({
  clusterId,
  namespaceName,
  externalWorkloadFilter,
  onClearExternalFilter,
}: ClusterEventsDockProps) {
  const [isExpanded, setIsExpanded] = useState(false)
  const [typeFilter, setTypeFilter] = useState<'all' | 'Warning' | 'Normal'>('all')
  const [searchTerm, setSearchTerm] = useState('')
  const [localWorkloadFilter, setLocalWorkloadFilter] = useState<string | null>(null)

  const activeWorkloadFilter = externalWorkloadFilter || localWorkloadFilter

  const {
    data: events = [],
    isLoading,
    isFetching,
    refetch,
  } = useClusterEvents(
    clusterId,
    namespaceName,
    typeFilter === 'all' ? undefined : typeFilter,
    true
  )

  const warningCount = useMemo(() => {
    return events.filter((e) => e.type === 'Warning').length
  }, [events])

  const filteredEvents = useMemo(() => {
    return events.filter((e) => {
      // Type filter (if not already filtered by query)
      if (typeFilter !== 'all' && e.type !== typeFilter) return false

      // Workload name filter
      if (activeWorkloadFilter) {
        const wf = activeWorkloadFilter.toLowerCase()
        const matchesInvolved = e.involvedObjectName.toLowerCase().includes(wf)
        const matchesMsg = e.message.toLowerCase().includes(wf)
        if (!matchesInvolved && !matchesMsg) return false
      }

      // Search term
      if (searchTerm.trim()) {
        const q = searchTerm.toLowerCase()
        const matchesMsg = e.message.toLowerCase().includes(q)
        const matchesReason = e.reason.toLowerCase().includes(q)
        const matchesObj = e.involvedObjectName.toLowerCase().includes(q)
        const matchesNs = e.namespace.toLowerCase().includes(q)
        if (!matchesMsg && !matchesReason && !matchesObj && !matchesNs) return false
      }

      return true
    })
  }, [events, typeFilter, activeWorkloadFilter, searchTerm])

  const latestEvent = events[0]

  const formatAge = (dateStr?: string | null) => {
    if (!dateStr) return 'Just now'
    const diff = Math.floor((Date.now() - new Date(dateStr).getTime()) / 1000)
    if (diff < 60) return `${diff}s ago`
    if (diff < 3600) return `${Math.floor(diff / 60)}m ago`
    if (diff < 86400) return `${Math.floor(diff / 3600)}h ago`
    return `${Math.floor(diff / 86400)}d ago`
  }

  const handleClearWorkloadFilter = () => {
    setLocalWorkloadFilter(null)
    onClearExternalFilter?.()
  }

  return (
    <div className="sticky bottom-0 z-30 w-full transition-all duration-200">
      {/* Collapsed Bar */}
      {!isExpanded ? (
        <div
          onClick={() => setIsExpanded(true)}
          className="flex items-center justify-between px-4 py-2.5 bg-zinc-900/95 border-t border-zinc-800 backdrop-blur-md cursor-pointer hover:bg-zinc-850 transition-colors shadow-xl"
        >
          <div className="flex items-center gap-3 min-w-0">
            <div className="flex items-center gap-2">
              <Activity className="h-4 w-4 text-sky-400 shrink-0" />
              <span className="text-xs font-semibold text-zinc-200">Cluster Events Stream</span>
            </div>

            {warningCount > 0 && (
              <span className="flex items-center gap-1 px-2 py-0.5 rounded-full bg-amber-950/80 border border-amber-800 text-amber-300 text-[10px] font-mono font-semibold">
                <AlertTriangle className="h-3 w-3" />
                {warningCount} Warnings
              </span>
            )}

            {latestEvent && (
              <div className="hidden md:flex items-center gap-2 text-xs text-zinc-400 truncate max-w-md lg:max-w-xl">
                <span className="text-zinc-600">•</span>
                <span className="font-mono text-zinc-300 text-[11px] truncate">
                  [{latestEvent.reason}] {latestEvent.involvedObjectName}: {latestEvent.message}
                </span>
              </div>
            )}
          </div>

          <div className="flex items-center gap-2 shrink-0">
            <span className="text-[11px] text-zinc-400 font-mono">
              {events.length} events
            </span>
            <Button
              variant="ghost"
              size="sm"
              className="h-7 px-2 text-zinc-400 hover:text-zinc-100 text-xs gap-1"
            >
              <span>Expand</span>
              <ChevronUp className="h-4 w-4" />
            </Button>
          </div>
        </div>
      ) : (
        /* Expanded Dock */
        <div className="flex flex-col h-80 sm:h-96 bg-zinc-950 border-t border-zinc-800 shadow-2xl overflow-hidden animate-in slide-in-from-bottom duration-200">
          {/* Dock Toolbar */}
          <div className="flex flex-wrap items-center justify-between gap-3 p-3 bg-zinc-900/90 border-b border-zinc-800 shrink-0">
            <div className="flex flex-wrap items-center gap-2.5">
              <div className="flex items-center gap-2">
                <Activity className="h-4 w-4 text-sky-400" />
                <h4 className="text-xs font-bold text-zinc-200 uppercase tracking-wider">
                  Cluster Events Stream
                </h4>
              </div>

              {/* Type Filter Chips */}
              <div className="flex items-center gap-1 p-0.5 bg-zinc-950 border border-zinc-800 rounded-lg">
                <button
                  type="button"
                  onClick={() => setTypeFilter('all')}
                  className={`px-2 py-0.5 text-[11px] rounded font-medium transition-colors cursor-pointer ${
                    typeFilter === 'all'
                      ? 'bg-zinc-800 text-white'
                      : 'text-zinc-400 hover:text-zinc-200'
                  }`}
                >
                  All ({events.length})
                </button>
                <button
                  type="button"
                  onClick={() => setTypeFilter('Warning')}
                  className={`px-2 py-0.5 text-[11px] rounded font-medium transition-colors flex items-center gap-1 cursor-pointer ${
                    typeFilter === 'Warning'
                      ? 'bg-amber-950 border border-amber-800 text-amber-300'
                      : 'text-zinc-400 hover:text-amber-300'
                  }`}
                >
                  <AlertTriangle className="h-3 w-3 text-amber-400" />
                  <span>Warnings ({warningCount})</span>
                </button>
                <button
                  type="button"
                  onClick={() => setTypeFilter('Normal')}
                  className={`px-2 py-0.5 text-[11px] rounded font-medium transition-colors flex items-center gap-1 cursor-pointer ${
                    typeFilter === 'Normal'
                      ? 'bg-emerald-950 border border-emerald-800 text-emerald-300'
                      : 'text-zinc-400 hover:text-emerald-300'
                  }`}
                >
                  <CheckCircle2 className="h-3 w-3 text-emerald-400" />
                  <span>Normal</span>
                </button>
              </div>

              {/* Active Workload Filter Chip */}
              {activeWorkloadFilter && (
                <div className="flex items-center gap-1 px-2 py-0.5 rounded-full bg-sky-950/80 border border-sky-800 text-sky-300 text-xs font-mono">
                  <Filter className="h-3 w-3" />
                  <span>Workload: {activeWorkloadFilter}</span>
                  <button
                    type="button"
                    onClick={handleClearWorkloadFilter}
                    className="p-0.5 hover:text-white rounded-full transition-colors cursor-pointer"
                    title="Clear workload filter"
                  >
                    <X className="h-3 w-3" />
                  </button>
                </div>
              )}
            </div>

            <div className="flex items-center gap-2">
              {/* Search input */}
              <div className="relative w-48 sm:w-64">
                <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-500" />
                <Input
                  placeholder="Filter events or reasons..."
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                  className="pl-8 text-xs bg-zinc-950 border-zinc-800 h-7"
                />
              </div>

              <Button
                variant="ghost"
                size="sm"
                onClick={() => refetch()}
                disabled={isFetching}
                className="h-7 w-7 p-0 text-zinc-400 hover:text-zinc-100"
                title="Refresh events"
              >
                <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} />
              </Button>

              <Button
                variant="ghost"
                size="sm"
                onClick={() => setIsExpanded(false)}
                className="h-7 px-2 text-zinc-400 hover:text-zinc-100 text-xs gap-1"
                title="Minimize events dock"
              >
                <span>Minimize</span>
                <ChevronDown className="h-4 w-4" />
              </Button>
            </div>
          </div>

          {/* Events Table Container */}
          <div className="flex-1 overflow-auto bg-zinc-950">
            {isLoading && events.length === 0 ? (
              <div className="p-12 text-center text-xs text-zinc-500 space-y-2">
                <RefreshCw className="h-5 w-5 animate-spin mx-auto text-sky-400" />
                <span>Polling Kubernetes events from cluster...</span>
              </div>
            ) : filteredEvents.length === 0 ? (
              <div className="p-12 text-center text-xs text-zinc-500 space-y-1">
                <Activity className="h-6 w-6 mx-auto text-zinc-700" />
                <div className="font-semibold text-zinc-300">No events found</div>
                <div>
                  {activeWorkloadFilter || searchTerm || typeFilter !== 'all'
                    ? 'No events match your current filters.'
                    : 'The cluster has emitted no events in this namespace.'}
                </div>
              </div>
            ) : (
              <table className="w-full text-left text-xs border-collapse">
                <thead className="sticky top-0 z-10 bg-zinc-950 text-[10px] uppercase font-semibold text-zinc-400 border-b border-zinc-800">
                  <tr>
                    <th className="py-2 px-3 w-20">Type</th>
                    <th className="py-2 px-3 w-24">Age</th>
                    <th className="py-2 px-3 w-48">Involved Object</th>
                    <th className="py-2 px-3 w-32">Reason</th>
                    <th className="py-2 px-3">Message</th>
                    <th className="py-2 px-3 w-28 text-right">Source</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-900 font-mono text-[11px]">
                  {filteredEvents.map((ev, idx) => {
                    const isWarning = ev.type === 'Warning'
                    return (
                      <tr
                        key={`${ev.name}-${idx}`}
                        className="hover:bg-zinc-900/50 transition-colors"
                      >
                        <td className="py-2 px-3 font-sans">
                          <Badge
                            variant={isWarning ? 'destructive' : 'default'}
                            className={`text-[9px] py-0 px-1.5 ${
                              isWarning
                                ? 'bg-rose-950/80 text-rose-300 border-rose-700'
                                : 'bg-zinc-900 text-zinc-400 border-zinc-800'
                            }`}
                          >
                            {ev.type}
                          </Badge>
                        </td>

                        <td className="py-2 px-3 text-zinc-500 text-[10px] font-sans">
                          {formatAge(ev.lastTimestamp)}
                        </td>

                        <td className="py-2 px-3">
                          <button
                            type="button"
                            onClick={() => setLocalWorkloadFilter(ev.involvedObjectName)}
                            className="font-bold text-sky-400 hover:text-sky-300 hover:underline cursor-pointer truncate max-w-[180px] text-left block"
                            title={`Filter events for ${ev.involvedObjectName}`}
                          >
                            <span className="text-zinc-500 font-normal">
                              {ev.involvedObjectKind}/
                            </span>
                            {ev.involvedObjectName}
                          </button>
                        </td>

                        <td className="py-2 px-3 text-amber-300 font-semibold truncate max-w-[130px]">
                          {ev.reason}
                        </td>

                        <td className="py-2 px-3 text-zinc-300 font-sans font-normal leading-tight">
                          {ev.message}
                          {ev.count && ev.count > 1 ? (
                            <span className="ml-1.5 text-[10px] px-1 py-0.2 rounded bg-zinc-900 border border-zinc-800 text-zinc-400 font-mono">
                              ×{ev.count}
                            </span>
                          ) : null}
                        </td>

                        <td className="py-2 px-3 text-right text-zinc-500 text-[10px] truncate max-w-[110px]">
                          {ev.sourceComponent || 'k8s-engine'}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
