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
  Shield,
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
import { useSaveOPNsenseInstance, useTestOPNsenseConnection } from './useOPNsense'
import type { OPNsenseInstanceDto, OPNsenseTestResult } from '../../../api/opnsense'

interface AddOPNsenseModalProps {
  open: boolean
  onClose: () => void
  initialInstance?: OPNsenseInstanceDto | null
}

export function AddOPNsenseModal({
  open,
  onClose,
  initialInstance,
}: AddOPNsenseModalProps) {
  const isEditing = Boolean(initialInstance)
  const saveMutation = useSaveOPNsenseInstance()
  const testMutation = useTestOPNsenseConnection()

  const [name, setName] = useState('')
  const [id, setId] = useState('')
  const [baseUrl, setBaseUrl] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [apiSecret, setApiSecret] = useState('')
  const [showApiSecret, setShowApiSecret] = useState(false)
  const [allowSelfSignedCert, setAllowSelfSignedCert] = useState(true)

  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [testResult, setTestResult] = useState<OPNsenseTestResult | null>(null)

  useEffect(() => {
    if (initialInstance) {
      setName(initialInstance.name || '')
      setId(initialInstance.id || '')
      setBaseUrl(initialInstance.baseUrl || '')
      setApiKey(initialInstance.apiKey || '')
      setApiSecret('')
      setAllowSelfSignedCert(initialInstance.allowSelfSignedCert ?? true)
    } else {
      setName('')
      setId('')
      setBaseUrl('')
      setApiKey('')
      setApiSecret('')
      setAllowSelfSignedCert(true)
    }
    setErrorMessage(null)
    setTestResult(null)
  }, [initialInstance, open])

  const handleTestConnection = async () => {
    setErrorMessage(null)
    setTestResult(null)

    if (!baseUrl.trim()) {
      setErrorMessage('Please provide a firewall Base URL before testing.')
      return
    }

    if (!apiKey.trim()) {
      setErrorMessage('Please provide an API Key before testing.')
      return
    }

    if (!isEditing && !apiSecret.trim()) {
      setErrorMessage('Please provide an API Secret before testing.')
      return
    }

    try {
      const result = await testMutation.mutateAsync({
        id: id.trim() || undefined,
        name: name.trim() || 'Preflight Test',
        baseUrl: baseUrl.trim(),
        apiKey: apiKey.trim(),
        apiSecret: apiSecret.trim() || undefined,
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
      setErrorMessage('Firewall Name is required.')
      return
    }

    if (!baseUrl.trim()) {
      setErrorMessage('Base URL is required.')
      return
    }

    if (!apiKey.trim()) {
      setErrorMessage('API Key is required.')
      return
    }

    if (!isEditing && !apiSecret.trim()) {
      setErrorMessage('API Secret is required for new configurations.')
      return
    }

    try {
      await saveMutation.mutateAsync({
        id: id.trim() || undefined,
        name: name.trim(),
        baseUrl: baseUrl.trim(),
        apiKey: apiKey.trim(),
        apiSecret: apiSecret.trim() || undefined,
        allowSelfSignedCert,
      })
      onClose()
    } catch (err: unknown) {
      setErrorMessage(err instanceof Error ? err.message : 'Failed to save OPNsense configuration.')
    }
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md">
      <DialogHeader onClose={onClose}>
        <div className="flex items-center gap-2.5">
          <div className="p-2 rounded-lg bg-orange-500/10 border border-orange-500/20 text-orange-400">
            <Shield className="h-5 w-5" />
          </div>
          <div>
            <DialogTitle>
              {isEditing ? 'Configure OPNsense Firewall' : 'Add OPNsense Gateway'}
            </DialogTitle>
            <DialogDescription>
              Connect to OPNsense Core REST API to monitor gateway health, service statuses, and ingest DHCP leases.
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
                    {testResult.success ? 'Pre-flight Connection Successful' : 'Connection Failed'}
                  </span>
                </div>
                <Badge variant={testResult.success ? 'success' : 'destructive'} className="text-[10px]">
                  {testResult.latencyMs}ms
                </Badge>
              </div>

              {testResult.success ? (
                <div className="mt-2 grid grid-cols-2 gap-2 text-[11px] text-zinc-300 bg-zinc-950/40 p-2 rounded border border-zinc-800/60">
                  <div>
                    <span className="text-zinc-500">Hostname:</span>{' '}
                    <span className="font-mono text-zinc-200">{testResult.hostname || 'OPNsense'}</span>
                  </div>
                  <div>
                    <span className="text-zinc-500">Version:</span>{' '}
                    <span className="font-mono text-zinc-200">{testResult.version || 'Unknown'}</span>
                  </div>
                </div>
              ) : (
                <p className="mt-1.5 text-red-400 font-mono text-[11px] break-all">
                  {testResult.message || 'Unable to reach OPNsense API.'}
                </p>
              )}
            </div>
          )}

          {/* Identity Fields */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <label className="text-xs font-medium text-zinc-300 flex items-center gap-1.5">
                <Server className="h-3.5 w-3.5 text-zinc-400" />
                Firewall Name
              </label>
              <Input
                placeholder="e.g. Primary Edge Firewall"
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

          {/* Base URL */}
          <div className="space-y-1.5">
            <label className="text-xs font-medium text-zinc-300 flex items-center justify-between">
              <span>Firewall Base URL</span>
              <span className="text-[10px] text-zinc-500 font-mono">https://&lt;ip&gt;:&lt;port&gt;</span>
            </label>
            <Input
              placeholder="https://192.168.1.1:8443 or https://opnsense.lan"
              value={baseUrl}
              onChange={(e) => setBaseUrl(e.target.value)}
              className="font-mono text-xs"
              required
            />
          </div>

          {/* API Credentials */}
          <div className="space-y-3 pt-2 border-t border-zinc-800/80">
            <div className="space-y-1.5">
              <label className="text-xs font-medium text-zinc-300 flex items-center gap-1.5">
                <Key className="h-3.5 w-3.5 text-zinc-400" />
                API Key
              </label>
              <Input
                placeholder="e.g. K9Z... (from System > Access > Users)"
                value={apiKey}
                onChange={(e) => setApiKey(e.target.value)}
                className="font-mono text-xs"
                required
              />
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-medium text-zinc-300 flex items-center justify-between">
                <span className="flex items-center gap-1.5">
                  <Lock className="h-3.5 w-3.5 text-zinc-400" />
                  API Secret
                </span>
                {isEditing && (
                  <span className="text-[10px] text-zinc-500">
                    Leave blank to keep existing secret
                  </span>
                )}
              </label>
              <div className="relative">
                <Input
                  type={showApiSecret ? 'text' : 'password'}
                  placeholder={isEditing ? '••••••••••••••••' : 'Enter API secret...'}
                  value={apiSecret}
                  onChange={(e) => setApiSecret(e.target.value)}
                  className="font-mono text-xs pr-9"
                  required={!isEditing}
                />
                <button
                  type="button"
                  onClick={() => setShowApiSecret(!showApiSecret)}
                  className="absolute right-2.5 top-1/2 -translate-y-1/2 text-zinc-500 hover:text-zinc-300"
                >
                  {showApiSecret ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
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
                className="rounded bg-zinc-900 border-zinc-700 text-orange-500 focus:ring-orange-500/20"
              />
              <span className="flex items-center gap-1.5">
                <ShieldCheck className="h-4 w-4 text-orange-400" />
                Allow Self-Signed TLS Certificates (Recommended for homelabs)
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
            disabled={testMutation.isPending || !baseUrl.trim() || !apiKey.trim()}
            className="gap-1.5 border-orange-500/30 hover:bg-orange-950/30 text-orange-300"
          >
            <Radio className={`h-3.5 w-3.5 ${testMutation.isPending ? 'animate-pulse text-orange-400' : ''}`} />
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
              className="gap-1.5 bg-orange-600 hover:bg-orange-500 text-white font-semibold"
            >
              <Save className="h-3.5 w-3.5" />
              {saveMutation.isPending ? 'Saving...' : isEditing ? 'Save Changes' : 'Connect Firewall'}
            </Button>
          </div>
        </DialogFooter>
      </form>
    </Dialog>
  )
}
