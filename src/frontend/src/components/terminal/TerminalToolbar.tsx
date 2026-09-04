import React from 'react'
import { Terminal as TerminalIcon, Copy, Trash2, ArrowDownCircle, CheckCircle2, AlertCircle, Loader2 } from 'lucide-react'
import { Button } from '../ui/button'

interface TerminalToolbarProps {
  status: 'idle' | 'connecting' | 'connected' | 'completed' | 'failed'
  title?: string
  lineCount?: number
  autoScroll: boolean
  onToggleAutoScroll: () => void
  onClear: () => void
  onCopy: () => void
}

export const TerminalToolbar: React.FC<TerminalToolbarProps> = ({
  status,
  title = 'Console Output',
  lineCount = 0,
  autoScroll,
  onToggleAutoScroll,
  onClear,
  onCopy,
}) => {
  const getStatusBadge = () => {
    switch (status) {
      case 'connecting':
        return (
          <span className="flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-amber-500/10 text-amber-400 border border-amber-500/20">
            <Loader2 className="w-3 h-3 animate-spin" />
            Connecting
          </span>
        )
      case 'connected':
        return (
          <span className="flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
            <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse" />
            Live Stream
          </span>
        )
      case 'completed':
        return (
          <span className="flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-zinc-500/10 text-zinc-300 border border-zinc-700/50">
            <CheckCircle2 className="w-3 h-3 text-emerald-400" />
            Completed
          </span>
        )
      case 'failed':
        return (
          <span className="flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-rose-500/10 text-rose-400 border border-rose-500/20">
            <AlertCircle className="w-3 h-3 text-rose-400" />
            Failed
          </span>
        )
      default:
        return (
          <span className="flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-zinc-800 text-zinc-400">
            Idle
          </span>
        )
    }
  }

  return (
    <div className="flex items-center justify-between px-3 py-2 bg-zinc-900/90 border border-zinc-800 rounded-t-lg backdrop-blur-sm">
      <div className="flex items-center gap-2 min-w-0">
        <TerminalIcon className="w-4 h-4 text-zinc-400 shrink-0" />
        <span className="text-xs font-mono font-medium text-zinc-200 truncate">{title}</span>
        {getStatusBadge()}
        {lineCount > 0 && (
          <span className="text-[11px] text-zinc-500 font-mono hidden sm:inline">
            {lineCount} lines
          </span>
        )}
      </div>

      <div className="flex items-center gap-1 shrink-0">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onToggleAutoScroll}
          className={`h-7 px-2 text-xs gap-1 transition-colors ${
            autoScroll ? 'text-sky-400 bg-sky-950/40 hover:bg-sky-900/40' : 'text-zinc-400 hover:text-zinc-200'
          }`}
          title={autoScroll ? 'Auto-scroll enabled' : 'Auto-scroll paused'}
        >
          <ArrowDownCircle className="w-3.5 h-3.5" />
          <span className="hidden md:inline">Auto-scroll</span>
        </Button>

        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onCopy}
          className="h-7 px-2 text-xs text-zinc-400 hover:text-zinc-200 gap-1"
          title="Copy output to clipboard"
        >
          <Copy className="w-3.5 h-3.5" />
          <span className="hidden md:inline">Copy</span>
        </Button>

        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onClear}
          className="h-7 px-2 text-xs text-zinc-400 hover:text-rose-400 gap-1"
          title="Clear console"
        >
          <Trash2 className="w-3.5 h-3.5" />
        </Button>
      </div>
    </div>
  )
}
