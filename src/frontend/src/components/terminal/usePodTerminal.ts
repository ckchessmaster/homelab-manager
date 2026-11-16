import { useCallback, useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { getSignalRAccessToken } from '../../api/client'

export type PodTerminalStatus =
  | 'idle'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'disconnected'
  | 'error'

export interface PodShellOptionItem {
  value: string
  label: string
}

export const POD_SHELL_OPTIONS: readonly PodShellOptionItem[] = [
  { value: 'auto', label: 'Auto (probe)' },
  { value: '/bin/bash', label: '/bin/bash' },
  { value: '/bin/sh', label: '/bin/sh' },
  { value: '/bin/ash', label: '/bin/ash' },
] as const

export interface PodTerminalAttachRequestPayload {
  namespace: string
  podName: string
  clusterId: string | null
  container: string | null
  shell: string | null
  cols: number
  rows: number
}

export interface PodTerminalAttachedInfo {
  sessionId: string
  clusterId: string
  namespace: string
  podName: string
  container?: string | null
  resolvedShell: string
  cols: number
  rows: number
  connectedAtUtc?: string
}

export interface PodTerminalAttachResultPayload {
  succeeded: boolean
  sessionId?: string | null
  clusterId?: string | null
  namespace?: string | null
  podName?: string | null
  container?: string | null
  resolvedShell?: string | null
  error?: string | null
}

export interface UsePodTerminalOptions {
  clusterId?: string | null
  namespace: string
  podName: string
  container?: string | null
  shell?: string | null
  enabled?: boolean
  accessToken?: string | null
  initialCols?: number
  initialRows?: number
  getDimensions?: () => { cols: number; rows: number }
  onOutput?: (data: string) => void
  onAttached?: (info: PodTerminalAttachedInfo) => void
  onClosed?: (exitCode: number | null) => void
  onError?: (message: string) => void
}

export interface UsePodTerminalResult {
  status: PodTerminalStatus
  sessionInfo: PodTerminalAttachedInfo | null
  error: string | null
  exitCode: number | null
  sendInput: (data: string) => void
  resize: (cols: number, rows: number) => void
  reconnect: () => void
  disconnect: () => void
}

const MIN_COLS = 10
const MAX_COLS = 500
const MIN_ROWS = 2
const MAX_ROWS = 200

function clampCols(cols: number, fallback = 80): number {
  if (!Number.isFinite(cols) || cols <= 0) return fallback
  return Math.max(MIN_COLS, Math.min(MAX_COLS, Math.floor(cols)))
}

function clampRows(rows: number, fallback = 24): number {
  if (!Number.isFinite(rows) || rows <= 0) return fallback
  return Math.max(MIN_ROWS, Math.min(MAX_ROWS, Math.floor(rows)))
}

export function normalizeRequestedShell(shell?: string | null): string | null {
  if (!shell) return null
  const trimmed = shell.trim()
  if (!trimmed || trimmed.toLowerCase() === 'auto') return null
  return trimmed
}

export function resolvePodTerminalHubUrl(): string {
  const configuredBase = import.meta.env.VITE_API_URL
  if (configuredBase && typeof configuredBase === 'string' && configuredBase.trim().length > 0) {
    return `${configuredBase.trim().replace(/\/$/, '')}/hubs/pod-terminal`
  }
  const origin = typeof window !== 'undefined' ? window.location.origin : 'http://localhost:5173'
  return `${origin}/hubs/pod-terminal`
}

export function usePodTerminal({
  clusterId = null,
  namespace,
  podName,
  container = null,
  shell = 'auto',
  enabled = true,
  accessToken = null,
  initialCols = 80,
  initialRows = 24,
  getDimensions,
  onOutput,
  onAttached,
  onClosed,
  onError,
}: UsePodTerminalOptions): UsePodTerminalResult {
  const [status, setStatus] = useState<PodTerminalStatus>('idle')
  const [sessionInfo, setSessionInfo] = useState<PodTerminalAttachedInfo | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [exitCode, setExitCode] = useState<number | null>(null)
  const [reconnectEpoch, setReconnectEpoch] = useState(0)
  const [manuallyDisconnected, setManuallyDisconnected] = useState(false)

  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const connectGenRef = useRef(0)
  const isAttachedRef = useRef(false)
  const sizeRef = useRef<{ cols: number; rows: number }>({
    cols: clampCols(initialCols),
    rows: clampRows(initialRows),
  })
  const lastSentSizeRef = useRef<{ cols: number; rows: number } | null>(null)
  const accessTokenRef = useRef(accessToken)
  const getDimensionsRef = useRef(getDimensions)
  const onOutputRef = useRef(onOutput)
  const onAttachedRef = useRef(onAttached)
  const onClosedRef = useRef(onClosed)
  const onErrorRef = useRef(onError)

  useEffect(() => {
    accessTokenRef.current = accessToken
    getDimensionsRef.current = getDimensions
    onOutputRef.current = onOutput
    onAttachedRef.current = onAttached
    onClosedRef.current = onClosed
    onErrorRef.current = onError
  })

  // Reset manual disconnect if target pod/container/shell changes
  const targetKey = `${clusterId ?? ''}::${namespace}::${podName}::${container ?? ''}::${shell ?? ''}`
  const [prevTargetKey, setPrevTargetKey] = useState(targetKey)
  if (targetKey !== prevTargetKey) {
    setPrevTargetKey(targetKey)
    if (manuallyDisconnected) {
      setManuallyDisconnected(false)
    }
  }

  const teardownConnection = useCallback((conn: signalR.HubConnection | null, sendDetach: boolean) => {
    if (!conn) return
    if (sendDetach && conn.state === signalR.HubConnectionState.Connected) {
      void conn.invoke('Detach').catch(() => {})
    }
    void conn.stop().catch(() => {})
  }, [])

  const disconnect = useCallback(() => {
    connectGenRef.current += 1
    isAttachedRef.current = false
    lastSentSizeRef.current = null
    setManuallyDisconnected(true)

    const active = connectionRef.current
    connectionRef.current = null
    teardownConnection(active, true)

    setSessionInfo(null)
    setStatus('disconnected')
  }, [teardownConnection])

  const reconnect = useCallback(() => {
    setManuallyDisconnected(false)
    setError(null)
    setExitCode(null)
    setReconnectEpoch((prev) => prev + 1)
  }, [])

  const sendInput = useCallback((data: string) => {
    if (!data) return
    const conn = connectionRef.current
    if (!conn || conn.state !== signalR.HubConnectionState.Connected || !isAttachedRef.current) {
      return
    }
    void conn.invoke('Input', data).catch(() => {})
  }, [])

  const resize = useCallback((cols: number, rows: number) => {
    if (!Number.isFinite(cols) || !Number.isFinite(rows) || cols <= 0 || rows <= 0) {
      return
    }
    const clampedCols = clampCols(cols)
    const clampedRows = clampRows(rows)
    sizeRef.current = { cols: clampedCols, rows: clampedRows }

    const conn = connectionRef.current
    if (!conn || conn.state !== signalR.HubConnectionState.Connected || !isAttachedRef.current) {
      return
    }

    if (
      lastSentSizeRef.current &&
      lastSentSizeRef.current.cols === clampedCols &&
      lastSentSizeRef.current.rows === clampedRows
    ) {
      return
    }

    lastSentSizeRef.current = { cols: clampedCols, rows: clampedRows }
    void conn.invoke('Resize', clampedCols, clampedRows).catch(() => {})
  }, [])

  useEffect(() => {
    const trimmedNamespace = namespace.trim()
    const trimmedPodName = podName.trim()

    if (!enabled || !trimmedNamespace || !trimmedPodName) {
      isAttachedRef.current = false
      lastSentSizeRef.current = null
      setSessionInfo(null)
      if (!enabled) {
        setStatus('idle')
      }
      return
    }

    if (manuallyDisconnected) {
      return
    }

    const gen = ++connectGenRef.current
    let isDisposed = false
    let reportedError: string | null = null
    isAttachedRef.current = false
    lastSentSizeRef.current = null

    setStatus('connecting')
    setError(null)
    setExitCode(null)
    setSessionInfo(null)

    const normalizedClusterId = clusterId && clusterId.trim().length > 0 ? clusterId.trim() : null
    const normalizedContainer = container && container.trim().length > 0 ? container.trim() : null
    const normalizedShell = normalizeRequestedShell(shell)

    const readCurrentDimensions = () => {
      const live = getDimensionsRef.current?.()
      const cols = clampCols(live?.cols ?? sizeRef.current.cols, clampCols(initialCols))
      const rows = clampRows(live?.rows ?? sizeRef.current.rows, clampRows(initialRows))
      sizeRef.current = { cols, rows }
      return { cols, rows }
    }

    const buildAttachPayload = (): PodTerminalAttachRequestPayload => {
      const { cols, rows } = readCurrentDimensions()
      return {
        namespace: trimmedNamespace,
        podName: trimmedPodName,
        clusterId: normalizedClusterId,
        container: normalizedContainer,
        shell: normalizedShell,
        cols,
        rows,
      }
    }

    const hubUrl = resolvePodTerminalHubUrl()
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => getSignalRAccessToken(accessTokenRef.current),
      })
      .withAutomaticReconnect([0, 1000, 2500, 5000, 10000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    connectionRef.current = connection

    const isCurrent = () => !isDisposed && connectGenRef.current === gen

    const reportAttachFailure = (msg: string) => {
      if (!isCurrent()) return
      isAttachedRef.current = false
      setError(msg)
      setStatus('error')
      if (reportedError !== msg) {
        reportedError = msg
        onErrorRef.current?.(msg)
      }
    }

    connection.on('Output', (data: string) => {
      if (!isCurrent() || !data) return
      onOutputRef.current?.(data)
    })

    connection.on('Attached', (info: PodTerminalAttachedInfo) => {
      if (!isCurrent()) return
      isAttachedRef.current = true
      reportedError = null
      lastSentSizeRef.current = { cols: info.cols, rows: info.rows }
      setSessionInfo(info)
      setError(null)
      setExitCode(null)
      setStatus('connected')
      onAttachedRef.current?.(info)
    })

    connection.on('Closed', (code?: number | null) => {
      if (!isCurrent()) return
      isAttachedRef.current = false
      lastSentSizeRef.current = null
      const normalizedCode = typeof code === 'number' ? code : 0
      setExitCode(normalizedCode)
      setStatus('disconnected')
      onClosedRef.current?.(normalizedCode)

      // Stop idle transport once remote shell process has exited
      connectGenRef.current += 1
      connectionRef.current = null
      void connection.stop().catch(() => {})
    })

    connection.on('Error', (message: string) => {
      if (!isCurrent()) return
      const errText = message || 'Pod terminal error.'
      reportAttachFailure(errText)
    })

    connection.onreconnecting(() => {
      if (!isCurrent()) return
      isAttachedRef.current = false
      lastSentSizeRef.current = null
      setStatus('reconnecting')
    })

    const invokeAttach = async () => {
      const payload = buildAttachPayload()
      const result = await connection.invoke<PodTerminalAttachResultPayload>('Attach', payload)
      if (!isCurrent()) return

      if (!result || !result.succeeded) {
        reportAttachFailure(result?.error || 'Failed to attach to pod terminal.')
        return
      }

      isAttachedRef.current = true
      reportedError = null
      setError(null)
      setExitCode(null)
      setStatus('connected')

      setSessionInfo((prev) => {
        if (prev && prev.sessionId === result.sessionId) {
          return prev
        }
        const fallbackInfo: PodTerminalAttachedInfo = {
          sessionId: result.sessionId ?? '',
          clusterId: result.clusterId ?? normalizedClusterId ?? 'default',
          namespace: result.namespace ?? trimmedNamespace,
          podName: result.podName ?? trimmedPodName,
          container: result.container ?? normalizedContainer,
          resolvedShell: result.resolvedShell ?? normalizedShell ?? '/bin/sh',
          cols: payload.cols,
          rows: payload.rows,
        }
        return fallbackInfo
      })

      // Sync dimensions if viewport resized while WebSocket/Attach handshake was in flight
      const latestDims = readCurrentDimensions()
      lastSentSizeRef.current = { cols: payload.cols, rows: payload.rows }
      if (latestDims.cols !== payload.cols || latestDims.rows !== payload.rows) {
        lastSentSizeRef.current = latestDims
        void connection.invoke('Resize', latestDims.cols, latestDims.rows).catch(() => {})
      }
    }

    connection.onreconnected(() => {
      if (!isCurrent()) return
      setStatus('connecting')
      void invokeAttach().catch((err: unknown) => {
        if (!isCurrent()) return
        const msg = err instanceof Error ? err.message : 'Failed to reattach pod terminal session.'
        reportAttachFailure(msg)
      })
    })

    connection.onclose((closeErr) => {
      if (!isCurrent()) return
      isAttachedRef.current = false
      lastSentSizeRef.current = null
      if (closeErr) {
        const msg = closeErr instanceof Error ? closeErr.message : 'Pod terminal connection closed unexpectedly.'
        reportAttachFailure(msg)
      } else {
        setStatus((prev) => (prev === 'error' ? prev : 'disconnected'))
      }
    })

    const start = async () => {
      try {
        await connection.start()
        if (!isCurrent()) return
        await invokeAttach()
      } catch (err: unknown) {
        if (!isCurrent()) return
        const msg = err instanceof Error ? err.message : 'Unable to connect to pod terminal hub.'
        reportAttachFailure(msg)
      }
    }

    void start()

    return () => {
      isDisposed = true
      const wasAttached = isAttachedRef.current
      isAttachedRef.current = false
      lastSentSizeRef.current = null
      if (connectionRef.current === connection) {
        connectionRef.current = null
      }
      teardownConnection(connection, wasAttached)
    }
  }, [
    clusterId,
    namespace,
    podName,
    container,
    shell,
    enabled,
    manuallyDisconnected,
    reconnectEpoch,
    initialCols,
    initialRows,
    teardownConnection,
  ])

  return {
    status,
    sessionInfo,
    error,
    exitCode,
    sendInput,
    resize,
    reconnect,
    disconnect,
  }
}
