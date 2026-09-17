import React, { useState, useEffect } from 'react'
import { Dialog, DialogHeader, DialogTitle, DialogBody, DialogFooter } from '../../components/ui/dialog'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Users, AlertCircle, CheckCircle2, Server, Cpu, Wifi, Shield } from 'lucide-react'
import { useBatchImportCandidates } from './useDiscovery'
import type { DiscoveredCandidate, BatchImportCandidatesPayload } from '../../api/discovery'

interface MassAdoptModalProps {
  candidates: DiscoveredCandidate[]
  open: boolean
  onClose: () => void
  onSuccess?: (succeededCount: number) => void
}

export const MassAdoptModal: React.FC<MassAdoptModalProps> = ({
  candidates,
  open,
  onClose,
  onSuccess,
}) => {
  const [commonTargetType, setCommonTargetType] = useState<string>('auto')
  const [commonOsFamily, setCommonOsFamily] = useState<string>('auto')
  const [ipOverrides, setIpOverrides] = useState<Record<string, string>>({})
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [results, setResults] = useState<{
    succeededCount: number
    failedCount: number
    details: Array<{ name: string; success: boolean; errorMessage?: string | null }>
  } | null>(null)

  const batchImportMutation = useBatchImportCandidates()

  useEffect(() => {
    if (open) {
      setErrorMessage(null)
      setResults(null)
      const initialIps: Record<string, string> = {}
      candidates.forEach((c) => {
        initialIps[c.name] = c.ipAddress || ''
      })
      setIpOverrides(initialIps)
    }
  }, [open])

  const handleIpChange = (name: string, val: string) => {
    setIpOverrides((prev) => ({ ...prev, [name]: val }))
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    // Validate that all candidates have an IP address
    const missingIps = candidates.filter((c) => !ipOverrides[c.name]?.trim())
    if (missingIps.length > 0) {
      setErrorMessage(
        `Please specify an IP address for all hosts. Missing: ${missingIps.map((c) => c.name).join(', ')}`
      )
      return
    }

    const payload: BatchImportCandidatesPayload = {
      candidates: candidates.map((c) => ({
        name: c.name.trim(),
        ipAddress: ipOverrides[c.name].trim(),
        targetType: commonTargetType !== 'auto' ? commonTargetType : c.targetType || 'baremetal',
        osFamily: commonOsFamily !== 'auto' ? commonOsFamily : c.osFamily || 'linux_debian',
        proxmoxNode: c.proxmoxNode || undefined,
        proxmoxVmid: c.proxmoxVmid || undefined,
        k8sNodeName: c.k8sNodeName || undefined,
      })),
      commonTargetType: commonTargetType !== 'auto' ? commonTargetType : undefined,
      commonOsFamily: commonOsFamily !== 'auto' ? commonOsFamily : undefined,
    }

    try {
      const resp = await batchImportMutation.mutateAsync(payload)
      setResults({
        succeededCount: resp.succeededCount,
        failedCount: resp.failedCount,
        details: resp.results,
      })
      onSuccess?.(resp.succeededCount)
    } catch (err: any) {
      setErrorMessage(err.message || 'Failed to complete batch adoption.')
    }
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="lg">
      <DialogHeader onClose={onClose}>
        <div className="flex items-center gap-2">
          <div className="p-1.5 bg-sky-950/60 border border-sky-800/50 rounded-md text-sky-400">
            <Users className="h-4 w-4" />
          </div>
          <DialogTitle>Mass Adopt Discovered Hosts</DialogTitle>
        </div>
      </DialogHeader>

      {results ? (
        <div>
          <DialogBody className="space-y-4">
            <div className="p-4 bg-zinc-950/80 border border-zinc-800 rounded-xl space-y-3">
              <div className="flex items-center gap-3">
                <div className="p-2 bg-emerald-950/60 border border-emerald-800/50 rounded-lg text-emerald-400">
                  <CheckCircle2 className="h-5 w-5" />
                </div>
                <div>
                  <h4 className="text-sm font-semibold text-zinc-100">
                    Batch Adoption Complete
                  </h4>
                  <p className="text-xs text-zinc-400">
                    Successfully adopted {results.succeededCount} of {candidates.length} hosts into inventory.
                  </p>
                </div>
              </div>

              <div className="max-h-60 overflow-y-auto space-y-1.5 pt-2 border-t border-zinc-800/60">
                {results.details.map((item) => (
                  <div
                    key={item.name}
                    className={`flex items-center justify-between p-2 rounded-lg text-xs ${
                      item.success
                        ? 'bg-emerald-950/20 border border-emerald-900/30 text-emerald-300'
                        : 'bg-red-950/20 border border-red-900/30 text-red-300'
                    }`}
                  >
                    <span className="font-mono font-medium">{item.name}</span>
                    <span className="text-[11px]">
                      {item.success ? 'Adopted successfully' : item.errorMessage || 'Failed'}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          </DialogBody>

          <DialogFooter>
            <Button
              variant="primary"
              onClick={onClose}
              className="bg-sky-600 hover:bg-sky-500 text-white"
            >
              Done
            </Button>
          </DialogFooter>
        </div>
      ) : (
        <form onSubmit={handleSubmit}>
          <DialogBody className="space-y-5">
            {errorMessage && (
              <div className="p-3 bg-red-950/40 border border-red-800/60 rounded-lg flex items-center gap-2 text-xs text-red-300">
                <AlertCircle className="h-4 w-4 shrink-0" />
                <span>{errorMessage}</span>
              </div>
            )}

            <div className="p-3.5 bg-zinc-950/50 border border-zinc-800 rounded-lg text-xs space-y-2">
              <div className="flex items-center justify-between text-zinc-300 font-medium">
                <span>Selected Candidates ({candidates.length})</span>
                <span className="text-zinc-500 text-[11px]">Specify IPs if unassigned</span>
              </div>

              <div className="max-h-48 overflow-y-auto space-y-2 pr-1">
                {candidates.map((c) => (
                  <div
                    key={c.name}
                    className="flex items-center justify-between gap-3 p-2 bg-zinc-900/60 border border-zinc-800/80 rounded-md text-xs"
                  >
                    <div className="flex items-center gap-2 min-w-0">
                      {c.source === 'Proxmox' ? (
                        <Server className="h-3.5 w-3.5 text-purple-400 shrink-0" />
                      ) : c.source === 'UniFi' ? (
                        <Wifi className="h-3.5 w-3.5 text-cyan-400 shrink-0" />
                      ) : c.source === 'OPNsense' ? (
                        <Shield className="h-3.5 w-3.5 text-amber-400 shrink-0" />
                      ) : (
                        <Cpu className="h-3.5 w-3.5 text-sky-400 shrink-0" />
                      )}
                      <div className="truncate">
                        <span className="font-medium text-zinc-200">{c.name}</span>
                        <span className="text-[10px] text-zinc-500 ml-1.5">({c.source})</span>
                      </div>
                    </div>

                    <div className="w-48 shrink-0">
                      <Input
                        value={ipOverrides[c.name] ?? ''}
                        onChange={(e) => handleIpChange(c.name, e.target.value)}
                        placeholder="IP Address *"
                        className="h-7 text-xs font-mono"
                        required
                      />
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Shared Options */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-medium text-zinc-300 mb-1">
                  Target Type
                </label>
                <select
                  value={commonTargetType}
                  onChange={(e) => setCommonTargetType(e.target.value)}
                  className="w-full bg-zinc-950 border border-zinc-800 rounded-lg px-3 py-2 text-xs text-zinc-200 focus:outline-none focus:border-sky-500"
                >
                  <option value="auto">Auto-detect from source</option>
                  <option value="kubernetes_node">Kubernetes Node</option>
                  <option value="proxmox_vm">Proxmox QEMU VM</option>
                  <option value="proxmox_lxc">Proxmox LXC Container</option>
                  <option value="baremetal">Baremetal Server</option>
                </select>
              </div>

              <div>
                <label className="block text-xs font-medium text-zinc-300 mb-1">
                  OS Family
                </label>
                <select
                  value={commonOsFamily}
                  onChange={(e) => setCommonOsFamily(e.target.value)}
                  className="w-full bg-zinc-950 border border-zinc-800 rounded-lg px-3 py-2 text-xs text-zinc-200 focus:outline-none focus:border-sky-500"
                >
                  <option value="auto">Auto-detect (recommended)</option>
                  <option value="linux_debian">Debian / Proxmox</option>
                  <option value="linux_ubuntu">Ubuntu LTS</option>
                  <option value="linux_rhel">RHEL / Rocky / Alma</option>
                  <option value="linux_alpine">Alpine Linux</option>
                </select>
              </div>
            </div>

            <p className="text-[11px] text-zinc-400">
              All selected hosts will be batch registered into your active inventory. Their hypervisor / cluster bindings will be preserved automatically.
            </p>
          </DialogBody>

          <DialogFooter>
            <Button
              type="button"
              variant="secondary"
              onClick={onClose}
              disabled={batchImportMutation.isPending}
            >
              Cancel
            </Button>
            <Button
              type="submit"
              variant="primary"
              disabled={batchImportMutation.isPending}
              className="bg-sky-600 hover:bg-sky-500 text-white gap-2 font-medium"
            >
              {batchImportMutation.isPending ? (
                <>
                  <div className="h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                  <span>Adopting Fleet...</span>
                </>
              ) : (
                <>
                  <Users className="h-3.5 w-3.5" />
                  <span>Mass Adopt ({candidates.length} Hosts)</span>
                </>
              )}
            </Button>
          </DialogFooter>
        </form>
      )}
    </Dialog>
  )
}
