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
import {
  Layers,
  ShieldCheck,
  CheckCircle2,
  XCircle,
  Radio,
  FileCode,
  Globe,
  Save,
  AlertCircle,
  UploadCloud,
} from 'lucide-react'
import {
  useSaveKubernetesCluster,
  useTestKubernetesClusterPreflight,
} from './useKubernetes'
import type {
  KubernetesClusterDto,
  KubernetesClusterTestResult,
} from '../../../api/kubernetes'

interface AddKubernetesModalProps {
  open: boolean
  onClose: () => void
  initialCluster?: KubernetesClusterDto | null
}

export function AddKubernetesModal({
  open,
  onClose,
  initialCluster,
}: AddKubernetesModalProps) {
  const isEditing = Boolean(initialCluster)
  const saveMutation = useSaveKubernetesCluster()
  const testPreflightMutation = useTestKubernetesClusterPreflight()

  const [authMode, setAuthMode] = useState<'kubeconfig' | 'token'>('kubeconfig')
  const [name, setName] = useState('')
  const [id, setId] = useState('')
  const [apiServerUrl, setApiServerUrl] = useState('')
  const [kubeConfigRaw, setKubeConfigRaw] = useState('')
  const [token, setToken] = useState('')
  const [contextName, setContextName] = useState('')
  const [skipTlsVerify, setSkipTlsVerify] = useState(true)

  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [testResult, setTestResult] = useState<KubernetesClusterTestResult | null>(null)

  useEffect(() => {
    if (initialCluster) {
      setName(initialCluster.name || '')
      setId(initialCluster.id || '')
      setApiServerUrl(initialCluster.apiServerUrl || '')
      setContextName(initialCluster.contextName || '')
      setSkipTlsVerify(initialCluster.skipTlsVerify ?? true)
      setKubeConfigRaw('')
      setToken('')
      setAuthMode(initialCluster.hasToken ? 'token' : 'kubeconfig')
    } else {
      setName('')
      setId('')
      setApiServerUrl('')
      setKubeConfigRaw('')
      setToken('')
      setContextName('')
      setSkipTlsVerify(true)
      setAuthMode('kubeconfig')
    }
    setErrorMessage(null)
    setTestResult(null)
  }, [initialCluster, open])

  const handleFileUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return

    const reader = new FileReader()
    reader.onload = (event) => {
      const content = event.target?.result as string
      setKubeConfigRaw(content)
      // Attempt to infer cluster name if not already set
      if (!name) {
        const base = file.name.replace(/(\.yaml|\.yml|\.conf|\.config)$/i, '')
        if (base && base !== 'config') {
          setName(base)
        }
      }
    }
    reader.readAsText(file)
  }

  const handleTestConnection = async () => {
    setErrorMessage(null)
    setTestResult(null)

    if (!name.trim()) {
      setErrorMessage('Cluster name is required.')
      return
    }

    if (authMode === 'kubeconfig' && !kubeConfigRaw.trim() && !isEditing) {
      setErrorMessage('Please paste or upload a kubeconfig YAML file.')
      return
    }

    if (authMode === 'token' && (!apiServerUrl.trim() || (!token.trim() && !isEditing))) {
      setErrorMessage('API Server URL and Bearer Token are required for direct token auth.')
      return
    }

    try {
      const result = await testPreflightMutation.mutateAsync({
        id: id || undefined,
        name: name.trim(),
        apiServerUrl: apiServerUrl.trim() || undefined,
        kubeConfigRaw: authMode === 'kubeconfig' ? kubeConfigRaw.trim() : undefined,
        token: authMode === 'token' ? token.trim() : undefined,
        contextName: contextName.trim() || undefined,
        skipTlsVerify,
      })
      setTestResult(result)
    } catch (err: any) {
      setErrorMessage(err.message || 'Failed to execute pre-flight connection test')
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    if (!name.trim()) {
      setErrorMessage('Cluster name is required.')
      return
    }

    if (authMode === 'kubeconfig' && !kubeConfigRaw.trim() && !isEditing) {
      setErrorMessage('Please paste or upload a kubeconfig YAML file.')
      return
    }

    if (authMode === 'token' && (!apiServerUrl.trim() || (!token.trim() && !isEditing))) {
      setErrorMessage('API Server URL and Bearer Token are required for direct token auth.')
      return
    }

    try {
      await saveMutation.mutateAsync({
        id: id || undefined,
        name: name.trim(),
        apiServerUrl: apiServerUrl.trim() || undefined,
        kubeConfigRaw: authMode === 'kubeconfig' ? kubeConfigRaw.trim() : undefined,
        token: authMode === 'token' ? token.trim() : undefined,
        contextName: contextName.trim() || undefined,
        skipTlsVerify,
      })
      onClose()
    } catch (err: any) {
      setErrorMessage(err.message || 'Failed to save cluster configuration')
    }
  }

  return (
    <Dialog open={open} onClose={onClose}>
      <form onSubmit={handleSubmit}>
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Layers className="h-5 w-5 text-sky-400" />
            <span>{isEditing ? 'Edit Kubernetes Cluster' : 'Connect Kubernetes Cluster'}</span>
          </DialogTitle>
          <DialogDescription>
            Configure an agentless connection to a Kubernetes or k3s cluster for node lifecycle, cordon/drain, and workload rollouts.
          </DialogDescription>
        </DialogHeader>

        <DialogBody className="space-y-4">
          {errorMessage && (
            <div className="flex items-center gap-2.5 p-3 rounded-lg bg-rose-950/40 border border-rose-800/60 text-rose-300 text-xs">
              <AlertCircle className="h-4 w-4 shrink-0 text-rose-400" />
              <span>{errorMessage}</span>
            </div>
          )}

          {testResult && (
            <div
              className={`p-3.5 rounded-xl border text-xs space-y-1.5 ${
                testResult.success
                  ? 'bg-emerald-950/30 border-emerald-800/60 text-emerald-200'
                  : 'bg-rose-950/30 border-rose-800/60 text-rose-200'
              }`}
            >
              <div className="flex items-center justify-between font-medium">
                <span className="flex items-center gap-1.5">
                  {testResult.success ? (
                    <CheckCircle2 className="h-4 w-4 text-emerald-400" />
                  ) : (
                    <XCircle className="h-4 w-4 text-rose-400" />
                  )}
                  {testResult.success ? 'Cluster API Reachable' : 'Connection Failed'}
                </span>
                <span className="text-[11px] font-mono opacity-80">{testResult.latencyMs}ms</span>
              </div>
              <p className="text-[11px] opacity-90">{testResult.message}</p>
              {testResult.success && (
                <div className="flex items-center gap-2 pt-1 border-t border-emerald-800/40 text-[11px]">
                  <span>Version: <strong className="font-mono">{testResult.serverVersion}</strong></span>
                  <span>•</span>
                  <span>Nodes: <strong>{testResult.nodeCount}</strong></span>
                </div>
              )}
            </div>
          )}

          {/* Cluster Name & Context */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-zinc-300 mb-1">
                Cluster Name <span className="text-rose-400">*</span>
              </label>
              <Input
                placeholder="e.g. k8s-homelab-prod"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-zinc-300 mb-1">
                Context Name <span className="text-zinc-500 font-normal">(Optional)</span>
              </label>
              <Input
                placeholder="e.g. default"
                value={contextName}
                onChange={(e) => setContextName(e.target.value)}
              />
            </div>
          </div>

          {/* Auth Method Selector */}
          <div className="space-y-1.5">
            <label className="block text-xs font-medium text-zinc-300">
              Authentication Method
            </label>
            <div className="grid grid-cols-2 gap-2">
              <button
                type="button"
                onClick={() => setAuthMode('kubeconfig')}
                className={`flex items-center justify-center gap-2 py-2 px-3 rounded-lg border text-xs font-medium transition-all ${
                  authMode === 'kubeconfig'
                    ? 'bg-sky-950/40 border-sky-600/80 text-sky-200'
                    : 'bg-zinc-900/60 border-zinc-800 text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <FileCode className="h-3.5 w-3.5" />
                <span>Kubeconfig YAML</span>
              </button>

              <button
                type="button"
                onClick={() => setAuthMode('token')}
                className={`flex items-center justify-center gap-2 py-2 px-3 rounded-lg border text-xs font-medium transition-all ${
                  authMode === 'token'
                    ? 'bg-sky-950/40 border-sky-600/80 text-sky-200'
                    : 'bg-zinc-900/60 border-zinc-800 text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <Globe className="h-3.5 w-3.5" />
                <span>API Server + Token</span>
              </button>
            </div>
          </div>

          {/* Kubeconfig Mode */}
          {authMode === 'kubeconfig' && (
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <label className="block text-xs font-medium text-zinc-300">
                  Kubeconfig YAML
                </label>
                <label className="cursor-pointer flex items-center gap-1.5 text-xs text-sky-400 hover:text-sky-300 transition-colors">
                  <UploadCloud className="h-3.5 w-3.5" />
                  <span>Upload File</span>
                  <input
                    type="file"
                    accept=".yaml,.yml,.config,.conf"
                    className="hidden"
                    onChange={handleFileUpload}
                  />
                </label>
              </div>
              <textarea
                rows={6}
                value={kubeConfigRaw}
                onChange={(e) => setKubeConfigRaw(e.target.value)}
                placeholder={isEditing ? '(Leave empty to keep existing kubeconfig credentials)' : 'apiVersion: v1\nclusters:\n  - cluster:\n      server: https://192.168.1.50:6443...'}
                className="w-full bg-zinc-900/80 border border-zinc-700/60 rounded-lg p-2.5 text-xs font-mono text-zinc-200 focus:outline-none focus:ring-1 focus:ring-sky-500 placeholder:text-zinc-600 resize-y"
              />
              <p className="text-[11px] text-zinc-400">
                Kubeconfig files are automatically encrypted with AES-256 before storage.
              </p>
            </div>
          )}

          {/* Direct Token Mode */}
          {authMode === 'token' && (
            <div className="space-y-3">
              <div>
                <label className="block text-xs font-medium text-zinc-300 mb-1">
                  API Server URL <span className="text-rose-400">*</span>
                </label>
                <Input
                  placeholder="https://192.168.1.50:6443"
                  value={apiServerUrl}
                  onChange={(e) => setApiServerUrl(e.target.value)}
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-zinc-300 mb-1">
                  ServiceAccount Bearer Token
                </label>
                <Input
                  type="password"
                  placeholder={isEditing ? '•••••••• (Leave empty to keep current token)' : 'eyJhbGciOiJSUzI1NiIsImtpZCI...'}
                  value={token}
                  onChange={(e) => setToken(e.target.value)}
                />
              </div>
            </div>
          )}

          {/* TLS Options */}
          <div className="pt-2 border-t border-zinc-800 flex items-center justify-between">
            <label className="flex items-center gap-2 text-xs text-zinc-300 cursor-pointer select-none">
              <input
                type="checkbox"
                checked={skipTlsVerify}
                onChange={(e) => setSkipTlsVerify(e.target.checked)}
                className="rounded border-zinc-700 text-sky-500 focus:ring-sky-500/20 bg-zinc-900 h-4 w-4"
              />
              <span className="flex items-center gap-1.5">
                <ShieldCheck className="h-3.5 w-3.5 text-emerald-400" />
                Skip TLS Certificate Validation (Recommended for Self-Signed Homelab CA)
              </span>
            </label>
          </div>
        </DialogBody>

        <DialogFooter className="flex items-center justify-between gap-2 border-t border-zinc-800/80 px-6 py-4">
          <Button
            type="button"
            variant="outline"
            onClick={handleTestConnection}
            disabled={testPreflightMutation.isPending}
            className="flex items-center gap-2 text-xs border-zinc-700 hover:bg-zinc-800"
          >
            <Radio className={`h-3.5 w-3.5 ${testPreflightMutation.isPending ? 'animate-spin text-sky-400' : 'text-zinc-400'}`} />
            <span>{testPreflightMutation.isPending ? 'Testing...' : 'Test Connection'}</span>
          </Button>

          <div className="flex items-center gap-2">
            <Button type="button" variant="ghost" onClick={onClose} className="text-xs">
              Cancel
            </Button>
            <Button
              type="submit"
              variant="primary"
              disabled={saveMutation.isPending}
              className="flex items-center gap-2 text-xs bg-sky-600 hover:bg-sky-500 text-white"
            >
              <Save className="h-3.5 w-3.5" />
              <span>{saveMutation.isPending ? 'Saving...' : isEditing ? 'Update Cluster' : 'Connect Cluster'}</span>
            </Button>
          </div>
        </DialogFooter>
      </form>
    </Dialog>
  )
}
