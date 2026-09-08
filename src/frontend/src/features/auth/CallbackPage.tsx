import { useEffect } from 'react'
import { useAuth } from 'react-oidc-context'
import { Loader2, AlertCircle } from 'lucide-react'

export function CallbackPage() {
  const auth = useAuth()

  useEffect(() => {
    if (auth.isAuthenticated) {
      window.location.replace('/')
    }
  }, [auth.isAuthenticated])

  if (auth.error) {
    return (
      <div className="min-h-screen bg-zinc-950 flex items-center justify-center p-4">
        <div className="max-w-md w-full p-6 bg-zinc-900/80 border border-zinc-800 rounded-xl space-y-4 text-center">
          <div className="h-12 w-12 rounded-full bg-rose-950/60 border border-rose-800/80 text-rose-400 flex items-center justify-center mx-auto">
            <AlertCircle className="h-6 w-6" />
          </div>
          <h2 className="text-base font-semibold text-zinc-100">Authentication Failed</h2>
          <p className="text-xs text-zinc-400 leading-relaxed">{auth.error.message}</p>
          <a
            href="/"
            className="inline-block px-4 py-2 bg-zinc-800 hover:bg-zinc-700 text-xs font-medium text-zinc-200 rounded-lg transition-colors"
          >
            Return to Dashboard
          </a>
        </div>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-zinc-950 flex items-center justify-center p-4">
      <div className="flex flex-col items-center gap-3">
        <Loader2 className="h-8 w-8 text-emerald-400 animate-spin" />
        <p className="text-xs text-zinc-400">Exchanging authorization code with Zitadel...</p>
      </div>
    </div>
  )
}
