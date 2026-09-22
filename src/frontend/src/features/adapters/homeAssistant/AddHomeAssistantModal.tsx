import { useState, useEffect } from 'react'
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
  Home,
  CheckCircle2,
  XCircle,
  Radio,
  Save,
  AlertCircle,
  Key,
  Lock,
  Eye,
  EyeOff,
  Server,
} from 'lucide-react'
import { useSaveHomeAssistantInstance, useTestHomeAssistantConnection } from './useHomeAssistant'
import type { HomeAssistantInstanceDto, HomeAssistantTestResult } from '../../../api/homeAssistant'

interface AddHomeAssistantModalProps {
  open: boolean
  onClose: () => void
  initialInstance?: HomeAssistantInstanceDto | null
}

export function AddHomeAssistantModal({
  open,
  onClose,
  initialInstance,
}: AddHomeAssistantModalProps) {
  const isEditing = Boolean(initialInstance)
  const saveMutation = useSaveHomeAssistantInstance()
  const testMutation = useTestHomeAssistantConnection()

  const [name, setName] = useState('')
  const [id, setId] = useState('')
  const [baseUrl, setBaseUrl] = useState('')
  const [token, setToken] = useState('')
  const [showToken, setShowToken] = useState(false)
  const [allowSelfSignedCert, setAllowSelfSignedCert] = useState(true)

  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [testResult, setTestResult] = useState<HomeAssistantTestResult | null>(null)

  useEffect(() => {
    if (initialInstance) {
      setName(initialInstance.name || '')
      setId(initialInstance.id || '')
      setBaseUrl(initialInstance.baseUrl || '')
      setToken('')
      setAllowSelfSignedCert(initialInstance.allowSelfSignedCert ?? true)
    } else {
      setName('')
      setId('')
      setBaseUrl('')
      setToken('')
      setAllowSelfSignedCert(true)
    }
    setErrorMessage(null)
    setTestResult(null)
  }, [initialInstance, open])

  const handleTestConnection = async () => {
    setErrorMessage(null)
    setTestResult(null)

    if (!baseUrl.trim()) {
      setErrorMessage('Please provide a Home Assistant Base URL before testing.')
      return
    }

    if (!isEditing && !token.trim()) {
      setErrorMessage('Please provide a Long-Lived Access Token before testing.')
      return
    }

    try {
      const result = await testMutation.mutateAsync({
        id: id.trim() || undefined,
        name: name.trim() || 'Preflight Test',
        baseUrl: baseUrl.trim(),
        token: token.trim() || null,
        allowSelfSignedCert,
      })
      setTestResult(result)
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Connection test failed.'
      setErrorMessage(message)
    }
  }

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    if (!name.trim()) {
      setErrorMessage('Instance name is required.')
      return
    }

    if (!baseUrl.trim()) {
      setErrorMessage('Base URL is required.')
      return
    }

    if (!isEditing && !token.trim()) {
      setErrorMessage('Long-Lived Access Token is required.')
      return
    }

    try {
      await saveMutation.mutateAsync({
        id: isEditing ? initialInstance?.id : id.trim() || undefined,
        name: name.trim(),
        baseUrl: baseUrl.trim(),
        token: token.trim() || (isEditing ? null : ''),
        allowSelfSignedCert,
      })
      onClose()
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to save Home Assistant instance.'
      setErrorMessage(message)
    }
  }

  return (
    <Dialog open={open} onClose={onClose}>
      <form onSubmit={handleSave} className="flex flex-col h-full">
        <DialogHeader onClose={onClose}>
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
              <Home className="h-5 w-5" />
            </div>
            <div>
              <DialogTitle>
                {isEditing ? `Edit Home Assistant: ${initialInstance?.name}` : 'Connect Home Assistant Appliance'}
              </DialogTitle>
              <DialogDescription>
                Agentless management for Home Assistant OS (HAOS), Supervised, or Container appliances via Supervisor API
              </DialogDescription>
            </div>
          </div>
        </DialogHeader>

        <DialogBody className="space-y-4">
          {errorMessage && (
            <div className="p-3 bg-rose-950/40 border border-rose-800/60 rounded-xl text-xs text-rose-300 flex items-start gap-2">
              <AlertCircle className="h-4 w-4 text-rose-400 shrink-0 mt-0.5" />
              <p className="font-mono">{errorMessage}</p>
            </div>
          )}

          {/* Form Fields */}
          <div className="space-y-3.5">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-3.5">
              <div>
                <label className="block text-xs font-medium text-zinc-300 mb-1">
                  Instance Name <span className="text-rose-400">*</span>
                </label>
                <Input
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="e.g. Home Assistant Main"
                  required
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-zinc-300 mb-1">
                  Unique Instance ID <span className="text-zinc-500 text-[11px]">(Optional)</span>
                </label>
                <Input
                  value={id}
                  onChange={(e) => setId(e.target.value)}
                  placeholder="e.g. ha-main"
                  disabled={isEditing}
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-medium text-zinc-300 mb-1">
                Base URL <span className="text-rose-400">*</span>
              </label>
              <div className="relative">
                <Input
                  value={baseUrl}
                  onChange={(e) => setBaseUrl(e.target.value)}
                  placeholder="http://192.168.1.50:8123 or https://ha.home.arpa"
                  className="pl-9 font-mono text-xs"
                  required
                />
                <Server className="h-4 w-4 text-zinc-500 absolute left-3 top-2.5" />
              </div>
              <p className="text-[11px] text-zinc-500 mt-1">
                Standard Home Assistant default port is <span className="font-mono text-zinc-400">:8123</span>.
              </p>
            </div>

            <div>
              <label className="block text-xs font-medium text-zinc-300 mb-1">
                Long-Lived Access Token {isEditing ? <span className="text-zinc-500 text-[11px]">(Leave blank to retain existing)</span> : <span className="text-rose-400">*</span>}
              </label>
              <div className="relative">
                <Input
                  type={showToken ? 'text' : 'password'}
                  value={token}
                  onChange={(e) => setToken(e.target.value)}
                  placeholder={isEditing ? '••••••••••••••••••••••••' : 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...'}
                  className="pl-9 pr-9 font-mono text-xs"
                  required={!isEditing}
                />
                <Key className="h-4 w-4 text-zinc-500 absolute left-3 top-2.5" />
                <button
                  type="button"
                  onClick={() => setShowToken(!showToken)}
                  className="absolute right-3 top-2.5 text-zinc-500 hover:text-zinc-300 transition-colors"
                >
                  {showToken ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                </button>
              </div>
              <p className="text-[11px] text-zinc-500 mt-1">
                Generate in Home Assistant under <span className="text-zinc-400 font-medium">User Profile &rarr; Long-Lived Access Tokens</span>. Encrypted at rest via AES-256-GCM.
              </p>
            </div>

            {/* TLS Certificate Option */}
            <div className="pt-2">
              <label className="flex items-center gap-2 cursor-pointer text-xs text-zinc-300 select-none">
                <input
                  type="checkbox"
                  checked={allowSelfSignedCert}
                  onChange={(e) => setAllowSelfSignedCert(e.target.checked)}
                  className="rounded border-zinc-700 bg-zinc-900 text-emerald-500 focus:ring-emerald-500/20"
                />
                <span>Allow self-signed or internal CA SSL certificates (Recommended for homelabs)</span>
              </label>
            </div>
          </div>

          {/* Pre-flight Test Connection Button & Result */}
          <div className="pt-2 border-t border-zinc-800/80 space-y-3">
            <div className="flex items-center justify-between">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={handleTestConnection}
                disabled={testMutation.isPending || !baseUrl.trim()}
                className="gap-2 text-xs border-zinc-700 text-zinc-300 hover:bg-zinc-800"
              >
                <Radio className={`h-3.5 w-3.5 ${testMutation.isPending ? 'animate-spin text-emerald-400' : 'text-emerald-400'}`} />
                {testMutation.isPending ? 'Testing Reachability...' : 'Test Connection'}
              </Button>

              {testResult && (
                <Badge
                  variant={testResult.success ? 'success' : 'destructive'}
                  className="text-xs px-2 py-0.5 gap-1.5"
                >
                  {testResult.success ? (
                    <>
                      <CheckCircle2 className="h-3.5 w-3.5" />
                      <span>Connected ({testResult.latencyMs}ms)</span>
                    </>
                  ) : (
                    <>
                      <XCircle className="h-3.5 w-3.5" />
                      <span>Failed</span>
                    </>
                  )}
                </Badge>
              )}
            </div>

            {testResult && (
              <div
                className={`p-3.5 rounded-xl border text-xs space-y-2 ${
                  testResult.success
                    ? 'bg-emerald-950/20 border-emerald-800/40 text-emerald-300'
                    : 'bg-rose-950/30 border-rose-800/50 text-rose-300'
                }`}
              >
                <p className="font-medium flex items-center justify-between">
                  <span>{testResult.message || (testResult.success ? 'Connected successfully.' : 'Failed to connect.')}</span>
                  {testResult.latencyMs > 0 && <span className="font-mono text-[11px] opacity-80">{testResult.latencyMs} ms</span>}
                </p>

                {testResult.success && (
                  <div className="grid grid-cols-2 md:grid-cols-4 gap-2 pt-1 border-t border-emerald-800/30 text-[11px]">
                    <div>
                      <span className="text-zinc-500 block">Core Version</span>
                      <span className="font-mono text-zinc-200 font-semibold">{testResult.coreVersion || 'N/A'}</span>
                    </div>
                    <div>
                      <span className="text-zinc-500 block">Host OS</span>
                      <span className="font-mono text-zinc-200 font-semibold">{testResult.osVersion || 'HAOS'}</span>
                    </div>
                    <div>
                      <span className="text-zinc-500 block">Supervisor</span>
                      <span className="font-mono text-zinc-200 font-semibold">{testResult.supervisorVersion || 'N/A'}</span>
                    </div>
                    <div>
                      <span className="text-zinc-500 block">Hostname</span>
                      <span className="font-mono text-zinc-200 font-semibold">{testResult.hostname || 'homeassistant'}</span>
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        </DialogBody>

        <DialogFooter>
          <div className="flex items-center justify-between w-full">
            <div className="flex items-center gap-1.5 text-[11px] text-zinc-500">
              <Lock className="h-3.5 w-3.5 text-zinc-400" />
              <span>Token encrypted with AES-256-GCM</span>
            </div>

            <div className="flex items-center gap-2">
              <Button type="button" variant="ghost" size="sm" onClick={onClose} disabled={saveMutation.isPending}>
                Cancel
              </Button>
              <Button type="submit" size="sm" disabled={saveMutation.isPending} className="gap-2 bg-emerald-600 hover:bg-emerald-500 text-white">
                <Save className="h-4 w-4" />
                {saveMutation.isPending ? 'Saving...' : isEditing ? 'Save Changes' : 'Connect Appliance'}
              </Button>
            </div>
          </div>
        </DialogFooter>
      </form>
    </Dialog>
  )
}
