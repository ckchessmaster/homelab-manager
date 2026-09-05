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
  scrollToBottom: () => void
}

interface TerminalCanvasProps {
  autoScroll?: boolean
  className?: string
  onData?: (data: string) => void
  onScrollPositionChange?: (isAtBottom: boolean) => void
}

export const TerminalCanvas = forwardRef<TerminalRef, TerminalCanvasProps>(
  ({ autoScroll = true, className = '', onData, onScrollPositionChange }, ref) => {
    const containerRef = useRef<HTMLDivElement>(null)
    const terminalRef = useRef<Terminal | null>(null)
    const fitAddonRef = useRef<FitAddon | null>(null)
    const autoScrollRef = useRef(autoScroll)

    useEffect(() => {
      autoScrollRef.current = autoScroll
      if (autoScroll && terminalRef.current) {
        terminalRef.current.scrollToBottom()
      }
    }, [autoScroll])

    useImperativeHandle(ref, () => ({
      write: (data: string) => {
        if (terminalRef.current) {
          terminalRef.current.write(data, () => {
            if (autoScrollRef.current) {
              requestAnimationFrame(() => {
                terminalRef.current?.scrollToBottom()
              })
            }
          })
        }
      },
      writeln: (data: string) => {
        if (terminalRef.current) {
          terminalRef.current.writeln(data, () => {
            if (autoScrollRef.current) {
              requestAnimationFrame(() => {
                terminalRef.current?.scrollToBottom()
              })
            }
          })
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
      scrollToBottom: () => {
        terminalRef.current?.scrollToBottom()
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
        scrollback: 10000,
        smoothScrollDuration: 0,
      })

      const fitAddon = new FitAddon()
      term.loadAddon(fitAddon)
      term.open(containerRef.current)

      // Fit after DOM render
      requestAnimationFrame(() => {
        try {
          fitAddon.fit()
          if (autoScrollRef.current) {
            term.scrollToBottom()
          }
        } catch {}
      })

      terminalRef.current = term
      fitAddonRef.current = fitAddon

      if (onData) {
        term.onData(onData)
      }

      // Detect user scrolling to pause or resume auto-scroll
      const scrollDisposable = term.onScroll(() => {
        const buffer = term.buffer.active
        const isAtBottom = buffer.viewportY >= buffer.baseY - 1
        onScrollPositionChange?.(isAtBottom)
      })

      const resizeObserver = new ResizeObserver(() => {
        requestAnimationFrame(() => {
          try {
            fitAddon.fit()
            if (autoScrollRef.current) {
              term.scrollToBottom()
            }
          } catch {
            // container might be hidden
          }
        })
      })

      resizeObserver.observe(containerRef.current)

      return () => {
        scrollDisposable.dispose()
        resizeObserver.disconnect()
        term.dispose()
        terminalRef.current = null
        fitAddonRef.current = null
      }
    }, [onData, onScrollPositionChange])

    return (
      <div
        ref={containerRef}
        className={`w-full h-full overflow-hidden bg-zinc-950 ${className}`}
      />
    )
  }
)

TerminalCanvas.displayName = 'TerminalCanvas'
