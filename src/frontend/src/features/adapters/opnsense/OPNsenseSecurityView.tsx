import { useState, useEffect } from 'react'
import {
  ShieldAlert,
  ShieldCheck,
  Search,
  Copy,
  Check,
  Layers,
  Activity,
  Flame,
  EyeOff,
  BellOff,
  RotateCcw,
  Trash2,
} from 'lucide-react'
import { Input } from '../../../components/ui/input'
import { Badge } from '../../../components/ui/badge'
import { Button } from '../../../components/ui/button'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogBody,
  DialogFooter,
} from '../../../components/ui/dialog'
import { useOPNsenseSecurityAlerts, useOPNsenseFirewallStats } from './useOPNsense'

interface OPNsenseSecurityViewProps {
  instanceId: string
}

export function OPNsenseSecurityView({ instanceId }: OPNsenseSecurityViewProps) {
  const { data: secStatus, isLoading: isSecLoading } = useOPNsenseSecurityAlerts(instanceId)
  const { data: fwStats } = useOPNsenseFirewallStats(instanceId)

  const [search, setSearch] = useState('')
  const [copiedIp, setCopiedIp] = useState<string | null>(null)
  const [showIgnoredModal, setShowIgnoredModal] = useState(false)

  const dismissedStorageKey = `controlplane:opnsense:dismissed-alerts:${instanceId}`
  const ignoredStorageKey = `controlplane:opnsense:ignored-threats:${instanceId}`

  const [dismissedAlertIds, setDismissedAlertIds] = useState<string[]>(() => {
    try {
      const raw = localStorage.getItem(dismissedStorageKey)
      return raw ? JSON.parse(raw) : []
    } catch {
      return []
    }
  })

  const [ignoredThreats, setIgnoredThreats] = useState<string[]>(() => {
    try {
      const raw = localStorage.getItem(ignoredStorageKey)
      return raw ? JSON.parse(raw) : []
    } catch {
      return []
    }
  })

  // Sync to localStorage
  useEffect(() => {
    try {
      localStorage.setItem(dismissedStorageKey, JSON.stringify(dismissedAlertIds))
    } catch {}
  }, [dismissedAlertIds, dismissedStorageKey])

  useEffect(() => {
    try {
      localStorage.setItem(ignoredStorageKey, JSON.stringify(ignoredThreats))
    } catch {}
  }, [ignoredThreats, ignoredStorageKey])

  const handleCopy = (text: string) => {
    navigator.clipboard.writeText(text)
    setCopiedIp(text)
    setTimeout(() => setCopiedIp(null), 2000)
  }

  const handleDismissAlert = (alertKey: string) => {
    setDismissedAlertIds((prev) => (prev.includes(alertKey) ? prev : [...prev, alertKey]))
  }

  const handleIgnoreThreat = (threat: string) => {
    setIgnoredThreats((prev) => (prev.includes(threat) ? prev : [...prev, threat]))
  }

  const handleUnignoreThreat = (threat: string) => {
    setIgnoredThreats((prev) => prev.filter((t) => t !== threat))
  }

  const handleUnignoreAll = () => {
    setIgnoredThreats([])
  }

  const handleResetDismissed = () => {
    setDismissedAlertIds([])
  }

  const alerts = secStatus?.alerts || []

  // Filter out dismissed individual instances and ignored threat signatures
  const activeAlerts = alerts.filter((alert) => {
    const alertKey = `${alert.timestamp}_${alert.sourceIp}_${alert.destinationIp}_${alert.threat}`
    if (dismissedAlertIds.includes(alertKey)) return false
    if (ignoredThreats.includes(alert.threat)) return false
    return true
  })

  const filteredAlerts = activeAlerts.filter((a) => {
    const q = search.toLowerCase()
    return (
      a.threat.toLowerCase().includes(q) ||
      a.category.toLowerCase().includes(q) ||
      a.sourceIp.toLowerCase().includes(q) ||
      a.destinationIp.toLowerCase().includes(q) ||
      a.severity.toLowerCase().includes(q)
    )
  })

  return (
    <div className="space-y-6 animate-in fade-in">
      {/* Top Security Metric Strip */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {/* PF State Table */}
        <div className="p-4 bg-zinc-900/50 border border-zinc-800 rounded-xl space-y-2">
          <div className="flex items-center justify-between text-zinc-400">
            <span className="text-[11px] font-medium flex items-center gap-1.5">
              <Layers className="h-3.5 w-3.5 text-sky-400" />
              Active PF States
            </span>
            <span className="text-[11px] font-mono text-zinc-200">
              {fwStats?.pfStatesPercent ?? 0.24}%
            </span>
          </div>

          <div className="flex items-center gap-2 pt-0.5">
            <span className="text-lg font-bold text-zinc-100 font-mono">
              {(fwStats?.pfStatesCurrent ?? 1420).toLocaleString()}
            </span>
            <span className="text-xs text-zinc-500 font-mono">
              / {(fwStats?.pfStatesMax ?? 600000).toLocaleString()}
            </span>
          </div>

          <div className="w-full bg-zinc-950 rounded-full h-1.5 overflow-hidden border border-zinc-800">
            <div
              className="bg-sky-500 h-full rounded-full"
              style={{ width: `${Math.max(1, (fwStats?.pfStatesPercent ?? 0.24) * 5)}%` }}
            />
          </div>
        </div>

        {/* Filter Rules */}
        <div className="p-4 bg-zinc-900/50 border border-zinc-800 rounded-xl space-y-1">
          <span className="text-[11px] text-zinc-400 font-medium flex items-center gap-1.5">
            <ShieldCheck className="h-3.5 w-3.5 text-emerald-400" />
            Firewall Filter Rules
          </span>
          <div className="flex items-center gap-2 pt-0.5">
            <span className="text-lg font-bold text-zinc-100 font-mono">
              {fwStats?.totalFilterRules ?? 42}
            </span>
            <Badge variant="success" className="text-[10px]">
              Active
            </Badge>
          </div>
          <p className="text-[11px] text-zinc-500">
            Across WAN, LAN & isolated VLAN zones
          </p>
        </div>

        {/* Aliases */}
        <div className="p-4 bg-zinc-900/50 border border-zinc-800 rounded-xl space-y-1">
          <span className="text-[11px] text-zinc-400 font-medium flex items-center gap-1.5">
            <Activity className="h-3.5 w-3.5 text-purple-400" />
            Network Aliases
          </span>
          <div className="flex items-center gap-2 pt-0.5">
            <span className="text-lg font-bold text-zinc-100 font-mono">
              {fwStats?.totalAliases ?? 18}
            </span>
            <span className="text-xs text-zinc-500 font-mono">configured</span>
          </div>
          <p className="text-[11px] text-zinc-500">
            Port groups, host lists & GeoIP tables
          </p>
        </div>

        {/* IDS Threats 24h */}
        <div className="p-4 bg-zinc-900/50 border border-zinc-800 rounded-xl space-y-1">
          <span className="text-[11px] text-zinc-400 font-medium flex items-center gap-1.5">
            <Flame className="h-3.5 w-3.5 text-amber-400" />
            24h Threat Events
          </span>
          <div className="flex items-center gap-2 pt-0.5">
            <span className="text-lg font-bold text-zinc-100 font-mono">
              {secStatus?.threatCount24h ?? fwStats?.recentBlockedPacketsCount ?? 14}
            </span>
            <Badge variant="warning" className="text-[10px]">
              {secStatus?.running ? 'Suricata IPS' : 'Firewall Dropped'}
            </Badge>
          </div>
          <p className="text-[11px] text-zinc-500">
            Inbound port scans & signature matches
          </p>
        </div>
      </div>

      {/* Suricata IDS Live Alerts Feed */}
      <div className="space-y-3">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <ShieldAlert className="h-4 w-4 text-orange-400 shrink-0" />
            <h4 className="text-xs font-semibold text-zinc-300 uppercase tracking-wider">
              Suricata Intrusion Detection & Security Alerts ({filteredAlerts.length})
            </h4>
            {secStatus?.isInstalled && (
              <Badge variant={secStatus.running ? 'success' : 'default'} className="text-[10px] py-0">
                {secStatus.running ? 'Engine Online' : 'Stopped'}
              </Badge>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {/* Ignored Threats Button */}
            {ignoredThreats.length > 0 && (
              <button
                onClick={() => setShowIgnoredModal(true)}
                className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs rounded-lg border border-amber-500/30 bg-amber-500/10 text-amber-300 hover:bg-amber-500/20 transition-colors"
                title="Manage ignored alert rules"
              >
                <BellOff className="h-3.5 w-3.5 text-amber-400" />
                <span>
                  {ignoredThreats.length} Ignored Rule{ignoredThreats.length > 1 ? 's' : ''}
                </span>
              </button>
            )}

            {/* Restore Dismissed Alerts Button */}
            {dismissedAlertIds.length > 0 && (
              <button
                onClick={handleResetDismissed}
                className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs rounded-lg border border-zinc-800 bg-zinc-900/80 text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800 transition-colors"
                title="Restore dismissed alerts for this session"
              >
                <RotateCcw className="h-3 w-3" />
                <span>Restore ({dismissedAlertIds.length})</span>
              </button>
            )}

            {/* Search Input */}
            <div className="relative flex-1 sm:w-64">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-500" />
              <Input
                placeholder="Search threat alerts (scan, IP, category)..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="pl-8 text-xs h-8 bg-zinc-950/60"
              />
            </div>
          </div>
        </div>

        <div className="overflow-x-auto rounded-xl border border-zinc-800 bg-zinc-950/50">
          <table className="w-full text-left text-xs text-zinc-300">
            <thead className="bg-zinc-900/80 text-zinc-400 font-medium uppercase text-[10px] tracking-wider border-b border-zinc-800">
              <tr>
                <th className="px-4 py-2.5">Severity / Action</th>
                <th className="px-4 py-2.5">Threat Description</th>
                <th className="px-4 py-2.5">Category</th>
                <th className="px-4 py-2.5">Source IP</th>
                <th className="px-4 py-2.5">Destination IP</th>
                <th className="px-4 py-2.5">Proto</th>
                <th className="px-4 py-2.5">Timestamp</th>
                <th className="px-4 py-2.5 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-800/60 font-mono">
              {filteredAlerts.length > 0 ? (
                filteredAlerts.map((alert, idx) => {
                  const alertKey = `${alert.timestamp}_${alert.sourceIp}_${alert.destinationIp}_${alert.threat}`
                  const isHigh = alert.severity.toLowerCase() === 'high'
                  const isMedium = alert.severity.toLowerCase() === 'medium'
                  const isDrop = alert.action.toLowerCase() === 'drop'

                  return (
                    <tr key={alertKey + idx} className="hover:bg-zinc-900/40">
                      <td className="px-4 py-2.5">
                        <div className="flex items-center gap-1.5">
                          <Badge
                            variant={isHigh ? 'destructive' : isMedium ? 'warning' : 'default'}
                            className="text-[10px] py-0"
                          >
                            {alert.severity.toUpperCase()}
                          </Badge>
                          <Badge
                            variant={isDrop ? 'destructive' : 'outline'}
                            className="text-[9px] py-0 font-sans"
                          >
                            {alert.action.toUpperCase()}
                          </Badge>
                        </div>
                      </td>

                      <td className="px-4 py-2.5 font-sans font-medium text-zinc-200 max-w-sm truncate" title={alert.threat}>
                        {alert.threat}
                      </td>

                      <td className="px-4 py-2.5 text-zinc-400 font-sans truncate">
                        {alert.category}
                      </td>

                      <td className="px-4 py-2.5 text-zinc-300 font-mono">
                        <div className="flex items-center gap-1">
                          <span>
                            {alert.sourceIp}
                            {alert.sourcePort ? `:${alert.sourcePort}` : ''}
                          </span>
                          <button
                            onClick={() => handleCopy(alert.sourceIp)}
                            className="p-0.5 rounded text-zinc-500 hover:text-zinc-300 transition-colors"
                            title="Copy IP"
                          >
                            {copiedIp === alert.sourceIp ? (
                              <Check className="h-3 w-3 text-emerald-400" />
                            ) : (
                              <Copy className="h-3 w-3" />
                            )}
                          </button>
                        </div>
                      </td>

                      <td className="px-4 py-2.5 text-zinc-300 font-mono">
                        <span>
                          {alert.destinationIp}
                          {alert.destinationPort ? `:${alert.destinationPort}` : ''}
                        </span>
                      </td>

                      <td className="px-4 py-2.5 text-zinc-400">{alert.protocol}</td>

                      <td className="px-4 py-2.5 text-zinc-500 text-[11px] whitespace-nowrap">
                        {alert.timestamp}
                      </td>

                      <td className="px-4 py-2.5 text-right whitespace-nowrap">
                        <div className="inline-flex items-center gap-1.5 justify-end">
                          <button
                            onClick={() => handleDismissAlert(alertKey)}
                            className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[11px] font-sans text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800 transition-colors"
                            title="Dismiss this alert instance"
                          >
                            <EyeOff className="h-3 w-3" />
                            <span>Dismiss</span>
                          </button>
                          <button
                            onClick={() => handleIgnoreThreat(alert.threat)}
                            className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[11px] font-sans text-amber-400 hover:text-amber-300 hover:bg-amber-500/10 transition-colors"
                            title="Ignore this alert signature going forward (UI only)"
                          >
                            <BellOff className="h-3 w-3" />
                            <span>Ignore Rule</span>
                          </button>
                        </div>
                      </td>
                    </tr>
                  )
                })
              ) : (
                <tr>
                  <td colSpan={8} className="px-4 py-8 text-center text-zinc-500 italic font-sans">
                    {isSecLoading
                      ? 'Querying IDS alert engine...'
                      : secStatus?.isInstalled === false
                      ? 'Suricata Intrusion Detection (os-suricata) is not installed or enabled.'
                      : activeAlerts.length === 0 && alerts.length > 0
                      ? 'All active threat alerts are dismissed or ignored.'
                      : 'No security threats detected in the log buffer.'}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Ignored Threat Signatures Modal */}
      {showIgnoredModal && (
        <Dialog open={showIgnoredModal} onClose={() => setShowIgnoredModal(false)} maxWidth="lg">
          <DialogHeader onClose={() => setShowIgnoredModal(false)}>
            <div className="flex items-center gap-2">
              <BellOff className="h-4 w-4 text-amber-400" />
              <DialogTitle>Ignored Alert Rules & Signatures</DialogTitle>
            </div>
            <DialogDescription>
              Threat alerts matching these signatures are hidden in ControlPlane. OPNsense firewall/IDS rules are not modified.
            </DialogDescription>
          </DialogHeader>

          <DialogBody>
            {ignoredThreats.length === 0 ? (
              <p className="text-zinc-500 italic text-center py-4">No threat signatures are currently ignored.</p>
            ) : (
              <div className="space-y-2">
                {ignoredThreats.map((threat) => (
                  <div
                    key={threat}
                    className="flex items-center justify-between gap-3 p-2.5 rounded-lg border border-zinc-800 bg-zinc-950/60"
                  >
                    <span className="text-xs font-sans text-zinc-200 truncate" title={threat}>
                      {threat}
                    </span>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleUnignoreThreat(threat)}
                      className="h-7 text-xs text-zinc-400 hover:text-zinc-100 hover:bg-zinc-800 gap-1 shrink-0"
                      title="Unhide this threat signature"
                    >
                      <RotateCcw className="h-3 w-3" />
                      <span>Restore</span>
                    </Button>
                  </div>
                ))}
              </div>
            )}
          </DialogBody>

          <DialogFooter>
            {ignoredThreats.length > 0 && (
              <Button
                variant="destructive"
                size="sm"
                onClick={handleUnignoreAll}
                className="text-xs gap-1.5"
              >
                <Trash2 className="h-3.5 w-3.5" />
                <span>Restore All Rules</span>
              </Button>
            )}
            <Button
              variant="primary"
              size="sm"
              onClick={() => setShowIgnoredModal(false)}
              className="text-xs"
            >
              Done
            </Button>
          </DialogFooter>
        </Dialog>
      )}
    </div>
  )
}
