import { useState } from 'react'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Badge } from '../../components/ui/badge'
import { useProxmoxProbe } from '../hosts/useHosts'
import { Server, CheckCircle2, XCircle, Cpu, HardDrive } from 'lucide-react'
import type { ProxmoxProbePayload, ProxmoxProbeResult } from '../../api/hosts'

export function ProxmoxProbeView() {
  const [formData, setFormData] = useState<ProxmoxProbePayload>({
    baseUrl: 'https://192.168.1.10:8006',
    apiTokenId: 'root@pam!homelab-admin',
    apiTokenSecret: '',
    allowSelfSignedCert: true,
  })

  const [result, setResult] = useState<ProxmoxProbeResult | null>(null)
  const probeMutation = useProxmoxProbe()

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    try {
      const res = await probeMutation.mutateAsync(formData)
      setResult(res)
    } catch (err) {
      setResult({
        success: false,
        errorMessage: err instanceof Error ? err.message : 'Failed to connect to Proxmox API.',
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

  return (
    <div className="space-y-6 max-w-4xl mx-auto">
      {/* Header Banner */}
      <div className="p-6 bg-zinc-900/60 border border-zinc-800 rounded-xl backdrop-blur-md">
        <div className="flex items-center gap-3">
          <div className="p-3 bg-purple-950/60 border border-purple-800/50 rounded-lg text-purple-400">
            <Server className="h-6 w-6" />
          </div>
          <div>
            <h2 className="text-lg font-semibold text-zinc-100">Proxmox VE Connection Probe</h2>
            <p className="text-xs text-zinc-400 mt-0.5">
              Verify API credentials and cluster reachability against a Proxmox VE hypervisor node.
            </p>
          </div>
        </div>

        <form onSubmit={handleSubmit} className="mt-6 space-y-4">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <Input
              label="Proxmox Base URL"
              required
              placeholder="https://192.168.1.10:8006"
              value={formData.baseUrl}
              onChange={(e) => setFormData({ ...formData, baseUrl: e.target.value })}
            />

            <Input
              label="API Token ID"
              required
              placeholder="user@pam!tokenid"
              value={formData.apiTokenId}
              onChange={(e) => setFormData({ ...formData, apiTokenId: e.target.value })}
            />

            <Input
              label="API Token Secret"
              required
              type="password"
              placeholder="UUID secret key"
              value={formData.apiTokenSecret}
              onChange={(e) => setFormData({ ...formData, apiTokenSecret: e.target.value })}
            />
          </div>

          <div className="flex flex-wrap items-center justify-between gap-4 pt-2">
            <label className="flex items-center gap-2 text-xs text-zinc-300 cursor-pointer">
              <input
                type="checkbox"
                checked={formData.allowSelfSignedCert}
                onChange={(e) =>
                  setFormData({ ...formData, allowSelfSignedCert: e.target.checked })
                }
                className="rounded border-zinc-700 bg-zinc-950 text-emerald-500 focus:ring-emerald-500"
              />
              <span>Allow self-signed TLS certificates (common for homelab IPs)</span>
            </label>

            <Button
              type="submit"
              variant="primary"
              size="md"
              isLoading={probeMutation.isPending}
            >
              Test Connection
            </Button>
          </div>
        </form>
      </div>

      {/* Results Section */}
      {result && (
        <div className="space-y-4 animate-in fade-in slide-in-from-top-2 duration-200">
          {result.success ? (
            <div className="p-5 bg-emerald-950/40 border border-emerald-800/60 rounded-xl space-y-4">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2 text-emerald-300 font-medium">
                  <CheckCircle2 className="h-5 w-5 text-emerald-400" />
                  <span>Proxmox VE Connection Verified</span>
                </div>
                <div className="flex items-center gap-2">
                  <Badge variant="success">Version {result.version || 'Unknown'}</Badge>
                  {result.release && <Badge variant="outline">Release {result.release}</Badge>}
                </div>
              </div>

              {result.nodes && result.nodes.length > 0 ? (
                <div>
                  <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider mb-2">
                    Discovered Cluster Nodes ({result.nodes.length})
                  </h4>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                    {result.nodes.map((n) => (
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
                {result.errorMessage || 'Unable to connect to Proxmox API.'}
              </p>
              <div className="text-xs text-zinc-400 pt-1 space-y-1">
                <p>Troubleshooting suggestions:</p>
                <ul className="list-disc pl-5 space-y-0.5 text-zinc-400">
                  <li>Ensure the target port (usually 8006) is accessible and unblocked by firewall.</li>
                  <li>Check that the API token has <code>PVEAuditor</code> or <code>Sys.Audit</code> permissions on <code>/</code>.</li>
                  <li>If using self-signed certs, verify the checkbox above is checked.</li>
                </ul>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
