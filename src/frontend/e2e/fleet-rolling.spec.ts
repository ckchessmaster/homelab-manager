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

    // Select all outdated or all hosts
    const selectAllBtn = page.getByRole('button', { name: /Select All/i })
    if (await selectAllBtn.isVisible()) {
      await selectAllBtn.click()
    }

    // Verify rolling launch button
    const launchButton = page.getByRole('button', { name: /Launch Fleet Upgrade/i })
    await expect(launchButton).toBeVisible()
    await launchButton.click()

    // Launcher modal closes upon successful dispatch
    await expect(page.getByText(/Multi-Node Fleet Rolling Orchestration/i)).not.toBeVisible()

    // Dashboard modal should be visible and not blank
    await expect(page.getByText(/Fleet Rolling Upgrade Progress/i)).toBeVisible()
    await expect(page.getByText(/Fleet Upgrade Completion:/i)).toBeVisible()

    // Verify Pause Fleet and Abort buttons are present in the active dashboard
    await expect(page.getByRole('button', { name: /Pause Fleet/i })).toBeVisible()
    await expect(page.getByRole('button', { name: /Abort/i })).toBeVisible()
  })

  test('deselecting a device to update does not get reselected after polling intervals', async ({ page }) => {
    await page.getByRole('button', { name: /Rolling Fleet Upgrade/i }).click()
    await expect(page.getByText(/Multi-Node Fleet Rolling Orchestration/i)).toBeVisible()

    // Verify initial selection shows all 3 hosts selected
    await expect(page.getByText('(3 of 3 selected)')).toBeVisible()

    // Click on k8s-control-01 to deselect it
    await page.getByText('k8s-control-01').click()

    // Verify selection count decremented to 2 of 3
    await expect(page.getByText('(2 of 3 selected)')).toBeVisible()

    // Wait 4 seconds (longer than the 3000ms polling interval of useJobs)
    await page.waitForTimeout(4000)

    // Verify it is STILL 2 of 3 selected and did not get reselected
    await expect(page.getByText('(2 of 3 selected)')).toBeVisible()
  })
})

