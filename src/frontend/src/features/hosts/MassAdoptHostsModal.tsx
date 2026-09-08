import React, { useState, useEffect } from 'react'
import { Dialog, DialogHeader, DialogTitle, DialogBody, DialogFooter } from '../../components/ui/dialog'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Shield, Key, Lock, Server, CheckCircle2, AlertCircle, Users } from 'lucide-react'
import type { Host } from '../../api/hosts'
import { useBatchAdoptNodes } from './useAdoptNode'

interface MassAdoptHostsModalProps {
  hosts: Host[]
  open: boolean
  onClose: () => void
  onSuccess?: () => void
}

export const MassAdoptHostsModal: React.FC<MassAdoptHostsModalProps> = ({
  hosts,
  open,
  onClose,
  onSuccess,
}) => {
  const [port, setPort] = useState(22)
  const [username, setUsername] = useState('root')
  const [authType, setAuthType] = useState<'password' | 'key'>('password')
  const [password, setPassword] = useState('')
  const [privateKey, setPrivateKey] = useState('')

  const getInitialHubUrl = () => {
    if (typeof window !== 'undefined') {
      const hostname = window.location.hostname
      if (hostname && hostname !== 'localhost' && hostname !== '127.0.0.1') {
        return `ws://${hostname}:5000/agent-hub`
      }
    }
    return 'ws://192.168.20.159:5000/agent-hub'
  }

  const [hubUrl, setHubUrl] = useState(getInitialHubUrl)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [results, setResults] = useState<{
    succeededCount: number
    failedCount: number
    totalRequested: number
    details: Array<{ hostId: string; hostname: string; success: boolean; message: string }>
  } | null>(null)

  const batchAdoptMutation = useBatchAdoptNodes()

  useEffect(() => {
    if (open) {
      setErrorMessage(null)
      setResults(null)
    }
  }, [open])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    if (hosts.length === 0) {
      setErrorMessage('No hosts selected for adoption.')
      return
    }

    try {
      const resp = await batchAdoptMutation.mutateAsync({
        hosts: hosts.map((h) => ({
          hostId: h.id,
          targetHost: h.ipAddress,
          hostname: h.hostname,
        })),
        port: Number(port) || 22,
        username: username.trim(),
        password: authType === 'password' ? password || null : null,
        privateKey: authType === 'key' ? privateKey || null : null,
        hubUrl: hubUrl.trim() || null,
      })

      setResults({
        succeededCount: resp.succeededCount,
        failedCount: resp.failedCount,
        totalRequested: resp.totalRequested || hosts.length,
        details: resp.results,
      })
      onSuccess?.()
    } catch (err: any) {
      setErrorMessage(err.message || 'Failed to complete mass adoption.')
    }
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="lg">
      <DialogHeader onClose={onClose}>
        <div className="flex items-center gap-2">
          <div className="p-1.5 bg-sky-950/60 border border-sky-800/50 rounded-md text-sky-400">
            <Shield className="h-4 w-4" />
          </div>
          <DialogTitle>Mass Adopt Hosts & Install Agent</DialogTitle>
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
                    Mass Adoption Complete
                  </h4>
                  <p className="text-xs text-zinc-400">
                    Successfully bootstrapped and connected {results.succeededCount} of {results.totalRequested} hosts.
                  </p>
                </div>
              </div>

              <div className="max-h-60 overflow-y-auto space-y-1.5 pt-2 border-t border-zinc-800/60">
                {results.details.map((item) => (
                  <div
                    key={item.hostId}
                    className={`flex items-center justify-between p-2.5 rounded-lg text-xs ${
                      item.success
                        ? 'bg-emerald-950/20 border border-emerald-900/30 text-emerald-300'
                        : 'bg-red-950/20 border border-red-900/30 text-red-300'
                    }`}
                  >
                    <div className="flex items-center gap-2">
                      <Server className="h-3.5 w-3.5 shrink-0 opacity-70" />
                      <span className="font-mono font-medium">{item.hostname}</span>
                    </div>
                    <span className="text-[11px] truncate max-w-xs text-right">
                      {item.success ? 'Agent online & active' : item.message || 'Failed'}
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

            {/* Target Hosts Summary */}
            <div className="p-3.5 bg-zinc-950/60 border border-zinc-800 rounded-lg text-xs space-y-2">
              <div className="flex items-center justify-between text-zinc-300 font-medium">
                <span className="flex items-center gap-1.5">
                  <Users className="h-3.5 w-3.5 text-sky-400" />
                  <span>Selected Targets ({hosts.length} Hosts)</span>
                </span>
                <span className="text-zinc-500 text-[11px]">ControlPlane agent will be installed via SSH</span>
              </div>

              <div className="max-h-36 overflow-y-auto space-y-1.5 pr-1">
                {hosts.map((h) => (
                  <div
                    key={h.id}
                    className="flex items-center justify-between p-2 bg-zinc-900/60 border border-zinc-800/70 rounded text-xs"
                  >
                    <div className="flex items-center gap-2 truncate">
                      <Server className="h-3.5 w-3.5 text-zinc-400 shrink-0" />
                      <span className="font-medium text-zinc-200">{h.hostname}</span>
                      {h.friendlyName && (
                        <span className="text-[11px] text-zinc-500 truncate">({h.friendlyName})</span>
                      )}
                    </div>
                    <div className="flex items-center gap-3 shrink-0">
                      <span className="font-mono text-zinc-400 text-[11px]">{h.ipAddress}</span>
                      <span
                        className={`text-[10px] px-1.5 py-0.5 rounded border ${
                          h.agent?.installed
                            ? 'bg-emerald-950/50 border-emerald-800/50 text-emerald-400'
                            : 'bg-zinc-800/60 border-zinc-700/60 text-zinc-400'
                        }`}
                      >
                        {h.agent?.installed ? 'Agent Active' : 'No Agent'}
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Shared SSH Credentials */}
            <div className="space-y-4">
              <h4 className="text-xs font-semibold text-zinc-300 uppercase tracking-wider">
                Shared SSH Bootstrap Credentials
              </h4>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <label className="block text-xs font-medium text-zinc-300 mb-1">
                    SSH Username *
                  </label>
                  <Input
                    value={username}
                    onChange={(e) => setUsername(e.target.value)}
                    placeholder="root"
                    required
                  />
                </div>

                <div>
                  <label className="block text-xs font-medium text-zinc-300 mb-1">
                    SSH Port *
                  </label>
                  <Input
                    type="number"
                    value={port}
                    onChange={(e) => setPort(Number(e.target.value))}
                    placeholder="22"
                    required
                  />
                </div>
              </div>

              <div>
                <label className="block text-xs font-medium text-zinc-300 mb-1">
                  Authentication Method
                </label>
                <div className="flex gap-2 mb-3">
                  <button
                    type="button"
                    onClick={() => setAuthType('password')}
                    className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs border transition-colors ${
                      authType === 'password'
                        ? 'bg-sky-950/60 border-sky-600 text-sky-300 font-medium'
                        : 'bg-zinc-900 border-zinc-800 text-zinc-400 hover:text-zinc-200'
                    }`}
                  >
                    <Lock className="h-3.5 w-3.5" />
                    <span>Password</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => setAuthType('key')}
                    className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs border transition-colors ${
                      authType === 'key'
                        ? 'bg-sky-950/60 border-sky-600 text-sky-300 font-medium'
                        : 'bg-zinc-900 border-zinc-800 text-zinc-400 hover:text-zinc-200'
                    }`}
                  >
                    <Key className="h-3.5 w-3.5" />
                    <span>Private Key</span>
                  </button>
                </div>

                {authType === 'password' ? (
                  <Input
                    type="password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="SSH password or sudo password"
                  />
                ) : (
                  <textarea
                    rows={4}
                    value={privateKey}
                    onChange={(e) => setPrivateKey(e.target.value)}
                    placeholder="-----BEGIN OPENSSH PRIVATE KEY-----..."
                    className="w-full bg-zinc-950 border border-zinc-800 rounded-lg p-2.5 text-xs font-mono text-zinc-200 focus:outline-none focus:border-sky-500"
                  />
                )}
              </div>

              <div>
                <label className="block text-xs font-medium text-zinc-300 mb-1">
                  Agent Outbound WebSocket Hub URL
                </label>
                <Input
                  value={hubUrl}
                  onChange={(e) => setHubUrl(e.target.value)}
                  placeholder="ws://192.168.20.159:5000/agent-hub"
                  className="font-mono text-xs"
                />
                <p className="text-[11px] text-zinc-500 mt-1">
                  The address that the daemon on each target host will dial outbound to connect with ControlPlane.
                </p>
              </div>
            </div>
          </DialogBody>

          <DialogFooter>
            <Button
              type="button"
              variant="secondary"
              onClick={onClose}
              disabled={batchAdoptMutation.isPending}
            >
              Cancel
            </Button>
            <Button
              type="submit"
              variant="primary"
              disabled={batchAdoptMutation.isPending}
              className="bg-sky-600 hover:bg-sky-500 text-white gap-2 font-medium"
            >
              {batchAdoptMutation.isPending ? (
                <>
                  <div className="h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                  <span>Bootstrapping Agents...</span>
                </>
              ) : (
                <>
                  <Shield className="h-3.5 w-3.5" />
                  <span>Adopt All ({hosts.length} Hosts)</span>
                </>
              )}
            </Button>
          </DialogFooter>
        </form>
      )}
    </Dialog>
  )
}
