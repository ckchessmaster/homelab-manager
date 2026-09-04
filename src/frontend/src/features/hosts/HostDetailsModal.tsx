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
import { Server, Shield, Network, Calendar, Copy, Check, Pencil } from 'lucide-react'
import { useState } from 'react'
import type { Host } from '../../api/hosts'

interface HostDetailsModalProps {
  host: Host | null
  open: boolean
  onClose: () => void
  onEdit?: (host: Host) => void
}

export function HostDetailsModal({ host, open, onClose, onEdit }: HostDetailsModalProps) {
  const [copied, setCopied] = useState(false)

  if (!host) return null

  const handleCopyIp = () => {
    navigator.clipboard.writeText(host.ipAddress)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  return (
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

          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
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
                </div>
              ) : (
                <span className="text-xs text-zinc-500">Not linked</span>
              )}
            </div>

            {/* iDRAC */}
            <div className="p-3 bg-zinc-950/50 border border-zinc-800/80 rounded-lg">
              <div className="flex items-center gap-1.5 text-xs font-medium text-amber-300 mb-1.5">
                <Shield className="h-3.5 w-3.5" />
                Dell iDRAC / BMC
              </div>
              {host.idrac?.ipAddress ? (
                <div className="text-xs text-zinc-300">
                  IP: <span className="font-mono text-zinc-100">{host.idrac.ipAddress}</span>
                </div>
              ) : (
                <span className="text-xs text-zinc-500">Not configured</span>
              )}
            </div>

            {/* UniFi Port */}
            <div className="p-3 bg-zinc-950/50 border border-zinc-800/80 rounded-lg">
              <div className="flex items-center gap-1.5 text-xs font-medium text-sky-300 mb-1.5">
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
  )
}
