import React, { useState, useEffect } from 'react'
import { X, Shield, Key, Lock, Server, Check, ArrowRight, AlertCircle, Copy, Terminal, Loader2 } from 'lucide-react'
import { useQueryClient } from '@tanstack/react-query'
import type { Host, NodeAdoptionResponse } from '../../api/hosts'
import { useAdoptNode } from './useAdoptNode'
import { AdoptionStepProgress } from './AdoptionStepProgress'
import { useHost, useCreateHost, HOSTS_QUERY_KEY } from './useHosts'

interface AdoptNodeModalProps {
  isOpen: boolean
  onClose: () => void
  host?: Host | null
}

export const AdoptNodeModal: React.FC<AdoptNodeModalProps> = ({
  isOpen,
  onClose,
  host: initialHost,
}) => {
  const [createdHost, setCreatedHost] = useState<Host | null>(null)
  const effectiveHost = initialHost || createdHost

  const initialPlatform = effectiveHost?.osFamily?.toLowerCase().includes('windows') ? 'windows' : 'linux'
  const [platform, setPlatform] = useState<'linux' | 'windows'>(initialPlatform)
  const [winMethod, setWinMethod] = useState<'powershell' | 'ssh'>('powershell')

  // SSH Form fields
  const [targetHost, setTargetHost] = useState(effectiveHost?.ipAddress || '')
  const [hostname, setHostname] = useState(effectiveHost?.hostname || effectiveHost?.friendlyName || '')
  const [port, setPort] = useState(22)
  const [username, setUsername] = useState(platform === 'windows' ? 'Administrator' : 'root')
  const [authType, setAuthType] = useState<'password' | 'key'>('password')
  const [password, setPassword] = useState('')
  const [privateKey, setPrivateKey] = useState('')

  const getInitialHubUrl = () => {
    if (typeof window !== 'undefined') {
      const hostname = window.location.hostname
      if (hostname && hostname !== 'localhost' && hostname !== '127.0.0.1') {
        return `ws://${hostname}:5029/agent-hub`
      }
    }
    return 'ws://192.168.20.159:5029/agent-hub'
  }

  const getHttpBaseUrlFromHubUrl = (currentHubUrl: string) => {
    try {
      const url = new URL(currentHubUrl)
      const protocol = url.protocol === 'wss:' ? 'https:' : 'http:'
      return `${protocol}//${url.host}`
    } catch {
      return 'http://192.168.20.159:5029'
    }
  }

  const [hubUrl, setHubUrl] = useState(getInitialHubUrl)
  const [adoptionResponse, setAdoptionResponse] = useState<NodeAdoptionResponse | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const queryClient = useQueryClient()
  const adoptMutation = useAdoptNode()
  const createHostMutation = useCreateHost()

  // Poll host details while waiting for agent handshake
  const { data: hostDetails } = useHost(effectiveHost?.id, {
    refetchInterval: isOpen ? 2000 : false,
  })
  const isAgentOnlineViaPolling = Boolean(hostDetails?.agent?.installed || hostDetails?.agent?.isOnline)

  useEffect(() => {
    if (initialHost) {
      setTargetHost(initialHost.ipAddress || '')
      setHostname(initialHost.hostname || initialHost.friendlyName || '')
      const isWin = initialHost.osFamily?.toLowerCase().includes('windows')
      setPlatform(isWin ? 'windows' : 'linux')
      setUsername(isWin ? 'Administrator' : 'root')
    }
  }, [initialHost])

  useEffect(() => {
    if (platform === 'windows') {
      setUsername(u => u === 'root' ? 'Administrator' : u)
    } else {
      setUsername(u => u === 'Administrator' ? 'root' : u)
    }
  }, [platform])

  if (!isOpen) return null

  const handleStartSshAdoption = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    try {
      const res = await adoptMutation.mutateAsync({
        hostId: effectiveHost?.id,
        targetHost: targetHost.trim(),
        hostname: hostname.trim() || undefined,
        port: Number(port) || 22,
        username: username.trim(),
        password: password || null,
        privateKey: authType === 'key' ? privateKey : null,
        hubUrl: hubUrl.trim() || null,
      })

      setAdoptionResponse(res)
      if (!res.success) {
        setErrorMessage(res.message)
      }
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Unknown adoption failure'
      setErrorMessage(message)
    }
  }

  const handleRegisterWindowsHost = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    if (!targetHost.trim() || !hostname.trim()) {
      setErrorMessage('Please provide both Hostname and IP Address for the new Windows host.')
      return
    }

    try {
      const newHost = await createHostMutation.mutateAsync({
        hostname: hostname.trim(),
        ipAddress: targetHost.trim(),
        friendlyName: hostname.trim(),
        osFamily: 'windows',
        targetType: 'baremetal',
      })
      setCreatedHost(newHost)
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to register Windows host'
      setErrorMessage(message)
    }
  }

  const httpBaseUrl = getHttpBaseUrlFromHubUrl(hubUrl)
  const powerShellCommand = effectiveHost
    ? `& ([scriptblock]::Create((iwr -UseBasicParsing '${httpBaseUrl}/api/v1/agents/install.ps1').Content)) -HubUrl '${hubUrl}' -Token '${effectiveHost.id}' -NodeId '${effectiveHost.id}'`
    : ''

  const handleCopyCommand = () => {
    if (!powerShellCommand) return
    navigator.clipboard.writeText(powerShellCommand)
    setCopied(true)
    setTimeout(() => setCopied(false), 2500)
  }

  const isCompleted = adoptionResponse?.success === true || (platform === 'windows' && winMethod === 'powershell' && isAgentOnlineViaPolling)
  const isAdopting = adoptMutation.isPending || createHostMutation.isPending
  const showProgress = isAdopting || Boolean(adoptionResponse) || Boolean(errorMessage && winMethod === 'ssh')

  useEffect(() => {
    if (isCompleted) {
      queryClient.invalidateQueries({ queryKey: HOSTS_QUERY_KEY })
    }
  }, [isCompleted, queryClient])

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-xs">
      <div className="relative w-full max-w-xl bg-zinc-900 border border-zinc-800 rounded-2xl shadow-2xl overflow-hidden flex flex-col max-h-[90vh]">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-zinc-800/80 bg-zinc-900/50">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-lg bg-sky-500/10 text-sky-400 border border-sky-500/20">
              <Shield className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-base font-semibold text-zinc-100">
                {effectiveHost ? `Adopt Host: ${effectiveHost.friendlyName || effectiveHost.hostname}` : 'Host Agent Adoption'}
              </h3>
              <p className="text-xs text-zinc-400">
                Deploy lightweight Go daemon with zero inbound ports and outbound-only telemetry
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            disabled={isAdopting}
            className="p-1 rounded-lg text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60 transition-colors disabled:opacity-50"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Platform Selection Tabs */}
        {!showProgress && !isCompleted && (
          <div className="px-6 pt-4 pb-0">
            <div className="grid grid-cols-2 p-1 bg-zinc-950/80 border border-zinc-800/80 rounded-xl text-xs">
              <button
                type="button"
                onClick={() => setPlatform('linux')}
                className={`py-2 rounded-lg font-medium transition-all ${
                  platform === 'linux'
                    ? 'bg-zinc-800 text-zinc-100 shadow-xs'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                Linux (Debian / Ubuntu / RHEL)
              </button>
              <button
                type="button"
                onClick={() => setPlatform('windows')}
                className={`py-2 rounded-lg font-medium transition-all flex items-center justify-center gap-1.5 ${
                  platform === 'windows'
                    ? 'bg-sky-500/20 text-sky-300 border border-sky-500/30 shadow-xs'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <span>Windows Server / Desktop</span>
                <span className="px-1.5 py-0.2 bg-sky-400/20 text-sky-300 text-[10px] rounded font-mono">NEW</span>
              </button>
            </div>
          </div>
        )}

        {/* Content Body */}
        <div className="p-6 overflow-y-auto space-y-5">
          {isCompleted ? (
            <div className="p-5 bg-emerald-950/30 border border-emerald-800/50 rounded-xl flex items-start gap-3.5 animate-in fade-in">
              <div className="p-2.5 bg-emerald-500/20 text-emerald-400 rounded-xl shrink-0 mt-0.5">
                <Check className="w-6 h-6" />
              </div>
              <div className="space-y-1.5">
                <h4 className="text-sm font-semibold text-emerald-300">
                  Adoption Completed Successfully!
                </h4>
                <p className="text-xs text-emerald-400/80 leading-relaxed">
                  The compute node agent daemon is running as a background service and has established its outbound WebSocket link back to the ControlPlane hub.
                </p>
                {effectiveHost && (
                  <div className="pt-2 flex items-center gap-2 text-[11px] font-mono text-emerald-300/90">
                    <span className="px-2 py-0.5 bg-emerald-900/50 rounded">Node ID: {effectiveHost.id}</span>
                    <span className="px-2 py-0.5 bg-emerald-900/50 rounded">{effectiveHost.ipAddress}</span>
                    {hostDetails?.agent?.version && (
                      <span className="px-2 py-0.5 bg-emerald-900/50 rounded">v{hostDetails.agent.version}</span>
                    )}
                  </div>
                )}
              </div>
            </div>
          ) : platform === 'windows' ? (
            <div className="space-y-4">
              {/* Windows Sub-methods: PowerShell vs SSH */}
              <div className="flex items-center gap-3 text-xs border-b border-zinc-800/80 pb-3">
                <button
                  type="button"
                  onClick={() => setWinMethod('powershell')}
                  className={`flex items-center gap-1.5 font-medium px-3 py-1.5 rounded-md transition-colors ${
                    winMethod === 'powershell'
                      ? 'bg-sky-500/15 text-sky-300 border border-sky-500/30'
                      : 'text-zinc-400 hover:text-zinc-200'
                  }`}
                >
                  <Terminal className="w-3.5 h-3.5" />
                  <span>PowerShell Bootstrap (1-Click)</span>
                </button>
                <button
                  type="button"
                  onClick={() => setWinMethod('ssh')}
                  className={`flex items-center gap-1.5 font-medium px-3 py-1.5 rounded-md transition-colors ${
                    winMethod === 'ssh'
                      ? 'bg-sky-500/15 text-sky-300 border border-sky-500/30'
                      : 'text-zinc-400 hover:text-zinc-200'
                  }`}
                >
                  <Lock className="w-3.5 h-3.5" />
                  <span>OpenSSH Server</span>
                </button>
              </div>

              {winMethod === 'powershell' ? (
                <div className="space-y-4">
                  {errorMessage && (
                    <div className="p-3 bg-rose-950/40 border border-rose-800/60 rounded-lg text-xs text-rose-300 flex items-start gap-2">
                      <AlertCircle className="w-4 h-4 text-rose-400 shrink-0 mt-0.5" />
                      <p className="font-mono text-[11px]">{errorMessage}</p>
                    </div>
                  )}

                  {!effectiveHost ? (
                    <form onSubmit={handleRegisterWindowsHost} className="space-y-3">
                      <div className="p-3 bg-zinc-950/50 border border-zinc-800 rounded-lg text-xs text-zinc-400">
                        Specify target Windows Server details to generate the one-click PowerShell installation command.
                      </div>
                      <div className="grid grid-cols-2 gap-3">
                        <div>
                          <label className="block text-xs font-medium text-zinc-300 mb-1">Hostname *</label>
                          <input
                            type="text"
                            required
                            value={hostname}
                            onChange={(e) => setHostname(e.target.value)}
                            placeholder="win-server-01"
                            className="w-full px-3 py-1.5 text-xs bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 placeholder-zinc-600 focus:outline-none focus:border-sky-500"
                          />
                        </div>
                        <div>
                          <label className="block text-xs font-medium text-zinc-300 mb-1">IP Address *</label>
                          <input
                            type="text"
                            required
                            value={targetHost}
                            onChange={(e) => setTargetHost(e.target.value)}
                            placeholder="192.168.1.200"
                            className="w-full px-3 py-1.5 text-xs bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 placeholder-zinc-600 focus:outline-none focus:border-sky-500"
                          />
                        </div>
                      </div>
                      <button
                        type="submit"
                        disabled={createHostMutation.isPending}
                        className="w-full py-2 bg-sky-500 hover:bg-sky-400 text-zinc-950 font-semibold text-xs rounded-lg transition-colors flex items-center justify-center gap-2"
                      >
                        {createHostMutation.isPending ? (
                          <>
                            <Loader2 className="w-3.5 h-3.5 animate-spin" />
                            <span>Registering Host...</span>
                          </>
                        ) : (
                          <>
                            <span>Register Host & Generate Command</span>
                            <ArrowRight className="w-3.5 h-3.5" />
                          </>
                        )}
                      </button>
                    </form>
                  ) : (
                    <div className="space-y-4">
                      <div>
                        <label className="block text-xs font-medium text-zinc-300 mb-1 flex items-center justify-between">
                          <span>ControlPlane Hub WebSocket URL</span>
                          <span className="text-[11px] text-zinc-500 font-normal">LAN address reachable from Windows node</span>
                        </label>
                        <input
                          type="text"
                          value={hubUrl}
                          onChange={(e) => setHubUrl(e.target.value)}
                          placeholder="ws://192.168.20.159:5029/agent-hub"
                          className="w-full px-3 py-1.5 text-xs font-mono bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 placeholder-zinc-600 focus:outline-none focus:border-sky-500"
                        />
                      </div>

                      <div>
                        <div className="flex items-center justify-between text-xs mb-1.5">
                          <span className="font-medium text-zinc-300">
                            Run in an Elevated PowerShell Prompt (Administrator):
                          </span>
                          <button
                            type="button"
                            onClick={handleCopyCommand}
                            className="flex items-center gap-1 text-[11px] text-sky-400 hover:text-sky-300 transition-colors"
                          >
                            {copied ? (
                              <>
                                <Check className="w-3 h-3 text-emerald-400" />
                                <span className="text-emerald-400">Copied!</span>
                              </>
                            ) : (
                              <>
                                <Copy className="w-3 h-3" />
                                <span>Copy Snippet</span>
                              </>
                            )}
                          </button>
                        </div>
                        <div className="relative group">
                          <pre className="p-3 bg-zinc-950 border border-zinc-800 rounded-xl font-mono text-[11px] text-sky-300/90 whitespace-pre-wrap break-all select-all">
                            {powerShellCommand}
                          </pre>
                        </div>
                      </div>

                      {/* Live Listener Beacon */}
                      <div className="p-4 bg-zinc-950/60 border border-zinc-800/80 rounded-xl flex items-center justify-between">
                        <div className="flex items-center gap-3">
                          <div className="relative flex h-3 w-3">
                            <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-sky-400 opacity-75"></span>
                            <span className="relative inline-flex rounded-full h-3 w-3 bg-sky-500"></span>
                          </div>
                          <div>
                            <p className="text-xs font-medium text-zinc-200">
                              Awaiting Agent Handshake
                            </p>
                            <p className="text-[11px] text-zinc-500">
                              Once the command executes, the agent dials into <span className="font-mono text-zinc-400">{hubUrl}</span>.
                            </p>
                          </div>
                        </div>
                        <Loader2 className="w-4 h-4 text-sky-400 animate-spin" />
                      </div>

                      <div className="space-y-1.5 text-[11px] text-zinc-400">
                        <p className="font-medium text-zinc-300">What this command executes:</p>
                        <ul className="list-disc list-inside space-y-0.5 text-zinc-400">
                          <li>Downloads <span className="font-mono text-zinc-300">controlplane-agent-windows-amd64.exe</span></li>
                          <li>Registers Windows Service <span className="font-mono text-zinc-300">ControlPlaneAgent</span> (Auto-start)</li>
                          <li>Configures auto-recovery upon service interruption</li>
                          <li>Initiates outbound WebSocket telemetry connection</li>
                        </ul>
                      </div>
                    </div>
                  )}
                </div>
              ) : (
                /* Windows SSH Bootstrap Form */
                <form id="adopt-form" onSubmit={handleStartSshAdoption} className="space-y-4">
                  {errorMessage && (
                    <div className="p-3 bg-rose-950/40 border border-rose-800/60 rounded-lg text-xs text-rose-300 flex items-start gap-2">
                      <AlertCircle className="w-4 h-4 text-rose-400 shrink-0 mt-0.5" />
                      <p className="font-mono text-[11px]">{errorMessage}</p>
                    </div>
                  )}
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                        Target Host (IP or Domain) <span className="text-rose-400">*</span>
                      </label>
                      <input
                        type="text"
                        required
                        value={targetHost}
                        onChange={(e) => setTargetHost(e.target.value)}
                        placeholder="192.168.1.150"
                        className="w-full px-3 py-2 text-sm bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 placeholder-zinc-600 focus:outline-none focus:border-sky-500"
                      />
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-zinc-300 mb-1.5">SSH Port</label>
                      <input
                        type="number"
                        value={port}
                        onChange={(e) => setPort(Number(e.target.value))}
                        className="w-full px-3 py-2 text-sm bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 focus:outline-none focus:border-sky-500"
                      />
                    </div>
                  </div>

                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-xs font-medium text-zinc-300 mb-1.5">Windows Administrator</label>
                      <input
                        type="text"
                        required
                        value={username}
                        onChange={(e) => setUsername(e.target.value)}
                        placeholder="Administrator"
                        className="w-full px-3 py-2 text-sm bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 placeholder-zinc-600 focus:outline-none focus:border-sky-500"
                      />
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-zinc-300 mb-1.5">Password</label>
                      <input
                        type="password"
                        required
                        value={password}
                        onChange={(e) => setPassword(e.target.value)}
                        placeholder="••••••••••••"
                        className="w-full px-3 py-2 text-sm bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 placeholder-zinc-600 focus:outline-none focus:border-sky-500"
                      />
                    </div>
                  </div>
                </form>
              )}
            </div>
          ) : !showProgress ? (
            /* Linux SSH Form */
            <form id="adopt-form" onSubmit={handleStartSshAdoption} className="space-y-4">
              {errorMessage && (
                <div className="p-3 bg-rose-950/40 border border-rose-800/60 rounded-lg text-xs text-rose-300 flex items-start gap-2">
                  <AlertCircle className="w-4 h-4 text-rose-400 shrink-0 mt-0.5" />
                  <div className="space-y-1">
                    <p className="font-semibold">Adoption Failed</p>
                    <p className="font-mono text-[11px] text-rose-300/90">{errorMessage}</p>
                  </div>
                </div>
              )}
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                    Target Host (IP or Domain) <span className="text-rose-400">*</span>
                    {effectiveHost && (
                      <span className="text-[11px] text-sky-400 font-normal ml-2">
                        (Pre-filled from inventory)
                      </span>
                    )}
                  </label>
                  <input
                    type="text"
                    required
                    value={targetHost}
                    onChange={(e) => setTargetHost(e.target.value)}
                    placeholder="192.168.1.150"
                    className="w-full px-3 py-2 text-sm bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 placeholder-zinc-600 focus:outline-none focus:border-sky-500 focus:ring-1 focus:ring-sky-500"
                  />
                </div>
                <div>
                  <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                    SSH Port
                  </label>
                  <input
                    type="number"
                    value={port}
                    onChange={(e) => setPort(Number(e.target.value))}
                    className="w-full px-3 py-2 text-sm bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 focus:outline-none focus:border-sky-500 focus:ring-1 focus:ring-sky-500"
                  />
                </div>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                    SSH Username
                  </label>
                  <input
                    type="text"
                    required
                    value={username}
                    onChange={(e) => setUsername(e.target.value)}
                    placeholder="root"
                    className="w-full px-3 py-2 text-sm bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 placeholder-zinc-600 focus:outline-none focus:border-sky-500 focus:ring-1 focus:ring-sky-500"
                  />
                </div>
                <div>
                  <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                    Hostname / Label (Optional)
                  </label>
                  <input
                    type="text"
                    value={hostname}
                    onChange={(e) => setHostname(e.target.value)}
                    placeholder="srv-node-01"
                    className="w-full px-3 py-2 text-sm bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 placeholder-zinc-600 focus:outline-none focus:border-sky-500 focus:ring-1 focus:ring-sky-500"
                  />
                </div>
              </div>

              {/* Auth Method Selector */}
              <div>
                <label className="block text-xs font-medium text-zinc-300 mb-2">
                  Authentication Method
                </label>
                <div className="grid grid-cols-2 gap-3">
                  <button
                    type="button"
                    onClick={() => setAuthType('password')}
                    className={`flex items-center justify-center gap-2 px-3 py-2 text-xs font-medium rounded-lg border transition-all ${
                      authType === 'password'
                        ? 'bg-sky-500/10 border-sky-500/40 text-sky-300 shadow-xs'
                        : 'bg-zinc-950 border-zinc-800 text-zinc-400 hover:text-zinc-200'
                    }`}
                  >
                    <Lock className="w-3.5 h-3.5" />
                    SSH Password
                  </button>
                  <button
                    type="button"
                    onClick={() => setAuthType('key')}
                    className={`flex items-center justify-center gap-2 px-3 py-2 text-xs font-medium rounded-lg border transition-all ${
                      authType === 'key'
                        ? 'bg-sky-500/10 border-sky-500/40 text-sky-300 shadow-xs'
                        : 'bg-zinc-950 border-zinc-800 text-zinc-400 hover:text-zinc-200'
                    }`}
                  >
                    <Key className="w-3.5 h-3.5" />
                    Private Key (Ed25519/RSA)
                  </button>
                </div>
              </div>

              {authType === 'password' ? (
                <div>
                  <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                    SSH Password
                  </label>
                  <input
                    type="password"
                    required
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="••••••••••••"
                    className="w-full px-3 py-2 text-sm bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 placeholder-zinc-600 focus:outline-none focus:border-sky-500 focus:ring-1 focus:ring-sky-500"
                  />
                </div>
              ) : (
                <div className="space-y-3">
                  <div>
                    <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                      Private Key PEM Content
                    </label>
                    <textarea
                      rows={4}
                      required
                      value={privateKey}
                      onChange={(e) => setPrivateKey(e.target.value)}
                      placeholder="-----BEGIN OPENSSH PRIVATE KEY-----&#10;..."
                      className="w-full px-3 py-2 text-xs font-mono bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 placeholder-zinc-600 focus:outline-none focus:border-sky-500 focus:ring-1 focus:ring-sky-500 resize-none"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                      Sudo Password <span className="text-zinc-500 font-normal">(Optional, if user requires sudo password)</span>
                    </label>
                    <input
                      type="password"
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      placeholder="••••••••••••"
                      className="w-full px-3 py-2 text-sm bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 placeholder-zinc-600 focus:outline-none focus:border-sky-500 focus:ring-1 focus:ring-sky-500"
                    />
                  </div>
                </div>
              )}

              <div className="pt-1">
                <label className="block text-xs font-medium text-zinc-300 mb-1.5 flex items-center justify-between">
                  <span>
                    ControlPlane Hub WebSocket URL <span className="text-rose-400">*</span>
                  </span>
                  <span className="text-[11px] text-zinc-500 font-normal">
                    Remote agent dials back here
                  </span>
                </label>
                <input
                  type="text"
                  required
                  value={hubUrl}
                  onChange={(e) => setHubUrl(e.target.value)}
                  placeholder="ws://192.168.20.159:5029/agent-hub"
                  className="w-full px-3 py-2 text-xs font-mono bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-100 placeholder-zinc-600 focus:outline-none focus:border-sky-500 focus:ring-1 focus:ring-sky-500"
                />
                <p className="text-[11px] text-zinc-500 mt-1">
                  Must be reachable from the target host (use this server&apos;s LAN IP or DNS name, never localhost).
                </p>
              </div>
            </form>
          ) : (
            <div className="space-y-4">
              <AdoptionStepProgress
                steps={adoptionResponse?.steps || []}
                isAdopting={isAdopting}
                error={errorMessage}
              />
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between px-6 py-4 border-t border-zinc-800/80 bg-zinc-900/50">
          <div className="flex items-center gap-2 text-xs text-zinc-500">
            <Server className="w-4 h-4" />
            <span>Outbound-only WebSocket (Zero inbound firewall ports)</span>
          </div>

          <div className="flex items-center gap-3">
            {isCompleted ? (
              <button
                type="button"
                onClick={onClose}
                className="px-4 py-2 text-xs font-semibold text-zinc-950 bg-emerald-400 hover:bg-emerald-300 rounded-lg transition-colors flex items-center gap-1.5"
              >
                <span>Done</span>
                <Check className="w-3.5 h-3.5" />
              </button>
            ) : !isAdopting && !showProgress ? (
              <>
                <button
                  type="button"
                  onClick={onClose}
                  className="px-4 py-2 text-xs font-medium text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60 rounded-lg transition-colors"
                >
                  Cancel
                </button>
                {platform === 'linux' || winMethod === 'ssh' ? (
                  <button
                    type="submit"
                    form="adopt-form"
                    className="px-4 py-2 text-xs font-semibold text-zinc-950 bg-sky-400 hover:bg-sky-300 rounded-lg transition-colors flex items-center gap-1.5 shadow-xs"
                  >
                    <span>Start Adoption</span>
                    <ArrowRight className="w-3.5 h-3.5" />
                  </button>
                ) : null}
              </>
            ) : !isAdopting && showProgress && !isCompleted ? (
              <>
                <button
                  type="button"
                  onClick={() => {
                    setAdoptionResponse(null)
                    setErrorMessage(null)
                  }}
                  className="px-4 py-2 text-xs font-medium text-zinc-300 bg-zinc-800 hover:bg-zinc-700 rounded-lg transition-colors"
                >
                  Try Again
                </button>
                <button
                  type="button"
                  onClick={onClose}
                  className="px-4 py-2 text-xs font-medium text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60 rounded-lg transition-colors"
                >
                  Close
                </button>
              </>
            ) : null}
          </div>
        </div>
      </div>
    </div>
  )
}
