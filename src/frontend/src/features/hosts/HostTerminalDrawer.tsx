import { useState, useRef, useCallback } from 'react'
import { X, Play, Terminal as TerminalIcon, Sparkles } from 'lucide-react'
import type { Host } from '../../api/hosts'
import { apiClient } from '../../api/client'
import { Button } from '../../components/ui/button'
import { TerminalCanvas } from '../../components/terminal/TerminalCanvas'
import type { TerminalRef } from '../../components/terminal/TerminalCanvas'
import { TerminalToolbar } from '../../components/terminal/TerminalToolbar'
import { useJobTerminalStream } from '../../components/terminal/useJobTerminalStream'

interface HostTerminalDrawerProps {
  host: Host
  isOpen: boolean
  onClose: () => void
}

interface ExecuteCommandResponse {
  jobId: string
  hostId: string
  command: string
  args: string[]
  status: string
}

export const HostTerminalDrawer: React.FC<HostTerminalDrawerProps> = ({
  host,
  isOpen,
  onClose,
}) => {
  const [activeJobId, setActiveJobId] = useState<string | null>(null)
  const [activeCommand, setActiveCommand] = useState<string>('')
  const [customCommand, setCustomCommand] = useState('')
  const [customArgs, setCustomArgs] = useState('')
  const [autoScroll, setAutoScroll] = useState(true)
  const [isExecuting, setIsExecuting] = useState(false)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

  const terminalRef = useRef<TerminalRef>(null)

  const handleLogLine = useCallback((line: string) => {
    terminalRef.current?.writeln(line)
  }, [])

  const { status, lineCount } = useJobTerminalStream({
    jobId: activeJobId,
    onLogLine: handleLogLine,
  })

  const runCommand = async (command: string, args: string[] = []) => {
    setErrorMsg(null)
    setIsExecuting(true)
    setActiveCommand(`${command} ${args.join(' ')}`)

    terminalRef.current?.writeln(`\x1b[36m$ ${command} ${args.join(' ')}\x1b[0m`)

    try {
      const res = await apiClient<ExecuteCommandResponse>('/api/v1/debug/execute-command', {
        method: 'POST',
        body: JSON.stringify({
          hostId: host.id,
          command,
          args,
        }),
      })
      setActiveJobId(res.jobId)
    } catch (err: unknown) {
      const msg = err && typeof err === 'object' && 'response' in err
        ? (err as { response?: { data?: { message?: string } } }).response?.data?.message ?? 'Command failed'
        : 'Failed to execute command'
      setErrorMsg(msg)
      terminalRef.current?.writeln(`\x1b[31mError: ${msg}\x1b[0m`)
    } finally {
      setIsExecuting(false)
    }
  }

  const handleClear = () => {
    terminalRef.current?.clear()
  }

  const handleCopy = () => {
    const text = terminalRef.current?.getSelection()
    if (text) {
      navigator.clipboard.writeText(text)
    }
  }

  if (!isOpen) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm animate-in fade-in">
      <div className="bg-zinc-950 border border-zinc-800 rounded-xl w-full max-w-4xl max-h-[90vh] flex flex-col shadow-2xl overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-zinc-800 bg-zinc-900/60">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-sky-500/10 text-sky-400 border border-sky-500/20">
              <TerminalIcon className="w-5 h-5" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-base font-semibold text-zinc-100">{host.hostname}</h2>
                <span className="text-xs px-2 py-0.5 rounded-full bg-zinc-800 text-zinc-400 font-mono">
                  {host.ipAddress}
                </span>
                {host.agent?.installed ? (
                  <span className="text-xs px-2 py-0.5 rounded-full bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                    Agent v{host.agent.version ?? '1.0'}
                  </span>
                ) : (
                  <span className="text-xs px-2 py-0.5 rounded-full bg-amber-500/10 text-amber-400 border border-amber-500/20">
                    Agent Not Connected
                  </span>
                )}
              </div>
              <p className="text-xs text-zinc-400 mt-0.5">Live Agent Terminal & Remote Diagnostics Console</p>
            </div>
          </div>
          <Button variant="ghost" size="sm" onClick={onClose} className="text-zinc-400 hover:text-zinc-100">
            <X className="w-5 h-5" />
          </Button>
        </div>

        {/* Action Presets & Command Bar */}
        <div className="p-4 border-b border-zinc-800 bg-zinc-900/30 flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs font-medium text-zinc-400 flex items-center gap-1 mr-1">
              <Sparkles className="w-3.5 h-3.5 text-sky-400" /> Presets:
            </span>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={isExecuting || !host.agent?.installed}
              onClick={() => runCommand('uname', ['-a'])}
              className="text-xs h-7 border-zinc-700 bg-zinc-800/60 hover:bg-zinc-800 text-zinc-200"
            >
              uname -a
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={isExecuting || !host.agent?.installed}
              onClick={() => runCommand('uptime')}
              className="text-xs h-7 border-zinc-700 bg-zinc-800/60 hover:bg-zinc-800 text-zinc-200"
            >
              uptime
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={isExecuting || !host.agent?.installed}
              onClick={() => runCommand('df', ['-h', '/'])}
              className="text-xs h-7 border-zinc-700 bg-zinc-800/60 hover:bg-zinc-800 text-zinc-200"
            >
              df -h /
            </Button>
            {host.osFamily.includes('debian') ? (
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={isExecuting || !host.agent?.installed}
                onClick={() => runCommand('apt-get', ['-s', 'upgrade'])}
                className="text-xs h-7 border-zinc-700 bg-zinc-800/60 hover:bg-zinc-800 text-amber-400"
              >
                apt-get -s upgrade
              </Button>
            ) : (
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={isExecuting || !host.agent?.installed}
                onClick={() => runCommand('dnf', ['check-update', '-q'])}
                className="text-xs h-7 border-zinc-700 bg-zinc-800/60 hover:bg-zinc-800 text-amber-400"
              >
                dnf check-update
              </Button>
            )}
          </div>

          {/* Custom Command Input */}
          <div className="flex items-center gap-2">
            <input
              type="text"
              placeholder="Command (e.g. systemctl)"
              value={customCommand}
              onChange={(e) => setCustomCommand(e.target.value)}
              className="w-44 px-2.5 py-1.5 text-xs bg-zinc-950 border border-zinc-800 rounded-md text-zinc-200 placeholder-zinc-500 font-mono focus:outline-none focus:ring-1 focus:ring-sky-500"
            />
            <input
              type="text"
              placeholder="Arguments (e.g. status nginx)"
              value={customArgs}
              onChange={(e) => setCustomArgs(e.target.value)}
              className="flex-1 px-2.5 py-1.5 text-xs bg-zinc-950 border border-zinc-800 rounded-md text-zinc-200 placeholder-zinc-500 font-mono focus:outline-none focus:ring-1 focus:ring-sky-500"
            />
            <Button
              type="button"
              size="sm"
              disabled={isExecuting || !customCommand.trim() || !host.agent?.installed}
              onClick={() => runCommand(customCommand.trim(), customArgs.trim().split(/\s+/).filter(Boolean))}
              className="h-8 px-3 text-xs bg-sky-600 hover:bg-sky-500 text-white gap-1.5"
            >
              <Play className="w-3.5 h-3.5 fill-current" /> Run
            </Button>
          </div>
          {errorMsg && (
            <p className="text-xs text-rose-400 font-mono">{errorMsg}</p>
          )}
        </div>

        {/* Terminal Canvas */}
        <div className="flex-1 min-h-[420px] p-4 flex flex-col bg-zinc-950">
          <TerminalToolbar
            status={status}
            title={activeCommand ? `$ ${activeCommand}` : 'Terminal Ready'}
            lineCount={lineCount}
            autoScroll={autoScroll}
            onToggleAutoScroll={() => setAutoScroll(!autoScroll)}
            onClear={handleClear}
            onCopy={handleCopy}
          />
          <div className="flex-1 min-h-[360px] relative">
            <TerminalCanvas ref={terminalRef} autoScroll={autoScroll} />
          </div>
        </div>
      </div>
    </div>
  )
}
