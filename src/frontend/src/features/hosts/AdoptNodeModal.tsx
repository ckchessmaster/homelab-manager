import React, { useState } from 'react'
import { X, Shield, Key, Lock, Server, Check, ArrowRight, AlertCircle } from 'lucide-react'
import type { Host, NodeAdoptionResponse } from '../../api/hosts'
import { useAdoptNode } from './useAdoptNode'
import { AdoptionStepProgress } from './AdoptionStepProgress'

interface AdoptNodeModalProps {
  isOpen: boolean
  onClose: () => void
  host?: Host | null
}

export const AdoptNodeModal: React.FC<AdoptNodeModalProps> = ({
  isOpen,
  onClose,
  host,
}) => {
  const [targetHost, setTargetHost] = useState(host?.ipAddress || '')
  const [hostname, setHostname] = useState(host?.hostname || host?.friendlyName || '')
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

  const [adoptionResponse, setAdoptionResponse] = useState<NodeAdoptionResponse | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const adoptMutation = useAdoptNode()

  if (!isOpen) return null

  const handleStartAdoption = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    try {
      const res = await adoptMutation.mutateAsync({
        hostId: host?.id,
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

  const isCompleted = adoptionResponse?.success === true
  const isAdopting = adoptMutation.isPending
  const showProgress = isAdopting || Boolean(adoptionResponse) || Boolean(errorMessage)

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
                {host ? `Adopt Host: ${host.friendlyName || host.hostname}` : 'One-Click Agent Adoption'}
              </h3>
              <p className="text-xs text-zinc-400">
                Deploy lightweight Go daemon over SSH with zero listening ports
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

        {/* Content Body */}
        <div className="p-6 overflow-y-auto space-y-5">
          {!showProgress ? (
            <form id="adopt-form" onSubmit={handleStartAdoption} className="space-y-4">
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
                    {host && (
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
                  placeholder="ws://192.168.20.159:5000/agent-hub"
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

              {isCompleted && (
                <div className="p-4 bg-emerald-950/30 border border-emerald-800/50 rounded-xl flex items-center gap-3">
                  <div className="p-2 bg-emerald-500/20 text-emerald-400 rounded-lg">
                    <Check className="w-5 h-5" />
                  </div>
                  <div>
                    <h4 className="text-sm font-semibold text-emerald-300">
                      Adoption Completed Successfully!
                    </h4>
                    <p className="text-xs text-emerald-400/80">
                      The compute node agent daemon is running and has established its outbound telemetry link.
                    </p>
                  </div>
                </div>
              )}
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
                <button
                  type="submit"
                  form="adopt-form"
                  className="px-4 py-2 text-xs font-semibold text-zinc-950 bg-sky-400 hover:bg-sky-300 rounded-lg transition-colors flex items-center gap-1.5 shadow-xs"
                >
                  <span>Start Adoption</span>
                  <ArrowRight className="w-3.5 h-3.5" />
                </button>
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
