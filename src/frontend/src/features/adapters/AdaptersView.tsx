import { useState } from 'react'
import { ProxmoxAdaptersView } from './proxmox/ProxmoxAdaptersView'
import { KubernetesAdaptersView } from './kubernetes/KubernetesAdaptersView'
import { UniFiAdaptersView } from './unifi/UniFiAdaptersView'
import { OPNsenseAdaptersView } from './opnsense/OPNsenseAdaptersView'
import { IdracAdaptersView } from './idrac/IdracAdaptersView'
import { HomeAssistantAdaptersView } from './homeAssistant/HomeAssistantAdaptersView'
import { Badge } from '../../components/ui/badge'
import {
  Server,
  Cpu,
  Shield,
  Layers,
  Wifi,
  Home,
} from 'lucide-react'
import { useProxmoxInstances } from './useAdapters'
import { useKubernetesClusters } from './kubernetes/useKubernetes'
import { useUniFiInstances } from './unifi/useUniFi'
import { useOPNsenseInstances } from './opnsense/useOPNsense'
import { useIdracInstances } from './idrac/useIdrac'
import { useHomeAssistantInstances } from './homeAssistant/useHomeAssistant'

export type AdapterTab = 'proxmox' | 'kubernetes' | 'unifi' | 'opnsense' | 'idrac' | 'homeassistant'

export function AdaptersView() {
  const [activeTab, setActiveTab] = useState<AdapterTab>('proxmox')
  const { data: proxmoxInstances } = useProxmoxInstances()
  const { data: k8sClusters } = useKubernetesClusters()
  const { data: unifiInstances } = useUniFiInstances()
  const { data: opnsenseInstances } = useOPNsenseInstances()
  const { data: idracInstances } = useIdracInstances()
  const { data: haInstances } = useHomeAssistantInstances()

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
      badge: opnsenseInstances && opnsenseInstances.length > 0 ? `${opnsenseInstances.length}` : undefined,
      badgeVariant: 'purple',
    },
    {
      id: 'idrac',
      label: 'BMC / iDRAC',
      icon: Cpu,
      badge: idracInstances && idracInstances.length > 0 ? `${idracInstances.length}` : undefined,
      badgeVariant: 'purple',
    },
    {
      id: 'homeassistant',
      label: 'Home Assistant',
      icon: Home,
      badge: haInstances && haInstances.length > 0 ? `${haInstances.length}` : undefined,
      badgeVariant: 'purple',
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
      {activeTab === 'opnsense' && <OPNsenseAdaptersView />}
      {activeTab === 'idrac' && <IdracAdaptersView />}
      {activeTab === 'homeassistant' && <HomeAssistantAdaptersView />}
    </div>
  )
}
