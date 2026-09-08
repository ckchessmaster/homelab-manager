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
  Cpu,
  ShieldCheck,
  CheckCircle2,
  XCircle,
  Radio,
  Save,
  AlertCircle,
  User,
  Lock,
  Eye,
  EyeOff,
  Server,
  Network,
} from 'lucide-react'
import { useSaveIdracInstance, useTestIdracConnection } from './useIdrac'
import type { IdracInstanceDto, IdracTestResult } from '../../../api/idrac'

interface AddIdracModalProps {
  open: boolean
  onClose: () => void
  initialInstance?: IdracInstanceDto | null
}

export function AddIdracModal({
  open,
  onClose,
  initialInstance,
}: AddIdracModalProps) {
  const isEditing = Boolean(initialInstance)
  const saveMutation = useSaveIdracInstance()
  const testMutation = useTestIdracConnection()

  const [name, setName] = useState('')
  const [id, setId] = useState('')
  const [bmcUrl, setBmcUrl] = useState('')
  const [username, setUsername] = useState('root')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [hostnameOrIp, setHostnameOrIp] = useState('')
  const [allowSelfSignedCert, setAllowSelfSignedCert] = useState(true)

  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [testResult, setTestResult] = useState<IdracTestResult | null>(null)

  useEffect(() => {
    if (initialInstance) {
      setName(initialInstance.name || '')
      setId(initialInstance.id || '')
      setBmcUrl(initialInstance.bmcUrl || '')
      setUsername(initialInstance.username || 'root')
      setPassword('')
      setHostnameOrIp(initialInstance.hostnameOrIp || '')
      setAllowSelfSignedCert(initialInstance.allowSelfSignedCert ?? true)
    } else {
      setName('')
      setId('')
      setBmcUrl('')
      setUsername('root')
      setPassword('')
      setHostnameOrIp('')
      setAllowSelfSignedCert(true)
    }
    setErrorMessage(null)
    setTestResult(null)
  }, [initialInstance, open])

  const handleTestConnection = async () => {
    setErrorMessage(null)
    setTestResult(null)

    if (!bmcUrl.trim()) {
      setErrorMessage('Please provide a BMC URL before testing.')
      return
    }

    if (!username.trim()) {
      setErrorMessage('Please provide a username before testing.')
      return
    }

    if (!isEditing && !password.trim()) {
      setErrorMessage('Please provide a password before testing.')
      return
    }

    try {
      const result = await testMutation.mutateAsync({
        id: id.trim() || undefined,
        name: name.trim() || 'BMC Preflight Test',
        bmcUrl: bmcUrl.trim(),
        username: username.trim(),
        password: password.trim() || undefined,
        hostnameOrIp: hostnameOrIp.trim() || undefined,
        allowSelfSignedCert,
      })
      setTestResult(result)
    } catch (err: unknown) {
      setErrorMessage(err instanceof Error ? err.message : 'Connection test failed.')
    }
  }

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    if (!name.trim()) {
      setErrorMessage('BMC Name is required.')
      return
    }

    if (!bmcUrl.trim()) {
      setErrorMessage('BMC URL is required.')
      return
    }

    if (!username.trim()) {
      setErrorMessage('Username is required.')
      return
    }

    if (!isEditing && !password.trim()) {
      setErrorMessage('Password is required for new configurations.')
      return
    }

    try {
      await saveMutation.mutateAsync({
        id: id.trim() || undefined,
        name: name.trim(),
        bmcUrl: bmcUrl.trim(),
        username: username.trim(),
        password: password.trim() || undefined,
        hostnameOrIp: hostnameOrIp.trim() || undefined,
        allowSelfSignedCert,
      })
      onClose()
    } catch (err: unknown) {
      setErrorMessage(err instanceof Error ? err.message : 'Failed to save iDRAC configuration.')
    }
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md">
      <DialogHeader onClose={onClose}>
        <div className="flex items-center gap-2.5">
          <div className="p-2 rounded-lg bg-purple-500/10 border border-purple-500/20 text-purple-400">
            <Cpu className="h-5 w-5" />
          </div>
          <div>
            <DialogTitle>
              {isEditing ? 'Configure BMC / iDRAC Endpoint' : 'Connect BMC / iDRAC Endpoint'}
            </DialogTitle>
            <DialogDescription>
              Connect to Dell iDRAC or Redfish BMC for hardware power control and thermal telemetry.
            </DialogDescription>
          </div>
        </div>
      </DialogHeader>

      <form onSubmit={handleSave}>
        <DialogBody className="space-y-4 py-3">
          {errorMessage && (
            <div className="p-3 rounded-lg bg-red-950/40 border border-red-800/60 text-red-300 text-xs flex items-start gap-2.5 animate-in fade-in">
              <AlertCircle className="h-4 w-4 shrink-0 text-red-400 mt-0.5" />
              <div>
                <p className="font-semibold">Configuration Error</p>
                <p className="mt-0.5">{errorMessage}</p>
              </div>
            </div>
          )}

          {/* Test Result Callout */}
          {testResult && (
            <div
              className={`p-3 rounded-lg border text-xs animate-in fade-in ${
                testResult.success
                  ? 'bg-emerald-950/30 border-emerald-800/60 text-emerald-300'
                  : 'bg-red-950/30 border-red-800/60 text-red-300'
              }`}
            >
              <div className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-2 font-medium">
                  {testResult.success ? (
                    <CheckCircle2 className="h-4 w-4 text-emerald-400 shrink-0" />
                  ) : (
                    <XCircle className="h-4 w-4 text-red-400 shrink-0" />
                  )}
                  <span>
                    {testResult.success ? 'BMC Connection Successful' : 'Connection Failed'}
                  </span>
                </div>
                <Badge variant={testResult.success ? 'success' : 'destructive'} className="text-[10px]">
                  {testResult.latencyMs}ms
                </Badge>
              </div>

              {testResult.success ? (
                <div className="mt-2 grid grid-cols-2 gap-2 text-[11px] text-zinc-300 bg-zinc-950/40 p-2 rounded border border-zinc-800/60">
                  <div>
                    <span className="text-zinc-500">Model:</span>{' '}
                    <span className="font-semibold text-zinc-200">{testResult.model || 'Unknown'}</span>
                  </div>
                  <div>
                    <span className="text-zinc-500">Power State:</span>{' '}
                    <Badge variant={testResult.powerState?.toLowerCase() === 'on' ? 'success' : 'destructive'} className="text-[10px] ml-1">
                      {testResult.powerState || 'Unknown'}
                    </Badge>
                  </div>
                  <div>
                    <span className="text-zinc-500">BIOS:</span>{' '}
                    <span className="font-mono text-zinc-200">{testResult.biosVersion || 'N/A'}</span>
                  </div>
                  <div>
                    <span className="text-zinc-500">Service Tag / Serial:</span>{' '}
                    <span className="font-mono text-zinc-200">{testResult.serialNumber || 'N/A'}</span>
                  </div>
                </div>
              ) : (
                <p className="mt-1.5 text-red-400 font-mono text-[11px] break-all">
                  {testResult.message || 'Unable to reach Redfish BMC endpoint.'}
                </p>
              )}
            </div>
          )}

          {/* Identity Fields */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <label className="text-xs font-medium text-zinc-300 flex items-center gap-1.5">
                <Server className="h-3.5 w-3.5 text-zinc-400" />
                Server / BMC Name
              </label>
              <Input
                placeholder="e.g. Dell PowerEdge R730xd"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
              />
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-medium text-zinc-300">
                Instance Identifier (Slug)
              </label>
              <Input
                placeholder="Auto-generated if blank"
                value={id}
                onChange={(e) => setId(e.target.value)}
                disabled={isEditing}
                className="font-mono text-xs"
              />
            </div>
          </div>

          {/* BMC URL */}
          <div className="space-y-1.5">
            <label className="text-xs font-medium text-zinc-300 flex items-center justify-between">
              <span>BMC Endpoint URL</span>
              <span className="text-[10px] text-zinc-500 font-mono">https://&lt;idrac-ip&gt;</span>
            </label>
            <Input
              placeholder="https://192.168.1.120"
              value={bmcUrl}
              onChange={(e) => setBmcUrl(e.target.value)}
              className="font-mono text-xs"
              required
            />
          </div>

          {/* Associated Host IP / Correlated Host */}
          <div className="space-y-1.5">
            <label className="text-xs font-medium text-zinc-300 flex items-center justify-between">
              <span className="flex items-center gap-1.5">
                <Network className="h-3.5 w-3.5 text-zinc-400" />
                Linked Host IP / BMC IP
              </span>
              <span className="text-[10px] text-zinc-500">Links to Inventory</span>
            </label>
            <Input
              placeholder="192.168.1.120 (for Host matching)"
              value={hostnameOrIp}
              onChange={(e) => setHostnameOrIp(e.target.value)}
              className="font-mono text-xs"
            />
          </div>

          {/* Credentials */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3 pt-2 border-t border-zinc-800/80">
            <div className="space-y-1.5">
              <label className="text-xs font-medium text-zinc-300 flex items-center gap-1.5">
                <User className="h-3.5 w-3.5 text-zinc-400" />
                BMC Username
              </label>
              <Input
                placeholder="root"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                className="font-mono text-xs"
                required
              />
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-medium text-zinc-300 flex items-center justify-between">
                <span className="flex items-center gap-1.5">
                  <Lock className="h-3.5 w-3.5 text-zinc-400" />
                  Password
                </span>
                {isEditing && (
                  <span className="text-[10px] text-zinc-500">Keep existing</span>
                )}
              </label>
              <div className="relative">
                <Input
                  type={showPassword ? 'text' : 'password'}
                  placeholder={isEditing ? '••••••••••••••••' : 'Enter password...'}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="font-mono text-xs pr-9"
                  required={!isEditing}
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute right-2.5 top-1/2 -translate-y-1/2 text-zinc-500 hover:text-zinc-300"
                >
                  {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                </button>
              </div>
            </div>
          </div>

          {/* TLS Options */}
          <div className="pt-2 border-t border-zinc-800/80">
            <label className="flex items-center gap-2.5 cursor-pointer text-xs text-zinc-300">
              <input
                type="checkbox"
                checked={allowSelfSignedCert}
                onChange={(e) => setAllowSelfSignedCert(e.target.checked)}
                className="rounded bg-zinc-900 border-zinc-700 text-purple-500 focus:ring-purple-500/20"
              />
              <span className="flex items-center gap-1.5">
                <ShieldCheck className="h-4 w-4 text-purple-400" />
                Allow Self-Signed TLS Certificates (Common for out-of-band BMCs)
              </span>
            </label>
          </div>
        </DialogBody>

        <DialogFooter className="flex items-center justify-between sm:justify-between w-full">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={handleTestConnection}
            disabled={testMutation.isPending || !bmcUrl.trim() || !username.trim()}
            className="gap-1.5 border-purple-500/30 hover:bg-purple-950/30 text-purple-300"
          >
            <Radio className={`h-3.5 w-3.5 ${testMutation.isPending ? 'animate-pulse text-purple-400' : ''}`} />
            {testMutation.isPending ? 'Testing...' : 'Test Preflight'}
          </Button>

          <div className="flex items-center gap-2">
            <Button type="button" variant="ghost" size="sm" onClick={onClose}>
              Cancel
            </Button>
            <Button
              type="submit"
              variant="primary"
              size="sm"
              disabled={saveMutation.isPending}
              className="gap-1.5 bg-purple-600 hover:bg-purple-500 text-white font-semibold"
            >
              <Save className="h-3.5 w-3.5" />
              {saveMutation.isPending ? 'Saving...' : isEditing ? 'Save Changes' : 'Connect BMC'}
            </Button>
          </div>
        </DialogFooter>
      </form>
    </Dialog>
  )
}
