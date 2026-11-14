import { fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthContext } from '../auth/AuthContext'
import type { AuthState } from '../auth/AuthTypes'
import type { PodSummary, WorkloadSummary } from '../../api/workloads'
import * as workloadsApi from '../../api/workloads'
import { PodDetailDrawer } from './PodDetailDrawer'

vi.mock('../../api/workloads', async () => {
  const actual = await vi.importActual<typeof import('../../api/workloads')>(
    '../../api/workloads'
  )
  return {
    ...actual,
    getWorkloadPods: vi.fn(),
    getAppBundle: vi.fn(),
    getPodLogs: vi.fn(),
    getWorkloadRevisions: vi.fn(),
  }
})

vi.mock('../../components/terminal/PodTerminal', () => ({
  PodTerminal: ({
    clusterId,
    clusterName,
    namespace,
    podName,
    containers,
    initialContainer,
  }: {
    clusterId?: string | null
    clusterName?: string | null
    namespace: string
    podName: string
    containers?: string[]
    initialContainer?: string | null
  }) => (
    <div
      data-testid="mock-pod-terminal"
      data-cluster-id={clusterId ?? ''}
      data-cluster-name={clusterName ?? ''}
      data-namespace={namespace}
      data-pod-name={podName}
      data-containers={(containers ?? []).join(',')}
      data-initial-container={initialContainer ?? ''}
    >
      PodTerminal:{namespace}/{podName}
    </div>
  ),
}))

function createMockAuth(overrides: Partial<AuthState> = {}): AuthState {
  return {
    authMode: 'oidc',
    isAuthenticated: true,
    isLoading: false,
    isBypass: false,
    user: {
      id: 'user-1',
      name: 'Test Operator',
      email: 'operator@example.com',
      roles: ['Operator', 'Viewer'],
    },
    token: 'jwt-token',
    roles: ['Operator', 'Viewer'],
    activeRole: 'Operator',
    login: vi.fn(),
    logout: vi.fn(),
    hasRole: (role) => role !== 'Admin',
    isAdmin: false,
    isOperator: true,
    isViewer: true,
    ...overrides,
  }
}

const sampleWorkload: WorkloadSummary = {
  clusterId: 'k3s-prod',
  clusterName: 'Production K3s',
  namespace: 'apps',
  name: 'api-gateway',
  kind: 'Deployment',
  desiredReplicas: 2,
  readyReplicas: 2,
  availableReplicas: 2,
  images: ['ghcr.io/homelab/api-gateway:1.2.0'],
  status: 'Ready',
}

const samplePods: PodSummary[] = [
  {
    name: 'api-gateway-7d9c8-alpha',
    namespace: 'apps',
    phase: 'Running',
    nodeName: 'worker-01',
    podIp: '10.42.1.10',
    restartCount: 0,
    isReady: true,
    containers: ['gateway', 'envoy'],
  },
  {
    name: 'api-gateway-7d9c8-beta',
    namespace: 'apps',
    phase: 'Running',
    nodeName: 'worker-02',
    podIp: '10.42.2.11',
    restartCount: 1,
    isReady: true,
    containers: ['gateway', 'envoy'],
  },
  {
    name: 'api-gateway-7d9c8-pending',
    namespace: 'apps',
    phase: 'Pending',
    nodeName: null,
    podIp: null,
    restartCount: 0,
    isReady: false,
    containers: ['gateway'],
  },
]

describe('PodDetailDrawer - Pod Exec Terminal & Expandable Width', { timeout: 15_000 }, () => {
  let queryClient: QueryClient

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    vi.clearAllMocks()
    vi.mocked(workloadsApi.getWorkloadPods).mockResolvedValue(samplePods)
    vi.mocked(workloadsApi.getAppBundle).mockResolvedValue({
      name: 'api-gateway',
      namespace: 'apps',
      kind: 'Deployment',
      replicas: 2,
      image: 'ghcr.io/homelab/api-gateway:1.2.0',
      ports: [],
      environmentVariables: [],
      volumeMounts: [],
    })
    vi.mocked(workloadsApi.getPodLogs).mockResolvedValue({
      podName: 'api-gateway-7d9c8-alpha',
      logs: '',
    })
    vi.mocked(workloadsApi.getWorkloadRevisions).mockResolvedValue([])
  })

  function renderDrawer(
    props: Partial<React.ComponentProps<typeof PodDetailDrawer>> = {},
    authOverrides: Partial<AuthState> = {}
  ) {
    const auth = createMockAuth(authOverrides)
    return render(
      <QueryClientProvider client={queryClient}>
        <AuthContext.Provider value={auth}>
          <PodDetailDrawer
            workload={sampleWorkload}
            isOpen={true}
            onClose={vi.fn()}
            {...props}
          />
        </AuthContext.Provider>
      </QueryClientProvider>
    )
  }

  it('renders Exec buttons only on Running pods and toggles expandable drawer width', async () => {
    renderDrawer()

    const alphaPodName = await screen.findByText('api-gateway-7d9c8-alpha', {}, { timeout: 5000 })
    expect(alphaPodName).toBeInTheDocument()

    // Terminal tab is visible for Operator
    expect(screen.getByRole('button', { name: /^Terminal$/i })).toBeInTheDocument()

    // Default width starts at 640px on Pods tab; toggle button expands and restores width
    const drawer = screen.getByTestId('pod-detail-drawer')
    const widthToggle = screen.getByTestId('pod-drawer-width-toggle')
    expect(drawer.className).toContain('sm:w-[640px]')

    fireEvent.click(widthToggle)
    expect(drawer.className).toContain('sm:w-[960px]')

    fireEvent.click(widthToggle)
    expect(drawer.className).toContain('sm:w-[640px]')

    // Only the 2 Running pods get an Exec action; Pending pod does not
    const execButtons = screen.getAllByRole('button', { name: /^Exec$/i })
    expect(execButtons).toHaveLength(2)

    const pendingCard = screen.getByText('api-gateway-7d9c8-pending').closest('div.p-3\\.5') as HTMLElement
    expect(within(pendingCard).queryByRole('button', { name: /^Exec$/i })).not.toBeInTheDocument()

    // Clicking Exec on the second running pod opens Terminal tab, auto-expands drawer, and targets beta pod
    fireEvent.click(execButtons[1])

    expect(drawer.className).toContain('sm:w-[960px]')
    const terminal = await screen.findByTestId('mock-pod-terminal', {}, { timeout: 5000 })
    expect(terminal).toHaveAttribute('data-cluster-id', 'k3s-prod')
    expect(terminal).toHaveAttribute('data-namespace', 'apps')
    expect(terminal).toHaveAttribute('data-pod-name', 'api-gateway-7d9c8-beta')
    expect(terminal).toHaveAttribute('data-containers', 'gateway,envoy')

    // Target Pod selector lets operator switch between Running replicas
    const targetPodSelect = screen.getByLabelText('Target Pod')
    fireEvent.change(targetPodSelect, { target: { value: 'api-gateway-7d9c8-alpha' } })
    expect(screen.getByTestId('mock-pod-terminal')).toHaveAttribute(
      'data-pod-name',
      'api-gateway-7d9c8-alpha'
    )
  })

  it('hides Terminal tab and Exec actions for Viewer or when canExecPods is false', async () => {
    renderDrawer(
      { initialTab: 'terminal' },
      {
        isOperator: false,
        isAdmin: false,
        canExecPods: false,
        activeRole: 'Viewer',
        roles: ['Viewer'],
      }
    )

    await screen.findByText('api-gateway-7d9c8-alpha', {}, { timeout: 5000 })

    expect(screen.queryByRole('button', { name: /^Terminal$/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Exec$/i })).not.toBeInTheDocument()
    expect(screen.queryByTestId('mock-pod-terminal')).not.toBeInTheDocument()
  })

  it('shows empty state in Terminal tab when workload has no Running pods', async () => {
    vi.mocked(workloadsApi.getWorkloadPods).mockResolvedValue([
      {
        name: 'api-gateway-pending',
        namespace: 'apps',
        phase: 'Pending',
        restartCount: 0,
        isReady: false,
      },
    ])

    renderDrawer({ initialTab: 'terminal' })

    expect(await screen.findByText(/No running pods available/i, {}, { timeout: 5000 })).toBeInTheDocument()
    expect(screen.queryByTestId('mock-pod-terminal')).not.toBeInTheDocument()
  })
})
