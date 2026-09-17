import { useState, useMemo } from 'react'
import {
  Package,
  Sparkles,
  Plus,
  RefreshCw,
  Search,
  Sliders,
  RotateCcw,
  Trash2,
} from 'lucide-react'
import { Button } from '../../../components/ui/button'
import { Input } from '../../../components/ui/input'
import { useHelmReleases, useHelmReleaseDetail } from './useHelm'
import { HelmCatalogModal } from './HelmCatalogModal'
import { InstallHelmModal } from './InstallHelmModal'
import { HelmReleaseDetailDrawer } from './HelmReleaseDetailDrawer'
import { RollbackHelmModal } from './RollbackHelmModal'
import { UninstallHelmModal } from './UninstallHelmModal'
import type { HelmReleaseSummary, HelmCatalogItem, InstallHelmReleasePayload } from '../../../api/helm'
import { useAuthUser } from '../../auth/useAuthUser'

interface HelmReleasesViewProps {
  activeClusterId: string
  selectedNamespace?: string
  availableNamespaces?: string[]
}

export function HelmReleasesView({
  activeClusterId,
  selectedNamespace,
  availableNamespaces = [],
}: HelmReleasesViewProps) {
  const [searchQuery, setSearchQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState<'all' | 'deployed' | 'failed' | 'other'>('all')

  // Modals & Drawers state
  const [isCatalogOpen, setIsCatalogOpen] = useState(false)
  const [isInstallOpen, setIsInstallOpen] = useState(false)
  const [installInitialData, setInstallInitialData] = useState<Partial<InstallHelmReleasePayload> | null>(null)

  const [detailRelease, setDetailRelease] = useState<HelmReleaseSummary | null>(null)
  const [rollbackRelease, setRollbackRelease] = useState<HelmReleaseSummary | null>(null)
  const [uninstallRelease, setUninstallRelease] = useState<HelmReleaseSummary | null>(null)

  const { isOperator } = useAuthUser()

  const {
    data: releases = [],
    isLoading,
    isRefetching,
    refetch,
  } = useHelmReleases(activeClusterId, selectedNamespace)

  // Query detail for upgrade prefill if needed
  const { data: upgradeDetail } = useHelmReleaseDetail(
    activeClusterId,
    detailRelease?.namespace || '',
    detailRelease?.name || ''
  )

  const filteredReleases = useMemo(() => {
    return releases.filter((r) => {
      // Status filter
      if (statusFilter === 'deployed' && r.status.toLowerCase() !== 'deployed') return false
      if (statusFilter === 'failed' && r.status.toLowerCase() !== 'failed') return false
      if (
        statusFilter === 'other' &&
        (r.status.toLowerCase() === 'deployed' || r.status.toLowerCase() === 'failed')
      ) {
        return false
      }

      // Search filter
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase()
        return (
          r.name.toLowerCase().includes(q) ||
          r.namespace.toLowerCase().includes(q) ||
          r.chart.toLowerCase().includes(q) ||
          r.chartName.toLowerCase().includes(q) ||
          (r.appVersion && r.appVersion.toLowerCase().includes(q))
        )
      }

      return true
    })
  }, [releases, statusFilter, searchQuery])

  const deployedCount = releases.filter((r) => r.status.toLowerCase() === 'deployed').length
  const failedCount = releases.filter((r) => r.status.toLowerCase() === 'failed').length

  const handleSelectCatalogItem = (item: HelmCatalogItem) => {
    setInstallInitialData({
      releaseName: item.id,
      namespace: item.defaultNamespace,
      chartName: item.chartName,
      repoUrl: item.repoUrl,
      valuesYaml: item.defaultValuesYaml,
    })
    setIsInstallOpen(true)
  }

  const handleUpgradeRelease = (rel: HelmReleaseSummary) => {
    setDetailRelease(null)
    const storedRepo =
      upgradeDetail?.repoUrl ||
      localStorage.getItem(`helm_repo_${activeClusterId}_${rel.namespace}_${rel.name}`) ||
      localStorage.getItem(`helm_chart_repo_${rel.chartName}`) ||
      ''

    setInstallInitialData({
      releaseName: rel.name,
      namespace: rel.namespace,
      chartName: rel.chartName,
      repoUrl: storedRepo,
      version: rel.chartVersion,
      valuesYaml: upgradeDetail?.valuesYaml || '',
    })
    setIsInstallOpen(true)
  }

  return (
    <div className="space-y-4">
      {/* Header and Action Controls */}
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 bg-zinc-900/40 p-4 rounded-xl border border-zinc-800">
        <div className="flex items-center gap-3 w-full sm:w-auto">
          {/* Search bar */}
          <div className="relative flex-1 sm:w-64">
            <Search className="w-4 h-4 text-zinc-400 absolute left-3 top-2.5" />
            <Input
              placeholder="Search Helm releases..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="pl-9 bg-zinc-900 border-zinc-700 text-zinc-100 text-xs h-9"
            />
          </div>

          {/* Status chips */}
          <div className="flex items-center gap-1.5 overflow-x-auto">
            <button
              onClick={() => setStatusFilter('all')}
              className={`text-xs px-2.5 py-1 rounded-full font-medium transition-all ${
                statusFilter === 'all'
                  ? 'bg-zinc-700 text-white'
                  : 'bg-zinc-800/80 text-zinc-400 hover:text-zinc-200'
              }`}
            >
              All ({releases.length})
            </button>
            <button
              onClick={() => setStatusFilter('deployed')}
              className={`text-xs px-2.5 py-1 rounded-full font-medium transition-all flex items-center gap-1.5 ${
                statusFilter === 'deployed'
                  ? 'bg-emerald-600 text-white'
                  : 'bg-zinc-800/80 text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <span className="w-1.5 h-1.5 rounded-full bg-emerald-400" />
              Deployed ({deployedCount})
            </button>
            {failedCount > 0 && (
              <button
                onClick={() => setStatusFilter('failed')}
                className={`text-xs px-2.5 py-1 rounded-full font-medium transition-all flex items-center gap-1.5 ${
                  statusFilter === 'failed'
                    ? 'bg-red-600 text-white'
                    : 'bg-zinc-800/80 text-red-400 hover:text-red-300'
                }`}
              >
                <span className="w-1.5 h-1.5 rounded-full bg-red-400" />
                Failed ({failedCount})
              </button>
            )}
          </div>
        </div>

        {/* Action buttons */}
        <div className="flex items-center gap-2 self-end sm:self-auto">
          <Button
            variant="outline"
            size="sm"
            onClick={() => refetch()}
            disabled={isLoading || isRefetching}
            className="h-8 text-xs border-zinc-700 hover:bg-zinc-800 text-zinc-300"
            title="Refresh releases"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isRefetching ? 'animate-spin' : ''}`} />
          </Button>

          {isOperator && (
            <>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setIsCatalogOpen(true)}
                className="h-8 text-xs border-amber-500/30 text-amber-300 hover:bg-amber-500/10 flex items-center gap-1.5 font-medium"
              >
                <Sparkles className="w-3.5 h-3.5 text-amber-400" />
                Homelab Catalog
              </Button>

              <Button
                size="sm"
                onClick={() => {
                  setInstallInitialData(null)
                  setIsInstallOpen(true)
                }}
                className="h-8 text-xs bg-indigo-600 hover:bg-indigo-500 text-white flex items-center gap-1.5 font-medium"
              >
                <Plus className="w-3.5 h-3.5" />
                Deploy Chart
              </Button>
            </>
          )}
        </div>
      </div>

      {/* Releases Table / Cards */}
      {isLoading ? (
        <div className="p-16 text-center text-zinc-500 text-sm bg-zinc-900/20 border border-zinc-800 rounded-xl">
          Loading Helm releases...
        </div>
      ) : filteredReleases.length === 0 ? (
        <div className="p-12 text-center bg-zinc-900/30 border border-dashed border-zinc-800 rounded-xl space-y-3">
          <div className="p-3 bg-indigo-500/10 border border-indigo-500/20 rounded-full w-fit mx-auto text-indigo-400">
            <Package className="w-6 h-6" />
          </div>
          <div>
            <h4 className="text-sm font-semibold text-zinc-200">No Helm Releases Found</h4>
            <p className="text-xs text-zinc-400 max-w-md mx-auto mt-1">
              {releases.length === 0
                ? 'No Helm charts are currently deployed in this cluster or namespace. Explore our curated Homelab Catalog to deploy Ingress, cert-manager, or storage with 1 click.'
                : 'No releases matched your search criteria.'}
            </p>
          </div>

          {releases.length === 0 && isOperator && (
            <div className="pt-2 flex items-center justify-center gap-2">
              <Button
                size="sm"
                onClick={() => setIsCatalogOpen(true)}
                className="bg-amber-600 hover:bg-amber-500 text-white text-xs flex items-center gap-1.5"
              >
                <Sparkles className="w-3.5 h-3.5" />
                Explore Homelab Catalog
              </Button>
              <Button
                size="sm"
                variant="outline"
                onClick={() => {
                  setInstallInitialData(null)
                  setIsInstallOpen(true)
                }}
                className="text-xs text-zinc-300 border-zinc-700 hover:bg-zinc-800"
              >
                Deploy Custom Chart
              </Button>
            </div>
          )}
        </div>
      ) : (
        <div className="bg-zinc-900/40 border border-zinc-800 rounded-xl overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-zinc-900/80 text-zinc-400 uppercase text-[10px] tracking-wider border-b border-zinc-800">
                <tr>
                  <th className="py-3 px-4 font-semibold">Release</th>
                  <th className="py-3 px-4 font-semibold">Namespace</th>
                  <th className="py-3 px-4 font-semibold">Status</th>
                  <th className="py-3 px-4 font-semibold">Chart</th>
                  <th className="py-3 px-4 font-semibold">App Version</th>
                  <th className="py-3 px-4 font-semibold">Revision</th>
                  <th className="py-3 px-4 font-semibold">Updated</th>
                  <th className="py-3 px-4 font-semibold text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800/60">
                {filteredReleases.map((r) => {
                  const isDeployed = r.status.toLowerCase() === 'deployed'
                  const isFailed = r.status.toLowerCase() === 'failed'

                  return (
                    <tr
                      key={`${r.namespace}-${r.name}`}
                      className="hover:bg-zinc-800/40 transition-colors group cursor-pointer"
                      onClick={() => setDetailRelease(r)}
                    >
                      <td className="py-3 px-4">
                        <div className="flex items-center gap-2">
                          <Package className="w-4 h-4 text-indigo-400 shrink-0" />
                          <span className="font-semibold text-zinc-100 group-hover:text-indigo-400 transition-colors">
                            {r.name}
                          </span>
                        </div>
                      </td>

                      <td className="py-3 px-4 font-mono text-zinc-300">
                        <span className="px-2 py-0.5 rounded bg-zinc-800/80 border border-zinc-700/60 text-[11px]">
                          {r.namespace}
                        </span>
                      </td>

                      <td className="py-3 px-4">
                        <span
                          className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11px] font-semibold border ${
                            isDeployed
                              ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400'
                              : isFailed
                              ? 'bg-red-500/10 border-red-500/20 text-red-400'
                              : 'bg-amber-500/10 border-amber-500/20 text-amber-400'
                          }`}
                        >
                          <span
                            className={`w-1.5 h-1.5 rounded-full ${
                              isDeployed ? 'bg-emerald-400 animate-pulse' : isFailed ? 'bg-red-400' : 'bg-amber-400'
                            }`}
                          />
                          {r.status}
                        </span>
                      </td>

                      <td className="py-3 px-4 text-zinc-200 font-medium">
                        {r.chart}
                      </td>

                      <td className="py-3 px-4 text-zinc-400 font-mono">
                        {r.appVersion || '—'}
                      </td>

                      <td className="py-3 px-4 text-zinc-300 font-mono">
                        rev {r.revision}
                      </td>

                      <td className="py-3 px-4 text-zinc-400 whitespace-nowrap">
                        {new Date(r.updated).toLocaleDateString()} {new Date(r.updated).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                      </td>

                      <td className="py-3 px-4 text-right" onClick={(e) => e.stopPropagation()}>
                        <div className="flex items-center justify-end gap-1">
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setDetailRelease(r)}
                            className="h-7 px-2 text-zinc-400 hover:text-zinc-100 hover:bg-zinc-800"
                            title="View Values & Manifest"
                          >
                            <Sliders className="w-3.5 h-3.5" />
                          </Button>

                          {isOperator && (
                            <>
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => setRollbackRelease(r)}
                                className="h-7 px-2 text-amber-400 hover:text-amber-300 hover:bg-amber-950/20"
                                title="Rollback Release"
                              >
                                <RotateCcw className="w-3.5 h-3.5" />
                              </Button>

                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => setUninstallRelease(r)}
                                className="h-7 px-2 text-red-400 hover:text-red-300 hover:bg-red-950/20"
                                title="Uninstall Release"
                              >
                                <Trash2 className="w-3.5 h-3.5" />
                              </Button>
                            </>
                          )}
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Catalog Modal */}
      <HelmCatalogModal
        open={isCatalogOpen}
        onClose={() => setIsCatalogOpen(false)}
        onSelectChart={handleSelectCatalogItem}
      />

      {/* Deploy / Upgrade Modal */}
      <InstallHelmModal
        open={isInstallOpen}
        onClose={() => setIsInstallOpen(false)}
        clusterId={activeClusterId}
        availableNamespaces={availableNamespaces}
        initialData={installInitialData}
      />

      {/* Release Detail Drawer */}
      <HelmReleaseDetailDrawer
        open={Boolean(detailRelease)}
        onClose={() => setDetailRelease(null)}
        clusterId={activeClusterId}
        release={detailRelease}
        onUpgrade={handleUpgradeRelease}
        onRollback={(rel) => {
          setDetailRelease(null)
          setRollbackRelease(rel)
        }}
        onUninstall={(rel) => {
          setDetailRelease(null)
          setUninstallRelease(rel)
        }}
      />

      {/* Rollback Modal */}
      <RollbackHelmModal
        open={Boolean(rollbackRelease)}
        onClose={() => setRollbackRelease(null)}
        clusterId={activeClusterId}
        release={rollbackRelease}
      />

      {/* Uninstall Modal */}
      <UninstallHelmModal
        open={Boolean(uninstallRelease)}
        onClose={() => setUninstallRelease(null)}
        clusterId={activeClusterId}
        release={uninstallRelease}
      />
    </div>
  )
}
