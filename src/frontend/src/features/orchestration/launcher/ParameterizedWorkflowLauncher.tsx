import { useState } from 'react'
import {
  Sparkles,
  Play,
  AlertTriangle,
  Loader2,
  X,
  ShieldCheck,
  ShieldAlert,
  Layers,
  Camera,
  Activity,
  RotateCcw,
  Zap,
} from 'lucide-react'
import { HostSelectorSection } from './sections/HostSelectorSection'
import { LiveDagPreview } from './LiveDagPreview'
import { startTemporalWorkflow } from '../../../api/temporal'
import { createJob } from '../../../api/jobs'
import { useActiveJobsByHost } from '../useJobs'
import type { Host } from '../../../api/hosts'
import { Button } from '../../../components/ui/button'
import { useQueryClient } from '@tanstack/react-query'

export interface ParameterizedWorkflowLauncherProps {
  isOpen: boolean
  onClose: () => void
  initialHost?: Host | null
  availableHosts?: Host[]
  onWorkflowLaunched: (jobId: string, host: Host, workflowId?: string) => void
}

export function ParameterizedWorkflowLauncher({
  isOpen,
  onClose,
  initialHost = null,
  availableHosts = [],
  onWorkflowLaunched,
}: ParameterizedWorkflowLauncherProps) {
  const queryClient = useQueryClient()

  // Workflow Type: Full OS Upgrade vs Safe Reboot
  const [workflowMode, setWorkflowMode] = useState<'upgrade' | 'reboot'>('upgrade')

  // Selected Host
  const [selectedHostId, setSelectedHostId] = useState<string>(() => initialHost?.id || '')

  const effectiveHost = initialHost || availableHosts.find((h) => h.id === selectedHostId) || null

  const isProxmoxCapable = Boolean(
    effectiveHost?.targetType?.toLowerCase().includes('proxmox') ||
    effectiveHost?.proxmox != null
  )
  const isK8sCapable = Boolean(
    effectiveHost?.targetType?.toLowerCase().includes('k8s') ||
    effectiveHost?.targetType?.toLowerCase().includes('kubernetes')
  )

  // 2 Main Options (both OFF by default)
  const [requireApprovalBeforeReboot, setRequireApprovalBeforeReboot] = useState(false)
  const [enableK8sDrain, setEnableK8sDrain] = useState(false)

  // Automated parameters derived from host
  const enableSnapshot = isProxmoxCapable
  const snapshotName = effectiveHost ? `pre-upgrade-${effectiveHost.hostname}` : ''
  const k8sNodeName = effectiveHost?.hostname || ''
  const alwaysReboot = false
  const probeUrls = effectiveHost?.ipAddress ? [`tcp://${effectiveHost.ipAddress}:22`] : []

  const activeJobsByHost = useActiveJobsByHost()
  const activeJobForTarget = effectiveHost ? activeJobsByHost.get(effectiveHost.id) : null

  // Submission state
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

  const handleSelectHost = (hostId: string) => {
    setSelectedHostId(hostId)
  }

  if (!isOpen) return null

  const handleLaunch = async () => {
    if (!effectiveHost || activeJobForTarget) return

    setIsSubmitting(true)
    setErrorMsg(null)

    try {
      let launchedJobId = ''
      let launchedWorkflowId = ''

      if (workflowMode === 'reboot') {
        const rebootPipeline = isK8sCapable ? 'k8s-node-safe-reboot' : 'safe-reboot-verify'
        const legacyJob = await createJob(effectiveHost.id, rebootPipeline)
        launchedJobId = legacyJob.id
      } else {
        // 1. Try launching via Temporal durable workflow
        try {
          const res = await startTemporalWorkflow({
            hostId: effectiveHost.id,
            requireApprovalBeforeReboot,
            alwaysReboot,
            probeUrls: probeUrls.length > 0 ? probeUrls : undefined,
            snapshotName: enableSnapshot ? snapshotName.trim() || undefined : undefined,
            k8sNodeName: enableK8sDrain ? k8sNodeName.trim() || effectiveHost.hostname : undefined,
            initiatedBy: 'Operator',
          })
          launchedJobId = res.jobId
          launchedWorkflowId = res.workflowId
        } catch (err: unknown) {
          // If Temporal service is not available (e.g. 503 or offline), fallback to legacy job API
          const status = err && typeof err === 'object' && 'status' in err ? (err as { status: number }).status : 0
          if (status === 503 || status === 404) {
            const legacyJob = await createJob(effectiveHost.id, 'standard-os-upgrade')
            launchedJobId = legacyJob.id
          } else {
            throw err
          }
        }
      }

      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      queryClient.invalidateQueries({ queryKey: ['temporalWorkflow'] })

      onWorkflowLaunched(launchedJobId, effectiveHost, launchedWorkflowId)
      onClose()
    } catch (err: unknown) {
      const msg =
        err && typeof err === 'object' && 'message' in err
          ? (err as { message: string }).message
          : 'Failed to launch workflow.'
      setErrorMsg(msg)
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-5 md:p-6 bg-black/80 backdrop-blur-md animate-in fade-in duration-200">
      <div className="relative w-full max-w-[1500px] h-[92vh] bg-zinc-950 border border-zinc-800 rounded-2xl shadow-2xl flex flex-col overflow-hidden">
        {/* Header */}
        <div className="px-6 py-4 bg-zinc-900/90 border-b border-zinc-800 flex items-center justify-between gap-4 shrink-0">
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400">
              <Sparkles className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-base font-bold text-zinc-100 flex items-center gap-2">
                <span>Parameterized Workflow Launcher</span>
                <span className="px-2 py-0.5 rounded text-[10px] font-mono bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                  Temporal Engine
                </span>
              </h3>
              <p className="text-xs text-zinc-400 mt-0.5">
                Configure safety policies, reboot approvals, and health probes with real-time DAG graph verification.
              </p>
            </div>
          </div>

          <button
            type="button"
            onClick={onClose}
            className="p-1.5 rounded-lg text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content Body: Split Screen */}
        <div className="flex-1 min-h-0 grid grid-cols-1 lg:grid-cols-12 overflow-hidden">
          {/* Left Column: Configuration Controls (Scrollable) */}
          <div className="lg:col-span-6 p-6 space-y-5 overflow-y-auto border-b lg:border-b-0 lg:border-r border-zinc-800">
            {errorMsg && (
              <div className="p-3 rounded-lg bg-rose-500/10 border border-rose-500/20 text-xs text-rose-300 flex items-center gap-2">
                <AlertTriangle className="w-4 h-4 shrink-0 text-rose-400" />
                <span>{errorMsg}</span>
              </div>
            )}

            {activeJobForTarget && (
              <div className="p-3.5 rounded-xl bg-amber-950/40 border border-amber-500/40 text-xs text-amber-200 flex items-start gap-3">
                <AlertTriangle className="w-4 h-4 text-amber-400 shrink-0 mt-0.5 animate-pulse" />
                <div className="space-y-0.5">
                  <div className="font-semibold text-amber-300">
                    Active DAG Workflow in Progress
                  </div>
                  <p className="text-[11px] text-amber-200/80 leading-relaxed">
                    Host <strong className="text-zinc-100">{effectiveHost?.hostname}</strong> is already executing an update pipeline ({activeJobForTarget.activeStep || activeJobForTarget.status}). Starting a second concurrent update is blocked to prevent lock collisions and system conflicts.
                  </p>
                </div>
              </div>
            )}

            {/* Workflow Mode Selector */}
            <div className="p-3 rounded-xl bg-zinc-950/80 border border-zinc-800 space-y-2">
              <span className="text-xs font-semibold text-zinc-300">Workflow Pipeline Type</span>
              <div className="grid grid-cols-2 gap-2">
                <button
                  type="button"
                  onClick={() => setWorkflowMode('upgrade')}
                  className={`flex items-center gap-2.5 p-2.5 rounded-lg border text-left transition-colors ${
                    workflowMode === 'upgrade'
                      ? 'border-emerald-500/50 bg-emerald-500/10 text-emerald-300'
                      : 'border-zinc-800 bg-zinc-900/40 text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/40'
                  }`}
                >
                  <Sparkles className="w-4 h-4 shrink-0 text-emerald-400" />
                  <div>
                    <div className="text-xs font-medium text-zinc-200">Full OS Upgrade</div>
                    <div className="text-[10px] text-zinc-400">Packages, snapshots, reboot</div>
                  </div>
                </button>
                <button
                  type="button"
                  onClick={() => setWorkflowMode('reboot')}
                  className={`flex items-center gap-2.5 p-2.5 rounded-lg border text-left transition-colors ${
                    workflowMode === 'reboot'
                      ? 'border-amber-500/50 bg-amber-500/10 text-amber-300'
                      : 'border-zinc-800 bg-zinc-900/40 text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/40'
                  }`}
                >
                  <RotateCcw className="w-4 h-4 shrink-0 text-amber-400" />
                  <div>
                    <div className="text-xs font-medium text-zinc-200">Safe Reboot</div>
                    <div className="text-[10px] text-zinc-400">Preflight, reboot, reconnect, health</div>
                  </div>
                </button>
              </div>
            </div>

            {/* Target Host Selector */}
            <HostSelectorSection
              availableHosts={availableHosts}
              selectedHostId={selectedHostId}
              onSelectHost={handleSelectHost}
              disabled={Boolean(initialHost)}
              initialHost={initialHost}
              activeJobsByHost={activeJobsByHost}
            />

            {/* Streamlined Controls: 2 Main Options (both default OFF) */}
            <div className="space-y-3.5">
              <div className="flex items-center justify-between">
                <span className="text-xs font-semibold text-zinc-300 uppercase tracking-wider">
                  Workflow Execution Controls
                </span>
                <span className="text-[11px] text-zinc-500 font-mono">2 Configurable Gates</span>
              </div>

              {/* 1. Operator Approval Gate */}
              <div className="p-4 rounded-xl bg-zinc-950/80 border border-zinc-800 space-y-2">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2.5">
                    <div className="p-2 rounded-lg bg-amber-500/10 text-amber-400 border border-amber-500/20">
                      <ShieldAlert className="w-4 h-4" />
                    </div>
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-semibold text-zinc-100">
                          Operator Approval Gate
                        </span>
                        <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-amber-500/20 text-amber-300">
                          Human-in-the-Loop
                        </span>
                      </div>
                      <p className="text-[11px] text-zinc-400 mt-0.5 leading-relaxed">
                        Pause workflow after packages install and await 1-click confirmation before executing host reboot.
                      </p>
                    </div>
                  </div>

                  <label className="relative inline-flex items-center cursor-pointer shrink-0 ml-4">
                    <input
                      type="checkbox"
                      checked={requireApprovalBeforeReboot}
                      onChange={(e) => setRequireApprovalBeforeReboot(e.target.checked)}
                      className="sr-only peer"
                    />
                    <div className="w-10 h-5 bg-zinc-800 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-amber-600"></div>
                  </label>
                </div>
              </div>

              {/* 2. Kubernetes Coordination */}
              <div className="p-4 rounded-xl bg-zinc-950/80 border border-zinc-800 space-y-2">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2.5">
                    <div className="p-2 rounded-lg bg-indigo-500/10 text-indigo-400 border border-indigo-500/20">
                      <Layers className="w-4 h-4" />
                    </div>
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-semibold text-zinc-100">
                          Kubernetes Coordination (Cordon & Drain)
                        </span>
                        {isK8sCapable && (
                          <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-indigo-500/20 text-indigo-300">
                            Cluster Node
                          </span>
                        )}
                      </div>
                      <p className="text-[11px] text-zinc-400 mt-0.5 leading-relaxed">
                        Cordon node and gracefully evict pods via Eviction API before restart; restore scheduling on completion.
                      </p>
                    </div>
                  </div>

                  <label className="relative inline-flex items-center cursor-pointer shrink-0 ml-4">
                    <input
                      type="checkbox"
                      checked={enableK8sDrain}
                      onChange={(e) => setEnableK8sDrain(e.target.checked)}
                      className="sr-only peer"
                    />
                    <div className="w-10 h-5 bg-zinc-800 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-indigo-600"></div>
                  </label>
                </div>
              </div>

              {/* Automated Policies Info Card */}
              <div className="p-3.5 rounded-xl bg-zinc-900/40 border border-zinc-800/60 space-y-2">
                <span className="text-[11px] font-semibold text-zinc-400">
                  Automated Pipeline Defaults
                </span>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-[11px] text-zinc-400">
                  <div className="flex items-center gap-2">
                    <Camera className="w-3.5 h-3.5 text-purple-400 shrink-0" />
                    <span>Proxmox snapshot {isProxmoxCapable ? 'auto-enabled' : 'bypassed (not VM)'}</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <Activity className="w-3.5 h-3.5 text-emerald-400 shrink-0" />
                    <span>Synthetic health probes auto-configured</span>
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* Right Column: Live DAG Preview & Launch Trigger */}
          <div className="lg:col-span-6 p-6 flex flex-col h-full bg-zinc-950/60 overflow-hidden">
            {/* Live Canvas Preview */}
            <div className="flex-1 min-h-0 flex flex-col h-full">
              <LiveDagPreview
                enableSnapshot={enableSnapshot}
                enableK8sDrain={enableK8sDrain}
                requireApprovalBeforeReboot={requireApprovalBeforeReboot}
                probeCount={probeUrls.length}
                workflowMode={workflowMode}
                height="100%"
                className="h-full"
              />
            </div>

            {/* Pre-launch Summary & Confirmation Card */}
            <div className="mt-4 p-4 rounded-xl bg-zinc-900/80 border border-zinc-800 space-y-3 shrink-0">
              <div className="flex items-center justify-between text-xs">
                <span className="text-zinc-400 flex items-center gap-1.5">
                  <ShieldCheck className="w-4 h-4 text-emerald-400" />
                  Target Node: <strong className="text-zinc-100 font-semibold">{effectiveHost?.hostname || 'No host selected'}</strong>
                </span>
                <span className="text-zinc-400 flex items-center gap-1">
                  <Zap className="w-3.5 h-3.5 text-amber-400" />
                  Approval Gate: <strong className="text-zinc-100">{requireApprovalBeforeReboot ? 'Enabled' : 'Bypassed'}</strong>
                </span>
              </div>

              <div className="flex items-center justify-end gap-3 pt-2 border-t border-zinc-800">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={onClose}
                  className="text-xs h-9 border-zinc-700 bg-zinc-900 text-zinc-300 hover:text-zinc-100"
                >
                  Cancel
                </Button>
                <Button
                  variant="primary"
                  size="sm"
                  onClick={handleLaunch}
                  disabled={!effectiveHost || isSubmitting || Boolean(activeJobForTarget)}
                  className={`text-xs h-9 px-5 font-semibold shadow-lg disabled:opacity-50 gap-2 cursor-pointer ${
                    activeJobForTarget
                      ? 'bg-amber-900/60 border border-amber-600/60 text-amber-300 shadow-amber-950/50 cursor-not-allowed'
                      : 'bg-emerald-600 hover:bg-emerald-500 text-white shadow-emerald-950/50'
                  }`}
                >
                  {isSubmitting ? (
                    <>
                      <Loader2 className="w-4 h-4 animate-spin" />
                      Dispatched to Temporal...
                    </>
                  ) : activeJobForTarget ? (
                    <>
                      <AlertTriangle className="w-4 h-4 text-amber-400" />
                      DAG In Progress ({activeJobForTarget.activeStep || activeJobForTarget.status})
                    </>
                  ) : (
                    <>
                      <Play className="w-4 h-4 fill-current" />
                      Execute Workflow DAG
                    </>
                  )}
                </Button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
