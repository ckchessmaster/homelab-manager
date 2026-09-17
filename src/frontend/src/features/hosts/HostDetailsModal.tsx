import {
  Dialog,
  DialogBody,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '../../components/ui/dialog'
import { Button } from '../../components/ui/button'
import {
  OsBadge,
  TargetTypeBadge,
  AgentStatusBadge,
  RebootBadge,
  UpdatesBadge,
} from './HostStatusBadge'
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

interface HostDetailsModalProps {
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

  const bmcPowerMutation = useIdracPowerActionByIp()
  const bmcIdentifyMutation = useIdracIdentifyByIp()
  const { data: idracInstances } = useIdracInstances()

  if (!host) return null

  const linkedBmcInstance = idracInstances?.find(
    (inst) => inst.hostId === host.id || (inst.hostnameOrIp && (inst.hostnameOrIp === host.ipAddress || inst.hostnameOrIp === host.hostname))
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
        idracIp: host.idrac?.ipAddress || linkedBmcInstance?.hostnameOrIp || linkedBmcInstance?.bmcUrl || undefined,
        state: 'Blink',
        durationSeconds: 15,
      })
      setTimeout(() => setIsBlinking(false), 15000)
    } catch {
      setIsBlinking(false)
    }
  }

  return (
    <>
      <Dialog open={open} onClose={onClose} maxWidth="lg">
      <DialogHeader onClose={onClose}>
        <div className="flex items-center gap-3">
          <DialogTitle>{host.hostname}</DialogTitle>
          <TargetTypeBadge type={host.targetType} />
        </div>
      </DialogHeader>

      <DialogBody className="space-y-5">
        {/* Top Summary Banner */}
        <div className="flex flex-wrap items-center justify-between gap-3 p-3.5 bg-zinc-950/60 border border-zinc-800 rounded-xl">
          <div className="flex items-center gap-2">
            <span className="font-mono text-sm text-zinc-100 font-medium">
              {host.ipAddress}
            </span>
            <button
              onClick={handleCopyIp}
              className="p-1 rounded text-zinc-400 hover:text-zinc-100 hover:bg-zinc-800 transition-colors"
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
        <div className="grid grid-cols-2 gap-4">
          <div className="space-y-1">
            <span className="text-xs text-zinc-400">Host ID</span>
            <p className="text-xs font-mono text-zinc-200 truncate">{host.id}</p>
          </div>
          <div className="space-y-1">
            <span className="text-xs text-zinc-400">Friendly Name</span>
            <p className="text-xs text-zinc-200">{host.friendlyName || 'None'}</p>
          </div>
        </div>

        {/* Correlation Targets */}
        <div className="space-y-3">
          <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider">
            Hardware & Infrastructure Correlation
          </h4>

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
            {/* Proxmox */}
            <div className="p-3 bg-zinc-950/50 border border-zinc-800/80 rounded-lg">
              <div className="flex items-center gap-1.5 text-xs font-medium text-purple-300 mb-1.5">
                <Server className="h-3.5 w-3.5" />
                Proxmox VE
              </div>
              {host.proxmox ? (
                <div className="text-xs text-zinc-300 space-y-0.5">
                  <div>Node: <span className="font-mono text-zinc-100">{host.proxmox.node}</span></div>
                  <div>VMID: <span className="font-mono text-zinc-100">{host.proxmox.vmid}</span></div>
                  {host.hypervisor && (
                    <div className="pt-1 text-[11px] text-purple-300">
                      Hypervisor: <span className="font-semibold text-purple-200">{host.hypervisor.hostname}</span>
                    </div>
                  )}
                  {host.hostedVms && host.hostedVms.length > 0 && (
                    <div className="pt-1 text-[11px] text-purple-300">
                      Hosts: <span className="font-semibold text-purple-200">{host.hostedVms.length} VM(s)</span>
                    </div>
                  )}
                </div>
              ) : (
                <span className="text-xs text-zinc-500">Not linked</span>
              )}
            </div>

            {/* Kubernetes */}
            <div className="p-3 bg-zinc-950/50 border border-zinc-800/80 rounded-lg">
              <div className="flex items-center gap-1.5 text-xs font-medium text-sky-300 mb-1.5">
                <Cpu className="h-3.5 w-3.5" />
                Kubernetes
              </div>
              {host.kubernetes?.clusterId || host.k8sClusterId ? (
                <div className="text-xs text-zinc-300 space-y-0.5">
                  <div>Cluster: <span className="font-mono text-zinc-100">{host.kubernetes?.clusterId || host.k8sClusterId}</span></div>
                  <div>Node: <span className="font-mono text-zinc-100">{host.kubernetes?.nodeName || host.k8sNodeName || host.hostname}</span></div>
                </div>
              ) : (
                <span className="text-xs text-zinc-500">Not in cluster</span>
              )}
            </div>

            {/* iDRAC / Out-of-band Management */}
            <div className="p-3 bg-zinc-950/50 border border-zinc-800/80 rounded-lg space-y-2">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-1.5 text-xs font-medium text-purple-300">
                  <Shield className="h-3.5 w-3.5" />
                  BMC / iDRAC
                </div>
                <div className="flex items-center gap-1.5">
                  {isBlinking && (
                    <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-amber-950/40 border border-amber-500/40 text-amber-300 animate-pulse flex items-center gap-1">
                      <Radio className="h-2.5 w-2.5" />
                      LED
                    </span>
                  )}
                  {linkedBmcInstance ? (
                    <span className="text-[10px] text-zinc-500 font-mono">
                      {linkedBmcInstance.connectionMode === 'agent' ? 'In-Band Agent' : 'Out-of-Band'}
                    </span>
                  ) : (
                    host.idrac?.ipAddress && (
                      <a
                        href={`https://${host.idrac.ipAddress}`}
                        target="_blank"
                        rel="noreferrer"
                        className="text-zinc-500 hover:text-zinc-300 text-[10px] flex items-center gap-0.5"
                        title="Open iDRAC Web Interface"
                      >
                        <span>WebGUI</span>
                        <ExternalLink className="h-2.5 w-2.5" />
                      </a>
                    )
                  )}
                </div>
              </div>
              {linkedBmcInstance || host.idrac?.ipAddress ? (
                <div className="space-y-2">
                  <div className="text-xs text-zinc-300">
                    {linkedBmcInstance?.connectionMode === 'agent' ? (
                      <div>
                        Mode: <span className="font-semibold text-purple-300">Host Agent IPMI</span>
                        {linkedBmcInstance.name && (
                          <span className="text-zinc-500 text-[11px] ml-1.5">({linkedBmcInstance.name})</span>
                        )}
                      </div>
                    ) : (
                      <div>
                        IP: <span className="font-mono text-zinc-100">{host.idrac?.ipAddress || linkedBmcInstance?.hostnameOrIp || linkedBmcInstance?.bmcUrl}</span>
                      </div>
                    )}
                  </div>
                  <div className="flex flex-wrap items-center justify-between gap-1.5 pt-1.5 border-t border-zinc-800/60">
                    <div className="flex items-center gap-1">
                      <button
                        onClick={() => setPowerModalConfig({ open: true, action: 'On', label: 'Power On' })}
                        disabled={bmcPowerMutation.isPending}
                        className="px-2 py-0.5 rounded text-[10px] font-semibold bg-emerald-950/40 text-emerald-400 border border-emerald-800/50 hover:bg-emerald-900/60 transition-colors cursor-pointer"
                        title="Power On Hardware"
                      >
                        On
                      </button>
                      <button
                        onClick={() => setPowerModalConfig({ open: true, action: 'PowerCycle', label: 'Cold Power Cycle' })}
                        disabled={bmcPowerMutation.isPending}
                        className="px-2 py-0.5 rounded text-[10px] font-semibold bg-sky-950/40 text-sky-400 border border-sky-800/50 hover:bg-sky-900/60 transition-colors cursor-pointer"
                        title="Cold Hardware Power Cycle"
                      >
                        Cycle
                      </button>
                      <button
                        onClick={() => setPowerModalConfig({ open: true, action: 'ForceOff', label: 'Immediate Force Off' })}
                        disabled={bmcPowerMutation.isPending}
                        className="px-2 py-0.5 rounded text-[10px] font-semibold bg-red-950/40 text-red-400 border border-red-800/50 hover:bg-red-900/60 transition-colors cursor-pointer"
                        title="Force Off (Hard Power Cut)"
                      >
                        Off
                      </button>
                    </div>

                    <div className="flex items-center gap-1">
                      <button
                        type="button"
                        onClick={() => setFanModalOpen(true)}
                        className="px-2 py-0.5 rounded text-[10px] font-medium bg-sky-950/30 text-sky-300 border border-sky-800/40 hover:bg-sky-900/50 transition-colors flex items-center gap-1 cursor-pointer"
                        title="Adjust server fan curve / manual duty cycle"
                      >
                        <Fan className="h-2.5 w-2.5 text-sky-400" />
                        Fans
                      </button>
                      <button
                        type="button"
                        onClick={handleBlinkLed}
                        disabled={isBlinking || bmcIdentifyMutation.isPending}
                        className="px-2 py-0.5 rounded text-[10px] font-medium bg-amber-950/30 text-amber-300 border border-amber-800/40 hover:bg-amber-900/50 transition-colors flex items-center gap-1 disabled:opacity-50 cursor-pointer"
                        title="Blink Chassis Locator LED for 15s"
                      >
                        <Radio className="h-2.5 w-2.5 text-amber-400" />
                        LED
                      </button>
                    </div>
                  </div>
                </div>
              ) : (
                <span className="text-xs text-zinc-500">Not configured</span>
              )}
            </div>

            {/* UniFi Port */}
            <div className="p-3 bg-zinc-950/50 border border-zinc-800/80 rounded-lg">
              <div className="flex items-center gap-1.5 text-xs font-medium text-emerald-300 mb-1.5">
                <Network className="h-3.5 w-3.5" />
                UniFi Switch Port
              </div>
              {host.networkPort ? (
                <div className="text-xs text-zinc-300 space-y-0.5">
                  <div className="truncate">MAC: <span className="font-mono text-zinc-100">{host.networkPort.switchMac}</span></div>
                  <div>Port: <span className="font-mono text-zinc-100">#{host.networkPort.portNumber}</span></div>
                </div>
              ) : (
                <span className="text-xs text-zinc-500">Not mapped</span>
              )}
            </div>
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
                <div key={vm.hostId} className="p-2.5 flex items-center justify-between text-xs">
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
                    <span className={`px-2 py-0.5 rounded text-[10px] font-semibold ${
                      vm.isOnline ? 'bg-emerald-950/50 text-emerald-400 border border-emerald-800/50' : 'bg-zinc-800 text-zinc-400'
                    }`}>
                      {vm.isOnline ? 'Online' : 'Offline'}
                    </span>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Timestamps */}
        <div className="flex items-center justify-between text-xs text-zinc-500 pt-2 border-t border-zinc-800/60">
          <div className="flex items-center gap-1.5">
            <Calendar className="h-3.5 w-3.5" />
            Created: {new Date(host.createdAt).toLocaleString()}
          </div>
          <div>
            Updated: {new Date(host.updatedAt).toLocaleString()}
          </div>
        </div>
      </DialogBody>

      <DialogFooter>
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
          <Button
            variant="outline"
            size="sm"
            className="gap-1.5 border-amber-600/40 text-amber-300 hover:bg-amber-950/40 hover:text-amber-200"
            onClick={() => {
              onClose()
              onReboot(host)
            }}
          >
            <RotateCcw className="h-3.5 w-3.5 text-amber-400" />
            Reboot Node
          </Button>
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
      </DialogFooter>
    </Dialog>

    {/* Power Confirmation Dialog Modal */}
    {powerModalConfig && (
      <ConfirmBmcPowerModal
        open={powerModalConfig.open}
        onClose={() => setPowerModalConfig(null)}
        targetName={linkedBmcInstance?.name || host.hostname}
        targetIp={host.idrac?.ipAddress || linkedBmcInstance?.hostnameOrIp || linkedBmcInstance?.bmcUrl || undefined}
        action={powerModalConfig.action}
        actionLabel={powerModalConfig.label}
        onConfirm={async () => {
          await bmcPowerMutation.mutateAsync({
            hostId: host.id,
            idracIp: host.idrac?.ipAddress || linkedBmcInstance?.hostnameOrIp || linkedBmcInstance?.bmcUrl || undefined,
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
