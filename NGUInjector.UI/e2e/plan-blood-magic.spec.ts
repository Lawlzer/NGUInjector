import { expect, test, type Page } from '@playwright/test';

type FixtureWindow = Window & {
  __bloodState: { connected: boolean; capability: number | null; running: boolean; telemetryPlan: string | null; plans: Record<string, Record<string, unknown>>; settings: Record<string, unknown>; writes: { name: string; plan: Record<string, unknown> }[]; patches: Record<string, unknown>[] };
};

// Only native IPC is mocked: App, controller, inline edit sessions, validation and autosave are real.
async function installFixture(page: Page, connected = true, capability: number | null = 1, noPlans = false): Promise<void> {
  await page.addInitScript(({ connected, capability, noPlans }) => {
    let revision = 1;
    const base = { Version: 17, __externalUiRevision: 'a'.repeat(64), Stages: [{ Name: 'NGU', Percent: 100, EnergyFocus: 5, MagicFocus: 6 }], RebirthHistory: [], UnknownRoot: { keep: 'untouched' } };
    const fixture: FixtureWindow['__bloodState'] = {
      connected, capability, running: true, telemetryPlan: 'alpha',
      plans: noPlans ? {} : {
        alpha: { ...base, Name: 'Stored name differs from file', AutoBloodSpellsEnabled: null, BloodSpaghettiGoalPercent: null, IronPillSaveSeconds: null },
        beta: { ...base, Name: 'beta', AutoBloodSpellsEnabled: false, AutoIronPillEnabled: false, BloodSpaghettiGoalPercent: 0, BloodCounterfeitGoalPercent: 0, IronPillSaveSeconds: 0, IronPillMinWorth: 0 }
      },
      settings: { _customPlanFile: 'alpha', _useCustomRebirthPlan: true, _customAutoBloodSpellsEnabled: true, _customBloodSpaghettiGoalPercent: 35, _customBloodCounterfeitGoalPercent: 450, _customIronPillSaveSeconds: 900, _customIronPillMinWorth: 125.5 },
      writes: [], patches: []
    };
    if (noPlans) { fixture.running = false; fixture.telemetryPlan = ''; fixture.settings._useCustomRebirthPlan = false; }
    Object.assign(window, { __bloodState: fixture, __TAURI_INTERNALS__: { invoke: async (command: string, args: Record<string, unknown> = {}) => {
      const casPath = '/e2e/fixtures/settings-cas.ts';
      const cas = await import(casPath);
      switch (command) {
        case 'get_ngui_state': return {
          runtime_dir: 'C:\\BloodFixture', marker_enabled: true, connected: fixture.connected,
          protocol: { version: 3 }, ...await cas.settingsSnapshot({ ...fixture.settings, _useCustomRebirthPlan: fixture.running }), plans: Object.keys(fixture.plans), profiles: [], logs: [], command_results: [],
          runtime: {
            externalUiEnabled: fixture.connected, status: fixture.connected ? 'running' : 'unloaded', updatedAt: new Date().toISOString(), startedAt: 'blood-session', protocolVersion: 3, settingsCasVersion: 1,
            supportedCommands: ['readPlan', 'savePlan', 'applySettingsPatch'], planVersion: 17, planBloodMagicVersion: fixture.capability,
            settings: { customPlanFile: 'alpha', useCustomRebirthPlan: fixture.running },
            bloodGoals: { available: true, planName: fixture.telemetryPlan, autoBloodSpellsEnabled: true,
              spaghetti: { goalPercent: 35, currentPercent: 10, remainingBlood: 90000, estimatedSeconds: 90, status: 'Casting' },
              counterfeit: { goalPercent: 450, currentPercent: 450, remainingBlood: 0, status: 'Goal reached' } },
            ironPill: { available: true, planName: fixture.telemetryPlan, saveSeconds: 900, minWorth: 125.5, eligible: false, reason: 'Live alpha blocker.' }
          }
        };
        case 'read_plan': return JSON.stringify(fixture.plans[String(args.name)]);
        case 'save_plan': {
          const name = String(args.name);
          const plan = JSON.parse(String(args.planJson));
          fixture.writes.push({ name, plan: structuredClone(plan) });
          fixture.plans[name] = { ...plan, __externalUiRevision: (++revision).toString(16).padStart(64, '0') };
          return { success: true, mode: fixture.connected ? 'connected' : 'offline' };
        }
        case 'save_settings_patch_json': {
          fixture.settings._useCustomRebirthPlan = fixture.running;
          const receipt = await cas.commitSettings(fixture.settings, args, fixture.connected ? 'connected' : 'offline');
          fixture.patches.push(cas.settingsPatch(args));
          return receipt;
        }
        case 'plugin:dialog|message': return 'Yes';
        default: throw new Error(`Unexpected native invocation: ${command}`);
      }
    } } });
    localStorage.clear();
  }, { connected, capability, noPlans });
  await page.goto('/');
  await expect(page.getByRole('article', { name: 'Blood Magic', exact: true })).toBeVisible();
  if (!noPlans) await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('alpha');
}

const state = (page: Page) => page.evaluate(() => (window as unknown as FixtureWindow).__bloodState);
async function editNumber(page: Page, label: string, value: string): Promise<void> {
  await page.getByRole('button', { name: `Edit ${label}`, exact: true }).click();
  const input = page.getByRole('spinbutton', { name: label, exact: true });
  await input.fill(value);
  const fields: Record<string, string> = {
    'Spaghetti goal (%)': 'BloodSpaghettiGoalPercent',
    'Counterfeit goal (%)': 'BloodCounterfeitGoalPercent',
    'Iron Pill saving time (s)': 'IronPillSaveSeconds',
    'Iron Pill minimum worth': 'IronPillMinWorth'
  };
  const key = fields[label];
  await expect.poll(async () => {
    const fixture = await state(page);
    return fixture.writes.at(-1)?.plan[key] ?? fixture.patches.at(-1)?.[`_custom${key}`];
  }).toBe(Number(value));
  await expect(input).toHaveValue(value);
  await expect(page.getByRole('group', { name: label, exact: true }).getByRole('button', { name: 'Save', exact: true })).toHaveCount(0);
  await input.press('Escape');
}

test('plan goals, thresholds and booleans save independently per file without global patches', async ({ page }) => {
  await installFixture(page);
  const before = await state(page);
  await expect(page.getByRole('checkbox', { name: 'Auto blood spells', exact: true })).toBeChecked();
  await expect(page.getByRole('checkbox', { name: 'Auto Iron Pill', exact: true })).toBeChecked();
  await editNumber(page, 'Spaghetti goal (%)', '60');
  await editNumber(page, 'Counterfeit goal (%)', '800');
  await editNumber(page, 'Iron Pill saving time (s)', '1200');
  await editNumber(page, 'Iron Pill minimum worth', '5000.25');
  await page.getByRole('checkbox', { name: 'Auto blood spells', exact: true }).uncheck();
  await expect.poll(async () => (await state(page)).plans.alpha.AutoBloodSpellsEnabled).toBe(false);
  await page.getByRole('checkbox', { name: 'Auto Iron Pill', exact: true }).uncheck();
  await expect.poll(async () => (await state(page)).plans.alpha.AutoIronPillEnabled).toBe(false);
  const alpha = (await state(page)).plans.alpha;
  expect(alpha).toMatchObject({ BloodSpaghettiGoalPercent: 60, BloodCounterfeitGoalPercent: 800, AutoBloodSpellsEnabled: false, AutoIronPillEnabled: false, IronPillSaveSeconds: 1200, IronPillMinWorth: 5000.25, UnknownRoot: { keep: 'untouched' } });
  await page.getByRole('combobox', { name: 'Plan', exact: true }).selectOption('beta');
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('0%');
  await expect(page.getByRole('checkbox', { name: 'Auto blood spells', exact: true })).not.toBeChecked();
  await expect(page.getByRole('button', { name: 'Edit Iron Pill saving time (s)', exact: true })).toHaveText('0s');
  await expect(page.getByRole('button', { name: 'Edit Iron Pill minimum worth', exact: true })).toHaveText('0');
  await editNumber(page, 'Spaghetti goal (%)', '15');
  await page.getByRole('checkbox', { name: 'Auto blood spells', exact: true }).check();
  await expect.poll(async () => (await state(page)).plans.beta.AutoBloodSpellsEnabled).toBe(true);
  await page.getByRole('combobox', { name: 'Plan', exact: true }).selectOption('alpha');
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('60%');
  const after = await state(page);
  expect(after.plans.alpha).toEqual(alpha);
  expect(after.plans.beta).toMatchObject({ BloodSpaghettiGoalPercent: 15, AutoBloodSpellsEnabled: true, BloodCounterfeitGoalPercent: 0, IronPillSaveSeconds: 0, IronPillMinWorth: 0 });
  expect(after.writes.map(write => write.name)).toContain('beta');
  expect(after.settings).toEqual(before.settings);
  expect(after.patches).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('missing/null values inherit globals without writes and explicit overrides can return to inheritance', async ({ page }) => {
  await installFixture(page);
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('35%');
  await expect(page.getByRole('button', { name: 'Edit Counterfeit goal (%)', exact: true })).toHaveText('450%');
  await expect(page.getByRole('button', { name: 'Edit Iron Pill saving time (s)', exact: true })).toHaveText('15m');
  await expect(page.getByRole('button', { name: 'Edit Iron Pill minimum worth', exact: true })).toHaveText('125.5');
  await expect(page.getByRole('button', { name: 'Use global defaults', exact: true })).toBeDisabled();
  expect((await state(page)).writes).toEqual([]);
  await page.getByRole('combobox', { name: 'Plan', exact: true }).selectOption('beta');
  await page.getByRole('button', { name: 'Use global defaults', exact: true }).click();
  await expect.poll(async () => (await state(page)).plans.beta).toMatchObject({ AutoBloodSpellsEnabled: null, BloodSpaghettiGoalPercent: null, BloodCounterfeitGoalPercent: null, IronPillSaveSeconds: null, IronPillMinWorth: null, AutoIronPillEnabled: false });
  await expect(page.getByRole('checkbox', { name: 'Auto blood spells', exact: true })).toBeChecked();
  await expect(page.getByRole('checkbox', { name: 'Auto Iron Pill', exact: true })).not.toBeChecked();
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('35%');
  await expect(page.getByRole('button', { name: 'Edit Counterfeit goal (%)', exact: true })).toHaveText('450%');
  await expect(page.getByRole('button', { name: 'Edit Iron Pill saving time (s)', exact: true })).toHaveText('15m');
  await expect(page.getByRole('button', { name: 'Edit Iron Pill minimum worth', exact: true })).toHaveText('125.5');
  await expect(page.getByRole('button', { name: 'Use global defaults', exact: true })).toBeDisabled();
  expect((await state(page)).patches).toEqual([]);
});

test('runtime telemetry never masquerades as another editor plan or Quick values', async ({ page }) => {
  await installFixture(page);
  const card = page.getByRole('article', { name: 'Blood Magic', exact: true });
  const identity = card.locator(':scope > p').first();
  await expect(identity).toHaveText('Plan: alpha');
  await expect(card).toContainText('ETA ~1m 30s');
  await expect(card).toContainText('Blocked: Live alpha blocker.');
  await page.evaluate(() => { const beta = (window as unknown as FixtureWindow).__bloodState.plans.beta; beta.BloodSpaghettiGoalPercent = 35; beta.BloodCounterfeitGoalPercent = 450; });
  await page.getByRole('combobox', { name: 'Plan', exact: true }).selectOption('beta');
  await expect(identity).toHaveText('Plan: beta / Running: alpha');
  await expect(card).not.toContainText('ETA');
  await expect(card).not.toContainText('Live alpha blocker.');
  await page.evaluate(() => { const f = (window as unknown as FixtureWindow).__bloodState; f.running = false; f.telemetryPlan = ''; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(identity).toHaveText('Plan: beta / Running: Quick');
  await expect(card).not.toContainText('ETA');
  await expect(card).not.toContainText('Live alpha blocker.');
  await page.getByRole('combobox', { name: 'Plan', exact: true }).selectOption('alpha');
  await expect(identity).toHaveText('Plan: alpha / Running: Quick');
  await expect(card).not.toContainText('ETA');
  await page.evaluate(() => { const f = (window as unknown as FixtureWindow).__bloodState; f.connected = false; f.running = true; f.telemetryPlan = 'beta'; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(identity).toHaveText('Plan: alpha');
  await expect(card).not.toContainText('ETA');
  await expect(card).not.toContainText('Live alpha blocker.');
  await page.evaluate(() => { const f = (window as unknown as FixtureWindow).__bloodState; f.connected = true; f.telemetryPlan = 'alpha'; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(identity).toHaveText('Plan: alpha');
  await expect(card).toContainText('ETA ~1m 30s');
  await expect(card).toContainText('Blocked: Live alpha blocker.');
  expect((await state(page)).writes).toEqual([]);
  expect((await state(page)).patches).toEqual([]);
});

test('old connected injectors block controls and Advanced JSON blood edits but offline edits work', async ({ page }) => {
  await installFixture(page, true, null);
  const card = page.getByRole('article', { name: 'Blood Magic', exact: true });
  await expect(card.getByRole('status').filter({ hasText: 'Update and reconnect' })).toHaveText('Update and reconnect the injector to edit plan Blood Magic.');
  await expect(card).not.toContainText('planBloodMagicVersion');
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toBeDisabled();
  await expect(page.getByRole('checkbox', { name: 'Auto blood spells', exact: true })).toBeDisabled();
  await expect(page.getByRole('checkbox', { name: 'Auto Iron Pill', exact: true })).toBeDisabled();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Advanced', exact: true }).click();
  await page.getByText('Advanced plan JSON', { exact: true }).click();
  const editor = page.getByRole('textbox', { name: 'Advanced Plan JSON', exact: true });
  const original = JSON.parse(await editor.inputValue());
  await editor.fill(JSON.stringify({ ...original, BloodSpaghettiGoalPercent: 123 }));
  await expect(page.getByRole('button', { name: 'Apply edits', exact: true })).toHaveCount(0);
  await expect(page.getByRole('alert')).toContainText('Update and reconnect, or edit offline.');
  expect((await state(page)).writes).toEqual([]);
  await editor.fill(JSON.stringify(original));
  await page.evaluate(() => { (window as unknown as FixtureWindow).__bloodState.connected = false; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Dashboard', exact: true }).click();
  await editNumber(page, 'Spaghetti goal (%)', '0');
  expect((await state(page)).plans.alpha.BloodSpaghettiGoalPercent).toBe(0);
  expect((await state(page)).patches).toEqual([]);
});

test('inline drafts survive tab navigation and invalid goals do not reach transport', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-18T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-18T12:00:01Z'));
  await installFixture(page);
  await page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true }).click();
  const input = page.getByRole('spinbutton', { name: 'Spaghetti goal (%)', exact: true });
  await input.fill('72');
  await page.getByRole('button', { name: 'Misc', exact: true }).click();
  await expect(page.getByRole('checkbox', { name: 'Auto blood spells', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(input).toHaveValue('72');
  for (const invalid of ['', '-1', '1.5', '100001']) {
    await input.fill(invalid);
    await page.clock.runFor(500);
    await expect(page.getByRole('group', { name: 'Spaghetti goal (%)', exact: true }).getByRole('alert')).toBeVisible();
  }
  expect((await state(page)).writes).toEqual([]);
  await input.fill('72');
  await page.clock.runFor(349);
  expect((await state(page)).writes).toEqual([]);
  await page.clock.runFor(1);
  await expect.poll(async () => (await state(page)).plans.alpha.BloodSpaghettiGoalPercent).toBe(72);
  await expect(input).toHaveValue('72');
  await input.press('Escape');
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('72%');
  expect((await state(page)).patches).toEqual([]);
});

test('queued autosaves and retries recheck capability before plan transport', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-18T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-18T12:00:01Z'));
  await installFixture(page);
  await page.getByRole('checkbox', { name: 'Auto blood spells', exact: true }).uncheck();
  await page.evaluate(() => { (window as unknown as FixtureWindow).__bloodState.capability = null; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await page.clock.runFor(500);
  await expect(page.getByRole('alert')).toContainText('Update and reconnect, or edit offline.');
  expect((await state(page)).writes).toEqual([]);
  await page.getByRole('button', { name: 'Retry saving', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Update and reconnect, or edit offline.');
  expect((await state(page)).writes).toEqual([]);
  expect((await state(page)).patches).toEqual([]);
});

test('with no editor plan, Quick defaults retain legacy global transport', async ({ page }) => {
  await installFixture(page, true, null, true);
  await expect(page.getByRole('article', { name: 'Blood Magic', exact: true }).locator(':scope > p').first()).toHaveText('Editing: Global defaults');
  await expect(page.getByRole('button', { name: 'Use global defaults', exact: true })).toHaveCount(0);
  await editNumber(page, 'Spaghetti goal (%)', '55');
  await page.getByRole('checkbox', { name: 'Auto blood spells', exact: true }).uncheck();
  await expect.poll(async () => (await state(page)).settings._customAutoBloodSpellsEnabled).toBe(false);
  expect((await state(page)).patches).toEqual([{ _customBloodSpaghettiGoalPercent: 55 }, { _customAutoBloodSpellsEnabled: false }]);
  expect((await state(page)).writes).toEqual([]);
});
