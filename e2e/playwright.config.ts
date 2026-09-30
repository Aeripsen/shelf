import { defineConfig, devices } from '@playwright/test'

// Runs against the full docker compose stack: storefront on 3000, which proxies /api to the real API.
// Every run keeps a video, a trace and a screenshot of each named step, so a CI run doubles as a recorded demo.
// CI uploads playwright-report/ and demo/ as artifacts; docs/demo in the repo holds a copy from one green run.
export default defineConfig({
  testDir: './tests',
  timeout: 60_000,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    ...devices['Desktop Chrome'],
    baseURL: process.env.BASE_URL ?? 'http://localhost:3000',
    viewport: { width: 1280, height: 800 },
    trace: 'on',
    video: { mode: 'on', size: { width: 1280, height: 800 } },
    screenshot: 'on',
  },
  projects: [{ name: 'chromium' }],
})
