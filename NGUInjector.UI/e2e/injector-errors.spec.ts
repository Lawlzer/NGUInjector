import { expect, test, type Page } from '@playwright/test';

type ErrorFixture = {
  runtime: Record<string, unknown>;
  readError: string;
  failSave: boolean;
  commands: string[];
  writes: Record<string, unknown>[];
};
type FixtureWindow = typeof window & { errorFixture: ErrorFixture };

async function openErrors(page: Page, runtime: Record<string, unknown> = {}) {
  await page.addInitScript(runtime => {
    const fixture: ErrorFixture = { runtime, readError: '', failSave: false, commands: [], writes: [] };
    (window as FixtureWindow).errorFixture = fixture;
    const settings = { _manageEnergy: true };
    let revision = 0;
    let result: Record<string, unknown> | null = null;
    Object.assign(window, { __TAURI_INTERNALS__: { invoke: async (command: string, args: Record<string, unknown> = {}) => {
      const casPath = '/e2e/fixtures/settings-cas.ts';
      const cas = await import(casPath);
      if (command === 'get_ngui_state') {
        if (fixture.readError) throw new Error(fixture.readError);
        return { runtime_dir: 'C:\\Fixture', marker_enabled: true, connected: true, protocol: {}, ...await cas.settingsSnapshot(settings), profiles: [], plans: [], logs: [{ name: 'inject.log', size: 1 }], command_results: [],
          runtime: { externalUiEnabled: true, status: 'running', protocolVersion: 3, startedAt: 'error-session', runtimeRevision: ++revision, updatedAt: new Date().toISOString(), supportedCommands: ['quickSave', 'applySettingsPatch'], ...fixture.runtime } };
      }
      if (command === 'save_settings_patch_json') {
        fixture.writes.push(cas.settingsPatch(args));
        if (fixture.failSave) throw new Error('Settings file is read-only.');
        return cas.commitSettings(settings, args, 'connected');
      }
      if (command === 'enqueue_command') {
        const envelope = args.envelope as { command: string };
        fixture.commands.push(envelope.command);
        result = { id: 'failed-command', command: envelope.command, success: false, message: 'Game save rejected: save folder is read-only.' };
        return 'failed-command';
      }
      if (command === 'read_command_result') return result;
      if (command === 'tail_log') return 'Diagnostic log';
      throw new Error(`Unexpected IPC: ${command}`);
    } } });
    localStorage.clear();
  }, runtime);
  await page.goto('/');
  if (runtime.status === 'failed') await expect(page.locator('.app-header')).toContainText('Disconnected');
  else await expect(page.getByRole('button', { name: 'Quicksave', exact: true })).toBeEnabled();
  return page.locator('.injector-errors');
}

test('errors stay above every tab with keyboard diagnostics and no automatic actions', async ({ page }) => {
  const panel = await openErrors(page, {
    automationErrors: [{ context: 'Adventure', message: 'Route execution failed.', details: `InvalidOperationException: ${'long-diagnostic-'.repeat(50)}`, firstSeenAt: '2026-10-23T10:00:00Z', lastSeenAt: '2026-10-23T10:01:00Z', count: 9 }],
    resourceAllocation: { state: 'NeedsUser', owner: 'stage:24hr', reason: 'Energy allocation changed outside the injector.' }
  });
  await expect(panel).toHaveAttribute('role', 'alert');
  await expect(panel).toContainText('Adventure: Route execution failed.');
  await expect(panel).toContainText('(9 times)');
  await expect(panel).toContainText('stage:24hr');
  const diagnostics = panel.getByRole('group').first();
  await expect(diagnostics).not.toHaveAttribute('open');
  await panel.getByText('Details', { exact: true }).first().focus();
  await page.keyboard.press('Enter');
  await expect(diagnostics).toHaveAttribute('open', '');
  await expect(panel.getByText(/InvalidOperationException:/)).toBeVisible();
  await page.evaluate(() => { ((window as FixtureWindow).errorFixture.runtime.automationErrors as Record<string, unknown>[])[0].count = 10; });
  await expect(panel).toContainText('(10 times)');
  await expect(diagnostics).toHaveAttribute('open', '');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.keyboard.press('Enter');

  for (const tab of ['Dashboard', 'Plan & Rebirth', 'Presets', 'Adventure', 'Misc', 'Logs', 'Advanced']) {
    await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: tab, exact: true }).click();
    await page.evaluate(() => window.scrollTo(0, 0));
    await expect(panel).toBeInViewport();
    expect(await panel.evaluate(element => !!(element.compareDocumentPosition(document.querySelector('nav.tabs')!) & Node.DOCUMENT_POSITION_FOLLOWING))).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
  await panel.getByRole('button', { name: 'Logs', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Log file' })).toBeFocused();
  expect(await page.evaluate(() => ({ commands: (window as FixtureWindow).errorFixture.commands, writes: (window as FixtureWindow).errorFixture.writes }))).toEqual({ commands: [], writes: [] });
});

test('normal waits are quiet and active domain failures clear only when the backend clears them', async ({ page }) => {
  const panel = await openErrors(page, {
    resourceAllocation: { state: 'Waiting', reason: 'Waiting for combat prep.' },
    execution: { domains: { Route: { state: 'Observed', waitingReason: 'Waiting for next spawn.' }, Energy: { state: 'Waiting', waitingReason: 'Waiting for a lock.' } }, support: { verified: false } },
    ironPill: { status: 'blocked', reason: 'Cooldown.' }, focusStatus: 'pending'
  });
  await expect(panel).toHaveCount(0);
  await page.evaluate(() => { (window as FixtureWindow).errorFixture.runtime.execution = { domains: { Route: { state: 'Failed', waitingReason: 'Adventure route was rejected.', details: 'Route diagnostic' } } }; });
  await expect(panel).toContainText('Route: Adventure route was rejected.');
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(panel).toContainText('Adventure route was rejected.');
  await page.evaluate(() => { (window as FixtureWindow).errorFixture.runtime.execution = { domains: { Route: { state: 'Observed', waitingReason: 'Waiting for next spawn.' } } }; });
  await expect(panel).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).errorFixture.commands)).toEqual([]);
});

test('failed reads retain reported errors and label the status unknown until a fresh read succeeds', async ({ page }) => {
  const panel = await openErrors(page, { resourceAllocation: { state: 'Failed', reason: '24hr allocation failed.' } });
  await page.evaluate(() => { (window as FixtureWindow).errorFixture.readError = 'Cannot read runtime.json: access denied.'; });
  await expect(panel).toContainText('Refresh failed: Cannot read runtime.json: access denied.');
  await expect(panel).toContainText('Disconnected. Current automation status is unknown.');
  await expect(panel).toContainText('24hr allocation failed.');
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(panel).toContainText('access denied');
  await page.evaluate(() => { (window as FixtureWindow).errorFixture.readError = ''; });
  await expect(panel).not.toContainText('Refresh failed:');
  await expect(panel).not.toContainText('Disconnected.');
  await expect(panel).toContainText('24hr allocation failed.');
});

test('save recovery preserves edits without hiding or retrying a failed command', async ({ page }) => {
  const panel = await openErrors(page);
  await page.getByRole('button', { name: 'Quicksave', exact: true }).click();
  await expect(panel).toContainText('Command failed: Quicksave: Game save rejected: save folder is read-only.');
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Misc', exact: true }).click();
  await page.evaluate(() => { (window as FixtureWindow).errorFixture.failSave = true; });
  const energy = page.getByRole('article', { name: 'Resources', exact: true }).getByRole('checkbox', { name: 'Energy', exact: true });
  await energy.uncheck();
  await expect(panel).toContainText('Changes not saved: Settings file is read-only.');
  await expect(energy).not.toBeChecked();
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(panel).toContainText('Settings file is read-only.');
  expect(await page.evaluate(() => (window as FixtureWindow).errorFixture.writes)).toEqual([{ _manageEnergy: false }]);
  await page.evaluate(() => { (window as FixtureWindow).errorFixture.failSave = false; });
  await page.getByRole('button', { name: 'Retry saving', exact: true }).click();
  await expect(panel).not.toContainText('Changes not saved:');
  await expect(panel).toContainText('Game save rejected: save folder is read-only.');
  await expect(energy).not.toBeChecked();
  expect(await page.evaluate(() => ({ commands: (window as FixtureWindow).errorFixture.commands, writes: (window as FixtureWindow).errorFixture.writes }))).toEqual({ commands: ['quickSave'], writes: [{ _manageEnergy: false }, { _manageEnergy: false }] });
});

test('startup failure is prominent even though live commands are unavailable', async ({ page }) => {
  const panel = await openErrors(page, { status: 'failed', startupError: 'Injector initialization failed: settings.json is invalid.' });
  await expect(panel).toContainText('Injector startup: Injector initialization failed: settings.json is invalid.');
  await expect(panel).toContainText('Disconnected. Current automation status is unknown.');
  await expect(panel).toBeInViewport();
  await expect(page.getByRole('button', { name: 'Quicksave', exact: true })).toBeDisabled();
  await expect(panel.getByRole('button', { name: 'Logs', exact: true })).toBeEnabled();
  expect(await page.evaluate(() => (window as FixtureWindow).errorFixture.commands)).toEqual([]);
});

test('selection review navigation only focuses the existing review controls', async ({ page }) => {
  const panel = await openErrors(page, { executionReview: { reviewRequired: true, reason: 'Selected gear is missing.', canPreview: true } });
  await panel.getByRole('button', { name: 'Review selection', exact: true }).click();
  await expect(page.locator('#execution-review')).toBeFocused();
  await expect(page.getByRole('article', { name: 'Selection review' })).toBeVisible();
  expect(await page.evaluate(() => (window as FixtureWindow).errorFixture.commands)).toEqual([]);
});
