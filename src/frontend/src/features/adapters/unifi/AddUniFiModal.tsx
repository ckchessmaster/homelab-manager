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
  Wifi,
  ShieldCheck,
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
import { useSaveUniFiInstance, useTestUniFiPreflight } from './useUniFi'
import type { UniFiInstanceDto, UniFiTestResult } from '../../../api/unifi'

interface AddUniFiModalProps {
  open: boolean
  onClose: () => void
  initialInstance?: UniFiInstanceDto | null
}

export function AddUniFiModal({
  open,
  onClose,
  initialInstance,
}: AddUniFiModalProps) {
  const isEditing = Boolean(initialInstance)
  const saveMutation = useSaveUniFiInstance()
  const testMutation = useTestUniFiPreflight()

  const [authType, setAuthType] = useState<'api_key' | 'credentials'>('api_key')
  const [name, setName] = useState('')
  const [id, setId] = useState('')
  const [controllerUrl, setControllerUrl] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [showApiKey, setShowApiKey] = useState(false)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [site, setSite] = useState('default')
  const [allowSelfSignedCert, setAllowSelfSignedCert] = useState(true)

  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [testResult, setTestResult] = useState<UniFiTestResult | null>(null)

  useEffect(() => {
    if (initialInstance) {
      setName(initialInstance.name || '')
      setId(initialInstance.id || '')
      setControllerUrl(initialInstance.controllerUrl || '')
      const isLegacy = initialInstance.authType === 'credentials' || (initialInstance.hasPassword && !initialInstance.hasApiKey)
      setAuthType(isLegacy ? 'credentials' : 'api_key')
      setApiKey('')
      setUsername(initialInstance.username || '')
      setPassword('')
      setSite(initialInstance.site || 'default')
      setAllowSelfSignedCert(initialInstance.allowSelfSignedCert ?? true)
    } else {
      setName('')
      setId('')
      setControllerUrl('')
      setAuthType('api_key')
      setApiKey('')
      setUsername('')
      setPassword('')
      setSite('default')
      setAllowSelfSignedCert(true)
    }
    setErrorMessage(null)
    setTestResult(null)
  }, [initialInstance, open])

  const handleTestConnection = async () => {
    if (!controllerUrl.trim()) {
      setErrorMessage('Controller URL is required to test connection.')
      return
    }

    if (authType === 'api_key' && !apiKey.trim() && (!isEditing || !initialInstance?.hasApiKey)) {
      setErrorMessage('API Key is required to test connection with UniFi OS Server.')
      return
    }

    if (authType === 'credentials' && (!username.trim() || (!password.trim() && (!isEditing || !initialInstance?.hasPassword)))) {
      setErrorMessage('Username and Password are required to test connection.')
      return
    }

    setErrorMessage(null)
    setTestResult(null)

    try {
      const result = await testMutation.mutateAsync({
        controllerUrl: controllerUrl.trim(),
        authType,
        apiKey: apiKey.trim() || (isEditing && initialInstance?.hasApiKey ? '__MASKED__' : undefined),
        username: username.trim() || (authType === 'api_key' ? 'api-key' : undefined),
        password: password.trim() || (isEditing && initialInstance?.hasPassword ? '__MASKED__' : undefined),
        site: site.trim() || 'default',
        allowSelfSignedCert,
        name: name || 'UniFi Test',
      })
      setTestResult(result)
    } catch (err: any) {
      setTestResult({
        success: false,
        latencyMs: 0,
        message: err.message || 'Failed to reach UniFi Controller',
      })
    }
  }

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim() || !controllerUrl.trim()) {
      setErrorMessage('Please provide a name and controller URL.')
      return
    }

    if (authType === 'api_key') {
      if (!isEditing && !apiKey.trim()) {
        setErrorMessage('API Key is required for UniFi OS Server.')
        return
      }
    } else {
      if (!username.trim()) {
        setErrorMessage('Username is required.')
        return
      }
      if (!isEditing && !password.trim()) {
        setErrorMessage('Password is required for new UniFi controllers.')
        return
      }
    }

    try {
      await saveMutation.mutateAsync({
        id: id || undefined,
        name: name.trim(),
        controllerUrl: controllerUrl.trim(),
        authType,
        apiKey: apiKey.trim() ? apiKey.trim() : undefined,
        username: authType === 'credentials' ? username.trim() : (username.trim() || 'api-key'),
        password: authType === 'credentials' && password.trim() ? password.trim() : undefined,
        site: site.trim() || 'default',
        allowSelfSignedCert,
      })
      onClose()
    } catch (err: any) {
      setErrorMessage(err.message || 'Failed to save UniFi controller')
    }
  }

  return (
    <Dialog open={open} onClose={onClose}>
      <form onSubmit={handleSave}>
        <DialogHeader>
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-blue-500/10 text-blue-400 border border-blue-500/20">
              <Wifi className="h-5 w-5" />
            </div>
            <div>
              <DialogTitle>
                {isEditing ? 'Edit UniFi Controller' : 'Add UniFi Controller'}
              </DialogTitle>
              <DialogDescription>
                Connect to UniFi OS Server or Network Controller for PoE switch port power recycling and device telemetry.
              </DialogDescription>
            </div>
          </div>
        </DialogHeader>

        <DialogBody className="space-y-4 max-h-[70vh] overflow-y-auto py-4">
          {errorMessage && (
            <div className="p-3 bg-red-950/40 border border-red-800/50 rounded-lg flex items-center gap-3 text-red-200 text-sm">
              <AlertCircle className="h-4 w-4 shrink-0 text-red-400" />
              <span>{errorMessage}</span>
            </div>
          )}

          {/* Auth Method Selector */}
          <div className="space-y-1.5">
            <label className="text-xs font-semibold uppercase tracking-wider text-muted-foreground block">
              Authentication Method
            </label>
            <div className="grid grid-cols-2 gap-2 p-1 bg-slate-950/60 rounded-lg border border-slate-800">
              <button
                type="button"
                onClick={() => setAuthType('api_key')}
                className={`flex items-center justify-center gap-2 py-2 px-3 rounded-md text-xs font-medium transition-all ${
                  authType === 'api_key'
                    ? 'bg-blue-600 text-white shadow-sm font-semibold'
                    : 'text-slate-400 hover:text-slate-200 hover:bg-slate-900/50'
                }`}
              >
                <Key className="h-3.5 w-3.5" />
                <span>UniFi OS API Key</span>
              </button>
              <button
                type="button"
                onClick={() => setAuthType('credentials')}
                className={`flex items-center justify-center gap-2 py-2 px-3 rounded-md text-xs font-medium transition-all ${
                  authType === 'credentials'
                    ? 'bg-blue-600 text-white shadow-sm font-semibold'
                    : 'text-slate-400 hover:text-slate-200 hover:bg-slate-900/50'
                }`}
              >
                <Lock className="h-3.5 w-3.5" />
                <span>Legacy Username/Pass</span>
              </button>
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <label className="text-xs font-semibold uppercase tracking-wider text-muted-foreground block mb-1.5">
                Controller Name *
              </label>
              <Input
                placeholder="e.g. UniFi OS Server"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
              />
            </div>

            <div>
              <label className="text-xs font-semibold uppercase tracking-wider text-muted-foreground block mb-1.5">
                Site Identifier
              </label>
              <Input
                placeholder="default"
                value={site}
                onChange={(e) => setSite(e.target.value)}
              />
            </div>
          </div>

          <div>
            <label className="text-xs font-semibold uppercase tracking-wider text-muted-foreground block mb-1.5">
              Controller URL *
            </label>
            <Input
              placeholder={authType === 'api_key' ? 'https://192.168.1.1' : 'https://192.168.1.1:8443'}
              value={controllerUrl}
              onChange={(e) => setControllerUrl(e.target.value)}
              required
            />
            <p className="text-[11px] text-muted-foreground mt-1">
              {authType === 'api_key'
                ? 'For UniFi OS Server / UDM / CloudKey, standard HTTPS on port 443 is used (e.g. https://192.168.1.1).'
                : 'For standalone Network Controller docker containers, default port is 8443.'}
            </p>
          </div>

          {/* Conditional Auth Fields */}
          {authType === 'api_key' ? (
            <div className="space-y-2 p-3.5 rounded-lg border border-blue-500/20 bg-blue-950/10">
              <div className="flex items-center justify-between">
                <label className="text-xs font-semibold uppercase tracking-wider text-blue-300 flex items-center gap-1.5">
                  <Key className="h-3.5 w-3.5 text-blue-400" />
                  UniFi OS API Key {isEditing ? <span className="text-muted-foreground font-normal lowercase">(leave blank to keep)</span> : '*'}
                </label>
                <button
                  type="button"
                  onClick={() => setShowApiKey(!showApiKey)}
                  className="text-xs text-blue-400 hover:text-blue-300 flex items-center gap-1"
                >
                  {showApiKey ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
                  <span>{showApiKey ? 'Hide' : 'Show'}</span>
                </button>
              </div>
              <Input
                type={showApiKey ? 'text' : 'password'}
                placeholder={isEditing && initialInstance?.hasApiKey ? '••••••••••••••••••••••••••••••••' : 'Paste your UniFi OS API key here'}
                value={apiKey}
                onChange={(e) => setApiKey(e.target.value)}
                required={!isEditing}
                className="font-mono text-xs"
              />
              <p className="text-[11px] text-slate-400 flex items-start gap-1.5 pt-1">
                <Server className="h-3.5 w-3.5 text-blue-400 shrink-0 mt-0.5" />
                <span>
                  Generate an API key in UniFi OS Console under <strong>Settings &gt; System &gt; Advanced &gt; Integration / API Keys</strong>.
                </span>
              </p>
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label className="text-xs font-semibold uppercase tracking-wider text-muted-foreground block mb-1.5">
                  Admin Username *
                </label>
                <Input
                  placeholder="admin"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  required
                />
              </div>

              <div>
                <label className="text-xs font-semibold uppercase tracking-wider text-muted-foreground block mb-1.5">
                  Password {isEditing && <span className="text-muted-foreground font-normal">(leave blank to keep)</span>}
                </label>
                <Input
                  type="password"
                  placeholder={isEditing ? '••••••••' : 'Controller password'}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required={!isEditing}
                />
              </div>
            </div>
          )}

          <div className="pt-2">
            <label className="flex items-center gap-2.5 cursor-pointer text-sm font-medium">
              <input
                type="checkbox"
                checked={allowSelfSignedCert}
                onChange={(e) => setAllowSelfSignedCert(e.target.checked)}
                className="rounded border-slate-700 bg-slate-900 text-blue-500 focus:ring-blue-500/20"
              />
              <span>Allow Self-Signed TLS Certificates (Common for UniFi OS Consoles)</span>
            </label>
          </div>

          {/* Pre-flight Connection Result */}
          {testResult && (
            <div
              className={`p-3.5 rounded-lg border text-sm transition-all ${
                testResult.success
                  ? 'bg-emerald-950/20 border-emerald-800/40 text-emerald-300'
                  : 'bg-red-950/20 border-red-800/40 text-red-300'
              }`}
            >
              <div className="flex items-start gap-3">
                {testResult.success ? (
                  <CheckCircle2 className="h-5 w-5 text-emerald-400 shrink-0 mt-0.5" />
                ) : (
                  <XCircle className="h-5 w-5 text-red-400 shrink-0 mt-0.5" />
                )}
                <div className="space-y-1">
                  <div className="font-semibold flex items-center gap-2">
                    {testResult.success ? 'Connection Successful' : 'Connection Failed'}
                    <Badge variant={testResult.success ? 'success' : 'destructive'} className="text-[10px]">
                      {testResult.latencyMs}ms
                    </Badge>
                  </div>
                  {testResult.success ? (
                    <div className="text-xs space-y-0.5 text-emerald-200/80">
                      <div>Controller Version: <span className="font-mono text-emerald-100">{testResult.controllerVersion || 'Unknown'}</span></div>
                      <div>Adopted Devices: <span className="font-mono text-emerald-100">{testResult.deviceCount ?? 0}</span> | Active Clients: <span className="font-mono text-emerald-100">{testResult.clientCount ?? 0}</span></div>
                    </div>
                  ) : (
                    <div className="text-xs text-red-200/90">{testResult.message}</div>
                  )}
                </div>
              </div>
            </div>
          )}
        </DialogBody>

        <DialogFooter className="flex items-center justify-between border-t border-slate-800/60 pt-4">
          <Button
            type="button"
            variant="outline"
            onClick={handleTestConnection}
            disabled={testMutation.isPending || !controllerUrl.trim()}
            className="gap-2"
          >
            {testMutation.isPending ? (
              <Radio className="h-4 w-4 animate-spin text-blue-400" />
            ) : (
              <ShieldCheck className="h-4 w-4 text-blue-400" />
            )}
            <span>Test Connection</span>
          </Button>

          <div className="flex items-center gap-2">
            <Button type="button" variant="ghost" onClick={onClose}>
              Cancel
            </Button>
            <Button
              type="submit"
              disabled={saveMutation.isPending}
              className="gap-2 bg-blue-600 hover:bg-blue-500 text-white"
            >
              {saveMutation.isPending ? (
                <Radio className="h-4 w-4 animate-spin" />
              ) : (
                <Save className="h-4 w-4" />
              )}
              <span>{isEditing ? 'Save Changes' : 'Add Controller'}</span>
            </Button>
          </div>
        </DialogFooter>
      </form>
    </Dialog>
  )
}
