import { useEffect, useRef, useImperativeHandle, forwardRef } from 'react'
import { Terminal } from '@xterm/xterm'
import { FitAddon } from '@xterm/addon-fit'
import '@xterm/xterm/css/xterm.css'

export interface TerminalRef {
  write: (data: string) => void
  writeln: (data: string) => void
  clear: () => void
  focus: () => void
  getSelection: () => string
}

interface TerminalCanvasProps {
  autoScroll?: boolean
  className?: string
  onData?: (data: string) => void
}

export const TerminalCanvas = forwardRef<TerminalRef, TerminalCanvasProps>(
  ({ autoScroll = true, className = '', onData }, ref) => {
    const containerRef = useRef<HTMLDivElement>(null)
    const terminalRef = useRef<Terminal | null>(null)
    const fitAddonRef = useRef<FitAddon | null>(null)

    useImperativeHandle(ref, () => ({
      write: (data: string) => {
        if (terminalRef.current) {
          terminalRef.current.write(data)
          if (autoScroll) {
            terminalRef.current.scrollToBottom()
          }
        }
      },
      writeln: (data: string) => {
        if (terminalRef.current) {
          terminalRef.current.writeln(data)
          if (autoScroll) {
            terminalRef.current.scrollToBottom()
          }
        }
      },
      clear: () => {
        terminalRef.current?.clear()
      },
      focus: () => {
        terminalRef.current?.focus()
      },
      getSelection: () => {
        return terminalRef.current?.getSelection() ?? ''
      },
    }))

    useEffect(() => {
      if (!containerRef.current) return

      const term = new Terminal({
        cursorBlink: true,
        cursorStyle: 'bar',
        fontSize: 13,
        fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace',
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
        convertEol: true,
        scrollback: 5000,
      })

      const fitAddon = new FitAddon()
      term.loadAddon(fitAddon)
      term.open(containerRef.current)
      fitAddon.fit()

      terminalRef.current = term
      fitAddonRef.current = fitAddon

      if (onData) {
        term.onData(onData)
      }

      const resizeObserver = new ResizeObserver(() => {
        try {
          fitAddon.fit()
        } catch {
          // container might be hidden
        }
      })

      resizeObserver.observe(containerRef.current)

      return () => {
        resizeObserver.disconnect()
        term.dispose()
        terminalRef.current = null
        fitAddonRef.current = null
      }
    }, [onData])

    return (
      <div
        ref={containerRef}
        className={`w-full h-full min-h-[300px] overflow-hidden rounded-lg bg-zinc-950 p-2 border border-zinc-800 ${className}`}
      />
    )
  }
)

TerminalCanvas.displayName = 'TerminalCanvas'
