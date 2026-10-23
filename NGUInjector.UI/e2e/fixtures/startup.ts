import { chromium, expect, type FullConfig } from '@playwright/test';

export default async function startup(config: FullConfig): Promise<void> {
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage({ baseURL: config.projects[0].use.baseURL });
    // Import without mounting: compile/fetch the full graph without native IPC or polling.
    await page.route(/\/$/, route => route.fulfill({ contentType: 'text/html', body: `<!doctype html><html><body>
      <script type="module">
        import '/src/App.svelte';
        import '/e2e/fixtures/NguAllocationHarness.svelte';
        import '/e2e/fixtures/PlanProgressHarness.svelte';
        import '/e2e/fixtures/settings-cas.ts';
        document.documentElement.dataset.ready = 'true';
      </script>
    </body></html>` }));
    await page.goto('/');
    await expect(page.locator('html')).toHaveAttribute('data-ready', 'true');
  } finally {
    await browser.close();
  }
}
