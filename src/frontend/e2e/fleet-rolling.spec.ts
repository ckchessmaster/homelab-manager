import { test, expect } from '@playwright/test'
import { setupMockApi } from './fixtures/mockApi'

test.describe('Fleet Rolling Orchestrator Modal', () => {
  test.beforeEach(async ({ page }) => {
    await setupMockApi(page)
    await page.goto('/')
    await page.getByRole('button', { name: /Workflows & DAGs/i }).click()
  })

  test('opens fleet rolling upgrade launcher and configures batch execution', async ({ page }) => {
    await page.getByRole('button', { name: /Rolling Fleet Upgrade/i }).click()

    // Modal should be visible
    await expect(page.getByText(/Multi-Node Fleet Rolling Orchestration/i)).toBeVisible()

    // Verify rolling launch button
    const launchButton = page.getByRole('button', { name: /Launch Fleet Upgrade/i })
    await expect(launchButton).toBeVisible()
    await launchButton.click()

    // Modal closes upon successful dispatch
    await expect(page.getByText(/Multi-Node Fleet Rolling Orchestration/i)).not.toBeVisible()
  })
})
