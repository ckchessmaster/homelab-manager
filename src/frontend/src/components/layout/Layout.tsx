import React, { useState } from 'react'
import { AppSidebar, type NavTab } from './AppSidebar'
import { AppHeader } from './AppHeader'
import { Dialog, DialogHeader, DialogTitle, DialogDescription, DialogBody, DialogFooter } from '../ui/dialog'
import { Input } from '../ui/input'
import { Button } from '../ui/button'
import { getApiKey, setApiKey } from '../../api/client'
import { Key } from 'lucide-react'

interface LayoutProps {
  activeTab: NavTab
  onSelectTab: (tab: NavTab) => void
  totalHosts?: number
  rebootPendingCount?: number
  children: React.ReactNode
}

export function Layout({
  activeTab,
  onSelectTab,
  totalHosts = 0,
  rebootPendingCount = 0,
  children,
}: LayoutProps) {
  const [apiKeyModalOpen, setApiKeyModalOpen] = useState(false)
  const [currentKey, setCurrentKey] = useState(getApiKey())

  const handleSaveApiKey = (e: React.FormEvent) => {
    e.preventDefault()
    setApiKey(currentKey)
    setApiKeyModalOpen(false)
    window.location.reload()
  }

  return (
    <div className="min-h-screen bg-[#09090b] text-zinc-100 flex">
      {/* Sidebar */}
      <AppSidebar
        activeTab={activeTab}
        onSelectTab={onSelectTab}
        totalHosts={totalHosts}
        rebootPendingCount={rebootPendingCount}
      />

      {/* Main Content Area */}
      <div className="flex-1 flex flex-col min-w-0 overflow-x-hidden">
        <AppHeader
          activeTab={activeTab}
          onOpenSettings={() => setApiKeyModalOpen(true)}
          totalHosts={totalHosts}
          rebootPendingCount={rebootPendingCount}
        />

        <main className="flex-1 p-6 overflow-y-auto overflow-x-hidden">
          {children}
        </main>
      </div>

      {/* API Key Modal */}
      <Dialog open={apiKeyModalOpen} onClose={() => setApiKeyModalOpen(false)}>
        <form onSubmit={handleSaveApiKey}>
          <DialogHeader onClose={() => setApiKeyModalOpen(false)}>
            <div className="flex items-center gap-2">
              <Key className="h-4 w-4 text-emerald-400" />
              <DialogTitle>ControlPlane API Key</DialogTitle>
            </div>
            <DialogDescription>
              Configure the static API authentication key sent in the <code>X-ControlPlane-Key</code> header.
            </DialogDescription>
          </DialogHeader>

          <DialogBody>
            <Input
              label="API Key"
              type="text"
              required
              value={currentKey}
              onChange={(e) => setCurrentKey(e.target.value)}
              helperText="Default development key: dev-secret-key-123"
            />
          </DialogBody>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => setApiKeyModalOpen(false)}
            >
              Cancel
            </Button>
            <Button type="submit" variant="primary" size="sm">
              Save & Reload
            </Button>
          </DialogFooter>
        </form>
      </Dialog>
    </div>
  )
}
