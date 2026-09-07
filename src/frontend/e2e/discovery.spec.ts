import { test, expect } from '@playwright/test'
import { setupMockApi } from './fixtures/mockApi'

test.describe('Service Discovery & Adoption', () => {
  test.beforeEach(async ({ page }) => {
    await setupMockApi(page)
    await page.goto('/')
    await page.getByRole('button', { name: /Service Discovery/i }).click()
  })

  test('displays discovered candidates from hypervisors and clusters', async ({ page }) => {
    await expect(page.getByText('Service Discovery & Adoption Hub')).toBeVisible()
    await expect(page.getByText('gitlab-runner-vm')).toBeVisible()
    await expect(page.getByText('pihole-dns')).toBeVisible()
    await expect(page.getByText('k8s-worker-02').first()).toBeVisible()
  })

  test('filters candidates by source and management status', async ({ page }) => {
    // Filter by Proxmox source
    const sourceSelect = page.locator('select').first()
    await sourceSelect.selectOption('Proxmox')

    await expect(page.getByText('gitlab-runner-vm')).toBeVisible()
    await expect(page.getByText('pihole-dns')).toBeVisible()
    await expect(page.getByText('k8s-worker-02').first()).not.toBeVisible()

    // Reset to All Sources
    await sourceSelect.selectOption('all')
    await expect(page.getByText('k8s-worker-02').first()).toBeVisible()
  })

  test('opens import modal and imports a candidate node', async ({ page }) => {
    const runnerRow = page.locator('tr', { hasText: 'gitlab-runner-vm' })
    await runnerRow.getByRole('button', { name: /Import/i }).click()

    // Modal opens with candidate pre-populated
    await expect(page.getByText(/Import Discovered Host/i)).toBeVisible()
    await expect(page.getByPlaceholder(/e\.g\. k8s-worker-01/i)).toHaveValue('gitlab-runner-vm')

    // Submit import
    await page.getByRole('button', { name: /Add to Inventory/i }).click()

    // Modal closes
    await expect(page.getByText(/Import Discovered Host/i)).not.toBeVisible()
  })
})
