import { test, expect } from '@playwright/test'
import { setupMockApi } from './fixtures/mockApi'

test.describe('Authentication & RBAC User Experience', () => {
  test.beforeEach(async ({ page }) => {
    await setupMockApi(page)
    await page.goto('/')
  })

  test('displays authenticated user avatar and profile details in header', async ({ page }) => {
    // Check avatar button with initials 'DA' (Dev Admin)
    const profileBtn = page.getByRole('button', { name: /DA Dev Admin Admin/i })
    await expect(profileBtn).toBeVisible()

    // Open user profile dropdown
    await profileBtn.click()

    // Assert dropdown contents
    await expect(page.getByText('admin@controlplane.local')).toBeVisible()
    await expect(page.getByText(/Dev Bypass/i)).toBeVisible()
    await expect(page.getByText(/Running in local dev\/standby bypass mode/i)).toBeVisible()
  })

  test('enforces RBAC role gating when switching roles in dev mode', async ({ page }) => {
    // Initially as Admin, Add Host button should be enabled and clickable
    const addHostBtn = page.getByRole('button', { name: /Add Host/i })
    await expect(addHostBtn).toBeEnabled()

    // Open dropdown and switch simulated role to 'Viewer'
    const profileBtn = page.getByRole('button', { name: /DA Dev Admin/i })
    await profileBtn.click()

    // Click 'Viewer' role pill in dev simulator
    await page.getByRole('button', { name: 'Viewer', exact: true }).click()

    // Close menu by clicking outside
    await page.mouse.click(10, 10)

    // With Viewer role, both Adopt Server and Add Host containers should have disabled opacity styling
    const gatedWrappers = page.locator('div[title*="Requires Admin permission"]')
    await expect(gatedWrappers).toHaveCount(2)
    await expect(gatedWrappers.first()).toBeVisible()

    // Switch back to Admin
    await page.getByRole('button', { name: /DA Dev Admin/i }).click()
    await page.getByRole('button', { name: 'Admin', exact: true }).click()
    await page.mouse.click(10, 10)

    // Verify Add Host is now enabled again
    await expect(addHostBtn).toBeEnabled()
  })
})
