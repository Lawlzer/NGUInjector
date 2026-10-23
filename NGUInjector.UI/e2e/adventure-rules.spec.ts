import { expect, test, type Page } from '@playwright/test';

type Rule = { Enabled: boolean; MinPower: number; MinToughness: number; MinRegen: number; Mode: number; Zone: number };
type FixtureWindow = typeof window & {
  __rulesSettings: Record<string, unknown>;
  __rulesPatches: Record<string, unknown>[];
  __rulesRuntime: Record<string, unknown>;
};
const rule = (fields: Partial<Rule> = {}): Rule => ({ Enabled: true, MinPower: 0, MinToughness: 0, MinRegen: 0, Mode: 0, Zone: 0, ...fields });

// Mock only native IPC; rendering, settings validation, controller and autosave are real.
async function openAdventure(page: Page, rules?: Rule[], connected = true, telemetry?: Record<string, number>) {
  await page.addInitScript(({ rules, connected, telemetry }) => {
    const fixture = window as FixtureWindow;
    fixture.__rulesSettings = {
      _customAdventureEnabled: true, _customAdventureMode: 1, _customAdventureZone: 12,
      _manualAdventureEnabled: false, _manualAdventureZone: 4, _manualAdventureFastCombat: true,
      Future: { keep: 42 }, ...(rules ? { _customAdventureRules: rules } : {})
    };
    fixture.__rulesPatches = [];
    fixture.__rulesRuntime = {
      externalUiEnabled: true, status: 'running', protocolVersion: 3, settingsCasVersion: 1,
      supportedCommands: ['applySettingsPatch'], ...(telemetry ? { adventureRules: telemetry } : {})
    };
    Object.assign(window, { __TAURI_INTERNALS__: { invoke: async (command: string, args: Record<string, unknown> = {}) => {
      const casPath = '/e2e/fixtures/settings-cas.ts';
      const cas = await import(casPath);
      if (command === 'get_ngui_state') return {
        runtime_dir: 'C:\\Fixture\\NGUInjector', connected, marker_enabled: true, protocol: {},
        ...await cas.settingsSnapshot(fixture.__rulesSettings),
        runtime: { updatedAt: new Date().toISOString(), ...fixture.__rulesRuntime },
        profiles: [], plans: [], logs: [], command_results: []
      };
      if (command === 'save_settings_patch_json') {
        const receipt = await cas.commitSettings(fixture.__rulesSettings, args);
        fixture.__rulesPatches.push(cas.settingsPatch(args));
        return receipt;
      }
      throw new Error(`Unexpected native invocation: ${command}`);
    } } });
    localStorage.clear();
  }, { rules, connected, telemetry });
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Global adventure rules' })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Adventure mode', exact: true })).toHaveValue('1');
}

test('ordered rules add, edit, move, disable and remove through normal autosave', async ({ page }) => {
  await openAdventure(page);
  const editor = page.getByRole('region', { name: 'Global adventure rules' });
  const first = editor.getByRole('group', { name: 'Rule 1', exact: true });
  const second = editor.getByRole('group', { name: 'Rule 2', exact: true });
  await expect(editor.getByRole('group', { name: /^Rule \d+$/ })).toHaveCount(0);
  await expect(editor.getByRole('button', { name: 'Add rule', exact: true })).toBeEnabled();
  expect(await page.evaluate(() => (window as FixtureWindow).__rulesPatches)).toEqual([]);
  await editor.getByRole('button', { name: 'Add rule', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__rulesSettings._customAdventureRules)).toEqual([rule()]);
  const mode = first.getByRole('combobox', { name: 'Target mode' });
  expect(await mode.locator('option').evaluateAll(options => options.map(option => option.getAttribute('value')))).toEqual(['0', '1', '2', '4']);
  await mode.selectOption('4');
  await first.getByRole('combobox', { name: 'Target zone' }).selectOption('43');
  await first.getByRole('spinbutton', { name: 'Minimum power' }).fill('100.5');
  await first.getByRole('spinbutton', { name: 'Minimum toughness' }).fill('200');
  await first.getByRole('spinbutton', { name: 'Minimum regen' }).fill('3.25');
  const manualRule = rule({ Mode: 4, Zone: 43, MinPower: 100.5, MinToughness: 200, MinRegen: 3.25 });
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__rulesSettings._customAdventureRules)).toEqual([manualRule]);
  const manual = page.getByRole('article', { name: 'Manual Adventure', exact: true });
  await expect(manual.getByRole('checkbox', { name: 'Manual adventure', exact: true })).not.toBeChecked();
  await expect(manual.getByRole('checkbox', { name: 'Fast combat', exact: true })).toBeChecked();
  await expect(manual.getByRole('combobox', { name: 'Manual adventure zone' })).toHaveCount(0);

  await editor.getByRole('button', { name: 'Add rule', exact: true }).click();
  await second.getByRole('combobox', { name: 'Target mode' }).selectOption('2');
  await editor.getByRole('button', { name: 'Move rule 2 up', exact: true }).click();
  await expect(first.getByRole('combobox', { name: 'Target mode' })).toHaveValue('2');
  await editor.getByRole('button', { name: 'Move rule 1 down', exact: true }).click();
  await expect(first.getByRole('combobox', { name: 'Target mode' })).toHaveValue('4');
  await first.getByRole('checkbox', { name: 'Enabled', exact: true }).uncheck();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__rulesSettings._customAdventureRules)).toEqual([{ ...manualRule, Enabled: false }, rule({ Mode: 2 })]);
  await expect(manual.getByRole('checkbox')).toHaveCount(1);
  await editor.getByRole('button', { name: 'Remove rule 2', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__rulesSettings._customAdventureRules)).toEqual([{ ...manualRule, Enabled: false }]);
  expect(await page.evaluate(() => (window as FixtureWindow).__rulesSettings)).toMatchObject({
    _customAdventureMode: 1, _customAdventureZone: 12, _manualAdventureEnabled: false, _manualAdventureZone: 4, Future: { keep: 42 }
  });
  expect(await page.evaluate(() => (window as FixtureWindow).__rulesPatches.every(patch => Object.keys(patch).join() === '_customAdventureRules'))).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('invalid minimum drafts stay local and survive polling and tab changes', async ({ page }) => {
  await openAdventure(page, [rule()]);
  const input = page.getByRole('spinbutton', { name: 'Minimum power' });
  for (const invalid of ['', '1e-400', '-1']) {
    await input.fill(invalid);
    await expect(page.getByRole('alert')).toContainText('_customAdventureRules[0] MinPower');
    await expect(input).toHaveValue(invalid);
    expect(await page.evaluate(() => (window as FixtureWindow).__rulesPatches)).toEqual([]);
  }
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Misc', exact: true }).click();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(input).toHaveValue('-1');
  for (const value of ['0', '1e100']) {
    await input.fill(value);
    if (value === '0') await page.getByRole('spinbutton', { name: 'Minimum regen' }).fill('.5');
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__rulesSettings._customAdventureRules)).toEqual([rule({ MinPower: Number(value), MinRegen: .5 })]);
    await expect(page.getByRole('alert')).toHaveCount(0);
  }
});

test('rule limit blocks additions until a rule is removed', async ({ page }) => {
  await openAdventure(page, Array.from({ length: 32 }, () => rule()));
  const editor = page.getByRole('region', { name: 'Global adventure rules' });
  await expect(editor.getByRole('group', { name: /^Rule \d+$/ })).toHaveCount(32);
  await expect(editor.getByRole('button', { name: 'Add rule', exact: true })).toBeDisabled();
  await editor.getByRole('button', { name: 'Remove rule 32', exact: true }).click();
  await expect(editor.getByRole('group', { name: /^Rule \d+$/ })).toHaveCount(31);
  await expect(editor.getByRole('button', { name: 'Add rule', exact: true })).toBeEnabled();
});

test('backend telemetry is read-only and explicit Manual Adventure overrides rules', async ({ page }) => {
  await openAdventure(page, [rule()], true, { power: 123, toughness: 456, regen: 7.5, matchedRuleIndex: 0 });
  const editor = page.getByRole('region', { name: 'Global adventure rules' });
  await expect(editor).toContainText('Power 123 / Toughness 456 / Regen 7.5');
  await expect(editor).toContainText('First matching rule: 1');
  await page.getByRole('checkbox', { name: 'Manual adventure', exact: true }).check();
  await expect(editor.locator('.notice')).toContainText('Manual Adventure overrides these rules');
  await page.evaluate(() => { (window as FixtureWindow).__rulesRuntime.adventureRules = { power: 0, toughness: 0, regen: 0, matchedRuleIndex: -1 }; });
  await expect(editor).toContainText('No matching rule; Adventure mode fallback.');
  expect(await page.evaluate(() => (window as FixtureWindow).__rulesPatches)).toEqual([{ _manualAdventureEnabled: true }]);
});

test('rules remain editable offline and hide stale live stats', async ({ page }) => {
  await openAdventure(page, [rule({ Mode: 1, Zone: -1 })], false, { power: 123, toughness: 456, regen: 7.5, matchedRuleIndex: 0 });
  const editor = page.getByRole('region', { name: 'Global adventure rules' });
  await expect(editor).toContainText('Connect to see live stats');
  await expect(editor).not.toContainText('First matching rule:');
  await expect(editor).not.toContainText('Power 123');
  await editor.getByRole('combobox', { name: 'Target zone' }).selectOption('1000');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__rulesSettings._customAdventureRules)).toEqual([rule({ Mode: 1, Zone: 1000 })]);
});
