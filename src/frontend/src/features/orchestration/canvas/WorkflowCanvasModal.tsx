import { useMemo, useCallback, useState } from 'react'
import {
  X,
  GitFork,
  CheckCircle2,
  AlertCircle,
  Terminal,
  ShieldAlert,
  Loader2,
  RefreshCw,
  Square,
} from 'lucide-react'
import { useQueryClient } from '@tanstack/react-query'
import { WorkflowDagCanvas } from './WorkflowDagCanvas'
import { useTemporalWorkflow } from './hooks/useTemporalWorkflow'
import { cancelJob, type JobSummary } from '../../../api/jobs'
import type { Host } from '../../../api/hosts'
import type { WorkflowStateLike } from './layout/dagLayout'
import { Button } from '../../../components/ui/button'

export interface WorkflowCanvasModalProps {
  isOpen: boolean
  onClose: () => void
  job: JobSummary | null
  host: Host | null
  onOpenTerminal?: (job: JobSummary) => void
}

export const WorkflowCanvasModal: React.FC<WorkflowCanvasModalProps> = ({
  isOpen,
  onClose,
  job,
  host,
  onOpenTerminal,
}) => {
  const workflowId = useMemo(() => {
    if (!job) return null
    // If job id starts with host-upgrade-, use directly, otherwise standard format
    return job.id.startsWith('host-upgrade-') ? job.id : `host-upgrade-${job.id}`
  }, [job])

  const queryClient = useQueryClient()
  const [isStopping, setIsStopping] = useState(false)

  const {
    data: temporalData,
    refetch,
    isFetching,
    approveReboot,
    isApproving,
    rejectWorkflow,
    cancelWorkflow,
    isRejecting,
  } = useTemporalWorkflow(isOpen ? workflowId : null)

  // Construct effective workflow state combining Temporal live query and JobSummary
  const effectiveState: WorkflowStateLike = useMemo(() => {
    if (temporalData?.state) {
      return {
        status: temporalData.executionStatus || temporalData.state.status,
        activeStep: temporalData.state.activeStep,
        completedSteps: temporalData.state.completedSteps || [],
        skippedSteps: temporalData.state.skippedSteps || [],
        awaitingApproval: temporalData.state.awaitingApproval,
        rebootApproved: temporalData.state.rebootApproved,
        cancelled: temporalData.state.cancelled,
        failureReason: temporalData.executionStatus === 'Completed' || temporalData.state.status === 'Completed' ? null : temporalData.state.failureReason,
        snapshotIdentifier: temporalData.state.snapshotIdentifier,
        k8sNodeName: temporalData.state.k8sNodeName,
      }
    }

    // Fallback based on JobSummary
    if (job) {
      return {
        status: job.status,
        activeStep: job.activeStep,
        completedSteps: job.status === 'Completed' ? ['Preflight: Heartbeat Freshness', 'Preflight: Disk Headroom', 'Preflight: Package Lock Check', 'Hypervisor Safety Snapshot', 'Package Upgrade Execution', 'Post-Flight Health Probes'] : [],
        skippedSteps: [],
        failureReason: job.status === 'Completed' ? null : job.failureReason,
      }
    }

    return { status: 'Pending', completedSteps: [], skippedSteps: [] }
  }, [temporalData, job])

  const handleApprove = useCallback(() => {
    approveReboot()
  }, [approveReboot])

  const handleReject = useCallback(() => {
    rejectWorkflow('Operator rejected reboot from node action')
  }, [rejectWorkflow])

  const isWorkflowActive = useMemo(() => {
    const status = (effectiveState.status || job?.status || '').toLowerCase()
    return (
      status === 'running' ||
      status === 'pending' ||
      status === 'verifying' ||
      status === 'awaitingreconnect' ||
      status === 'awaitingapproval'
    )
  }, [effectiveState.status, job?.status])

  const handleStopWorkflow = useCallback(async () => {
    const reason = 'Operator stopped workflow from DAG canvas'
    setIsStopping(true)
    try {
      if (workflowId) {
        try {
          await cancelWorkflow(reason)
        } catch {
          // ignore if not temporal
        }
      }
      if (job?.id) {
        try {
          await cancelJob(job.id, reason)
        } catch {
          // ignore
        }
      }
      queryClient.invalidateQueries({ queryKey: ['temporalWorkflow', workflowId] })
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      refetch()
    } finally {
      setIsStopping(false)
    }
  }, [workflowId, job?.id, cancelWorkflow, refetch, queryClient])

  if (!isOpen || !job) return null

  const isAwaitingApproval = effectiveState.awaitingApproval
  const isProxmoxHost = Boolean(
    host?.targetType?.toLowerCase().includes('proxmox') ||
    host?.proxmox != null ||
    Boolean(effectiveState.snapshotIdentifier) ||
    effectiveState.activeStep?.toLowerCase().includes('snapshot') ||
    effectiveState.completedSteps?.some((s) => s.toLowerCase().includes('snapshot'))
  )
  const isK8sHost = Boolean(
    host?.targetType?.toLowerCase().includes('k8s') ||
    host?.targetType?.toLowerCase().includes('kubernetes') ||
    host?.kubernetes != null ||
    Boolean(host?.k8sNodeName) ||
    Boolean(host?.k8sClusterId) ||
    host?.hostname?.toLowerCase().includes('kube') ||
    Boolean(effectiveState.k8sNodeName) ||
    effectiveState.activeStep?.toLowerCase().includes('kubernetes') ||
    effectiveState.activeStep?.toLowerCase().includes('evict') ||
    effectiveState.activeStep?.toLowerCase().includes('cordon') ||
    effectiveState.activeStep?.toLowerCase().includes('drain') ||
    effectiveState.completedSteps?.some((s) => s.toLowerCase().includes('kubernetes')) ||
    job?.pipelineId?.toLowerCase().includes('k8s')
  )

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 sm:p-6 md:p-8 bg-black/80 backdrop-blur-md animate-in fade-in duration-200">
      <div className="relative w-full max-w-[1400px] h-[90vh] bg-zinc-950 border border-zinc-800 rounded-2xl shadow-2xl flex flex-col overflow-hidden">
        {/* Header */}
        <div className="p-4 sm:px-6 bg-zinc-900/80 border-b border-zinc-800 flex items-center justify-between gap-4 shrink-0">
          <div className="flex items-center gap-3 min-w-0">
            <div className="p-2.5 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 shrink-0">
              <GitFork className="w-5 h-5" />
            </div>
            <div className="min-w-0">
              <div className="flex items-center gap-2 flex-wrap">
                <h3 className="text-base font-bold text-zinc-100 truncate">
                  Workflow DAG Canvas: {host?.hostname || job.targetHostId}
                </h3>
                <span className="px-2 py-0.5 rounded text-[11px] font-mono bg-zinc-800 text-zinc-300 border border-zinc-700">
                  {job.id}
                </span>
              </div>
              <p className="text-xs text-zinc-400 mt-0.5 flex items-center gap-3">
                <span>Pipeline: <strong className="text-zinc-200">{job.pipelineId || 'standard-os-upgrade'}</strong></span>
                <span>Target IP: <strong className="text-zinc-200 font-mono">{host?.ipAddress || '—'}</strong></span>
                {effectiveState.activeStep && (
                  <span className="text-sky-400">
                    Active Step: <strong>{effectiveState.activeStep}</strong>
                  </span>
                )}
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2 shrink-0">
            {isWorkflowActive && (
              <Button
                variant="outline"
                size="sm"
                onClick={handleStopWorkflow}
                disabled={isStopping || isRejecting}
                className="text-xs h-8 gap-1.5 border-rose-800/80 bg-rose-950/40 text-rose-300 hover:bg-rose-900/60 hover:text-rose-100 transition-colors shadow-xs shadow-rose-950/40"
                title="Stop and cancel this running workflow"
              >
                {isStopping || isRejecting ? (
                  <Loader2 className="w-3.5 h-3.5 animate-spin mr-1" />
                ) : (
                  <Square className="w-3 h-3 fill-rose-400 text-rose-400 mr-1" />
                )}
                Stop Workflow
              </Button>
            )}
            {onOpenTerminal && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => onOpenTerminal(job)}
                className="text-xs h-8 gap-1.5 border-zinc-700 bg-zinc-900 text-sky-400 hover:text-sky-300"
              >
                <Terminal className="w-3.5 h-3.5" />
                Terminal Stream
              </Button>
            )}
            <Button
              variant="outline"
              size="sm"
              onClick={() => refetch()}
              disabled={isFetching}
              className="text-xs h-8 px-2 border-zinc-700 bg-zinc-900 text-zinc-300 hover:text-zinc-100"
              title="Refresh workflow status"
            >
              <RefreshCw className={`w-3.5 h-3.5 ${isFetching ? 'animate-spin' : ''}`} />
            </Button>
            <button
              type="button"
              onClick={onClose}
              className="p-1.5 rounded-lg text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800 transition-colors"
            >
              <X className="w-5 h-5" />
            </button>
          </div>
        </div>

        {/* Approval Alert Banner (When waiting for approval) */}
        {isAwaitingApproval && (
          <div className="bg-gradient-to-r from-amber-950/80 via-amber-900/60 to-zinc-900 border-b border-amber-500/40 px-6 py-3 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 animate-in slide-in-from-top-2 duration-300">
            <div className="flex items-center gap-3">
              <div className="p-2 rounded-lg bg-amber-500/20 text-amber-300 border border-amber-500/30 shrink-0 animate-pulse">
                <ShieldAlert className="w-5 h-5" />
              </div>
              <div>
                <h4 className="text-xs font-bold text-amber-200">
                  Human-in-the-Loop Action Required: Host Reboot Gate
                </h4>
                <p className="text-[11px] text-amber-300/80 mt-0.5">
                  The workflow has completed package upgrades and reached the safety checkpoint. Verify host status and approve to proceed with reboot.
                </p>
              </div>
            </div>

            <div className="flex items-center gap-2 self-end sm:self-center shrink-0">
              <Button
                variant="primary"
                size="sm"
                onClick={() => approveReboot()}
                disabled={isApproving}
                className="text-xs h-8 px-3 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold shadow-md shadow-emerald-950/50"
              >
                {isApproving ? (
                  <Loader2 className="w-3.5 h-3.5 animate-spin mr-1.5" />
                ) : (
                  <CheckCircle2 className="w-3.5 h-3.5 mr-1.5" />
                )}
                Approve Reboot
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => rejectWorkflow('Operator rejected reboot from modal banner')}
                disabled={isRejecting}
                className="text-xs h-8 px-3 border-rose-700 bg-rose-950/40 text-rose-300 hover:bg-rose-900/60"
              >
                {isRejecting ? (
                  <Loader2 className="w-3.5 h-3.5 animate-spin mr-1.5" />
                ) : (
                  <AlertCircle className="w-3.5 h-3.5 mr-1.5" />
                )}
                Reject & Rollback
              </Button>
            </div>
          </div>
        )}

        {/* Canvas Body */}
        <div className="flex-1 w-full h-full relative overflow-hidden bg-zinc-950">
          <WorkflowDagCanvas
            workflowId={workflowId || job.id}
            state={effectiveState}
            pipelineId={job.pipelineId}
            onApprove={handleApprove}
            onReject={handleReject}
            isSubmittingApproval={isApproving || isRejecting}
            isProxmoxHost={isProxmoxHost}
            isK8sHost={isK8sHost}
            requireApproval={Boolean(isAwaitingApproval || effectiveState.rebootApproved)}
            height="100%"
            className="h-full border-none rounded-none"
          />
        </div>

        {/* Footer / Legend */}
        <div className="px-6 py-2.5 bg-zinc-900/90 border-t border-zinc-800 flex items-center justify-between text-xs text-zinc-400 shrink-0">
          <div className="flex items-center gap-4 flex-wrap">
            <span className="text-[11px] font-semibold text-zinc-300">Legend:</span>
            <span className="inline-flex items-center gap-1.5 text-[11px]">
              <span className="w-2 h-2 rounded-full bg-emerald-500" />
              Completed
            </span>
            <span className="inline-flex items-center gap-1.5 text-[11px]">
              <span className="w-2 h-2 rounded-full bg-sky-400 animate-pulse" />
              Running Activity
            </span>
            <span className="inline-flex items-center gap-1.5 text-[11px]">
              <span className="w-2 h-2 rounded-full bg-amber-400" />
              Approval Gate
            </span>
            <span className="inline-flex items-center gap-1.5 text-[11px]">
              <span className="w-2 h-2 rounded-full bg-orange-400" />
              Saga Compensation
            </span>
            <span className="inline-flex items-center gap-1.5 text-[11px]">
              <span className="w-2 h-2 rounded-full bg-rose-500" />
              Failed
            </span>
          </div>

          <div className="text-[11px] font-mono text-zinc-500">
            Powered by @xyflow/react & Temporal Durable Execution
          </div>
        </div>
      </div>
    </div>
  )
}
