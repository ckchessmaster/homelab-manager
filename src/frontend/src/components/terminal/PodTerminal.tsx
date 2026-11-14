import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Terminal } from '@xterm/xterm'
import { FitAddon } from '@xterm/addon-fit'
import '@xterm/xterm/css/xterm.css'
import {
  Terminal as TerminalIcon,
  RefreshCw,
  PlugZap,
  Unplug,
  Copy,
  Check,
  Trash2,
  AlertCircle,
  Loader2,
  Box,
} from 'lucide-react'
import { Button } from '../ui/button'
import { copyToClipboard } from './TerminalCanvas'
import {
  POD_SHELL_OPTIONS,
  usePodTerminal,
  type PodTerminalAttachedInfo,
  type PodTerminalStatus,
} from './usePodTerminal'

export interface PodTerminalProps {
  clusterId?: string | null
  clusterName?: string | null
  namespace: string
  podName: string
  containers?: string[]
  initialContainer?: string | null
  initialShell?: string
  enabled?: boolean
  className?: string
  onContainerChange?: (container: string) => void
  onShellChange?: (shell: string) => void
}

const EMPTY_CONTAINERS: string[] = []

function getAllBufferText(term: Terminal | null): string {
  if (!term) return ''
  const buffer = term.buffer?.active
  if (!buffer) return ''
  const lines: string[] = []
  for (let i = 0; i < buffer.length; i++) {
    const line = buffer.getLine(i)
    if (line) {
      lines.push(line.translateToString(true))
    }
  }
  while (lines.length > 0 && lines[lines.length - 1] === '') {
    lines.pop()
  }
  return lines.join('\n')
}

export function PodTerminal({
  clusterId = null,
  clusterName = null,
  namespace,
  podName,
  containers = EMPTY_CONTAINERS,
  initialContainer = null,
  initialShell = 'auto',
  enabled = true,
  className = '',
  onContainerChange,
  onShellChange,
}: PodTerminalProps) {
  const defaultContainer = useMemo(() => {
    if (initialContainer && (containers.length === 0 || containers.includes(initialContainer))) {
      return initialContainer
    }
    return containers[0] ?? ''
  }, [containers, initialContainer])

  const [selectedContainer, setSelectedContainer] = useState<string>(defaultContainer)
  const [selectedShell, setSelectedShell] = useState<string>(initialShell || 'auto')
  const [copied, setCopied] = useState(false)

  // Keep selected container synchronized when pod or container list changes
  const containersKey = containers.join('\0')
  const syncKey = `${namespace}::${podName}::${containersKey}::${initialContainer ?? ''}`
  const [prevSyncKey, setPrevSyncKey] = useState(syncKey)
  if (syncKey !== prevSyncKey) {
    setPrevSyncKey(syncKey)
    if (containers.length > 0) {
      if (!selectedContainer || !containers.includes(selectedContainer)) {
        const next = initialContainer && containers.includes(initialContainer)
          ? initialContainer
          : containers[0]
        setSelectedContainer(next)
      }
    } else {
      setSelectedContainer(initialContainer ?? '')
    }
  }

  const viewportRef = useRef<HTMLDivElement | null>(null)
  const terminalRef = useRef<Terminal | null>(null)
  const fitAddonRef = useRef<FitAddon | null>(null)
  const pendingWritesRef = useRef<string[]>([])
  const sendInputRef = useRef<(data: string) => void>(() => {})
  const resizeRef = useRef<(cols: number, rows: number) => void>(() => {})

  const writeToTerminal = useCallback((data: string) => {
    if (!data) return
    if (terminalRef.current) {
      terminalRef.current.write(data)
    } else {
      pendingWritesRef.current.push(data)
    }
  }, [])

  const getDimensions = useCallback(() => {
    const term = terminalRef.current
    return {
      cols: term?.cols && term.cols > 0 ? term.cols : 80,
      rows: term?.rows && term.rows > 0 ? term.rows : 24,
    }
  }, [])

  // Mount raw PTY xterm.js instance BEFORE starting SignalR hook effect so initial dimensions are ready
  useEffect(() => {
    const containerEl = viewportRef.current
    if (!containerEl) return

    const term = new Terminal({
      cursorBlink: true,
      cursorStyle: 'bar',
      cursorInactiveStyle: 'outline',
      convertEol: false, // Raw PTY mode: preserve remote kernel line discipline & full-screen TUIs
      scrollback: 10000,
      smoothScrollDuration: 0,
      fontSize: 13,
      fontFamily:
        'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace',
      lineHeight: 1.25,
      theme: {
        background: '#09090b',
        foreground: '#f4f4f5',
        cursor: '#38bdf8',
        selectionBackground: 'rgba(56, 189, 248, 0.25)',
        black: '#18181b',
        red: '#f87171',
        green: '#4ade80',
        yellow: '#facc15',
        blue: '#60a5fa',
        magenta: '#c084fc',
        cyan: '#38bdf8',
        white: '#f4f4f5',
        brightBlack: '#71717a',
        brightRed: '#ef4444',
        brightGreen: '#22c55e',
        brightYellow: '#eab308',
        brightBlue: '#3b82f6',
        brightMagenta: '#a855f7',
        brightCyan: '#0ea5e9',
        brightWhite: '#ffffff',
      },
    })

    const fitAddon = new FitAddon()
    term.loadAddon(fitAddon)
    term.open(containerEl)

    // Allow clipboard copy when text is selected while preserving raw Ctrl+C (SIGINT) & Ctrl+A/E/D/R for PTY shells
    term.attachCustomKeyEventHandler((event) => {
      if (
        event.type === 'keydown' &&
        (event.ctrlKey || event.metaKey) &&
        (event.key === 'c' || event.key === 'C') &&
        term.hasSelection()
      ) {
        const selected = term.getSelection()
        if (selected) {
          void copyToClipboard(selected)
          term.clearSelection()
        }
        return false
      }
      return true
    })

    terminalRef.current = term
    fitAddonRef.current = fitAddon

    if (pendingWritesRef.current.length > 0) {
      for (const chunk of pendingWritesRef.current) {
        term.write(chunk)
      }
      pendingWritesRef.current = []
    }

    const fitAndNotifyResize = () => {
      try {
        fitAddon.fit()
        if (term.cols > 0 && term.rows > 0) {
          resizeRef.current(term.cols, term.rows)
        }
      } catch {
        // Container may temporarily have zero layout size while drawer animates
      }
    }

    fitAndNotifyResize()

    const rafId = requestAnimationFrame(() => {
      fitAndNotifyResize()
      term.focus()
    })

    const transitionFitTimer = setTimeout(() => {
      fitAndNotifyResize()
    }, 150)

    const onDataDisposable = term.onData((data) => {
      sendInputRef.current(data)
    })

    const onResizeDisposable = term.onResize(({ cols, rows }) => {
      resizeRef.current(cols, rows)
    })

    let resizeRafId: number | null = null
    const resizeObserver = new ResizeObserver(() => {
      if (resizeRafId !== null) {
        cancelAnimationFrame(resizeRafId)
      }
      resizeRafId = requestAnimationFrame(() => {
        resizeRafId = null
        fitAndNotifyResize()
      })
    })

    resizeObserver.observe(containerEl)

    return () => {
      cancelAnimationFrame(rafId)
      if (resizeRafId !== null) {
        cancelAnimationFrame(resizeRafId)
      }
      clearTimeout(transitionFitTimer)
      onDataDisposable.dispose()
      onResizeDisposable.dispose()
      resizeObserver.disconnect()
      term.dispose()
      terminalRef.current = null
      fitAddonRef.current = null
    }
  }, [])

  const handleAttached = useCallback(
    (info: PodTerminalAttachedInfo) => {
      terminalRef.current?.focus()
      // Ensure post-attach dimensions stay aligned with fitted canvas
      try {
        fitAddonRef.current?.fit()
        const term = terminalRef.current
        if (term && term.cols > 0 && term.rows > 0 && (term.cols !== info.cols || term.rows !== info.rows)) {
          resizeRef.current(term.cols, term.rows)
        }
      } catch {
        // Ignore transient layout errors
      }
    },
    []
  )

  const handleClosed = useCallback(
    (code: number | null) => {
      const codeLabel = code !== null ? ` (exit code ${code})` : ''
      writeToTerminal(`\r\n\x1b[33m[Session closed${codeLabel}]\x1b[0m\r\n`)
    },
    [writeToTerminal]
  )

  const {
    status,
    sessionInfo,
    error,
    exitCode,
    sendInput,
    resize,
    reconnect,
    disconnect,
  } = usePodTerminal({
    clusterId,
    namespace,
    podName,
    container: selectedContainer || null,
    shell: selectedShell,
    enabled,
    getDimensions,
    onOutput: writeToTerminal,
    onAttached: handleAttached,
    onClosed: handleClosed,
  })

  useEffect(() => {
    sendInputRef.current = sendInput
    resizeRef.current = resize
  }, [sendInput, resize])

  const handleContainerSelect = (nextContainer: string) => {
    if (nextContainer === selectedContainer) return
    writeToTerminal(
      `\r\n\x1b[36m─── Switching container to ${nextContainer} ───\x1b[0m\r\n`
    )
    setSelectedContainer(nextContainer)
    onContainerChange?.(nextContainer)
  }

  const handleShellSelect = (nextShell: string) => {
    if (nextShell === selectedShell) return
    const label = nextShell === 'auto' ? 'auto-detect' : nextShell
    writeToTerminal(`\r\n\x1b[36m─── Switching shell to ${label} ───\x1b[0m\r\n`)
    setSelectedShell(nextShell)
    onShellChange?.(nextShell)
  }

  const handleReconnectClick = () => {
    writeToTerminal(`\r\n\x1b[36m─── Reconnecting to ${namespace}/${podName} ───\x1b[0m\r\n`)
    reconnect()
    terminalRef.current?.focus()
  }

  const handleDisconnectClick = () => {
    disconnect()
    writeToTerminal('\r\n\x1b[90m[Disconnected by operator]\x1b[0m\r\n')
  }

  const handleClear = () => {
    terminalRef.current?.clear()
    terminalRef.current?.focus()
  }

  const handleCopy = async () => {
    const selection = terminalRef.current?.getSelection() ?? ''
    const textToCopy = selection.length > 0 ? selection : getAllBufferText(terminalRef.current)
    if (!textToCopy) return
    const ok = await copyToClipboard(textToCopy)
    if (ok) {
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    }
  }

  const renderStatusBadge = (currentStatus: PodTerminalStatus) => {
    switch (currentStatus) {
      case 'connecting':
        return (
          <span
            data-testid="pod-terminal-status"
            className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-amber-500/10 text-amber-400 border border-amber-500/20"
          >
            <Loader2 className="w-3 h-3 animate-spin" />
            Connecting
          </span>
        )
      case 'reconnecting':
        return (
          <span
            data-testid="pod-terminal-status"
            className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-amber-500/10 text-amber-400 border border-amber-500/20"
          >
            <Loader2 className="w-3 h-3 animate-spin" />
            Reconnecting
          </span>
        )
      case 'connected':
        return (
          <span
            data-testid="pod-terminal-status"
            className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-emerald-500/10 text-emerald-400 border border-emerald-500/20"
          >
            <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse" />
            Connected
            {sessionInfo?.resolvedShell && (
              <span className="text-[11px] font-mono text-emerald-300/80">
                ({sessionInfo.resolvedShell})
              </span>
            )}
          </span>
        )
      case 'error':
        return (
          <span
            data-testid="pod-terminal-status"
            className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-rose-500/10 text-rose-400 border border-rose-500/20"
          >
            <AlertCircle className="w-3 h-3 text-rose-400" />
            Error
          </span>
        )
      case 'disconnected':
        return (
          <span
            data-testid="pod-terminal-status"
            className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-zinc-800 text-zinc-300 border border-zinc-700/60"
          >
            Disconnected
            {exitCode !== null && (
              <span className="text-[11px] font-mono text-zinc-400">(exit {exitCode})</span>
            )}
          </span>
        )
      default:
        return (
          <span
            data-testid="pod-terminal-status"
            className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-zinc-800 text-zinc-400"
          >
            Idle
          </span>
        )
    }
  }

  const isSessionActive =
    status === 'connected' || status === 'connecting' || status === 'reconnecting'

  const resolvedContainer = sessionInfo?.container ?? null
  const effectiveContainerList = useMemo(() => {
    if (containers.length > 0) return containers
    if (resolvedContainer) return [resolvedContainer]
    if (selectedContainer) return [selectedContainer]
    return []
  }, [containers, resolvedContainer, selectedContainer])

  return (
    <div
      className={`flex flex-col h-full min-h-[320px] rounded-lg border border-zinc-800 bg-zinc-950 overflow-hidden ${className}`}
    >
      {/* Toolbar */}
      <div className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 bg-zinc-900/90 border-b border-zinc-800 shrink-0">
        <div className="flex items-center gap-2 min-w-0 flex-wrap">
          <TerminalIcon className="w-4 h-4 text-sky-400 shrink-0" />
          <span
            className="text-xs font-mono font-medium text-zinc-200 truncate max-w-[260px] sm:max-w-[340px]"
            title={`${namespace}/${podName}`}
          >
            {namespace}/{podName}
          </span>
          {(clusterName || (clusterId && clusterId !== 'default')) && (
            <span className="text-[11px] px-1.5 py-0.5 rounded bg-zinc-800 text-zinc-400 font-mono">
              {clusterName || clusterId}
            </span>
          )}
          {renderStatusBadge(status)}
        </div>

        <div className="flex items-center gap-1.5 flex-wrap">
          {/* Container Selector */}
          {effectiveContainerList.length > 0 && (
            <label className="inline-flex items-center gap-1.5 text-xs text-zinc-400">
              <Box className="w-3.5 h-3.5 text-zinc-500 shrink-0" />
              <span className="sr-only">Container</span>
              <select
                aria-label="Container"
                value={selectedContainer || effectiveContainerList[0]}
                onChange={(e) => handleContainerSelect(e.target.value)}
                disabled={effectiveContainerList.length <= 1}
                className="h-7 rounded-md border border-zinc-700/80 bg-zinc-950 px-2 text-xs font-mono text-zinc-200 focus:outline-none focus:ring-1 focus:ring-sky-500 disabled:opacity-75"
              >
                {effectiveContainerList.map((c) => (
                  <option key={c} value={c} className="bg-zinc-900 text-zinc-100">
                    {c}
                  </option>
                ))}
              </select>
            </label>
          )}

          {/* Shell Selector */}
          <label className="inline-flex items-center gap-1 text-xs text-zinc-400">
            <span className="sr-only">Shell</span>
            <select
              aria-label="Shell"
              value={selectedShell}
              onChange={(e) => handleShellSelect(e.target.value)}
              className="h-7 rounded-md border border-zinc-700/80 bg-zinc-950 px-2 text-xs font-mono text-zinc-200 focus:outline-none focus:ring-1 focus:ring-sky-500"
            >
              {POD_SHELL_OPTIONS.map((opt) => (
                <option key={opt.value} value={opt.value} className="bg-zinc-900 text-zinc-100">
                  {opt.label}
                </option>
              ))}
            </select>
          </label>

          {/* Reconnect / Disconnect */}
          {isSessionActive ? (
            <>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={handleReconnectClick}
                className="h-7 px-2 text-xs text-zinc-400 hover:text-zinc-100 gap-1"
                title="Restart terminal session"
              >
                <RefreshCw className="w-3.5 h-3.5" />
                <span className="hidden xl:inline">Reconnect</span>
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={handleDisconnectClick}
                className="h-7 px-2 text-xs text-amber-400 hover:text-amber-300 hover:bg-amber-950/30 gap-1"
                title="Disconnect session"
              >
                <Unplug className="w-3.5 h-3.5" />
                <span className="hidden md:inline">Disconnect</span>
              </Button>
            </>
          ) : (
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={handleReconnectClick}
              className="h-7 px-2.5 text-xs border-sky-500/40 bg-sky-500/10 text-sky-300 hover:bg-sky-500/20 gap-1"
              title="Reconnect terminal session"
            >
              <PlugZap className="w-3.5 h-3.5" />
              <span>Reconnect</span>
            </Button>
          )}

          {/* Copy */}
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={handleCopy}
            className={`h-7 px-2 text-xs gap-1 transition-colors ${
              copied
                ? 'text-emerald-400 bg-emerald-950/40 border border-emerald-500/30'
                : 'text-zinc-400 hover:text-zinc-200'
            }`}
            title={copied ? 'Copied to clipboard!' : 'Copy selection or terminal output'}
          >
            {copied ? (
              <>
                <Check className="w-3.5 h-3.5 text-emerald-400" />
                <span className="text-emerald-400 font-medium">Copied!</span>
              </>
            ) : (
              <>
                <Copy className="w-3.5 h-3.5" />
                <span className="hidden xl:inline">Copy</span>
              </>
            )}
          </Button>

          {/* Clear */}
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={handleClear}
            className="h-7 px-2 text-xs text-zinc-400 hover:text-rose-400 gap-1"
            title="Clear terminal screen"
            aria-label="Clear terminal"
          >
            <Trash2 className="w-3.5 h-3.5" />
          </Button>
        </div>
      </div>

      {/* Inline error banner when attach or transport fails */}
      {status === 'error' && error && (
        <div
          role="alert"
          className="flex items-center justify-between gap-2 px-3 py-2 bg-rose-950/40 border-b border-rose-500/30 text-xs text-rose-300 shrink-0"
        >
          <div className="flex items-center gap-2 min-w-0">
            <AlertCircle className="w-4 h-4 text-rose-400 shrink-0" />
            <span className="truncate">{error}</span>
          </div>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={handleReconnectClick}
            className="h-6 px-2 text-xs text-rose-200 hover:text-white hover:bg-rose-900/40 shrink-0"
          >
            Retry
          </Button>
        </div>
      )}

      {/* Raw PTY xterm viewport */}
      <div
        ref={viewportRef}
        data-testid="pod-terminal-viewport"
        onClick={() => terminalRef.current?.focus()}
        className="flex-1 w-full min-h-0 overflow-hidden bg-zinc-950 p-1.5"
      />
    </div>
  )
}
