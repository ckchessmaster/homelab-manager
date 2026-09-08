import React, { useState, useEffect } from 'react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogBody,
  DialogFooter,
} from '../../../components/ui/dialog'
import { Button } from '../../../components/ui/button'
import { Input } from '../../../components/ui/input'
import { Badge } from '../../../components/ui/badge'
import {
  Server,
  ShieldCheck,
  CheckCircle2,
  XCircle,
  Radio,
  Cpu,
  Save,
  AlertCircle,
} from 'lucide-react'
import { useSaveProxmoxInstance, useProbeProxmox } from '../useAdapters'
import type { ProxmoxInstanceDto } from '../../../api/adapters'
import type { ProxmoxProbeResult } from '../../../api/hosts'

interface AddProxmoxModalProps {
  open: boolean
  onClose: () => void
  initialInstance?: ProxmoxInstanceDto | null
}

export function AddProxmoxModal({
  open,
  onClose,
  initialInstance,
}: AddProxmoxModalProps) {
  const isEditing = Boolean(initialInstance)
  const saveMutation = useSaveProxmoxInstance()
  const probeMutation = useProbeProxmox()

  const [name, setName] = useState('')
  const [id, setId] = useState('')
  const [baseUrl, setBaseUrl] = useState('')
  const [apiTokenId, setApiTokenId] = useState('')
  const [apiTokenSecret, setApiTokenSecret] = useState('')
  const [allowSelfSignedCert, setAllowSelfSignedCert] = useState(true)
  const [taskPollTimeoutSeconds, setTaskPollTimeoutSeconds] = useState(300)
  const [showAdvanced, setShowAdvanced] = useState(false)

  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [probeResult, setProbeResult] = useState<ProxmoxProbeResult | null>(null)

  useEffect(() => {
    if (initialInstance) {
      setName(initialInstance.name || '')
      setId(initialInstance.id || '')
      setBaseUrl(initialInstance.baseUrl || '')
      setApiTokenId(initialInstance.apiTokenId || '')
      setApiTokenSecret('')
      setAllowSelfSignedCert(initialInstance.allowSelfSignedCert ?? true)
      setTaskPollTimeoutSeconds(initialInstance.taskPollTimeoutSeconds || 300)
    } else {
      setName('')
      setId('')
      setBaseUrl('')
      setApiTokenId('')
      setApiTokenSecret('')
      setAllowSelfSignedCert(true)
      setTaskPollTimeoutSeconds(300)
    }
    setErrorMessage(null)
    setProbeResult(null)
  }, [initialInstance, open])

  const handleTestConnection = async () => {
    setErrorMessage(null)
    setProbeResult(null)

    if (!baseUrl.trim() || !apiTokenId.trim()) {
      setErrorMessage('Base URL and API Token ID are required to test connection.')
      return
    }

    try {
      const res = await probeMutation.mutateAsync({
        baseUrl: baseUrl.trim(),
        apiTokenId: apiTokenId.trim(),
        apiTokenSecret: apiTokenSecret.trim() || (isEditing && initialInstance?.hasSecret ? '••••••••' : ''),
        allowSelfSignedCert,
      })
      setProbeResult(res)
    } catch (err) {
      setProbeResult({
        success: false,
        errorMessage: err instanceof Error ? err.message : 'Failed to probe Proxmox endpoint.',
      })
    }
  }

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    if (!name.trim()) {
      setErrorMessage('Instance name is required.')
      return
    }
    if (!baseUrl.trim() || !apiTokenId.trim()) {
      setErrorMessage('Base URL and API Token ID are required.')
      return
    }

    try {
      await saveMutation.mutateAsync({
        id: id.trim() || undefined,
        name: name.trim(),
        baseUrl: baseUrl.trim(),
        apiTokenId: apiTokenId.trim(),
        apiTokenSecret: apiTokenSecret.trim() || undefined,
        allowSelfSignedCert,
        taskPollTimeoutSeconds,
      })
      onClose()
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : 'Failed to save Proxmox instance.')
    }
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="lg">
      <DialogHeader onClose={onClose}>
        <div className="flex items-center gap-2">
          <div className="p-1.5 bg-orange-500/10 border border-orange-500/20 rounded-lg text-orange-400">
            <Server className="h-5 w-5" />
          </div>
          <div>
            <DialogTitle>
              {isEditing ? `Edit Proxmox Instance: ${initialInstance?.name}` : 'Add Proxmox VE Instance'}
            </DialogTitle>
            <DialogDescription>
              Configure API credentials and snapshot management for this hypervisor cluster.
            </DialogDescription>
          </div>
        </div>
      </DialogHeader>

      <form onSubmit={handleSave} className="flex flex-col flex-1 overflow-hidden">
        <DialogBody className="space-y-4 overflow-y-auto max-h-[65vh] p-6">
          {errorMessage && (
            <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-lg flex items-center gap-2 text-rose-400 text-xs animate-in fade-in">
              <AlertCircle className="h-4 w-4 shrink-0" />
              <span>{errorMessage}</span>
            </div>
          )}

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <Input
              label="Display Name"
              required
              placeholder="e.g. Production Cluster"
              value={name}
              onChange={(e) => setName(e.target.value)}
            />

            <Input
              label="Instance ID (Optional Slug)"
              placeholder="e.g. pve-prod (auto-generated if blank)"
              value={id}
              onChange={(e) => setId(e.target.value)}
              disabled={isEditing}
            />
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <Input
              label="Proxmox Base URL"
              required
              placeholder="https://192.168.1.10:8006"
              value={baseUrl}
              onChange={(e) => setBaseUrl(e.target.value)}
            />

            <Input
              label="API Token ID"
              required
              placeholder="user@pam!tokenid"
              value={apiTokenId}
              onChange={(e) => setApiTokenId(e.target.value)}
            />
          </div>

          <div className="space-y-1">
            <div className="flex items-center justify-between">
              <label className="text-xs font-medium text-zinc-300">
                API Token Secret {isEditing && initialInstance?.hasSecret && '(Configured)'}
              </label>
              {isEditing && initialInstance?.hasSecret && (
                <span className="text-[11px] text-zinc-500 flex items-center gap-1">
                  <ShieldCheck className="h-3 w-3 text-emerald-400" />
                  Encrypted & stored (leave blank to keep current)
                </span>
              )}
            </div>
            <Input
              type="password"
              placeholder={isEditing && initialInstance?.hasSecret ? '••••••••' : 'Secret UUID token'}
              value={apiTokenSecret}
              onChange={(e) => setApiTokenSecret(e.target.value)}
            />
          </div>

          {/* SSL Certificate & Security Checkbox */}
          <div className="flex items-center gap-3 p-3 bg-zinc-950/40 border border-zinc-800 rounded-lg">
            <input
              type="checkbox"
              id="allowSelfSignedCertModal"
              checked={allowSelfSignedCert}
              onChange={(e) => setAllowSelfSignedCert(e.target.checked)}
              className="h-4 w-4 rounded bg-zinc-900 border-zinc-700 text-emerald-500 focus:ring-emerald-500/20 focus:ring-offset-0"
            />
            <label htmlFor="allowSelfSignedCertModal" className="text-xs text-zinc-300 cursor-pointer">
              <span className="font-medium text-zinc-200">Allow Self-Signed Certificates</span>
              <p className="text-zinc-500 text-[11px] mt-0.5">
                Disable strict TLS verification for homelab self-signed certificates.
              </p>
            </label>
          </div>

          {/* Advanced Collapsible */}
          <div>
            <button
              type="button"
              onClick={() => setShowAdvanced(!showAdvanced)}
              className="text-xs font-medium text-zinc-400 hover:text-zinc-200 transition-colors"
            >
              {showAdvanced ? '− Hide Advanced Settings' : '+ Show Advanced Settings (Task Polling)'}
            </button>
            {showAdvanced && (
              <div className="mt-3 p-3 bg-zinc-950/40 border border-zinc-800 rounded-lg grid grid-cols-1 md:grid-cols-2 gap-4 animate-in fade-in">
                <Input
                  label="Task Timeout (Seconds)"
                  type="number"
                  value={taskPollTimeoutSeconds}
                  onChange={(e) => setTaskPollTimeoutSeconds(parseInt(e.target.value) || 300)}
                />
              </div>
            )}
          </div>

          {/* Test Connection Inline Section */}
          <div className="pt-3 border-t border-zinc-800/80">
            <div className="flex items-center justify-between mb-3">
              <span className="text-xs font-semibold text-zinc-300">Connectivity Pre-flight Check</span>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={handleTestConnection}
                isLoading={probeMutation.isPending}
                className="text-xs"
              >
                <Radio className="h-3.5 w-3.5 mr-1.5 text-orange-400" />
                Test Connection
              </Button>
            </div>

            {probeResult && (
              <div
                className={`p-3 rounded-lg border text-xs animate-in fade-in ${
                  probeResult.success
                    ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-300'
                    : 'bg-rose-500/10 border-rose-500/20 text-rose-300'
                }`}
              >
                <div className="flex items-center gap-2">
                  {probeResult.success ? (
                    <CheckCircle2 className="h-4 w-4 text-emerald-400 shrink-0" />
                  ) : (
                    <XCircle className="h-4 w-4 text-rose-400 shrink-0" />
                  )}
                  <span className="font-semibold">
                    {probeResult.success ? 'Connection Succeeded' : 'Connection Failed'}
                  </span>
                  {probeResult.version && (
                    <Badge variant="success" className="ml-auto">
                      PVE {probeResult.version}
                    </Badge>
                  )}
                </div>

                {probeResult.success && probeResult.nodes && probeResult.nodes.length > 0 && (
                  <div className="mt-2 text-[11px] text-zinc-400 flex items-center gap-2">
                    <Cpu className="h-3.5 w-3.5 text-zinc-500" />
                    <span>
                      Discovered {probeResult.nodes.length} cluster nodes: {probeResult.nodes.map((n) => n.node).join(', ')}
                    </span>
                  </div>
                )}

                {probeResult.errorMessage && (
                  <p className="mt-1 text-[11px] text-rose-400/90">{probeResult.errorMessage}</p>
                )}
              </div>
            )}
          </div>
        </DialogBody>

        <DialogFooter className="px-6 py-4 bg-zinc-900/80 border-t border-zinc-800 flex justify-end gap-2">
          <Button type="button" variant="outline" size="sm" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            variant="primary"
            size="sm"
            isLoading={saveMutation.isPending}
            className="bg-emerald-600 hover:bg-emerald-500 text-white"
          >
            <Save className="h-4 w-4 mr-1.5" />
            {isEditing ? 'Update Instance' : 'Save Instance'}
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  )
}
