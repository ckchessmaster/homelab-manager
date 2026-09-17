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

    // Filter by UniFi source
    await sourceSelect.selectOption('UniFi')
    await expect(page.getByText('roborock-vacuum-a27')).toBeVisible()
    await expect(page.getByText('gitlab-runner-vm')).not.toBeVisible()
    await expect(page.getByText('k8s-worker-02').first()).not.toBeVisible()

    // Verify UniFi row renders UniFi badge and Client type, not Kubernetes Node
    const unifiRow = page.locator('tr', { hasText: 'roborock-vacuum-a27' })
    await expect(unifiRow.getByText('UniFi', { exact: true })).toBeVisible()
    await expect(unifiRow.getByText('Client', { exact: true })).toBeVisible()
    await expect(unifiRow.getByText('UniFi Client')).toBeVisible()
    await expect(unifiRow.getByText('Kubernetes')).not.toBeVisible()

    // Reset to All Sources
    await sourceSelect.selectOption('all')
  })

  test('opens import modal and imports a candidate node while remaining on discovery page', async ({ page }) => {
    const runnerRow = page.locator('tr', { hasText: 'gitlab-runner-vm' })
    await runnerRow.getByRole('button', { name: /Import/i }).click()

    // Modal opens with candidate pre-populated
    await expect(page.getByText(/Import Discovered Host/i)).toBeVisible()
    await expect(page.getByPlaceholder(/e\.g\. k8s-worker-01/i)).toHaveValue('gitlab-runner-vm')

    // Submit import
    await page.getByRole('button', { name: /Add to Inventory/i }).click()

    // Modal closes
    await expect(page.getByText(/Import Discovered Host/i)).not.toBeVisible()

    // User remains on the Service Discovery page
    await expect(page.getByText('Service Discovery & Adoption Hub')).toBeVisible()

    // Success banner is visible
    await expect(page.getByText(/successfully imported into inventory/i)).toBeVisible()
  })

  test('supports multi-select mass adoption of unmanaged candidates', async ({ page }) => {
    // Select checkboxes for gitlab-runner-vm and pihole-dns
    const runnerRow = page.locator('tr', { hasText: 'gitlab-runner-vm' })
    await runnerRow.locator('input[type="checkbox"]').check()

    const piholeRow = page.locator('tr', { hasText: 'pihole-dns' })
    await piholeRow.locator('input[type="checkbox"]').check()

    // Selection bar appears
    await expect(page.getByText(/2 unmanaged hosts selected/i)).toBeVisible()

    // Click Mass Adopt button in the selection bar
    await page.getByRole('button', { name: /Mass Adopt \(2\)/i }).click()

    // Mass Adopt modal opens
    await expect(page.getByText(/Mass Adopt Discovered Hosts/i)).toBeVisible()
    await expect(page.getByText(/Selected Candidates \(2\)/i)).toBeVisible()

    // Submit batch adoption
    await page.getByRole('button', { name: /Mass Adopt \(2 Hosts\)/i }).click()

    // Batch adoption completes
    await expect(page.getByRole('heading', { name: 'Batch Adoption Complete' })).toBeVisible()
    await expect(page.getByText(/Successfully adopted 2 of 2 hosts into inventory/i)).toBeVisible()

    // Close results dialog
    await page.getByRole('button', { name: /Done/i }).click()

    // Modal closes and success notification is displayed
    await expect(page.getByText(/Mass Adopt Discovered Hosts/i)).not.toBeVisible()
    await expect(page.getByText(/Batch adoption complete/i)).toBeVisible()

    // Remains on Discovery page
    await expect(page.getByText('Service Discovery & Adoption Hub')).toBeVisible()
  })
})

