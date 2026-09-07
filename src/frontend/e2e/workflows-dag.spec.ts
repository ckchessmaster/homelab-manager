import { test, expect } from '@playwright/test'
import { setupMockApi } from './fixtures/mockApi'

test.describe('Workflows & Visual DAG Canvas', () => {
  test.beforeEach(async ({ page }) => {
    await setupMockApi(page)
    await page.goto('/')
    await page.getByRole('button', { name: /Workflows & DAGs/i }).click()
  })

  test('displays workflows dashboard and metrics', async ({ page }) => {
    await expect(page.getByText(/DAG Update Orchestration Engine/i)).toBeVisible()
    await expect(page.getByRole('button', { name: /Launch Workflow/i })).toBeVisible()
    await expect(page.getByRole('button', { name: /Rolling Fleet Upgrade/i })).toBeVisible()
  })

  test('opens Parameterized Workflow Launcher and renders interactive DAG canvas preview', async ({ page }) => {
    await page.getByRole('button', { name: /Launch Workflow/i }).click()

    // Slide-over modal should open
    await expect(page.getByText(/Parameterized Workflow Launcher/i)).toBeVisible()

    // Host selector section
    await expect(page.getByText('Target Managed Host', { exact: true })).toBeVisible()

    // Select a target host from dropdown
    const hostSelect = page.locator('select').filter({ hasText: /Choose Target Managed Host/i })
    await hostSelect.selectOption({ index: 1 })

    // Live DAG Preview container should render
    await expect(page.getByText(/Real-Time Execution Graph Preview/i)).toBeVisible()

    // ReactFlow canvas container should be mounted
    await expect(page.locator('.react-flow')).toBeVisible()

    // Verify key workflow nodes are rendered in the DAG
    await expect(page.locator('.react-flow__node').first()).toBeVisible()

    // Verify launch button
    const launchBtn = page.getByRole('button', { name: /Execute Workflow DAG/i })
    await expect(launchBtn).toBeVisible()
    await launchBtn.click()

    // Launcher should close upon launch
    await expect(page.getByText(/Parameterized Workflow Launcher/i)).not.toBeVisible()
  })
})
