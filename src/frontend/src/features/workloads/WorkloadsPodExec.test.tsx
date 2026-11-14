import { fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthContext } from '../auth/AuthContext'
import type { AuthState } from '../auth/AuthTypes'
import type { WorkloadSummary } from '../../api/workloads'
import * as workloadsApi from '../../api/workloads'
import { WorkloadCard } from './WorkloadCard'
import { WorkloadsPage } from './WorkloadsPage'

vi.mock('../../api/workloads', async () => {
  const actual = await vi.importActual<typeof import('../../api/workloads')>(
    '../../api/workloads'
  )
  return {
    ...actual,
    getWorkloads: vi.fn(),
    getCachedImageUpdates: vi.fn(),
    getClusterEvents: vi.fn(),
    getWorkloadPods: vi.fn(),
    getAppBundle: vi.fn(),
  }
})

vi.mock('./helm/HelmReleasesView', () => ({
  HelmReleasesView: () => null,
}))
vi.mock('./ConfigSecretsView', () => ({
  ConfigSecretsView: () => null,
}))
vi.mock('./NetworkIngressView', () => ({
  NetworkIngressView: () => null,
}))
vi.mock('./StorageView', () => ({
  StorageView: () => null,
}))
vi.mock('./ClusterVitalsView', () => ({
  ClusterVitalsView: () => null,
}))
vi.mock('./ClusterEventsDock', () => ({
  ClusterEventsDock: () => null,
}))

vi.mock('../../components/terminal/PodTerminal', () => ({
  PodTerminal: ({ namespace, podName }: { namespace: string; podName: string }) => (
    <div data-testid="mock-pod-terminal">
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
      id: 'user-op',
      name: 'Operator',
      email: 'op@example.com',
      roles: ['Operator', 'Viewer'],
    },
    token: 'token',
    roles: ['Operator', 'Viewer'],
    activeRole: 'Operator',
    login: vi.fn(),
    logout: vi.fn(),
    hasRole: (r) => r !== 'Admin',
    isAdmin: false,
    isOperator: true,
    isViewer: true,
    ...overrides,
  }
}

const runningDeployment: WorkloadSummary = {
  clusterId: 'k3s-prod',
  clusterName: 'Production K3s',
  namespace: 'default',
  name: 'nginx-ingress',
  kind: 'Deployment',
  desiredReplicas: 2,
  readyReplicas: 2,
  availableReplicas: 2,
  images: ['nginx:1.27'],
  status: 'Ready',
}

const scaledDownDeployment: WorkloadSummary = {
  clusterId: 'k3s-prod',
  clusterName: 'Production K3s',
  namespace: 'default',
  name: 'idle-worker',
  kind: 'Deployment',
  desiredReplicas: 0,
  readyReplicas: 0,
  availableReplicas: 0,
  images: ['worker:1.0'],
  status: 'ScaledDown',
}

const degradedNoReadyDeployment: WorkloadSummary = {
  clusterId: 'k3s-prod',
  clusterName: 'Production K3s',
  namespace: 'default',
  name: 'broken-api',
  kind: 'Deployment',
  desiredReplicas: 1,
  readyReplicas: 0,
  availableReplicas: 0,
  images: ['broken:1.0'],
  status: 'Degraded',
}

describe('WorkloadCard & WorkloadsPage - Pod Exec Entry Points', { timeout: 15_000 }, () => {
  let queryClient: QueryClient

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    vi.clearAllMocks()
    vi.mocked(workloadsApi.getWorkloads).mockResolvedValue({
      items: [runningDeployment, scaledDownDeployment, degradedNoReadyDeployment],
      clusters: ['k3s-prod'],
      namespaces: ['default'],
      totalDeployments: 3,
      healthyDeployments: 1,
    })
    vi.mocked(workloadsApi.getCachedImageUpdates).mockResolvedValue({})
    vi.mocked(workloadsApi.getClusterEvents).mockResolvedValue([])
    vi.mocked(workloadsApi.getWorkloadPods).mockResolvedValue([
      {
        name: 'nginx-ingress-pod-1',
        namespace: 'default',
        phase: 'Running',
        restartCount: 0,
        isReady: true,
        containers: ['nginx'],
      },
    ])
    vi.mocked(workloadsApi.getAppBundle).mockResolvedValue({
      name: 'nginx-ingress',
      namespace: 'default',
      kind: 'Deployment',
      replicas: 2,
      image: 'nginx:1.27',
      ports: [],
      environmentVariables: [],
      volumeMounts: [],
    })
  })

  it('WorkloadCard renders Exec only when workload has running pods and user has RBAC permission', () => {
    const onOpenPods = vi.fn()

    const { rerender } = render(
      <QueryClientProvider client={queryClient}>
        <WorkloadCard
          workload={runningDeployment}
          onScale={vi.fn()}
          onOpenPods={onOpenPods}
          isOperator={true}
          canExecPods={true}
        />
      </QueryClientProvider>
    )

    const execBtn = screen.getByRole('button', { name: /^Exec$/i })
    fireEvent.click(execBtn)
    expect(onOpenPods).toHaveBeenCalledWith(runningDeployment, 'terminal')

    // Hidden when scaled down
    rerender(
      <QueryClientProvider client={queryClient}>
        <WorkloadCard
          workload={scaledDownDeployment}
          onScale={vi.fn()}
          onOpenPods={onOpenPods}
          isOperator={true}
          canExecPods={true}
        />
      </QueryClientProvider>
    )
    expect(screen.queryByRole('button', { name: /^Exec$/i })).not.toBeInTheDocument()

    // Hidden when RBAC denies pod exec (e.g., Viewer or POD_EXEC_REQUIRE_ADMIN for Operator)
    rerender(
      <QueryClientProvider client={queryClient}>
        <WorkloadCard
          workload={runningDeployment}
          onScale={vi.fn()}
          onOpenPods={onOpenPods}
          isOperator={true}
          canExecPods={false}
        />
      </QueryClientProvider>
    )
    expect(screen.queryByRole('button', { name: /^Exec$/i })).not.toBeInTheDocument()
  })

  it('WorkloadsPage renders Exec on Running workloads for Operators and opens PodDetailDrawer on Terminal tab', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <AuthContext.Provider value={createMockAuth()}>
          <WorkloadsPage />
        </AuthContext.Provider>
      </QueryClientProvider>
    )

    const runningRow = (await screen.findByText('nginx-ingress')).closest('tr') as HTMLElement
    const scaledRow = screen.getByText('idle-worker').closest('tr') as HTMLElement
    const degradedRow = screen.getByText('broken-api').closest('tr') as HTMLElement

    expect(within(runningRow).getByRole('button', { name: /^Exec$/i })).toBeInTheDocument()
    expect(within(scaledRow).queryByRole('button', { name: /^Exec$/i })).not.toBeInTheDocument()
    expect(within(degradedRow).queryByRole('button', { name: /^Exec$/i })).not.toBeInTheDocument()

    fireEvent.click(within(runningRow).getByRole('button', { name: /^Exec$/i }))

    const terminal = await screen.findByTestId('mock-pod-terminal')
    expect(terminal).toHaveTextContent('PodTerminal:default/nginx-ingress-pod-1')
    expect(screen.getByTestId('pod-detail-drawer').className).toContain('sm:w-[960px]')
  })

  it('WorkloadsPage hides Exec actions for Viewer users', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <AuthContext.Provider
          value={createMockAuth({
            isOperator: false,
            isAdmin: false,
            canExecPods: false,
            activeRole: 'Viewer',
            roles: ['Viewer'],
          })}
        >
          <WorkloadsPage />
        </AuthContext.Provider>
      </QueryClientProvider>
    )

    await screen.findByText('nginx-ingress')
    expect(screen.queryByRole('button', { name: /^Exec$/i })).not.toBeInTheDocument()
  })
})
