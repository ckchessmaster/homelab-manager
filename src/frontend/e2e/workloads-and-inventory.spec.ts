import { test, expect } from '@playwright/test'
import { setupMockApi } from './fixtures/mockApi'

test.describe('Modern Grouped Inventory & Application Workloads Hub', () => {
  test.beforeEach(async ({ page }) => {
    await setupMockApi(page)
    await page.goto('/')
  })

  test('filters inventory by platform chips and toggles grouped platform view', async ({ page }) => {
    // Check presence of platform chips
    await expect(page.getByRole('button', { name: /All Platforms/i })).toBeVisible()
    await expect(page.getByRole('button', { name: /Proxmox PVE/i })).toBeVisible()
    await expect(page.getByRole('button', { name: /Kubernetes Nodes/i })).toBeVisible()
    await expect(page.getByRole('button', { name: /Baremetal \/ Physical/i })).toBeVisible()

    // Filter by Proxmox PVE
    await page.getByRole('button', { name: /Proxmox PVE/i }).click()
    await expect(page.getByText('pve-node-01')).toBeVisible()
    await expect(page.getByText('ubuntu-worker-01')).toBeVisible()

    // Toggle to Grouped by Platform view
    await page.getByRole('button', { name: /Grouped/i }).click()

    // Should see Proxmox cluster group card
    await expect(page.getByText(/Proxmox VE: proxmox/i)).toBeVisible()
    await expect(page.getByText('VMID 101')).toBeVisible()

    // Switch back to Flat List
    await page.getByRole('button', { name: /Flat List/i }).click()
    await expect(page.getByRole('table')).toBeVisible()
  })

  test('navigates to Applications & Workloads, inspects pods, and scales deployment', async ({ page }) => {
    // Click Workloads navigation in sidebar
    await page.getByRole('button', { name: /Applications & Workloads/i }).click()

    // Header and metrics should appear
    await expect(page.getByRole('heading', { name: 'Applications & Workloads' })).toBeVisible()
    await expect(page.getByText(/Total Deployments/i)).toBeVisible()
    await expect(page.getByText('nginx-ingress-controller')).toBeVisible()
    await expect(page.getByText('prometheus-server')).toBeVisible()
    await expect(page.getByText('plex-media-server')).toBeVisible()

    // Filter by search query
    const searchInput = page.getByPlaceholder(/Search deployment or image/i)
    await searchInput.fill('prometheus')
    await expect(page.getByText('prometheus-server')).toBeVisible()
    await expect(page.getByText('nginx-ingress-controller')).not.toBeVisible()

    // Clear search
    await searchInput.fill('')
    await expect(page.getByText('nginx-ingress-controller')).toBeVisible()

    // Open Pods drawer for nginx-ingress-controller
    const podsBtn = page.getByRole('button', { name: /Pods/i }).first()
    await podsBtn.click()

    // Pod drawer should slide in
    await expect(page.getByText(/Total Pods/i)).toBeVisible()
    await expect(page.getByText('nginx-ingress-controller-784f9b8c6f-abcde')).toBeVisible()
    await expect(page.getByText('k8s-worker-01')).toBeVisible()
    await expect(page.getByText('10.244.1.20')).toBeVisible()

    // Close pod drawer
    await page.getByTitle('Close drawer').click()
    await expect(page.getByText('nginx-ingress-controller-784f9b8c6f-abcde')).not.toBeVisible()

    // Open Scale modal
    const scaleBtn = page.getByRole('button', { name: 'Scale', exact: true }).first()
    await scaleBtn.click()

    await expect(page.getByText('Scale Deployment Replicas')).toBeVisible()

    // Click '+' to increase replica count
    const plusBtn = page.getByRole('button').filter({ has: page.locator('svg.lucide-plus') })
    await plusBtn.click()

    // Click Apply Scale
    await page.getByRole('button', { name: /Apply Scale/i }).click()
    await expect(page.getByText(/Successfully requested/i)).toBeVisible()
  })
})
