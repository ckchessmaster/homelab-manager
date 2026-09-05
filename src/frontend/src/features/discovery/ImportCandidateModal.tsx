import React, { useState, useEffect } from 'react'
import { Dialog, DialogHeader, DialogTitle, DialogBody, DialogFooter } from '../../components/ui/dialog'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { PlusCircle, AlertCircle, Check } from 'lucide-react'
import { useImportCandidate } from './useDiscovery'
import type { DiscoveredCandidate } from '../../api/discovery'

interface ImportCandidateModalProps {
  candidate: DiscoveredCandidate | null
  open: boolean
  onClose: () => void
  onSuccess?: (hostId: string) => void
}

export const ImportCandidateModal: React.FC<ImportCandidateModalProps> = ({
  candidate,
  open,
  onClose,
  onSuccess,
}) => {
  const [name, setName] = useState('')
  const [ipAddress, setIpAddress] = useState('')
  const [friendlyName, setFriendlyName] = useState('')
  const [targetType, setTargetType] = useState('proxmox_vm')
  const [osFamily, setOsFamily] = useState('linux_debian')
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

  const importMutation = useImportCandidate()

  useEffect(() => {
    if (candidate) {
      setName(candidate.name)
      setIpAddress(candidate.ipAddress || '')
      setFriendlyName(candidate.name)
      setTargetType(candidate.targetType || 'proxmox_vm')
      setOsFamily(candidate.osFamily || 'linux_debian')
      setErrorMsg(null)
    }
  }, [candidate])

  if (!candidate) return null

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMsg(null)

    if (!ipAddress.trim()) {
      setErrorMsg('Please specify a valid IP address for the host.')
      return
    }

    try {
      const result = await importMutation.mutateAsync({
        name: name.trim(),
        ipAddress: ipAddress.trim(),
        friendlyName: friendlyName.trim() || undefined,
        targetType,
        osFamily,
        proxmoxNode: candidate.proxmoxNode || undefined,
        proxmoxVmid: candidate.proxmoxVmid || undefined,
        k8sNodeName: candidate.k8sNodeName || undefined,
      })

      if (result.success && result.hostId) {
        onSuccess?.(result.hostId)
        onClose()
      } else {
        setErrorMsg(result.errorMessage || 'Failed to import host.')
      }
    } catch (err: any) {
      setErrorMsg(err.message || 'An unexpected error occurred during import.')
    }
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md">
      <DialogHeader onClose={onClose}>
        <div className="flex items-center gap-2">
          <div className="p-1.5 bg-emerald-950/60 border border-emerald-800/50 rounded-md text-emerald-400">
            <PlusCircle className="h-4 w-4" />
          </div>
          <DialogTitle>Import Discovered Host</DialogTitle>
        </div>
      </DialogHeader>

      <form onSubmit={handleSubmit}>
        <DialogBody className="space-y-4">
          {errorMsg && (
            <div className="p-3 bg-red-950/40 border border-red-800/60 rounded-lg flex items-center gap-2 text-xs text-red-300">
              <AlertCircle className="h-4 w-4 shrink-0" />
              <span>{errorMsg}</span>
            </div>
          )}

          <div className="p-3 bg-zinc-950/50 border border-zinc-800 rounded-lg text-xs space-y-1">
            <div className="flex justify-between text-zinc-400">
              <span>Discovery Source:</span>
              <span className="font-semibold text-zinc-200">{candidate.source}</span>
            </div>
            {candidate.proxmoxNode && (
              <div className="flex justify-between text-zinc-400">
                <span>Proxmox Node / VMID:</span>
                <span className="font-mono text-zinc-200">
                  {candidate.proxmoxNode} / #{candidate.proxmoxVmid}
                </span>
              </div>
            )}
            {candidate.k8sNodeName && (
              <div className="flex justify-between text-zinc-400">
                <span>Kubernetes Node:</span>
                <span className="font-mono text-zinc-200">{candidate.k8sNodeName}</span>
              </div>
            )}
          </div>

          <div>
            <label className="block text-xs font-medium text-zinc-300 mb-1">Hostname *</label>
            <Input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. k8s-worker-01"
              required
            />
          </div>

          <div>
            <label className="block text-xs font-medium text-zinc-300 mb-1">IP Address *</label>
            <Input
              value={ipAddress}
              onChange={(e) => setIpAddress(e.target.value)}
              placeholder="e.g. 192.168.1.150"
              required
            />
            {!candidate.ipAddress && (
              <p className="text-[11px] text-amber-400/90 mt-1 flex items-center gap-1">
                <AlertCircle className="h-3 w-3" />
                No IP address was reported by guest agent. Please confirm the IP manually.
              </p>
            )}
          </div>

          <div>
            <label className="block text-xs font-medium text-zinc-300 mb-1">Friendly Name</label>
            <Input
              value={friendlyName}
              onChange={(e) => setFriendlyName(e.target.value)}
              placeholder="e.g. Primary Kubernetes Worker"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-zinc-300 mb-1">Target Type</label>
              <select
                value={targetType}
                onChange={(e) => setTargetType(e.target.value)}
                className="w-full h-9 bg-zinc-900 border border-zinc-800 rounded-md px-3 text-xs text-zinc-200 focus:outline-none focus:border-zinc-700"
              >
                <option value="proxmox_vm">Proxmox VM (QEMU)</option>
                <option value="proxmox_lxc">Proxmox LXC Container</option>
                <option value="baremetal">Bare-Metal Server</option>
              </select>
            </div>

            <div>
              <label className="block text-xs font-medium text-zinc-300 mb-1">OS Family</label>
              <select
                value={osFamily}
                onChange={(e) => setOsFamily(e.target.value)}
                className="w-full h-9 bg-zinc-900 border border-zinc-800 rounded-md px-3 text-xs text-zinc-200 focus:outline-none focus:border-zinc-700"
              >
                <option value="linux_debian">Debian / Ubuntu (APT)</option>
                <option value="linux_rhel">RHEL / Rocky / Fedora (DNF)</option>
                <option value="windows">Windows Server</option>
              </select>
            </div>
          </div>
        </DialogBody>

        <DialogFooter>
          <Button variant="secondary" size="sm" type="button" onClick={onClose} disabled={importMutation.isPending}>
            Cancel
          </Button>
          <Button
            variant="primary"
            size="sm"
            type="submit"
            disabled={importMutation.isPending}
            className="bg-emerald-600 hover:bg-emerald-500 text-white gap-1.5"
          >
            <Check className="h-3.5 w-3.5" />
            {importMutation.isPending ? 'Importing...' : 'Add to Inventory'}
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  )
}
