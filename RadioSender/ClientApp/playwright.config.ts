import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './tests',
  workers: 1,
  timeout: 45000,
  use: {
    baseURL: 'http://127.0.0.1:18082',
    viewport: { width: 1600, height: 1000 },
    launchOptions: { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH },
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  webServer: {
    command:
      'dotnet run --no-build --project ../RadioSender.csproj -- --Desktop:Enabled=false --urls http://127.0.0.1:18082',
    url: 'http://127.0.0.1:18082/healthz',
    reuseExistingServer: false,
    timeout: 30000,
  },
})
