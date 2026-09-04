import React, { useState } from 'react'
import {
  Dialog,
  DialogBody,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '../../components/ui/dialog'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { useCreateHost } from './useHosts'
import { ChevronDown, ChevronUp, Server, Shield, Network, AlertCircle } from 'lucide-react'
import type { CreateHostPayload } from '../../api/hosts'

interface AddHostModalProps {
  open: boolean
  onClose: () => void
}

export function AddHostModal({ open, onClose }: AddHostModalProps) {
  const createMutation = useCreateHost()

  const [formData, setFormData] = useState<CreateHostPayload>({
    hostname: '',
    friendlyName: '',
    ipAddress: '',
    osFamily: 'linux_debian',
    targetType: 'baremetal',
    proxmoxNode: '',
    proxmoxVmid: undefined,
    idracIp: '',
    unifiSwitchMac: '',
    unifiSwitchPort: undefined,
  })

  const [showAdvanced, setShowAdvanced] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})

  const handleChange = (
    e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>
  ) => {
    const { name, value } = e.target
    setFormData((prev) => ({
      ...prev,
      [name]:
        name === 'proxmoxVmid' || name === 'unifiSwitchPort'
          ? value === ''
            ? undefined
            : parseInt(value, 10)
          : value,
    }))
    if (fieldErrors[name]) {
      setFieldErrors((prev) => {
        const copy = { ...prev }
        delete copy[name]
        return copy
      })
    }
  }

  const validate = (): boolean => {
    const errors: Record<string, string> = {}
    if (!formData.hostname.trim()) {
      errors.hostname = 'Hostname is required.'
    } else if (!/^[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$/.test(formData.hostname.trim())) {
      errors.hostname = 'Hostname must be valid DNS format (alphanumeric and hyphens).'
    }

    if (!formData.ipAddress.trim()) {
      errors.ipAddress = 'IP Address is required.'
    }

    setFieldErrors(errors)
    return Object.keys(errors).length === 0
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setFormError(null)

    if (!validate()) return

    try {
      await createMutation.mutateAsync(formData)
      onClose()
      // reset form
      setFormData({
        hostname: '',
        friendlyName: '',
        ipAddress: '',
        osFamily: 'linux_debian',
        targetType: 'baremetal',
        proxmoxNode: '',
        proxmoxVmid: undefined,
        idracIp: '',
        unifiSwitchMac: '',
        unifiSwitchPort: undefined,
      })
      setShowAdvanced(false)
    } catch (err: unknown) {
      if (err instanceof Error) {
        setFormError(err.message)
      } else {
        setFormError('An unexpected error occurred.')
      }
    }
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="lg">
      <form onSubmit={handleSubmit}>
        <DialogHeader onClose={onClose}>
          <DialogTitle>Register New Host</DialogTitle>
          <DialogDescription>
            Add a compute node, virtual machine, or bare-metal server to your inventory.
          </DialogDescription>
        </DialogHeader>

        <DialogBody>
          {formError && (
            <div className="flex items-center gap-2 p-3 bg-rose-950/60 border border-rose-800/80 rounded-lg text-rose-300 text-xs">
              <AlertCircle className="h-4 w-4 shrink-0 text-rose-400" />
              <span>{formError}</span>
            </div>
          )}

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <Input
              label="Hostname"
              name="hostname"
              required
              placeholder="e.g. k8s-worker-01"
              value={formData.hostname}
              onChange={handleChange}
              error={fieldErrors.hostname}
            />

            <Input
              label="Friendly Name"
              name="friendlyName"
              placeholder="e.g. Worker GPU 01"
              value={formData.friendlyName || ''}
              onChange={handleChange}
            />
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <Input
              label="IP Address"
              name="ipAddress"
              required
              placeholder="192.168.1.50"
              value={formData.ipAddress}
              onChange={handleChange}
              error={fieldErrors.ipAddress}
            />

            <Select
              label="OS Family"
              name="osFamily"
              required
              value={formData.osFamily}
              onChange={handleChange}
            >
              <option value="linux_debian">Debian GNU/Linux</option>
              <option value="linux_ubuntu">Ubuntu Linux</option>
              <option value="linux_rhel">RHEL / Rocky / Alma</option>
              <option value="linux_arch">Arch Linux</option>
              <option value="linux_alpine">Alpine Linux</option>
              <option value="windows">Windows Server</option>
            </Select>

            <Select
              label="Target Type"
              name="targetType"
              required
              value={formData.targetType}
              onChange={handleChange}
            >
              <option value="baremetal">Bare-Metal Server</option>
              <option value="proxmox_vm">Proxmox Virtual Machine</option>
              <option value="proxmox_lxc">Proxmox LXC Container</option>
            </Select>
          </div>

          {/* Hypervisor & Hardware Linking Accordion */}
          <div className="mt-2 border border-zinc-800/80 rounded-lg overflow-hidden bg-zinc-950/40">
            <button
              type="button"
              onClick={() => setShowAdvanced(!showAdvanced)}
              className="w-full px-4 py-3 flex items-center justify-between text-xs font-semibold text-zinc-300 hover:text-zinc-100 hover:bg-zinc-800/40 transition-colors"
            >
              <span className="flex items-center gap-2">
                <Server className="h-3.5 w-3.5 text-emerald-400" />
                Hypervisor & Hardware Out-of-Band Linking
              </span>
              {showAdvanced ? (
                <ChevronUp className="h-4 w-4 text-zinc-400" />
              ) : (
                <ChevronDown className="h-4 w-4 text-zinc-400" />
              )}
            </button>

            {showAdvanced && (
              <div className="p-4 border-t border-zinc-800/60 space-y-4 text-xs bg-zinc-900/30 animate-in fade-in">
                {/* Proxmox Linking */}
                <div>
                  <div className="flex items-center gap-1.5 font-medium text-purple-300 mb-2">
                    <Server className="h-3.5 w-3.5" />
                    Proxmox VE Correlation
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    <Input
                      label="Proxmox Node"
                      name="proxmoxNode"
                      placeholder="pve-01"
                      value={formData.proxmoxNode || ''}
                      onChange={handleChange}
                    />
                    <Input
                      label="Proxmox VMID"
                      name="proxmoxVmid"
                      type="number"
                      placeholder="100"
                      value={formData.proxmoxVmid ?? ''}
                      onChange={handleChange}
                    />
                  </div>
                </div>

                {/* iDRAC Linking */}
                <div className="pt-2 border-t border-zinc-800/50">
                  <div className="flex items-center gap-1.5 font-medium text-amber-300 mb-2">
                    <Shield className="h-3.5 w-3.5" />
                    Dell iDRAC / BMC Out-of-Band
                  </div>
                  <Input
                    label="iDRAC IP Address"
                    name="idracIp"
                    placeholder="192.168.1.120"
                    value={formData.idracIp || ''}
                    onChange={handleChange}
                  />
                </div>

                {/* UniFi Switch Linking */}
                <div className="pt-2 border-t border-zinc-800/50">
                  <div className="flex items-center gap-1.5 font-medium text-sky-300 mb-2">
                    <Network className="h-3.5 w-3.5" />
                    Ubiquiti UniFi Switch Port
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    <Input
                      label="Switch MAC Address"
                      name="unifiSwitchMac"
                      placeholder="00:11:22:33:44:55"
                      value={formData.unifiSwitchMac || ''}
                      onChange={handleChange}
                    />
                    <Input
                      label="Port Number"
                      name="unifiSwitchPort"
                      type="number"
                      placeholder="8"
                      value={formData.unifiSwitchPort ?? ''}
                      onChange={handleChange}
                    />
                  </div>
                </div>
              </div>
            )}
          </div>
        </DialogBody>

        <DialogFooter>
          <Button type="button" variant="outline" size="sm" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            variant="primary"
            size="sm"
            isLoading={createMutation.isPending}
          >
            Register Host
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  )
}
