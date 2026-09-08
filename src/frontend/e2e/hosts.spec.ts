import { test, expect } from '@playwright/test'
import { setupMockApi } from './fixtures/mockApi'

test.describe('Host Inventory & Management', () => {
  test.beforeEach(async ({ page }) => {
    await setupMockApi(page)
    await page.goto('/')
  })

  test('renders seeded hosts with badges and IP addresses', async ({ page }) => {
    await expect(page.getByText('k8s-control-01')).toBeVisible()
    await expect(page.getByText('pve-node-01')).toBeVisible()
    await expect(page.getByText('ubuntu-worker-01')).toBeVisible()

    // Verify IP addresses using exact match to prevent prefix collisions
    await expect(page.getByText('192.168.1.10', { exact: true })).toBeVisible()
    await expect(page.getByText('192.168.1.20', { exact: true })).toBeVisible()
    await expect(page.getByText('192.168.1.101', { exact: true })).toBeVisible()
  })

  test('filters host list by search query', async ({ page }) => {
    const searchInput = page.getByPlaceholder(/Search hostname, IP, friendly name/i)
    await searchInput.fill('ubuntu')

    await expect(page.getByText('ubuntu-worker-01')).toBeVisible()
    await expect(page.getByText('k8s-control-01')).not.toBeVisible()
    await expect(page.getByText('pve-node-01')).not.toBeVisible()

    // Clear search
    await searchInput.fill('')
    await expect(page.getByText('k8s-control-01')).toBeVisible()
  })

  test('opens Add Host modal and creates a new host', async ({ page }) => {
    await page.getByRole('button', { name: /Add Host/i }).click()
    await expect(page.getByText(/Register New Host/i)).toBeVisible()

    // Fill required fields
    await page.locator('input[name="hostname"]').fill('nas-storage-01')
    await page.locator('input[name="friendlyName"]').fill('TrueNAS Storage Core')
    await page.locator('input[name="ipAddress"]').fill('192.168.1.50')

    // Submit form
    await page.getByRole('button', { name: /Register Host/i }).click()

    // Modal closes and new host is displayed
    await expect(page.getByText(/Register New Host/i)).not.toBeVisible()
    await expect(page.getByText('nas-storage-01')).toBeVisible()
    await expect(page.getByText('192.168.1.50')).toBeVisible()
  })

  test('deletes a host after user confirmation', async ({ page }) => {
    // Find the row containing pve-node-01 and open More Actions dropdown
    const row = page.locator('tr', { hasText: 'pve-node-01' })
    await row.locator('button[title*="More Actions"]').click()

    // Click Delete Host menu item
    await page.getByRole('menuitem', { name: /Delete Host/i }).click()

    // Confirm dialog should appear
    await expect(page.getByText(/Are you sure you want to remove/i)).toBeVisible()
    await page.getByRole('button', { name: /Delete Host/i }).click()

    // Wait for delete confirmation dialog to disappear
    await expect(page.getByText(/Are you sure you want to remove/i)).not.toBeVisible()

    // pve-node-01 should no longer be in table
    await expect(page.getByRole('table').getByText('pve-node-01')).not.toBeVisible()
  })

  test('supports selecting hosts and mass adopting them via SSH', async ({ page }) => {
    // Check checkboxes for hosts
    const k8sRow = page.locator('tr', { hasText: 'k8s-control-01' })
    const pveRow = page.locator('tr', { hasText: 'pve-node-01' })

    await k8sRow.locator('input[type="checkbox"]').check()
    await pveRow.locator('input[type="checkbox"]').check()

    // Top batch toolbar should show selection state
    await expect(page.getByText('2 Selected')).toBeVisible()

    // Click "Adopt / Install Agent" button in the toolbar
    await page.getByRole('button', { name: /Adopt \/ Install Agent/i }).click()

    // Mass adopt modal should open
    await expect(page.getByText('Mass Adopt Hosts & Install Agent')).toBeVisible()
    await expect(page.getByText(/Selected Targets \(2 Hosts\)/i)).toBeVisible()

    // Fill credentials
    await page.locator('input[type="password"]').fill('secretpassword')

    // Submit adoption
    await page.getByRole('button', { name: /Adopt All \(2 Hosts\)/i }).click()

    // Verify success report
    await expect(page.getByText(/Mass Adoption Complete/i)).toBeVisible()
    await expect(page.getByText(/Successfully bootstrapped and connected 2 of 2 hosts/i)).toBeVisible()

    // Close modal
    await page.getByRole('button', { name: /Done/i }).click()
    await expect(page.getByText('Mass Adopt Hosts & Install Agent')).not.toBeVisible()
  })
})
