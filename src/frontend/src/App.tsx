import { useQuery } from '@tanstack/react-query'
import {
  Server,
  Activity,
  ShieldCheck,
  RefreshCw,
  Terminal,
  Cpu,
  HardDrive,
  CheckCircle2,
  AlertTriangle,
  Clock,
  Zap,
} from 'lucide-react'
import { useState } from 'react'

interface ApiHealthResponse {
  status: string
  service: string
  timestamp: string
}

interface NodeSummary {
  id: string
  hostname: string
  ip: string
  os: string
  role: string
  status: 'online' | 'reboot_pending' | 'updating' | 'failed'
  cpu: number
  ram: number
  agentVersion: string
}

const mockNodes: NodeSummary[] = [
  {
    id: 'node-01',
    hostname: 'pve-hypervisor-01',
    ip: '192.168.1.10',
    os: 'Proxmox VE 8.2 (Linux 6.8)',
    role: 'Hypervisor Host',
    status: 'online',
    cpu: 18,
    ram: 64,
    agentVersion: 'v0.1.0-alpha',
  },
  {
    id: 'node-02',
    hostname: 'k8s-control-01',
    ip: '192.168.1.21',
    os: 'Debian 12 Bookworm',
    role: 'K8s Control Plane',
    status: 'reboot_pending',
    cpu: 24,
    ram: 42,
    agentVersion: 'v0.1.0-alpha',
  },
  {
    id: 'node-03',
    hostname: 'k8s-worker-gpu-01',
    ip: '192.168.1.25',
    os: 'Ubuntu 24.04 LTS',
    role: 'K8s GPU Worker',
    status: 'updating',
    cpu: 78,
    ram: 82,
    agentVersion: 'v0.1.0-alpha',
  },
  {
    id: 'node-04',
    hostname: 'nas-storage-01',
    ip: '192.168.1.30',
    os: 'TrueNAS SCALE 24.04',
    role: 'Storage Appliance',
    status: 'online',
    cpu: 8,
    ram: 35,
    agentVersion: 'v0.1.0-alpha',
  },
]

export default function App() {
  const [selectedFilter, setSelectedFilter] = useState<'all' | 'online' | 'attention'>('all')

  const { data: apiStatus, isLoading, isError, refetch, isFetching } = useQuery<ApiHealthResponse>({
    queryKey: ['apiHealth'],
    queryFn: async () => {
      const res = await fetch('/')
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      return res.json()
    },
    refetchInterval: 15000,
  })

  const filteredNodes = mockNodes.filter((node) => {
    if (selectedFilter === 'online') return node.status === 'online'
    if (selectedFilter === 'attention') return node.status !== 'online'
    return true
  })

  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100 flex flex-col antialiased selection:bg-cyan-500/20 selection:text-cyan-200">
      {/* Top Navigation Bar */}
      <header className="sticky top-0 z-50 border-b border-zinc-800/80 bg-zinc-950/80 backdrop-blur-md px-6 py-3.5 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="flex items-center justify-center h-10 w-10 rounded-xl bg-gradient-to-br from-cyan-500 to-blue-600 text-white shadow-lg shadow-cyan-500/20">
            <Server className="h-5 w-5" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-lg font-bold tracking-tight text-white m-0">ControlPlane</h1>
              <span className="text-[11px] font-semibold uppercase tracking-wider px-2 py-0.5 rounded-full bg-zinc-800 text-zinc-300 border border-zinc-700/50">
                Milestone 1 Core
              </span>
            </div>
            <p className="text-xs text-zinc-400 m-0">Homelab Orchestration & Management Plane</p>
          </div>
        </div>

        <div className="flex items-center gap-3">
          {/* Backend Status Badge */}
          <div className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-zinc-900/90 border border-zinc-800 text-xs">
            <span
              className={`h-2 w-2 rounded-full ${
                isLoading
                  ? 'bg-amber-400 animate-pulse'
                  : isError
                  ? 'bg-rose-500'
                  : 'bg-emerald-400 shadow-sm shadow-emerald-400/50'
              }`}
            />
            <span className="text-zinc-400">Backend API:</span>
            <span className="font-mono text-zinc-200">
              {isLoading ? 'Connecting...' : isError ? 'Offline (Mock Mode)' : `${apiStatus?.status} (${apiStatus?.service})`}
            </span>
          </div>

          <button
            onClick={() => refetch()}
            disabled={isFetching}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-zinc-900 hover:bg-zinc-800 border border-zinc-800 text-xs font-medium text-zinc-200 transition disabled:opacity-50"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} />
            Sync
          </button>
        </div>
      </header>

      {/* Main Operational Canvas */}
      <main className="flex-1 max-w-7xl w-full mx-auto p-6 space-y-6">
        {/* Welcome & System State Banner */}
        <section className="relative overflow-hidden rounded-2xl border border-zinc-800/80 bg-gradient-to-b from-zinc-900/60 to-zinc-950/80 p-6 backdrop-blur-sm shadow-xl">
          <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-4">
            <div className="space-y-1">
              <div className="flex items-center gap-2">
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-md text-xs font-medium bg-emerald-950/80 text-emerald-300 border border-emerald-800/60">
                  <CheckCircle2 className="h-3.5 w-3.5" />
                  Aspire 13.5 + .NET 10 Initialized
                </span>
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-md text-xs font-medium bg-cyan-950/80 text-cyan-300 border border-cyan-800/60">
                  <Zap className="h-3.5 w-3.5" />
                  React 19 + Tailwind v4 Active
                </span>
              </div>
              <h2 className="text-2xl font-semibold tracking-tight text-white m-0">Cluster Operational Overview</h2>
              <p className="text-sm text-zinc-400 m-0">
                Hybrid agent / agentless topology with dual PostgreSQL & SQLite standby resilience.
              </p>
            </div>

            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
              <div className="rounded-xl border border-zinc-800/80 bg-zinc-900/40 p-3">
                <div className="text-xs text-zinc-400 flex items-center gap-1.5">
                  <Activity className="h-3.5 w-3.5 text-cyan-400" />
                  Online Hosts
                </div>
                <div className="text-xl font-bold text-white mt-1">2 / 4</div>
              </div>
              <div className="rounded-xl border border-zinc-800/80 bg-zinc-900/40 p-3">
                <div className="text-xs text-zinc-400 flex items-center gap-1.5">
                  <Clock className="h-3.5 w-3.5 text-amber-400" />
                  Pending Reboot
                </div>
                <div className="text-xl font-bold text-amber-400 mt-1">1</div>
              </div>
              <div className="rounded-xl border border-zinc-800/80 bg-zinc-900/40 p-3">
                <div className="text-xs text-zinc-400 flex items-center gap-1.5">
                  <RefreshCw className="h-3.5 w-3.5 text-cyan-400" />
                  Active DAGs
                </div>
                <div className="text-xl font-bold text-cyan-400 mt-1">1</div>
              </div>
              <div className="rounded-xl border border-zinc-800/80 bg-zinc-900/40 p-3">
                <div className="text-xs text-zinc-400 flex items-center gap-1.5">
                  <ShieldCheck className="h-3.5 w-3.5 text-emerald-400" />
                  Standby Runner
                </div>
                <div className="text-xl font-bold text-emerald-400 mt-1">Standby Idle</div>
              </div>
            </div>
          </div>
        </section>

        {/* Node Inventory Section */}
        <section className="space-y-4">
          <div className="flex items-center justify-between">
            <div>
              <h3 className="text-base font-semibold text-white m-0">Managed Infrastructure Nodes</h3>
              <p className="text-xs text-zinc-400 m-0">Outbound WebSocket agent daemons & agentless hypervisor targets</p>
            </div>
            <div className="flex items-center gap-2">
              <div className="flex rounded-lg bg-zinc-900 border border-zinc-800 p-0.5 text-xs">
                <button
                  onClick={() => setSelectedFilter('all')}
                  className={`px-3 py-1 rounded-md font-medium transition ${
                    selectedFilter === 'all' ? 'bg-zinc-800 text-white shadow-sm' : 'text-zinc-400 hover:text-zinc-200'
                  }`}
                >
                  All (4)
                </button>
                <button
                  onClick={() => setSelectedFilter('online')}
                  className={`px-3 py-1 rounded-md font-medium transition ${
                    selectedFilter === 'online' ? 'bg-zinc-800 text-white shadow-sm' : 'text-zinc-400 hover:text-zinc-200'
                  }`}
                >
                  Online (2)
                </button>
                <button
                  onClick={() => setSelectedFilter('attention')}
                  className={`px-3 py-1 rounded-md font-medium transition ${
                    selectedFilter === 'attention' ? 'bg-zinc-800 text-white shadow-sm' : 'text-zinc-400 hover:text-zinc-200'
                  }`}
                >
                  Action Required (2)
                </button>
              </div>
            </div>
          </div>

          {/* Node Grid */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {filteredNodes.map((node) => (
              <div
                key={node.id}
                className="group relative rounded-xl border border-zinc-800/80 bg-zinc-900/40 hover:bg-zinc-900/70 p-4 transition duration-200 backdrop-blur-sm hover:border-zinc-700/80"
              >
                <div className="flex items-start justify-between">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <span className="font-semibold text-white tracking-tight">{node.hostname}</span>
                      <span className="text-xs font-mono text-zinc-400">{node.ip}</span>
                    </div>
                    <div className="text-xs text-zinc-400">
                      {node.role} • <span className="text-zinc-500">{node.os}</span>
                    </div>
                  </div>

                  {/* Status Badges conforming to frontend-react.md rules */}
                  {node.status === 'online' && (
                    <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium bg-emerald-950/80 text-emerald-300 border border-emerald-800/60">
                      <span className="h-1.5 w-1.5 rounded-full bg-emerald-400"></span>
                      Online
                    </span>
                  )}
                  {node.status === 'reboot_pending' && (
                    <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium bg-amber-950/80 text-amber-300 border border-amber-800/60">
                      <AlertTriangle className="h-3 w-3" />
                      Reboot Pending
                    </span>
                  )}
                  {node.status === 'updating' && (
                    <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium bg-cyan-950/80 text-cyan-300 border border-cyan-800/60 animate-pulse">
                      <RefreshCw className="h-3 w-3 animate-spin" />
                      Updating / In-Flight
                    </span>
                  )}
                  {node.status === 'failed' && (
                    <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium bg-rose-950/80 text-rose-300 border border-rose-800/60">
                      Critical / Failed
                    </span>
                  )}
                </div>

                {/* Resource utilization bars */}
                <div className="mt-4 pt-3 border-t border-zinc-800/60 grid grid-cols-2 gap-4 text-xs">
                  <div>
                    <div className="flex justify-between text-zinc-400 mb-1">
                      <span className="flex items-center gap-1">
                        <Cpu className="h-3 w-3" /> CPU
                      </span>
                      <span className="font-mono">{node.cpu}%</span>
                    </div>
                    <div className="h-1.5 w-full bg-zinc-800 rounded-full overflow-hidden">
                      <div
                        className={`h-full rounded-full ${
                          node.cpu > 70 ? 'bg-amber-500' : 'bg-cyan-500'
                        }`}
                        style={{ width: `${node.cpu}%` }}
                      />
                    </div>
                  </div>

                  <div>
                    <div className="flex justify-between text-zinc-400 mb-1">
                      <span className="flex items-center gap-1">
                        <HardDrive className="h-3 w-3" /> RAM
                      </span>
                      <span className="font-mono">{node.ram}%</span>
                    </div>
                    <div className="h-1.5 w-full bg-zinc-800 rounded-full overflow-hidden">
                      <div
                        className={`h-full rounded-full ${
                          node.ram > 80 ? 'bg-rose-500' : 'bg-blue-500'
                        }`}
                        style={{ width: `${node.ram}%` }}
                      />
                    </div>
                  </div>
                </div>

                {/* Footer metadata */}
                <div className="mt-3 flex items-center justify-between text-[11px] text-zinc-500 font-mono">
                  <span>Agent: {node.agentVersion}</span>
                  <span className="text-zinc-600">ID: {node.id}</span>
                </div>
              </div>
            ))}
          </div>
        </section>

        {/* Real-time Diagnostics Terminal Strip */}
        <section className="rounded-xl border border-zinc-800/80 bg-zinc-950 p-4 space-y-2">
          <div className="flex items-center justify-between text-xs text-zinc-400">
            <span className="flex items-center gap-1.5 font-medium text-zinc-300">
              <Terminal className="h-4 w-4 text-cyan-400" />
              Aspire Orchestration Bus & Diagnostics
            </span>
            <span className="font-mono text-[11px] text-zinc-500">SignalR Streaming Target: /hubs/jobs</span>
          </div>
          <div className="font-mono text-xs p-3 rounded-lg bg-zinc-900/80 border border-zinc-800 text-zinc-300 space-y-1 overflow-x-auto">
            <div className="text-zinc-500"># System diagnostic stream ready</div>
            <div className="text-emerald-400">✓ ControlPlane.AppHost (Aspire 13.5.3) initialized</div>
            <div className="text-emerald-400">✓ ControlPlane.Api (net10.0 Web API) listening with Aspire ServiceDefaults</div>
            <div className="text-cyan-400">→ Frontend SPA (React 19 + Vite) connected via proxy</div>
            {apiStatus && (
              <div className="text-zinc-300">
                [API Health Probe] Status: <span className="text-emerald-300">{apiStatus.status}</span>, Server Time: {apiStatus.timestamp}
              </div>
            )}
          </div>
        </section>
      </main>

      {/* Footer */}
      <footer className="border-t border-zinc-800/60 py-4 px-6 text-center text-xs text-zinc-500">
        ControlPlane • Homelab Autonomous Standby & Orchestration Platform • Phase 1
      </footer>
    </div>
  )
}
