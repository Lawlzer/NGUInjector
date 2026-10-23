import { createHash } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';

const source = '{"_customAutoBoostEnabled":true,"_customAutoBoostCubePowerRatio":2,"_customAdventureZone":4,"Future":{"counter":9007199254740993,"fraction":0.12345678901234567890123456789}}';
type FixtureWindow = typeof window & {
  __settingsCas: {
    settings: Record<string, unknown>;
    requests: Record<string, unknown>[];
    receipts: string[];
    deferNext: boolean;
    failNext: '' | 'transport' | 'receipt';
    release: (() => void) | null;
  };
};

async function openSettings(page: Page) {
  await page.clock.install({ time: new Date('2026-10-23T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-23T12:00:01Z'));
  await page.addInitScript(source => {
    const fixture: FixtureWindow['__settingsCas'] = {
      settings: {}, requests: [], receipts: [], deferNext: false, failNext: '', release: null
    };
    let initialized = false;
    Object.assign(window, { __settingsCas: fixture, __TAURI_INTERNALS__: {
      async invoke(command: string, args: Record<string, unknown> = {}) {
        const casPath = '/e2e/fixtures/settings-cas.ts';
        const cas = await import(casPath);
        if (!initialized) { Object.assign(fixture.settings, cas.parseSettingsJson(source)); initialized = true; }
        if (command === 'get_ngui_state') return {
          runtime_dir: 'C:\\SettingsCasFixture', marker_enabled: true, connected: true, protocol: {},
          ...await cas.settingsSnapshot(fixture.settings), profiles: [], plans: [], logs: [], command_results: [],
          runtime: { externalUiEnabled: true, status: 'running', protocolVersion: 3, settingsCasVersion: 1,
            startedAt: 'settings-cas-session', updatedAt: new Date().toISOString(), supportedCommands: ['applySettingsPatch'] }
        };
        if (command === 'save_settings_patch_json') {
          fixture.requests.push(structuredClone(args));
          const failure = fixture.failNext;
          fixture.failNext = '';
          if (fixture.deferNext) {
            fixture.deferNext = false;
            await new Promise<void>(resolve => { fixture.release = resolve; });
            fixture.release = null;
          }
          const receipt = await cas.commitSettings(fixture.settings, args, 'connected');
          fixture.receipts.push(receipt);
          if (failure === 'transport') throw new Error('Settings outcome unknown: transport lost after commit.');
          if (failure === 'receipt') return '{"success":true,"mode":"connected"}';
          return receipt;
        }
        throw new Error(`Unexpected native invocation: ${command}`);
      }
    } });
    localStorage.clear();
  }, source);
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Adventure', exact: true }).click();
  const ratio = page.getByRole('spinbutton', { name: 'Cube Power:Toughness ratio', exact: true });
  await expect(ratio).toHaveValue('2');
  return ratio;
}

const requests = (page: Page) => page.evaluate(() => (window as FixtureWindow).__settingsCas.requests);
const snapshot = (page: Page) => page.evaluate(async () => {
  const casPath = '/e2e/fixtures/settings-cas.ts';
  const cas = await import(casPath);
  return cas.settingsSnapshot((window as FixtureWindow).__settingsCas.settings) as Promise<{ settings_json: string; settings_revision: string }>;
});

test('settings saves use the exact revision and retain lossless values in committed receipts', async ({ page }) => {
  const ratio = await openSettings(page);
  const initialRevision = createHash('sha256').update(source).digest('hex');
  expect(await snapshot(page)).toEqual({ settings_json: source, settings_revision: initialRevision });
  await ratio.fill('4');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__settingsCas.receipts.length)).toBe(1);
  const expected = source.replace('Ratio":2', 'Ratio":4');
  const revision = createHash('sha256').update(expected).digest('hex');
  expect(await requests(page)).toEqual([{
    runtimeDir: 'C:\\SettingsCasFixture', patchJson: '{"_customAutoBoostCubePowerRatio":4}', expectedRevision: initialRevision
  }]);
  expect(await snapshot(page)).toEqual({ settings_json: expected, settings_revision: revision });
  expect(await page.evaluate(() => (window as FixtureWindow).__settingsCas.receipts)).toEqual([
    `{"success":true,"mode":"connected","data":{"settings":${expected},"revision":"${revision}"}}`
  ]);
  await expect(page.getByRole('button', { name: 'Retry saving', exact: true })).toHaveCount(0);
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('a concurrent settings change rejects stale CAS and requires an explicit fresh-revision retry', async ({ page }) => {
  const ratio = await openSettings(page);
  const initial = await snapshot(page);
  await page.evaluate(() => { (window as FixtureWindow).__settingsCas.deferNext = true; });
  await ratio.fill('4');
  await page.clock.runFor(350);
  await expect.poll(() => requests(page)).toHaveLength(1);
  await expect.poll(() => page.evaluate(() => typeof (window as FixtureWindow).__settingsCas.release)).toBe('function');
  await page.evaluate(() => { (window as FixtureWindow).__settingsCas.settings._customAdventureZone = 12; });
  const foreign = await snapshot(page);
  expect(foreign.settings_revision).not.toBe(initial.settings_revision);
  await page.evaluate(() => { (window as FixtureWindow).__settingsCas.release!(); });
  await expect(page.getByRole('alert')).toContainText('Settings revision changed.');
  await expect(ratio).toHaveValue('4');
  const retry = page.getByRole('button', { name: 'Retry saving', exact: true });
  await expect(retry).toBeEnabled();
  await page.clock.runFor(10_000);
  expect(await snapshot(page)).toEqual(foreign);
  expect(await requests(page)).toEqual([{
    runtimeDir: 'C:\\SettingsCasFixture', patchJson: '{"_customAutoBoostCubePowerRatio":4}', expectedRevision: initial.settings_revision
  }]);
  expect(await page.evaluate(() => (window as FixtureWindow).__settingsCas.receipts)).toEqual([]);
  await retry.click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__settingsCas.receipts.length)).toBe(1);
  expect((await requests(page))[1]).toEqual({
    runtimeDir: 'C:\\SettingsCasFixture', patchJson: '{"_customAutoBoostCubePowerRatio":4}', expectedRevision: foreign.settings_revision
  });
  expect((await snapshot(page)).settings_json).toBe(foreign.settings_json.replace('Ratio":2', 'Ratio":4'));
  await expect(retry).toHaveCount(0);
  await expect(page.getByRole('alert')).toHaveCount(0);
});

for (const failure of ['transport', 'receipt'] as const) {
  test(`a lost settings ${failure} does not replay newer edits after an unknown outcome`, async ({ page }) => {
    const ratio = await openSettings(page);
    await page.evaluate(failure => {
      const fixture = (window as FixtureWindow).__settingsCas;
      fixture.deferNext = true;
      fixture.failNext = failure;
    }, failure);
    await ratio.fill('3');
    await page.clock.runFor(350);
    await expect.poll(() => page.evaluate(() => typeof (window as FixtureWindow).__settingsCas.release)).toBe('function');
    await ratio.fill('4');
    await page.clock.runFor(350);
    expect(await requests(page)).toHaveLength(1);
    await page.evaluate(() => { (window as FixtureWindow).__settingsCas.release!(); });
    await expect(page.getByRole('alert')).toContainText(failure === 'transport' ? 'transport lost after commit' : 'committed snapshot and revision');
    await expect(page.getByRole('button', { name: 'Retry saving', exact: true })).toBeEnabled();
    await page.clock.runFor(10_000);
    await expect(ratio).toHaveValue('4');
    expect(await requests(page)).toHaveLength(1);
    expect((await snapshot(page)).settings_json).toBe(source.replace('Ratio":2', 'Ratio":3'));
    expect(await page.evaluate(() => (window as FixtureWindow).__settingsCas.receipts.length)).toBe(1);
  });
}
