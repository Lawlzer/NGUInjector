import { expect, test, type Page } from '@playwright/test';

const longestPath = `C:\\NGU saves\\${'long-directory-name-'.repeat(12)}\\longest-known-save.txt`;
const behind = {
  version: 1, status: 'behind', message: 'Loaded save has less total playtime than a decoded save.',
  current: { totalPlaytimeSeconds: '360000', rebirth: '99', rebirthSeconds: '7200' },
  longest: { path: longestPath, sha256: 'a'.repeat(64), totalPlaytimeSeconds: '720000', rebirth: '98', rebirthSeconds: '3600' },
  testActive: false, scanComplete: true, errors: []
};

type Fixture = { saveSafety?: unknown; stale: boolean; calls: string[] };

async function openSafety(page: Page, saveSafety?: unknown) {
  await page.addInitScript(data => {
    const state = window as typeof window & { saveSafetyFixture: Fixture };
    state.saveSafetyFixture = { saveSafety: data, stale: false, calls: [] };
    let runtimeRevision = 0;
    Object.assign(window, { __TAURI_INTERNALS__: { invoke: async (command: string) => {
      state.saveSafetyFixture.calls.push(command);
      if (command === 'get_ngui_state') {
        return { runtime_dir: 'C:\\Fixture', marker_enabled: true, connected: true,
          settings_json: '{}', settings_revision: 'b'.repeat(64), protocol: {}, profiles: [], plans: [], logs: [], command_results: [],
          runtime: { externalUiEnabled: true, status: 'running', protocolVersion: 3, startedAt: 'save-safety-session',
            runtimeRevision: ++runtimeRevision,
            updatedAt: new Date(Date.now() - (state.saveSafetyFixture.stale ? 10000 : 0)).toISOString(),
            saveSafety: state.saveSafetyFixture.saveSafety } };
      }
      if (command === 'tail_log') return '';
      throw new Error(`Unexpected IPC: ${command}`);
    } } });
    localStorage.clear();
  }, saveSafety);
  await page.goto('/');
  await expect(page.getByText('Connected', { exact: true })).toBeVisible();
  return page.getByRole('alert', { name: 'Save safety' });
}

async function updateSafety(page: Page, saveSafety: unknown, stale = false) {
  await page.evaluate(({ data, stale }) => {
    const state = window as typeof window & { saveSafetyFixture: Fixture };
    state.saveSafetyFixture.saveSafety = data;
    state.saveSafetyFixture.stale = stale;
  }, { data: saveSafety, stale });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
}

test('older loaded save stays prominent above navigation on every tab without writing or loading saves', async ({ page }) => {
  const alert = await openSafety(page, behind);
  await expect(alert.getByRole('heading', { name: 'Loaded save is behind' })).toBeVisible();
  await expect(alert).toContainText('Loaded save: 100 h / Longest known: 200 h');
  await expect(alert).toContainText(longestPath);
  await expect(alert).toHaveClass(/critical/);
  await expect(alert).toHaveCSS('background-color', 'rgb(71, 28, 37)');
  const heading = alert.getByRole('heading');
  expect(await heading.evaluate(node => parseFloat(getComputedStyle(node).fontSize))).toBeGreaterThanOrEqual(26);
  const nav = page.getByRole('navigation', { name: 'Primary' });
  expect(await alert.evaluate(node => !!(node.compareDocumentPosition(document.querySelector('nav')!) & Node.DOCUMENT_POSITION_FOLLOWING))).toBe(true);
  const alertBox = await alert.boundingBox();
  const navBox = await nav.boundingBox();
  expect(alertBox!.y + alertBox!.height).toBeLessThanOrEqual(navBox!.y);
  await expect(alert.locator('button, input, select, a, details')).toHaveCount(0);

  for (const tab of ['Plan & Rebirth', 'Presets', 'Adventure', 'Misc', 'Logs', 'Advanced', 'Dashboard']) {
    await nav.getByRole('button', { name: tab, exact: true }).click();
    await expect(alert).toBeVisible();
    await expect(alert).toContainText('Loaded save is behind');
    expect(await alert.evaluate(node => node.scrollWidth <= node.clientWidth)).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  }
  expect(await page.evaluate(() => (window as typeof window & { saveSafetyFixture: Fixture }).saveSafetyFixture.calls.filter(command => !['get_ngui_state', 'tail_log'].includes(command)))).toEqual([]);
});

test('connected older backend keeps a visible amber status without adding an alert', async ({ page }) => {
  await openSafety(page);
  const warning = page.getByRole('status', { name: 'Save safety' });
  await expect(warning).toBeVisible();
  await expect(warning).toHaveText('Save safety unavailable Update and reconnect the injector to check the loaded save.');
  await expect(warning).toHaveCSS('background-color', 'rgb(48, 39, 25)');
  await expect(page.getByRole('alert')).toHaveCount(0);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(warning).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Adventure automation', exact: true })).toBeVisible();
  await updateSafety(page, null);
  await expect(warning).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await updateSafety(page, { ...behind, status: 'unknown', message: 'Save scan failed.', current: null });
  await expect(warning).toHaveCount(0);
  await expect(page.getByRole('alert', { name: 'Save safety' })).toBeVisible();
  await expect(page.getByRole('alert')).toContainText('Save safety unknown');
});

test('scan failure and testing never hide a recovery warning, then fresh verification clears it', async ({ page }) => {
  const alert = await openSafety(page, { ...behind, status: 'unknown', message: 'Save scan failed.', current: null, scanComplete: false,
    errors: ['Cannot decode damaged-save.txt.'] });
  await expect(alert).toContainText('Save safety unknown');
  await expect(alert).toContainText('Loaded save: Unavailable / Longest known: 200 h');
  await expect(alert).toContainText('Cannot decode damaged-save.txt.');
  await expect(alert).not.toHaveClass(/critical/);

  const verified = { ...behind, status: 'verified', message: 'Loaded save verified.', current: behind.longest };
  await updateSafety(page, { ...verified, testActive: true });
  await expect(alert).toContainText('Save testing active');
  await expect(alert).not.toContainText('Loaded save verified.');
  await updateSafety(page, { ...behind, status: 'recoveryRequired', testActive: true, message: 'Could not verify restored save.' });
  await expect(alert).toHaveClass(/critical/);
  await expect(alert).toContainText('Save recovery required');
  await expect(alert).toContainText('Save testing is active.');
  await expect(alert).toContainText('Preserve your saves and resolve recovery before continuing.');
  await updateSafety(page, { ...behind, status: 'recoveryRequired', testActive: true }, true);
  await expect(page.locator('.app-header').getByText('Disconnected', { exact: true })).toBeVisible();
  await expect(alert).toHaveClass(/critical/);
  await expect(alert).toContainText('current safety is unknown.');
  await updateSafety(page, verified);
  await expect(page.getByText('Connected', { exact: true })).toBeVisible();
  await expect(alert).toHaveCount(0);
  expect(await page.evaluate(() => (window as typeof window & { saveSafetyFixture: Fixture }).saveSafetyFixture.calls.filter(command => !['get_ngui_state', 'tail_log'].includes(command)))).toEqual([]);
});

test('verified telemetry becomes a warning when the heartbeat is stale', async ({ page }) => {
  const verified = { ...behind, status: 'verified', message: 'Loaded save verified.', current: behind.longest };
  const alert = await openSafety(page, verified);
  await expect(alert).toHaveCount(0);
  await updateSafety(page, verified, true);
  await expect(page.locator('.app-header').getByText('Disconnected', { exact: true })).toBeVisible();
  await expect(alert).toBeVisible();
  await expect(alert).toContainText('Save safety unknown');
  await expect(alert).toContainText('current safety is unknown.');
  await expect(alert).not.toContainText('Loaded save verified.');
  await updateSafety(page, verified);
  await expect(alert).toHaveCount(0);
});

for (const key of ['current', 'longest'] as const) {
  test(`${key} impossible rebirth timing or Int64 overflow cannot clear the warning`, async ({ page }) => {
    const verified = { ...behind, status: 'verified', message: 'Loaded save verified.', current: behind.longest };
    const invalidClock = { ...verified[key], totalPlaytimeSeconds: '100', rebirthSeconds: '200' };
    const alert = await openSafety(page, { ...verified, [key]: invalidClock });
    await expect(alert).toBeVisible();
    await expect(alert).toContainText('Save safety unknown');
    await expect(alert).toContainText('Save metadata is invalid or inconsistent.');
    await expect(alert).not.toContainText('Loaded save verified.');
    await updateSafety(page, { ...verified, [key]: { ...verified[key], rebirth: '9223372036854775808' } });
    await expect(alert).toBeVisible();
    await expect(alert).toContainText('Save metadata is invalid or inconsistent.');
    await updateSafety(page, { ...verified, [key]: { ...verified[key], rebirth: '9223372036854775807' } });
    await expect(alert).toHaveCount(0);
  });
}

test('ambiguous saves remain visible and decoded playtime overrides a false verified status', async ({ page }) => {
  const verified = { ...behind, status: 'verified', message: 'Loaded save verified.', current: behind.longest };
  const alert = await openSafety(page, { ...verified, ambiguousLongest: true });
  await expect(alert).toBeVisible();
  await expect(alert).not.toHaveClass(/critical/);
  await expect(alert).toContainText('Different saves tie for longest playtime.');
  await updateSafety(page, { ...verified, status: 'unknown', message: '', ambiguousLongest: true });
  await expect(alert).toContainText('Save safety unknown');
  await expect(alert).toContainText('Review the saved branches.');
  await updateSafety(page, { ...verified, current: behind.current, ambiguousLongest: true });
  await expect(alert).toHaveClass(/critical/);
  await expect(alert).toContainText('Loaded save is behind');
  await expect(alert).toContainText('Loaded save: 100 h / Longest known: 200 h');
  await expect(alert).toContainText('Different saves tie for longest playtime.');
  await expect(alert).not.toContainText('Loaded save verified.');
  await expect(alert.locator('button, input, select, a, details')).toHaveCount(0);
  expect(await alert.evaluate(node => node.scrollWidth <= node.clientWidth)).toBe(true);
});

test('pending files and malformed known-file counts contradict a verified complete scan', async ({ page }) => {
  const verified = { ...behind, status: 'verified', message: 'Loaded save verified.', current: behind.longest, scanComplete: true };
  const alert = await openSafety(page, { ...verified, pendingFiles: 1, knownFiles: 1 });
  await expect(alert).toContainText('Save scan counts are inconsistent or incomplete.');
  await expect(alert).toContainText('Save scan is incomplete.');
  await expect(alert).not.toContainText('Loaded save verified.');
  await updateSafety(page, { ...verified, pendingFiles: 0, knownFiles: '1' });
  await expect(alert).toBeVisible();
  await expect(alert).toContainText('Save scan counts are inconsistent or incomplete.');
  await updateSafety(page, { ...verified, pendingFiles: 0, knownFiles: 1, ambiguousLongest: false });
  await expect(alert).toHaveCount(0);
  expect(await page.evaluate(() => (window as typeof window & { saveSafetyFixture: Fixture }).saveSafetyFixture.calls.filter(command => !['get_ngui_state', 'tail_log'].includes(command)))).toEqual([]);
});
