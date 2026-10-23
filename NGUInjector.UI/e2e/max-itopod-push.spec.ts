import { expect, test, type Page } from '@playwright/test';

type FixtureWindow = typeof window & {
  __pushSettings: Record<string, unknown>;
  __pushPatches: Record<string, unknown>[];
  __pushCommands: Record<string, unknown>[];
  __nativeCalls: string[];
  __pushRuntime: Record<string, unknown>;
  __pushRejection: string;
};

// Only native IPC is mocked; the App, controller, autosave and validation are real.
async function openAdventure(page: Page, settings: Record<string, unknown> = {}, runtime: Record<string, unknown> = {}) {
  await page.addInitScript(({ settings, runtime }) => {
    const fixture = window as FixtureWindow;
    fixture.__pushPatches = [];
    fixture.__pushCommands = [];
    fixture.__nativeCalls = [];
    fixture.__pushRejection = '';
    fixture.__pushSettings = {
      _customAdventureMode: 1, _customAdventureEnabled: false,
      _customAdventureZone: 12, _manualAdventureZone: 4, Future: { keep: 42 }, ...settings
    };
    fixture.__pushRuntime = {
      externalUiEnabled: true, status: 'running', protocolVersion: 3, settingsCasVersion: 1,
      startedAt: '2026-10-16T10:00:00Z', supportedCommands: ['applySettingsPatch'],
      maxItopodPush: { active: false, remainingDeaths: 0, floor: 0 }, ...runtime
    };
    const supportsCancel = Array.isArray(runtime.supportedCommands) && runtime.supportedCommands.includes('cancelMaxItopodPush');
    let cancelQueued = false;
    Object.assign(window, { __TAURI_INTERNALS__: { invoke: async (command: string, args: Record<string, unknown> = {}) => {
      fixture.__nativeCalls.push(command);
      const casPath = '/e2e/fixtures/settings-cas.ts';
      const cas = await import(casPath);
      if (command === 'get_ngui_state') return {
        runtime_dir: 'C:\\Fixture\\NGUInjector', connected: true, marker_enabled: true, protocol: {},
        ...await cas.settingsSnapshot(fixture.__pushSettings),
        runtime: { updatedAt: new Date().toISOString(), ...fixture.__pushRuntime },
        profiles: [], plans: [], logs: [], command_results: []
      };
      if (command === 'save_settings_patch_json') {
        if (fixture.__pushRejection) throw new Error(fixture.__pushRejection);
        const receipt = await cas.commitSettings(fixture.__pushSettings, args);
        fixture.__pushPatches.push(cas.settingsPatch(args));
        return receipt;
      }
      if (command === 'enqueue_command' && supportsCancel) {
        const envelope = args.envelope as { command?: string; args?: Record<string, unknown> } | undefined;
        if (args.runtimeDir !== 'C:\\Fixture\\NGUInjector' || envelope?.command !== 'cancelMaxItopodPush'
          || !envelope.args || Array.isArray(envelope.args) || Object.keys(envelope.args).length || cancelQueued) {
          throw new Error(`Unexpected command envelope: ${JSON.stringify(args)}`);
        }
        fixture.__pushCommands.push(structuredClone(envelope));
        cancelQueued = true;
        return 'push-cancel-1';
      }
      if (command === 'read_command_result' && supportsCancel && cancelQueued
        && args.runtimeDir === 'C:\\Fixture\\NGUInjector' && args.id === 'push-cancel-1') {
        fixture.__pushRuntime.maxItopodPush = { active: false, remainingDeaths: 0, floor: 321 };
        return { id: 'push-cancel-1', command: 'cancelMaxItopodPush', success: true, completedAt: new Date().toISOString() };
      }
      throw new Error(`Unexpected native invocation: ${command}`);
    } } });
    localStorage.clear();
  }, { settings, runtime });
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Adventure automation' })).toBeVisible();
}

test('manual settings appear only when enabled and retain their values when hidden', async ({ page }) => {
  await openAdventure(page, {
    _manualAdventureEnabled: false, _manualAdventureBossOnly: true,
    _manualAdventureRecoverHealth: true, _manualAdventurePrecastBuffs: false,
    _manualAdventureFastCombat: true, _manualAdventureBeastMode: false,
    _manualAdventureAllowFallback: true
  });
  const manual = page.getByRole('article', { name: 'Manual Adventure', exact: true });
  const enabled = manual.getByRole('checkbox', { name: 'Manual adventure', exact: true });
  const zone = manual.getByRole('combobox', { name: 'Manual adventure zone', exact: true });
  const push = manual.getByRole('button', { name: 'Push ITOPOD', exact: true });
  await expect(manual.getByRole('checkbox')).toHaveCount(1);
  await expect(zone).toHaveCount(0);
  await expect(push).toBeVisible();
  await expect(push).toHaveAttribute('aria-pressed', 'false');
  expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([]);

  await enabled.check();
  await expect(manual.getByRole('checkbox')).toHaveCount(7);
  await expect(manual).not.toContainText('Gear swaps recall energy, magic, and R3.');
  await expect(zone).toHaveValue('4');
  await expect(manual.getByRole('checkbox', { name: 'Bosses only', exact: true })).toBeChecked();
  await expect(manual.getByRole('checkbox', { name: 'Precast buffs', exact: true })).not.toBeChecked();
  await zone.selectOption('5');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualAdventureZone)).toBe(5);
  await enabled.uncheck();
  await expect(manual.getByRole('checkbox')).toHaveCount(1);
  await expect(zone).toHaveCount(0);
  await expect(push).toBeVisible();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualAdventureEnabled)).toBe(false);
  await enabled.check();
  await expect(zone).toHaveValue('5');
  await expect(manual.getByRole('checkbox', { name: 'Bosses only', exact: true })).toBeChecked();
  await expect(manual.getByRole('checkbox', { name: 'Precast buffs', exact: true })).not.toBeChecked();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('adventure-settings.png'), fullPage: true });
});

test('Adventure mode excludes push and each card owns its zone or push settings', async ({ page }) => {
  await openAdventure(page);
  const adventure = page.getByRole('article', { name: 'Adventure automation' });
  const manual = page.getByRole('article', { name: 'Manual Adventure', exact: true });
  const mode = adventure.getByRole('combobox', { name: 'Adventure mode', exact: true });
  const farm = adventure.getByRole('combobox', { name: 'Farm zone', exact: true });
  const deaths = manual.getByRole('spinbutton', { name: 'Max ITOPOD death limit' });
  const push = manual.getByRole('button', { name: 'Push ITOPOD', exact: true });
  await expect(mode.locator('option[value="3"]')).toHaveCount(0);
  await expect(mode.getByRole('option', { name: /ITOPOD push/i })).toHaveCount(0);
  await expect(adventure.getByRole('button', { name: 'Push ITOPOD', exact: true })).toHaveCount(0);
  await expect(farm).toHaveValue('12');
  await expect(deaths).toHaveCount(0);
  await expect(adventure.locator('#max-itopod-deaths-help')).toHaveCount(0);
  for (const selection of ['0', '2']) {
    await mode.selectOption(selection);
    await expect(farm).toHaveCount(0);
    await expect(deaths).toHaveCount(0);
    await expect(adventure.getByRole('status')).toHaveCount(0);
  }
  await push.click();
  await expect(push).toHaveAttribute('aria-pressed', 'true');
  await expect(mode).toHaveValue('0');
  await expect(farm).toHaveCount(0);
  await expect(deaths).toHaveValue('3');
  await expect(deaths).toHaveAccessibleDescription(/Limit changes apply to the next push/);
  await expect(manual.locator('#max-itopod-deaths-help')).toContainText('returns to the manual zone or Optimal ITOPOD');
  await expect(manual.getByRole('status')).toHaveText('Idle');
  await expect(adventure.getByRole('spinbutton', { name: 'Max ITOPOD death limit' })).toHaveCount(0);
  await expect(adventure.locator('#max-itopod-deaths-help')).toHaveCount(0);
  await expect(adventure.getByRole('status')).toHaveCount(0);
  await mode.selectOption('1');
  await expect(farm).toHaveValue('12');
  await expect(push).toHaveAttribute('aria-pressed', 'false');
  await expect(deaths).toHaveCount(0);
  await expect(adventure.locator('#max-itopod-deaths-help')).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings._customAdventureZone)).toBe(12);
});

test('manual bonuses configure while off, autosave numeric priorities, and preserve unrelated settings', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-16T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-16T12:00:01Z'));
  await openAdventure(page, { _manualAdventureEnabled: false, _manualAdventureLoadout: 2, _manualAdventureBeardWeights: [0,0,0,0,0,7,0] });
  const manual = page.getByRole('article', { name: 'Manual Adventure', exact: true });
  const disclosure = manual.locator('details');
  await expect(disclosure).not.toHaveAttribute('open');
  await manual.getByText('Manual adventure bonuses', { exact: true }).click();
  const push = manual.getByRole('textbox', { name: 'Gear weights Push bonus', exact: true });
  await expect(push).toHaveValue('0');
  expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([]);
  await push.fill('+100');
  await page.clock.runFor(349);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([]);
  await page.clock.runFor(1);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualAdventureGearWeights)).toEqual([100,0,0,0,0]);
  const toughness = manual.getByRole('textbox', { name: 'Gear weights Adventure toughness bonus', exact: true });
  await toughness.fill('0.125');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualAdventureGearWeights)).toEqual([100,0,.125,0,0]);
  await manual.getByRole('button', { name: 'Increase Beard priorities NGU bonus by 100', exact: true }).click();
  await page.clock.runFor(1);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualAdventureBeardWeights)).toEqual([0,0,0,100,0,7,0]);
  const digger = manual.getByRole('textbox', { name: 'Digger priorities Adventure bonus', exact: true });
  for (const invalid of ['', '-1', '1000001', 'Infinity', 'NaN']) {
    await digger.fill(invalid);
    await page.clock.runFor(500);
    await expect(page.getByRole('alert').first()).toContainText('_manualAdventureDiggerWeights');
    expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches.length)).toBe(3);
  }
  await digger.fill('1000000');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualAdventureDiggerWeights)).toEqual([0,0,0,1000000,0,0,0,0,0,0,0,0]);
  await digger.fill('0');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualAdventureDiggerWeights)).toEqual(Array(12).fill(0));
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Misc', exact: true }).click();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await manual.getByText('Manual adventure bonuses', { exact: true }).click();
  await expect(push).toHaveValue('100');
  await expect(toughness).toHaveValue('0.125');
  expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings)).toMatchObject({
    _manualAdventureEnabled: false, _manualAdventureLoadout: 2, _customAdventureZone: 12, _manualAdventureZone: 4, Future: { keep: 42 }
  });
  expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls)).not.toContain('save_preset_library');
  expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls)).not.toContain('enqueue_command');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('manual-adventure-bonuses.png'), fullPage: true });
});

for (const push of ['enabled mode', 'manual-only mode', 'active telemetry']) {
  test(`manual push combat hides ignored settings for ${push}`, async ({ page }) => {
    await openAdventure(page, {
      _manualAdventureEnabled: true, _manualAdventureBossOnly: true,
      _manualAdventureAllowFallback: true, _manualAdventureFastCombat: true,
      _customAdventureEnabled: push === 'enabled mode', _customAdventureMode: push === 'active telemetry' ? 0 : 3
    }, push === 'active telemetry' ? { maxItopodPush: { active: true, remainingDeaths: 2, floor: 321 } } : {});
    const manual = page.getByRole('article', { name: 'Manual Adventure', exact: true });
    await expect(manual.getByRole('button', { name: 'Push ITOPOD', exact: true })).toHaveAttribute('aria-pressed', 'true');
    const zone = manual.getByRole('combobox', { name: 'Manual adventure zone', exact: true });
    await expect(zone).toHaveValue('4');
    await expect(zone).toBeEnabled();
    await expect(manual.getByRole('combobox', { name: 'Manual adventure loadout', exact: true })).toHaveValue('-1');
    await expect(manual.getByRole('checkbox', { name: 'Bosses only', exact: true })).toHaveCount(0);
    await expect(manual.getByRole('checkbox', { name: 'Allow fallback', exact: true })).toHaveCount(0);
    for (const name of ['Recover health', 'Precast buffs', 'Fast combat', 'Beast mode']) {
      await expect(manual.getByRole('checkbox', { name, exact: true })).toBeVisible();
    }
    await expect(manual).toContainText('Push uses ITOPOD; the selected zone resumes afterward.');
    await zone.selectOption('5');
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([{ _manualAdventureZone: 5 }]);
    expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings)).toMatchObject({
      _customAdventureMode: push === 'active telemetry' ? 0 : 3, _customAdventureZone: 12, _manualAdventureZone: 5,
      _manualAdventureBossOnly: true, _manualAdventureAllowFallback: true
    });
    if (push === 'active telemetry') {
      await expect(manual.getByRole('status')).toHaveText('Active: 2 deaths remaining / Floor 321');
    }
    await page.evaluate(() => {
      const fixture = window as FixtureWindow;
      fixture.__pushSettings._customAdventureMode = 0;
      fixture.__pushRuntime.maxItopodPush = { active: false, remainingDeaths: 0, floor: 321 };
    });
    await expect(zone).toHaveValue('5');
    await expect(manual.getByRole('checkbox', { name: 'Bosses only', exact: true })).toBeChecked();
    await expect(manual.getByRole('checkbox', { name: 'Allow fallback', exact: true })).toBeChecked();
    await expect(manual).not.toContainText('Push uses ITOPOD; the selected zone resumes afterward.');
    expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([{ _manualAdventureZone: 5 }]);
    expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls)).not.toContain('enqueue_command');
  });
}

test('command-started push cancels in one click without changing the saved mode or zones', async ({ page }) => {
  await openAdventure(page, { _customAdventureEnabled: true, _manualAdventureEnabled: true }, {
    supportedCommands: ['applySettingsPatch', 'cancelMaxItopodPush'],
    maxItopodPush: { active: true, remainingDeaths: 2, floor: 321 }
  });
  const adventure = page.getByRole('article', { name: 'Adventure automation' });
  const manual = page.getByRole('article', { name: 'Manual Adventure', exact: true });
  const push = manual.getByRole('button', { name: 'Push ITOPOD', exact: true });
  const saved = await page.evaluate(() => (window as FixtureWindow).__pushSettings);
  await expect(push).toHaveAttribute('aria-pressed', 'true');
  await expect(push).toBeEnabled();
  await expect(adventure.getByRole('combobox', { name: 'Adventure mode', exact: true })).toHaveValue('1');
  await expect(adventure.getByRole('combobox', { name: 'Farm zone', exact: true })).toHaveValue('12');
  await expect(manual.getByRole('combobox', { name: 'Manual adventure zone', exact: true })).toHaveValue('4');
  await expect(manual.getByRole('status')).toHaveText('Active: 2 deaths remaining / Floor 321');
  await expect(manual.getByRole('spinbutton', { name: 'Max ITOPOD death limit' })).toHaveCount(0);
  await push.click();
  await expect(push).toHaveAttribute('aria-pressed', 'false');
  await expect(push).toBeEnabled();
  await expect(manual.getByRole('status')).toHaveCount(0);
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(adventure.getByRole('combobox', { name: 'Adventure mode', exact: true })).toHaveValue('1');
  await expect(adventure.getByRole('combobox', { name: 'Farm zone', exact: true })).toHaveValue('12');
  await expect(manual.getByRole('combobox', { name: 'Manual adventure zone', exact: true })).toHaveValue('4');
  expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings)).toEqual(saved);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushCommands)).toEqual([
    { command: 'cancelMaxItopodPush', args: {} }
  ]);
  expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls.filter(command => command !== 'get_ngui_state'))).toEqual([
    'enqueue_command', 'read_command_result'
  ]);
});

for (const scenario of ['unsupported', 'stale', 'disconnected']) {
  test(`command-started push cannot be cancelled when ${scenario}`, async ({ page }) => {
    await openAdventure(page, {}, {
      supportedCommands: scenario === 'unsupported' ? ['applySettingsPatch'] : ['applySettingsPatch', 'cancelMaxItopodPush'],
      maxItopodPush: { active: true, remainingDeaths: 2, floor: 321 },
      ...(scenario === 'stale' ? { updatedAt: '2000-01-01T00:00:00Z' } : {}),
      ...(scenario === 'disconnected' ? { externalUiEnabled: false } : {})
    });
    const manual = page.getByRole('article', { name: 'Manual Adventure', exact: true });
    const push = manual.getByRole('button', { name: 'Push ITOPOD', exact: true });
    await expect(push).toHaveAttribute('aria-pressed', 'true');
    await expect(push).toBeDisabled();
    await expect(manual.getByRole('status')).toHaveText(scenario === 'unsupported' ? 'Active: 2 deaths remaining / Floor 321' : 'Disconnected');
    expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings)).toMatchObject({
      _customAdventureMode: 1, _customAdventureZone: 12, _manualAdventureZone: 4
    });
    expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__pushCommands)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls.filter(command => command !== 'get_ngui_state'))).toEqual([]);
  });
}

test('cube ratio appears only while auto boost is enabled', async ({ page }) => {
  await openAdventure(page, { _customAutoBoostEnabled: false, _customAutoBoostCubePowerRatio: 4 });
  const inventory = page.getByRole('article', { name: 'Inventory & Boosts', exact: true });
  const enabled = inventory.getByRole('checkbox', { name: 'Auto boost', exact: true });
  const ratio = inventory.getByRole('spinbutton', { name: 'Cube Power:Toughness ratio', exact: true });
  await expect(ratio).toHaveCount(0);
  await enabled.check();
  await expect(ratio).toHaveValue('4');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._customAutoBoostEnabled)).toBe(true);
  await enabled.uncheck();
  await expect(ratio).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([
    { _customAutoBoostEnabled: true }, { _customAutoBoostEnabled: false }
  ]);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings._customAutoBoostCubePowerRatio)).toBe(4);
});

test('toggling Push ITOPOD saves settings and enabling Adventure uses no one-shot commands', async ({ page }) => {
  await openAdventure(page);
  const adventure = page.getByRole('article', { name: 'Adventure automation' });
  const manual = page.getByRole('article', { name: 'Manual Adventure', exact: true });
  const mode = adventure.getByRole('combobox', { name: 'Adventure mode', exact: true });
  const deaths = manual.getByRole('spinbutton', { name: 'Max ITOPOD death limit' });
  const push = manual.getByRole('button', { name: 'Push ITOPOD', exact: true });
  await expect(deaths).toHaveCount(0);
  await expect(push).toHaveAttribute('aria-pressed', 'false');
  await expect(manual.getByRole('combobox', { name: 'Manual adventure zone', exact: true })).toHaveCount(0);
  await push.click();
  await expect(push).toHaveAttribute('aria-pressed', 'true');
  await expect(mode).toHaveValue('0');
  await expect(deaths).toHaveValue('3');
  await expect(deaths).toHaveAccessibleDescription(/Limit changes apply to the next push/);
  await expect(manual).toContainText('After the death limit, returns to the manual zone or Optimal ITOPOD.');
  await expect(manual.getByRole('status')).toHaveText('Idle');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([{ _customAdventureMode: 3 }]);
  await page.getByRole('checkbox', { name: 'Adventure goal', exact: true }).check();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([
    { _customAdventureMode: 3 }, { _customAdventureEnabled: true }
  ]);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings)).toMatchObject({
    _customAdventureMode: 3, _customAdventureEnabled: true, _customAdventureZone: 12, _manualAdventureZone: 4, Future: { keep: 42 }
  });
  await push.click();
  await expect(push).toHaveAttribute('aria-pressed', 'false');
  await expect(mode).toHaveValue('0');
  await expect(deaths).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([
    { _customAdventureMode: 3 }, { _customAdventureEnabled: true }, { _customAdventureMode: 0 }
  ]);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings)).toMatchObject({
    _customAdventureMode: 0, _customAdventureEnabled: true, _customAdventureZone: 12, _manualAdventureZone: 4, Future: { keep: 42 }
  });
  expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls)).not.toContain('enqueue_command');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('disabled Adventure can configure the selected push before enabling it', async ({ page }) => {
  await openAdventure(page, { _customAdventureMode: 3 });
  const card = page.getByRole('article', { name: 'Adventure automation' });
  const manual = page.getByRole('article', { name: 'Manual Adventure', exact: true });
  await expect(manual.getByRole('button', { name: 'Push ITOPOD', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await manual.getByRole('spinbutton', { name: 'Max ITOPOD death limit' }).fill('7');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([{ _customAdventureMaxItopodDeaths: 7 }]);
  await expect(card.getByRole('combobox', { name: 'Adventure mode', exact: true })).toHaveValue('0');
  expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings._customAdventureEnabled)).toBe(false);
  await card.getByRole('checkbox', { name: 'Adventure goal', exact: true }).check();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([
    { _customAdventureMaxItopodDeaths: 7 }, { _customAdventureEnabled: true }
  ]);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings._customAdventureEnabled)).toBe(true);
  expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls)).not.toContain('enqueue_command');
});

test('manual-only push toggles through settings without enabling Adventure or changing its zone', async ({ page }) => {
  await openAdventure(page, { _manualAdventureEnabled: true });
  const adventure = page.getByRole('article', { name: 'Adventure automation' });
  const manual = page.getByRole('article', { name: 'Manual Adventure', exact: true });
  const push = manual.getByRole('button', { name: 'Push ITOPOD', exact: true });
  const zone = manual.getByRole('combobox', { name: 'Manual adventure zone', exact: true });
  await expect(zone).toHaveValue('4');
  await expect(manual.getByRole('checkbox', { name: 'Bosses only', exact: true })).toBeVisible();
  await push.click();
  await expect(push).toHaveAttribute('aria-pressed', 'true');
  await expect(adventure.getByRole('combobox', { name: 'Adventure mode', exact: true })).toHaveValue('0');
  await expect(zone).toHaveValue('4');
  await expect(zone).toBeEnabled();
  await expect(manual.getByRole('checkbox', { name: 'Bosses only', exact: true })).toHaveCount(0);
  await expect(manual.getByRole('checkbox', { name: 'Allow fallback', exact: true })).toHaveCount(0);
  await expect(manual).toContainText('Push uses ITOPOD; the selected zone resumes afterward.');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([{ _customAdventureMode: 3 }]);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings)).toMatchObject({
    _customAdventureMode: 3, _customAdventureEnabled: false, _manualAdventureEnabled: true,
    _customAdventureZone: 12, _manualAdventureZone: 4, Future: { keep: 42 }
  });
  await push.click();
  await expect(push).toHaveAttribute('aria-pressed', 'false');
  await expect(zone).toHaveValue('4');
  await expect(manual.getByRole('checkbox', { name: 'Bosses only', exact: true })).toBeVisible();
  await expect(manual.getByRole('checkbox', { name: 'Allow fallback', exact: true })).toBeVisible();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([
    { _customAdventureMode: 3 }, { _customAdventureMode: 0 }
  ]);
  await expect(adventure.getByRole('checkbox', { name: 'Adventure goal', exact: true })).not.toBeChecked();
  await expect(manual.getByRole('checkbox', { name: 'Manual adventure', exact: true })).toBeChecked();
  expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls)).not.toContain('enqueue_command');
});

for (const [name, key] of [['Adventure goal', '_customAdventureEnabled'], ['Manual adventure', '_manualAdventureEnabled']]) {
  test(`enabling ${name} includes a pending death-limit edit in the same patch`, async ({ page }) => {
    await page.clock.install({ time: new Date('2026-10-16T12:00:00Z') });
    await page.clock.pauseAt(new Date('2026-10-16T12:00:01Z'));
    await openAdventure(page, { _customAdventureMode: 3 });
    const card = page.getByRole('article', { name: 'Manual Adventure', exact: true });
    await card.getByRole('spinbutton', { name: 'Max ITOPOD death limit' }).fill('9');
    await page.clock.runFor(200);
    expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([]);
    await page.getByRole('checkbox', { name, exact: true }).check();
    await page.clock.runFor(1);
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([
      { _customAdventureMaxItopodDeaths: 9, [key]: true }
    ]);
    await page.clock.runFor(350);
    expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([
      { _customAdventureMaxItopodDeaths: 9, [key]: true }
    ]);
    expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings)).toMatchObject({
      _customAdventureMode: 3, _customAdventureMaxItopodDeaths: 9, [key]: true
    });
    if (key === '_manualAdventureEnabled') {
      await expect(page.getByRole('checkbox', { name: 'Adventure goal', exact: true })).not.toBeChecked();
      await expect(card.getByRole('combobox', { name: 'Manual adventure zone', exact: true })).toHaveValue('4');
      await expect(card.getByRole('checkbox', { name: 'Bosses only', exact: true })).toHaveCount(0);
      await expect(card.getByRole('checkbox', { name: 'Allow fallback', exact: true })).toHaveCount(0);
    }
    expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls)).not.toContain('enqueue_command');
  });
}

test('saved death limit autosaves and survives polling and tab changes', async ({ page }) => {
  await openAdventure(page, { _customAdventureMode: 3, _customAdventureMaxItopodDeaths: 7 });
  const card = page.getByRole('article', { name: 'Manual Adventure', exact: true });
  const mode = page.getByRole('article', { name: 'Adventure automation' }).getByRole('combobox', { name: 'Adventure mode', exact: true });
  const deaths = card.getByRole('spinbutton', { name: 'Max ITOPOD death limit' });
  await expect(deaths).toHaveValue('7');
  await deaths.fill('8');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([{ _customAdventureMaxItopodDeaths: 8 }]);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Misc', exact: true }).click();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(deaths).toHaveValue('8');
  await mode.selectOption('2');
  await expect(deaths).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([
    { _customAdventureMaxItopodDeaths: 8 }, { _customAdventureMode: 2 }
  ]);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings._customAdventureMaxItopodDeaths)).toBe(8);
  await card.getByRole('button', { name: 'Push ITOPOD', exact: true }).click();
  await expect(deaths).toHaveValue('8');
  await expect(mode).toHaveValue('0');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([
    { _customAdventureMaxItopodDeaths: 8 }, { _customAdventureMode: 2 }, { _customAdventureMode: 3 }
  ]);
  expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls)).not.toContain('enqueue_command');
});

for (const control of ['Adventure mode', 'Push ITOPOD']) {
  test(`changing ${control} away from an active push saves only the mode and waits for backend status`, async ({ page }) => {
    await openAdventure(page, { _customAdventureMode: 3, _customAdventureMaxItopodDeaths: 7 }, {
      maxItopodPush: { active: true, remainingDeaths: 2, floor: 321 }
    });
    const card = page.getByRole('article', { name: 'Adventure automation' });
    const manual = page.getByRole('article', { name: 'Manual Adventure', exact: true });
    const mode = card.getByRole('combobox', { name: 'Adventure mode', exact: true });
    const push = manual.getByRole('button', { name: 'Push ITOPOD', exact: true });
    await expect(mode).toHaveValue('0');
    await expect(push).toHaveAttribute('aria-pressed', 'true');
    if (control === 'Adventure mode') await mode.selectOption('1');
    else await push.click();
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([{ _customAdventureMode: control === 'Adventure mode' ? 1 : 0 }]);
    await expect(push).toHaveAttribute('aria-pressed', 'true');
    await expect(manual.getByRole('spinbutton', { name: 'Max ITOPOD death limit' })).toHaveCount(0);
    await expect(manual.locator('#max-itopod-deaths-help')).toHaveCount(0);
    if (control === 'Adventure mode') await expect(card.getByRole('combobox', { name: 'Farm zone', exact: true })).toHaveValue('12');
    else await expect(card.getByRole('combobox', { name: 'Farm zone', exact: true })).toHaveCount(0);
    await expect(manual.getByRole('status')).toHaveText('Active: 2 deaths remaining / Floor 321');
    await expect(card.getByRole('status')).toHaveCount(0);
    await page.evaluate(() => { (window as FixtureWindow).__pushRuntime.maxItopodPush = { active: false, remainingDeaths: 0, floor: 0 }; });
    await expect(manual.getByRole('status')).toHaveCount(0);
    await expect(push).toHaveAttribute('aria-pressed', 'false');
    expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings._customAdventureMaxItopodDeaths)).toBe(7);
    expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls)).not.toContain('enqueue_command');
  });
}

test('invalid death-limit drafts stay local while both boundaries autosave', async ({ page }) => {
  await openAdventure(page, { _customAdventureMode: 3 });
  const deaths = page.getByRole('spinbutton', { name: 'Max ITOPOD death limit' });
  for (const invalid of ['', '0', '-1', '1.5', '1001']) {
    await deaths.fill(invalid);
    await expect(page.getByRole('alert')).toContainText('_customAdventureMaxItopodDeaths');
    await expect(deaths).toHaveValue(invalid);
    expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([]);
  }
  for (const limit of [1, 1000]) {
    await deaths.fill(String(limit));
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._customAdventureMaxItopodDeaths)).toBe(limit);
    await expect(page.getByRole('alert')).toHaveCount(0);
  }
  expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([
    { _customAdventureMaxItopodDeaths: 1 }, { _customAdventureMaxItopodDeaths: 1000 }
  ]);
});

test('runtime deaths are read-only and backend completion selects Optimal ITOPOD', async ({ page }) => {
  await openAdventure(page, { _customAdventureMode: 3, _customAdventureMaxItopodDeaths: 7 }, {
    settings: { customAdventureMaxItopodDeaths: 7 }, maxItopodPush: { active: true, remainingDeaths: 2, floor: 321 }
  });
  const card = page.getByRole('article', { name: 'Manual Adventure', exact: true });
  await expect(card.getByRole('status')).toHaveText('Active: 2 deaths remaining / Floor 321');
  const deaths = card.getByRole('spinbutton', { name: 'Max ITOPOD death limit' });
  await expect(deaths).toHaveValue('7');
  await deaths.fill('8');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([{ _customAdventureMaxItopodDeaths: 8 }]);
  await expect(card.getByRole('status')).toHaveText('Active: 2 deaths remaining / Floor 321');
  await page.evaluate(() => { (window as FixtureWindow).__pushRuntime.maxItopodPush = { active: true, remainingDeaths: 1, floor: 322 }; });
  await expect(card.getByRole('status')).toHaveText('Active: 1 deaths remaining / Floor 322');
  await page.evaluate(() => {
    const fixture = window as FixtureWindow;
    fixture.__pushSettings._customAdventureMode = 0;
    fixture.__pushRuntime.maxItopodPush = { active: false, remainingDeaths: 0, floor: 322 };
  });
  await expect(page.getByRole('article', { name: 'Adventure automation' }).getByRole('combobox', { name: 'Adventure mode', exact: true })).toHaveValue('0');
  await expect(card.getByRole('button', { name: 'Push ITOPOD', exact: true })).toHaveAttribute('aria-pressed', 'false');
  await expect(deaths).toHaveCount(0);
  await expect(card.getByRole('status')).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([{ _customAdventureMaxItopodDeaths: 8 }]);
  expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls)).not.toContain('enqueue_command');
});

for (const scenario of ['disconnected', 'stale']) {
  test(`settings stay editable but status is disconnected when ${scenario}`, async ({ page }) => {
    await openAdventure(page, { _customAdventureMode: 3 }, scenario === 'disconnected'
      ? { externalUiEnabled: false } : { updatedAt: '2000-01-01T00:00:00Z' });
    const card = page.getByRole('article', { name: 'Manual Adventure', exact: true });
    await expect(card.getByRole('status')).toHaveText('Disconnected');
    await card.getByRole('spinbutton', { name: 'Max ITOPOD death limit' }).fill('5');
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([{ _customAdventureMaxItopodDeaths: 5 }]);
    expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls)).not.toContain('enqueue_command');
  });
}

test('missing runtime telemetry does not claim an active or idle push', async ({ page }) => {
  await openAdventure(page, { _customAdventureMode: 3 }, { maxItopodPush: null });
  await expect(page.getByRole('article', { name: 'Manual Adventure', exact: true }).getByRole('status')).toHaveText('Max ITOPOD push status not reported.');
});

test('failed autosave retains the saved-setting draft for explicit retry', async ({ page }) => {
  await openAdventure(page, { _customAdventureMode: 3, _customAdventureMaxItopodDeaths: 7 });
  await page.evaluate(() => { (window as FixtureWindow).__pushRejection = 'Settings file is locked.'; });
  const deaths = page.getByRole('spinbutton', { name: 'Max ITOPOD death limit' });
  await deaths.fill('8');
  await expect(page.getByRole('alert')).toContainText('Settings file is locked.');
  await expect(deaths).toHaveValue('8');
  expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings._customAdventureMaxItopodDeaths)).toBe(7);
  await page.evaluate(() => { (window as FixtureWindow).__pushRejection = ''; });
  await page.getByRole('button', { name: 'Retry saving' }).click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([{ _customAdventureMaxItopodDeaths: 8 }]);
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('manual adventure loadout appears for explicit manual combat, manual rules, and max pushes', async ({ page }) => {
  await openAdventure(page);
  const manual = page.getByRole('article', { name: 'Manual Adventure', exact: true });
  const loadout = manual.getByRole('combobox', { name: 'Manual adventure loadout', exact: true });
  await expect(loadout).toHaveCount(0);
  await manual.getByRole('checkbox', { name: 'Manual adventure', exact: true }).check();
  await expect(loadout).toHaveValue('-1');
  await loadout.selectOption('2');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualAdventureLoadout)).toBe(2);
  await manual.getByRole('checkbox', { name: 'Manual adventure', exact: true }).uncheck();
  await expect(loadout).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualAdventureEnabled)).toBe(false);
  await page.evaluate(() => {
    (window as FixtureWindow).__pushSettings._customAdventureRules = [
      { Enabled: true, MinPower: 0, MinToughness: 0, MinRegen: 0, Mode: 4, Zone: 4 }
    ];
  });
  await expect(loadout).toHaveValue('2');
  await expect(manual.getByRole('combobox', { name: 'Manual adventure zone', exact: true })).toHaveCount(0);
  await page.evaluate(() => {
    const fixture = window as FixtureWindow;
    fixture.__pushSettings._customAdventureRules = [];
    fixture.__pushSettings._customAdventureEnabled = true;
    fixture.__pushSettings._customAdventureMode = 3;
  });
  await expect(loadout).toHaveCount(0);
  await manual.getByRole('checkbox', { name: 'Manual adventure', exact: true }).check();
  await expect(loadout).toHaveValue('2');
  await expect(manual.getByRole('combobox', { name: 'Manual adventure zone', exact: true })).toHaveValue('4');
});

test('manual titan card configures and opts in without Manual Adventure or global presets', async ({ page }) => {
  await openAdventure(page);
  const card = page.getByRole('article', { name: 'Manual Titan Fights', exact: true });
  const enabled = card.getByRole('checkbox', { name: 'Enable manual titan fights', exact: true });
  const target = card.getByRole('combobox', { name: 'Titan target', exact: true });
  const loadout = card.getByRole('combobox', { name: 'Titan loadout', exact: true });
  const gate = card.getByRole('checkbox', { name: 'Require AT targets reached', exact: true });
  const deaths = card.getByRole('spinbutton', { name: 'Max titan deaths', exact: true });
  await expect(enabled).not.toBeChecked();
  await expect(target).toHaveValue('16');
  await expect(target.locator('option:checked')).toHaveText('WALDERP');
  await expect(target.locator('option')).toHaveCount(12);
  await expect(loadout).toHaveValue('1');
  await expect(loadout.locator('option')).toHaveText([
    'Keep equipped', 'sqrt(Power * Toughness) * Move Speed',
    'Power * Toughness * Move Speed', '(Power + Toughness) * Move Speed'
  ]);
  await expect(gate).toBeChecked();
  await expect(gate).toHaveAccessibleDescription('Set positive Power and Toughness targets in-game.');
  await expect(deaths).toHaveValue('3');
  await expect(card).toContainText('Gear swaps recall energy, magic, and R3');
  const limits = card.locator('details');
  await expect(limits).not.toHaveAttribute('open');
  await limits.getByText('Attempt limits', { exact: true }).click();
  for (const guidance of ['per-spawn death limit', '10 minutes of active attempts', 'Find Walderp in-game', 'resets the attempt and remaining deaths']) await expect(limits).toContainText(guidance);
  await expect(card.getByRole('status')).toHaveText('Manual titan status not reported.');
  expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([]);
  await target.selectOption('19');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualTitanZone)).toBe(19);
  await loadout.selectOption('-1');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualTitanLoadout)).toBe(-1);
  await gate.uncheck();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualTitanRequireAT)).toBe(false);
  await deaths.fill('7');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualTitanMaxDeaths)).toBe(7);
  await enabled.check();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualTitanEnabled)).toBe(true);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Misc', exact: true }).click();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(target).toHaveValue('19');
  await expect(loadout).toHaveValue('-1');
  await expect(gate).not.toBeChecked();
  await expect(deaths).toHaveValue('7');
  await expect(page.getByRole('checkbox', { name: 'Manual adventure', exact: true })).not.toBeChecked();
  expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([
    { _manualTitanZone: 19 }, { _manualTitanLoadout: -1 }, { _manualTitanRequireAT: false },
    { _manualTitanMaxDeaths: 7 }, { _manualTitanEnabled: true }
  ]);
  expect(await page.evaluate(() => (window as FixtureWindow).__nativeCalls)).not.toContain('enqueue_command');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('manual titan death drafts validate locally and do not replace the runtime budget', async ({ page }) => {
  await openAdventure(page, { _manualTitanEnabled: true, _manualTitanMaxDeaths: 7 }, {
    manualTitan: { active: true, remainingDeaths: 2, zone: 16, status: 'Fighting' }
  });
  const card = page.getByRole('article', { name: 'Manual Titan Fights', exact: true });
  const deaths = card.getByRole('spinbutton', { name: 'Max titan deaths', exact: true });
  await expect(card.getByRole('status')).toHaveText('Active: WALDERP / 2 deaths remaining / Fighting');
  for (const invalid of ['', '0', '-1', '1.5', '1001']) {
    await deaths.fill(invalid);
    await expect(page.getByRole('alert')).toContainText('_manualTitanMaxDeaths');
    await expect(deaths).toHaveValue(invalid);
    expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([]);
  }
  for (const limit of [1, 1000]) {
    await deaths.fill(String(limit));
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualTitanMaxDeaths)).toBe(limit);
    await expect(card.getByRole('status')).toHaveText('Active: WALDERP / 2 deaths remaining / Fighting');
  }
  await page.evaluate(() => {
    (window as FixtureWindow).__pushRuntime.manualTitan = { active: false, remainingDeaths: 0, zone: 16, status: 'Waiting for next spawn' };
  });
  await expect(card.getByRole('status')).toHaveText('Inactive: WALDERP / 0 deaths remaining / Waiting for next spawn');
  await page.evaluate(() => { (window as FixtureWindow).__pushRuntime.externalUiEnabled = false; });
  await expect(card.getByRole('status')).toHaveText('Disconnected');
  expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([
    { _manualTitanMaxDeaths: 1 }, { _manualTitanMaxDeaths: 1000 }
  ]);
});

test('manual titan status does not present stale heartbeat telemetry as connected', async ({ page }) => {
  await openAdventure(page, { _manualTitanEnabled: true }, {
    updatedAt: '2000-01-01T00:00:00Z',
    manualTitan: { active: true, remainingDeaths: 3, zone: 19, status: 'Fighting' }
  });
  const card = page.getByRole('article', { name: 'Manual Titan Fights', exact: true });
  await expect(card.getByRole('status')).toHaveText('Disconnected');
  expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([]);
});

test('titan-only combat exposes shared toggles without normal adventure targets or loadouts', async ({ page }) => {
  await openAdventure(page, {
    _manualAdventureEnabled: false, _customAdventureRules: [], _manualTitanEnabled: false,
    _manualAdventureRecoverHealth: true, _manualAdventurePrecastBuffs: false,
    _manualAdventureFastCombat: true, _manualAdventureBeastMode: false,
    _manualAdventureBossOnly: true, _manualAdventureAllowFallback: true,
    _manualAdventureLoadout: 2
  });
  const manual = page.getByRole('article', { name: 'Manual Adventure', exact: true });
  const enabled = page.getByRole('checkbox', { name: 'Enable manual titan fights', exact: true });
  await expect(manual.getByRole('checkbox')).toHaveCount(1);
  await enabled.check();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualTitanEnabled)).toBe(true);
  await expect(manual.getByRole('checkbox')).toHaveCount(5);
  for (const name of ['Recover health', 'Precast buffs', 'Fast combat', 'Beast mode']) {
    await expect(manual.getByRole('checkbox', { name, exact: true })).toBeVisible();
  }
  for (const name of ['Bosses only', 'Allow fallback']) {
    await expect(manual.getByRole('checkbox', { name, exact: true })).toHaveCount(0);
  }
  await expect(manual.getByRole('combobox')).toHaveCount(0);
  await expect(manual.getByRole('checkbox', { name: 'Manual adventure', exact: true })).not.toBeChecked();
  await manual.getByRole('checkbox', { name: 'Precast buffs', exact: true }).check();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualAdventurePrecastBuffs)).toBe(true);
  await enabled.uncheck();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualTitanEnabled)).toBe(false);
  await expect(manual.getByRole('checkbox')).toHaveCount(1);
  await enabled.check();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__pushSettings._manualTitanEnabled)).toBe(true);
  await expect(manual.getByRole('checkbox', { name: 'Precast buffs', exact: true })).toBeChecked();
  await expect(manual.getByRole('combobox')).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([
    { _manualTitanEnabled: true }, { _manualAdventurePrecastBuffs: true },
    { _manualTitanEnabled: false }, { _manualTitanEnabled: true }
  ]);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushSettings)).toMatchObject({
    _manualAdventureEnabled: false, _manualAdventureZone: 4, _manualAdventureLoadout: 2,
    _manualAdventureBossOnly: true, _manualAdventureAllowFallback: true
  });
});

test('manual titan recovery blocks combat and displays restore errors until backend recovery clears', async ({ page }) => {
  await openAdventure(page, { _manualTitanEnabled: false }, {
    manualTitan: { active: true, recovering: true, recoveryError: 'Original helmet is unavailable', remainingDeaths: 2, zone: 16, status: 'Recovery pending' }
  });
  const card = page.getByRole('article', { name: 'Manual Titan Fights', exact: true });
  await expect(card.getByRole('checkbox', { name: 'Enable manual titan fights', exact: true })).not.toBeChecked();
  await expect(card.getByRole('status')).toHaveText('Recovering gear: WALDERP / Combat blocked');
  await expect(card.getByRole('alert')).toHaveText('Exact gear restore failed: Original helmet is unavailable');
  await expect(card.locator('.notice').filter({ hasText: 'All combat stays blocked' })).toContainText('until original gear is restored');
  await page.evaluate(() => { (window as FixtureWindow).__pushRuntime.externalUiEnabled = false; });
  await expect(card.getByRole('status')).toHaveText('Disconnected');
  await expect(card.getByRole('alert')).toHaveCount(0);
  await expect(card).not.toContainText('Original helmet is unavailable');
  await page.evaluate(() => {
    const fixture = window as FixtureWindow;
    fixture.__pushRuntime.externalUiEnabled = true;
    fixture.__pushRuntime.manualTitan = { active: false, recovering: false, recoveryError: '', remainingDeaths: 2, zone: 16, status: 'Waiting for next spawn' };
  });
  await expect(card.getByRole('status')).toHaveText('Inactive: WALDERP / 2 deaths remaining / Waiting for next spawn');
  await expect(card.getByRole('alert')).toHaveCount(0);
  await expect(card.locator('.notice').filter({ hasText: 'All combat stays blocked' })).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__pushPatches)).toEqual([]);
});
