import {
  Sheet,
  SheetHeader,
  SheetTitle,
  SheetDescription,
  SheetBody,
  SheetFooter,
} from '../../components/ui/sheet'
import { Button } from '../../components/ui/button'
import {
  OsBadge,
  TargetTypeBadge,
  AgentStatusBadge,
  RebootBadge,
  UpdatesBadge,
} from './HostStatusBadge'
import { HostVitalsBadge } from './HostVitalsBadge'
import { useHostVitals } from './useHosts'
import {
  Server,
  Shield,
  Network,
  Calendar,
  Copy,
  Check,
  Pencil,
  Sparkles,
  RotateCcw,
  ExternalLink,
  Cpu,
  Layers,
  Fan,
  Radio,
  ArrowRight,
  AlertTriangle,
} from 'lucide-react'
import { useState } from 'react'
import type { Host } from '../../api/hosts'
import {
  useIdracPowerActionByIp,
  useIdracInstances,
  useIdracIdentifyByIp,
} from '../adapters/idrac/useIdrac'
import { ConfirmBmcPowerModal } from '../adapters/idrac/ConfirmBmcPowerModal'
import { BmcFanControlModal } from '../adapters/idrac/BmcFanControlModal'

export interface HostDetailsModalProps {
  host: Host | null
  open: boolean
  onClose: () => void
  onEdit?: (host: Host) => void
  onAdopt?: (host: Host) => void
  onTriggerUpdate?: (host: Host) => void
  onReboot?: (host: Host) => void
}

export function HostDetailsModal({
  host,
  open,
  onClose,
  onEdit,
  onAdopt,
  onTriggerUpdate,
  onReboot,
}: HostDetailsModalProps) {
  const [copied, setCopied] = useState(false)
  const [powerModalConfig, setPowerModalConfig] = useState<{
    open: boolean
    action: string
    label: string
  } | null>(null)
  const [fanModalOpen, setFanModalOpen] = useState(false)
  const [isBlinking, setIsBlinking] = useState(false)
  const [rebootConfirmOpen, setRebootConfirmOpen] = useState(false)

  const bmcPowerMutation = useIdracPowerActionByIp()
  const bmcIdentifyMutation = useIdracIdentifyByIp()
  const { data: idracInstances } = useIdracInstances()
  const { data: liveVitals } = useHostVitals(host?.id)

  if (!host) return null

  const linkedBmcInstance = idracInstances?.find(
    (inst) =>
      inst.hostId === host.id ||
      (inst.hostnameOrIp &&
        (inst.hostnameOrIp === host.ipAddress || inst.hostnameOrIp === host.hostname))
  )

  const handleCopyIp = () => {
    navigator.clipboard.writeText(host.ipAddress)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  const handleBlinkLed = async () => {
    setIsBlinking(true)
    try {
      await bmcIdentifyMutation.mutateAsync({
        hostId: host.id,
        idracIp:
          host.idrac?.ipAddress ||
          linkedBmcInstance?.hostnameOrIp ||
          linkedBmcInstance?.bmcUrl ||
          undefined,
        state: 'Blink',
        durationSeconds: 15,
      })
      setTimeout(() => setIsBlinking(false), 15000)
    } catch {
      setIsBlinking(false)
    }
  }

  const handleRebootClick = () => {
    if (onReboot) {
      setRebootConfirmOpen(false)
      onClose()
      onReboot(host)
    }
  }

  return (
    <>
      <Sheet open={open} onClose={onClose} width="sm:w-[560px]">
        <SheetHeader onClose={onClose}>
          <div className="flex items-center justify-between gap-3">
            <div className="flex items-center gap-2.5 min-w-0">
              <SheetTitle>{host.hostname}</SheetTitle>
              <TargetTypeBadge type={host.targetType} />
            </div>
          </div>
          <SheetDescription>
            Hardware & agent telemetry inspector
          </SheetDescription>
        </SheetHeader>

        <SheetBody>
          {/* Top Summary Banner */}
          <div className="flex flex-wrap items-center justify-between gap-3 p-3.5 bg-zinc-950/60 border border-zinc-800 rounded-xl">
            <div className="flex items-center gap-2">
              <span className="font-mono text-xs text-zinc-100 font-medium">
                {host.ipAddress}
              </span>
              <button
                onClick={handleCopyIp}
                className="p-1 rounded text-zinc-400 hover:text-zinc-100 hover:bg-zinc-800 transition-colors cursor-pointer"
                title="Copy IP"
              >
                {copied ? (
                  <Check className="h-3.5 w-3.5 text-emerald-400" />
                ) : (
                  <Copy className="h-3.5 w-3.5" />
                )}
              </button>
              <OsBadge osFamily={host.osFamily} />
            </div>

            <div className="flex items-center gap-2">
              <AgentStatusBadge agent={host.agent} />
              <RebootBadge pending={host.agent.pendingReboot} />
              <UpdatesBadge count={host.agent.upgradablePackagesCount} />
            </div>
          </div>

          {/* Identity & Metadata */}
          <div className="grid grid-cols-2 gap-3 p-3 bg-zinc-950/40 border border-zinc-800/60 rounded-xl text-xs">
            <div className="space-y-1">
              <span className="text-[11px] text-zinc-400">Host ID</span>
              <p className="font-mono text-zinc-200 truncate">{host.id}</p>
            </div>
            <div className="space-y-1">
              <span className="text-[11px] text-zinc-400">Friendly Name</span>
              <p className="text-zinc-200">{host.friendlyName || 'None'}</p>
            </div>
          </div>

          {/* Live Resource Vitals */}
          <HostVitalsBadge vitals={liveVitals || host.vitals} mode="detailed" />

          {/* Clean Horizontal Correlation Pipeline */}
          <div className="space-y-2.5">
            <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider">
              Infrastructure Correlation Pipeline
            </h4>

            <div className="p-3 bg-zinc-950/60 border border-zinc-800 rounded-xl space-y-3">
              <div className="flex items-center gap-1.5 overflow-x-auto pb-1 scrollbar-none text-xs">
                {/* 1. Proxmox */}
                {host.proxmox ? (
                  <div
                    className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg bg-purple-950/50 border border-purple-800/70 text-purple-200 shrink-0"
                    title={`Proxmox VE - Node: ${host.proxmox.node}, VMID: ${host.proxmox.vmid}`}
                  >
                    <Server className="h-3.5 w-3.5 text-purple-400 shrink-0" />
                    <span className="font-semibold">Proxmox:</span>
                    <span className="font-mono text-purple-300">VM {host.proxmox.vmid}</span>
                  </div>
                ) : (
                  <div className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg border border-dashed border-zinc-800 text-zinc-500 shrink-0">
                    <Server className="h-3.5 w-3.5 opacity-40 shrink-0" />
                    <span>Proxmox: Unmapped</span>
                  </div>
                )}

                <ArrowRight className="h-3.5 w-3.5 text-zinc-600 shrink-0" />

                {/* 2. Kubernetes */}
                {host.kubernetes?.clusterId || host.k8sClusterId ? (
                  <div
                    className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg bg-sky-950/50 border border-sky-800/70 text-sky-200 shrink-0"
                    title={`Cluster: ${host.kubernetes?.clusterId || host.k8sClusterId}`}
                  >
                    <Cpu className="h-3.5 w-3.5 text-sky-400 shrink-0" />
                    <span className="font-semibold">K8s:</span>
                    <span className="font-mono text-sky-300">
                      {host.kubernetes?.nodeName || host.k8sNodeName || host.hostname}
                    </span>
                  </div>
                ) : (
                  <div className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg border border-dashed border-zinc-800 text-zinc-500 shrink-0">
                    <Cpu className="h-3.5 w-3.5 opacity-40 shrink-0" />
                    <span>K8s: None</span>
                  </div>
                )}

                <ArrowRight className="h-3.5 w-3.5 text-zinc-600 shrink-0" />

                {/* 3. UniFi Port */}
                {host.networkPort ? (
                  <div
                    className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg bg-emerald-950/50 border border-emerald-800/70 text-emerald-200 shrink-0"
                    title={`Switch MAC: ${host.networkPort.switchMac}`}
                  >
                    <Network className="h-3.5 w-3.5 text-emerald-400 shrink-0" />
                    <span className="font-semibold">UniFi:</span>
                    <span className="font-mono text-emerald-300">
                      Port #{host.networkPort.portNumber}
                    </span>
                  </div>
                ) : (
                  <div className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg border border-dashed border-zinc-800 text-zinc-500 shrink-0">
                    <Network className="h-3.5 w-3.5 opacity-40 shrink-0" />
                    <span>UniFi: Unmapped</span>
                  </div>
                )}

                <ArrowRight className="h-3.5 w-3.5 text-zinc-600 shrink-0" />

                {/* 4. BMC */}
                {linkedBmcInstance || host.idrac?.ipAddress ? (
                  <div
                    className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg bg-amber-950/50 border border-amber-800/70 text-amber-200 shrink-0"
                    title={`BMC IP: ${host.idrac?.ipAddress || linkedBmcInstance?.hostnameOrIp || linkedBmcInstance?.bmcUrl}`}
                  >
                    <Shield className="h-3.5 w-3.5 text-amber-400 shrink-0" />
                    <span className="font-semibold">BMC:</span>
                    <span className="font-mono text-amber-300">
                      {host.idrac?.ipAddress || linkedBmcInstance?.hostnameOrIp || 'Active'}
                    </span>
                  </div>
                ) : (
                  <div className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg border border-dashed border-zinc-800 text-zinc-500 shrink-0">
                    <Shield className="h-3.5 w-3.5 opacity-40 shrink-0" />
                    <span>BMC: None</span>
                  </div>
                )}
              </div>

              {/* BMC Controls & Hardware Management when configured */}
              {(linkedBmcInstance || host.idrac?.ipAddress) && (
                <div className="pt-2 border-t border-zinc-800/80 space-y-2 text-xs">
                  <div className="flex items-center justify-between">
                    <span className="text-zinc-400 font-medium">Out-of-Band IPMI / Redfish Controls:</span>
                    <div className="flex items-center gap-1.5">
                      {isBlinking && (
                        <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-amber-950/40 border border-amber-500/40 text-amber-300 animate-pulse flex items-center gap-1">
                          <Radio className="h-2.5 w-2.5" />
                          LED
                        </span>
                      )}
                      {host.idrac?.ipAddress && (
                        <a
                          href={`https://${host.idrac.ipAddress}`}
                          target="_blank"
                          rel="noreferrer"
                          className="text-zinc-400 hover:text-zinc-200 text-[10px] flex items-center gap-1 hover:underline"
                        >
                          <span>WebGUI</span>
                          <ExternalLink className="h-2.5 w-2.5" />
                        </a>
                      )}
                    </div>
                  </div>

                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="flex items-center gap-1.5">
                      <button
                        onClick={() =>
                          setPowerModalConfig({ open: true, action: 'On', label: 'Power On' })
                        }
                        disabled={bmcPowerMutation.isPending}
                        className="px-2 py-1 rounded text-[11px] font-semibold bg-emerald-950/40 text-emerald-400 border border-emerald-800/50 hover:bg-emerald-900/60 transition-colors cursor-pointer"
                        title="Power On Hardware"
                      >
                        Power On
                      </button>
                      <button
                        onClick={() =>
                          setPowerModalConfig({
                            open: true,
                            action: 'PowerCycle',
                            label: 'Cold Power Cycle',
                          })
                        }
                        disabled={bmcPowerMutation.isPending}
                        className="px-2 py-1 rounded text-[11px] font-semibold bg-sky-950/40 text-sky-400 border border-sky-800/50 hover:bg-sky-900/60 transition-colors cursor-pointer"
                        title="Cold Hardware Power Cycle"
                      >
                        Power Cycle
                      </button>
                      <button
                        onClick={() =>
                          setPowerModalConfig({
                            open: true,
                            action: 'ForceOff',
                            label: 'Immediate Force Off',
                          })
                        }
                        disabled={bmcPowerMutation.isPending}
                        className="px-2 py-1 rounded text-[11px] font-semibold bg-rose-950/40 text-rose-400 border border-rose-800/50 hover:bg-rose-900/60 transition-colors cursor-pointer"
                        title="Force Off (Hard Power Cut)"
                      >
                        Force Off
                      </button>
                    </div>

                    <div className="flex items-center gap-1.5">
                      <button
                        type="button"
                        onClick={() => setFanModalOpen(true)}
                        className="px-2.5 py-1 rounded text-[11px] font-medium bg-sky-950/30 text-sky-300 border border-sky-800/40 hover:bg-sky-900/50 transition-colors flex items-center gap-1 cursor-pointer"
                        title="Adjust server fan curve / manual duty cycle"
                      >
                        <Fan className="h-3 w-3 text-sky-400" />
                        Fans
                      </button>
                      <button
                        type="button"
                        onClick={handleBlinkLed}
                        disabled={isBlinking || bmcIdentifyMutation.isPending}
                        className="px-2.5 py-1 rounded text-[11px] font-medium bg-amber-950/30 text-amber-300 border border-amber-800/40 hover:bg-amber-900/50 transition-colors flex items-center gap-1 disabled:opacity-50 cursor-pointer"
                        title="Blink Chassis Locator LED for 15s"
                      >
                        <Radio className="h-3 w-3 text-amber-400" />
                        LED
                      </button>
                    </div>
                  </div>
                </div>
              )}
            </div>
          </div>

          {/* Hosted Virtual Machines section for Hypervisors */}
          {host.hostedVms && host.hostedVms.length > 0 && (
            <div className="space-y-2.5">
              <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider flex items-center gap-1.5">
                <Layers className="h-3.5 w-3.5 text-purple-400" />
                Correlated Guest Virtual Machines ({host.hostedVms.length})
              </h4>
              <div className="border border-zinc-800/80 rounded-xl bg-zinc-950/50 overflow-hidden divide-y divide-zinc-800/60">
                {host.hostedVms.map((vm) => (
                  <div
                    key={vm.hostId}
                    className="p-2.5 flex items-center justify-between text-xs hover:bg-zinc-900/40 transition-colors"
                  >
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-[11px] px-1.5 py-0.5 rounded bg-zinc-800 text-purple-300 border border-purple-500/20 font-semibold">
                        VM #{vm.vmid}
                      </span>
                      <span className="font-medium text-zinc-200">{vm.hostname}</span>
                      {vm.friendlyName && (
                        <span className="text-zinc-500 text-[11px]">({vm.friendlyName})</span>
                      )}
                    </div>
                    <div className="flex items-center gap-2">
                      <span className="text-[11px] text-zinc-400">{vm.targetType}</span>
                      <span
                        className={`px-2 py-0.5 rounded text-[10px] font-semibold ${
                          vm.isOnline
                            ? 'bg-emerald-950/50 text-emerald-400 border border-emerald-800/50'
                            : 'bg-zinc-800 text-zinc-400'
                        }`}
                      >
                        {vm.isOnline ? 'Online' : 'Offline'}
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Timestamps */}
          <div className="flex items-center justify-between text-xs text-zinc-500 pt-3 border-t border-zinc-800/60">
            <div className="flex items-center gap-1.5">
              <Calendar className="h-3.5 w-3.5" />
              <span>Created: {new Date(host.createdAt).toLocaleDateString()}</span>
            </div>
            <div>
              Updated: {new Date(host.updatedAt).toLocaleTimeString()}
            </div>
          </div>
        </SheetBody>

        {/* Sticky Drawer Footer for Primary Mutations */}
        <SheetFooter>
          {host.agent.installed && onTriggerUpdate && (
            <Button
              variant="primary"
              size="sm"
              className="gap-1.5 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold shadow-xs"
              onClick={() => {
                onClose()
                onTriggerUpdate(host)
              }}
            >
              <Sparkles className="h-3.5 w-3.5" />
              Run DAG Update
            </Button>
          )}

          {host.agent.installed && onReboot && (
            <div className="relative">
              {rebootConfirmOpen ? (
                <div className="flex items-center gap-1 bg-zinc-950 border border-amber-500/50 rounded-lg p-1 animate-in fade-in">
                  <span className="text-[11px] text-amber-300 font-medium px-1 flex items-center gap-1">
                    <AlertTriangle className="h-3 w-3 text-amber-400" />
                    Confirm?
                  </span>
                  <button
                    type="button"
                    onClick={handleRebootClick}
                    className="px-2 py-0.5 rounded text-[10px] font-bold bg-amber-500 text-zinc-950 hover:bg-amber-400 transition-colors cursor-pointer"
                  >
                    Yes, Reboot
                  </button>
                  <button
                    type="button"
                    onClick={() => setRebootConfirmOpen(false)}
                    className="px-2 py-0.5 rounded text-[10px] text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800 transition-colors cursor-pointer"
                  >
                    Cancel
                  </button>
                </div>
              ) : (
                <Button
                  variant="outline"
                  size="sm"
                  className="gap-1.5 border-amber-600/40 text-amber-300 hover:bg-amber-950/40 hover:text-amber-200"
                  onClick={() => setRebootConfirmOpen(true)}
                  title="Initiate graceful host reboot workflow with impact verification"
                >
                  <RotateCcw className="h-3.5 w-3.5 text-amber-400" />
                  Reboot Node
                </Button>
              )}
            </div>
          )}

          {!host.agent.installed && onAdopt && (
            <Button
              variant="primary"
              size="sm"
              className="gap-1.5 bg-sky-500 hover:bg-sky-400 text-zinc-950 font-semibold"
              onClick={() => {
                onClose()
                onAdopt(host)
              }}
            >
              <Shield className="h-3.5 w-3.5" />
              Adopt Agent
            </Button>
          )}

          {onEdit && (
            <Button
              variant="outline"
              size="sm"
              className="gap-1.5"
              onClick={() => {
                onClose()
                onEdit(host)
              }}
            >
              <Pencil className="h-3.5 w-3.5" />
              Edit Host
            </Button>
          )}

          <Button variant="secondary" size="sm" onClick={onClose}>
            Close
          </Button>
        </SheetFooter>
      </Sheet>

      {/* Power Confirmation Dialog Modal */}
      {powerModalConfig && (
        <ConfirmBmcPowerModal
          open={powerModalConfig.open}
          onClose={() => setPowerModalConfig(null)}
          targetName={linkedBmcInstance?.name || host.hostname}
          targetIp={
            host.idrac?.ipAddress ||
            linkedBmcInstance?.hostnameOrIp ||
            linkedBmcInstance?.bmcUrl ||
            undefined
          }
          action={powerModalConfig.action}
          actionLabel={powerModalConfig.label}
          onConfirm={async () => {
            await bmcPowerMutation.mutateAsync({
              hostId: host.id,
              idracIp:
                host.idrac?.ipAddress ||
                linkedBmcInstance?.hostnameOrIp ||
                linkedBmcInstance?.bmcUrl ||
                undefined,
              resetType: powerModalConfig.action,
            })
          }}
        />
      )}

      {/* Fan Control Modal */}
      {fanModalOpen && (
        <BmcFanControlModal
          open={fanModalOpen}
          onClose={() => setFanModalOpen(false)}
          instanceId={linkedBmcInstance?.id}
          hostId={host.id}
          idracIp={host.idrac?.ipAddress}
          serverName={linkedBmcInstance?.name || host.hostname}
        />
      )}
    </>
  )
}

export const InspectorSheet = HostDetailsModal
