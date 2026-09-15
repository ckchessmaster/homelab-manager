import React, { Component, type ReactNode } from 'react'
import { AlertCircle, RefreshCw, Home } from 'lucide-react'
import { Button } from './ui/button'

interface ErrorBoundaryProps {
  children: ReactNode
  fallbackTitle?: string
  onReset?: () => void
}

interface ErrorBoundaryState {
  hasError: boolean
  error: Error | null
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error }
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo) {
    console.error('Unhandled React Error caught by ErrorBoundary:', error, errorInfo)
  }

  handleReset = () => {
    this.setState({ hasError: false, error: null })
    if (this.props.onReset) {
      this.props.onReset()
    }
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="min-h-[400px] w-full flex items-center justify-center p-6 bg-zinc-950/60 rounded-xl border border-rose-900/40">
          <div className="max-w-lg w-full p-6 bg-zinc-900 border border-rose-800/60 rounded-2xl shadow-xl space-y-4">
            <div className="flex items-center gap-3">
              <div className="p-2.5 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-400">
                <AlertCircle className="w-6 h-6" />
              </div>
              <div>
                <h3 className="text-base font-bold text-zinc-100">
                  {this.props.fallbackTitle || 'Component Encountered an Error'}
                </h3>
                <p className="text-xs text-zinc-400 mt-0.5">
                  An unexpected exception occurred while rendering this section.
                </p>
              </div>
            </div>

            {this.state.error?.message && (
              <div className="p-3 bg-zinc-950 border border-zinc-800 rounded-lg font-mono text-xs text-rose-300 overflow-x-auto whitespace-pre-wrap break-all max-h-36">
                {this.state.error.message}
              </div>
            )}

            <div className="flex items-center gap-3 pt-2">
              <Button
                variant="primary"
                size="sm"
                onClick={this.handleReset}
                className="text-xs gap-1.5 bg-rose-600 hover:bg-rose-500 text-white"
              >
                <RefreshCw className="w-3.5 h-3.5" />
                Retry Render
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => window.location.reload()}
                className="text-xs gap-1.5 border-zinc-700 bg-zinc-800 text-zinc-300 hover:text-white"
              >
                <Home className="w-3.5 h-3.5" />
                Reload Application
              </Button>
            </div>
          </div>
        </div>
      )
    }

    return this.props.children
  }
}
