import { defineConfig, devices } from '@playwright/test'

const libPath = '/home/ckingdon/.cache/playwright-libs/extracted/usr/lib/x86_64-linux-gnu'
if (!process.env.LD_LIBRARY_PATH?.includes(libPath)) {
  process.env.LD_LIBRARY_PATH = `${libPath}:${process.env.LD_LIBRARY_PATH || ''}`
}

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: [
    ['list'],
    ['html', { open: 'never' }],
  ],
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    colorScheme: 'dark',
    viewport: { width: 1280, height: 800 },
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    command: 'npm run dev',
    port: 5173,
    reuseExistingServer: !process.env.CI,
    timeout: 30000,
  },
})
