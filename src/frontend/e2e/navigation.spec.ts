import { test, expect } from '@playwright/test'
import { setupMockApi } from './fixtures/mockApi'

test.describe('Navigation & Shell', () => {
  test.beforeEach(async ({ page }) => {
    await setupMockApi(page)
    await page.goto('/')
  })

  test('renders top header branding and system status', async ({ page }) => {
    await expect(page.getByRole('heading', { name: /ControlPlane/i })).toBeVisible()
    await expect(page.getByText(/v0\.1\.0-alpha/i)).toBeVisible()
  })

  test('switches across sidebar views cleanly', async ({ page }) => {
    // 1. Initial view: Host Inventory
    await expect(page.getByText('Total Managed Nodes')).toBeVisible()

    // 2. Switch to Service Discovery
    await page.getByRole('button', { name: /Service Discovery/i }).click()
    await expect(page.getByText(/Service Discovery & Adoption Hub/i)).toBeVisible()

    // 3. Switch to Workflows & DAGs
    await page.getByRole('button', { name: /Workflows & DAGs/i }).click()
    await expect(page.getByText(/DAG Update Orchestration Engine/i)).toBeVisible()

    // 4. Switch to Infrastructure Adapters
    await page.getByRole('button', { name: /Infrastructure Adapters/i }).click()
    await expect(page.getByRole('heading', { name: /Infrastructure Adapters/i })).toBeVisible()

    // 5. Switch back to Host Inventory
    await page.getByRole('button', { name: /Host Inventory/i }).click()
    await expect(page.getByText('Total Managed Nodes')).toBeVisible()
  })
})
