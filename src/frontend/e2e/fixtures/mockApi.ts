import { Page } from '@playwright/test'
import type { Host } from '../../src/api/hosts'
import type { DiscoveredCandidate, DiscoveryScanResult } from '../../src/api/discovery'

export const INITIAL_MOCK_HOSTS: Host[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    hostname: 'k8s-control-01',
    friendlyName: 'Kubernetes Primary Control Plane',
    ipAddress: '192.168.1.10',
    osFamily: 'linux_debian',
    targetType: 'baremetal',
    agent: {
      installed: true,
      version: '1.2.0',
      lastSeenAt: new Date().toISOString(),
      pendingReboot: false,
      upgradablePackagesCount: 3,
    },
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
  {
    id: '22222222-2222-2222-2222-222222222222',
    hostname: 'pve-node-01',
    friendlyName: 'Primary Proxmox Hypervisor',
    ipAddress: '192.168.1.20',
    osFamily: 'linux_debian',
    targetType: 'baremetal',
    proxmox: {
      node: 'proxmox',
      vmid: 0,
    },
    agent: {
      installed: true,
      version: '1.2.0',
      lastSeenAt: new Date().toISOString(),
      pendingReboot: false,
      upgradablePackagesCount: 0,
    },
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
  {
    id: '33333333-3333-3333-3333-333333333333',
    hostname: 'ubuntu-worker-01',
    friendlyName: 'Web App Worker VM',
    ipAddress: '192.168.1.101',
    osFamily: 'linux_ubuntu',
    targetType: 'proxmox_qemu',
    proxmox: {
      node: 'proxmox',
      vmid: 101,
    },
    agent: {
      installed: true,
      version: '1.2.0',
      lastSeenAt: new Date().toISOString(),
      pendingReboot: true,
      upgradablePackagesCount: 12,
    },
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
]

export const INITIAL_MOCK_CANDIDATES: DiscoveredCandidate[] = [
  {
    id: 'pve-qemu-200',
    source: 'Proxmox',
    name: 'gitlab-runner-vm',
    ipAddress: '192.168.1.200',
    targetType: 'proxmox_qemu',
    osFamily: 'linux_debian',
    status: 'running',
    proxmoxNode: 'proxmox',
    proxmoxVmid: 200,
    isManaged: false,
  },
  {
    id: 'pve-lxc-201',
    source: 'Proxmox',
    name: 'pihole-dns',
    ipAddress: '192.168.1.201',
    targetType: 'proxmox_lxc',
    osFamily: 'linux_alpine',
    status: 'running',
    proxmoxNode: 'proxmox',
    proxmoxVmid: 201,
    isManaged: false,
  },
  {
    id: 'k8s-node-worker-02',
    source: 'Kubernetes',
    name: 'k8s-worker-02',
    ipAddress: '192.168.1.102',
    targetType: 'baremetal',
    osFamily: 'linux_ubuntu',
    status: 'Ready',
    k8sNodeName: 'k8s-worker-02',
    roles: ['worker'],
    isManaged: false,
  },
]

export async function setupMockApi(page: Page, options?: {
  hosts?: Host[]
  candidates?: DiscoveredCandidate[]
}) {
  let hosts = [...(options?.hosts || INITIAL_MOCK_HOSTS)]
  const candidates = [...(options?.candidates || INITIAL_MOCK_CANDIDATES)]

  // 1. Generic catch-all for /api/ (must NOT match /src/api/)
  await page.route(/^https?:\/\/[^/]+\/api\//, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({}),
    })
  })

  // 2. Storage status
  await page.route(/^https?:\/\/[^/]+\/api\/storage\/status/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ isStandby: false, provider: 'PostgreSQL', hasActiveLease: true }),
    })
  })

  // 3. Agent version info
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/agents\/version-info/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        latestVersion: '1.2.0',
        outdatedAgentsCount: 0,
        onlineOutdatedCount: 0,
      }),
    })
  })

  // 4. Snapshots list
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/adapters\/proxmox\/snapshots/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([]),
    })
  })

  // 5. Jobs list
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/jobs/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([]),
    })
  })

  // 6. Pipelines catalog
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/pipelines/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          id: 'standard-os-upgrade',
          name: 'Standard OS Package Upgrade',
          description: 'Runs apt update && apt upgrade with preflight safety checks.',
          steps: ['PreflightChecks', 'AptUpdate', 'AptUpgrade', 'VerifyServices'],
        },
      ]),
    })
  })

  // 7. SignalR negotiate
  await page.route(/^https?:\/\/[^/]+\/hubs\//, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        connectionId: 'mock-signalr-connection-id',
        availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text'] }],
      }),
    })
  })

  // 8. Hosts endpoints (GET with search params & POST)
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/hosts(\?.*)?$/, async (route) => {
    const method = route.request().method()
    if (method === 'GET') {
      const url = new URL(route.request().url())
      const search = url.searchParams.get('search')?.toLowerCase()
      const osFamily = url.searchParams.get('osFamily')
      const targetType = url.searchParams.get('targetType')
      const pendingReboot = url.searchParams.get('pendingReboot')

      let result = [...hosts]
      if (search) {
        result = result.filter(
          (h) =>
            h.hostname.toLowerCase().includes(search) ||
            h.ipAddress.toLowerCase().includes(search) ||
            (h.friendlyName && h.friendlyName.toLowerCase().includes(search))
        )
      }
      if (osFamily) {
        result = result.filter((h) => h.osFamily === osFamily)
      }
      if (targetType) {
        result = result.filter((h) => h.targetType === targetType)
      }
      if (pendingReboot === 'true') {
        result = result.filter((h) => h.agent.pendingReboot)
      }

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(result),
      })
    } else if (method === 'POST') {
      const payload = route.request().postDataJSON()
      const newHost: Host = {
        id: `mock-host-${Date.now()}`,
        hostname: payload.hostname,
        friendlyName: payload.friendlyName || null,
        ipAddress: payload.ipAddress,
        osFamily: payload.osFamily || 'linux_debian',
        targetType: payload.targetType || 'baremetal',
        proxmox: payload.proxmoxNode ? { node: payload.proxmoxNode, vmid: payload.proxmoxVmid || 0 } : null,
        agent: {
          installed: false,
          pendingReboot: false,
          upgradablePackagesCount: 0,
        },
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }
      hosts.push(newHost)
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify(newHost),
      })
    } else {
      await route.continue()
    }
  })

  // 9. DELETE /api/v1/hosts/:id
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/hosts\/[a-zA-Z0-9-]+$/, async (route) => {
    if (route.request().method() === 'DELETE') {
      const url = route.request().url()
      const hostId = url.split('?')[0].split('/').pop()
      hosts = hosts.filter((h) => h.id !== hostId)
      await route.fulfill({ status: 204 })
    } else {
      await route.continue()
    }
  })

  // 10. Discovery Scan
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/discovery\/scan/, async (route) => {
    const result: DiscoveryScanResult = {
      candidates,
      totalDiscovered: candidates.length,
      alreadyManaged: candidates.filter((c) => c.isManaged).length,
      unmanagedCount: candidates.filter((c) => !c.isManaged).length,
      scannedAt: new Date().toISOString(),
      errors: [],
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(result),
    })
  })

  // 11. Discovery Import
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/discovery\/import/, async (route) => {
    const payload = route.request().postDataJSON()
    const newHostId = `imported-host-${Date.now()}`
    const importedHost: Host = {
      id: newHostId,
      hostname: payload.name,
      friendlyName: payload.friendlyName || payload.name,
      ipAddress: payload.ipAddress,
      osFamily: payload.osFamily || 'linux_debian',
      targetType: payload.targetType || 'baremetal',
      proxmox: payload.proxmoxNode ? { node: payload.proxmoxNode, vmid: payload.proxmoxVmid || 0 } : null,
      agent: {
        installed: false,
        pendingReboot: false,
        upgradablePackagesCount: 0,
      },
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }
    hosts.push(importedHost)

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: true,
        hostId: newHostId,
        hostname: payload.name,
      }),
    })
  })

  // 12. Temporal Start & Rolling
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/orchestration\/temporal\/workflows\/start/, async (route) => {
    const payload = route.request().postDataJSON()
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        workflowId: `wf-upgrade-${payload.hostId}-${Date.now()}`,
        runId: `run-${Date.now()}`,
        jobId: `job-${Date.now()}`,
        hostId: payload.hostId,
      }),
    })
  })

  await page.route(/^https?:\/\/[^/]+\/api\/v1\/orchestration\/temporal\/workflows\/rolling\/start/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        workflowId: `wf-rolling-${Date.now()}`,
        runId: `run-${Date.now()}`,
        totalHosts: 3,
      }),
    })
  })
}
