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

  test('blocks unauthenticated visitors and prompts for API key in api_key mode', async ({ page }) => {
    // Clear any auth flags to simulate clean unauthenticated visitor in api_key mode
    await page.addInitScript(() => {
      localStorage.removeItem('cp_auth_bypass')
      localStorage.removeItem('cp_bypass_role')
      localStorage.removeItem('cp_api_key')
      localStorage.setItem('cp_auth_mode', 'api_key')
    })
    await page.goto('/')

    // Auth gate screen must be visible
    await expect(page.getByRole('heading', { name: /ControlPlane API Key Sign In/i })).toBeVisible()
    await expect(page.getByRole('button', { name: /Connect & Sign In/i })).toBeVisible()

    // Protected dashboard elements MUST NOT be visible
    await expect(page.getByText('Total Managed Nodes')).not.toBeVisible()
    await expect(page.getByRole('table')).not.toBeVisible()

    // Submit with empty key -> error message
    await page.getByRole('button', { name: /Connect & Sign In/i }).click()
    await expect(page.getByText(/Please enter a valid ControlPlane API key/i)).toBeVisible()

    // Enter valid API key and submit
    await page.getByPlaceholder(/Enter X-ControlPlane-Key/i).fill('secret-test-token-789')
    await page.getByRole('button', { name: /Connect & Sign In/i }).click()

    // Now authenticated: dashboard renders
    await expect(page.getByText('Total Managed Nodes')).toBeVisible()
    await expect(page.getByRole('table')).toBeVisible()

    // Header displays API Key widget (Full Admin) and NOT OIDC profile dropdown
    await expect(page.getByText('API Key')).toBeVisible()
    await expect(page.getByText('Full Admin')).toBeVisible()
    await expect(page.getByRole('button', { name: /DA Dev Admin/i })).not.toBeVisible()

    // Disconnect via API Key menu
    await page.getByRole('button', { name: /API Key/i }).click()
    await page.getByRole('button', { name: /Disconnect \/ Lock/i }).click()

    // Returns to AuthGatePage
    await expect(page.getByRole('heading', { name: /ControlPlane API Key Sign In/i })).toBeVisible()
    await expect(page.getByText('Total Managed Nodes')).not.toBeVisible()
  })
})
