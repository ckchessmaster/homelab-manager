import React, { useState } from 'react'
import { useAuthUser } from './useAuthUser'
import { getAuthConfig } from './authConfig'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import {
  Shield,
  Key,
  LogIn,
  Eye,
  EyeOff,
  Server,
  AlertCircle,
  ArrowRight,
  ShieldCheck,
} from 'lucide-react'

export const AuthGatePage: React.FC = () => {
  const { authMode, login, switchAuthMode } = useAuthUser()
  const [apiKeyInput, setApiKeyInput] = useState('')
  const [showKey, setShowKey] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  const handleApiKeySubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    const trimmed = apiKeyInput.trim()
    if (!trimmed) {
      setErrorMessage('Please enter a valid ControlPlane API key.')
      return
    }

    setIsSubmitting(true)
    try {
      await login(trimmed)
    } catch (err: any) {
      setErrorMessage(err.message || 'Failed to authenticate with API key.')
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleOidcLogin = async () => {
    setErrorMessage(null)
    setIsSubmitting(true)
    try {
      await login()
    } catch (err: any) {
      setErrorMessage(err.message || 'Failed to initiate OIDC sign in.')
      setIsSubmitting(false)
    }
  }

  return (
    <div className="min-h-screen w-full bg-zinc-950 text-zinc-100 flex flex-col justify-between items-center p-6 relative overflow-hidden">
      {/* Background ambient lighting */}
      <div className="absolute top-1/4 left-1/2 -translate-x-1/2 -translate-y-1/2 w-96 h-96 bg-emerald-500/10 rounded-full blur-3xl pointer-events-none" />
      <div className="absolute bottom-1/4 left-1/2 -translate-x-1/2 translate-y-1/2 w-96 h-96 bg-sky-500/10 rounded-full blur-3xl pointer-events-none" />

      {/* Header Logo */}
      <header className="w-full max-w-4xl flex items-center justify-between z-10">
        <div className="flex items-center gap-2.5">
          <div className="h-8 w-8 rounded-lg bg-gradient-to-tr from-emerald-600 to-sky-500 flex items-center justify-center text-white shadow-lg shadow-emerald-950">
            <Server className="h-4 w-4" />
          </div>
          <div>
            <span className="font-bold text-sm tracking-tight text-zinc-100">ControlPlane</span>
            <span className="ml-1.5 px-1.5 py-0.5 rounded text-[10px] font-mono bg-zinc-900 border border-zinc-800 text-zinc-400">
              v0.1.0-alpha
            </span>
          </div>
        </div>

        <div className="text-xs text-zinc-500 font-mono">
          Mode: <span className="text-zinc-300 font-semibold">{authMode === 'api_key' ? 'API Key' : 'OIDC'}</span>
        </div>
      </header>

      {/* Main Authentication Card */}
      <main className="w-full max-w-md z-10 animate-in fade-in zoom-in-95 duration-200">
        <div className="bg-zinc-900/80 border border-zinc-800/80 backdrop-blur-xl rounded-2xl p-8 shadow-2xl space-y-6">
          <div className="space-y-2 text-center">
            <div className="mx-auto w-12 h-12 rounded-xl bg-zinc-950 border border-zinc-800 flex items-center justify-center text-emerald-400 mb-4 shadow-inner">
              {authMode === 'api_key' ? (
                <Key className="h-6 w-6 text-emerald-400" />
              ) : (
                <Shield className="h-6 w-6 text-sky-400" />
              )}
            </div>
            <h1 className="text-xl font-bold tracking-tight text-zinc-100">
              {authMode === 'api_key' ? 'ControlPlane API Key Sign In' : 'Single Sign-On Required'}
            </h1>
            <p className="text-xs text-zinc-400 leading-relaxed">
              {authMode === 'api_key'
                ? 'Enter your ControlPlane API key to access your homelab management plane. API Key mode grants full administrative access.'
                : 'Authenticate with your identity provider (Zitadel OIDC) to access your clusters, nodes, and workloads.'}
            </p>
          </div>

          {errorMessage && (
            <div className="p-3.5 bg-red-950/40 border border-red-800/60 rounded-xl flex items-start gap-2.5 text-xs text-red-300">
              <AlertCircle className="h-4 w-4 shrink-0 mt-0.5" />
              <span>{errorMessage}</span>
            </div>
          )}

          {authMode === 'api_key' ? (
            <form onSubmit={handleApiKeySubmit} className="space-y-4">
              <div className="space-y-1.5">
                <label className="block text-xs font-medium text-zinc-300">
                  ControlPlane API Key
                </label>
                <div className="relative">
                  <Input
                    type={showKey ? 'text' : 'password'}
                    value={apiKeyInput}
                    onChange={(e) => setApiKeyInput(e.target.value)}
                    placeholder="Enter X-ControlPlane-Key..."
                    className="pr-10 font-mono text-xs bg-zinc-950/90"
                    autoFocus
                  />
                  <button
                    type="button"
                    onClick={() => setShowKey(!showKey)}
                    className="absolute right-3 top-1/2 -translate-y-1/2 text-zinc-500 hover:text-zinc-300 transition-colors cursor-pointer"
                    title={showKey ? 'Hide key' : 'Show key'}
                  >
                    {showKey ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                  </button>
                </div>
                <p className="text-[11px] text-zinc-500">
                  Header sent as <code className="text-zinc-400">X-ControlPlane-Key</code> for all requests.
                </p>
              </div>

              <div className="p-3 bg-emerald-950/30 border border-emerald-800/40 rounded-xl flex items-center gap-2.5 text-xs text-emerald-300">
                <ShieldCheck className="h-4 w-4 shrink-0 text-emerald-400" />
                <span>Full access enabled: Admin privileges across all nodes, adapters, and DAGs.</span>
              </div>

              <Button
                type="submit"
                variant="primary"
                disabled={isSubmitting}
                className="w-full bg-emerald-600 hover:bg-emerald-500 text-white font-medium py-2.5 gap-2"
              >
                {isSubmitting ? (
                  <>
                    <div className="h-4 w-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                    <span>Connecting...</span>
                  </>
                ) : (
                  <>
                    <Key className="h-4 w-4" />
                    <span>Connect & Sign In</span>
                  </>
                )}
              </Button>
            </form>
          ) : (
            <div className="space-y-4">
              <Button
                onClick={handleOidcLogin}
                variant="primary"
                disabled={isSubmitting}
                className="w-full bg-sky-600 hover:bg-sky-500 text-white font-medium py-2.5 gap-2"
              >
                {isSubmitting ? (
                  <>
                    <div className="h-4 w-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                    <span>Redirecting to Zitadel...</span>
                  </>
                ) : (
                  <>
                    <LogIn className="h-4 w-4" />
                    <span>Sign In with Zitadel</span>
                  </>
                )}
              </Button>

              <div className="text-center pt-1">
                <span className="text-[11px] text-zinc-500 font-mono">
                  Identity Provider:{' '}
                  <span className="text-zinc-400 font-medium">{getAuthConfig().authority}</span>
                </span>
              </div>
            </div>
          )}

          {/* Mode Switcher Footer */}
          <div className="pt-4 border-t border-zinc-800/80 text-center">
            {authMode === 'api_key' ? (
              <button
                type="button"
                onClick={() => switchAuthMode?.('oidc')}
                className="text-xs text-zinc-400 hover:text-sky-400 transition-colors inline-flex items-center gap-1 cursor-pointer"
              >
                <span>Switch to Zitadel Single Sign-On (OIDC)</span>
                <ArrowRight className="h-3 w-3" />
              </button>
            ) : (
              <button
                type="button"
                onClick={() => switchAuthMode?.('api_key')}
                className="text-xs text-zinc-400 hover:text-emerald-400 transition-colors inline-flex items-center gap-1 cursor-pointer"
              >
                <span>Switch to API Key Authentication</span>
                <ArrowRight className="h-3 w-3" />
              </button>
            )}
          </div>
        </div>
      </main>

      {/* Footer */}
      <footer className="w-full max-w-4xl text-center text-[11px] text-zinc-600 z-10">
        ControlPlane • Homelab Orchestration & Management Engine • Standby-Ready
      </footer>
    </div>
  )
}
