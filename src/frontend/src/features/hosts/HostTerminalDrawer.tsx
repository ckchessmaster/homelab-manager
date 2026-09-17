import { useState, useRef, useCallback, useEffect } from 'react'
import { X, Play, Terminal as TerminalIcon, Sparkles, CheckCircle2, AlertCircle, XCircle, Maximize2, Minimize2, GitFork } from 'lucide-react'
import type { Host } from '../../api/hosts'
import { apiClient } from '../../api/client'
import { Button } from '../../components/ui/button'
import { TerminalCanvas, copyToClipboard } from '../../components/terminal/TerminalCanvas'
import type { TerminalRef } from '../../components/terminal/TerminalCanvas'
import { TerminalToolbar } from '../../components/terminal/TerminalToolbar'
import { useJobTerminalStream } from '../../components/terminal/useJobTerminalStream'

interface HostTerminalDrawerProps {
  host: Host
  isOpen: boolean
  onClose: () => void
  initialJobId?: string | null
  autoTriggerDag?: boolean
  isWorkflow?: boolean
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
  initialJobId = null,
  autoTriggerDag = false,
  isWorkflow = false,
}) => {
  const terminalRef = useRef<TerminalRef>(null)
  const [activeJobId, setActiveJobId] = useState<string | null>(initialJobId)
  const [activeCommand, setActiveCommand] = useState<string>(
    initialJobId ? (isWorkflow ? 'DAG Execution Log' : 'Command Execution Log') : ''
  )
  const [customCommand, setCustomCommand] = useState('')
  const [customArgs, setCustomArgs] = useState('')
  const [autoScroll, setAutoScroll] = useState(true)
  const [isMaximized, setIsMaximized] = useState(false)
  const [isExecuting, setIsExecuting] = useState(false)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  const autoTriggeredRef = useRef(false)
  const [isDagExecution, setIsDagExecution] = useState(Boolean(isWorkflow || autoTriggerDag))

  const isWorkflowMode = Boolean(isWorkflow || isDagExecution)

  const handleScrollPositionChange = useCallback((isAtBottom: boolean) => {
    setAutoScroll((prev) => {
      if (!isAtBottom && prev) return false
      if (isAtBottom && !prev) return true
      return prev
    })
  }, [])

  const handleLogLine = useCallback((line: string, streamType?: string) => {
    if (!line) {
      terminalRef.current?.writeln('')
      return
    }
    if (streamType === 'stderr' && !line.includes('\x1b[')) {
      terminalRef.current?.writeln(`\x1b[31m${line}\x1b[0m`)
    } else if (streamType === 'system' && !line.includes('\x1b[')) {
      terminalRef.current?.writeln(`\x1b[36m${line}\x1b[0m`)
    } else {
      terminalRef.current?.writeln(line)
    }
  }, [])

  const { status, jobState, activeStep, failureReason, lineCount } = useJobTerminalStream({
    jobId: activeJobId,
    onLogLine: handleLogLine,
  })

  const runCommand = async (command: string, args: string[] = []) => {
    setErrorMsg(null)
    setIsExecuting(true)
    setIsDagExecution(false)
    setActiveCommand(`${command} ${args.join(' ')}`.trim())
    terminalRef.current?.clear()
    terminalRef.current?.writeln(`\x1b[36m$ ${command} ${args.join(' ')}\x1b[0m`.trim())

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

  const runDagUpdate = useCallback(async () => {
    setErrorMsg(null)
    setIsExecuting(true)
    setIsDagExecution(true)
    setActiveCommand('DAG Upgrade Pipeline')
    terminalRef.current?.clear()
    terminalRef.current?.writeln(`\x1b[35m=== Triggering DAG Update Pipeline for ${host.hostname} ===\x1b[0m`)

    try {
      const res = await apiClient<{ id: string; targetHostId: string; status: string }>('/api/v1/jobs', {
        method: 'POST',
        body: JSON.stringify({ targetHostId: host.id }),
      })
      setActiveJobId(res.id)
      terminalRef.current?.writeln(`\x1b[32mJob created (${res.id}). Awaiting DAG execution...\x1b[0m`)
    } catch (err: unknown) {
      const msg = err && typeof err === 'object' && 'response' in err
        ? (err as { response?: { data?: { message?: string } } }).response?.data?.message ?? 'DAG update failed'
        : 'Failed to launch update job'
      setErrorMsg(msg)
      terminalRef.current?.writeln(`\x1b[31mError: ${msg}\x1b[0m`)
    } finally {
      setIsExecuting(false)
    }
  }, [host.id, host.hostname])

  useEffect(() => {
    if (isOpen && autoTriggerDag && !autoTriggeredRef.current && !initialJobId && host.agent?.installed) {
      autoTriggeredRef.current = true
      runDagUpdate()
    }
  }, [isOpen, autoTriggerDag, initialJobId, host.agent?.installed, runDagUpdate])

  const handleClear = () => {
    terminalRef.current?.clear()
  }

  const handleCopy = async () => {
    const selection = terminalRef.current?.getSelection()
    const textToCopy = selection && selection.length > 0
      ? selection
      : terminalRef.current?.getAllText()

    if (textToCopy) {
      const ok = await copyToClipboard(textToCopy)
      if (ok) {
        setCopied(true)
        setTimeout(() => setCopied(false), 2000)
      }
    }
  }

  if (!isOpen) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm animate-in fade-in">
      <div
        className={`bg-zinc-950 border border-zinc-800 rounded-xl flex flex-col shadow-2xl overflow-hidden transition-all duration-200 ${
          isMaximized
            ? 'w-[96vw] h-[94vh] max-w-none max-h-none'
            : 'w-full max-w-5xl 2xl:max-w-6xl h-[85vh] max-h-[920px] min-h-[580px]'
        }`}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-zinc-800 bg-zinc-900/60 shrink-0">
          <div className="flex items-center gap-3 min-w-0">
            <div className={`p-2 rounded-lg shrink-0 border ${
              isWorkflowMode
                ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20'
                : 'bg-sky-500/10 text-sky-400 border-sky-500/20'
            }`}>
              {isWorkflowMode ? <GitFork className="w-5 h-5" /> : <TerminalIcon className="w-5 h-5" />}
            </div>
            <div className="min-w-0">
              <div className="flex items-center gap-2 flex-wrap">
                <h2 className="text-base font-semibold text-zinc-100 truncate">{host.hostname}</h2>
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
              <p className="text-xs text-zinc-400 mt-0.5">
                {isWorkflowMode ? 'Live Pipeline Stream & Execution Audit Trail' : 'Live Agent Terminal & Remote Diagnostics Console'}
              </p>
            </div>
          </div>
          <div className="flex items-center gap-1 shrink-0">
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setIsMaximized(!isMaximized)}
              className="text-zinc-400 hover:text-zinc-100 h-8 w-8 p-0"
              title={isMaximized ? 'Restore window size' : 'Maximize console view'}
            >
              {isMaximized ? <Minimize2 className="w-4 h-4" /> : <Maximize2 className="w-4 h-4" />}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={onClose}
              className="text-zinc-400 hover:text-zinc-100 h-8 w-8 p-0"
              title="Close console"
            >
              <X className="w-5 h-5" />
            </Button>
          </div>
        </div>

        {/* Action Presets & Command Bar (interactive mode only) */}
        {!isWorkflowMode && (
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
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={isExecuting || !host.agent?.installed}
                onClick={runDagUpdate}
                className="text-xs h-7 border-emerald-700/60 bg-emerald-950/40 hover:bg-emerald-900/60 text-emerald-400 gap-1 font-medium shadow-sm"
              >
                <Sparkles className="w-3 h-3" />
                Run Update (DAG)
              </Button>
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
        )}

        {/* Active DAG Job & Step Status Banner */}
        {isWorkflowMode && activeJobId && jobState && (
          <div className="px-5 py-2.5 bg-zinc-900/90 border-b border-zinc-800 flex flex-wrap items-center justify-between gap-2">
            <div className="flex flex-wrap items-center gap-2.5">
              <span className="text-xs font-medium text-zinc-400">DAG Status:</span>
              {jobState === 'Pending' && (
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-amber-500/10 text-amber-300 border border-amber-500/20">
                  <span className="w-1.5 h-1.5 rounded-full bg-amber-400" />
                  Pending Pre-Flight
                </span>
              )}
              {jobState === 'Running' && (
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-sky-500/10 text-sky-300 border border-sky-500/20">
                  <span className="w-1.5 h-1.5 rounded-full bg-sky-400 animate-pulse" />
                  Running Pipeline
                </span>
              )}
              {jobState === 'Verifying' && (
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-purple-500/10 text-purple-300 border border-purple-500/20">
                  <span className="w-1.5 h-1.5 rounded-full bg-purple-400 animate-pulse" />
                  Verifying
                </span>
              )}
              {jobState === 'Completed' && (
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-500/10 text-emerald-300 border border-emerald-500/20">
                  <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" />
                  Completed
                </span>
              )}
              {jobState === 'Failed' && (
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-rose-500/10 text-rose-300 border border-rose-500/20">
                  <AlertCircle className="w-3.5 h-3.5 text-rose-400" />
                  Failed
                </span>
              )}
              {jobState === 'RolledBack' && (
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-orange-500/10 text-orange-300 border border-orange-500/20">
                  <AlertCircle className="w-3.5 h-3.5 text-orange-400" />
                  Rolled Back
                </span>
              )}
              {jobState === 'Cancelled' && (
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-zinc-800 text-zinc-300 border border-zinc-700">
                  <XCircle className="w-3.5 h-3.5 text-zinc-400" />
                  Cancelled
                </span>
              )}

              {activeStep && (
                <div className="flex items-center gap-1.5 text-xs text-zinc-300 font-mono">
                  <span className="text-zinc-600">|</span>
                  <span className="text-zinc-400">Step:</span>
                  <span className="text-sky-400 font-semibold">{activeStep}</span>
                </div>
              )}
            </div>

            {failureReason && (jobState === 'Failed' || jobState === 'RolledBack' || jobState === 'Cancelled') && (
              <span className="text-xs text-amber-400 font-mono truncate max-w-sm" title={failureReason}>
                {failureReason}
              </span>
            )}
          </div>
        )}

        {/* Ad-Hoc Command Execution Status Banner */}
        {!isWorkflowMode && activeJobId && (
          <div className="px-5 py-2 bg-zinc-900/60 border-b border-zinc-800/80 flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-2 min-w-0">
              <span className="text-xs font-medium text-zinc-400">Command:</span>
              <span className="text-xs font-mono text-zinc-200 truncate max-w-md bg-zinc-950 px-2 py-0.5 rounded border border-zinc-800">
                {activeCommand || 'Custom Command'}
              </span>
              <span className="text-xs text-zinc-600">|</span>
              <span className="text-xs font-medium text-zinc-400">Status:</span>
              {jobState === 'Running' || jobState === 'Pending' ? (
                <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-sky-500/10 text-sky-300 border border-sky-500/20">
                  <span className="w-1.5 h-1.5 rounded-full bg-sky-400 animate-pulse" />
                  Running
                </span>
              ) : jobState === 'Completed' ? (
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-500/10 text-emerald-300 border border-emerald-500/20">
                  <CheckCircle2 className="w-3 h-3 text-emerald-400" />
                  Completed
                </span>
              ) : jobState === 'Failed' ? (
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium bg-rose-500/10 text-rose-300 border border-rose-500/20">
                  <AlertCircle className="w-3 h-3 text-rose-400" />
                  Failed
                </span>
              ) : (
                <span className="text-xs text-zinc-400 font-mono">{jobState || 'Idle'}</span>
              )}
            </div>
            {failureReason && (
              <span className="text-xs text-rose-400 font-mono truncate max-w-sm" title={failureReason}>
                {failureReason}
              </span>
            )}
          </div>
        )}

        {/* Terminal Canvas Section */}
        <div className="flex-1 min-h-0 p-4 flex flex-col bg-zinc-950">
          <TerminalToolbar
            status={status}
            title={isWorkflowMode ? 'DAG Execution Log' : (activeCommand ? `$ ${activeCommand}` : 'Terminal Ready')}
            lineCount={lineCount}
            autoScroll={autoScroll}
            onToggleAutoScroll={() => {
              const next = !autoScroll
              setAutoScroll(next)
              if (next) {
                terminalRef.current?.scrollToBottom()
              }
            }}
            onClear={!isWorkflowMode ? handleClear : undefined}
            onCopy={handleCopy}
            showClear={!isWorkflowMode}
            copied={copied}
          />
          <div className="flex-1 min-h-0 relative rounded-b-lg border-x border-b border-zinc-800 bg-zinc-950 overflow-hidden">
            <div className="absolute inset-0">
              <TerminalCanvas
                ref={terminalRef}
                autoScroll={autoScroll}
                readOnly={isWorkflowMode}
                onScrollPositionChange={handleScrollPositionChange}
              />
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
