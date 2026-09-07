import { test, expect } from '@playwright/test'
import { setupMockApi } from './fixtures/mockApi'

test.describe('Host Terminal & Streaming Console', () => {
  test.beforeEach(async ({ page }) => {
    await setupMockApi(page)
    await page.goto('/')
  })

  test('opens sliding terminal drawer and mounts xterm.js canvas', async ({ page }) => {
    // Locate the first host row and click "Open Terminal Console"
    const firstRow = page.locator('tr', { hasText: 'k8s-control-01' })
    await firstRow.locator('button[title*="Open Terminal Console"]').click()

    // Drawer should become visible
    await expect(page.getByText(/Live Agent Terminal & Remote Diagnostics Console/i)).toBeVisible()

    // xterm terminal container should be mounted
    await expect(page.locator('.xterm')).toBeVisible()

    // Verify command input fields
    const commandInput = page.getByPlaceholder(/Command \(e\.g\. systemctl\)/i)
    await expect(commandInput).toBeVisible()
    await expect(page.getByRole('button', { name: 'Run', exact: true })).toBeVisible()

    // Close the drawer
    await page.locator('button[title="Close console"]').click()
    await expect(page.getByText(/Live Agent Terminal & Remote Diagnostics Console/i)).not.toBeVisible()
  })

  test('supports typing commands and running preset actions', async ({ page }) => {
    const row = page.locator('tr', { hasText: 'k8s-control-01' })
    await row.locator('button[title*="Open Terminal Console"]').click()

    await expect(page.locator('.xterm')).toBeVisible()

    // Type a command in input
    const commandInput = page.getByPlaceholder(/Command \(e\.g\. systemctl\)/i)
    await commandInput.fill('uptime')
    await expect(commandInput).toHaveValue('uptime')

    // Find and click preset button
    const presetBtn = page.getByRole('button', { name: 'uname -a' })
    await expect(presetBtn).toBeVisible()
    await presetBtn.click()

    // Close drawer
    await page.locator('button[title="Close console"]').click()
  })
})
