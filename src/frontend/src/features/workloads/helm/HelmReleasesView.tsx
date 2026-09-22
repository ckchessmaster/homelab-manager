import { useState, useMemo, useEffect } from 'react'
import {
  Package,
  Sparkles,
  Plus,
  RefreshCw,
  Search,
  Sliders,
  RotateCcw,
  Trash2,
  Check,
  Loader2,
} from 'lucide-react'
import { Button } from '../../../components/ui/button'
import { Input } from '../../../components/ui/input'
import { Select } from '../../../components/ui/select'
import { useHelmReleases, useCheckHelmUpdates } from './useHelm'
import { HelmCatalogModal } from './HelmCatalogModal'
import { InstallHelmModal } from './InstallHelmModal'
import { HelmReleaseDetailDrawer } from './HelmReleaseDetailDrawer'
import { RollbackHelmModal } from './RollbackHelmModal'
import { UninstallHelmModal } from './UninstallHelmModal'
import {
  getHelmReleaseDetail,
  type HelmReleaseSummary,
  type HelmReleaseDetail,
  type HelmCatalogItem,
  type InstallHelmReleasePayload,
} from '../../../api/helm'
import { useAuthUser } from '../../auth/useAuthUser'

interface HelmReleasesViewProps {
  activeClusterId: string
  selectedNamespace?: string
  onNamespaceChange?: (namespace: string) => void
  availableNamespaces?: string[]
}

export function HelmReleasesView({
  activeClusterId,
  selectedNamespace,
  onNamespaceChange,
  availableNamespaces = [],
}: HelmReleasesViewProps) {
  const [localNamespace, setLocalNamespace] = useState<string>(selectedNamespace || '')

  useEffect(() => {
    if (selectedNamespace !== undefined) {
      setLocalNamespace(selectedNamespace)
    }
  }, [selectedNamespace])
  const [searchQuery, setSearchQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState<'all' | 'deployed' | 'failed' | 'outdated' | 'other'>('all')

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
  } = useHelmReleases(activeClusterId, localNamespace || undefined)

  const { mutate: checkUpdates, isPending: isCheckingUpdates } = useCheckHelmUpdates(activeClusterId)



  const releaseList = Array.isArray(releases) ? releases : []

  const outdatedCount = useMemo(
    () => releaseList.filter((r) => r.updateInfo?.isOutdated).length,
    [releaseList]
  )

  const filteredReleases = useMemo(() => {
    return releaseList.filter((r) => {
      // Status filter
      if (statusFilter === 'deployed' && r.status.toLowerCase() !== 'deployed') return false
      if (statusFilter === 'failed' && r.status.toLowerCase() !== 'failed') return false
      if (statusFilter === 'outdated' && !r.updateInfo?.isOutdated) return false
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
  }, [releaseList, statusFilter, searchQuery])

  const deployedCount = releaseList.filter((r) => r.status.toLowerCase() === 'deployed').length
  const failedCount = releaseList.filter((r) => r.status.toLowerCase() === 'failed').length

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

  const [upgradingReleaseKey, setUpgradingReleaseKey] = useState<string | null>(null)

  const handleUpgradeRelease = async (
    rel: HelmReleaseSummary,
    passedDetail?: HelmReleaseDetail | null,
    overrideValuesYaml?: string
  ) => {
    const releaseKey = `${rel.namespace}/${rel.name}`
    setUpgradingReleaseKey(releaseKey)

    try {
      let detail = passedDetail
      if (!detail && overrideValuesYaml === undefined) {
        try {
          detail = await getHelmReleaseDetail(activeClusterId, rel.namespace, rel.name)
        } catch (err) {
          console.warn('Could not fetch live release detail before upgrade', err)
        }
      }

      const storedRepo =
        detail?.repoUrl ||
        rel.updateInfo?.repoUrl ||
        localStorage.getItem(`helm_repo_${activeClusterId}_${rel.namespace}_${rel.name}`) ||
        localStorage.getItem(`helm_chart_repo_${rel.chartName}`) ||
        ''

      const targetVersion =
        rel.updateInfo?.isOutdated && rel.updateInfo.latestVersion
          ? rel.updateInfo.latestVersion
          : rel.chartVersion

      const cachedBackup =
        localStorage.getItem(`helm_values_${activeClusterId}_${rel.namespace}_${rel.name}`) || ''

      const resolvedValues =
        overrideValuesYaml !== undefined
          ? overrideValuesYaml
          : (detail?.valuesYaml || cachedBackup || '')

      setInstallInitialData({
        releaseName: rel.name,
        namespace: rel.namespace,
        chartName: rel.chartName,
        repoUrl: storedRepo,
        version: targetVersion,
        valuesYaml: resolvedValues,
        reuseValues: !resolvedValues && !overrideValuesYaml,
      })
      setIsInstallOpen(true)
      setDetailRelease(null)
    } finally {
      setUpgradingReleaseKey(null)
    }
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

          {availableNamespaces.length > 0 && (
            <div className="w-36">
              <Select
                value={localNamespace}
                onChange={(e) => {
                  const val = e.target.value
                  setLocalNamespace(val)
                  onNamespaceChange?.(val)
                }}
                className="bg-zinc-900 border-zinc-700 text-zinc-100 text-xs h-9"
              >
                <option value="">All Namespaces</option>
                {availableNamespaces.map((ns) => (
                  <option key={ns} value={ns}>
                    {ns}
                  </option>
                ))}
              </Select>
            </div>
          )}

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
            {outdatedCount > 0 && (
              <button
                onClick={() => setStatusFilter(statusFilter === 'outdated' ? 'all' : 'outdated')}
                className={`text-xs px-2.5 py-1 rounded-full font-medium transition-all flex items-center gap-1.5 ${
                  statusFilter === 'outdated'
                    ? 'bg-amber-500 text-zinc-950 font-semibold shadow-sm'
                    : 'bg-amber-500/10 text-amber-400 border border-amber-500/20 hover:bg-amber-500/20'
                }`}
              >
                <Sparkles className="w-3 h-3 text-amber-400" />
                Updates Available ({outdatedCount})
              </button>
            )}
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
            onClick={() => checkUpdates({ force: true })}
            disabled={isLoading || isRefetching || isCheckingUpdates}
            className="h-8 text-xs border-zinc-700 hover:bg-zinc-800 text-zinc-300 flex items-center gap-1.5"
            title="Check remote Helm repositories and Artifact Hub for chart updates"
          >
            <Sparkles className={`w-3.5 h-3.5 text-amber-400 ${isCheckingUpdates ? 'animate-spin' : ''}`} />
            <span>{isCheckingUpdates ? 'Checking...' : 'Check Updates'}</span>
          </Button>

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
                        <div className="space-y-1">
                          <div className="flex items-center gap-2">
                            <span>{r.chart}</span>
                          </div>
                          {r.updateInfo?.isOutdated ? (
                            <div className="flex items-center gap-1.5">
                              <span
                                className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[10px] font-medium bg-amber-500/10 text-amber-400 border border-amber-500/20"
                                title={r.updateInfo.message || `Version ${r.updateInfo.latestVersion} available`}
                              >
                                <Sparkles className="w-2.5 h-2.5 text-amber-400 shrink-0" />
                                Update: {r.updateInfo.latestVersion}
                                {r.updateInfo.updateType && (
                                  <span className="uppercase text-[9px] px-1 rounded bg-amber-500/20 font-mono font-semibold">
                                    {r.updateInfo.updateType}
                                  </span>
                                )}
                              </span>
                            </div>
                          ) : r.updateInfo && !r.updateInfo.isOutdated ? (
                            <div className="flex items-center gap-1 text-[10px] text-emerald-400/80">
                              <Check className="w-3 h-3 text-emerald-400" />
                              <span>Up to date</span>
                            </div>
                          ) : null}
                        </div>
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
                                onClick={() => handleUpgradeRelease(r)}
                                disabled={upgradingReleaseKey === `${r.namespace}/${r.name}`}
                                className={`h-7 px-2 ${
                                  r.updateInfo?.isOutdated
                                    ? 'text-amber-400 hover:text-amber-300 hover:bg-amber-950/30'
                                    : 'text-indigo-400 hover:text-indigo-300 hover:bg-indigo-950/20'
                                }`}
                                title={
                                  r.updateInfo?.isOutdated
                                    ? `Upgrade release to ${r.updateInfo.latestVersion}`
                                    : 'Upgrade / Reconfigure Release'
                                }
                              >
                                {upgradingReleaseKey === `${r.namespace}/${r.name}` ? (
                                  <Loader2 className="w-3.5 h-3.5 animate-spin text-indigo-400" />
                                ) : r.updateInfo?.isOutdated ? (
                                  <Sparkles className="w-3.5 h-3.5" />
                                ) : (
                                  <Sliders className="w-3.5 h-3.5" />
                                )}
                              </Button>

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
