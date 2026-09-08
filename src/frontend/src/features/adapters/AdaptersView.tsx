import { useState } from 'react'
import { ProxmoxAdaptersView } from './proxmox/ProxmoxAdaptersView'
import { KubernetesAdaptersView } from './kubernetes/KubernetesAdaptersView'
import { UniFiAdaptersView } from './unifi/UniFiAdaptersView'
import { Badge } from '../../components/ui/badge'
import {
  Server,
  Cpu,
  Shield,
  Layers,
  Wifi,
} from 'lucide-react'
import { useProxmoxInstances } from './useAdapters'
import { useKubernetesClusters } from './kubernetes/useKubernetes'
import { useUniFiInstances } from './unifi/useUniFi'

export type AdapterTab = 'proxmox' | 'kubernetes' | 'unifi' | 'opnsense' | 'idrac'

export function AdaptersView() {
  const [activeTab, setActiveTab] = useState<AdapterTab>('proxmox')
  const { data: proxmoxInstances } = useProxmoxInstances()
  const { data: k8sClusters } = useKubernetesClusters()
  const { data: unifiInstances } = useUniFiInstances()

  const tabs: {
    id: AdapterTab
    label: string
    icon: typeof Server
    badge?: string
    badgeVariant?: 'default' | 'success' | 'warning' | 'purple'
  }[] = [
    {
      id: 'proxmox',
      label: 'Proxmox VE',
      icon: Server,
      badge: proxmoxInstances && proxmoxInstances.length > 0 ? `${proxmoxInstances.length}` : 'Active',
      badgeVariant: 'purple',
    },
    {
      id: 'kubernetes',
      label: 'Kubernetes',
      icon: Layers,
      badge: k8sClusters && k8sClusters.length > 0 ? `${k8sClusters.length}` : 'Active',
      badgeVariant: 'purple',
    },
    {
      id: 'unifi',
      label: 'Ubiquiti UniFi',
      icon: Wifi,
      badge: unifiInstances && unifiInstances.length > 0 ? `${unifiInstances.length}` : 'Active',
      badgeVariant: 'purple',
    },
    {
      id: 'opnsense',
      label: 'OPNsense',
      icon: Shield,
      badge: 'Plan 05',
      badgeVariant: 'default',
    },
    {
      id: 'idrac',
      label: 'BMC / iDRAC',
      icon: Cpu,
      badge: 'Plan 05',
      badgeVariant: 'default',
    },
  ]

  return (
    <div className="space-y-6 max-w-[1700px] mx-auto">
      {/* Adapters Navigation Bar */}
      <div className="flex items-center gap-2 border-b border-zinc-800 pb-3 overflow-x-auto">
        {tabs.map((tab) => {
          const Icon = tab.icon
          const isActive = activeTab === tab.id

          return (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`flex items-center gap-2.5 px-4 py-2.5 rounded-xl text-xs font-medium transition-all shrink-0 ${
                isActive
                  ? 'bg-zinc-800 text-zinc-100 shadow-md border border-zinc-700/60'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900/60 border border-transparent'
              }`}
            >
              <Icon className={`h-4 w-4 ${isActive ? 'text-emerald-400' : 'text-zinc-400'}`} />
              <span>{tab.label}</span>
              {tab.badge && (
                <Badge
                  variant={isActive ? 'success' : tab.badgeVariant || 'default'}
                  className="text-[10px] px-1.5 py-0"
                >
                  {tab.badge}
                </Badge>
              )}
            </button>
          )
        })}
      </div>

      {/* Tab Panels */}
      {activeTab === 'proxmox' && <ProxmoxAdaptersView />}
      {activeTab === 'kubernetes' && <KubernetesAdaptersView />}
      {activeTab === 'unifi' && <UniFiAdaptersView />}

      {activeTab === 'opnsense' && (
        <div className="p-8 bg-zinc-900/40 border border-zinc-800 rounded-xl space-y-4 max-w-2xl mx-auto text-center animate-in fade-in">
          <div className="p-3 bg-orange-500/10 border border-orange-500/20 rounded-full w-12 h-12 flex items-center justify-center mx-auto text-orange-400">
            <Shield className="h-6 w-6" />
          </div>
          <div>
            <div className="flex items-center justify-center gap-2">
              <h3 className="text-base font-semibold text-zinc-100">OPNsense Firewall & Gateway Adapter</h3>
              <Badge variant="warning">Coming in Plan 05</Badge>
            </div>
            <p className="text-xs text-zinc-400 mt-2 leading-relaxed">
              Query gateway health, WAN ping latency, DHCP leases for automated node discovery, service restarts (Unbound DNS, WireGuard), and system firmware updates.
            </p>
          </div>
        </div>
      )}

      {activeTab === 'idrac' && (
        <div className="p-8 bg-zinc-900/40 border border-zinc-800 rounded-xl space-y-4 max-w-2xl mx-auto text-center animate-in fade-in">
          <div className="p-3 bg-purple-500/10 border border-purple-500/20 rounded-full w-12 h-12 flex items-center justify-center mx-auto text-purple-400">
            <Cpu className="h-6 w-6" />
          </div>
          <div>
            <div className="flex items-center justify-center gap-2">
              <h3 className="text-base font-semibold text-zinc-100">Out-of-Band BMC (Dell iDRAC / Redfish)</h3>
              <Badge variant="purple">Planned</Badge>
            </div>
            <p className="text-xs text-zinc-400 mt-2 leading-relaxed">
              Out-of-band hardware management for baremetal servers: chassis power state control (Power On/Off/Force Reset), PSU health, and intake temperature sensor telemetry.
            </p>
          </div>
        </div>
      )}
    </div>
  )
}
