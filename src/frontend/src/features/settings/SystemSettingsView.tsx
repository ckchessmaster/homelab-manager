import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  Terminal,
  ShieldCheck,
  Cpu,
  Database,
  Activity,
  Server,
  Clock,
  Layers,
  HardDrive,
} from 'lucide-react'
import { SystemLogsView } from '../system/SystemLogsView'
import { PersonalAccessTokensCard } from '../auth/PersonalAccessTokensCard'
import { AgentBinariesCard } from './AgentBinariesCard'
import { useAuthUser } from '../auth/useAuthUser'
import { getApiKey } from '../../api/client'
import { fetchSystemInfo, type SystemInfoDto } from '../../api/system'
import { Badge } from '../../components/ui/badge'

type SettingsSubTab = 'logs' | 'security' | 'agents' | 'storage'

export function SystemSettingsView() {
  const [activeSubTab, setActiveSubTab] = useState<SettingsSubTab>('logs')
  const { authMode } = useAuthUser()

  const { data: sysInfo } = useQuery<SystemInfoDto>({
    queryKey: ['system-info'],
    queryFn: fetchSystemInfo,
    refetchInterval: 30000,
  })

  const formatBytes = (bytes?: number) => {
    if (!bytes) return '0 MB'
    const mb = bytes / (1024 * 1024)
    return `${mb.toFixed(1)} MB`
  }

  const formatUptime = (uptimeStr?: string) => {
    if (!uptimeStr) return 'Active'
    // uptime format can be dd.hh:mm:ss or hh:mm:ss
    return uptimeStr.split('.')[0] || uptimeStr
  }

  return (
    <div className="space-y-6 w-full max-w-[1700px] mx-auto">
      {/* Sub-navigation Segmented Bar */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-zinc-800 pb-4">
        <div>
          <h2 className="text-lg font-bold text-zinc-100 tracking-tight flex items-center gap-2">
            <Layers className="h-5 w-5 text-sky-400" />
            System & Settings
          </h2>
          <p className="text-xs text-zinc-400 mt-0.5">
            Diagnostics, live backend logs, authentication credentials, and platform infrastructure.
          </p>
        </div>

        {/* Tab Pills */}
        <div className="flex items-center gap-1.5 p-1 bg-zinc-900/80 border border-zinc-800 rounded-xl">
          <button
            type="button"
            onClick={() => setActiveSubTab('logs')}
            className={`px-3 py-1.5 rounded-lg text-xs font-medium flex items-center gap-2 transition-all cursor-pointer ${
              activeSubTab === 'logs'
                ? 'bg-sky-500 text-zinc-950 font-semibold shadow-xs'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60'
            }`}
          >
            <Terminal className="w-3.5 h-3.5" />
            <span>Backend Logs</span>
          </button>

          <button
            type="button"
            onClick={() => setActiveSubTab('security')}
            className={`px-3 py-1.5 rounded-lg text-xs font-medium flex items-center gap-2 transition-all cursor-pointer ${
              activeSubTab === 'security'
                ? 'bg-sky-500 text-zinc-950 font-semibold shadow-xs'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60'
            }`}
          >
            <ShieldCheck className="w-3.5 h-3.5" />
            <span>Security & Tokens</span>
          </button>

          <button
            type="button"
            onClick={() => setActiveSubTab('agents')}
            className={`px-3 py-1.5 rounded-lg text-xs font-medium flex items-center gap-2 transition-all cursor-pointer ${
              activeSubTab === 'agents'
                ? 'bg-sky-500 text-zinc-950 font-semibold shadow-xs'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60'
            }`}
          >
            <Cpu className="w-3.5 h-3.5" />
            <span>Agent Binaries</span>
          </button>

          <button
            type="button"
            onClick={() => setActiveSubTab('storage')}
            className={`px-3 py-1.5 rounded-lg text-xs font-medium flex items-center gap-2 transition-all cursor-pointer ${
              activeSubTab === 'storage'
                ? 'bg-sky-500 text-zinc-950 font-semibold shadow-xs'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60'
            }`}
          >
            <Database className="w-3.5 h-3.5" />
            <span>Storage & Runtime</span>
          </button>
        </div>
      </div>

      {/* Tab 1: Backend Logs */}
      {activeSubTab === 'logs' && <SystemLogsView />}

      {/* Tab 2: Security & Tokens */}
      {activeSubTab === 'security' && (
        <div className="max-w-4xl mx-auto space-y-6">
          <div className="p-6 bg-zinc-900/60 border border-zinc-800 rounded-xl space-y-4">
            <h3 className="text-base font-semibold text-zinc-100 flex items-center gap-2">
              <ShieldCheck className="h-5 w-5 text-emerald-400" />
              Authentication & Security Credentials
            </h3>
            <p className="text-xs text-zinc-400">
              {authMode === 'api_key'
                ? 'ControlPlane is running in API Key mode with administrative access.'
                : 'ControlPlane is running in OIDC (Single Sign-On) mode with role-based access control.'}
            </p>

            {authMode === 'api_key' && (
              <div className="p-3.5 bg-zinc-950/80 border border-zinc-800 rounded-lg space-y-1 font-mono text-xs">
                <div className="text-zinc-500">// Active Header Format</div>
                <div className="text-emerald-400">X-ControlPlane-Key: {getApiKey()}</div>
              </div>
            )}
          </div>

          <PersonalAccessTokensCard />
        </div>
      )}

      {/* Tab 3: Agent Binaries */}
      {activeSubTab === 'agents' && (
        <div className="max-w-4xl mx-auto">
          <AgentBinariesCard />
        </div>
      )}

      {/* Tab 4: Storage & Runtime Architecture */}
      {activeSubTab === 'storage' && (
        <div className="max-w-4xl mx-auto space-y-6">
          {/* Runtime Process Diagnostics */}
          <div className="p-6 bg-zinc-900/60 border border-zinc-800 rounded-xl space-y-4">
            <div className="flex items-center justify-between">
              <h3 className="text-base font-semibold text-zinc-100 flex items-center gap-2">
                <Activity className="h-5 w-5 text-sky-400" />
                Backend Process Diagnostics
              </h3>
              <Badge variant="success">Online</Badge>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 pt-2">
              <div className="p-3 bg-zinc-950/60 border border-zinc-800 rounded-lg space-y-1">
                <span className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider block">
                  Host Machine
                </span>
                <span className="text-xs text-zinc-200 font-mono font-medium flex items-center gap-1.5">
                  <Server className="h-3.5 w-3.5 text-zinc-400" />
                  {sysInfo?.machineName || 'localhost'}
                </span>
              </div>

              <div className="p-3 bg-zinc-950/60 border border-zinc-800 rounded-lg space-y-1">
                <span className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider block">
                  Process Memory
                </span>
                <span className="text-xs text-sky-400 font-mono font-semibold flex items-center gap-1.5">
                  <HardDrive className="h-3.5 w-3.5" />
                  {formatBytes(sysInfo?.workingSetBytes)}
                </span>
              </div>

              <div className="p-3 bg-zinc-950/60 border border-zinc-800 rounded-lg space-y-1">
                <span className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider block">
                  Core Runtime
                </span>
                <span className="text-xs text-zinc-200 font-mono">
                  {sysInfo?.frameworkDescription || '.NET 10.0'}
                </span>
              </div>

              <div className="p-3 bg-zinc-950/60 border border-zinc-800 rounded-lg space-y-1">
                <span className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider block">
                  Process Uptime
                </span>
                <span className="text-xs text-emerald-400 font-mono flex items-center gap-1.5">
                  <Clock className="h-3.5 w-3.5" />
                  {formatUptime(sysInfo?.uptime)}
                </span>
              </div>
            </div>

            <div className="p-3 bg-zinc-950/40 border border-zinc-800 rounded-lg font-mono text-[11px] text-zinc-400">
              <span className="text-zinc-500">Operating System: </span>
              {sysInfo?.osDescription || 'Linux (Container)'}
            </div>
          </div>

          {/* Dual Topology Information */}
          <div className="p-6 bg-zinc-900/60 border border-zinc-800 rounded-xl space-y-4">
            <h3 className="text-base font-semibold text-zinc-100 flex items-center gap-2">
              <Database className="h-5 w-5 text-sky-400" />
              Dual-Topology Storage Architecture
            </h3>
            <p className="text-xs text-zinc-400 leading-relaxed">
              When operating inside Kubernetes, ControlPlane runs against PostgreSQL. During node reboot or maintenance windows, the autonomous <strong>Standby Runner</strong> takes over locally using SQLite and lease lock coordination (<code>GLOBAL_MAINTENANCE_LOCK</code>).
            </p>

            <div className="p-4 bg-zinc-950/80 border border-zinc-800 rounded-lg space-y-2 text-xs">
              <div className="flex items-center justify-between text-zinc-300">
                <span className="font-medium">Active Database Provider:</span>
                <span className="font-mono text-sky-400">PostgreSQL / SQLite (Dual-Provider)</span>
              </div>
              <div className="flex items-center justify-between text-zinc-300">
                <span className="font-medium">Standby Lease Coordination:</span>
                <span className="font-mono text-emerald-400">GLOBAL_MAINTENANCE_LOCK</span>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
