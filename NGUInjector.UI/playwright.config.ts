import { defineConfig } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const port = Number(process.env.NGUI_TEST_PORT ?? 1420);
const baseURL = `http://127.0.0.1:${port}`;

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  outputDir: process.env.NGUI_TEST_OUTPUT ?? 'test-results',
  globalSetup: './e2e/fixtures/startup.ts',
  use: { baseURL, trace: 'retain-on-failure' },
  projects: [
    { name: 'desktop', use: { viewport: { width: 1180, height: 760 } } },
    { name: 'mobile', use: { viewport: { width: 390, height: 844 } } }
  ],
  webServer: {
    command: `npm run dev -- --port ${port} --strictPort`,
    env: { NGUI_TEST_VITE_CACHE: join(tmpdir(), `ngui-playwright-${randomUUID()}`) },
    // Wait for the application entry to transform, not just Vite's uncompiled HTML.
    url: `${baseURL}/src/main.ts`,
    reuseExistingServer: false
  }
});
