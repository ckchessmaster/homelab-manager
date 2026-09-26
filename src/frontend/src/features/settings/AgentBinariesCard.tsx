import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  fetchAgentBinaryStatus,
  syncAgentBinaries,
  type AgentBinaryStatusDto,
} from '../../api/agentBinaries'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import {
  Cpu,
  RefreshCw,
  Download,
  AlertTriangle,
  Clock,
  ExternalLink,
  Loader2,
  Check,
  HardDrive,
} from 'lucide-react'

export function AgentBinariesCard() {
  const queryClient = useQueryClient()
  const [forceSync, setForceSync] = useState(false)
  const [actionFeedback, setActionFeedback] = useState<string | null>(null)

  const { data: status, isLoading, isError } = useQuery<AgentBinaryStatusDto>({
    queryKey: ['agent-binaries-status'],
    queryFn: fetchAgentBinaryStatus,
    refetchInterval: 15000,
  })

  const syncMutation = useMutation({
    mutationFn: (force: boolean) => syncAgentBinaries(force),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['agent-binaries-status'] })
      queryClient.invalidateQueries({ queryKey: ['agent-version-info'] })
      queryClient.invalidateQueries({ queryKey: ['hosts'] })
      setActionFeedback(result.message)
      setTimeout(() => setActionFeedback(null), 6000)
    },
    onError: (err: Error) => {
      setActionFeedback(`Sync failed: ${err.message}`)
      setTimeout(() => setActionFeedback(null), 6000)
    },
  })

  const handleSync = () => {
    setActionFeedback(null)
    syncMutation.mutate(forceSync)
  }

  const formatFileSize = (bytes?: number | null) => {
    if (!bytes || bytes <= 0) return '0 B'
    const mb = bytes / (1024 * 1024)
    return `${mb.toFixed(2)} MB`
  }

  const formatDate = (isoString?: string | null) => {
    if (!isoString) return 'Never'
    try {
      const d = new Date(isoString)
      return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }) +
             ' (' + d.toLocaleDateString() + ')'
    } catch {
      return isoString
    }
  }

  const isSyncInProgress = syncMutation.isPending || status?.isSyncing

  return (
    <div className="p-6 bg-zinc-900/60 border border-zinc-800 rounded-xl space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <Cpu className="h-5 w-5 text-sky-400" />
            <h3 className="text-base font-semibold text-zinc-100">
              Compute Node Agent Binaries
            </h3>
            {status && (
              <Badge
                variant={
                  status.status === 'Success' || status.status === 'UpToDate'
                    ? 'success'
                    : status.status === 'Checking' || status.status === 'Downloading'
                    ? 'info'
                    : status.status === 'Failed'
                    ? 'destructive'
                    : 'default'
                }
              >
                {status.status === 'Success' || status.status === 'UpToDate'
                  ? 'Synchronized'
                  : status.status === 'Checking'
                  ? 'Checking...'
                  : status.status === 'Downloading'
                  ? 'Downloading...'
                  : status.status === 'Failed'
                  ? 'Sync Degraded'
                  : 'Idle'}
              </Badge>
            )}
          </div>
          <p className="text-xs text-zinc-400 leading-relaxed max-w-xl">
            Precompiled static Go binaries used for one-click SSH bootstrap adoption and automated agent upgrades.
            Automatically checked on startup and synchronized periodically from GitHub releases.
          </p>
        </div>

        {/* Sync Controls */}
        <div className="flex items-center gap-3 self-start sm:self-center">
          <label className="flex items-center gap-2 text-xs text-zinc-400 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={forceSync}
              onChange={(e) => setForceSync(e.target.checked)}
              className="rounded border-zinc-700 bg-zinc-950 text-sky-500 focus:ring-sky-500/20"
            />
            <span>Force re-download</span>
          </label>

          <Button
            size="sm"
            onClick={handleSync}
            disabled={isSyncInProgress}
            className="bg-sky-600 hover:bg-sky-500 text-white gap-2 font-medium"
          >
            {isSyncInProgress ? (
              <>
                <Loader2 className="h-3.5 w-3.5 animate-spin" />
                <span>Syncing...</span>
              </>
            ) : (
              <>
                <RefreshCw className="h-3.5 w-3.5" />
                <span>Sync Binaries Now</span>
              </>
            )}
          </Button>
        </div>
      </div>

      {/* Action feedback message */}
      {actionFeedback && (
        <div
          className={`p-3 rounded-lg border text-xs flex items-center gap-2 ${
            actionFeedback.includes('failed') || actionFeedback.includes('Error')
              ? 'bg-rose-950/40 border-rose-800/60 text-rose-300'
              : 'bg-emerald-950/40 border-emerald-800/60 text-emerald-300'
          }`}
        >
          {actionFeedback.includes('failed') ? (
            <AlertTriangle className="h-4 w-4 shrink-0 text-rose-400" />
          ) : (
            <Check className="h-4 w-4 shrink-0 text-emerald-400" />
          )}
          <span>{actionFeedback}</span>
        </div>
      )}

      {/* Error Banner */}
      {status?.lastError && (
        <div className="p-3 bg-amber-950/30 border border-amber-800/50 rounded-lg text-xs flex items-start gap-2.5 text-amber-300">
          <AlertTriangle className="h-4 w-4 shrink-0 text-amber-400 mt-0.5" />
          <div className="space-y-0.5">
            <span className="font-medium">GitHub Synchronization Warning</span>
            <p className="text-amber-400/80 leading-normal">{status.lastError}</p>
          </div>
        </div>
      )}

      {/* Metadata Strip */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <div className="p-3 bg-zinc-950/60 border border-zinc-800 rounded-lg space-y-1">
          <span className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider block">Active Version</span>
          <div className="flex items-center gap-1.5 font-mono text-xs text-sky-400 font-semibold">
            <span>{status?.currentInstalledVersion ? `v${status.currentInstalledVersion.replace(/^v/, '')}` : '1.4.0'}</span>
          </div>
        </div>

        <div className="p-3 bg-zinc-950/60 border border-zinc-800 rounded-lg space-y-1">
          <span className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider block">Latest Release</span>
          <div className="flex items-center gap-1.5 font-mono text-xs text-zinc-200">
            <span>{status?.latestAvailableVersion ? status.latestAvailableVersion : 'Checking...'}</span>
            {status?.repository && (
              <a
                href={`https://github.com/${status.repository}/releases`}
                target="_blank"
                rel="noreferrer"
                className="text-zinc-500 hover:text-zinc-300 inline-flex items-center"
                title="View GitHub Releases"
              >
                <ExternalLink className="h-3 w-3" />
              </a>
            )}
          </div>
        </div>

        <div className="p-3 bg-zinc-950/60 border border-zinc-800 rounded-lg space-y-1">
          <span className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider block">Sync Policy</span>
          <div className="flex items-center gap-1.5 text-xs text-zinc-300">
            <Clock className="h-3.5 w-3.5 text-zinc-500" />
            <span>Every {status?.syncIntervalHours ?? 6}h & on restart</span>
          </div>
        </div>

        <div className="p-3 bg-zinc-950/60 border border-zinc-800 rounded-lg space-y-1">
          <span className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider block">Last Synchronized</span>
          <div className="text-xs text-zinc-400 font-mono">
            {formatDate(status?.lastDownloadedAtUtc || status?.lastCheckedAtUtc)}
          </div>
        </div>
      </div>

      {/* Platform Binaries Matrix */}
      <div className="space-y-2">
        <span className="text-xs font-semibold text-zinc-300 flex items-center gap-2">
          <HardDrive className="h-4 w-4 text-zinc-400" />
          Platform Distribution Files
        </span>

        <div className="border border-zinc-800 rounded-lg overflow-hidden divide-y divide-zinc-800 bg-zinc-950/40">
          {isLoading ? (
            <div className="p-6 text-center text-xs text-zinc-500 flex items-center justify-center gap-2">
              <Loader2 className="h-4 w-4 animate-spin text-sky-400" />
              <span>Loading binary distribution status...</span>
            </div>
          ) : isError ? (
            <div className="p-4 text-center text-xs text-rose-400">
              Failed to load agent binary status.
            </div>
          ) : (
            status?.platforms.map((platform) => (
              <div
                key={platform.architecture}
                className="p-3.5 flex flex-col sm:flex-row sm:items-center justify-between gap-3 hover:bg-zinc-900/40 transition-colors"
              >
                <div className="flex items-center gap-3">
                  <div className="flex flex-col">
                    <div className="flex items-center gap-2">
                      <span className="text-xs font-medium text-zinc-200">{platform.displayName}</span>
                      <Badge variant={platform.isAvailable ? 'success' : 'default'}>
                        {platform.isAvailable ? 'Available' : 'Missing'}
                      </Badge>
                    </div>
                    <span className="text-[11px] font-mono text-zinc-500">{platform.fileName}</span>
                  </div>
                </div>

                <div className="flex items-center gap-4 text-xs text-zinc-400">
                  {platform.isAvailable ? (
                    <>
                      <span className="font-mono text-zinc-400">{formatFileSize(platform.sizeBytes)}</span>
                      <span className="text-zinc-600">|</span>
                      <span className="text-zinc-500 text-[11px]">
                        {formatDate(platform.lastModifiedAtUtc)}
                      </span>
                      <a
                        href={platform.downloadPath}
                        download={platform.fileName}
                        className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded bg-zinc-800 hover:bg-zinc-700 text-zinc-200 text-xs font-medium transition-colors"
                      >
                        <Download className="h-3 w-3 text-sky-400" />
                        <span>Download</span>
                      </a>
                    </>
                  ) : (
                    <span className="text-zinc-500 italic">Not yet downloaded</span>
                  )}
                </div>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  )
}
