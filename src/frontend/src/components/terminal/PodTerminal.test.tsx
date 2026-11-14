import { act, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as signalR from '@microsoft/signalr'
import { setApiKey, setAuthTokenProvider } from '../../api/client'
import {
  normalizeRequestedShell,
  usePodTerminal,
  type PodTerminalAttachRequestPayload,
  type PodTerminalAttachResultPayload,
  type PodTerminalAttachedInfo,
} from './usePodTerminal'
import { PodTerminal } from './PodTerminal'

// --- Controllable SignalR Mock ---
interface MockHubInstance {
  url: string
  options?: signalR.IHttpConnectionOptions
  state: signalR.HubConnectionState
  handlers: Map<string, Array<(...args: unknown[]) => void>>
  reconnectingCallbacks: Array<(error?: Error) => void>
  reconnectedCallbacks: Array<(connectionId?: string) => void>
  closeCallbacks: Array<(error?: Error) => void>
  start: ReturnType<typeof vi.fn>
  stop: ReturnType<typeof vi.fn>
  invoke: ReturnType<typeof vi.fn>
  emit: (event: string, ...args: unknown[]) => void
  triggerReconnecting: (error?: Error) => void
  triggerReconnected: (connectionId?: string) => void
  triggerClose: (error?: Error) => void
}

const mockHubInstances: MockHubInstance[] = []
let nextAttachHandler: ((payload: PodTerminalAttachRequestPayload, hub: MockHubInstance) => Promise<PodTerminalAttachResultPayload>) | null = null

vi.mock('@microsoft/signalr', () => {
  const HubConnectionState = {
    Disconnected: 'Disconnected',
    Connecting: 'Connecting',
    Connected: 'Connected',
    Disconnecting: 'Disconnecting',
    Reconnecting: 'Reconnecting',
  } as const

  const LogLevel = {
    Trace: 0,
    Debug: 1,
    Information: 2,
    Warning: 3,
    Error: 4,
    Critical: 5,
    None: 6,
  } as const

  class MockHubConnectionBuilder {
    private url = ''
    private options?: signalR.IHttpConnectionOptions

    withUrl(url: string, options?: signalR.IHttpConnectionOptions) {
      this.url = url
      this.options = options
      return this
    }

    withAutomaticReconnect() {
      return this
    }

    configureLogging() {
      return this
    }

    build() {
      const handlers = new Map<string, Array<(...args: unknown[]) => void>>()
      const reconnectingCallbacks: Array<(error?: Error) => void> = []
      const reconnectedCallbacks: Array<(connectionId?: string) => void> = []
      const closeCallbacks: Array<(error?: Error) => void> = []

      const instance: MockHubInstance = {
        url: this.url,
        options: this.options,
        state: HubConnectionState.Disconnected as unknown as signalR.HubConnectionState,
        handlers,
        reconnectingCallbacks,
        reconnectedCallbacks,
        closeCallbacks,
        start: vi.fn(async () => {
          instance.state = HubConnectionState.Connected as unknown as signalR.HubConnectionState
        }),
        stop: vi.fn(async () => {
          instance.state = HubConnectionState.Disconnected as unknown as signalR.HubConnectionState
        }),
        invoke: vi.fn(async (method: string, ...args: unknown[]) => {
          if (method === 'Attach') {
            const req = args[0] as PodTerminalAttachRequestPayload
            if (nextAttachHandler) {
              return nextAttachHandler(req, instance)
            }
            const info: PodTerminalAttachedInfo = {
              sessionId: `sess-${mockHubInstances.length}`,
              clusterId: req.clusterId ?? 'default',
              namespace: req.namespace,
              podName: req.podName,
              container: req.container ?? 'app',
              resolvedShell: req.shell ?? '/bin/bash',
              cols: req.cols,
              rows: req.rows,
            }
            instance.emit('Attached', info)
            return {
              succeeded: true,
              sessionId: info.sessionId,
              clusterId: info.clusterId,
              namespace: info.namespace,
              podName: info.podName,
              container: info.container,
              resolvedShell: info.resolvedShell,
            } satisfies PodTerminalAttachResultPayload
          }
          return undefined
        }),
        emit(event: string, ...args: unknown[]) {
          const list = handlers.get(event) ?? []
          for (const cb of list) {
            cb(...args)
          }
        },
        triggerReconnecting(error?: Error) {
          instance.state = HubConnectionState.Reconnecting as unknown as signalR.HubConnectionState
          for (const cb of reconnectingCallbacks) {
            cb(error)
          }
        },
        triggerReconnected(connectionId = 'reconnected-id') {
          instance.state = HubConnectionState.Connected as unknown as signalR.HubConnectionState
          for (const cb of reconnectedCallbacks) {
            cb(connectionId)
          }
        },
        triggerClose(error?: Error) {
          instance.state = HubConnectionState.Disconnected as unknown as signalR.HubConnectionState
          for (const cb of closeCallbacks) {
            cb(error)
          }
        },
      }

      const connectionObject = {
        get state() {
          return instance.state
        },
        start: instance.start,
        stop: instance.stop,
        invoke: instance.invoke,
        on: (event: string, cb: (...args: unknown[]) => void) => {
          const existing = handlers.get(event) ?? []
          existing.push(cb)
          handlers.set(event, existing)
        },
        off: (event: string, cb: (...args: unknown[]) => void) => {
          const existing = handlers.get(event) ?? []
          handlers.set(
            event,
            existing.filter((fn) => fn !== cb)
          )
        },
        onreconnecting: (cb: (error?: Error) => void) => {
          reconnectingCallbacks.push(cb)
        },
        onreconnected: (cb: (connectionId?: string) => void) => {
          reconnectedCallbacks.push(cb)
        },
        onclose: (cb: (error?: Error) => void) => {
          closeCallbacks.push(cb)
        },
      }

      mockHubInstances.push(instance)
      return connectionObject
    }
  }

  return {
    HubConnectionBuilder: MockHubConnectionBuilder,
    HubConnectionState,
    LogLevel,
  }
})

// --- Controllable @xterm/xterm & @xterm/addon-fit Mocks ---
interface MockTerminalInstance {
  options: Record<string, unknown>
  cols: number
  rows: number
  writes: string[]
  selection: string
  disposed: boolean
  customKeyHandler: ((event: KeyboardEvent) => boolean) | null
  open: ReturnType<typeof vi.fn>
  write: ReturnType<typeof vi.fn>
  clear: ReturnType<typeof vi.fn>
  focus: ReturnType<typeof vi.fn>
  dispose: ReturnType<typeof vi.fn>
  hasSelection: ReturnType<typeof vi.fn>
  getSelection: ReturnType<typeof vi.fn>
  clearSelection: ReturnType<typeof vi.fn>
  emitData: (data: string) => void
  emitResize: (cols: number, rows: number) => void
}

const mockTerminals: MockTerminalInstance[] = []
let nextFitDimensions: { cols: number; rows: number } | null = null
const mockFitCalls: number[] = []

vi.mock('@xterm/xterm', () => {
  class MockTerminal {
    options: Record<string, unknown>
    cols = 100
    rows = 30
    writes: string[] = []
    selection = ''
    disposed = false
    customKeyHandler: ((event: KeyboardEvent) => boolean) | null = null
    private dataListeners: Array<(data: string) => void> = []
    private resizeListeners: Array<(size: { cols: number; rows: number }) => void> = []

    buffer = {
      active: {
        length: 1,
        getLine: () => ({
          translateToString: () => 'root@pod:/# uptime',
        }),
      },
    }

    open = vi.fn()
    write = vi.fn((data: string, cb?: () => void) => {
      this.writes.push(data)
      cb?.()
    })
    clear = vi.fn(() => {
      this.writes = []
    })
    focus = vi.fn()
    dispose = vi.fn(() => {
      this.disposed = true
    })
    hasSelection = vi.fn(() => this.selection.length > 0)
    getSelection = vi.fn(() => this.selection)
    clearSelection = vi.fn(() => {
      this.selection = ''
    })

    constructor(opts: Record<string, unknown> = {}) {
      this.options = opts
      mockTerminals.push(this as unknown as MockTerminalInstance)
    }

    loadAddon(addon: { activate?: (term: unknown) => void }) {
      addon.activate?.(this)
    }

    attachCustomKeyEventHandler(handler: (event: KeyboardEvent) => boolean) {
      this.customKeyHandler = handler
    }

    onData(cb: (data: string) => void) {
      this.dataListeners.push(cb)
      return {
        dispose: () => {
          this.dataListeners = this.dataListeners.filter((fn) => fn !== cb)
        },
      }
    }

    onResize(cb: (size: { cols: number; rows: number }) => void) {
      this.resizeListeners.push(cb)
      return {
        dispose: () => {
          this.resizeListeners = this.resizeListeners.filter((fn) => fn !== cb)
        },
      }
    }

    emitData(data: string) {
      for (const cb of this.dataListeners) {
        cb(data)
      }
    }

    emitResize(cols: number, rows: number) {
      this.cols = cols
      this.rows = rows
      for (const cb of this.resizeListeners) {
        cb({ cols, rows })
      }
    }
  }

  return { Terminal: MockTerminal }
})

vi.mock('@xterm/addon-fit', () => {
  class MockFitAddon {
    private term: MockTerminalInstance | null = null

    activate(term: MockTerminalInstance) {
      this.term = term
    }

    fit() {
      mockFitCalls.push(1)
      if (this.term && nextFitDimensions) {
        this.term.emitResize(nextFitDimensions.cols, nextFitDimensions.rows)
      }
    }

    dispose() {
      this.term = null
    }
  }

  return { FitAddon: MockFitAddon }
})

// --- Controllable ResizeObserver for PTY Resize Verification ---
let resizeObserverCallbacks: ResizeObserverCallback[] = []

class ControllableResizeObserver implements ResizeObserver {
  private callback: ResizeObserverCallback

  constructor(callback: ResizeObserverCallback) {
    this.callback = callback
    resizeObserverCallbacks.push(callback)
  }

  observe() {}
  unobserve() {}
  disconnect() {
    resizeObserverCallbacks = resizeObserverCallbacks.filter((cb) => cb !== this.callback)
  }
}

describe('usePodTerminal hook', () => {
  const originalResizeObserver = global.ResizeObserver

  beforeEach(() => {
    mockHubInstances.length = 0
    mockTerminals.length = 0
    mockFitCalls.length = 0
    nextAttachHandler = null
    nextFitDimensions = null
    resizeObserverCallbacks = []
    global.ResizeObserver = ControllableResizeObserver
    localStorage.clear()
    setApiKey('')
    setAuthTokenProvider(null)
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation((cb: FrameRequestCallback) => {
      cb(0)
      return 1
    })
    vi.spyOn(window, 'cancelAnimationFrame').mockImplementation(() => {})
  })

  afterEach(() => {
    global.ResizeObserver = originalResizeObserver
    vi.restoreAllMocks()
  })

  it('normalizes requested shell values properly', () => {
    expect(normalizeRequestedShell(undefined)).toBeNull()
    expect(normalizeRequestedShell('auto')).toBeNull()
    expect(normalizeRequestedShell('  AUTO  ')).toBeNull()
    expect(normalizeRequestedShell('/bin/bash')).toBe('/bin/bash')
    expect(normalizeRequestedShell(' /bin/ash ')).toBe('/bin/ash')
  })

  it('connects to /hubs/pod-terminal with auth token, attaches, streams output, and forwards input/resize', async () => {
    setApiKey('cp_pat_test_operator_token')
    const outputs: string[] = []

    const { result, unmount } = renderHook(() =>
      usePodTerminal({
        clusterId: 'k3s-edge',
        namespace: 'monitoring',
        podName: 'prometheus-0',
        container: 'prometheus',
        shell: 'auto',
        initialCols: 120,
        initialRows: 36,
        onOutput: (chunk) => outputs.push(chunk),
      })
    )

    await waitFor(() => {
      expect(result.current.status).toBe('connected')
    })

    expect(mockHubInstances).toHaveLength(1)
    const hub = mockHubInstances[0]
    expect(hub.url).toContain('/hubs/pod-terminal')

    // Verify SignalR accessTokenFactory supplies the configured PAT / API key
    const resolvedToken = await hub.options?.accessTokenFactory?.()
    expect(resolvedToken).toBe('cp_pat_test_operator_token')

    expect(hub.invoke).toHaveBeenCalledWith('Attach', {
      namespace: 'monitoring',
      podName: 'prometheus-0',
      clusterId: 'k3s-edge',
      container: 'prometheus',
      shell: null,
      cols: 120,
      rows: 36,
    })

    expect(result.current.sessionInfo?.resolvedShell).toBe('/bin/bash')

    // Server streams raw ANSI output
    act(() => {
      hub.emit('Output', '\x1b[32mprometheus-0:/$\x1b[0m ')
    })
    expect(outputs).toEqual(['\x1b[32mprometheus-0:/$\x1b[0m '])

    // Forward keystrokes via Input
    act(() => {
      result.current.sendInput('ls -la\r')
    })
    expect(hub.invoke).toHaveBeenCalledWith('Input', 'ls -la\r')

    // Forward deduplicated PTY Resize
    act(() => {
      result.current.resize(140, 44)
      result.current.resize(140, 44) // duplicate should be skipped
    })

    const resizeCalls = hub.invoke.mock.calls.filter(([method]) => method === 'Resize')
    expect(resizeCalls).toEqual([['Resize', 140, 44]])

    // Unmount detaches and stops hub cleanly
    unmount()
    expect(hub.invoke).toHaveBeenCalledWith('Detach')
    expect(hub.stop).toHaveBeenCalled()
  })

  it('handles remote shell exit (Closed), manual disconnect, reconnect, and SignalR automatic reconnect', async () => {
    localStorage.setItem('cp_auth_mode', 'oidc')
    setAuthTokenProvider(() => 'oidc-jwt-token-xyz')

    const closedCodes: Array<number | null> = []
    let liveDims = { cols: 90, rows: 28 }

    const { result } = renderHook(() =>
      usePodTerminal({
        namespace: 'default',
        podName: 'nginx-abc',
        getDimensions: () => liveDims,
        onClosed: (code) => closedCodes.push(code),
      })
    )

    await waitFor(() => {
      expect(result.current.status).toBe('connected')
    })

    const firstHub = mockHubInstances[0]
    expect(await firstHub.options?.accessTokenFactory?.()).toBe('oidc-jwt-token-xyz')

    // Simulate SignalR transport drop & automatic reconnect with updated viewport size
    act(() => {
      firstHub.triggerReconnecting(new Error('network blip'))
    })
    expect(result.current.status).toBe('reconnecting')

    liveDims = { cols: 112, rows: 34 }
    await act(async () => {
      firstHub.triggerReconnected('new-conn-id')
    })

    await waitFor(() => {
      expect(result.current.status).toBe('connected')
    })
    expect(firstHub.invoke).toHaveBeenLastCalledWith(
      'Attach',
      expect.objectContaining({
        namespace: 'default',
        podName: 'nginx-abc',
        cols: 112,
        rows: 34,
      })
    )

    // Simulate remote shell exiting with code 0
    act(() => {
      firstHub.emit('Closed', 0)
    })

    expect(result.current.status).toBe('disconnected')
    expect(result.current.exitCode).toBe(0)
    expect(closedCodes).toEqual([0])

    // Manual reconnect creates a fresh hub session
    act(() => {
      result.current.reconnect()
    })

    await waitFor(() => {
      expect(result.current.status).toBe('connected')
    })
    expect(mockHubInstances).toHaveLength(2)

    // Manual disconnect sends Detach and marks status disconnected
    const secondHub = mockHubInstances[1]
    act(() => {
      result.current.disconnect()
    })
    expect(result.current.status).toBe('disconnected')
    expect(secondHub.invoke).toHaveBeenCalledWith('Detach')
    expect(secondHub.stop).toHaveBeenCalled()
  })

  it('surfaces attach errors without firing duplicate onError notifications', async () => {
    const errors: string[] = []
    nextAttachHandler = async (_req, hub) => {
      const message = "Pod 'missing-pod' was not found in namespace 'default'."
      hub.emit('Error', message)
      return {
        succeeded: false,
        error: message,
      }
    }

    const { result } = renderHook(() =>
      usePodTerminal({
        namespace: 'default',
        podName: 'missing-pod',
        onError: (msg) => errors.push(msg),
      })
    )

    await waitFor(() => {
      expect(result.current.status).toBe('error')
    })

    expect(result.current.error).toContain("Pod 'missing-pod' was not found")
    expect(errors).toHaveLength(1)
  })
})

describe('PodTerminal component', () => {
  const originalResizeObserver = global.ResizeObserver

  beforeEach(() => {
    mockHubInstances.length = 0
    mockTerminals.length = 0
    mockFitCalls.length = 0
    nextAttachHandler = null
    nextFitDimensions = null
    resizeObserverCallbacks = []
    global.ResizeObserver = ControllableResizeObserver
    localStorage.clear()
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation((cb: FrameRequestCallback) => {
      cb(0)
      return 1
    })
    vi.spyOn(window, 'cancelAnimationFrame').mockImplementation(() => {})
  })

  afterEach(() => {
    global.ResizeObserver = originalResizeObserver
    vi.restoreAllMocks()
  })

  it('mounts xterm in convertEol: false raw PTY mode, wires I/O, and resizes PTY via ResizeObserver', async () => {
    render(
      <PodTerminal
        clusterId="prod-cluster"
        clusterName="Prod K8s"
        namespace="apps"
        podName="api-server-7f9d"
        containers={['api', 'istio-proxy']}
      />
    )

    expect(mockTerminals).toHaveLength(1)
    const term = mockTerminals[0]

    // Critical raw PTY invariant: convertEol MUST be false
    expect(term.options.convertEol).toBe(false)
    expect(term.open).toHaveBeenCalledTimes(1)
    expect(mockFitCalls.length).toBeGreaterThan(0)

    await waitFor(() => {
      expect(screen.getByTestId('pod-terminal-status')).toHaveTextContent('Connected')
    })
    expect(screen.getByTestId('pod-terminal-status')).toHaveTextContent('/bin/bash')
    expect(screen.getByText('Prod K8s')).toBeInTheDocument()

    const hub = mockHubInstances[0]
    expect(hub.invoke).toHaveBeenCalledWith('Attach', {
      namespace: 'apps',
      podName: 'api-server-7f9d',
      clusterId: 'prod-cluster',
      container: 'api',
      shell: null,
      cols: 100,
      rows: 30,
    })

    // User types into xterm -> forwarded to SignalR Input
    act(() => {
      term.emitData('whoami\r')
    })
    expect(hub.invoke).toHaveBeenCalledWith('Input', 'whoami\r')

    // Server emits raw PTY output -> written to xterm
    act(() => {
      hub.emit('Output', 'root\r\n')
    })
    expect(term.writes).toContain('root\r\n')

    // Ctrl+C without selection passes through (returns true) so SIGINT reaches remote PTY
    const sigintAllowed = term.customKeyHandler?.({
      type: 'keydown',
      ctrlKey: true,
      metaKey: false,
      key: 'c',
    } as KeyboardEvent)
    expect(sigintAllowed).toBe(true)

    // Ctrl+C WITH selection intercepts (returns false) to copy text instead of killing foreground process
    term.selection = 'selected-log-text'
    const copyIntercepted = term.customKeyHandler?.({
      type: 'keydown',
      ctrlKey: true,
      metaKey: false,
      key: 'c',
    } as KeyboardEvent)
    expect(copyIntercepted).toBe(false)

    // ResizeObserver fires when drawer/window resizes -> FitAddon fits -> SignalR Resize(136, 42)
    nextFitDimensions = { cols: 136, rows: 42 }
    act(() => {
      for (const cb of resizeObserverCallbacks) {
        cb([], {} as ResizeObserver)
      }
    })

    expect(hub.invoke).toHaveBeenCalledWith('Resize', 136, 42)
  })

  it('supports switching container and shell selectors and disconnect/reconnect lifecycle', async () => {
    const onContainerChange = vi.fn()
    const onShellChange = vi.fn()

    render(
      <PodTerminal
        namespace="kube-system"
        podName="coredns-5d78c9869d"
        containers={['coredns', 'metrics-sidecar']}
        onContainerChange={onContainerChange}
        onShellChange={onShellChange}
      />
    )

    await waitFor(() => {
      expect(screen.getByTestId('pod-terminal-status')).toHaveTextContent('Connected')
    })

    const firstHub = mockHubInstances[0]
    expect(firstHub.invoke).toHaveBeenCalledWith(
      'Attach',
      expect.objectContaining({
        container: 'coredns',
        shell: null,
      })
    )

    // Switch container -> detaches previous session and attaches to metrics-sidecar
    const containerSelect = screen.getByLabelText('Container')
    fireEvent.change(containerSelect, { target: { value: 'metrics-sidecar' } })
    expect(onContainerChange).toHaveBeenCalledWith('metrics-sidecar')
    expect(firstHub.invoke).toHaveBeenCalledWith('Detach')

    await waitFor(() => {
      expect(mockHubInstances).toHaveLength(2)
      expect(screen.getByTestId('pod-terminal-status')).toHaveTextContent('Connected')
    })

    const secondHub = mockHubInstances[1]
    expect(secondHub.invoke).toHaveBeenCalledWith(
      'Attach',
      expect.objectContaining({
        container: 'metrics-sidecar',
        shell: null,
      })
    )

    // Switch shell -> detaches previous session and attaches with explicit /bin/sh
    const shellSelect = screen.getByLabelText('Shell')
    fireEvent.change(shellSelect, { target: { value: '/bin/sh' } })
    expect(onShellChange).toHaveBeenCalledWith('/bin/sh')
    expect(secondHub.invoke).toHaveBeenCalledWith('Detach')

    await waitFor(() => {
      expect(mockHubInstances).toHaveLength(3)
      expect(screen.getByTestId('pod-terminal-status')).toHaveTextContent('Connected')
    })

    const thirdHub = mockHubInstances[2]
    expect(thirdHub.invoke).toHaveBeenCalledWith(
      'Attach',
      expect.objectContaining({
        container: 'metrics-sidecar',
        shell: '/bin/sh',
      })
    )

    // Click Disconnect -> detaches and renders Reconnect button
    fireEvent.click(screen.getByTitle('Disconnect session'))
    expect(thirdHub.invoke).toHaveBeenCalledWith('Detach')
    expect(screen.getByTestId('pod-terminal-status')).toHaveTextContent('Disconnected')

    // Click Reconnect -> re-establishes session
    fireEvent.click(screen.getByTitle('Reconnect terminal session'))
    await waitFor(() => {
      expect(mockHubInstances).toHaveLength(4)
      expect(screen.getByTestId('pod-terminal-status')).toHaveTextContent('Connected')
    })
  })
})
