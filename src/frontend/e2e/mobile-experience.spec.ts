import { test, expect } from '@playwright/test'
import { setupMockApi } from './fixtures/mockApi'

test.describe('Mobile Experience & Responsive Shell', () => {
  test.beforeEach(async ({ page }) => {
    await setupMockApi(page)
    await page.goto('/')
  })

  test('renders mobile bottom navigation and switches primary tabs', async ({ page, isMobile }) => {
    if (!isMobile) {
      test.skip()
    }

    // Verify bottom nav bar is visible on mobile
    const bottomNav = page.getByRole('navigation', { name: /Mobile Navigation Bar/i })
    await expect(bottomNav).toBeVisible()

    // 1. Initial tab: Hosts
    await expect(page.getByTestId('host-mobile-cards')).toBeVisible()

    // 2. Switch to Workloads
    await bottomNav.getByRole('button', { name: 'Workloads' }).click()
    await expect(page.getByRole('heading', { name: 'Applications & Workloads' })).toBeVisible()

    // 3. Switch to Discovery
    await bottomNav.getByRole('button', { name: 'Discovery' }).click()
    await expect(page.getByText('Service Discovery & Adoption Hub')).toBeVisible()

    // 4. Switch to Workflows
    await bottomNav.getByRole('button', { name: 'Workflows' }).click()
    await expect(page.getByText('DAG Update Orchestration Engine')).toBeVisible()

    // 5. Switch back to Hosts
    await bottomNav.getByRole('button', { name: 'Hosts' }).click()
    await expect(page.getByTestId('host-mobile-cards')).toBeVisible()
  })

  test('opens More drawer to navigate to secondary views', async ({ page, isMobile }) => {
    if (!isMobile) {
      test.skip()
    }

    const bottomNav = page.getByRole('navigation', { name: /Mobile Navigation Bar/i })
    await bottomNav.getByRole('button', { name: /More views and options/i }).click()

    // Verify More sheet opens
    await expect(page.getByText('ControlPlane Navigation')).toBeVisible()

    // Navigate to Infrastructure Adapters
    await page.getByRole('button', { name: /Infrastructure Adapters/i }).click()
    await expect(page.getByRole('heading', { name: /Infrastructure Adapters/i })).toBeVisible()

    // Open More sheet again and navigate to System & Settings
    await bottomNav.getByRole('button', { name: /More views and options/i }).click()
    await page.getByRole('button', { name: /System & Settings/i }).click()
    await expect(page.getByRole('heading', { name: /System & Settings/i })).toBeVisible()
  })

  test('interacts with mobile host cards and opens host inspector sheet', async ({ page, isMobile }) => {
    if (!isMobile) {
      test.skip()
    }

    const cards = page.getByTestId('host-mobile-cards')
    await expect(cards).toBeVisible()

    const hostCard = cards.getByText('k8s-control-01')
    await expect(hostCard).toBeVisible()

    // Tap on host card to open inspector sheet
    await hostCard.click()
    await expect(page.getByRole('heading', { name: 'k8s-control-01' })).toBeVisible()

    // Close the sheet
    const closeBtn = page.getByRole('button', { name: /Close dialog/i })
    await closeBtn.click()
    await expect(page.getByRole('heading', { name: 'k8s-control-01' })).not.toBeVisible()
  })
})
