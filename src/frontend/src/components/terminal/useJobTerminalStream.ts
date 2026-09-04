import { useEffect, useRef, useState, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'
import { apiClient } from '../../api/client'

export interface StepLogLine {
  id: number
  jobId: string
  sequenceId: number
  streamType: string
  logLine: string
  timestamp: string
}

export interface UseJobTerminalStreamOptions {
  jobId: string | null
  onLogLine?: (line: string, streamType: string, sequenceId: number) => void
  onStatusChanged?: (status: string, activeStep?: string | null) => void
}

export function useJobTerminalStream({
  jobId,
  onLogLine,
  onStatusChanged,
}: UseJobTerminalStreamOptions) {
  const [status, setStatus] = useState<'idle' | 'connecting' | 'connected' | 'completed' | 'failed'>('idle')
  const [lineCount, setLineCount] = useState(0)
  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const lastSequenceIdRef = useRef<number>(0)
  const onLogLineRef = useRef(onLogLine)
  const onStatusChangedRef = useRef(onStatusChanged)

  useEffect(() => {
    onLogLineRef.current = onLogLine
    onStatusChangedRef.current = onStatusChanged
  })

  const fetchHistoricalLogs = useCallback(async (id: string) => {
    try {
      const data = await apiClient<StepLogLine[]>(`/api/v1/jobs/${id}/logs?fromSequenceId=0`)
      if (data && data.length > 0) {
        setLineCount(data.length)
        for (const entry of data) {
          lastSequenceIdRef.current = Math.max(lastSequenceIdRef.current, entry.sequenceId)
          onLogLineRef.current?.(entry.logLine, entry.streamType, entry.sequenceId)
        }
      }
    } catch {
      // Job might not have logs yet
    }
  }, [])

  useEffect(() => {
    if (!jobId) {
      return
    }

    const currentJobId = jobId
    let isMounted = true

    const hubUrl = `${window.location.origin}/hubs/jobs`
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    connectionRef.current = connection

    connection.on('ReceiveLogLine', (receivedJobId: string, sequenceId: number, streamType: string, logLine: string) => {
      if (receivedJobId.toLowerCase() === currentJobId.toLowerCase()) {
        if (sequenceId > lastSequenceIdRef.current) {
          lastSequenceIdRef.current = sequenceId
          setLineCount((prev) => prev + 1)
          onLogLineRef.current?.(logLine, streamType, sequenceId)
        }
      }
    })

    connection.on('JobStatusChanged', (receivedJobId: string, jobStatus: string, activeStep?: string | null) => {
      if (receivedJobId.toLowerCase() === currentJobId.toLowerCase()) {
        if (jobStatus === 'Completed') {
          setStatus('completed')
        } else if (jobStatus === 'Failed') {
          setStatus('failed')
        }
        onStatusChangedRef.current?.(jobStatus, activeStep)
      }
    })

    async function start() {
      try {
        setStatus('connecting')
        await connection.start()
        if (!isMounted) return

        await connection.invoke('JoinJobGroup', currentJobId)
        setStatus('connected')

        // Fetch backlog
        await fetchHistoricalLogs(currentJobId)
      } catch {
        if (isMounted) {
          setStatus('idle')
        }
      }
    }

    start()

    return () => {
      isMounted = false
      if (connection.state === signalR.HubConnectionState.Connected) {
        connection.invoke('LeaveJobGroup', currentJobId).catch(() => {})
      }
      connection.stop().catch(() => {})
      connectionRef.current = null
    }
  }, [jobId, fetchHistoricalLogs])

  return {
    status,
    lineCount,
  }
}
