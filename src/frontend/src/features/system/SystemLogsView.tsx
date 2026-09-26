import { useState, useEffect, useRef, useMemo } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  fetchSystemLogs,
  clearSystemLogs,
  type SystemLogEntryDto,
  type SystemLogResponseDto,
} from '../../api/system'
import { MetricStrip } from '../../components/ui/metric-strip'
import {
  TableToolbar,
  TableToolbarSearch,
  TableToolbarGroup,
  TableToolbarActions,
} from '../../components/ui/table-toolbar'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import {
  Sheet,
  SheetHeader,
  SheetTitle,
  SheetDescription,
  SheetBody,
  SheetFooter,
} from '../../components/ui/sheet'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../components/ui/dialog'
import {
  Terminal,
  AlertOctagon,
  AlertTriangle,
  Info,
  Layers,
  RefreshCw,
  Trash2,
  Download,
  Copy,
  Check,
  Pause,
  Play,
  Filter,
  CheckCircle2,
  Maximize2,
} from 'lucide-react'

export function SystemLogsView() {
  const queryClient = useQueryClient()

  // Filters state
  const [searchTerm, setSearchTerm] = useState('')
  const [levelFilter, setLevelFilter] = useState<string>('All')
  const [categoryFilter, setCategoryFilter] = useState('')
  const [liveTail, setLiveTail] = useState(true)
  const [autoScroll, setAutoScroll] = useState(true)
  const [selectedLog, setSelectedLog] = useState<SystemLogEntryDto | null>(null)
  const [clearDialogOpen, setClearDialogOpen] = useState(false)
  const [copiedId, setCopiedId] = useState<string | null>(null)

  const logContainerRef = useRef<HTMLDivElement>(null)

  // Query parameters calculation
  const queryParams = useMemo(() => {
    return {
      minLevel: levelFilter !== 'All' ? levelFilter : undefined,
      category: categoryFilter.trim() || undefined,
      search: searchTerm.trim() || undefined,
      limit: 300,
      tail: true,
    }
  }, [levelFilter, categoryFilter, searchTerm])

  const { data, isLoading, isFetching, refetch } = useQuery<SystemLogResponseDto>({
    queryKey: ['system-logs', queryParams],
    queryFn: () => fetchSystemLogs(queryParams),
    refetchInterval: liveTail ? 2500 : false,
    refetchOnWindowFocus: true,
  })

  const logs = data?.logs ?? []
  const stats = data?.stats ?? {
    totalCount: 0,
    errorCount: 0,
    warningCount: 0,
    infoCount: 0,
    debugCount: 0,
    capacity: 2500,
  }

  // Clear Mutation
  const clearMutation = useMutation({
    mutationFn: clearSystemLogs,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['system-logs'] })
      setClearDialogOpen(false)
      setSelectedLog(null)
    },
  })

  // Auto-scroll to bottom if autoScroll is enabled
  useEffect(() => {
    if (autoScroll && logContainerRef.current) {
      logContainerRef.current.scrollTop = logContainerRef.current.scrollHeight
    }
  }, [logs, autoScroll])

  const handleCopy = (text: string, id: string) => {
    navigator.clipboard.writeText(text)
    setCopiedId(id)
    setTimeout(() => setCopiedId(null), 2000)
  }

  const handleDownload = () => {
    if (!logs.length) return
    const logContent = logs
      .map(
        (l) =>
          `[${l.timestamp}] [${l.logLevel.padEnd(5)}] [${l.category}] ${l.message}${
            l.exception ? `\nException:\n${l.exception}` : ''
          }`
      )
      .join('\n')

    const blob = new Blob([logContent], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `controlplane-backend-logs-${new Date().toISOString().replace(/[:.]/g, '-')}.log`
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    URL.revokeObjectURL(url)
  }

  const formatTimestamp = (ts: string) => {
    try {
      const d = new Date(ts)
      return d.toLocaleTimeString([], {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        fractionalSecondDigits: 3,
        hour12: false,
      })
    } catch {
      return ts
    }
  }

  const renderLevelBadge = (level: string) => {
    const norm = level.toLowerCase()
    if (norm === 'error' || norm === 'critical') {
      return (
        <span className="px-1.5 py-0.5 rounded text-[10px] font-mono font-bold bg-rose-950/70 border border-rose-800/60 text-rose-300">
          ERR
        </span>
      )
    }
    if (norm === 'warning') {
      return (
        <span className="px-1.5 py-0.5 rounded text-[10px] font-mono font-bold bg-amber-950/70 border border-amber-800/60 text-amber-300">
          WRN
        </span>
      )
    }
    if (norm === 'information') {
      return (
        <span className="px-1.5 py-0.5 rounded text-[10px] font-mono font-bold bg-sky-950/70 border border-sky-800/60 text-sky-300">
          INF
        </span>
      )
    }
    return (
      <span className="px-1.5 py-0.5 rounded text-[10px] font-mono font-bold bg-zinc-900 border border-zinc-800 text-zinc-400">
        DBG
      </span>
    )
  }

  return (
    <div className="space-y-4 w-full">
      {/* 1. Top Compact Metric Strip */}
      <MetricStrip
        items={[
          {
            id: 'buffered-logs',
            label: 'Buffered Logs',
            value: stats.totalCount,
            icon: Terminal,
            iconColor: 'text-sky-400',
            subtext: `Capacity: ${stats.capacity.toLocaleString()}`,
          },
          {
            id: 'log-errors',
            label: 'Errors & Critical',
            value: stats.errorCount,
            icon: AlertOctagon,
            iconColor: stats.errorCount > 0 ? 'text-rose-400' : 'text-zinc-500',
            badge: stats.errorCount > 0 ? `${stats.errorCount} Issues` : 'Clean',
            badgeVariant: stats.errorCount > 0 ? 'warning' : 'success',
            badgeDot: stats.errorCount > 0,
            subtext: 'Requires investigation',
          },
          {
            id: 'log-warnings',
            label: 'Warnings',
            value: stats.warningCount,
            icon: AlertTriangle,
            iconColor: stats.warningCount > 0 ? 'text-amber-400' : 'text-zinc-500',
            badge: stats.warningCount > 0 ? `${stats.warningCount} Warns` : undefined,
            badgeVariant: 'warning',
            subtext: 'Transient or recoverable',
          },
          {
            id: 'log-info',
            label: 'Information / Ops',
            value: stats.infoCount,
            icon: Info,
            iconColor: 'text-sky-400',
            subtext: 'Adapters & agent heartbeats',
          },
        ]}
      />

      {/* 2. Filter & Action Toolbar */}
      <TableToolbar>
        <TableToolbarGroup className="flex-1">
          <TableToolbarSearch
            value={searchTerm}
            onChange={setSearchTerm}
            placeholder="Search logs message, exception, or category..."
            className="max-w-xs"
          />

          {/* Level Filter Dropdown */}
          <div className="flex items-center gap-1.5 bg-zinc-950/80 border border-zinc-800 rounded-lg px-2.5 h-9 text-xs">
            <Filter className="w-3.5 h-3.5 text-zinc-500" />
            <span className="text-zinc-400 text-[11px] font-medium">Min Level:</span>
            <select
              value={levelFilter}
              onChange={(e) => setLevelFilter(e.target.value)}
              className="bg-transparent text-zinc-200 focus:outline-none cursor-pointer font-medium pr-1 text-xs"
            >
              <option value="All" className="bg-zinc-900 text-zinc-200">
                All Levels (Debug+)
              </option>
              <option value="Information" className="bg-zinc-900 text-zinc-200">
                Information & Above
              </option>
              <option value="Warning" className="bg-zinc-900 text-zinc-200">
                Warning & Above
              </option>
              <option value="Error" className="bg-zinc-900 text-zinc-200">
                Errors Only
              </option>
            </select>
          </div>

          {/* Category Filter Input */}
          <div className="hidden sm:flex items-center gap-1.5 bg-zinc-950/80 border border-zinc-800 rounded-lg px-2.5 h-9 text-xs max-w-[200px]">
            <Layers className="w-3.5 h-3.5 text-zinc-500 shrink-0" />
            <input
              type="text"
              value={categoryFilter}
              onChange={(e) => setCategoryFilter(e.target.value)}
              placeholder="Filter Category..."
              className="bg-transparent text-zinc-200 placeholder-zinc-500 focus:outline-none w-full text-xs"
            />
            {categoryFilter && (
              <button
                type="button"
                onClick={() => setCategoryFilter('')}
                className="text-zinc-500 hover:text-zinc-300 text-xs"
              >
                ×
              </button>
            )}
          </div>
        </TableToolbarGroup>

        <TableToolbarActions>
          {/* Live Tail Toggle */}
          <button
            type="button"
            onClick={() => setLiveTail(!liveTail)}
            className={`h-9 px-3 rounded-lg text-xs font-medium flex items-center gap-2 border transition-all cursor-pointer ${
              liveTail
                ? 'bg-emerald-950/40 text-emerald-300 border-emerald-800/60 shadow-xs'
                : 'bg-zinc-950/80 text-zinc-400 border-zinc-800 hover:text-zinc-200 hover:bg-zinc-900'
            }`}
            title={liveTail ? 'Live Tail Active (Auto-refresh every 2.5s)' : 'Live Tail Paused'}
          >
            {liveTail ? (
              <>
                <span className="relative flex h-2 w-2">
                  <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75"></span>
                  <span className="relative inline-flex rounded-full h-2 w-2 bg-emerald-500"></span>
                </span>
                <span>Live Tail</span>
              </>
            ) : (
              <>
                <Pause className="w-3.5 h-3.5 text-zinc-500" />
                <span>Paused</span>
              </>
            )}
          </button>

          {/* Auto Scroll Toggle */}
          <button
            type="button"
            onClick={() => setAutoScroll(!autoScroll)}
            className={`h-9 px-2.5 rounded-lg text-xs font-medium flex items-center gap-1.5 border transition-all cursor-pointer ${
              autoScroll
                ? 'bg-sky-950/40 text-sky-300 border-sky-800/60'
                : 'bg-zinc-950/80 text-zinc-400 border-zinc-800 hover:text-zinc-200'
            }`}
            title="Auto-scroll to latest log entries"
          >
            {autoScroll ? <Play className="w-3.5 h-3.5" /> : <Pause className="w-3.5 h-3.5" />}
            <span className="hidden sm:inline">Auto-scroll</span>
          </button>

          {/* Refresh Manual */}
          <Button
            size="sm"
            variant="outline"
            onClick={() => refetch()}
            disabled={isFetching}
            className="h-9 px-2.5 text-xs text-zinc-300 border-zinc-800 bg-zinc-950/80 hover:bg-zinc-900"
            title="Refresh now"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isFetching ? 'animate-spin text-sky-400' : ''}`} />
          </Button>

          {/* Export / Download */}
          <Button
            size="sm"
            variant="outline"
            onClick={handleDownload}
            disabled={logs.length === 0}
            className="h-9 px-2.5 text-xs text-zinc-300 border-zinc-800 bg-zinc-950/80 hover:bg-zinc-900 gap-1.5"
            title="Export filtered logs as .log file"
          >
            <Download className="w-3.5 h-3.5 text-zinc-400" />
            <span className="hidden md:inline">Export</span>
          </Button>

          {/* Clear Buffer */}
          <Button
            size="sm"
            variant="outline"
            onClick={() => setClearDialogOpen(true)}
            disabled={stats.totalCount === 0}
            className="h-9 px-2.5 text-xs text-rose-400 hover:text-rose-300 border-rose-950/80 bg-rose-950/20 hover:bg-rose-950/40 gap-1.5"
            title="Clear in-memory logs buffer"
          >
            <Trash2 className="w-3.5 h-3.5" />
            <span className="hidden md:inline">Clear</span>
          </Button>
        </TableToolbarActions>
      </TableToolbar>

      {/* 3. Terminal Log Canvas */}
      <div className="bg-zinc-950/90 border border-zinc-800 rounded-xl overflow-hidden shadow-2xl flex flex-col">
        {/* Terminal Title Bar */}
        <div className="px-4 py-2 bg-zinc-900/80 border-b border-zinc-800/80 flex items-center justify-between text-xs text-zinc-400">
          <div className="flex items-center gap-2 font-mono text-[11px]">
            <span className="h-2 w-2 rounded-full bg-sky-500 animate-pulse" />
            <span className="text-zinc-300 font-semibold">ControlPlane.Api</span>
            <span className="text-zinc-600">•</span>
            <span className="text-zinc-500">
              Showing {logs.length} / {data?.totalAvailable ?? stats.totalCount} lines
            </span>
          </div>

          <div className="flex items-center gap-3 text-[11px]">
            {isFetching && (
              <span className="text-sky-400 flex items-center gap-1">
                <RefreshCw className="w-3 h-3 animate-spin" /> Fetching...
              </span>
            )}
            <span className="text-zinc-500 font-mono">Buffer Ring: 2,500</span>
          </div>
        </div>

        {/* Scrollable Log Rows */}
        <div
          ref={logContainerRef}
          className="h-[580px] overflow-y-auto font-mono text-[11.5px] leading-relaxed p-3 space-y-0.5 divide-y divide-zinc-900/60 select-text scrollbar-thin scrollbar-thumb-zinc-800 scrollbar-track-transparent"
        >
          {isLoading && logs.length === 0 ? (
            <div className="h-full flex flex-col items-center justify-center text-zinc-500 space-y-2">
              <RefreshCw className="h-5 w-5 animate-spin text-sky-400" />
              <span>Loading backend application logs...</span>
            </div>
          ) : logs.length === 0 ? (
            <div className="h-full flex flex-col items-center justify-center text-zinc-500 space-y-3">
              <CheckCircle2 className="h-8 w-8 text-zinc-600" />
              <div className="text-center">
                <p className="text-zinc-300 font-medium text-xs">No log entries matched your filter criteria.</p>
                <p className="text-zinc-500 text-[11px] mt-1">
                  Try clearing the search term or switching to &apos;All Levels&apos;.
                </p>
              </div>
              <Button
                size="sm"
                variant="outline"
                onClick={() => {
                  setSearchTerm('')
                  setLevelFilter('All')
                  setCategoryFilter('')
                }}
                className="text-xs border-zinc-800 hover:bg-zinc-800"
              >
                Reset Filters
              </Button>
            </div>
          ) : (
            logs.map((log) => {
              const isError = log.logLevel === 'Error' || log.logLevel === 'Critical'
              const isWarn = log.logLevel === 'Warning'

              return (
                <div
                  key={log.id}
                  onClick={() => setSelectedLog(log)}
                  className={`group py-1 px-2 rounded-md flex items-start gap-2.5 transition-colors cursor-pointer ${
                    isError
                      ? 'bg-rose-950/20 hover:bg-rose-950/40 text-rose-200'
                      : isWarn
                      ? 'bg-amber-950/20 hover:bg-amber-950/40 text-amber-200'
                      : 'hover:bg-zinc-900/60 text-zinc-300'
                  }`}
                >
                  {/* Sequence / Timestamp */}
                  <span className="text-zinc-500 shrink-0 select-none text-[10.5px]">
                    {formatTimestamp(log.timestamp)}
                  </span>

                  {/* Level Badge */}
                  <div className="shrink-0">{renderLevelBadge(log.logLevel)}</div>

                  {/* Category */}
                  <span
                    className="text-zinc-500 shrink-0 max-w-[180px] truncate select-none text-[10.5px]"
                    title={log.category}
                  >
                    {log.category.replace(/^ControlPlane\.Api\./, '').replace(/^Features\./, '')}
                  </span>

                  {/* Log Message */}
                  <span className="flex-1 break-all whitespace-pre-wrap">{log.message}</span>

                  {/* Action / Exception Indicator */}
                  <div className="shrink-0 flex items-center gap-1.5 opacity-0 group-hover:opacity-100 transition-opacity">
                    {log.exception && (
                      <Badge variant="destructive" className="text-[9px] px-1 py-0">
                        Exception
                      </Badge>
                    )}
                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation()
                        handleCopy(log.message, `log-${log.id}`)
                      }}
                      className="text-zinc-400 hover:text-zinc-100 p-0.5 rounded cursor-pointer"
                      title="Copy message"
                    >
                      {copiedId === `log-${log.id}` ? (
                        <Check className="h-3 w-3 text-emerald-400" />
                      ) : (
                        <Copy className="h-3 w-3" />
                      )}
                    </button>
                    <Maximize2 className="h-3 w-3 text-zinc-500 hover:text-sky-400" />
                  </div>
                </div>
              )
            })
          )}
        </div>
      </div>

      {/* 4. Log Details Slide-over Inspector Sheet */}
      <Sheet open={!!selectedLog} onClose={() => setSelectedLog(null)} width="sm:w-[620px]">
        {selectedLog && (
          <>
            <SheetHeader onClose={() => setSelectedLog(null)}>
              <div className="flex items-center gap-2">
                {renderLevelBadge(selectedLog.logLevel)}
                <SheetTitle>Log Entry #{selectedLog.id}</SheetTitle>
              </div>
              <SheetDescription>
                Logged at {new Date(selectedLog.timestamp).toLocaleString()}
              </SheetDescription>
            </SheetHeader>

            <SheetBody className="space-y-4">
              {/* Category & Event Meta */}
              <div className="p-3 bg-zinc-950/80 border border-zinc-800 rounded-lg space-y-2 font-mono text-xs">
                <div className="flex items-center justify-between text-zinc-400">
                  <span className="text-zinc-500">Category:</span>
                  <span className="text-zinc-200 select-all font-semibold">{selectedLog.category}</span>
                </div>
                <div className="flex items-center justify-between text-zinc-400">
                  <span className="text-zinc-500">Timestamp:</span>
                  <span className="text-zinc-200 select-all">{selectedLog.timestamp}</span>
                </div>
                {selectedLog.eventId && (
                  <div className="flex items-center justify-between text-zinc-400">
                    <span className="text-zinc-500">Event ID:</span>
                    <span className="text-zinc-200">{selectedLog.eventId}</span>
                  </div>
                )}
              </div>

              {/* Unwrapped Message */}
              <div className="space-y-1.5">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-semibold text-zinc-300">Message</span>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => handleCopy(selectedLog.message, 'sheet-msg')}
                    className="h-7 text-xs border-zinc-800 bg-zinc-950 text-zinc-300 gap-1.5"
                  >
                    {copiedId === 'sheet-msg' ? (
                      <>
                        <Check className="h-3 w-3 text-emerald-400" />
                        <span className="text-emerald-400">Copied</span>
                      </>
                    ) : (
                      <>
                        <Copy className="h-3 w-3" />
                        <span>Copy Message</span>
                      </>
                    )}
                  </Button>
                </div>
                <pre className="p-3 bg-zinc-950/90 border border-zinc-800 rounded-lg font-mono text-xs text-zinc-200 whitespace-pre-wrap break-all leading-relaxed select-all">
                  {selectedLog.message}
                </pre>
              </div>

              {/* Exception & Stack Trace */}
              {selectedLog.exception && (
                <div className="space-y-1.5">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-semibold text-rose-400 flex items-center gap-1.5">
                      <AlertOctagon className="h-3.5 w-3.5" />
                      Exception Stack Trace
                    </span>
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => handleCopy(selectedLog.exception!, 'sheet-exc')}
                      className="h-7 text-xs border-rose-900/60 bg-rose-950/30 text-rose-300 gap-1.5 hover:bg-rose-950/50"
                    >
                      {copiedId === 'sheet-exc' ? (
                        <>
                          <Check className="h-3 w-3 text-emerald-400" />
                          <span className="text-emerald-400">Copied</span>
                        </>
                      ) : (
                        <>
                          <Copy className="h-3 w-3" />
                          <span>Copy Trace</span>
                        </>
                      )}
                    </Button>
                  </div>
                  <pre className="p-3 bg-rose-950/20 border border-rose-900/50 rounded-lg font-mono text-[11px] text-rose-300 whitespace-pre-wrap break-all leading-normal max-h-80 overflow-y-auto select-all">
                    {selectedLog.exception}
                  </pre>
                </div>
              )}

              {/* Raw JSON */}
              <div className="space-y-1.5">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-semibold text-zinc-400">Raw JSON Payload</span>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => handleCopy(JSON.stringify(selectedLog, null, 2), 'sheet-json')}
                    className="h-7 text-xs border-zinc-800 bg-zinc-950 text-zinc-400 gap-1.5 hover:text-zinc-200"
                  >
                    {copiedId === 'sheet-json' ? (
                      <>
                        <Check className="h-3 w-3 text-emerald-400" />
                        <span>Copied</span>
                      </>
                    ) : (
                      <>
                        <Copy className="h-3 w-3" />
                        <span>Copy JSON</span>
                      </>
                    )}
                  </Button>
                </div>
                <pre className="p-3 bg-zinc-950/60 border border-zinc-800 rounded-lg font-mono text-[10.5px] text-zinc-400 whitespace-pre-wrap max-h-48 overflow-y-auto select-all">
                  {JSON.stringify(selectedLog, null, 2)}
                </pre>
              </div>
            </SheetBody>

            <SheetFooter>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setSelectedLog(null)}
                className="border-zinc-800 text-zinc-300 hover:bg-zinc-800"
              >
                Close
              </Button>
            </SheetFooter>
          </>
        )}
      </Sheet>

      {/* 5. Clear Buffer Confirmation Dialog */}
      <Dialog open={clearDialogOpen} onClose={() => setClearDialogOpen(false)}>
        <DialogHeader>
          <div className="flex items-center gap-2 text-rose-400">
            <Trash2 className="h-5 w-5" />
            <DialogTitle>Clear In-Memory System Logs</DialogTitle>
          </div>
        </DialogHeader>
        <DialogBody>
          <p className="text-xs text-zinc-400 leading-relaxed">
            Are you sure you want to clear the in-memory backend application log buffer? All current{' '}
            <strong className="text-zinc-200">{stats.totalCount} entries</strong> will be permanently wiped
            from the active ring buffer.
          </p>
        </DialogBody>
        <DialogFooter>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setClearDialogOpen(false)}
            className="border-zinc-800 text-zinc-300 hover:bg-zinc-800"
          >
            Cancel
          </Button>
          <Button
            size="sm"
            onClick={() => clearMutation.mutate()}
            disabled={clearMutation.isPending}
            className="bg-rose-600 hover:bg-rose-500 text-white font-medium gap-1.5"
          >
            {clearMutation.isPending ? 'Clearing...' : 'Clear All Logs'}
          </Button>
        </DialogFooter>
      </Dialog>
    </div>
  )
}
