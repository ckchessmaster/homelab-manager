import { useState, useMemo } from 'react'
import {
  Sparkles,
  Search,
  Globe,
  ShieldCheck,
  HardDrive,
  Activity,
  Shield,
  Network,
  Lock,
  Key,
  Film,
  Home,
  Package,
  ExternalLink,
  ArrowRight,
} from 'lucide-react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
} from '../../../components/ui/dialog'
import { Button } from '../../../components/ui/button'
import { Input } from '../../../components/ui/input'
import { useHelmCatalog } from './useHelm'
import type { HelmCatalogItem } from '../../../api/helm'

interface HelmCatalogModalProps {
  open: boolean
  onClose: () => void
  onSelectChart: (chart: HelmCatalogItem) => void
}

const CATEGORIES = ['All', 'Networking', 'Storage', 'Monitoring', 'Certificates', 'Security', 'Media', 'Smart Home']

const ICON_MAP: Record<string, React.ReactNode> = {
  Globe: <Globe className="w-5 h-5 text-blue-400" />,
  ShieldCheck: <ShieldCheck className="w-5 h-5 text-emerald-400" />,
  HardDrive: <HardDrive className="w-5 h-5 text-amber-400" />,
  Activity: <Activity className="w-5 h-5 text-rose-400" />,
  Shield: <Shield className="w-5 h-5 text-purple-400" />,
  Network: <Network className="w-5 h-5 text-cyan-400" />,
  Lock: <Lock className="w-5 h-5 text-indigo-400" />,
  Key: <Key className="w-5 h-5 text-yellow-400" />,
  Film: <Film className="w-5 h-5 text-pink-400" />,
  Home: <Home className="w-5 h-5 text-teal-400" />,
}

export function HelmCatalogModal({ open, onClose, onSelectChart }: HelmCatalogModalProps) {
  const { data: catalog = [], isLoading } = useHelmCatalog()
  const [selectedCategory, setSelectedCategory] = useState('All')
  const [searchQuery, setSearchQuery] = useState('')

  const filteredItems = useMemo(() => {
    const list = Array.isArray(catalog) ? catalog : []
    return list.filter((item) => {
      if (selectedCategory !== 'All' && item.category !== selectedCategory) {
        return false
      }
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase()
        return (
          item.name.toLowerCase().includes(q) ||
          item.description.toLowerCase().includes(q) ||
          item.chartName.toLowerCase().includes(q)
        )
      }
      return true
    })
  }, [catalog, selectedCategory, searchQuery])

  return (
    <Dialog open={open} onClose={onClose} maxWidth="2xl">
      <DialogHeader>
        <div className="flex items-center gap-2">
          <div className="p-2 rounded-lg bg-amber-500/10 border border-amber-500/20 text-amber-400">
            <Sparkles className="w-5 h-5" />
          </div>
          <div>
            <DialogTitle>Homelab Helm Catalog</DialogTitle>
            <p className="text-xs text-zinc-400">
              Curated, production-tested Helm charts pre-configured for Kubernetes homelabs
            </p>
          </div>
        </div>
      </DialogHeader>

      <DialogBody className="space-y-4 max-h-[75vh] overflow-y-auto pr-1">
        {/* Search and Filters */}
        <div className="flex flex-col sm:flex-row items-center justify-between gap-3">
          <div className="relative w-full sm:w-72">
            <Search className="w-4 h-4 text-zinc-400 absolute left-3 top-2.5" />
            <Input
              placeholder="Search charts..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="pl-9 bg-zinc-900 border-zinc-700 text-zinc-100 text-xs"
            />
          </div>

          {/* Category Facets */}
          <div className="flex flex-wrap gap-1.5 w-full sm:w-auto">
            {CATEGORIES.map((cat) => (
              <button
                key={cat}
                type="button"
                onClick={() => setSelectedCategory(cat)}
                className={`text-xs px-2.5 py-1 rounded-full font-medium transition-all ${
                  selectedCategory === cat
                    ? 'bg-indigo-600 text-white shadow-sm'
                    : 'bg-zinc-800 text-zinc-400 hover:text-zinc-200 hover:bg-zinc-700'
                }`}
              >
                {cat}
              </button>
            ))}
          </div>
        </div>

        {/* Charts Grid */}
        {isLoading ? (
          <div className="py-12 text-center text-zinc-500 text-sm">
            Loading curated catalog...
          </div>
        ) : filteredItems.length === 0 ? (
          <div className="py-12 text-center text-zinc-500 text-sm">
            No charts found matching your filter criteria.
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3.5 pt-1">
            {filteredItems.map((item) => (
              <div
                key={item.id}
                className="p-4 rounded-xl bg-zinc-900/60 border border-zinc-800 hover:border-zinc-700 transition-all flex flex-col justify-between group"
              >
                <div>
                  <div className="flex items-start justify-between gap-3 mb-2">
                    <div className="flex items-center gap-2.5">
                      <div className="p-2 rounded-lg bg-zinc-800/80 border border-zinc-700/60 shrink-0">
                        {ICON_MAP[item.icon] || <Package className="w-5 h-5 text-indigo-400" />}
                      </div>
                      <div>
                        <h4 className="text-sm font-semibold text-zinc-100 group-hover:text-indigo-400 transition-colors">
                          {item.name}
                        </h4>
                        <span className="inline-block text-[10px] uppercase font-semibold tracking-wider text-indigo-400 bg-indigo-500/10 px-1.5 py-0.5 rounded border border-indigo-500/20 mt-0.5">
                          {item.category}
                        </span>
                      </div>
                    </div>

                    {item.officialUrl && (
                      <a
                        href={item.officialUrl}
                        target="_blank"
                        rel="noreferrer"
                        className="text-zinc-500 hover:text-zinc-300 p-1 transition-colors"
                        title="Official Documentation"
                      >
                        <ExternalLink className="w-3.5 h-3.5" />
                      </a>
                    )}
                  </div>

                  <p className="text-xs text-zinc-400 line-clamp-2 leading-relaxed mb-3">
                    {item.description}
                  </p>
                </div>

                <div className="pt-3 border-t border-zinc-800/60 flex items-center justify-between mt-auto">
                  <div className="text-[11px] font-mono text-zinc-500 truncate max-w-[200px]">
                    {item.chartName}
                  </div>

                  <Button
                    size="sm"
                    onClick={() => {
                      onSelectChart(item)
                      onClose()
                    }}
                    className="bg-indigo-600/90 hover:bg-indigo-600 text-white text-xs h-7 px-3 flex items-center gap-1"
                  >
                    Configure & Install
                    <ArrowRight className="w-3 h-3" />
                  </Button>
                </div>
              </div>
            ))}
          </div>
        )}
      </DialogBody>
    </Dialog>
  )
}
