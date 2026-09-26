import { Page } from '@playwright/test'
import type { Host } from '../../src/api/hosts'
import type { DiscoveredCandidate, DiscoveryScanResult } from '../../src/api/discovery'
import type { HelmCatalogItem } from '../../src/api/helm'

export const INITIAL_MOCK_HELM_CATALOG: HelmCatalogItem[] = [
  {
    id: 'ingress-nginx',
    name: 'Ingress NGINX',
    category: 'Networking',
    description: 'Ingress controller for Kubernetes using NGINX as a reverse proxy and load balancer.',
    repoUrl: 'https://kubernetes.github.io/ingress-nginx',
    chartName: 'ingress-nginx',
    defaultNamespace: 'ingress-nginx',
    defaultValuesYaml: '# Default values for ingress-nginx\ncontroller:\n  replicaCount: 1\n',
    icon: 'Network',
    officialUrl: 'https://kubernetes.github.io/ingress-nginx/',
  },
  {
    id: 'prometheus-stack',
    name: 'kube-prometheus-stack',
    category: 'Monitoring',
    description: 'Collection of Kubernetes manifests, Grafana dashboards, and Prometheus rules.',
    repoUrl: 'https://prometheus-community.github.io/helm-charts',
    chartName: 'kube-prometheus-stack',
    defaultNamespace: 'monitoring',
    defaultValuesYaml: '# Default values for kube-prometheus-stack\ngrafana:\n  enabled: true\n',
    icon: 'Activity',
    officialUrl: 'https://github.com/prometheus-community/helm-charts',
  },
]

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
  {
    id: 'unifi:default:aabbcc112233',
    source: 'UniFi',
    name: 'roborock-vacuum-a27',
    ipAddress: '192.168.80.235',
    targetType: 'baremetal',
    osFamily: 'linux_debian',
    status: 'online',
    roles: ['network-client'],
    isManaged: false,
  },
  {
    id: 'opnsense:default:ddeeff445566',
    source: 'OPNsense',
    name: 'smart-plug-01',
    ipAddress: '192.168.80.120',
    targetType: 'baremetal',
    osFamily: 'linux_debian',
    status: 'active',
    roles: ['dhcp-lease'],
    isManaged: false,
  },
]

export async function setupMockApi(page: Page, options?: {
  hosts?: Host[]
  candidates?: DiscoveredCandidate[]
}) {
  let hosts = [...(options?.hosts || INITIAL_MOCK_HOSTS)]
  const candidates = [...(options?.candidates || INITIAL_MOCK_CANDIDATES)]

  // Set default Admin role and auth bypass for test predictability
  await page.addInitScript(() => {
    localStorage.setItem('cp_auth_bypass', 'true')
    if (!localStorage.getItem('cp_bypass_role')) {
      localStorage.setItem('cp_bypass_role', 'Admin')
    }
  })

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

  // 8b. GET /api/v1/hosts/:id/vitals
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/hosts\/[a-zA-Z0-9-]+\/vitals$/, async (route) => {
    if (route.request().method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          cpuUsagePct: 18.5,
          memoryUsagePct: 45.2,
          diskFreePct: 62.1,
          powerWatts: 145,
          temperatureCelsius: 38.5,
          uptimeSeconds: 86400 * 4 + 3600 * 5,
          source: 'mock-agent',
        }),
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

  // 9b. POST /api/v1/hosts/adopt-batch
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/hosts\/adopt-batch$/, async (route) => {
    const payload = route.request().postDataJSON()
    const targetHosts = payload.hosts || []
    for (const h of targetHosts) {
      const existing = hosts.find((item) => item.id === h.hostId)
      if (existing) {
        existing.agent = {
          installed: true,
          version: '1.1.0',
          lastSeenAt: new Date().toISOString(),
          pendingReboot: false,
          upgradablePackagesCount: 0,
        }
      }
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        totalRequested: targetHosts.length,
        succeededCount: targetHosts.length,
        failedCount: 0,
        results: targetHosts.map((h: any) => ({
          hostId: h.hostId,
          hostname: h.hostname || h.targetHost,
          success: true,
          message: 'Agent installed and running',
        })),
      }),
    })
  })

  // 9c. POST /api/v1/hosts/adopt and /api/v1/hosts/:id/adopt
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/hosts\/([a-zA-Z0-9-]+\/)?adopt$/, async (route) => {
    const payload = route.request().postDataJSON()
    if (payload.hostId) {
      const existing = hosts.find((item) => item.id === payload.hostId)
      if (existing) {
        existing.agent = {
          installed: true,
          version: '1.1.0',
          lastSeenAt: new Date().toISOString(),
          pendingReboot: false,
          upgradablePackagesCount: 0,
        }
      }
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        hostId: payload.hostId || 'adopted-host',
        success: true,
        message: 'Successfully adopted node',
        steps: [],
      }),
    })
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

  // 11. Discovery Import (Single)
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/discovery\/import$/, async (route) => {
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

    // Update candidate in mock state
    const match = candidates.find((c) => c.name === payload.name)
    if (match) {
      match.isManaged = true
      match.existingHostId = newHostId
      match.existingHostname = payload.name
    }

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

  // 11b. Discovery Batch Import
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/discovery\/import-batch$/, async (route) => {
    const payload = route.request().postDataJSON()
    const items = payload.candidates || []
    const results = []

    for (const c of items) {
      const newHostId = `imported-host-${Date.now()}-${Math.random().toString(36).substring(2, 7)}`
      const importedHost: Host = {
        id: newHostId,
        hostname: c.name,
        friendlyName: c.friendlyName || c.name,
        ipAddress: c.ipAddress,
        osFamily: c.osFamily || payload.commonOsFamily || 'linux_debian',
        targetType: c.targetType || payload.commonTargetType || 'baremetal',
        proxmox: c.proxmoxNode ? { node: c.proxmoxNode, vmid: c.proxmoxVmid || 0 } : null,
        agent: {
          installed: false,
          pendingReboot: false,
          upgradablePackagesCount: 0,
        },
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }
      hosts.push(importedHost)

      const match = candidates.find((cand) => cand.name === c.name)
      if (match) {
        match.isManaged = true
        match.existingHostId = newHostId
        match.existingHostname = c.name
      }

      results.push({
        name: c.name,
        success: true,
        hostId: newHostId,
        hostname: c.name,
        errorMessage: null,
      })
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        totalRequested: items.length,
        succeededCount: results.length,
        failedCount: 0,
        results,
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

  let mockRollingBatches: any[] = []

  // Active rolling batch
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/orchestration\/temporal\/batch\/active/, async (route) => {
    const active = mockRollingBatches.find((b) => b.status === 'Running' || b.status === 'Paused') || null
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(active),
    })
  })

  // Start rolling upgrade batch
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/orchestration\/temporal\/batch\/rolling-upgrade/, async (route) => {
    const payload = route.request().postDataJSON()
    const batchId = `batch-${Date.now()}`
    const workflowId = `rolling-upgrade-${batchId}`
    const targetHostIds: string[] = payload.hostIds || []
    const targetHostObjects = hosts.filter((h) => targetHostIds.includes(h.id))
    const hostnames = targetHostObjects.map((h) => h.hostname)

    const hostProgresses: Record<string, any> = {}
    for (const h of targetHostObjects) {
      hostProgresses[h.id] = {
        hostId: h.id,
        hostname: h.hostname,
        status: 'Pending',
        currentStep: null,
      }
    }

    const newBatch = {
      batchId,
      workflowId,
      status: 'Running',
      totalHosts: targetHostIds.length,
      completedHosts: 0,
      failedHosts: 0,
      activeHostname: hostnames[0] || null,
      isPaused: false,
      hostIds: targetHostIds,
      hostnames,
      initiatedBy: payload.initiatedBy || 'Operator',
      startedAt: new Date().toISOString(),
      hostProgresses,
    }
    mockRollingBatches.unshift(newBatch)

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        batchId,
        workflowId,
        totalHosts: targetHostIds.length,
        targetHostIds,
      }),
    })
  })

  // Batch status endpoint
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/orchestration\/temporal\/batch\/[^/]+\/status/, async (route) => {
    const url = route.request().url()
    const match = url.match(/\/batch\/([^/]+)\/status/)
    const batchId = match ? match[1] : 'mock-batch'
    const found = mockRollingBatches.find((b) => b.batchId === batchId)
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        workflowId: found?.workflowId || `rolling-upgrade-${batchId}`,
        executionStatus: found?.status || 'Running',
        state: found
          ? {
              batchId: found.batchId,
              status: found.status,
              totalHosts: found.totalHosts,
              completedHosts: found.completedHosts,
              failedHosts: found.failedHosts,
              activeHostname: found.activeHostname,
              isPaused: found.isPaused,
              cancelled: false,
              hostProgresses: found.hostProgresses || {},
            }
          : null,
      }),
    })
  })

  // Batch signal endpoint
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/orchestration\/temporal\/batch\/[^/]+\/signals\/[^/]+/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ success: true, signal: 'ack', workflowId: 'mock-wf' }),
    })
  })

  // List batches endpoint (must be registered after more specific subpaths)
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/orchestration\/temporal\/batch(\?.*)?$/, async (route) => {
    if (route.request().method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(mockRollingBatches),
      })
    } else {
      await route.continue()
    }
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

  // 13. Adapters: Proxmox Instances
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/adapters\/proxmox\/instances/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          id: 'pve-primary',
          name: 'Primary Proxmox VE',
          baseUrl: 'https://192.168.1.20:8006',
          apiTokenId: 'root@pam!token',
          apiTokenSecretMasked: '••••••••',
          hasSecret: true,
          allowSelfSignedCert: true,
          taskPollTimeoutSeconds: 300,
          taskPollIntervalMilliseconds: 1000,
        },
      ]),
    })
  })

  // 13b. Adapters: iDRAC Instances
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/adapters\/idrac\/instances/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([]),
    })
  })

  // 14. Adapters: Kubernetes Clusters
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/adapters\/k8s\/clusters/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          id: 'k8s-prod',
          name: 'k8s-homelab-prod',
          apiServerUrl: 'https://192.168.1.10:6443',
          hasKubeConfig: true,
          hasToken: false,
          skipTlsVerify: true,
        },
      ]),
    })
  })

  // 15. Adapters: UniFi Instances
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/adapters\/unifi\/instances/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          id: 'unifi-primary',
          name: 'Main UniFi Controller',
          controllerUrl: 'https://192.168.1.1:8443',
          username: 'admin',
          passwordMasked: '••••••••',
          hasPassword: true,
          site: 'default',
          allowSelfSignedCert: true,
          updatedAt: new Date().toISOString(),
        },
      ]),
    })
  })

  // 16. Adapters: UniFi Devices
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/adapters\/unifi\/[^/]+\/devices/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          mac: '74:83:c2:11:22:33',
          name: 'Core Switch 24 PoE',
          model: 'USW-24-PoE',
          type: 'usw',
          ip: '192.168.1.2',
          state: 'Connected',
          version: '6.5.59',
          upgradeAvailable: false,
          uptimeSeconds: 86400,
          temperature: 46,
          ports: [
            { portIdx: 1, name: 'k8s-node-01', up: true, speedMbps: 1000, poeMode: 'auto', poePowerWatts: 7.2 },
            { portIdx: 2, name: 'k8s-node-02', up: true, speedMbps: 1000, poeMode: 'auto', poePowerWatts: 6.8 },
            { portIdx: 3, name: 'Port 3', up: false, speedMbps: 0, poeMode: 'off', poePowerWatts: 0 },
          ],
        },
      ]),
    })
  })

  // 17. Adapters: UniFi Clients
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/adapters\/unifi\/[^/]+\/clients/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          mac: '00:11:22:33:44:55',
          ip: '192.168.1.50',
          hostname: 'nas-storage-01',
          lastSeen: new Date().toISOString(),
        },
      ]),
    })
  })

  // 18. Workloads Aggregation & Operations
  let mockWorkloads = [
    {
      clusterId: 'k8s-prod',
      clusterName: 'k8s-homelab-prod',
      namespace: 'default',
      name: 'nginx-ingress-controller',
      desiredReplicas: 2,
      readyReplicas: 2,
      availableReplicas: 2,
      images: ['registry.k8s.io/ingress-nginx/controller:v1.9.4'],
      creationTimestamp: '2026-01-01T00:00:00Z',
      status: 'Ready',
      kind: 'Deployment',
    },
    {
      clusterId: 'k8s-prod',
      clusterName: 'k8s-homelab-prod',
      namespace: 'monitoring',
      name: 'prometheus-server',
      desiredReplicas: 1,
      readyReplicas: 1,
      availableReplicas: 1,
      images: ['prom/prometheus:v2.48.0'],
      creationTimestamp: '2026-01-02T00:00:00Z',
      status: 'Ready',
      kind: 'Deployment',
    },
    {
      clusterId: 'k8s-prod',
      clusterName: 'k8s-homelab-prod',
      namespace: 'media',
      name: 'plex-media-server',
      desiredReplicas: 1,
      readyReplicas: 0,
      availableReplicas: 0,
      images: ['linuxserver/plex:latest'],
      creationTimestamp: '2026-01-03T00:00:00Z',
      status: 'Degraded',
      kind: 'Deployment',
    },
  ]

  await page.route(/^https?:\/\/[^/]+\/api\/v1\/workloads(\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items: mockWorkloads,
        clusters: ['k8s-homelab-prod'],
        namespaces: ['default', 'monitoring', 'media'],
        totalDeployments: mockWorkloads.length,
        healthyDeployments: mockWorkloads.filter((w) => w.status === 'Ready').length,
      }),
    })
  })

  await page.route(/^https?:\/\/[^/]+\/api\/v1\/workloads\/[^/]+\/[^/]+\/[^/]+\/pods/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          name: 'nginx-ingress-controller-784f9b8c6f-abcde',
          namespace: 'default',
          phase: 'Running',
          nodeName: 'k8s-worker-01',
          podIp: '10.244.1.20',
          restartCount: 0,
          isReady: true,
          startTime: '2026-01-01T00:05:00Z',
        },
        {
          name: 'nginx-ingress-controller-784f9b8c6f-fghij',
          namespace: 'default',
          phase: 'Running',
          nodeName: 'k8s-worker-02',
          podIp: '10.244.2.22',
          restartCount: 0,
          isReady: true,
          startTime: '2026-01-01T00:05:00Z',
        },
      ]),
    })
  })

  await page.route(/^https?:\/\/[^/]+\/api\/v1\/workloads\/[^/]+\/[^/]+\/[^/]+\/restart/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: true,
        message: 'Rollout restart triggered successfully.',
      }),
    })
  })

  await page.route(/^https?:\/\/[^/]+\/api\/v1\/workloads\/[^/]+\/[^/]+\/[^/]+\/scale/, async (route) => {
    const payload = route.request().postDataJSON()
    const match = route.request().url().match(/workloads\/([^/]+)\/([^/]+)\/([^/]+)\/scale/)
    if (match) {
      const [, _clusterId, _namespace, name] = match
      const found = mockWorkloads.find((w) => w.name === name)
      if (found) {
        found.desiredReplicas = payload.replicas
        found.readyReplicas = payload.replicas
        found.status = payload.replicas > 0 ? 'Ready' : 'ScaledDown'
      }
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: true,
        replicas: payload?.replicas ?? 1,
        message: 'Deployment scaled successfully.',
      }),
    })
  })

  // Kubernetes Helm Catalog & Releases
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/kubernetes\/helm\/catalog/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(INITIAL_MOCK_HELM_CATALOG),
    })
  })

  await page.route(/^https?:\/\/[^/]+\/api\/v1\/kubernetes\/[^/]+\/helm\/releases/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([]),
    })
  })

  await page.route(/^https?:\/\/[^/]+\/api\/v1\/kubernetes\/[^/]+\/helm\/updates/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({}),
    })
  })

  // Kubernetes Secrets & ConfigMaps
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/kubernetes\/[^/]+\/secrets/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([]),
    })
  })

  await page.route(/^https?:\/\/[^/]+\/api\/v1\/kubernetes\/[^/]+\/configmaps/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([]),
    })
  })

  // Kubernetes Networking & Services
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/kubernetes\/[^/]+\/(network\/)?services/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([]),
    })
  })

  await page.route(/^https?:\/\/[^/]+\/api\/v1\/kubernetes\/[^/]+\/(network\/)?ingresses/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([]),
    })
  })

  await page.route(/^https?:\/\/[^/]+\/api\/v1\/kubernetes\/[^/]+\/(network\/)?certificates/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([]),
    })
  })

  // Kubernetes Storage & Vitals & Events
  await page.route(/^https?:\/\/[^/]+\/api\/v1\/kubernetes\/[^/]+\/storage-overview/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        totalPvcs: 0,
        boundPvcs: 0,
        totalCapacityBytes: 0,
        pvcs: [],
        storageClasses: [],
        longhornDetected: false,
      }),
    })
  })

  await page.route(/^https?:\/\/[^/]+\/api\/v1\/kubernetes\/[^/]+\/vitals/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        metricsServerAvailable: false,
        totalCpuUsageMillis: 0,
        totalCpuAllocatableMillis: 0,
        totalMemoryUsageBytes: 0,
        totalMemoryAllocatableBytes: 0,
        nodes: [],
        topPods: [],
      }),
    })
  })

  await page.route(/^https?:\/\/[^/]+\/api\/v1\/kubernetes\/[^/]+\/events/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([]),
    })
  })

  await page.route(/^https?:\/\/[^/]+\/api\/v1\/kubernetes\/[^/]+\/image-updates/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([]),
    })
  })
}
