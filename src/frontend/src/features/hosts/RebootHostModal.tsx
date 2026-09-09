import { useState } from 'react'
import {
  RotateCcw,
  AlertTriangle,
  Server,
  Loader2,
  CheckCircle2,
  GitFork,
  ArrowRight,
  ShieldCheck,
  Cpu,
  AlertOctagon,
} from 'lucide-react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../components/ui/dialog'
import { Button } from '../../components/ui/button'
import { Badge } from '../../components/ui/badge'
import { useHostRebootImpact, useRebootHost } from './useHosts'
import type { Host } from '../../api/hosts'

interface RebootHostModalProps {
  host?: Host | null
  bulkHosts?: Host[] | null
  open: boolean
  onClose: () => void
  onRebootSuccess?: (jobId: string, host: Host, pipelineId?: string) => void
}

export function RebootHostModal({
  host,
  bulkHosts,
  open,
  onClose,
  onRebootSuccess,
}: RebootHostModalProps) {
  const rebootMutation = useRebootHost()
  const [errorMsg, setErrorMsg] = useState<string | null>(null)
  const [isSuccess, setIsSuccess] = useState(false)
  const [confirmedHypervisor, setConfirmedHypervisor] = useState(false)
  const [confirmedK8s, setConfirmedK8s] = useState(false)

  const targetHosts = bulkHosts && bulkHosts.length > 0 ? bulkHosts : host ? [host] : []
  const isBulk = targetHosts.length > 1
  const singleHostId = !isBulk && targetHosts.length === 1 ? targetHosts[0].id : undefined

  // Real-time correlation and impact analysis
  const { data: impact, isLoading: isLoadingImpact } = useHostRebootImpact(singleHostId)

  const hasK8sHost =
    impact?.isKubernetesNode ||
    targetHosts.some((h) => h.targetType?.toLowerCase().includes('k8s') || h.kubernetes != null)

  const hasHypervisorWarning = Boolean(impact?.isHypervisor && impact.affectedRunningVms.length > 0)
  const hasK8sWarning = Boolean(impact?.isKubernetesNode && impact.requiresConfirmation)

  const canConfirm =
    (!hasHypervisorWarning || confirmedHypervisor) &&
    (!hasK8sWarning || confirmedK8s)

  const handleConfirm = async () => {
    setErrorMsg(null)
    if (targetHosts.length === 0) return

    try {
      for (const h of targetHosts) {
        const res = await rebootMutation.mutateAsync({
          hostId: h.id,
          force: true,
        })
        if (!isBulk && onRebootSuccess) {
          onRebootSuccess(res.jobId, h, res.pipelineId)
        }
      }
      setIsSuccess(true)
      setTimeout(() => {
        setIsSuccess(false)
        setConfirmedHypervisor(false)
        setConfirmedK8s(false)
        onClose()
      }, 1200)
    } catch (err: unknown) {
      const msg =
        err && typeof err === 'object' && 'response' in err
          ? (err as { response?: { data?: { message?: string } } }).response?.data?.message ??
            'Failed to dispatch reboot DAG pipeline'
          : 'Failed to dispatch reboot DAG pipeline'
      setErrorMsg(msg)
    }
  }

  if (!open || targetHosts.length === 0) return null

  return (
    <Dialog open={open} onClose={onClose} maxWidth="lg">
      <DialogHeader>
        <div className="flex items-center gap-3">
          <div className="p-2.5 rounded-xl bg-amber-500/10 border border-amber-500/20 text-amber-400">
            <RotateCcw className="w-5 h-5" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <DialogTitle>
                {isBulk ? `Reboot ${targetHosts.length} Hosts` : `Reboot ${targetHosts[0].hostname}`}
              </DialogTitle>
              <Badge variant="outline" className="text-[10px] px-1.5 py-0 border-emerald-500/30 text-emerald-400 bg-emerald-500/10">
                <GitFork className="w-2.5 h-2.5 mr-1" />
                DAG Orchestrated
              </Badge>
              {impact?.isHypervisor && (
                <Badge variant="outline" className="text-[10px] px-1.5 py-0 border-purple-500/30 text-purple-400 bg-purple-500/10">
                  <Server className="w-2.5 h-2.5 mr-1" />
                  Proxmox Hypervisor
                </Badge>
              )}
              {impact?.isKubernetesNode && (
                <Badge variant="outline" className="text-[10px] px-1.5 py-0 border-sky-500/30 text-sky-400 bg-sky-500/10">
                  <Cpu className="w-2.5 h-2.5 mr-1" />
                  Kubernetes Node
                </Badge>
              )}
            </div>
            <p className="text-xs text-zinc-400 mt-0.5">
              {isBulk
                ? 'Dispatch orchestrated DAG reboot workflows across multiple nodes'
                : 'Execute deterministic safe reboot with preflight safety checks, reconnection monitoring, and health verification'}
            </p>
          </div>
        </div>
      </DialogHeader>

      <DialogBody className="space-y-4 pt-2">
        {errorMsg && (
          <div className="p-3 bg-rose-950/40 border border-rose-800/50 rounded-lg text-xs text-rose-300 flex items-start gap-2">
            <AlertTriangle className="w-4 h-4 text-rose-400 shrink-0 mt-0.5" />
            <span>{errorMsg}</span>
          </div>
        )}

        {isSuccess && (
          <div className="p-3 bg-emerald-950/40 border border-emerald-800/50 rounded-lg text-xs text-emerald-300 flex items-center gap-2">
            <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
            <span>Reboot DAG pipeline successfully initiated!</span>
          </div>
        )}

        {/* Hypervisor Impact Warning Banner */}
        {hasHypervisorWarning && impact && (
          <div className="p-4 bg-rose-950/30 border border-rose-800/60 rounded-xl space-y-3">
            <div className="flex items-start gap-2.5">
              <AlertOctagon className="w-5 h-5 text-rose-400 shrink-0 mt-0.5" />
              <div>
                <h4 className="text-sm font-semibold text-rose-200">
                  Proxmox Hypervisor Warning: {impact.affectedRunningVms.length} Running VM(s) Detected
                </h4>
                <p className="text-xs text-rose-300/80 mt-0.5 leading-relaxed">
                  Rebooting this hypervisor ({impact.hypervisorNode}) will immediately terminate or interrupt power to the following active guests. Ensure workloads have been migrated or safely paused.
                </p>
              </div>
            </div>

            {/* Affected VMs list */}
            <div className="bg-zinc-950/70 border border-rose-900/40 rounded-lg overflow-hidden divide-y divide-zinc-800/70 max-h-48 overflow-y-auto">
              {impact.affectedRunningVms.map((vm) => (
                <div key={vm.vmid} className="p-2.5 flex items-center justify-between text-xs">
                  <div className="flex items-center gap-2.5">
                    <span className="font-mono text-[11px] px-1.5 py-0.5 rounded bg-zinc-800 text-purple-300 border border-purple-500/20 font-semibold">
                      {vm.type === 'lxc' ? 'LXC' : 'VM'} #{vm.vmid}
                    </span>
                    <span className="font-medium text-zinc-200">{vm.name}</span>
                    {vm.ipAddress && (
                      <span className="font-mono text-[11px] text-zinc-400">({vm.ipAddress})</span>
                    )}
                  </div>

                  <div className="flex items-center gap-2">
                    {vm.k8sClusterId && (
                      <Badge variant="outline" className="text-[10px] px-1.5 py-0 border-sky-500/30 text-sky-400 bg-sky-500/10">
                        <Cpu className="w-2.5 h-2.5 mr-1" />
                        K8s: {vm.k8sClusterId}
                      </Badge>
                    )}
                    <span className="flex items-center gap-1 text-[11px] text-emerald-400 font-medium">
                      <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
                      Running
                    </span>
                  </div>
                </div>
              ))}
            </div>

            {/* Hypervisor confirmation checkbox */}
            <label className="flex items-start gap-2.5 cursor-pointer pt-1 select-none">
              <input
                type="checkbox"
                checked={confirmedHypervisor}
                onChange={(e) => setConfirmedHypervisor(e.target.checked)}
                className="mt-0.5 rounded border-rose-700 bg-zinc-900 text-rose-500 focus:ring-rose-500 focus:ring-offset-zinc-900"
              />
              <span className="text-xs text-rose-200 font-medium">
                I understand that rebooting this hypervisor will terminate or interrupt {impact.affectedRunningVms.length} running virtual machine(s) and their active services.
              </span>
            </label>
          </div>
        )}

        {/* Kubernetes Cluster Health Impact Warning */}
        {impact?.isKubernetesNode && impact.kubernetesImpact && (
          <div className="p-4 bg-sky-950/30 border border-sky-800/60 rounded-xl space-y-3">
            <div className="flex items-start gap-2.5">
              <Cpu className="w-5 h-5 text-sky-400 shrink-0 mt-0.5" />
              <div>
                <div className="flex items-center gap-2">
                  <h4 className="text-sm font-semibold text-sky-200">
                    Kubernetes Cluster Impact: {impact.kubernetesImpact.clusterId}
                  </h4>
                  {impact.kubernetesImpact.quorumAtRisk && (
                    <Badge variant="destructive" className="text-[10px] px-1.5 py-0">
                      Quorum at Risk
                    </Badge>
                  )}
                  {impact.kubernetesImpact.isControlPlane && (
                    <Badge variant="outline" className="text-[10px] px-1.5 py-0 border-amber-500/30 text-amber-400 bg-amber-500/10">
                      Control Plane Node
                    </Badge>
                  )}
                </div>
                <p className="text-xs text-sky-300/80 mt-1 leading-relaxed">
                  {impact.kubernetesImpact.summary}
                </p>
              </div>
            </div>

            {/* Cluster Stats Grid */}
            <div className="grid grid-cols-2 md:grid-cols-4 gap-2 pt-1">
              <div className="p-2 bg-zinc-950/60 border border-zinc-800/60 rounded-lg text-center">
                <span className="text-[10px] text-zinc-400 uppercase tracking-wider block">Role</span>
                <span className="text-xs font-semibold text-zinc-200">
                  {impact.kubernetesImpact.isControlPlane ? 'Control Plane' : 'Worker'}
                </span>
              </div>
              <div className="p-2 bg-zinc-950/60 border border-zinc-800/60 rounded-lg text-center">
                <span className="text-[10px] text-zinc-400 uppercase tracking-wider block">Cluster Capacity</span>
                <span className="text-xs font-semibold text-emerald-400">
                  {impact.kubernetesImpact.readyNodes} / {impact.kubernetesImpact.totalNodes} Nodes Ready
                </span>
              </div>
              <div className="p-2 bg-zinc-950/60 border border-zinc-800/60 rounded-lg text-center">
                <span className="text-[10px] text-zinc-400 uppercase tracking-wider block">Pods on Node</span>
                <span className="text-xs font-semibold text-amber-400 font-mono">
                  {impact.kubernetesImpact.runningPodsCount} Running
                </span>
              </div>
              <div className="p-2 bg-zinc-950/60 border border-zinc-800/60 rounded-lg text-center">
                <span className="text-[10px] text-zinc-400 uppercase tracking-wider block">DAG Action</span>
                <span className="text-xs font-semibold text-indigo-400">
                  Cordon & Drain
                </span>
              </div>
            </div>

            {/* K8s confirmation checkbox if quorum at risk or high pod count */}
            {impact.requiresConfirmation && (
              <label className="flex items-start gap-2.5 cursor-pointer pt-1 select-none">
                <input
                  type="checkbox"
                  checked={confirmedK8s}
                  onChange={(e) => setConfirmedK8s(e.target.checked)}
                  className="mt-0.5 rounded border-sky-700 bg-zinc-900 text-sky-500 focus:ring-sky-500 focus:ring-offset-zinc-900"
                />
                <span className="text-xs text-sky-200 font-medium">
                  I understand that rebooting node {impact.kubernetesImpact.nodeName} will temporarily drain workloads and may degrade or disrupt cluster '{impact.kubernetesImpact.clusterId}'.
                </span>
              </label>
            )}
          </div>
        )}

        {/* Loading Impact Indicator */}
        {isLoadingImpact && !isBulk && (
          <div className="p-3 bg-zinc-900/40 border border-zinc-800/50 rounded-lg flex items-center justify-center gap-2 text-xs text-zinc-400">
            <Loader2 className="w-3.5 h-3.5 animate-spin text-purple-400" />
            <span>Checking Proxmox hypervisor & Kubernetes cluster correlation...</span>
          </div>
        )}

        {/* DAG Pipeline Flow Preview */}
        <div className="p-3.5 bg-zinc-900/60 border border-zinc-800/80 rounded-xl space-y-2.5">
          <div className="flex items-center justify-between text-xs">
            <div className="flex items-center gap-1.5 text-zinc-300 font-medium">
              <ShieldCheck className="w-3.5 h-3.5 text-emerald-400" />
              <span>Orchestrated DAG Steps</span>
            </div>
            <span className="text-[10px] text-zinc-400 font-mono">
              {hasK8sHost ? 'k8s-node-safe-reboot' : 'safe-reboot-verify'}
            </span>
          </div>

          <div className="flex items-center gap-1 text-[11px] text-zinc-400 overflow-x-auto pb-1">
            <span className="px-2 py-1 rounded bg-zinc-800/70 text-zinc-300 whitespace-nowrap">
              1. Preflight Check
            </span>
            <ArrowRight className="w-3 h-3 text-zinc-600 shrink-0" />
            {hasK8sHost && (
              <>
                <span className="px-2 py-1 rounded bg-indigo-950/40 border border-indigo-800/40 text-indigo-300 whitespace-nowrap">
                  2. Cordon & Drain
                </span>
                <ArrowRight className="w-3 h-3 text-zinc-600 shrink-0" />
              </>
            )}
            <span className="px-2 py-1 rounded bg-amber-950/40 border border-amber-800/40 text-amber-300 whitespace-nowrap">
              {hasK8sHost ? '3.' : '2.'} Deterministic Reboot
            </span>
            <ArrowRight className="w-3 h-3 text-zinc-600 shrink-0" />
            <span className="px-2 py-1 rounded bg-sky-950/40 border border-sky-800/40 text-sky-300 whitespace-nowrap">
              {hasK8sHost ? '4.' : '3.'} Await Reconnect
            </span>
            <ArrowRight className="w-3 h-3 text-zinc-600 shrink-0" />
            <span className="px-2 py-1 rounded bg-purple-950/40 border border-purple-800/40 text-purple-300 whitespace-nowrap">
              {hasK8sHost ? '5.' : '4.'} Health Probes
            </span>
            {hasK8sHost && (
              <>
                <ArrowRight className="w-3 h-3 text-zinc-600 shrink-0" />
                <span className="px-2 py-1 rounded bg-indigo-950/40 border border-indigo-800/40 text-indigo-300 whitespace-nowrap">
                  6. Node Uncordon
                </span>
              </>
            )}
          </div>
        </div>

        {/* Warning Banner */}
        <div className="p-3 bg-amber-950/20 border border-amber-800/30 rounded-xl space-y-1.5">
          <div className="flex items-center gap-2 text-amber-300 text-xs font-medium">
            <AlertTriangle className="w-3.5 h-3.5 text-amber-400 shrink-0" />
            <span>Controlled System Restart</span>
          </div>
          <p className="text-[11px] text-zinc-400 leading-relaxed">
            The compute node will perform a filesystem sync and graceful reboot. The ControlPlane DAG state machine will track WebSocket reconnection and verify service health before marking the job completed.
          </p>
        </div>

        {/* Target Hosts Summary */}
        <div className="border border-zinc-800/80 rounded-xl bg-zinc-950/50 p-3 divide-y divide-zinc-800/60 max-h-48 overflow-y-auto">
          {targetHosts.map((h) => (
            <div key={h.id} className="py-2 first:pt-0 last:pb-0 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Server className="w-4 h-4 text-zinc-500 shrink-0" />
                <div>
                  <span className="text-xs font-medium text-zinc-200">{h.hostname}</span>
                  <span className="text-[11px] text-zinc-500 ml-2 font-mono">{h.ipAddress}</span>
                </div>
              </div>
              <div className="flex items-center gap-1.5">
                {h.agent.pendingReboot && (
                  <Badge variant="warning" className="text-[10px] px-1.5 py-0.5">
                    Kernel Pending
                  </Badge>
                )}
                {!h.agent.installed && (
                  <Badge variant="destructive" className="text-[10px] px-1.5 py-0.5">
                    Offline
                  </Badge>
                )}
              </div>
            </div>
          ))}
        </div>
      </DialogBody>

      <DialogFooter>
        <Button variant="ghost" size="sm" onClick={onClose} disabled={rebootMutation.isPending}>
          Cancel
        </Button>
        <Button
          variant="primary"
          size="sm"
          onClick={handleConfirm}
          disabled={rebootMutation.isPending || isSuccess || !canConfirm}
          className={`gap-1.5 text-white ${
            hasHypervisorWarning
              ? 'bg-rose-600 hover:bg-rose-500 border-rose-500'
              : 'bg-amber-600 hover:bg-amber-500 border-amber-500'
          }`}
        >
          {rebootMutation.isPending ? (
            <>
              <Loader2 className="w-3.5 h-3.5 animate-spin" />
              Starting DAG...
            </>
          ) : (
            <>
              <RotateCcw className="w-3.5 h-3.5" />
              {isBulk ? `Launch Reboot DAG (${targetHosts.length})` : 'Launch Reboot DAG'}
            </>
          )}
        </Button>
      </DialogFooter>
    </Dialog>
  )
}
