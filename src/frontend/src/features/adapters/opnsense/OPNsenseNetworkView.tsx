import { useState } from 'react'
import {
  Radio,
  Search,
  Copy,
  Check,
  UserPlus,
  RefreshCw,
  Layers,
} from 'lucide-react'
import { Input } from '../../../components/ui/input'
import { Badge } from '../../../components/ui/badge'
import { Button } from '../../../components/ui/button'
import { useOPNsenseDhcpLeases, useOPNsenseArp } from './useOPNsense'
import { AdoptNodeModal } from '../../hosts/AdoptNodeModal'
import type { OPNsenseDhcpLease, OPNsenseArpEntry } from '../../../api/opnsense'
import type { Host } from '../../../api/hosts'

interface OPNsenseNetworkViewProps {
  instanceId: string
}

export function OPNsenseNetworkView({ instanceId }: OPNsenseNetworkViewProps) {
  const { data: leases, isLoading: isLeasesLoading, refetch: refetchLeases } = useOPNsenseDhcpLeases(instanceId)
  const { data: arpEntries, isLoading: isArpLoading, refetch: refetchArp } = useOPNsenseArp(instanceId)

  const [networkSubTab, setNetworkSubTab] = useState<'dhcp' | 'arp'>('dhcp')
  const [search, setSearch] = useState('')
  const [copiedMac, setCopiedMac] = useState<string | null>(null)
  const [adoptTarget, setAdoptTarget] = useState<Host | null>(null)
  const [adoptModalOpen, setAdoptModalOpen] = useState(false)

  const handleCopy = (text: string) => {
    navigator.clipboard.writeText(text)
    setCopiedMac(text)
    setTimeout(() => setCopiedMac(null), 2000)
  }

  const handleAdoptLease = (lease: OPNsenseDhcpLease) => {
    const pseudoHost: Host = {
      id: `lease-${lease.ip.replace(/\./g, '-')}`,
      hostname: lease.hostname || `dhcp-${lease.ip.replace(/\./g, '-')}`,
      friendlyName: lease.hostname || `DHCP Host (${lease.ip})`,
      ipAddress: lease.ip,
      osFamily: 'linux_debian',
      targetType: 'baremetal',
      agent: {
        installed: false,
        version: null,
        lastSeenAt: null,
        pendingReboot: false,
        upgradablePackagesCount: 0,
      },
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }
    setAdoptTarget(pseudoHost)
    setAdoptModalOpen(true)
  }

  const handleAdoptArp = (entry: OPNsenseArpEntry) => {
    const pseudoHost: Host = {
      id: `arp-${entry.ip.replace(/\./g, '-')}`,
      hostname: entry.hostname || `host-${entry.ip.replace(/\./g, '-')}`,
      friendlyName: entry.hostname || `${entry.manufacturer || 'Network Host'} (${entry.ip})`,
      ipAddress: entry.ip,
      osFamily: 'linux_debian',
      targetType: 'baremetal',
      agent: {
        installed: false,
        version: null,
        lastSeenAt: null,
        pendingReboot: false,
        upgradablePackagesCount: 0,
      },
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }
    setAdoptTarget(pseudoHost)
    setAdoptModalOpen(true)
  }

  const filteredLeases = (leases || []).filter((l) => {
    const q = search.toLowerCase()
    return (
      l.ip.toLowerCase().includes(q) ||
      l.mac.toLowerCase().includes(q) ||
      (l.hostname && l.hostname.toLowerCase().includes(q))
    )
  })

  const filteredArp = (arpEntries || []).filter((a) => {
    const q = search.toLowerCase()
    return (
      a.ip.toLowerCase().includes(q) ||
      a.mac.toLowerCase().includes(q) ||
      (a.hostname && a.hostname.toLowerCase().includes(q)) ||
      (a.manufacturer && a.manufacturer.toLowerCase().includes(q)) ||
      a.interface.toLowerCase().includes(q)
    )
  })

  return (
    <div className="space-y-4 animate-in fade-in">
      {/* Sub-selector and search bar */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div className="flex items-center gap-1.5 bg-zinc-900/80 p-1 rounded-lg border border-zinc-800">
          <button
            onClick={() => setNetworkSubTab('dhcp')}
            className={`px-3 py-1 text-xs font-medium rounded-md transition-colors flex items-center gap-1.5 ${
              networkSubTab === 'dhcp'
                ? 'bg-zinc-800 text-zinc-100 shadow-xs'
                : 'text-zinc-400 hover:text-zinc-200'
            }`}
          >
            <Radio className="h-3.5 w-3.5 text-orange-400" />
            <span>DHCP Leases</span>
            {leases && (
              <Badge variant="purple" className="text-[10px] py-0 px-1">
                {leases.length}
              </Badge>
            )}
          </button>

          <button
            onClick={() => setNetworkSubTab('arp')}
            className={`px-3 py-1 text-xs font-medium rounded-md transition-colors flex items-center gap-1.5 ${
              networkSubTab === 'arp'
                ? 'bg-zinc-800 text-zinc-100 shadow-xs'
                : 'text-zinc-400 hover:text-zinc-200'
            }`}
          >
            <Layers className="h-3.5 w-3.5 text-sky-400" />
            <span>ARP & Neighbors</span>
            {arpEntries && (
              <Badge variant="default" className="text-[10px] py-0 px-1">
                {arpEntries.length}
              </Badge>
            )}
          </button>
        </div>

        <div className="flex items-center gap-2">
          <div className="relative flex-1 sm:w-64">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-500" />
            <Input
              placeholder={
                networkSubTab === 'dhcp'
                  ? 'Search DHCP leases (IP, MAC, host)...'
                  : 'Search ARP table (IP, MAC, vendor)...'
              }
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="pl-8 text-xs h-8 bg-zinc-950/60"
            />
          </div>

          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              if (networkSubTab === 'dhcp') refetchLeases()
              else refetchArp()
            }}
            className="h-8 text-zinc-400 hover:text-zinc-200"
            title="Refresh network table"
          >
            <RefreshCw className="h-3.5 w-3.5" />
          </Button>
        </div>
      </div>

      {/* Tab 1: Live DHCP Leases */}
      {networkSubTab === 'dhcp' && (
        <div className="overflow-x-auto rounded-xl border border-zinc-800 bg-zinc-950/50">
          <table className="w-full text-left text-xs text-zinc-300">
            <thead className="bg-zinc-900/80 text-zinc-400 font-medium uppercase text-[10px] tracking-wider border-b border-zinc-800">
              <tr>
                <th className="px-4 py-2.5">IP Address</th>
                <th className="px-4 py-2.5">Hostname</th>
                <th className="px-4 py-2.5">MAC Address</th>
                <th className="px-4 py-2.5">Status</th>
                <th className="px-4 py-2.5 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-800/60 font-mono">
              {filteredLeases.length > 0 ? (
                filteredLeases.map((lease) => (
                  <tr key={lease.ip + lease.mac} className="hover:bg-zinc-900/40">
                    <td className="px-4 py-2.5 font-bold text-zinc-100 font-mono">
                      {lease.ip}
                    </td>
                    <td className="px-4 py-2.5 font-sans font-medium text-zinc-300">
                      {lease.hostname || (
                        <span className="text-zinc-500 italic font-mono text-[11px]">unnamed</span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-zinc-400">
                      <div className="flex items-center gap-1.5">
                        <span>{lease.mac}</span>
                        <button
                          onClick={() => handleCopy(lease.mac)}
                          className="p-1 rounded text-zinc-500 hover:text-zinc-300 transition-colors"
                          title="Copy MAC"
                        >
                          {copiedMac === lease.mac ? (
                            <Check className="h-3 w-3 text-emerald-400" />
                          ) : (
                            <Copy className="h-3 w-3" />
                          )}
                        </button>
                      </div>
                    </td>
                    <td className="px-4 py-2.5">
                      <Badge variant="success" className="text-[10px] py-0">
                        {lease.status}
                      </Badge>
                    </td>
                    <td className="px-4 py-2.5 text-right">
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => handleAdoptLease(lease)}
                        className="h-7 px-2 text-[11px] font-sans gap-1 text-sky-400 border-sky-600/30 hover:bg-sky-950/30 hover:text-sky-300"
                      >
                        <UserPlus className="h-3 w-3" />
                        <span>Adopt Host</span>
                      </Button>
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={5} className="px-4 py-8 text-center text-zinc-500 italic font-sans">
                    {isLeasesLoading ? 'Querying OPNsense DHCP daemon...' : 'No DHCP leases found.'}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* Tab 2: Live ARP & Neighbor Table */}
      {networkSubTab === 'arp' && (
        <div className="overflow-x-auto rounded-xl border border-zinc-800 bg-zinc-950/50">
          <table className="w-full text-left text-xs text-zinc-300">
            <thead className="bg-zinc-900/80 text-zinc-400 font-medium uppercase text-[10px] tracking-wider border-b border-zinc-800">
              <tr>
                <th className="px-4 py-2.5">IP Address</th>
                <th className="px-4 py-2.5">Hostname</th>
                <th className="px-4 py-2.5">MAC Address</th>
                <th className="px-4 py-2.5">Interface</th>
                <th className="px-4 py-2.5">Manufacturer</th>
                <th className="px-4 py-2.5 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-800/60 font-mono">
              {filteredArp.length > 0 ? (
                filteredArp.map((entry) => (
                  <tr key={entry.ip + entry.mac} className="hover:bg-zinc-900/40">
                    <td className="px-4 py-2.5 font-bold text-zinc-100 font-mono">
                      {entry.ip}
                    </td>
                    <td className="px-4 py-2.5 font-sans font-medium text-zinc-300">
                      {entry.hostname || (
                        <span className="text-zinc-500 italic font-mono text-[11px]">unnamed</span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-zinc-400">
                      <div className="flex items-center gap-1.5">
                        <span>{entry.mac}</span>
                        <button
                          onClick={() => handleCopy(entry.mac)}
                          className="p-1 rounded text-zinc-500 hover:text-zinc-300 transition-colors"
                          title="Copy MAC"
                        >
                          {copiedMac === entry.mac ? (
                            <Check className="h-3 w-3 text-emerald-400" />
                          ) : (
                            <Copy className="h-3 w-3" />
                          )}
                        </button>
                      </div>
                    </td>
                    <td className="px-4 py-2.5 text-zinc-400">
                      <span className="px-1.5 py-0.5 rounded text-[10px] bg-zinc-900 border border-zinc-800">
                        {entry.interface}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 font-sans text-zinc-400">
                      {entry.manufacturer || (
                        <span className="text-zinc-600 italic">Unknown</span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-right">
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => handleAdoptArp(entry)}
                        className="h-7 px-2 text-[11px] font-sans gap-1 text-sky-400 border-sky-600/30 hover:bg-sky-950/30 hover:text-sky-300"
                      >
                        <UserPlus className="h-3 w-3" />
                        <span>Adopt Host</span>
                      </Button>
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={6} className="px-4 py-8 text-center text-zinc-500 italic font-sans">
                    {isArpLoading ? 'Querying OPNsense ARP cache...' : 'No ARP entries found.'}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* Adopt Modal */}
      {adoptModalOpen && (
        <AdoptNodeModal
          isOpen={adoptModalOpen}
          onClose={() => {
            setAdoptModalOpen(false)
            setAdoptTarget(null)
          }}
          host={adoptTarget}
        />
      )}
    </div>
  )
}
