import { useState, useEffect } from 'react'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Badge } from '../../components/ui/badge'
import { useProxmoxConfig, useSaveProxmoxConfig, useProbeProxmox } from './useAdapters'
import {
  Server,
  CheckCircle2,
  XCircle,
  Cpu,
  HardDrive,
  Save,
  Radio,
  Clock,
  ShieldCheck,
  AlertCircle,
  RotateCw,
} from 'lucide-react'
import type { ProxmoxProbeResult } from '../../api/hosts'

export function ProxmoxProbeView() {
  const { data: config, isLoading: isLoadingConfig, refetch: refetchConfig } = useProxmoxConfig()
  const saveMutation = useSaveProxmoxConfig()
  const probeMutation = useProbeProxmox()

  const [baseUrl, setBaseUrl] = useState('')
  const [apiTokenId, setApiTokenId] = useState('')
  const [apiTokenSecret, setApiTokenSecret] = useState('')
  const [allowSelfSignedCert, setAllowSelfSignedCert] = useState(true)
  const [taskPollTimeoutSeconds, setTaskPollTimeoutSeconds] = useState(300)

  const [saveSuccessMessage, setSaveSuccessMessage] = useState<string | null>(null)
  const [saveErrorMessage, setSaveErrorMessage] = useState<string | null>(null)
  const [probeResult, setProbeResult] = useState<ProxmoxProbeResult | null>(null)

  // Populate form with saved configuration on initial load
  useEffect(() => {
    if (config) {
      setBaseUrl(config.baseUrl || '')
      setApiTokenId(config.apiTokenId || '')
      setAllowSelfSignedCert(config.allowSelfSignedCert ?? true)
      if (config.taskPollTimeoutSeconds) {
        setTaskPollTimeoutSeconds(config.taskPollTimeoutSeconds)
      }
      // Leave apiTokenSecret blank in input so the user doesn't expose it,
      // but if hasSecret is true the backend preserves it
      setApiTokenSecret('')
    }
  }, [config])

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    setSaveSuccessMessage(null)
    setSaveErrorMessage(null)

    if (!baseUrl.trim() || !apiTokenId.trim()) {
      setSaveErrorMessage('Base URL and API Token ID are required.')
      return
    }

    try {
      await saveMutation.mutateAsync({
        baseUrl: baseUrl.trim(),
        apiTokenId: apiTokenId.trim(),
        apiTokenSecret: apiTokenSecret.trim() ? apiTokenSecret.trim() : undefined,
        allowSelfSignedCert,
        taskPollTimeoutSeconds,
      })

      setSaveSuccessMessage('Configuration saved successfully to persistent storage.')
      setApiTokenSecret('')
      setTimeout(() => setSaveSuccessMessage(null), 5000)
    } catch (err) {
      setSaveErrorMessage(err instanceof Error ? err.message : 'Failed to save configuration.')
    }
  }

  const handleProbe = async () => {
    setSaveErrorMessage(null)
    try {
      const res = await probeMutation.mutateAsync({
        baseUrl: baseUrl.trim() || undefined,
        apiTokenId: apiTokenId.trim() || undefined,
        apiTokenSecret: apiTokenSecret.trim() || undefined,
        allowSelfSignedCert,
      })
      setProbeResult(res)
    } catch (err) {
      setProbeResult({
        success: false,
        errorMessage: err instanceof Error ? err.message : 'Failed to probe Proxmox API.',
      })
    }
  }

  const formatBytes = (bytes?: number | null) => {
    if (!bytes) return '0 B'
    const units = ['B', 'KB', 'MB', 'GB', 'TB']
    let val = bytes
    let i = 0
    while (val >= 1024 && i < units.length - 1) {
      val /= 1024
      i++
    }
    return `${val.toFixed(1)} ${units[i]}`
  }

  const isConfigured = Boolean(config?.baseUrl && config?.apiTokenId && config?.hasSecret)

  return (
    <div className="space-y-6 max-w-4xl mx-auto">
      {/* Header & Status Card */}
      <div className="p-6 bg-zinc-900/60 border border-zinc-800 rounded-xl backdrop-blur-md space-y-6">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="p-3 bg-purple-950/60 border border-purple-800/50 rounded-lg text-purple-400">
              <Server className="h-6 w-6" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-lg font-semibold text-zinc-100">Proxmox VE Hypervisor Adapter</h2>
                {isLoadingConfig ? (
                  <Badge variant="default">Loading...</Badge>
                ) : isConfigured ? (
                  <Badge variant="success" dot>Configured</Badge>
                ) : (
                  <Badge variant="warning" dot>Not Configured</Badge>
                )}
              </div>
              <p className="text-xs text-zinc-400 mt-0.5">
                Manage credentials, cluster discovery, and safety snapshot coordination for Proxmox VE.
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => refetchConfig()}
              disabled={isLoadingConfig}
              className="text-xs"
            >
              <RotateCw className={`h-3.5 w-3.5 mr-1.5 ${isLoadingConfig ? 'animate-spin' : ''}`} />
              Refresh
            </Button>
          </div>
        </div>

        {/* Configuration Form */}
        <form onSubmit={handleSave} className="space-y-4 pt-2 border-t border-zinc-800/80">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
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

            <div className="space-y-1">
              <div className="flex items-center justify-between">
                <label className="text-xs font-medium text-zinc-300">
                  API Token Secret {config?.hasSecret && !apiTokenSecret && '(Saved)'}
                </label>
                {config?.hasSecret && (
                  <span className="text-[10px] text-emerald-400 flex items-center gap-1 font-mono">
                    <ShieldCheck className="h-3 w-3" /> Stored
                  </span>
                )}
              </div>
              <Input
                type="password"
                placeholder={config?.hasSecret ? '•••••••• (leave blank to keep stored)' : 'UUID secret key'}
                value={apiTokenSecret}
                onChange={(e) => setApiTokenSecret(e.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 pt-1">
            <label className="flex items-center gap-2.5 text-xs text-zinc-300 cursor-pointer select-none">
              <input
                type="checkbox"
                checked={allowSelfSignedCert}
                onChange={(e) => setAllowSelfSignedCert(e.target.checked)}
                className="rounded border-zinc-700 bg-zinc-950 text-emerald-500 focus:ring-emerald-500"
              />
              <span>Allow self-signed TLS certificates (common for local homelab IP addresses)</span>
            </label>

            {config?.updatedAt && (
              <div className="flex items-center justify-end gap-1.5 text-xs text-zinc-500">
                <Clock className="h-3.5 w-3.5" />
                <span>Last updated: {new Date(config.updatedAt).toLocaleString()}</span>
              </div>
            )}
          </div>

          {/* Feedback alerts */}
          {saveSuccessMessage && (
            <div className="p-3 bg-emerald-950/50 border border-emerald-800/60 rounded-lg flex items-center gap-2 text-xs text-emerald-300 animate-in fade-in">
              <CheckCircle2 className="h-4 w-4 text-emerald-400 shrink-0" />
              <span>{saveSuccessMessage}</span>
            </div>
          )}

          {saveErrorMessage && (
            <div className="p-3 bg-rose-950/50 border border-rose-800/60 rounded-lg flex items-center gap-2 text-xs text-rose-300 animate-in fade-in">
              <AlertCircle className="h-4 w-4 text-rose-400 shrink-0" />
              <span>{saveErrorMessage}</span>
            </div>
          )}

          {/* Action Buttons */}
          <div className="flex items-center justify-end gap-3 pt-3 border-t border-zinc-800/60">
            <Button
              type="button"
              variant="outline"
              size="md"
              onClick={handleProbe}
              isLoading={probeMutation.isPending}
              disabled={saveMutation.isPending}
            >
              <Radio className="h-4 w-4 mr-1.5 text-purple-400" />
              Test Connection
            </Button>

            <Button
              type="submit"
              variant="primary"
              size="md"
              isLoading={saveMutation.isPending}
              disabled={probeMutation.isPending}
            >
              <Save className="h-4 w-4 mr-1.5" />
              Save Configuration
            </Button>
          </div>
        </form>
      </div>

      {/* Connection Probe Results Section */}
      {probeResult && (
        <div className="space-y-4 animate-in fade-in slide-in-from-top-2 duration-200">
          {probeResult.success ? (
            <div className="p-5 bg-emerald-950/40 border border-emerald-800/60 rounded-xl space-y-4">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2 text-emerald-300 font-medium">
                  <CheckCircle2 className="h-5 w-5 text-emerald-400" />
                  <span>Proxmox VE Connection Verified</span>
                </div>
                <div className="flex items-center gap-2">
                  <Badge variant="success">Version {probeResult.version || 'Unknown'}</Badge>
                  {probeResult.release && <Badge variant="outline">Release {probeResult.release}</Badge>}
                </div>
              </div>

              {probeResult.nodes && probeResult.nodes.length > 0 ? (
                <div>
                  <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider mb-2">
                    Discovered Cluster Nodes ({probeResult.nodes.length})
                  </h4>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                    {probeResult.nodes.map((n) => (
                      <div
                        key={n.node}
                        className="p-3 bg-zinc-900/80 border border-zinc-800 rounded-lg space-y-2"
                      >
                        <div className="flex items-center justify-between">
                          <span className="font-semibold text-sm text-zinc-100">{n.node}</span>
                          <Badge variant={n.status === 'online' ? 'success' : 'default'} dot>
                            {n.status}
                          </Badge>
                        </div>
                        <div className="grid grid-cols-2 gap-2 text-xs text-zinc-400">
                          <div className="flex items-center gap-1.5">
                            <Cpu className="h-3.5 w-3.5 text-zinc-500" />
                            <span>CPU: {n.cpu ? `${(n.cpu * 100).toFixed(1)}%` : '0%'} ({n.maxCpu || 0} cores)</span>
                          </div>
                          <div className="flex items-center gap-1.5">
                            <HardDrive className="h-3.5 w-3.5 text-zinc-500" />
                            <span>RAM: {formatBytes(n.memory)} / {formatBytes(n.maxMemory)}</span>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              ) : (
                <p className="text-xs text-zinc-400">No compute nodes returned in cluster listing.</p>
              )}
            </div>
          ) : (
            <div className="p-5 bg-rose-950/40 border border-rose-800/60 rounded-xl space-y-2">
              <div className="flex items-center gap-2 text-rose-300 font-medium">
                <XCircle className="h-5 w-5 text-rose-400" />
                <span>Connection Probe Failed</span>
              </div>
              <p className="text-xs text-rose-200 bg-black/40 p-3 rounded-lg font-mono border border-rose-900/50">
                {probeResult.errorMessage || 'Unable to connect to Proxmox API.'}
              </p>
              <div className="text-xs text-zinc-400 pt-1 space-y-1">
                <p>Troubleshooting suggestions:</p>
                <ul className="list-disc pl-5 space-y-0.5 text-zinc-400">
                  <li>Ensure the target port (usually 8006) is accessible and unblocked by firewall.</li>
                  <li>Check that the API token has <code>PVEAuditor</code> or <code>Sys.Audit</code> permissions on <code>/</code>.</li>
                  <li>If using self-signed certs, verify the checkbox above is checked.</li>
                  <li>If you changed credentials, make sure to click <strong>Save Configuration</strong>.</li>
                </ul>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
