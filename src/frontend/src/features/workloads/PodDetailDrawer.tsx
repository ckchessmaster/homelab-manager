import { useState, useMemo, useEffect, useRef } from 'react'
import {
  X,
  Boxes,
  Cpu,
  RefreshCw,
  Copy,
  Check,
  CheckCircle2,
  AlertCircle,
  Clock,
  Terminal,
  History,
  RotateCcw,
  Search,
  ArrowDown,
  Layers,
  AlertTriangle,
  Loader2,
  ArrowUpCircle,
  Tag,
  Key,
  Lock,
  Eye,
  EyeOff,
  Sliders,
  Pencil,
  Plus,
} from 'lucide-react'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import {
  useWorkloadPods,
  usePodLogs,
  useWorkloadRevisions,
  useRollbackWorkloadRevision,
  useAppBundle,
} from './useWorkloads'
import { useAuthUser } from '../auth/useAuthUser'
import type { WorkloadSummary, RolloutRevision } from '../../api/workloads'

interface PodDetailDrawerProps {
  workload: WorkloadSummary | null
  isOpen: boolean
  onClose: () => void
  initialTab?: 'pods' | 'logs' | 'revisions' | 'env'
  onEditWorkload?: (workload: WorkloadSummary) => void
}

export function PodDetailDrawer({
  workload,
  isOpen,
  onClose,
  initialTab = 'pods',
  onEditWorkload,
}: PodDetailDrawerProps) {
  const [activeTab, setActiveTab] = useState<'pods' | 'logs' | 'revisions' | 'env'>(initialTab)
  const [copiedPod, setCopiedPod] = useState<string | null>(null)
  const [copiedLogs, setCopiedLogs] = useState(false)

  // Live Logs state
  const [selectedPodName, setSelectedPodName] = useState<string>('')
  const [selectedContainer, setSelectedContainer] = useState<string>('')
  const [tailLines, setTailLines] = useState<number>(100)
  const [logSearch, setLogSearch] = useState<string>('')
  const [autoScroll, setAutoScroll] = useState<boolean>(true)
  const logContainerRef = useRef<HTMLDivElement>(null)

  // Rollback state & confirmation modal
  const [confirmRollbackRev, setConfirmRollbackRev] = useState<RolloutRevision | null>(null)
  const [rollbackSuccessMsg, setRollbackSuccessMsg] = useState<string | null>(null)

  const { isOperator } = useAuthUser()

  // Reset tab when workload changes or drawer re-opens
  useEffect(() => {
    if (isOpen) {
      setActiveTab(initialTab)
    }
  }, [isOpen, initialTab, workload?.name])

  // Queries
  const {
    data: pods,
    isLoading: isPodsLoading,
    isRefetching: isPodsRefetching,
    refetch: refetchPods,
  } = useWorkloadPods(
    workload?.clusterId,
    workload?.namespace,
    workload?.name,
    isOpen
  )

  // Keep selected pod name in sync with available pods
  useEffect(() => {
    if (pods && pods.length > 0) {
      const exists = pods.some((p) => p.name === selectedPodName)
      if (!exists || !selectedPodName) {
        setSelectedPodName(pods[0].name)
      }
    } else {
      setSelectedPodName('')
    }
  }, [pods, selectedPodName])

  // Current pod object
  const currentPod = useMemo(() => {
    return pods?.find((p) => p.name === selectedPodName) || pods?.[0]
  }, [pods, selectedPodName])

  // Keep container in sync with selected pod
  useEffect(() => {
    if (currentPod?.containers && currentPod.containers.length > 0) {
      if (!selectedContainer || !currentPod.containers.includes(selectedContainer)) {
        setSelectedContainer(currentPod.containers[0])
      }
    } else {
      setSelectedContainer('')
    }
  }, [currentPod, selectedContainer])

  // Pod logs query
  const {
    data: logResult,
    isLoading: isLogsLoading,
    isRefetching: isLogsRefetching,
    refetch: refetchLogs,
  } = usePodLogs(
    workload?.clusterId,
    workload?.namespace,
    selectedPodName,
    selectedContainer || undefined,
    tailLines,
    isOpen && activeTab === 'logs' && Boolean(selectedPodName)
  )

  // Revisions query
  const {
    data: revisions,
    isLoading: isRevisionsLoading,
    isRefetching: isRevisionsRefetching,
    refetch: refetchRevisions,
  } = useWorkloadRevisions(
    workload?.clusterId,
    workload?.namespace,
    workload?.name,
    isOpen && activeTab === 'revisions'
  )

  // App bundle (for inspecting environment variables and container specs)
  const {
    data: appBundle,
    isLoading: isBundleLoading,
    isRefetching: isBundleRefetching,
    refetch: refetchBundle,
  } = useAppBundle(
    workload?.clusterId,
    workload?.namespace,
    workload?.name
  )

  // Environment tab state
  const [envSearch, setEnvSearch] = useState<string>('')
  const [selectedEnvContainer, setSelectedEnvContainer] = useState<string>('all')
  const [envTypeFilter, setEnvTypeFilter] = useState<'all' | 'literal' | 'secret' | 'configMap'>('all')
  const [revealedValues, setRevealedValues] = useState<Record<string, boolean>>({})
  const [copiedEnvKey, setCopiedEnvKey] = useState<string | null>(null)

  const handleCopyEnv = (text: string, id: string) => {
    navigator.clipboard.writeText(text)
    setCopiedEnvKey(id)
    setTimeout(() => setCopiedEnvKey(null), 2000)
  }

  const toggleRevealValue = (key: string) => {
    setRevealedValues((prev) => ({
      ...prev,
      [key]: !prev[key],
    }))
  }

  const allEnvVars = useMemo(() => appBundle?.environmentVariables || [], [appBundle])
  const envFromSources = useMemo(() => appBundle?.envFrom || [], [appBundle])

  const envContainers = useMemo(() => {
    const set = new Set<string>()
    allEnvVars.forEach((ev) => {
      if (ev.containerName) set.add(ev.containerName)
    })
    envFromSources.forEach((ef) => {
      if (ef.containerName) set.add(ef.containerName)
    })
    return Array.from(set)
  }, [allEnvVars, envFromSources])

  const filteredEnvVars = useMemo(() => {
    return allEnvVars.filter((ev) => {
      if (selectedEnvContainer !== 'all' && ev.containerName && ev.containerName !== selectedEnvContainer) {
        return false
      }
      const isSec = ev.isSecret || Boolean(ev.secretName)
      const isCm = Boolean(ev.configMapName)
      const isLit = !isSec && !isCm

      if (envTypeFilter === 'secret' && !isSec) return false
      if (envTypeFilter === 'configMap' && !isCm) return false
      if (envTypeFilter === 'literal' && !isLit) return false

      if (!envSearch.trim()) return true
      const q = envSearch.toLowerCase()
      return (
        ev.key.toLowerCase().includes(q) ||
        ev.value.toLowerCase().includes(q) ||
        (ev.secretName && ev.secretName.toLowerCase().includes(q)) ||
        (ev.secretKey && ev.secretKey.toLowerCase().includes(q)) ||
        (ev.configMapName && ev.configMapName.toLowerCase().includes(q))
      )
    })
  }, [allEnvVars, selectedEnvContainer, envTypeFilter, envSearch])

  const totalEnvCount = allEnvVars.length
  const secretEnvCount = allEnvVars.filter((ev) => ev.isSecret || Boolean(ev.secretName)).length
  const configMapEnvCount = allEnvVars.filter((ev) => Boolean(ev.configMapName)).length
  const literalEnvCount = totalEnvCount - secretEnvCount - configMapEnvCount

  const rollbackMutation = useRollbackWorkloadRevision()

  // Auto-scroll logs
  useEffect(() => {
    if (autoScroll && logContainerRef.current) {
      logContainerRef.current.scrollTop = logContainerRef.current.scrollHeight
    }
  }, [logResult?.logs, autoScroll])

  if (!isOpen || !workload) return null

  const handleCopy = (text: string) => {
    navigator.clipboard.writeText(text)
    setCopiedPod(text)
    setTimeout(() => setCopiedPod(null), 2000)
  }

  const handleCopyLogs = () => {
    if (!logResult?.logs) return
    navigator.clipboard.writeText(logResult.logs)
    setCopiedLogs(true)
    setTimeout(() => setCopiedLogs(false), 2000)
  }

  const handleConfirmRollback = async () => {
    if (!confirmRollbackRev) return
    try {
      await rollbackMutation.mutateAsync({
        clusterId: workload.clusterId,
        namespaceName: workload.namespace,
        name: workload.name,
        revision: confirmRollbackRev.revision,
      })
      setRollbackSuccessMsg(`Rolled back to revision #${confirmRollbackRev.revision}`)
      setConfirmRollbackRev(null)
      refetchRevisions()
      refetchPods()
      setTimeout(() => setRollbackSuccessMsg(null), 4000)
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to roll back revision')
    }
  }

  const runningCount = pods?.filter((p) => p.phase === 'Running').length ?? 0
  const totalRestarts = pods?.reduce((acc, p) => acc + p.restartCount, 0) ?? 0

  // Filter logs by search query
  const displayedLogs = (logResult?.logs || '')
    .split('\n')
    .filter((line) => {
      if (!logSearch.trim()) return true
      return line.toLowerCase().includes(logSearch.toLowerCase())
    })
    .join('\n')

  return (
    <>
      {/* Backdrop */}
      <div
        className="fixed inset-0 z-40 bg-black/70 backdrop-blur-xs animate-in fade-in"
        onClick={onClose}
      />

      {/* Slide-over Drawer: InspectorSheet w-[640px] */}
      <div className="fixed inset-y-0 right-0 z-50 w-full sm:w-[640px] max-w-[640px] bg-zinc-950 border-l border-zinc-800 shadow-2xl flex flex-col animate-in slide-in-from-right duration-250 ease-out">
        {/* Header */}
        <div className="p-4 border-b border-zinc-800 bg-zinc-900/60 shrink-0">
          <div className="flex items-center justify-between gap-3">
            <div className="flex items-center gap-2.5 min-w-0">
              <div className="p-2 rounded-lg bg-sky-500/10 text-sky-400 border border-sky-500/20 shrink-0">
                <Boxes className="w-5 h-5" />
              </div>
              <div className="min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <h2 className="text-base font-semibold font-mono text-zinc-100 truncate">
                    {workload.name}
                  </h2>
                  {workload.kind && (
                    <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-zinc-800 text-zinc-300 border border-zinc-700/60">
                      {workload.kind}
                    </span>
                  )}
                  <Badge variant="info" className="text-xs">
                    {workload.clusterName || workload.clusterId}
                  </Badge>
                </div>
                <div className="flex items-center gap-2 text-xs text-zinc-400 mt-0.5 font-sans">
                  <span>Namespace:</span>
                  <span className="font-mono text-amber-300 font-semibold">{workload.namespace}</span>
                  <span>•</span>
                  <span>
                    {workload.readyReplicas} / {workload.desiredReplicas} Ready
                  </span>
                </div>
              </div>
            </div>

            <div className="flex items-center gap-1 shrink-0">
              <Button
                variant="ghost"
                size="sm"
                onClick={() => {
                  if (activeTab === 'pods') refetchPods()
                  else if (activeTab === 'logs') refetchLogs()
                  else if (activeTab === 'env') refetchBundle()
                  else if (activeTab === 'revisions') refetchRevisions()
                }}
                className="text-zinc-400 hover:text-zinc-100 h-8 w-8 p-0"
                title="Refresh"
              >
                <RefreshCw
                  className={`w-4 h-4 ${
                    isPodsRefetching || isLogsRefetching || isRevisionsRefetching || isBundleRefetching
                      ? 'animate-spin'
                      : ''
                  }`}
                />
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={onClose}
                className="text-zinc-400 hover:text-zinc-100 h-8 w-8 p-0"
                title="Close drawer"
              >
                <X className="w-5 h-5" />
              </Button>
            </div>
          </div>

          {/* Image & Update Status Banner */}
          {workload.imageUpdate?.isOutdated ? (
            <div className="mt-3 p-2.5 rounded-xl bg-amber-950/40 border border-amber-800/60 flex items-center justify-between gap-3 shadow-xs">
              <div className="flex items-center gap-2.5 min-w-0">
                <div className="p-1.5 rounded-lg bg-amber-900/60 border border-amber-700/60 text-amber-400 shrink-0">
                  <ArrowUpCircle className="h-4 w-4" />
                </div>
                <div className="min-w-0">
                  <div className="flex items-center gap-1.5">
                    <span className="text-xs font-bold text-amber-200">Image Update Available</span>
                    {workload.imageUpdate.updateType && (
                      <Badge variant="warning" className="text-[9px] uppercase px-1 py-0 font-mono">
                        {workload.imageUpdate.updateType}
                      </Badge>
                    )}
                  </div>
                  <div className="text-[11px] font-mono text-amber-300/80 truncate mt-0.5">
                    Current: <span className="text-zinc-300">{workload.imageUpdate.currentTag}</span> &rarr; Latest: <span className="text-emerald-300 font-semibold">{workload.imageUpdate.latestTag}</span>
                  </div>
                </div>
              </div>
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  const newImg = workload.imageUpdate?.latestTag
                    ? (workload.images[0] || '').replace(workload.imageUpdate.currentTag, workload.imageUpdate.latestTag)
                    : null
                  if (newImg) {
                    navigator.clipboard.writeText(newImg)
                    alert(`Copied updated image tag: ${newImg}`)
                  }
                }}
                className="h-7 px-2 text-[11px] border-amber-700/60 bg-amber-950/80 text-amber-200 hover:bg-amber-900 hover:text-white shrink-0"
                title="Copy updated image reference"
              >
                <Copy className="h-3 w-3 mr-1" />
                Copy Tag
              </Button>
            </div>
          ) : workload.imageUpdate && !workload.imageUpdate.isOutdated && workload.imageUpdate.latestTag ? (
            <div className="mt-3 px-3 py-1.5 rounded-lg bg-emerald-950/30 border border-emerald-800/40 flex items-center justify-between text-xs text-emerald-300">
              <div className="flex items-center gap-2">
                <CheckCircle2 className="h-3.5 w-3.5 text-emerald-400 shrink-0" />
                <span>Primary image tag <span className="font-mono text-emerald-200 font-semibold">{workload.imageUpdate.currentTag}</span> is up to date</span>
              </div>
              <span className="text-[10px] text-zinc-500 font-mono">Verified</span>
            </div>
          ) : workload.images?.[0] ? (
            <div className="mt-3 px-3 py-1.5 rounded-lg bg-zinc-950/60 border border-zinc-800 flex items-center justify-between text-xs text-zinc-400">
              <div className="flex items-center gap-1.5 min-w-0">
                <Tag className="h-3.5 w-3.5 text-zinc-500 shrink-0" />
                <span className="font-mono text-[11px] text-zinc-300 truncate">{workload.images[0]}</span>
              </div>
              <button
                type="button"
                onClick={() => navigator.clipboard.writeText(workload.images[0])}
                className="text-zinc-500 hover:text-zinc-300 p-1 cursor-pointer"
                title="Copy image"
              >
                <Copy className="h-3 w-3" />
              </button>
            </div>
          ) : null}

          {/* Tab Navigation */}
          <div className="flex items-center gap-1 mt-4 p-1 bg-zinc-950/80 border border-zinc-800 rounded-lg">
            <button
              type="button"
              onClick={() => setActiveTab('pods')}
              className={`flex-1 flex items-center justify-center gap-1.5 py-1.5 px-2 rounded-md text-xs font-semibold transition-all cursor-pointer ${
                activeTab === 'pods'
                  ? 'bg-zinc-800 text-white shadow-xs'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900/60'
              }`}
            >
              <Boxes className="h-3.5 w-3.5 text-sky-400" />
              <span>Pods ({pods?.length ?? 0})</span>
            </button>

            <button
              type="button"
              onClick={() => setActiveTab('logs')}
              className={`flex-1 flex items-center justify-center gap-1.5 py-1.5 px-2 rounded-md text-xs font-semibold transition-all cursor-pointer ${
                activeTab === 'logs'
                  ? 'bg-zinc-800 text-white shadow-xs'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900/60'
              }`}
            >
              <Terminal className="h-3.5 w-3.5 text-emerald-400" />
              <span>Live Logs</span>
            </button>

            <button
              type="button"
              onClick={() => setActiveTab('env')}
              className={`flex-1 flex items-center justify-center gap-1.5 py-1.5 px-2 rounded-md text-xs font-semibold transition-all cursor-pointer ${
                activeTab === 'env'
                  ? 'bg-zinc-800 text-white shadow-xs'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900/60'
              }`}
            >
              <Key className="h-3.5 w-3.5 text-amber-400" />
              <span>Environment ({totalEnvCount})</span>
            </button>

            <button
              type="button"
              onClick={() => setActiveTab('revisions')}
              className={`flex-1 flex items-center justify-center gap-1.5 py-1.5 px-2 rounded-md text-xs font-semibold transition-all cursor-pointer ${
                activeTab === 'revisions'
                  ? 'bg-zinc-800 text-white shadow-xs'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900/60'
              }`}
            >
              <History className="h-3.5 w-3.5 text-purple-400" />
              <span>Rollout Revisions</span>
            </button>
          </div>
        </div>

        {/* Feedback Alert if rollback triggered */}
        {rollbackSuccessMsg && (
          <div className="mx-4 mt-3 p-3 rounded-lg bg-emerald-950/60 border border-emerald-800 text-xs text-emerald-300 flex items-center gap-2">
            <CheckCircle2 className="h-4 w-4 text-emerald-400 shrink-0" />
            <span>{rollbackSuccessMsg}</span>
          </div>
        )}

        {/* TAB 1: PODS LIST */}
        {activeTab === 'pods' && (
          <>
            {/* Quick Stats Bar */}
            <div className="grid grid-cols-3 gap-2 p-3 border-b border-zinc-800 bg-zinc-900/30 text-center text-xs">
              <div className="p-2 bg-zinc-950/60 rounded-lg border border-zinc-800/80">
                <span className="text-zinc-500 block text-[10px]">Total Pods</span>
                <span className="text-base font-bold font-mono text-zinc-100">
                  {pods?.length ?? 0}
                </span>
              </div>
              <div className="p-2 bg-zinc-950/60 rounded-lg border border-zinc-800/80">
                <span className="text-zinc-500 block text-[10px]">Running</span>
                <span className="text-base font-bold font-mono text-emerald-400">
                  {runningCount}
                </span>
              </div>
              <div className="p-2 bg-zinc-950/60 rounded-lg border border-zinc-800/80">
                <span className="text-zinc-500 block text-[10px]">Total Restarts</span>
                <span
                  className={`text-base font-bold font-mono ${
                    totalRestarts > 0 ? 'text-amber-400' : 'text-zinc-300'
                  }`}
                >
                  {totalRestarts}
                </span>
              </div>
            </div>

            {/* Pods List */}
            <div className="flex-1 overflow-y-auto p-4 space-y-3">
              {isPodsLoading ? (
                <div className="p-12 text-center text-zinc-400 space-y-2">
                  <RefreshCw className="h-6 w-6 animate-spin mx-auto text-sky-400" />
                  <p className="text-xs">Fetching active pods from cluster...</p>
                </div>
              ) : !pods || pods.length === 0 ? (
                <div className="p-12 text-center rounded-xl border border-zinc-800 bg-zinc-900/40">
                  <Boxes className="h-10 w-10 text-zinc-600 mx-auto mb-2" />
                  <h4 className="text-sm font-medium text-zinc-300">No active pods</h4>
                  <p className="text-xs text-zinc-500 mt-1">
                    The workload has no running pods or might be scaled down to 0.
                  </p>
                </div>
              ) : (
                pods.map((pod) => {
                  const isRunning = pod.phase === 'Running'
                  return (
                    <div
                      key={pod.name}
                      className="p-3.5 rounded-xl border border-zinc-800/90 bg-zinc-900/60 space-y-2.5 hover:border-zinc-700 transition-all shadow-xs"
                    >
                      {/* Pod Title & Status */}
                      <div className="flex items-center justify-between gap-2">
                        <div className="flex items-center gap-1.5 min-w-0">
                          <span className="text-xs font-mono font-semibold text-zinc-200 truncate">
                            {pod.name}
                          </span>
                          <button
                            type="button"
                            onClick={() => handleCopy(pod.name)}
                            className="text-zinc-500 hover:text-zinc-300 p-0.5 rounded transition-colors cursor-pointer"
                            title="Copy pod name"
                          >
                            {copiedPod === pod.name ? (
                              <Check className="h-3 w-3 text-emerald-400" />
                            ) : (
                              <Copy className="h-3 w-3" />
                            )}
                          </button>
                        </div>

                        <div className="flex items-center gap-1.5 shrink-0">
                          <Badge
                            variant={
                              isRunning
                                ? 'success'
                                : pod.phase === 'Pending'
                                ? 'warning'
                                : 'destructive'
                            }
                            dot
                            className="text-[11px]"
                          >
                            {pod.phase}
                          </Badge>

                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => {
                              setSelectedPodName(pod.name)
                              setActiveTab('logs')
                            }}
                            className="h-6 px-2 text-[11px] text-zinc-400 hover:text-emerald-300 hover:bg-emerald-950/40 gap-1"
                            title="Open logs for this pod"
                          >
                            <Terminal className="h-3 w-3 text-emerald-400" />
                            <span>Logs</span>
                          </Button>
                        </div>
                      </div>

                      {/* Pod Attributes */}
                      <div className="grid grid-cols-2 gap-2 text-[11px] text-zinc-400 pt-1 border-t border-zinc-800/60 font-mono">
                        <div className="flex items-center gap-1 truncate">
                          <Cpu className="h-3 w-3 text-zinc-500 shrink-0" />
                          <span className="truncate">{pod.nodeName || 'Unassigned'}</span>
                        </div>

                        <div className="flex items-center justify-end gap-1">
                          <span className="text-zinc-500">IP:</span>
                          <span>{pod.podIp || 'None'}</span>
                        </div>

                        <div className="flex items-center gap-1">
                          {pod.isReady ? (
                            <CheckCircle2 className="h-3 w-3 text-emerald-400 shrink-0" />
                          ) : (
                            <AlertCircle className="h-3 w-3 text-amber-400 shrink-0" />
                          )}
                          <span>Ready: {pod.isReady ? 'Yes' : 'No'}</span>
                        </div>

                        <div className="flex items-center justify-end gap-1">
                          <span className="text-zinc-500">Restarts:</span>
                          <span className={pod.restartCount > 0 ? 'text-amber-400 font-bold' : ''}>
                            {pod.restartCount}
                          </span>
                        </div>
                      </div>

                      {/* Containers Chips */}
                      {pod.containers && pod.containers.length > 0 && (
                        <div className="flex items-center gap-1.5 flex-wrap pt-1 border-t border-zinc-800/40">
                          <span className="text-[10px] text-zinc-500">Containers:</span>
                          {pod.containers.map((c) => (
                            <button
                              key={c}
                              type="button"
                              onClick={() => {
                                setSelectedPodName(pod.name)
                                setSelectedContainer(c)
                                setActiveTab('logs')
                              }}
                              className="px-1.5 py-0.2 rounded bg-zinc-950 border border-zinc-800 text-[10px] text-zinc-300 font-mono hover:text-sky-300 hover:border-sky-800 cursor-pointer"
                              title={`Inspect logs for container ${c}`}
                            >
                              {c}
                            </button>
                          ))}
                        </div>
                      )}

                      {pod.startTime && (
                        <div className="flex items-center gap-1 text-[10px] text-zinc-500 pt-0.5">
                          <Clock className="h-3 w-3 text-zinc-600" />
                          <span>Started {new Date(pod.startTime).toLocaleString()}</span>
                        </div>
                      )}
                    </div>
                  )
                })
              )}
            </div>
          </>
        )}

        {/* TAB 2: LIVE LOGS */}
        {activeTab === 'logs' && (
          <div className="flex-1 flex flex-col min-h-0 bg-zinc-950">
            {/* Log Toolbar */}
            <div className="p-3 border-b border-zinc-800 bg-zinc-900/50 space-y-2 shrink-0">
              <div className="flex flex-wrap items-center gap-2">
                {/* Pod selector */}
                <div className="flex-1 min-w-[140px]">
                  <Select
                    value={selectedPodName}
                    onChange={(e) => setSelectedPodName(e.target.value)}
                    className="h-8 text-xs font-mono bg-zinc-950"
                  >
                    {pods?.map((p) => (
                      <option key={p.name} value={p.name}>
                        {p.name} ({p.phase})
                      </option>
                    ))}
                  </Select>
                </div>

                {/* Container selector */}
                {currentPod?.containers && currentPod.containers.length > 0 && (
                  <div className="w-36">
                    <Select
                      value={selectedContainer}
                      onChange={(e) => setSelectedContainer(e.target.value)}
                      className="h-8 text-xs font-mono bg-zinc-950"
                    >
                      {currentPod.containers.map((c) => (
                        <option key={c} value={c}>
                          {c}
                        </option>
                      ))}
                    </Select>
                  </div>
                )}

                {/* Tail lines selector */}
                <div className="w-24">
                  <Select
                    value={tailLines.toString()}
                    onChange={(e) => setTailLines(parseInt(e.target.value, 10))}
                    className="h-8 text-xs font-mono bg-zinc-950"
                  >
                    <option value="50">50 lines</option>
                    <option value="100">100 lines</option>
                    <option value="250">250 lines</option>
                    <option value="500">500 lines</option>
                    <option value="1000">1000 lines</option>
                  </Select>
                </div>

                {/* Follow / Auto-scroll toggle */}
                <Button
                  variant={autoScroll ? 'primary' : 'outline'}
                  size="sm"
                  onClick={() => setAutoScroll(!autoScroll)}
                  className={`h-8 text-xs gap-1 ${
                    autoScroll
                      ? 'bg-sky-600 hover:bg-sky-500 text-white'
                      : 'border-zinc-700 text-zinc-300'
                  }`}
                  title="Toggle autoscroll to follow live logs"
                >
                  <ArrowDown className="h-3 w-3" />
                  <span>Follow</span>
                </Button>

                {/* Copy logs */}
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handleCopyLogs}
                  disabled={!logResult?.logs}
                  className="h-8 text-xs border-zinc-700 text-zinc-300 hover:text-white"
                  title="Copy full log output"
                >
                  {copiedLogs ? (
                    <Check className="h-3 w-3 text-emerald-400" />
                  ) : (
                    <Copy className="h-3 w-3" />
                  )}
                </Button>
              </div>

              {/* Search filter in logs */}
              <div className="relative">
                <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-500" />
                <Input
                  placeholder="Filter log output..."
                  value={logSearch}
                  onChange={(e) => setLogSearch(e.target.value)}
                  className="pl-8 text-xs bg-zinc-950 border-zinc-800 h-7"
                />
              </div>
            </div>

            {/* Log Terminal Screen */}
            <div
              ref={logContainerRef}
              className="flex-1 overflow-auto p-3 font-mono text-[11px] leading-relaxed bg-zinc-950 text-zinc-200 select-text"
            >
              {isLogsLoading && !logResult ? (
                <div className="p-12 text-center text-zinc-500 space-y-2">
                  <Loader2 className="h-6 w-6 animate-spin mx-auto text-sky-400" />
                  <p className="text-xs">Streaming logs from {selectedPodName}...</p>
                </div>
              ) : !logResult?.logs ? (
                <div className="p-12 text-center text-zinc-500 space-y-2">
                  <Terminal className="h-8 w-8 text-zinc-700 mx-auto" />
                  <p className="text-xs">No logs found or container has not emitted stdout/stderr.</p>
                </div>
              ) : displayedLogs.length === 0 && logSearch.trim() ? (
                <div className="p-12 text-center text-zinc-500">
                  No log lines match '{logSearch}'.
                </div>
              ) : (
                <pre className="whitespace-pre-wrap break-all font-mono">
                  {displayedLogs}
                </pre>
              )}
            </div>
          </div>
        )}

        {/* TAB 3: ENVIRONMENT VARIABLES & SECRET REFERENCES */}
        {activeTab === 'env' && (
          <div className="flex-1 overflow-y-auto flex flex-col">
            {/* Quick Stats Strip */}
            <div className="grid grid-cols-3 gap-2 p-3 border-b border-zinc-800 bg-zinc-900/30 text-center text-xs shrink-0">
              <div className="p-2 bg-zinc-950/60 rounded-lg border border-zinc-800/80">
                <span className="text-zinc-500 block text-[10px]">Total Variables</span>
                <span className="text-base font-bold font-mono text-zinc-100">
                  {totalEnvCount}
                </span>
              </div>
              <div className="p-2 bg-zinc-950/60 rounded-lg border border-zinc-800/80">
                <span className="text-zinc-500 block text-[10px]">Secret References</span>
                <span className="text-base font-bold font-mono text-purple-400">
                  {secretEnvCount}
                </span>
              </div>
              <div className="p-2 bg-zinc-950/60 rounded-lg border border-zinc-800/80">
                <span className="text-zinc-500 block text-[10px]">Plain Values</span>
                <span className="text-base font-bold font-mono text-sky-400">
                  {literalEnvCount}
                </span>
              </div>
            </div>

            {/* Filter & Search Toolbar */}
            <div className="p-3 border-b border-zinc-800 bg-zinc-900/40 space-y-2 shrink-0">
              <div className="flex items-center gap-2">
                <div className="relative flex-1">
                  <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-500" />
                  <Input
                    placeholder="Filter by key, value, secret name..."
                    value={envSearch}
                    onChange={(e) => setEnvSearch(e.target.value)}
                    className="pl-8 text-xs bg-zinc-950 border-zinc-800 h-8"
                  />
                  {envSearch && (
                    <button
                      type="button"
                      onClick={() => setEnvSearch('')}
                      className="absolute right-2.5 top-1/2 -translate-y-1/2 text-zinc-500 hover:text-zinc-300"
                    >
                      <X className="h-3.5 w-3.5" />
                    </button>
                  )}
                </div>

                {envContainers.length > 1 && (
                  <div className="w-36 shrink-0">
                    <Select
                      value={selectedEnvContainer}
                      onChange={(e) => setSelectedEnvContainer(e.target.value)}
                      className="h-8 text-xs font-mono bg-zinc-950"
                    >
                      <option value="all">All Containers</option>
                      {envContainers.map((c) => (
                        <option key={c} value={c}>
                          {c}
                        </option>
                      ))}
                    </Select>
                  </div>
                )}

                {isOperator && onEditWorkload && (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => {
                      onClose()
                      onEditWorkload(workload)
                    }}
                    className="h-8 text-xs px-2.5 text-purple-300 hover:text-purple-200 border-purple-800/60 bg-purple-950/40 hover:bg-purple-900/50 gap-1.5 shrink-0"
                    title="Edit environment variables in App Editor"
                  >
                    <Pencil className="h-3 w-3" />
                    <span>Edit Env</span>
                  </Button>
                )}
              </div>

              {/* Type Filter Pills */}
              <div className="flex items-center gap-1.5 pt-0.5">
                <button
                  type="button"
                  onClick={() => setEnvTypeFilter('all')}
                  className={`px-2 py-0.5 rounded text-[11px] font-medium transition-colors cursor-pointer ${
                    envTypeFilter === 'all'
                      ? 'bg-zinc-800 text-white shadow-xs'
                      : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900'
                  }`}
                >
                  All ({totalEnvCount})
                </button>
                <button
                  type="button"
                  onClick={() => setEnvTypeFilter('secret')}
                  className={`px-2 py-0.5 rounded text-[11px] font-medium transition-colors flex items-center gap-1 cursor-pointer ${
                    envTypeFilter === 'secret'
                      ? 'bg-purple-950 text-purple-200 border border-purple-800/80 shadow-xs'
                      : 'text-zinc-400 hover:text-purple-300 hover:bg-zinc-900'
                  }`}
                >
                  <Lock className="h-3 w-3 text-purple-400" />
                  Secrets ({secretEnvCount})
                </button>
                <button
                  type="button"
                  onClick={() => setEnvTypeFilter('literal')}
                  className={`px-2 py-0.5 rounded text-[11px] font-medium transition-colors cursor-pointer ${
                    envTypeFilter === 'literal'
                      ? 'bg-sky-950 text-sky-200 border border-sky-800/80 shadow-xs'
                      : 'text-zinc-400 hover:text-sky-300 hover:bg-zinc-900'
                  }`}
                >
                  Plain Values ({literalEnvCount})
                </button>
                {configMapEnvCount > 0 && (
                  <button
                    type="button"
                    onClick={() => setEnvTypeFilter('configMap')}
                    className={`px-2 py-0.5 rounded text-[11px] font-medium transition-colors flex items-center gap-1 cursor-pointer ${
                      envTypeFilter === 'configMap'
                        ? 'bg-amber-950 text-amber-200 border border-amber-800/80 shadow-xs'
                        : 'text-zinc-400 hover:text-amber-300 hover:bg-zinc-900'
                    }`}
                  >
                    <Sliders className="h-3 w-3 text-amber-400" />
                    ConfigMaps ({configMapEnvCount})
                  </button>
                )}
              </div>
            </div>

            {/* Content List */}
            <div className="flex-1 overflow-y-auto p-4 space-y-3">
              {isBundleLoading && !appBundle ? (
                <div className="p-12 text-center text-zinc-500 space-y-2">
                  <Loader2 className="h-6 w-6 animate-spin mx-auto text-sky-400" />
                  <p className="text-xs">Loading environment variables...</p>
                </div>
              ) : filteredEnvVars.length === 0 && envFromSources.length === 0 ? (
                <div className="p-12 text-center text-zinc-500 space-y-3 bg-zinc-900/30 rounded-xl border border-zinc-800/80">
                  <Key className="h-8 w-8 text-zinc-700 mx-auto" />
                  <div className="space-y-1">
                    <p className="text-xs font-semibold text-zinc-300">
                      {envSearch ? 'No variables match search criteria' : 'No environment variables set'}
                    </p>
                    <p className="text-[11px] text-zinc-500">
                      {envSearch
                        ? 'Try clearing the search query or changing filters.'
                        : 'This workload does not have any container environment variables defined.'}
                    </p>
                  </div>
                  {isOperator && onEditWorkload && (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => {
                        onClose()
                        onEditWorkload(workload)
                      }}
                      className="text-xs border-zinc-700 text-zinc-300 hover:text-white mt-2"
                    >
                      <Plus className="h-3.5 w-3.5 mr-1" /> Add Environment Variables
                    </Button>
                  )}
                </div>
              ) : (
                <>
                  {/* envFrom Sources Banner (if any) */}
                  {envFromSources.length > 0 && (
                    <div className="p-3 rounded-xl bg-purple-950/20 border border-purple-800/40 space-y-2">
                      <div className="flex items-center justify-between text-xs">
                        <div className="flex items-center gap-1.5 font-bold text-purple-300 text-[11px] uppercase tracking-wider">
                          <Lock className="h-3.5 w-3.5 text-purple-400" />
                          <span>Mounted Environment Sources (envFrom)</span>
                        </div>
                        <span className="text-[10px] text-purple-400/80 font-mono">
                          {envFromSources.length} source{envFromSources.length === 1 ? '' : 's'}
                        </span>
                      </div>
                      <p className="text-[11px] text-zinc-400 leading-normal">
                        All key-value pairs from these resources are automatically injected into the container environment.
                      </p>
                      <div className="space-y-1.5 pt-1">
                        {envFromSources.map((ef, idx) => (
                          <div
                            key={idx}
                            className="flex items-center justify-between p-2 rounded-lg bg-zinc-950/70 border border-zinc-800/80 text-xs"
                          >
                            <div className="flex items-center gap-2">
                              {ef.secretRef ? (
                                <Badge variant="purple" className="text-[10px] px-1.5 py-0.5">
                                  Secret
                                </Badge>
                              ) : (
                                <Badge variant="warning" className="text-[10px] px-1.5 py-0.5">
                                  ConfigMap
                                </Badge>
                              )}
                              <span className="font-mono font-semibold text-zinc-100">
                                {ef.secretRef || ef.configMapRef}
                              </span>
                              {ef.prefix && (
                                <span className="text-[10px] text-zinc-400 font-mono bg-zinc-900 px-1 py-0.2 rounded border border-zinc-800">
                                  prefix: {ef.prefix}
                                </span>
                              )}
                            </div>
                            {ef.containerName && (
                              <span className="text-[10px] text-zinc-500 font-mono">
                                container: {ef.containerName}
                              </span>
                            )}
                          </div>
                        ))}
                      </div>
                    </div>
                  )}

                  {/* Individual Environment Variables */}
                  <div className="space-y-2">
                    {filteredEnvVars.map((ev, idx) => {
                      const isSec = ev.isSecret || Boolean(ev.secretName)
                      const isCm = Boolean(ev.configMapName)
                      const varId = `${ev.containerName || 'default'}-${ev.key}-${idx}`
                      const isRevealed = revealedValues[varId] ?? false
                      const isSensitive =
                        /password|secret|token|key|credential|auth|pass/i.test(ev.key)

                      return (
                        <div
                          key={varId}
                          className={`p-3 rounded-xl border transition-all ${
                            isSec
                              ? 'bg-zinc-900/60 border-purple-800/40 hover:border-purple-700/60'
                              : isCm
                              ? 'bg-zinc-900/60 border-amber-800/40 hover:border-amber-700/60'
                              : 'bg-zinc-900/50 border-zinc-800/80 hover:border-zinc-700'
                          }`}
                        >
                          {/* Top Row: Variable Name, Type Badge, Container Badge, Copy Key */}
                          <div className="flex items-center justify-between gap-2">
                            <div className="flex items-center gap-2 min-w-0 flex-wrap">
                              <span className="font-mono font-bold text-xs text-zinc-100">
                                {ev.key}
                              </span>

                              {isSec ? (
                                <span className="inline-flex items-center gap-1 text-[10px] font-mono px-1.5 py-0.2 rounded bg-purple-950/80 text-purple-300 border border-purple-800/80">
                                  <Lock className="h-2.5 w-2.5 text-purple-400" />
                                  Secret Ref
                                </span>
                              ) : isCm ? (
                                <span className="inline-flex items-center gap-1 text-[10px] font-mono px-1.5 py-0.2 rounded bg-amber-950/80 text-amber-300 border border-amber-800/80">
                                  <Sliders className="h-2.5 w-2.5 text-amber-400" />
                                  ConfigMap Ref
                                </span>
                              ) : (
                                <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-zinc-800 text-zinc-400 border border-zinc-700/60">
                                  Value
                                </span>
                              )}

                              {ev.containerName && envContainers.length > 1 && (
                                <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-zinc-950 text-zinc-500 border border-zinc-800">
                                  {ev.containerName}
                                </span>
                              )}
                            </div>

                            <button
                              type="button"
                              onClick={() => handleCopyEnv(ev.key, `key-${varId}`)}
                              className="text-zinc-500 hover:text-zinc-300 p-1 rounded hover:bg-zinc-800 transition-colors shrink-0 cursor-pointer"
                              title="Copy variable name"
                            >
                              {copiedEnvKey === `key-${varId}` ? (
                                <Check className="h-3 w-3 text-emerald-400" />
                              ) : (
                                <Copy className="h-3 w-3" />
                              )}
                            </button>
                          </div>

                          {/* Value / Reference Content */}
                          <div className="mt-2 pt-2 border-t border-zinc-800/60 text-xs font-mono">
                            {isSec ? (
                              <div className="flex items-center justify-between gap-2 p-2 rounded-lg bg-zinc-950/80 border border-zinc-800/80">
                                <div className="flex items-center gap-1.5 min-w-0 flex-wrap">
                                  <span className="text-zinc-500 text-[11px]">secretKeyRef:</span>
                                  <span className="text-purple-300 font-semibold truncate">
                                    {ev.secretName || '(unspecified)'}
                                  </span>
                                  <span className="text-zinc-600">&rarr;</span>
                                  <span className="text-purple-200">
                                    {ev.secretKey || '(key)'}
                                  </span>
                                </div>
                                <button
                                  type="button"
                                  onClick={() =>
                                    handleCopyEnv(
                                      `${ev.secretName || ''}:${ev.secretKey || ''}`,
                                      `ref-${varId}`
                                    )
                                  }
                                  className="text-zinc-500 hover:text-purple-300 p-1 rounded hover:bg-zinc-800 shrink-0 cursor-pointer"
                                  title="Copy secret reference"
                                >
                                  {copiedEnvKey === `ref-${varId}` ? (
                                    <Check className="h-3 w-3 text-emerald-400" />
                                  ) : (
                                    <Copy className="h-3 w-3" />
                                  )}
                                </button>
                              </div>
                            ) : isCm ? (
                              <div className="flex items-center justify-between gap-2 p-2 rounded-lg bg-zinc-950/80 border border-zinc-800/80">
                                <div className="flex items-center gap-1.5 min-w-0 flex-wrap">
                                  <span className="text-zinc-500 text-[11px]">configMapKeyRef:</span>
                                  <span className="text-amber-300 font-semibold truncate">
                                    {ev.configMapName}
                                  </span>
                                  <span className="text-zinc-600">&rarr;</span>
                                  <span className="text-amber-200">
                                    {ev.configMapKey || '(key)'}
                                  </span>
                                </div>
                                <button
                                  type="button"
                                  onClick={() =>
                                    handleCopyEnv(
                                      `${ev.configMapName}:${ev.configMapKey || ''}`,
                                      `ref-${varId}`
                                    )
                                  }
                                  className="text-zinc-500 hover:text-amber-300 p-1 rounded hover:bg-zinc-800 shrink-0 cursor-pointer"
                                  title="Copy ConfigMap reference"
                                >
                                  {copiedEnvKey === `ref-${varId}` ? (
                                    <Check className="h-3 w-3 text-emerald-400" />
                                  ) : (
                                    <Copy className="h-3 w-3" />
                                  )}
                                </button>
                              </div>
                            ) : (
                              <div className="flex items-center justify-between gap-2 p-2 rounded-lg bg-zinc-950/80 border border-zinc-800/80">
                                <div className="min-w-0 flex-1 truncate text-zinc-300 select-text">
                                  {isSensitive && !isRevealed
                                    ? '••••••••••••••••'
                                    : ev.value || <span className="text-zinc-600 italic">(empty)</span>}
                                </div>
                                <div className="flex items-center gap-1 shrink-0">
                                  {isSensitive && (
                                    <button
                                      type="button"
                                      onClick={() => toggleRevealValue(varId)}
                                      className="text-zinc-500 hover:text-zinc-300 p-1 rounded hover:bg-zinc-800 transition-colors cursor-pointer"
                                      title={isRevealed ? 'Mask value' : 'Reveal value'}
                                    >
                                      {isRevealed ? (
                                        <EyeOff className="h-3 w-3" />
                                      ) : (
                                        <Eye className="h-3 w-3" />
                                      )}
                                    </button>
                                  )}
                                  <button
                                    type="button"
                                    onClick={() => handleCopyEnv(ev.value, `val-${varId}`)}
                                    className="text-zinc-500 hover:text-zinc-300 p-1 rounded hover:bg-zinc-800 transition-colors cursor-pointer"
                                    title="Copy value"
                                  >
                                    {copiedEnvKey === `val-${varId}` ? (
                                      <Check className="h-3 w-3 text-emerald-400" />
                                    ) : (
                                      <Copy className="h-3 w-3" />
                                    )}
                                  </button>
                                </div>
                              </div>
                            )}
                          </div>
                        </div>
                      )
                    })}
                  </div>
                </>
              )}
            </div>
          </div>
        )}

        {/* TAB 4: ROLLOUT REVISIONS & ROLLBACK */}
        {activeTab === 'revisions' && (
          <div className="flex-1 overflow-y-auto p-4 space-y-3">
            <div className="flex items-center justify-between pb-2 border-b border-zinc-800 text-xs">
              <span className="text-zinc-400 font-semibold uppercase tracking-wider text-[11px] flex items-center gap-1.5">
                <History className="h-3.5 w-3.5 text-purple-400" />
                Deployment Revision History
              </span>
              <span className="text-zinc-500 font-mono">
                {revisions?.length ?? 0} revisions tracked
              </span>
            </div>

            {isRevisionsLoading ? (
              <div className="p-12 text-center text-zinc-400 space-y-2">
                <RefreshCw className="h-6 w-6 animate-spin mx-auto text-purple-400" />
                <p className="text-xs">Fetching ReplicaSets & revision history...</p>
              </div>
            ) : !revisions || revisions.length === 0 ? (
              <div className="p-12 text-center rounded-xl border border-zinc-800 bg-zinc-900/40 space-y-2">
                <Layers className="h-10 w-10 text-zinc-600 mx-auto" />
                <p className="text-sm font-semibold text-zinc-300">No revisions found</p>
                <p className="text-xs text-zinc-500">
                  Only Deployments with ReplicaSet history track rollback revisions.
                </p>
              </div>
            ) : (
              revisions.map((rev) => {
                return (
                  <div
                    key={rev.revision}
                    className={`p-3.5 rounded-xl border transition-all ${
                      rev.isCurrent
                        ? 'border-purple-600/70 bg-purple-950/20 shadow-xs'
                        : 'border-zinc-800 bg-zinc-900/60 hover:border-zinc-700'
                    }`}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <div className="flex items-center gap-2">
                        <span className="font-mono font-bold text-xs text-zinc-100">
                          Revision #{rev.revision}
                        </span>
                        {rev.isCurrent && (
                          <Badge variant="info" className="text-[10px] bg-purple-950 text-purple-300 border-purple-800">
                            Current Active
                          </Badge>
                        )}
                      </div>

                      {!rev.isCurrent && isOperator && (
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => setConfirmRollbackRev(rev)}
                          disabled={rollbackMutation.isPending}
                          className="h-7 px-2.5 text-xs border-amber-800/80 text-amber-300 hover:bg-amber-950/60 gap-1"
                        >
                          <RotateCcw className="h-3 w-3" />
                          <span>Rollback to #{rev.revision}</span>
                        </Button>
                      )}
                    </div>

                    <div className="mt-2 text-xs text-zinc-400 space-y-1 font-mono">
                      {rev.images && rev.images.length > 0 && (
                        <div className="flex items-center gap-1.5 flex-wrap">
                          <span className="text-zinc-500 text-[10px]">Images:</span>
                          {rev.images.map((img) => (
                            <span
                              key={img}
                              className="px-1.5 py-0.2 rounded bg-zinc-950 border border-zinc-800 text-[10px] text-zinc-300 truncate max-w-xs"
                              title={img}
                            >
                              {img.split('/').pop() || img}
                            </span>
                          ))}
                        </div>
                      )}

                      <div className="flex items-center justify-between text-[11px] text-zinc-500 pt-1 font-sans">
                        <span>Replicas: {rev.readyReplicas} / {rev.replicas}</span>
                        <span>
                          {rev.creationTimestamp
                            ? new Date(rev.creationTimestamp).toLocaleString()
                            : 'Unknown time'}
                        </span>
                      </div>
                    </div>
                  </div>
                )
              })
            )}
          </div>
        )}

        {/* ROLLBACK CONFIRMATION MODAL GUARD */}
        {confirmRollbackRev && (
          <div className="fixed inset-0 z-60 flex items-center justify-center p-4 bg-black/80 backdrop-blur-xs animate-in fade-in">
            <div className="w-full max-w-md bg-zinc-900 border border-zinc-800 rounded-xl p-5 shadow-2xl space-y-4">
              <div className="flex items-center gap-2 text-amber-400 font-semibold text-sm">
                <AlertTriangle className="h-5 w-5" />
                <span>Confirm Deployment Rollback</span>
              </div>

              <div className="space-y-2 text-xs text-zinc-300 leading-relaxed">
                <p>
                  Are you sure you want to roll back deployment{' '}
                  <strong className="text-zinc-100 font-mono">{workload.name}</strong> to{' '}
                  <strong className="text-amber-300 font-mono">Revision #{confirmRollbackRev.revision}</strong>?
                </p>
                <div className="p-3 bg-amber-950/30 border border-amber-800/40 rounded-lg space-y-1 text-amber-200/90 text-[11px]">
                  <div>Target Container Images:</div>
                  <div className="font-mono text-zinc-200 truncate">
                    {confirmRollbackRev.images?.join(', ') || 'Template configuration'}
                  </div>
                </div>
              </div>

              <div className="flex items-center justify-end gap-2 pt-2 border-t border-zinc-800">
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => setConfirmRollbackRev(null)}
                  disabled={rollbackMutation.isPending}
                >
                  Cancel
                </Button>
                <Button
                  variant="primary"
                  size="sm"
                  onClick={handleConfirmRollback}
                  disabled={rollbackMutation.isPending}
                  className="bg-amber-500 hover:bg-amber-400 text-zinc-950 font-bold gap-1.5"
                >
                  {rollbackMutation.isPending ? (
                    <Loader2 className="h-4 w-4 animate-spin" />
                  ) : (
                    <RotateCcw className="h-4 w-4" />
                  )}
                  <span>Confirm Rollback</span>
                </Button>
              </div>
            </div>
          </div>
        )}
      </div>
    </>
  )
}
