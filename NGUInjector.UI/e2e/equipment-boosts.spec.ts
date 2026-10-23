import { expect, test, type Page } from '@playwright/test';

type FixtureWindow = typeof window & {
  __boostSettings: Record<string, unknown>;
  __boostPatches: Record<string, unknown>[];
  __boostTelemetry: Record<string, unknown>;
  __boostConnected: boolean;
};
const items = [
  { id: 100, name: 'Helmet', copies: 2, level: 100, fullyBoosted: false },
  { id: 101, name: 'Sword', copies: 1, level: 20, fullyBoosted: true },
  { id: 102, name: 'Red Heart', copies: 1, level: 0, fullyBoosted: false }
];

async function openMisc(page: Page, connected = true, available = true) {
  // Native IPC is the boundary; UI, polling, validation, and autosave remain real.
  await page.addInitScript(({ items, connected, available }) => {
    const fixture = window as FixtureWindow;
    fixture.__boostSettings = { _customAutoBoostEnabled: true, _priorityBoosts: [999, 101], _boostBlacklist: [888, 102], Future: { keep: 42 } };
    fixture.__boostPatches = [];
    fixture.__boostConnected = connected;
    fixture.__boostTelemetry = { available, items, ...(available ? {} : { reason: 'Inventory is loading.' }) };
    Object.assign(window, { __TAURI_INTERNALS__: { invoke: async (command: string, args: Record<string, unknown> = {}) => {
      const casPath = '/e2e/fixtures/settings-cas.ts';
      const cas = await import(casPath);
      if (command === 'get_ngui_state') return {
        runtime_dir: 'C:\\Fixture\\NGUInjector', connected: fixture.__boostConnected, marker_enabled: true, protocol: {},
        ...await cas.settingsSnapshot(fixture.__boostSettings),
        runtime: { updatedAt: new Date().toISOString(), externalUiEnabled: true, status: 'running', protocolVersion: 3, settingsCasVersion: 1, supportedCommands: ['applySettingsPatch'], equipmentBoosts: structuredClone(fixture.__boostTelemetry) },
        profiles: [], plans: [], logs: [], command_results: []
      };
      if (command === 'save_settings_patch_json') {
        const receipt = await cas.commitSettings(fixture.__boostSettings, args);
        fixture.__boostPatches.push(cas.settingsPatch(args));
        return receipt;
      }
      throw new Error(`Unexpected native invocation: ${command}`);
    } } });
    localStorage.clear();
  }, { items, connected, available });
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Misc', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Equipment boost priorities' })).toBeVisible();
}

test('Misc loads one row per owned item without writing settings', async ({ page }) => {
  await openMisc(page);
  const list = page.getByRole('list', { name: 'Owned equipment' });
  await expect(list.getByRole('listitem')).toHaveCount(3);
  await expect(list.getByRole('listitem').first()).toContainText('Sword');
  await expect(list.getByText('Helmet', { exact: true })).toHaveCount(1);
  await expect(list).toContainText('#100 / Level 100 / 2 copies / Needs boosts');
  await expect(list).toContainText('#101 / Level 20 / 1 copy / Fully boosted');
  await expect(list.getByRole('checkbox', { name: 'Boost Red Heart', exact: true })).not.toBeChecked();
  await page.waitForTimeout(700);
  expect(await page.evaluate(() => (window as FixtureWindow).__boostPatches)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('priority changes autosave while preserving hidden item priorities', async ({ page }) => {
  await openMisc(page);
  await page.getByRole('combobox', { name: 'Boost priority for Helmet', exact: true }).selectOption({ value: '0' });
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__boostSettings._priorityBoosts)).toEqual([100, 999, 101]);
  await page.getByRole('combobox', { name: 'Boost priority for Red Heart', exact: true }).selectOption({ value: '2' });
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__boostSettings._priorityBoosts)).toEqual([100, 999, 102, 101]);
  await page.getByRole('combobox', { name: 'Boost priority for Sword', exact: true }).selectOption({ value: '0' });
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__boostSettings._priorityBoosts)).toEqual([101, 100, 999, 102]);
  await page.getByRole('combobox', { name: 'Boost priority for Sword', exact: true }).selectOption({ value: '3' });
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__boostSettings._priorityBoosts)).toEqual([100, 999, 102, 101]);
  await page.getByRole('combobox', { name: 'Boost priority for Red Heart', exact: true }).selectOption({ value: '' });
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__boostSettings._priorityBoosts)).toEqual([100, 999, 101]);
  expect(await page.evaluate(() => (window as FixtureWindow).__boostPatches.every(patch => Object.keys(patch).join() === '_priorityBoosts'))).toBe(true);
  expect(await page.evaluate(() => (window as FixtureWindow).__boostSettings.Future)).toEqual({ keep: 42 });
});

test('disabling an item persists without removing its row or priority', async ({ page }) => {
  await openMisc(page);
  await page.getByRole('checkbox', { name: 'Boost Sword', exact: true }).uncheck();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__boostSettings._boostBlacklist)).toEqual([888, 102, 101]);
  await expect(page.getByRole('combobox', { name: 'Boost priority for Sword', exact: true })).toHaveValue('1');
  await page.getByRole('checkbox', { name: 'Boost Red Heart', exact: true }).check();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__boostSettings._boostBlacklist)).toEqual([888, 101]);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Misc', exact: true }).click();
  await expect(page.getByRole('checkbox', { name: 'Boost Sword', exact: true })).not.toBeChecked();
  await expect(page.getByRole('list', { name: 'Owned equipment' }).getByRole('listitem')).toHaveCount(3);
  expect(await page.evaluate(() => (window as FixtureWindow).__boostSettings._priorityBoosts)).toEqual([999, 101]);
});

test('polling and reopening refresh equipment without deleting saved preferences', async ({ page }) => {
  await openMisc(page);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await page.evaluate(() => {
    (window as FixtureWindow).__boostTelemetry = { available: true, items: [{ id: 103, name: 'New Boots', copies: 3, level: 1, fullyBoosted: false }] };
  });
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Misc', exact: true }).click();
  const list = page.getByRole('list', { name: 'Owned equipment' });
  await expect(list.getByRole('listitem')).toHaveCount(1);
  await expect(list).toContainText('New Boots');
  await page.evaluate(() => { (window as FixtureWindow).__boostTelemetry = { available: true, items: [] }; });
  await expect(page.getByRole('article', { name: 'Equipment boost priorities' })).toContainText('No equipment needs levels or boosts.');
  expect(await page.evaluate(() => (window as FixtureWindow).__boostPatches)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__boostSettings._priorityBoosts)).toEqual([999, 101]);
  expect(await page.evaluate(() => (window as FixtureWindow).__boostSettings._boostBlacklist)).toEqual([888, 102]);
});

test('completed items disappear on refresh without deleting saved preferences', async ({ page }) => {
  await openMisc(page);
  const list = page.getByRole('list', { name: 'Owned equipment' });
  await expect(list.getByRole('listitem')).toHaveCount(3);
  await page.evaluate(() => {
    (window as FixtureWindow).__boostTelemetry = { available: true, items: [
      { id: 100, name: 'Helmet', copies: 2, level: 100, fullyBoosted: false },
      { id: 101, name: 'Sword', copies: 1, level: 100, fullyBoosted: true },
      { id: 102, name: 'Red Heart', copies: 1, level: 99, fullyBoosted: true }
    ] };
  });
  await expect(list.getByRole('listitem')).toHaveCount(2);
  await expect(list.getByText('Sword', { exact: true })).toHaveCount(0);
  await expect(list.getByText('Helmet', { exact: true })).toBeVisible();
  await expect(list.getByText('Red Heart', { exact: true })).toBeVisible();
  expect(await page.evaluate(() => (window as FixtureWindow).__boostPatches)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__boostSettings._priorityBoosts)).toEqual([999, 101]);
  expect(await page.evaluate(() => (window as FixtureWindow).__boostSettings._boostBlacklist)).toEqual([888, 102]);
});

test('unavailable and disconnected inventory never exposes stale controls', async ({ page }) => {
  await openMisc(page, true, false);
  const editor = page.getByRole('article', { name: 'Equipment boost priorities' });
  await expect(editor).toContainText('Inventory is loading.');
  await expect(editor.getByRole('combobox')).toHaveCount(0);
  await page.evaluate(() => { (window as FixtureWindow).__boostConnected = false; });
  await expect(editor).toContainText('Connect to load owned equipment.');
  await expect(editor.getByRole('checkbox')).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__boostPatches)).toEqual([]);
});
