import { expect, test, type Page } from '@playwright/test';

const snapshot = () => ({
  available: true, strategy: 'weighted-ngu-buffered-cap', capBasis: 'last-allocation-pass',
  energyWeights: [0, 0, 0, 0, 2, 0, 1, 0, 0], magicWeights: [1, 0, 0, 0, 0, 0, 0],
  energyAppliedWeights: [0, 0, 0, 0, 2, 0, 1, 0, 0], magicAppliedWeights: [1, 0, 0, 0, 0, 0, 0],
  energyWeightsApplied: true, magicWeightsApplied: true,
  energyTargets: ['Augments', 'Wandoos', 'Respawn', 'Gold', 'Adventure Alpha', 'Power Alpha', 'Drop Chance', 'Magic NGU', 'PP'].map((name, index) =>
    ({ index, name, weight: index === 4 ? 2 : index === 6 ? 1 : 0, allocated: index === 4 ? '9223372036854775805' : index === 6 ? '500' : '0', bufferedCap: '9223372036854775806', belowBufferedCap: true })),
  magicTargets: ['Yggdrasil', 'EXP', 'Power Beta', 'Number', 'Time Machine', 'Energy NGU', 'Adventure Beta'].map((name, index) =>
    ({ index, name, weight: index === 0 ? 1 : 0, allocated: index === 0 ? '100' : '0', bufferedCap: null, belowBufferedCap: null }))
});
type FixtureWindow = Window & {
  __nativeCalls: string[];
  __updateNgu: (data: Record<string, unknown>, plan?: string, connected?: boolean) => void;
};

async function mountAllocation(page: Page) {
  await page.route('http://127.0.0.1:1420/', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><head><link rel="stylesheet" href="/src/styles.css"></head><body><main class="shell" id="fixture"></main></body></html>' }));
  await page.goto('/');
  const main = await (await page.request.get('/src/main.ts')).text();
  const runtimePath = main.match(/import\s*\{\s*mount\s*\}\s*from\s*["']([^"']+)["']/)?.[1];
  if (!runtimePath) throw new Error('Could not find the served Svelte runtime.');
  await page.evaluate(async ({ runtimePath, data }) => {
    const { mount } = await import(runtimePath);
    const fixturePath = '/e2e/fixtures/NguAllocationHarness.svelte';
    const { default: Fixture } = await import(fixturePath);
    const state = window as unknown as FixtureWindow;
    state.__nativeCalls = [];
    Object.assign(state, { __TAURI_INTERNALS__: { invoke: async (command: string) => {
      state.__nativeCalls.push(command);
      throw new Error(`Unexpected native call: ${command}`);
    } } });
    state.__updateNgu = mount(Fixture, { target: document.getElementById('fixture'), props: { data } }).update;
  }, { runtimePath, data: snapshot() });
  await expect(page.getByRole('region', { name: 'NGU allocation', exact: true })).toBeVisible();
}

test('read-only weights render without a legacy goal, keep exact amounts, and fit mobile', async ({ page }) => {
  await mountAllocation(page);
  const region = page.getByRole('region', { name: 'NGU allocation', exact: true });
  await expect(region.locator('input, select, button')).toHaveCount(0);
  const energy = region.getByRole('list', { name: 'ENERGY NGU targets' });
  await expect(energy.getByRole('listitem')).toHaveCount(2);
  await expect(energy).toContainText('Adventure Alpha weight 2');
  await expect(energy).toContainText('9,223,372.04T');
  await expect(energy.getByRole('listitem').first()).toHaveAttribute('title', 'Adventure Alpha: allocated 9223372036854775805; buffered cap 9223372036854775806');
  await expect(region.getByRole('list', { name: 'MAGIC NGU targets' })).toContainText('Yggdrasil weight 1');
  await expect(region.locator('details')).not.toHaveAttribute('open');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await region.getByText('Allocation details', { exact: true }).click();
  await expect(region.locator('pre')).toContainText('"allocated": "9223372036854775805"');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(await page.evaluate(() => (window as unknown as FixtureWindow).__nativeCalls)).toEqual([]);
});

test('blocked weights distinguish desired from last applied and retain nonzero actual rows', async ({ page }) => {
  await mountAllocation(page);
  const data = snapshot();
  data.energyWeightsApplied = false;
  data.energyAppliedWeights = [3, 0, 0, 0, 0, 0, 0, 0, 0];
  data.energyTargets[0].allocated = '1000';
  await page.evaluate(data => (window as unknown as FixtureWindow).__updateNgu(data, 'other'), data);
  await expect(page.getByText('Active plan:', { exact: false })).toContainText('other');
  const energy = page.getByRole('list', { name: 'ENERGY NGU targets' });
  await expect(energy.getByRole('listitem')).toHaveCount(3);
  await expect(energy).toContainText('Augments weight 0 / last applied weight 3');
  await expect(page.getByText('Selected weights not applied;', { exact: false })).toBeVisible();
  await page.evaluate(data => (window as unknown as FixtureWindow).__updateNgu(data), { ...data, energyAppliedWeights: null });
  await expect(energy).toContainText('last applied weight unknown');
  expect(await page.evaluate(() => (window as unknown as FixtureWindow).__nativeCalls)).toEqual([]);
});

test('disconnected and unavailable telemetry hides stale targets without any writes', async ({ page }) => {
  await mountAllocation(page);
  await page.evaluate(data => (window as unknown as FixtureWindow).__updateNgu(data, '24hr', false), snapshot());
  await expect(page.getByRole('status')).toHaveText('Connect to see NGU allocation.');
  await expect(page.getByRole('list')).toHaveCount(0);
  await page.evaluate(() => (window as unknown as FixtureWindow).__updateNgu({ available: false, reason: 'NGU is locked.' }, '24hr', true));
  await expect(page.getByRole('status')).toHaveText('NGU is locked.');
  expect(await page.evaluate(() => (window as unknown as FixtureWindow).__nativeCalls)).toEqual([]);
});
