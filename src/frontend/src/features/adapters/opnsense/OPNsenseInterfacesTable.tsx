import { useState } from 'react'
import { Search, Copy, Check, Network } from 'lucide-react'
import { Input } from '../../../components/ui/input'
import { Badge } from '../../../components/ui/badge'
import type { OPNsenseInterface } from '../../../api/opnsense'

interface OPNsenseInterfacesTableProps {
  interfaces?: OPNsenseInterface[]
}

export function OPNsenseInterfacesTable({ interfaces = [] }: OPNsenseInterfacesTableProps) {
  const [search, setSearch] = useState('')
  const [copiedMac, setCopiedMac] = useState<string | null>(null)

  const handleCopy = (text: string) => {
    navigator.clipboard.writeText(text)
    setCopiedMac(text)
    setTimeout(() => setCopiedMac(null), 2000)
  }

  const filtered = interfaces.filter((iface) => {
    const q = search.toLowerCase()
    return (
      iface.name.toLowerCase().includes(q) ||
      (iface.description && iface.description.toLowerCase().includes(q)) ||
      iface.device.toLowerCase().includes(q) ||
      (iface.ipAddress && iface.ipAddress.toLowerCase().includes(q)) ||
      (iface.macAddress && iface.macAddress.toLowerCase().includes(q))
    )
  })

  return (
    <div className="space-y-3">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider flex items-center gap-1.5">
          <Network className="h-3.5 w-3.5 text-orange-400" />
          <span>Physical & Virtual Interfaces ({interfaces.length})</span>
        </h4>

        <div className="relative flex-1 max-w-xs">
          <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-500" />
          <Input
            placeholder="Filter interfaces (WAN, KUBERNETES, VLAN)..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="pl-8 text-xs h-8 bg-zinc-950/60"
          />
        </div>
      </div>

      <div className="overflow-x-auto rounded-xl border border-zinc-800 bg-zinc-950/50">
        <table className="w-full text-left text-xs text-zinc-300">
          <thead className="bg-zinc-900/80 text-zinc-400 font-medium uppercase text-[10px] tracking-wider border-b border-zinc-800">
            <tr>
              <th className="px-4 py-2.5">Zone / Interface</th>
              <th className="px-4 py-2.5">Device</th>
              <th className="px-4 py-2.5">IP Address / Subnet</th>
              <th className="px-4 py-2.5">MAC Address</th>
              <th className="px-4 py-2.5">Media / Speed</th>
              <th className="px-4 py-2.5 text-right">Status</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-zinc-800/60 font-mono">
            {filtered.length > 0 ? (
              filtered.map((iface) => {
                const displayName = iface.description || iface.name
                const isWan = displayName.toUpperCase().includes('WAN')
                const isVlan = iface.device.includes('.') || displayName.toUpperCase().includes('VLAN')

                return (
                  <tr key={iface.name + iface.device} className="hover:bg-zinc-900/40">
                    <td className="px-4 py-2.5 font-sans font-medium text-zinc-100">
                      <div className="flex items-center gap-2">
                        <span
                          className={`px-2 py-0.5 rounded text-[11px] font-bold font-mono ${
                            isWan
                              ? 'bg-amber-500/10 text-amber-400 border border-amber-500/20'
                              : isVlan
                              ? 'bg-purple-500/10 text-purple-400 border border-purple-500/20'
                              : 'bg-sky-500/10 text-sky-400 border border-sky-500/20'
                          }`}
                        >
                          [{displayName.toUpperCase()}]
                        </span>
                        {iface.description && iface.description !== iface.name && (
                          <span className="text-[11px] text-zinc-500 font-mono">
                            {iface.name}
                          </span>
                        )}
                      </div>
                    </td>

                    <td className="px-4 py-2.5 text-zinc-400 font-mono">
                      {iface.device}
                    </td>

                    <td className="px-4 py-2.5 font-bold text-zinc-200">
                      {iface.ipAddress || (
                        <span className="text-zinc-500 italic font-sans font-normal text-[11px]">
                          Unassigned / Bridge
                        </span>
                      )}
                    </td>

                    <td className="px-4 py-2.5 text-zinc-400">
                      {iface.macAddress ? (
                        <div className="flex items-center gap-1.5">
                          <span>{iface.macAddress}</span>
                          <button
                            onClick={() => handleCopy(iface.macAddress!)}
                            className="p-1 rounded text-zinc-500 hover:text-zinc-300 transition-colors"
                            title="Copy MAC"
                          >
                            {copiedMac === iface.macAddress ? (
                              <Check className="h-3 w-3 text-emerald-400" />
                            ) : (
                              <Copy className="h-3 w-3" />
                            )}
                          </button>
                        </div>
                      ) : (
                        <span className="text-zinc-600">N/A</span>
                      )}
                    </td>

                    <td className="px-4 py-2.5 text-zinc-400 truncate max-w-xs">
                      {iface.media || 'Ethernet'}
                      {iface.mtu && (
                        <span className="text-zinc-500 ml-1.5 text-[10px]">
                          MTU {iface.mtu}
                        </span>
                      )}
                    </td>

                    <td className="px-4 py-2.5 text-right">
                      <div className="inline-flex items-center gap-1.5 justify-end">
                        <Badge
                          variant={iface.status.toLowerCase() === 'up' ? 'success' : 'default'}
                          className="text-[10px] py-0"
                        >
                          {iface.status.toUpperCase()}
                        </Badge>
                      </div>
                    </td>
                  </tr>
                )
              })
            ) : (
              <tr>
                <td colSpan={6} className="px-4 py-8 text-center text-zinc-500 italic font-sans">
                  {interfaces.length === 0
                    ? 'No interface data available.'
                    : 'No interfaces match the search query.'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
