import { useState, useMemo } from 'react'
import {
  Cloud,
  Cpu,
  Server,
  ChevronDown,
  ChevronRight,
  Terminal,
  Play,
  RotateCcw,
  Camera,
  MoreVertical,
  Edit2,
  Trash2,
  Copy,
  Check,
  AlertTriangle,
  GitFork,
} from 'lucide-react'
import type { Host } from '../../api/hosts'
import { isBaremetalHost, isProxmoxHost, isKubernetesHost } from '../../api/hosts'
import type { JobSummary } from '../../api/jobs'
import type { PlatformFilter } from './HostFilterPills'
import { Badge } from '../../components/ui/badge'
import { AgentStatusBadge, RebootBadge, UpdatesBadge } from './HostStatusBadge'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
  DropdownMenuSeparator,
} from '../../components/ui/dropdown-menu'

interface GroupedHostViewProps {
  hosts: Host[]
  platformFilter?: PlatformFilter
  onInspect: (host: Host) => void
  onOpenTerminal: (host: Host) => void
  onTriggerUpdate: (host: Host) => void
  onReboot: (host: Host) => void
  onEdit: (host: Host) => void
  onDelete: (host: Host) => void
  onOpenSnapshots: (host: Host) => void
  isAdmin: boolean
  isOperator: boolean
  activeJobsByHost?: Map<string, JobSummary>
  onViewDag?: (job: JobSummary) => void
}

export function GroupedHostView({
  hosts,
  platformFilter,
  onInspect,
  onOpenTerminal,
  onTriggerUpdate,
  onReboot,
  onEdit,
  onDelete,
  onOpenSnapshots,
  isAdmin,
  isOperator,
  activeJobsByHost,
  onViewDag,
}: GroupedHostViewProps) {
  const [collapsedGroups, setCollapsedGroups] = useState<Record<string, boolean>>(() => {
    try {
      const saved = localStorage.getItem('cp_grouped_collapsed')
      return saved ? JSON.parse(saved) : {}
    } catch {
      return {}
    }
  })

  const [copiedIp, setCopiedIp] = useState<string | null>(null)

  const toggleGroup = (groupId: string) => {
    setCollapsedGroups((prev) => {
      const next = { ...prev, [groupId]: !prev[groupId] }
      try {
        localStorage.setItem('cp_grouped_collapsed', JSON.stringify(next))
      } catch {
        // ignore
      }
      return next
    })
  }

  const handleCopyIp = (ip: string, e: React.MouseEvent) => {
    e.stopPropagation()
    navigator.clipboard.writeText(ip)
    setCopiedIp(ip)
    setTimeout(() => setCopiedIp(null), 2000)
  }

  // Partition hosts into Proxmox, Kubernetes, and Baremetal groups
  const groups = useMemo(() => {
    const proxmoxHosts = hosts.filter(isProxmoxHost)
    const k8sHosts = hosts.filter(isKubernetesHost)
    const baremetalHosts = hosts.filter(isBaremetalHost)

    // Further subgroup Proxmox by node or instance
    const proxmoxByNode = new Map<string, Host[]>()
    for (const h of proxmoxHosts) {
      const key = h.proxmox?.node || h.proxmoxInstanceId || 'Default Proxmox Node'
      if (!proxmoxByNode.has(key)) proxmoxByNode.set(key, [])
      proxmoxByNode.get(key)!.push(h)
    }

    // Sort each Proxmox node's hosts: hypervisor node first, then guest VMs sorted by VMID/hostname
    for (const list of proxmoxByNode.values()) {
      list.sort((a, b) => {
        const aIsGuest = (a.proxmox && a.proxmox.vmid > 0) || a.targetType === 'proxmox_vm' || a.targetType === 'proxmox_lxc' || a.targetType === 'proxmox_qemu'
        const bIsGuest = (b.proxmox && b.proxmox.vmid > 0) || b.targetType === 'proxmox_vm' || b.targetType === 'proxmox_lxc' || b.targetType === 'proxmox_qemu'
        if (aIsGuest !== bIsGuest) return aIsGuest ? 1 : -1
        const aVmid = a.proxmox?.vmid || 0
        const bVmid = b.proxmox?.vmid || 0
        if (aVmid !== bVmid) return aVmid - bVmid
        return a.hostname.localeCompare(b.hostname)
      })
    }

    // Subgroup K8s by clusterId
    const k8sByCluster = new Map<string, Host[]>()
    for (const h of k8sHosts) {
      const key = h.kubernetes?.clusterId || h.k8sClusterId || 'k8s-cluster'
      if (!k8sByCluster.has(key)) k8sByCluster.set(key, [])
      k8sByCluster.get(key)!.push(h)
    }

    return {
      proxmox: Array.from(proxmoxByNode.entries()).map(([nodeName, nodeHosts]) => ({
        id: `pve-${nodeName}`,
        title: `Proxmox VE: ${nodeName}`,
        icon: Cloud,
        badgeColor: 'purple' as const,
        badgeText: `${nodeHosts.length} Nodes & Guests`,
        hosts: nodeHosts,
      })),
      k8s: Array.from(k8sByCluster.entries()).map(([clusterId, clusterHosts]) => ({
        id: `k8s-${clusterId}`,
        title: `Kubernetes Cluster: ${clusterId}`,
        icon: Cpu,
        badgeColor: 'info' as const,
        badgeText: `${clusterHosts.length} Nodes`,
        hosts: clusterHosts,
      })),
      baremetal: {
        id: 'baremetal-physical',
        title: 'Baremetal & Physical Servers',
        icon: Server,
        badgeColor: 'warning' as const,
        badgeText: `${baremetalHosts.length} Machines`,
        hosts: baremetalHosts,
      },
    }
  }, [hosts])

  const renderHostRow = (host: Host, isNested = false) => {
    const isPveGuest =
      host.targetType === 'proxmox_vm' ||
      host.targetType === 'proxmox_lxc' ||
      host.targetType === 'proxmox_qemu' ||
      Boolean(host.proxmox && host.proxmox.vmid > 0)
    const isK8s = Boolean(host.kubernetes?.clusterId || host.k8sClusterId || host.k8sNodeName || host.targetType.includes('k8s'))
    const activeJob = activeJobsByHost?.get(host.id)

    return (
      <div
        key={host.id}
        onClick={() => onInspect(host)}
        className={`flex items-center justify-between p-3.5 rounded-xl border transition-all cursor-pointer select-none ${
          isNested ? 'ml-4 bg-zinc-950/40 border-zinc-800/60' : 'bg-zinc-900/60 border-zinc-800/80'
        } hover:bg-zinc-800/70 hover:border-zinc-700 shadow-sm`}
      >
        {/* Host Identity */}
        <div className="flex items-center gap-3 min-w-0">
          <div className="h-9 w-9 rounded-lg bg-zinc-800/80 border border-zinc-700/60 flex items-center justify-center shrink-0">
            {isK8s ? (
              <Cpu className="h-4 w-4 text-sky-400" />
            ) : isPveGuest ? (
              <Cloud className="h-4 w-4 text-purple-400" />
            ) : (
              <Server className="h-4 w-4 text-emerald-400" />
            )}
          </div>
          <div className="min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="text-sm font-semibold text-zinc-100 truncate">
                {host.hostname}
              </span>
              {host.friendlyName && (
                <span className="text-xs text-zinc-400 truncate hidden sm:inline">
                  ({host.friendlyName})
                </span>
              )}
              {host.proxmox?.vmid ? (
                <Badge variant="purple" className="text-[10px] px-1.5 py-0 font-mono">
                  VMID {host.proxmox.vmid}
                </Badge>
              ) : null}
              {host.k8sNodeName && (
                <Badge variant="info" className="text-[10px] px-1.5 py-0 font-mono">
                  {host.k8sNodeName}
                </Badge>
              )}
              {activeJob && (
                <span
                  onClick={(e) => {
                    e.stopPropagation()
                    if (onViewDag) onViewDag(activeJob)
                    else onTriggerUpdate(host)
                  }}
                  className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-sky-500/15 text-sky-400 border border-sky-500/30 hover:bg-sky-500/25 transition-colors animate-pulse"
                  title={`Active DAG: ${activeJob.activeStep || activeJob.status}. Click to view DAG Canvas.`}
                >
                  <GitFork className="w-2.5 h-2.5" />
                  DAG Active
                </span>
              )}
            </div>

            <div className="flex items-center gap-3 mt-1 text-xs text-zinc-400">
              {/* IP address with 1-click copy */}
              <button
                type="button"
                onClick={(e) => handleCopyIp(host.ipAddress, e)}
                className="flex items-center gap-1 font-mono text-zinc-400 hover:text-emerald-300 transition-colors"
                title="Click to copy IP"
              >
                <span>{host.ipAddress}</span>
                {copiedIp === host.ipAddress ? (
                  <Check className="h-3 w-3 text-emerald-400" />
                ) : (
                  <Copy className="h-3 w-3 text-zinc-500 hover:text-zinc-300" />
                )}
              </button>

              <span className="text-zinc-600">•</span>
              <span className="capitalize">{host.osFamily.replace('linux_', '')}</span>

              {host.idracIp && (
                <>
                  <span className="text-zinc-600">•</span>
                  <span className="text-[11px] text-amber-400/90 font-mono">
                    iDRAC: {host.idracIp}
                  </span>
                </>
              )}
            </div>
          </div>
        </div>

        {/* Telemetry & Action Buttons */}
        <div className="flex items-center gap-3 shrink-0 ml-3">
          <div className="flex items-center gap-2">
            <AgentStatusBadge agent={host.agent} />
            <RebootBadge pending={host.agent.pendingReboot} />
            <UpdatesBadge count={host.agent.upgradablePackagesCount} />
          </div>

          {/* Quick Action Buttons */}
          <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
            <button
              type="button"
              onClick={() => onOpenTerminal(host)}
              disabled={!host.agent?.installed}
              title="Open Terminal"
              className="p-1.5 rounded-lg border border-zinc-800 bg-zinc-950/60 text-zinc-400 hover:text-zinc-100 hover:bg-zinc-800 disabled:opacity-30 disabled:pointer-events-none transition-colors"
            >
              <Terminal className="h-3.5 w-3.5" />
            </button>

            {isOperator && (
              <>
                {activeJob ? (
                  <button
                    type="button"
                    onClick={() => {
                      if (onViewDag) onViewDag(activeJob)
                      else onTriggerUpdate(host)
                    }}
                    title={`DAG Update already running (${activeJob.activeStep || activeJob.status}). Click to view live DAG.`}
                    className="p-1.5 rounded-lg border border-sky-800/80 bg-sky-950/40 text-sky-400 hover:text-sky-300 hover:bg-sky-900/60 transition-colors animate-pulse"
                  >
                    <GitFork className="h-3.5 w-3.5" />
                  </button>
                ) : (
                  <button
                    type="button"
                    onClick={() => onTriggerUpdate(host)}
                    disabled={!host.agent?.installed}
                    title="Launch Update Workflow"
                    className="p-1.5 rounded-lg border border-zinc-800 bg-zinc-950/60 text-zinc-400 hover:text-emerald-400 hover:bg-zinc-800 disabled:opacity-30 disabled:pointer-events-none transition-colors"
                  >
                    <Play className="h-3.5 w-3.5" />
                  </button>
                )}

                <button
                  type="button"
                  onClick={() => onReboot(host)}
                  disabled={!host.agent?.installed || Boolean(activeJob)}
                  title={activeJob ? 'Cannot reboot while DAG is in progress' : 'Reboot Host'}
                  className="p-1.5 rounded-lg border border-zinc-800 bg-zinc-950/60 text-zinc-400 hover:text-amber-400 hover:bg-zinc-800 disabled:opacity-30 disabled:pointer-events-none transition-colors"
                >
                  <RotateCcw className="h-3.5 w-3.5" />
                </button>
              </>
            )}

            {/* Dropdown Menu */}
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <button
                  type="button"
                  className="p-1.5 rounded-lg border border-zinc-800 bg-zinc-950/60 text-zinc-400 hover:text-zinc-100 hover:bg-zinc-800 transition-colors"
                >
                  <MoreVertical className="h-3.5 w-3.5" />
                </button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="right" className="w-48 bg-zinc-950 border-zinc-800">
                <DropdownMenuItem onClick={() => onInspect(host)}>
                  <Server className="h-3.5 w-3.5 mr-2 text-zinc-400" />
                  View Details
                </DropdownMenuItem>

                {activeJob ? (
                  <DropdownMenuItem
                    onClick={() => {
                      if (onViewDag) onViewDag(activeJob)
                      else onTriggerUpdate(host)
                    }}
                    className="text-sky-400 hover:text-sky-300"
                  >
                    <GitFork className="h-3.5 w-3.5 mr-2" />
                    <span>View Active DAG</span>
                  </DropdownMenuItem>
                ) : (
                  <DropdownMenuItem
                    disabled={!isOperator || !host.agent?.installed}
                    onClick={() => onTriggerUpdate(host)}
                    className="text-emerald-400 hover:text-emerald-300"
                  >
                    <Play className="h-3.5 w-3.5 mr-2" />
                    <span>Launch Update</span>
                  </DropdownMenuItem>
                )}

                {host.proxmox && (
                  <DropdownMenuItem onClick={() => onOpenSnapshots(host)}>
                    <Camera className="h-3.5 w-3.5 mr-2 text-purple-400" />
                    Manage Snapshots
                  </DropdownMenuItem>
                )}

                {isAdmin && (
                  <>
                    <DropdownMenuSeparator className="bg-zinc-800" />
                    <DropdownMenuItem onClick={() => onEdit(host)}>
                      <Edit2 className="h-3.5 w-3.5 mr-2 text-zinc-400" />
                      Edit Host
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      onClick={() => onDelete(host)}
                      className="text-rose-400 hover:text-rose-300 hover:bg-rose-950/50"
                    >
                      <Trash2 className="h-3.5 w-3.5 mr-2" />
                      Delete Host
                    </DropdownMenuItem>
                  </>
                )}
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>
      </div>
    )
  }

  const renderSection = (
    id: string,
    title: string,
    Icon: typeof Cloud,
    badgeText: string,
    badgeColor: 'purple' | 'info' | 'warning',
    sectionHosts: Host[],
    borderAccent: string,
    isProxmoxGroup = false
  ) => {
    if (sectionHosts.length === 0) return null
    const isCollapsed = Boolean(collapsedGroups[id])
    const hasHypervisor =
      isProxmoxGroup &&
      sectionHosts.some((h) => {
        const isGuest =
          (h.proxmox && h.proxmox.vmid > 0) ||
          h.targetType === 'proxmox_vm' ||
          h.targetType === 'proxmox_lxc' ||
          h.targetType === 'proxmox_qemu'
        return !isGuest
      })

    return (
      <div
        key={id}
        className={`rounded-2xl border ${borderAccent} bg-zinc-950/40 p-4 transition-all shadow-md space-y-3`}
      >
        {/* Section Header */}
        <div
          onClick={() => toggleGroup(id)}
          className="flex items-center justify-between cursor-pointer select-none py-1"
        >
          <div className="flex items-center gap-2.5">
            <button
              type="button"
              className="p-1 rounded-md text-zinc-400 hover:text-zinc-200 transition-colors"
            >
              {isCollapsed ? (
                <ChevronRight className="h-4 w-4" />
              ) : (
                <ChevronDown className="h-4 w-4" />
              )}
            </button>
            <Icon className="h-5 w-5 text-zinc-300" />
            <h3 className="text-sm font-bold text-zinc-100">{title}</h3>
            <Badge variant={badgeColor} className="text-xs px-2 py-0.5">
              {badgeText}
            </Badge>
          </div>

          <div className="flex items-center gap-3 text-xs text-zinc-500">
            <span>
              {sectionHosts.filter((h) => h.agent?.installed).length} online
            </span>
            {sectionHosts.some((h) => h.agent?.pendingReboot) && (
              <span className="flex items-center gap-1 text-amber-400 font-medium">
                <AlertTriangle className="h-3.5 w-3.5" />
                Reboot pending
              </span>
            )}
          </div>
        </div>

        {/* Hosts List */}
        {!isCollapsed && (
          <div className="space-y-2 pt-1">
            {sectionHosts.map((h) => {
              const isGuest =
                (h.proxmox && h.proxmox.vmid > 0) ||
                h.targetType === 'proxmox_vm' ||
                h.targetType === 'proxmox_lxc' ||
                h.targetType === 'proxmox_qemu'
              return renderHostRow(h, hasHypervisor && isGuest)
            })}
          </div>
        )}
      </div>
    )
  }

  const showProxmox = !platformFilter || platformFilter === 'all' || platformFilter === 'proxmox'
  const showK8s = !platformFilter || platformFilter === 'all' || platformFilter === 'kubernetes'
  const showBaremetal = !platformFilter || platformFilter === 'all' || platformFilter === 'baremetal'

  const renderedProxmox = showProxmox
    ? groups.proxmox.map((grp) =>
        renderSection(
          grp.id,
          grp.title,
          grp.icon,
          grp.badgeText,
          grp.badgeColor,
          grp.hosts,
          'border-purple-900/40 hover:border-purple-800/60',
          true
        )
      )
    : []

  const renderedK8s = showK8s
    ? groups.k8s.map((grp) =>
        renderSection(
          grp.id,
          grp.title,
          grp.icon,
          grp.badgeText,
          grp.badgeColor,
          grp.hosts,
          'border-sky-900/40 hover:border-sky-800/60'
        )
      )
    : []

  const renderedBaremetal = showBaremetal
    ? renderSection(
        groups.baremetal.id,
        groups.baremetal.title,
        groups.baremetal.icon,
        groups.baremetal.badgeText,
        groups.baremetal.badgeColor,
        groups.baremetal.hosts,
        'border-amber-900/40 hover:border-amber-800/60'
      )
    : null

  const hasAnySections =
    renderedProxmox.some(Boolean) ||
    renderedK8s.some(Boolean) ||
    Boolean(renderedBaremetal)

  if (hosts.length === 0 || !hasAnySections) {
    return (
      <div className="p-12 text-center rounded-xl border border-zinc-800 bg-zinc-900/40">
        <Server className="h-10 w-10 text-zinc-600 mx-auto mb-3" />
        <h4 className="text-sm font-semibold text-zinc-300">No matching hosts found</h4>
        <p className="text-xs text-zinc-500 mt-1">
          Adjust your search or filter facets above.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      {renderedProxmox}
      {renderedK8s}
      {renderedBaremetal}
    </div>
  )
}
