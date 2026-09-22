import { useState } from 'react'
import {
  X,
  Package,
  RotateCcw,
  Trash2,
  Copy,
  Check,
  FileCode,
  History,
  FileText,
  Sliders,
  Sparkles,
  Loader2,
} from 'lucide-react'
import { Button } from '../../../components/ui/button'
import {
  useHelmReleaseDetail,
  useHelmReleaseHistory,
} from './useHelm'
import {
  getHelmReleaseDetail,
  type HelmReleaseSummary,
  type HelmReleaseDetail,
} from '../../../api/helm'

interface HelmReleaseDetailDrawerProps {
  open: boolean
  onClose: () => void
  clusterId: string
  release: HelmReleaseSummary | null
  onUpgrade?: (release: HelmReleaseSummary, detail?: HelmReleaseDetail | null, overrideValuesYaml?: string) => void
  onRollback?: (release: HelmReleaseSummary) => void
  onUninstall?: (release: HelmReleaseSummary) => void
}

type TabType = 'values' | 'manifest' | 'history' | 'notes'

export function HelmReleaseDetailDrawer({
  open,
  onClose,
  clusterId,
  release,
  onUpgrade,
  onRollback,
  onUninstall,
}: HelmReleaseDetailDrawerProps) {
  const [activeTab, setActiveTab] = useState<TabType>('values')
  const [copiedText, setCopiedText] = useState<string | null>(null)
  const [valuesView, setValuesView] = useState<'custom' | 'computed'>('custom')
  const [loadingRevisionValues, setLoadingRevisionValues] = useState<number | null>(null)

  const { data: detail, isLoading: isDetailLoading } = useHelmReleaseDetail(
    clusterId,
    release?.namespace || '',
    release?.name || ''
  )

  const { data: history = [], isLoading: isHistoryLoading } = useHelmReleaseHistory(
    clusterId,
    release?.namespace || '',
    release?.name || ''
  )

  if (!open || !release) return null

  const handleCopy = (text: string, label: string) => {
    navigator.clipboard.writeText(text)
    setCopiedText(label)
    setTimeout(() => setCopiedText(null), 2000)
  }

  const isDeployed = release.status.toLowerCase() === 'deployed'
  const isFailed = release.status.toLowerCase() === 'failed'
  const updateInfo = detail?.updateInfo ?? release.updateInfo

  const handleLoadRevisionValues = async (revNumber: number) => {
    setLoadingRevisionValues(revNumber)
    try {
      const revDetail = await getHelmReleaseDetail(clusterId, release.namespace, release.name, revNumber)
      if (onUpgrade) {
        onUpgrade(release, revDetail, revDetail.valuesYaml || revDetail.computedValuesYaml || '')
      }
    } catch (err) {
      console.error('Failed to load revision detail', err)
    } finally {
      setLoadingRevisionValues(null)
    }
  }

  return (
    <div className="fixed inset-y-0 right-0 z-50 w-full max-w-2xl bg-zinc-950 border-l border-zinc-800 shadow-2xl flex flex-col animate-in slide-in-from-right duration-200">
      {/* Drawer Header */}
      <div className="p-4 border-b border-zinc-800 flex items-start justify-between gap-4 bg-zinc-900/40">
        <div className="flex items-start gap-3">
          <div className="p-2.5 rounded-lg bg-indigo-500/10 border border-indigo-500/20 text-indigo-400 mt-0.5">
            <Package className="w-5 h-5" />
          </div>
          <div>
            <div className="flex items-center gap-2 flex-wrap">
              <h3 className="text-base font-semibold text-zinc-100">{release.name}</h3>
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
                {release.status}
              </span>
              <span className="text-[11px] font-mono px-2 py-0.5 rounded bg-zinc-800 text-zinc-300 border border-zinc-700">
                rev {release.revision}
              </span>
              {updateInfo?.isOutdated ? (
                <span className="inline-flex items-center gap-1 text-[11px] font-medium text-amber-400 bg-amber-500/10 border border-amber-500/20 px-2 py-0.5 rounded-full">
                  <Sparkles className="w-3 h-3 text-amber-400" />
                  Update available
                </span>
              ) : updateInfo && !updateInfo.isOutdated ? (
                <span className="inline-flex items-center gap-1 text-[11px] font-medium text-emerald-400 bg-emerald-500/10 border border-emerald-500/20 px-2 py-0.5 rounded-full">
                  <Check className="w-3 h-3 text-emerald-400" />
                  Up to date
                </span>
              ) : null}
            </div>

            <div className="flex items-center gap-3 text-xs text-zinc-400 mt-1">
              <span>Namespace: <strong className="text-zinc-200 font-mono">{release.namespace}</strong></span>
              <span>•</span>
              <span>Chart: <strong className="text-zinc-200">{release.chart}</strong></span>
              {release.appVersion && (
                <>
                  <span>•</span>
                  <span>App Version: <strong className="text-zinc-200">{release.appVersion}</strong></span>
                </>
              )}
            </div>
          </div>
        </div>

        <button
          onClick={onClose}
          className="p-1 rounded-lg text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800 transition-colors"
        >
          <X className="w-5 h-5" />
        </button>
      </div>

      {/* Outdated Chart Callout Banner */}
      {updateInfo?.isOutdated && (
        <div className="mx-4 mt-3 p-3 rounded-xl bg-amber-500/10 border border-amber-500/25 text-xs flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div className="flex items-start gap-2.5">
            <div className="p-1.5 rounded-lg bg-amber-500/20 text-amber-400 mt-0.5">
              <Sparkles className="w-4 h-4" />
            </div>
            <div>
              <div className="flex items-center gap-1.5 font-medium text-amber-200">
                <span>Newer Chart Available:</span>
                <span className="font-mono font-bold text-white bg-amber-500/20 px-1.5 py-0.5 rounded border border-amber-500/30">
                  {updateInfo.latestVersion}
                </span>
                {updateInfo.updateType && (
                  <span className="uppercase text-[9px] px-1.5 py-0.5 rounded bg-amber-500/30 text-amber-300 font-mono font-bold">
                    {updateInfo.updateType}
                  </span>
                )}
              </div>
              <div className="flex items-center gap-2 text-[11px] text-amber-300/80 mt-1 flex-wrap">
                <span>Current: <strong className="font-mono text-zinc-300">{release.chartVersion}</strong></span>
                {updateInfo.latestAppVersion && (
                  <>
                    <span>•</span>
                    <span>New App: <strong className="font-mono text-zinc-300">{updateInfo.latestAppVersion}</strong></span>
                  </>
                )}
                {updateInfo.repoUrl && (
                  <>
                    <span>•</span>
                    <span className="text-zinc-400 truncate max-w-xs">{updateInfo.repoUrl}</span>
                  </>
                )}
              </div>
            </div>
          </div>

          {onUpgrade && (
            <Button
              size="sm"
              onClick={() => onUpgrade(release)}
              className="h-7 text-xs bg-amber-600 hover:bg-amber-500 text-white font-medium shrink-0 flex items-center gap-1.5 self-end sm:self-auto shadow-sm"
            >
              <Sparkles className="w-3.5 h-3.5" />
              Upgrade to {updateInfo.latestVersion}
            </Button>
          )}
        </div>
      )}

      {/* Tabs */}
      <div className="flex items-center gap-1 px-4 border-b border-zinc-800 bg-zinc-900/20">
        <button
          onClick={() => setActiveTab('values')}
          className={`px-3 py-2 text-xs font-medium border-b-2 flex items-center gap-1.5 transition-colors ${
            activeTab === 'values'
              ? 'border-indigo-500 text-indigo-400'
              : 'border-transparent text-zinc-400 hover:text-zinc-200'
          }`}
        >
          <Sliders className="w-3.5 h-3.5" />
          Values (YAML)
        </button>

        <button
          onClick={() => setActiveTab('manifest')}
          className={`px-3 py-2 text-xs font-medium border-b-2 flex items-center gap-1.5 transition-colors ${
            activeTab === 'manifest'
              ? 'border-indigo-500 text-indigo-400'
              : 'border-transparent text-zinc-400 hover:text-zinc-200'
          }`}
        >
          <FileCode className="w-3.5 h-3.5" />
          Rendered Manifest
        </button>

        <button
          onClick={() => setActiveTab('history')}
          className={`px-3 py-2 text-xs font-medium border-b-2 flex items-center gap-1.5 transition-colors ${
            activeTab === 'history'
              ? 'border-indigo-500 text-indigo-400'
              : 'border-transparent text-zinc-400 hover:text-zinc-200'
          }`}
        >
          <History className="w-3.5 h-3.5" />
          Revisions ({history.length})
        </button>

        {detail?.notes && (
          <button
            onClick={() => setActiveTab('notes')}
            className={`px-3 py-2 text-xs font-medium border-b-2 flex items-center gap-1.5 transition-colors ${
              activeTab === 'notes'
                ? 'border-indigo-500 text-indigo-400'
                : 'border-transparent text-zinc-400 hover:text-zinc-200'
            }`}
          >
            <FileText className="w-3.5 h-3.5" />
            Release Notes
          </button>
        )}
      </div>

      {/* Drawer Content Area */}
      <div className="flex-1 overflow-y-auto p-4 space-y-4">
        {isDetailLoading ? (
          <div className="py-16 text-center text-xs text-zinc-500">Loading release details...</div>
        ) : activeTab === 'values' ? (
          <div className="space-y-3">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
              <div className="flex items-center bg-zinc-900 border border-zinc-800 rounded-lg p-0.5 w-fit">
                <button
                  type="button"
                  onClick={() => setValuesView('custom')}
                  className={`text-xs px-2.5 py-1 rounded font-medium transition-all ${
                    valuesView === 'custom'
                      ? 'bg-indigo-600 text-white shadow-sm'
                      : 'text-zinc-400 hover:text-zinc-200'
                  }`}
                >
                  Custom Values (User)
                </button>
                <button
                  type="button"
                  onClick={() => setValuesView('computed')}
                  className={`text-xs px-2.5 py-1 rounded font-medium transition-all ${
                    valuesView === 'computed'
                      ? 'bg-indigo-600 text-white shadow-sm'
                      : 'text-zinc-400 hover:text-zinc-200'
                  }`}
                >
                  Computed Values (All)
                </button>
              </div>

              <div className="flex items-center gap-2">
                {onUpgrade && (
                  <Button
                    size="sm"
                    onClick={() =>
                      onUpgrade(
                        release,
                        detail,
                        valuesView === 'custom'
                          ? detail?.valuesYaml || ''
                          : detail?.computedValuesYaml || ''
                      )
                    }
                    className="h-7 text-xs bg-indigo-600 hover:bg-indigo-500 text-white flex items-center gap-1 shadow-sm"
                  >
                    <Sliders className="w-3 h-3" />
                    Configure Values
                  </Button>
                )}

                {((valuesView === 'custom' && detail?.valuesYaml) ||
                  (valuesView === 'computed' && detail?.computedValuesYaml)) && (
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() =>
                      handleCopy(
                        valuesView === 'custom'
                          ? detail?.valuesYaml || ''
                          : detail?.computedValuesYaml || '',
                        'values'
                      )
                    }
                    className="h-7 text-xs flex items-center gap-1"
                  >
                    {copiedText === 'values' ? (
                      <>
                        <Check className="w-3 h-3 text-emerald-400" />
                        Copied
                      </>
                    ) : (
                      <>
                        <Copy className="w-3 h-3" />
                        Copy YAML
                      </>
                    )}
                  </Button>
                )}
              </div>
            </div>

            {valuesView === 'custom' ? (
              detail?.valuesYaml ? (
                <pre className="p-3.5 bg-zinc-900 border border-zinc-800 rounded-lg text-xs font-mono text-zinc-200 overflow-x-auto whitespace-pre leading-relaxed">
                  {detail.valuesYaml}
                </pre>
              ) : (
                <div className="p-8 text-center bg-zinc-900/40 border border-zinc-800 rounded-lg text-xs text-zinc-400 space-y-3">
                  <p>No custom user values provided (this release is using default chart values).</p>
                  <div className="flex items-center justify-center gap-2">
                    {detail?.computedValuesYaml && (
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() => setValuesView('computed')}
                        className="text-xs border-zinc-700 text-zinc-300"
                      >
                        Inspect Computed Values
                      </Button>
                    )}
                    {onUpgrade && (
                      <Button
                        size="sm"
                        onClick={() => onUpgrade(release, detail, '')}
                        className="text-xs bg-indigo-600 hover:bg-indigo-500 text-white flex items-center gap-1.5"
                      >
                        <Sliders className="w-3.5 h-3.5" />
                        Configure Custom Values
                      </Button>
                    )}
                  </div>
                </div>
              )
            ) : detail?.computedValuesYaml ? (
              <pre className="p-3.5 bg-zinc-900 border border-zinc-800 rounded-lg text-xs font-mono text-zinc-200 overflow-x-auto whitespace-pre leading-relaxed max-h-[60vh]">
                {detail.computedValuesYaml}
              </pre>
            ) : (
              <div className="p-8 text-center bg-zinc-900/40 border border-zinc-800 rounded-lg text-xs text-zinc-500">
                No computed values available.
              </div>
            )}
          </div>
        ) : activeTab === 'manifest' ? (
          <div>
            <div className="flex items-center justify-between mb-2">
              <span className="text-xs text-zinc-400">Rendered Kubernetes resources</span>
              {detail?.manifest && (
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => handleCopy(detail.manifest || '', 'manifest')}
                  className="h-7 text-xs flex items-center gap-1"
                >
                  {copiedText === 'manifest' ? (
                    <>
                      <Check className="w-3 h-3 text-emerald-400" />
                      Copied
                    </>
                  ) : (
                    <>
                      <Copy className="w-3 h-3" />
                      Copy Manifest
                    </>
                  )}
                </Button>
              )}
            </div>

            {detail?.manifest ? (
              <pre className="p-3.5 bg-zinc-900 border border-zinc-800 rounded-lg text-xs font-mono text-zinc-200 overflow-x-auto whitespace-pre leading-relaxed max-h-[60vh]">
                {detail.manifest}
              </pre>
            ) : (
              <div className="p-8 text-center bg-zinc-900/40 border border-zinc-800 rounded-lg text-xs text-zinc-500">
                No manifest available for this release.
              </div>
            )}
          </div>
        ) : activeTab === 'history' ? (
          <div className="space-y-3">
            <div className="text-xs text-zinc-400">
              Revision history of upgrades and rollbacks for this release:
            </div>

            {isHistoryLoading ? (
              <div className="py-8 text-center text-xs text-zinc-500">Loading revisions...</div>
            ) : history.length === 0 ? (
              <div className="p-8 text-center bg-zinc-900/40 border border-zinc-800 rounded-lg text-xs text-zinc-500">
                No historical revisions recorded.
              </div>
            ) : (
              <div className="space-y-2">
                {history.map((rev) => (
                  <div
                    key={rev.revision}
                    className={`p-3 rounded-lg border flex items-center justify-between gap-3 ${
                      rev.revision === release.revision
                        ? 'bg-indigo-500/10 border-indigo-500/30'
                        : 'bg-zinc-900/60 border-zinc-800'
                    }`}
                  >
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="font-mono text-xs font-semibold text-zinc-100">
                          Revision {rev.revision}
                        </span>
                        {rev.revision === release.revision && (
                          <span className="text-[10px] bg-indigo-500/20 text-indigo-300 px-1.5 py-0.5 rounded border border-indigo-500/30 font-semibold">
                            ACTIVE
                          </span>
                        )}
                        <span className="text-[10px] uppercase font-mono px-1.5 py-0.5 rounded bg-zinc-800 text-zinc-400">
                          {rev.status}
                        </span>
                      </div>
                      <div className="text-[11px] text-zinc-400 mt-1">
                        {rev.chart} {rev.appVersion && `(App ${rev.appVersion})`} •{' '}
                        {new Date(rev.updated).toLocaleString()}
                      </div>
                      {rev.description && (
                        <div className="text-[11px] text-zinc-500 italic mt-0.5">
                          "{rev.description}"
                        </div>
                      )}
                    </div>

                    <div className="flex items-center gap-2">
                      {onUpgrade && (
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={loadingRevisionValues === rev.revision}
                          onClick={() => handleLoadRevisionValues(rev.revision)}
                          className="h-7 text-xs text-indigo-400 hover:text-indigo-300 border-indigo-500/30 hover:bg-indigo-500/10 flex items-center gap-1"
                          title="Load configuration values from this revision into setup"
                        >
                          {loadingRevisionValues === rev.revision ? (
                            <Loader2 className="w-3 h-3 animate-spin text-indigo-400" />
                          ) : (
                            <FileCode className="w-3 h-3" />
                          )}
                          <span>Use Values in Setup</span>
                        </Button>
                      )}

                      {rev.revision !== release.revision && (
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => onRollback?.(release)}
                          className="h-7 text-xs text-amber-400 hover:text-amber-300 border-amber-500/30 hover:bg-amber-500/10 flex items-center gap-1"
                        >
                          <RotateCcw className="w-3 h-3" />
                          Rollback
                        </Button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        ) : (
          <div>
            <div className="flex items-center justify-between mb-2">
              <span className="text-xs text-zinc-400">Post-installation release notes</span>
            </div>
            <pre className="p-3.5 bg-zinc-900 border border-zinc-800 rounded-lg text-xs font-mono text-zinc-300 overflow-x-auto whitespace-pre-wrap leading-relaxed">
              {detail?.notes}
            </pre>
          </div>
        )}
      </div>

      {/* Drawer Footer Actions */}
      <div className="p-4 border-t border-zinc-800 bg-zinc-900/40 flex items-center justify-between">
        <Button
          variant="outline"
          onClick={() => onUninstall?.(release)}
          className="text-red-400 hover:text-red-300 border-red-800/40 hover:bg-red-950/20 text-xs flex items-center gap-1.5"
        >
          <Trash2 className="w-3.5 h-3.5" />
          Uninstall Release
        </Button>

        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            onClick={() => onRollback?.(release)}
            className="text-amber-400 hover:text-amber-300 border-amber-500/30 hover:bg-amber-500/10 text-xs flex items-center gap-1.5"
          >
            <RotateCcw className="w-3.5 h-3.5" />
            Rollback
          </Button>

          <Button
            onClick={() =>
              onUpgrade?.(
                release,
                detail,
                valuesView === 'custom'
                  ? detail?.valuesYaml || ''
                  : detail?.computedValuesYaml || ''
              )
            }
            className={`text-xs flex items-center gap-1.5 font-medium ${
              updateInfo?.isOutdated
                ? 'bg-amber-600 hover:bg-amber-500 text-white shadow-sm'
                : 'bg-indigo-600 hover:bg-indigo-500 text-white'
            }`}
          >
            {updateInfo?.isOutdated ? (
              <>
                <Sparkles className="w-3.5 h-3.5" />
                Upgrade to {updateInfo.latestVersion}
              </>
            ) : (
              <>
                <Sliders className="w-3.5 h-3.5" />
                Configure / Upgrade Values
              </>
            )}
          </Button>
        </div>
      </div>
    </div>
  )
}
