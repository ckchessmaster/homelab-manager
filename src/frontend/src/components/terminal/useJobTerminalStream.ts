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

export interface JobDetails {
  id: string
  targetHostId: string
  status: string
  activeStep?: string | null
  initiatedBy: string
  startedAt?: string | null
  completedAt?: string | null
  failureReason?: string | null
}

export function useJobTerminalStream({
  jobId,
  onLogLine,
  onStatusChanged,
}: UseJobTerminalStreamOptions) {
  const [status, setStatus] = useState<'idle' | 'connecting' | 'connected' | 'completed' | 'failed'>('idle')
  const [jobState, setJobState] = useState<string | null>(null)
  const [activeStep, setActiveStep] = useState<string | null>(null)
  const [failureReason, setFailureReason] = useState<string | null>(null)
  const [lineCount, setLineCount] = useState(0)
  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const lastSequenceIdRef = useRef<number>(0)
  const onLogLineRef = useRef(onLogLine)
  const onStatusChangedRef = useRef(onStatusChanged)

  useEffect(() => {
    onLogLineRef.current = onLogLine
    onStatusChangedRef.current = onStatusChanged
  })

  const fetchJobMetadata = useCallback(async (id: string) => {
    try {
      const data = await apiClient<JobDetails>(`/api/v1/jobs/${id}`)
      if (data) {
        setJobState(data.status)
        setActiveStep(data.activeStep ?? null)
        setFailureReason(data.failureReason ?? null)
        if (data.status === 'Completed') {
          setStatus('completed')
        } else if (data.status === 'Failed' || data.status === 'RolledBack') {
          setStatus('failed')
        }
        onStatusChangedRef.current?.(data.status, data.activeStep)
      }
    } catch {
      // Job might not exist or still creating
    }
  }, [])

  const fetchHistoricalLogs = useCallback(async (id: string) => {
    try {
      const fromSeq = lastSequenceIdRef.current > 0 ? lastSequenceIdRef.current + 1 : 0
      const data = await apiClient<StepLogLine[]>(`/api/v1/jobs/${id}/logs?fromSequenceId=${fromSeq}`)
      if (data && data.length > 0) {
        setLineCount((prev) => prev + data.length)
        for (const entry of data) {
          if (entry.sequenceId > lastSequenceIdRef.current) {
            lastSequenceIdRef.current = entry.sequenceId
            onLogLineRef.current?.(entry.logLine, entry.streamType, entry.sequenceId)
          }
        }
      }
    } catch {
      // Job might not have logs yet
    }
  }, [])

  const [prevJobId, setPrevJobId] = useState<string | null>(jobId)
  if (jobId !== prevJobId) {
    setPrevJobId(jobId)
    setJobState(null)
    setActiveStep(null)
    setFailureReason(null)
    setLineCount(0)
  }

  useEffect(() => {
    lastSequenceIdRef.current = 0
    if (!jobId) {
      return
    }

    const currentJobId = jobId
    let isMounted = true

    // 1. Immediately fetch existing logs and metadata
    const loadInitialData = async () => {
      await Promise.all([
        fetchHistoricalLogs(currentJobId),
        fetchJobMetadata(currentJobId),
      ])
    }
    void loadInitialData()

    // 2. Setup SignalR connection for low-latency live streaming
    const hubUrl = `${window.location.origin}/hubs/jobs`
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect([0, 1500, 3000, 5000, 10000])
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

    connection.on('JobStatusChanged', (receivedJobId: string, receivedStatus: string, step?: string | null) => {
      if (receivedJobId.toLowerCase() === currentJobId.toLowerCase()) {
        setJobState(receivedStatus)
        setActiveStep(step ?? null)
        if (receivedStatus === 'Completed') {
          setStatus('completed')
        } else if (receivedStatus === 'Failed' || receivedStatus === 'RolledBack') {
          setStatus('failed')
        }
        onStatusChangedRef.current?.(receivedStatus, step)
      }
    })

    async function start() {
      try {
        setStatus('connecting')
        await connection.start()
        if (!isMounted) return

        await connection.invoke('JoinJobGroup', currentJobId)
        if (!isMounted) return
        setStatus('connected')

        // Backlog catch-up after joining group
        await Promise.all([
          fetchHistoricalLogs(currentJobId),
          fetchJobMetadata(currentJobId),
        ])
      } catch (err) {
        console.warn('[TerminalStream] SignalR connection failed, using fallback polling:', err)
        if (isMounted) {
          setStatus('idle')
        }
      }
    }

    start()

    // 3. Fallback polling timer (ensures updates if SignalR dropped or for fast jobs)
    const pollTimer = setInterval(() => {
      if (!isMounted) return
      fetchHistoricalLogs(currentJobId)
      fetchJobMetadata(currentJobId)
    }, 1500)

    return () => {
      isMounted = false
      clearInterval(pollTimer)
      if (connection.state === signalR.HubConnectionState.Connected) {
        connection.invoke('LeaveJobGroup', currentJobId).catch(() => {})
      }
      connection.stop().catch(() => {})
      connectionRef.current = null
    }
  }, [jobId, fetchHistoricalLogs, fetchJobMetadata])

  const effectiveStatus = jobId ? status : 'idle'

  return {
    status: effectiveStatus,
    jobState,
    activeStep,
    failureReason,
    lineCount,
  }
}
