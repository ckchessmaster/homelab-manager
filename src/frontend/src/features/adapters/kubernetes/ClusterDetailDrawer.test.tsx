import { fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthContext } from '../../auth/AuthContext'
import type { AuthState } from '../../auth/AuthTypes'
import type { KubernetesClusterDto } from '../../../api/kubernetes'
import * as k8sApi from '../../../api/kubernetes'
import { ClusterDetailDrawer } from './ClusterDetailDrawer'

vi.mock('../../../api/kubernetes', async () => {
  const actual = await vi.importActual<typeof import('../../../api/kubernetes')>(
    '../../../api/kubernetes'
  )
  return {
    ...actual,
    fetchClusterNodes: vi.fn(),
    fetchClusterNamespaces: vi.fn(),
    fetchClusterDeployments: vi.fn(),
    fetchClusterPods: vi.fn(),
  }
})

vi.mock('../../../components/terminal/PodTerminal', () => ({
  PodTerminal: ({
    clusterId,
    clusterName,
    namespace,
    podName,
    containers,
  }: {
    clusterId?: string | null
    clusterName?: string | null
    namespace: string
    podName: string
    containers?: string[]
  }) => (
    <div
      data-testid="mock-pod-terminal"
      data-cluster-id={clusterId ?? ''}
      data-cluster-name={clusterName ?? ''}
      data-namespace={namespace}
      data-pod-name={podName}
      data-containers={(containers ?? []).join(',')}
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

const sampleCluster: KubernetesClusterDto = {
  id: 'cluster-homelab-1',
  name: 'Homelab Talos',
  apiServerUrl: 'https://10.0.10.50:6443',
  hasKubeConfig: true,
  hasToken: false,
  skipTlsVerify: false,
}

describe('ClusterDetailDrawer - Pod Exec Entry Point & RBAC', { timeout: 15_000 }, () => {
  let queryClient: QueryClient

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    vi.clearAllMocks()
    vi.mocked(k8sApi.fetchClusterNodes).mockResolvedValue([])
    vi.mocked(k8sApi.fetchClusterNamespaces).mockResolvedValue(['monitoring', 'jobs'])
    vi.mocked(k8sApi.fetchClusterDeployments).mockResolvedValue([])
    vi.mocked(k8sApi.fetchClusterPods).mockResolvedValue([
      {
        name: 'grafana-67b8d-12345',
        namespace: 'monitoring',
        phase: 'Running',
        nodeName: 'talos-w1',
        podIp: '10.244.1.16',
        restartCount: 0,
        isReady: true,
        containers: ['grafana', 'sidecar'],
      },
      {
        name: 'backup-job-pending',
        namespace: 'jobs',
        phase: 'Pending',
        nodeName: null,
        podIp: null,
        restartCount: 0,
        isReady: false,
        containers: ['backup'],
      },
    ])
  })

  it('renders Exec only on Running pods for authorized users and opens interactive PodTerminal', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <AuthContext.Provider value={createMockAuth()}>
          <ClusterDetailDrawer cluster={sampleCluster} open={true} onClose={vi.fn()} />
        </AuthContext.Provider>
      </QueryClientProvider>
    )

    const podsTabBtn = await screen.findByRole('button', { name: /Pods \(2\)/i }, { timeout: 5000 })
    fireEvent.click(podsTabBtn)

    const runningPodRow = (
      await screen.findByText('grafana-67b8d-12345', {}, { timeout: 5000 })
    ).closest('div.p-3') as HTMLElement
    const pendingPodRow = screen.getByText('backup-job-pending').closest('div.p-3') as HTMLElement

    const execBtn = within(runningPodRow).getByRole('button', { name: /^Exec$/i })
    expect(execBtn).toBeInTheDocument()
    expect(within(pendingPodRow).queryByRole('button', { name: /^Exec$/i })).not.toBeInTheDocument()

    fireEvent.click(execBtn)

    const dialog = await screen.findByTestId('cluster-pod-terminal-dialog', {}, { timeout: 5000 })
    expect(dialog.className).toContain('max-w-5xl')

    const terminal = screen.getByTestId('mock-pod-terminal')
    expect(terminal).toHaveAttribute('data-cluster-id', 'cluster-homelab-1')
    expect(terminal).toHaveAttribute('data-cluster-name', 'Homelab Talos')
    expect(terminal).toHaveAttribute('data-namespace', 'monitoring')
    expect(terminal).toHaveAttribute('data-pod-name', 'grafana-67b8d-12345')
    expect(terminal).toHaveAttribute('data-containers', 'grafana,sidecar')

    // Maximize and close
    fireEvent.click(screen.getByRole('button', { name: /Maximize terminal/i }))
    expect(dialog.className).toContain('w-[98vw]')

    fireEvent.click(screen.getByRole('button', { name: /Close pod terminal/i }))
    expect(screen.queryByTestId('cluster-pod-terminal-dialog')).not.toBeInTheDocument()
  })

  it('hides Exec actions on cluster pods for Viewer role', async () => {
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
          <ClusterDetailDrawer cluster={sampleCluster} open={true} onClose={vi.fn()} />
        </AuthContext.Provider>
      </QueryClientProvider>
    )

    const podsTabBtn = await screen.findByRole('button', { name: /Pods \(2\)/i }, { timeout: 5000 })
    fireEvent.click(podsTabBtn)

    await screen.findByText('grafana-67b8d-12345', {}, { timeout: 5000 })
    expect(screen.queryByRole('button', { name: /^Exec$/i })).not.toBeInTheDocument()
  })
})
