import { useState } from 'react'
import {
  Sparkles,
  Play,
  AlertTriangle,
  Loader2,
  X,
  ShieldCheck,
  Zap,
} from 'lucide-react'
import { HostSelectorSection } from './sections/HostSelectorSection'
import { SafetyPolicySections } from './sections/SafetyPolicySections'
import { HealthProbesSection } from './sections/HealthProbesSection'
import { LiveDagPreview } from './LiveDagPreview'
import { startTemporalWorkflow } from '../../../api/temporal'
import { createJob } from '../../../api/jobs'
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

  // Safety & Rollback Parameters
  const [enableSnapshot, setEnableSnapshot] = useState(() => Boolean(
    initialHost?.targetType?.toLowerCase().includes('proxmox') ||
    initialHost?.proxmox != null
  ))
  const [snapshotName, setSnapshotName] = useState(() => initialHost ? `pre-upgrade-${initialHost.hostname}` : '')

  // Kubernetes Parameters
  const [enableK8sDrain, setEnableK8sDrain] = useState(() => Boolean(
    initialHost?.targetType?.toLowerCase().includes('k8s') ||
    initialHost?.targetType?.toLowerCase().includes('kubernetes')
  ))
  const [k8sNodeName, setK8sNodeName] = useState(() => initialHost?.hostname || '')

  // Reboot & Approval Parameters
  const [requireApprovalBeforeReboot, setRequireApprovalBeforeReboot] = useState(true)
  const [alwaysReboot, setAlwaysReboot] = useState(false)

  // Health Probes
  const [probeUrls, setProbeUrls] = useState<string[]>(() => initialHost?.ipAddress ? [`tcp://${initialHost.ipAddress}:22`] : [])

  // Submission state
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

  const handleSelectHost = (hostId: string) => {
    setSelectedHostId(hostId)
    const host = availableHosts.find((h) => h.id === hostId)
    if (host) {
      const isPve = Boolean(
        host.targetType?.toLowerCase().includes('proxmox') ||
        host.proxmox != null
      )
      const isK8s = Boolean(
        host.targetType?.toLowerCase().includes('k8s') ||
        host.targetType?.toLowerCase().includes('kubernetes')
      )
      setEnableSnapshot(isPve)
      setSnapshotName(isPve ? `pre-upgrade-${host.hostname}` : '')
      setEnableK8sDrain(isK8s)
      setK8sNodeName(isK8s ? host.hostname : '')
      if (host.ipAddress) {
        setProbeUrls([`tcp://${host.ipAddress}:22`])
      }
    }
  }

  if (!isOpen) return null

  const handleAddProbe = (url: string) => {
    if (!probeUrls.includes(url)) {
      setProbeUrls([...probeUrls, url])
    }
  }

  const handleRemoveProbe = (index: number) => {
    setProbeUrls(probeUrls.filter((_, i) => i !== index))
  }

  const handleLaunch = async () => {
    if (!effectiveHost) return

    setIsSubmitting(true)
    setErrorMsg(null)

    try {
      // 1. Try launching via Temporal durable workflow
      let launchedJobId = ''
      let launchedWorkflowId = ''

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

            {/* Target Host Selector */}
            <HostSelectorSection
              availableHosts={availableHosts}
              selectedHostId={selectedHostId}
              onSelectHost={handleSelectHost}
              disabled={Boolean(initialHost)}
              initialHost={initialHost}
            />

            {/* Safety & Rollback Policies */}
            <SafetyPolicySections
              enableSnapshot={enableSnapshot}
              onToggleSnapshot={setEnableSnapshot}
              snapshotName={snapshotName}
              onChangeSnapshotName={setSnapshotName}
              isProxmoxCapable={isProxmoxCapable}
              enableK8sDrain={enableK8sDrain}
              onToggleK8sDrain={setEnableK8sDrain}
              k8sNodeName={k8sNodeName}
              onChangeK8sNodeName={setK8sNodeName}
              isK8sCapable={isK8sCapable}
              requireApprovalBeforeReboot={requireApprovalBeforeReboot}
              onToggleApproval={setRequireApprovalBeforeReboot}
              alwaysReboot={alwaysReboot}
              onToggleAlwaysReboot={setAlwaysReboot}
            />

            {/* Synthetic Health Probes */}
            <HealthProbesSection
              probeUrls={probeUrls}
              onAddProbe={handleAddProbe}
              onRemoveProbe={handleRemoveProbe}
              hostIp={effectiveHost?.ipAddress || '127.0.0.1'}
            />
          </div>

          {/* Right Column: Live DAG Preview & Launch Trigger */}
          <div className="lg:col-span-6 p-6 flex flex-col bg-zinc-950/60 overflow-hidden">
            {/* Live Canvas Preview */}
            <div className="flex-1 min-h-0 flex flex-col">
              <LiveDagPreview
                enableSnapshot={enableSnapshot}
                enableK8sDrain={enableK8sDrain}
                requireApprovalBeforeReboot={requireApprovalBeforeReboot}
                probeCount={probeUrls.length}
                height="100%"
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
                  disabled={!effectiveHost || isSubmitting}
                  className="text-xs h-9 px-5 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold shadow-lg shadow-emerald-950/50 disabled:opacity-50 gap-2 cursor-pointer"
                >
                  {isSubmitting ? (
                    <>
                      <Loader2 className="w-4 h-4 animate-spin" />
                      Dispatched to Temporal...
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
