import { defineConfig, devices } from '@playwright/test';

/**
 * Starts the real API (with a real Postgres — see docker-compose.yml / CI's Postgres service)
 * and the real Angular dev server, then drives the app through a browser exactly like a user.
 * No mocking — this is the one place the whole stack is proven to work together.
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: [
    {
      // --no-launch-profile: launchSettings.json's own environmentVariables block (which sets
      // ASPNETCORE_ENVIRONMENT=Development) would otherwise apply regardless of what's already
      // in the process environment, silently overriding the explicit "Testing" override below.
      command: 'dotnet run --no-launch-profile --project ../src/Omnichannel.Api --urls http://localhost:5068',
      url: 'http://localhost:5068/health/live',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      stdout: 'pipe',
      stderr: 'pipe',
      env: {
        // Deterministic across local/CI, same reasoning as ApiTests/SecurityTests's
        // TestWebApplicationFactory: loads appsettings.Testing.json (committed, non-secret
        // signing key) instead of depending on local dotnet user-secrets existing.
        ASPNETCORE_ENVIRONMENT: 'Testing',
        // E2E must not depend on a real external email provider — slow, non-deterministic, and
        // would hit real send limits. A whitespace-only host makes SmtpEmailSender skip sending
        // (see its IsNullOrWhiteSpace early-return). Deliberately not an empty string: Windows
        // child-process env blocks silently drop empty-string variables instead of setting them.
        Smtp__Host: ' ',
      },
    },
    {
      command: 'npm run start --prefix ../web',
      url: 'http://localhost:4200',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
  ],
});
