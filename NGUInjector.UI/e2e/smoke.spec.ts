import { expect, test, type Page } from '@playwright/test';

type FixtureWindow = typeof window & {
  __fixtureCalls: string[];
  __fixtureSettingWrites: Record<string, unknown>[];
  __fixturePlanWrites: Record<string, unknown>[];
  __fixturePlanNames: string[];
  __fixtureCreatedPlans: { name: string; source: Record<string, unknown> | null }[];
  __fixtureDeletedPlans: string[];
  __fixtureState: () => { settings: Record<string, unknown>; plan: Record<string, unknown> };
  __fixtureFailNextSetting?: boolean;
  __fixtureFailNextPlan?: boolean;
  __fixtureConfirm?: boolean;
  __fixtureRuntimeOverrides?: Record<string, unknown>;
};

// Mock only native IPC. The real Svelte components, controller, validation, and styles run in Chromium.
async function installNativeFixture(page: Page, connected = true): Promise<void> {
  await page.addInitScript(({ connected }) => {
    const calls: string[] = [];
    const settingWrites: Record<string, unknown>[] = [];
    const planWrites: Record<string, unknown>[] = [];
    const planNames: string[] = [];
    const plans = ['default', '24hr'];
    const createdPlans: { name: string; source: Record<string, unknown> | null }[] = [];
    const deletedPlans: string[] = [];
    let revision = 10;
    const settings = {
      _customPlanFile: 'default', _allocationFile: 'default', _autoRebirth: false,
      _useCustomRebirthPlan: false, _customAutoBoostEnabled: true, _manageEnergy: true,
      _customAdventureMode: 0, _customAdventureEnabled: false, _manualAdventureZone: 0,
      _customAutoBoostCubePowerRatio: 2, _customAugmentsBloodEstimateSeconds: 300,
      _customBloodSpaghettiGoalPercent: 30, _customBloodCounterfeitGoalPercent: 400,
      _customIronPillSaveSeconds: 900, _customIronPillMinWorth: 0,
      _customIronPillHistory: [{ CastAtUtc: '2026-10-14T12:30:00Z', WaitSeconds: 900, Worth: 1234.5, RebirthSeconds: 7200 }]
    };
    let plan = {
      Version: 16, Name: 'default', AutoRebirthEnabled: false, TimeTriggerEnabled: true,
      TimeTriggerSeconds: 3600, NumberTriggerEnabled: false, NumberMultiplierTarget: 100,
      NumberClimbEnabled: false, NumberClimbMinSeconds: 480, NumberClimbMultiplierTarget: 100,
      TriggerMode: 0, StageSchedulerEnabled: true, StageDurationSeconds: 1800, StageLoopStartIndex: 1,
      EnergyNguWeights: [0, 0, 0, 0, 1, 0, 1, 0, 0], MagicNguWeights: [1, 0, 0, 0, 0, 0, 0],
      GearGoal: 9, FightScoreMode: 0, ActiveGoalPresetIndex: 0,
      GoalPresets: [{ Name: 'Default', GearGoal: 9 }], RebirthHistory: [],
      Stages: [
        { Name: 'Time Machine opener', Percent: 50, EnergyFocus: 1, MagicFocus: 1, GearGoal: 5, CompletionMode: 0, EnergyPriorities: [], MagicPriorities: [] },
        { Name: 'NGU sustain', Percent: 50, EnergyFocus: 5, MagicFocus: 6, GearGoal: 2, CompletionMode: 0, EnergyPriorities: [], MagicPriorities: [] }
      ],
      __externalUiRevision: revision.toString(16).padStart(64, '0')
    };
    // Native data outlives an App remount; only acknowledged writes survive a browser reload.
    const persisted = JSON.parse(sessionStorage.getItem('native-fixture') ?? 'null') as { settings: typeof settings; plan: typeof plan } | null;
    if (persisted) {
      Object.assign(settings, persisted.settings);
      plan = persisted.plan;
      revision = Number.parseInt(plan.__externalUiRevision, 16);
    }
    const persist = () => sessionStorage.setItem('native-fixture', JSON.stringify({ settings, plan }));
    const results: Record<string, object> = {};
    const state = () => ({
      runtime_dir: 'C:\\Fixture\\NGUInjector', marker_enabled: true, connected,
      protocol: { version: 3 }, settings, profiles: ['default'], plans,
      logs: [{ name: 'inject.log', size: 30 }], command_results: [],
      runtime: {
        externalUiEnabled: connected, status: connected ? 'running' : 'unloaded', protocolVersion: 3,
        startedAt: '2026-10-14T10:00:00Z', updatedAt: new Date().toISOString(),
        customStatus: 'Fixture automation idle.', version: 'test', activeFocusGoal: '',
        supportedCommands: ['readPlan', 'applySettingsPatch', 'savePlan', 'createPlan', 'deletePlan', 'focusGoal', 'stopFocus', 'manualFightPrep', 'manualRebirth', 'quickSave', 'safeProbe', 'previewGear'],
        character: { rebirthSeconds: 420 }, settings: { customPlanFile: settings._customPlanFile, useCustomRebirthPlan: settings._useCustomRebirthPlan, autoRebirth: settings._autoRebirth },
        bloodGoals: {
          available: true, status: 'available', reason: '',
          spaghetti: { currentPercent: 10, goalPercent: 30, requiredBlood: 100000, remainingBlood: 90000, bloodPerSecond: 1000, estimatedSeconds: 90, status: 'Casting', productionAssumption: 'Optimal ritual with all available magic' },
          counterfeit: { currentPercent: 400, goalPercent: 400, requiredBlood: 100000, remainingBlood: 0, bloodPerSecond: 1000, estimatedSeconds: 0, status: 'Goal reached', productionAssumption: 'Optimal ritual with all available magic' }
        },
        ironPill: { available: true, eligible: false, status: 'blocked', reason: 'Custom auto-rebirth is disabled.' },
        planVersion: 16,
        ...((window as FixtureWindow).__fixtureRuntimeOverrides ?? {})
      }
    });
    const native = {
      async invoke(command: string, args: Record<string, unknown> = {}) {
        calls.push(command);
        switch (command) {
          case 'get_ngui_state': return state();
          // The controller owns the requested editor name; these tests share one plan body.
          case 'read_plan': return JSON.stringify(plan);
          case 'create_plan': {
            const name = String(args.requestedName);
            if (plans.includes(name)) throw new Error('Plan already exists.');
            createdPlans.push({ name, source: args.sourceJson ? JSON.parse(String(args.sourceJson)) : null });
            plans.push(name);
            return name;
          }
          case 'delete_plan': {
            const name = String(args.name);
            if (name === 'default' || name === '24hr' || !plans.includes(name)) throw new Error('Cannot delete this plan.');
            deletedPlans.push(name);
            plans.splice(plans.indexOf(name), 1);
            settings._customPlanFile = 'default';
            return { success: true, mode: connected ? 'connected' : 'offline', data: { selectedPlan: 'default' }, result: { data: { selectedPlan: 'default' } } };
          }
          case 'save_settings_patch': {
            settingWrites.push(structuredClone(args.patch as Record<string, unknown>));
            const fixture = window as FixtureWindow;
            if (fixture.__fixtureFailNextSetting) { fixture.__fixtureFailNextSetting = false; throw new Error('Setting save failed.'); }
            Object.assign(settings, args.patch);
            persist();
            return { success: true, mode: connected ? 'connected' : 'offline' };
          }
          case 'save_plan': {
            const next = JSON.parse(String(args.planJson));
            planWrites.push(next);
            planNames.push(String(args.name));
            const fixture = window as FixtureWindow;
            if (fixture.__fixtureFailNextPlan) { fixture.__fixtureFailNextPlan = false; throw new Error('Plan save failed.'); }
            plan = { ...next, __externalUiRevision: (++revision).toString(16).padStart(64, '0') };
            persist();
            return { success: true, mode: connected ? 'connected' : 'offline' };
          }
          case 'tail_log': return 'Injected successfully\nFixture safe log';
          case 'enqueue_command': {
            const envelope = args.envelope as { command: string };
            const id = `fixture-${calls.length}`;
            results[id] = { id, command: envelope.command, success: true, message: 'Fixture command completed.' };
            return id;
          }
          case 'read_command_result': return results[String(args.id)] ?? null;
          case 'plugin:dialog|ask': return (window as FixtureWindow).__fixtureConfirm !== false;
          case 'plugin:dialog|message': return (window as FixtureWindow).__fixtureConfirm === false ? 'No' : 'Yes';
          case 'set_external_ui_enabled': return null;
          default: throw new Error(`Unexpected native invocation: ${command}`);
        }
      }
    };
    Object.assign(window, {
      __TAURI_INTERNALS__: native, __fixtureCalls: calls,
      __fixtureSettingWrites: settingWrites, __fixturePlanWrites: planWrites,
      __fixturePlanNames: planNames,
      __fixtureCreatedPlans: createdPlans, __fixtureDeletedPlans: deletedPlans,
      __fixtureState: () => structuredClone({ settings, plan })
    });
    localStorage.clear();
  }, { connected });
}

function planProgressFixture(planName = 'default') {
  return {
    available: true, status: 'running', planName, planVersion: 16, schedulerMode: 'time',
    activeStageIndex: 0, stageProgress: .5, elapsedSeconds: 450, stageElapsedSeconds: 450,
    cycle: 0, repeating: true, loopStartIndex: 1, reason: '',
    stages: [
      { index: 0, name: 'Time Machine opener', durationSeconds: 900, completionMode: 0, energyFocus: 1, magicFocus: 1, gearGoal: 5, configuredEnergyPriorities: [], configuredMagicPriorities: [], energyPriorities: ['TM', 'NGU'], magicPriorities: ['TM', 'NGU'] },
      { index: 1, name: 'NGU sustain', durationSeconds: 900, completionMode: 0, energyFocus: 5, magicFocus: 6, gearGoal: 2, configuredEnergyPriorities: [], configuredMagicPriorities: [], energyPriorities: ['NGU', 'TM'], magicPriorities: ['NGU', 'RIT', 'TM'] }
    ]
  };
}

async function openPlanDetails(page: Page, name: string) {
  const details = page.locator('#plan-editor details').filter({ has: page.locator('summary').filter({ hasText: new RegExp(`^${name}$`) }) });
  if (!await details.evaluate(element => element.hasAttribute('open'))) await details.locator(':scope > summary').click();
  return details;
}

async function dragStage(page: Page, from: number, to: number): Promise<void> {
  const segments = page.locator('.bar-segment');
  const source = segments.nth(from).locator('.stage-select');
  const target = segments.nth(to).locator('.stage-select');
  await source.scrollIntoViewIfNeeded();
  await target.scrollIntoViewIfNeeded();
  const start = (await source.boundingBox())!;
  const end = (await target.boundingBox())!;
  const x = end.x + (to < from ? 12 : end.width - 12);
  const y = end.y + end.height / 2;
  await page.mouse.move(start.x + start.width / 2, start.y + start.height / 2);
  await page.mouse.down();
  await page.mouse.move(start.x + start.width / 2 + (to < from ? -12 : 12), start.y + start.height / 2, { steps: 3 });
  await page.mouse.move(x, y, { steps: 12 });
  await expect(page.locator('.bar-segment.dragging')).toHaveCount(1);
  await page.mouse.up();
  await expect(page.locator('.dragging, .insert-before, .insert-after')).toHaveCount(0);
}

test('NGU weight controls label all targets, emit correct arrays, autosave, and fit mobile', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  await expect(page.locator('.plan-disclosure')).not.toHaveAttribute('open', '');
  await page.getByRole('button', { name: 'Edit NGU weights', exact: true }).click();
  await expect(page.locator('.plan-disclosure')).toHaveAttribute('open', '');
  await expect(page.getByRole('button', { name: 'Dashboard', exact: true })).toHaveAttribute('aria-current', 'page');
  const card = page.getByRole('article', { name: 'NGU weights', exact: true });
  const energy = ['Augments', 'Wandoos', 'Respawn', 'Gold', 'Adventure Alpha', 'Power Alpha', 'Drop Chance', 'Magic NGU', 'PP'];
  const magic = ['Yggdrasil', 'EXP', 'Power Beta', 'Number', 'Time Machine', 'Energy NGU', 'Adventure Beta'];
  await expect(card.getByRole('spinbutton')).toHaveCount(16);
  for (const [resource, names, selected] of [['Energy', energy, [4, 6]], ['Magic', magic, [0]]] as const) {
    for (const [index, name] of names.entries()) {
      const input = card.getByRole('spinbutton', { name: `${resource} ${name} weight`, exact: true });
      await expect(input).toHaveValue(selected.some(value => value === index) ? '1' : '0');
      await expect(input).toHaveAttribute('min', '0');
      await expect(input).toHaveAttribute('max', '2147483647');
      await expect(input).toHaveAttribute('step', '1');
    }
  }
  await expect(card).toContainText('Positive weights are ratios, not percentages');
  await expect(card).toContainText('All-zero weights skip generic NGU');
  const before = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
  const adventure = card.getByRole('spinbutton', { name: 'Energy Adventure Alpha weight', exact: true });
  await adventure.fill('2');
  await page.clock.runFor(200);
  await card.getByRole('spinbutton', { name: 'Magic EXP weight', exact: true }).fill('3');
  await page.clock.runFor(349);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  await page.clock.runFor(1);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites.length)).toBe(1);
  const saved = await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites[0]);
  expect(saved).toMatchObject({ Version: 16, EnergyNguWeights: [0, 0, 0, 0, 2, 0, 1, 0, 0], MagicNguWeights: [1, 3, 0, 0, 0, 0, 0], StageDurationSeconds: 1800, StageLoopStartIndex: before.StageLoopStartIndex });
  expect(saved.Stages).toEqual(before.Stages);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('NGU weight invalids stay local and corrected all-zero weights save without normalization', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  await openPlanDetails(page, 'NGU weights');
  const input = page.getByRole('spinbutton', { name: 'Magic Yggdrasil weight', exact: true });
  for (const invalid of ['', '-1', '0.5', '2147483648', '1e-400', '1.0000000000000001']) {
    await input.fill(invalid);
    await page.clock.runFor(350);
    await expect(input).toHaveValue(invalid);
    await expect(page.getByRole('alert').first()).toContainText('MagicNguWeights[0]');
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.MagicNguWeights)).toEqual([1, 0, 0, 0, 0, 0, 0]);
  }
  await input.fill('0');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.MagicNguWeights)).toEqual([0, 0, 0, 0, 0, 0, 0]);
});

test('NGU weight inputs accept exact whole decimal and exponent forms', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  await openPlanDetails(page, 'NGU weights');
  const input = page.getByRole('spinbutton', { name: 'Magic Yggdrasil weight', exact: true });
  for (const [token, weight] of [['2.0', 2], ['3e0', 3], ['0e0', 0]] as const) {
    await input.fill(token);
    await page.clock.runFor(350);
    await expect.poll(() => page.evaluate(() => ((window as FixtureWindow).__fixtureState().plan.MagicNguWeights as number[])[0])).toBe(weight);
  }
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites.length)).toBe(3);
});

test('NGU dashboard link opens its disclosure and scrolls without changing tabs or loading the active plan', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await openPlanDetails(page, 'Plan library');
  await page.getByRole('combobox', { name: 'Plan to edit', exact: true }).selectOption('24hr');
  await expect(page.locator('.plan-main').getByRole('heading', { name: '24hr', exact: true })).toBeVisible();
  const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
  await expect(page.locator('.plan-disclosure')).not.toHaveAttribute('open', '');
  await page.getByRole('button', { name: 'Edit NGU weights', exact: true }).click();
  await expect(page.locator('.plan-disclosure')).toHaveAttribute('open', '');
  await expect(page.getByRole('button', { name: 'Dashboard', exact: true })).toHaveAttribute('aria-current', 'page');
  const card = page.getByRole('article', { name: 'NGU weights', exact: true });
  await expect(card).toHaveAttribute('id', 'ngu-weights');
  await expect(card).toBeInViewport();
  await expect(page.getByRole('combobox', { name: 'Plan to edit', exact: true })).toHaveValue('24hr');
  await expect(card).toContainText('Editing plan');
  await expect(card).toContainText('24hr');
  await expect(card).toContainText('Active plan: default. Edits apply to 24hr');
  await page.waitForTimeout(700);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length)).toBe(reads);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  await card.getByRole('spinbutton', { name: 'Energy Adventure Alpha weight', exact: true }).fill('4');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixturePlanNames)).toEqual(['24hr']);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
});

test('NGU weights are disabled for unsupported runtime capability without affecting saved values', async ({ page }) => {
  await installNativeFixture(page);
  await page.addInitScript(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { planVersion: 15 }; });
  await page.goto('/');
  await openPlanDetails(page, 'NGU weights');
  const input = page.getByRole('spinbutton', { name: 'Energy Adventure Alpha weight', exact: true });
  await expect(input).toBeDisabled();
  await expect(input).toHaveValue('1');
  await expect(page.getByRole('article', { name: 'NGU weights', exact: true })).toContainText('planVersion 16 or newer');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
});

test('dashboard loads without JS errors and fits the viewport', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await installNativeFixture(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'NGU Injector', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Actions', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Fight bosses', exact: true })).toBeEnabled();
  await expect(page.getByRole('heading', { name: 'Plan & Rebirth', exact: true })).toBeVisible();
  await expect(page.locator('.dashboard-grid > article').first().getByRole('heading', { name: 'Quick', exact: true })).toBeVisible();
  const quick = await page.getByRole('heading', { name: 'Quick', exact: true }).boundingBox();
  expect(quick!.y).toBeLessThan(page.viewportSize()!.height);
  const theme = await page.evaluate(() => ({ scheme: getComputedStyle(document.documentElement).colorScheme, background: getComputedStyle(document.documentElement).backgroundColor }));
  expect(theme.scheme).toBe('dark');
  expect(theme.background).toBe('rgb(16, 17, 19)');
  await expect(page.getByText('Unity-thread bridge', { exact: false })).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('dashboard.png'), fullPage: true });
  expect(errors).toEqual([]);
});

test('Dashboard ends with one full-width bar editor and collapsed secondary controls', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  const editor = page.getByRole('region', { name: 'Plan editor', exact: true });
  await expect(editor).toHaveAttribute('id', 'plan-editor');
  await expect(page.locator('.plan-main')).toHaveCount(1);
  await expect(page.locator('.plan-sidebar')).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Plan progression', exact: true })).toHaveCount(0);
  await expect(editor.locator('.stage-row, .stage-list, table')).toHaveCount(0);
  await expect(editor.locator(':scope > :first-child')).toHaveClass(/\bplan-main\b/);
  const management = editor.locator('details.card.plan-management');
  await expect(editor.locator(':scope > :last-child')).toHaveClass('card plan-management');
  for (const summary of ['Schedule & templates', 'Gear settings', 'Rebirth settings', 'Rebirth history', 'NGU weights', 'Advanced plan JSON', 'Plan library']) {
    const disclosure = editor.locator('details').filter({ has: page.locator('summary').filter({ hasText: new RegExp(`^${summary}$`) }) });
    await expect(disclosure).not.toHaveAttribute('open', '');
    await expect(disclosure.locator(':scope > summary')).toBeVisible();
  }
  await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toBeVisible();
  await expect(management.getByRole('combobox', { name: 'Plan to edit', exact: true })).toHaveCount(0);
  const bar = editor.getByRole('list', { name: 'Plan stages', exact: true });
  await expect(bar).toHaveCount(1);
  await expect(bar.locator('.stage-select')).toHaveCount(2);
  for (const button of await bar.locator('.stage-select').all()) await expect(button).toHaveAttribute('aria-expanded', 'false');
  await expect(editor.locator('.stage-panel')).toHaveCount(2);
  await expect(editor.locator('.stage-panel:visible')).toHaveCount(0);
  await expect(bar.locator('strong')).toHaveText(['Time Machine', 'Energy NGU']);
  await expect(bar).not.toContainText('Time Machine opener');
  await expect(bar).not.toContainText('NGU sustain');
  const editorBox = await editor.boundingBox();
  const dashboardBox = await page.locator('.dashboard-grid').boundingBox();
  const mainBox = await editor.locator('.plan-main').boundingBox();
  const managementBox = await management.boundingBox();
  expect(editorBox!.y).toBeGreaterThanOrEqual(dashboardBox!.y + dashboardBox!.height);
  expect(editorBox!.width).toBeGreaterThan(dashboardBox!.width * .94);
  expect(mainBox!.width).toBeGreaterThan(editorBox!.width * .94);
  expect(managementBox!.width).toBeGreaterThan(editorBox!.width * .94);

  await page.getByRole('button', { name: 'Edit plan', exact: true }).click();
  await page.clock.runFor(500);
  await expect(editor).toBeInViewport();
  await bar.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
  const stage = editor.getByRole('region', { name: 'Stage 1 settings', exact: true });
  await expect(stage).toBeVisible();
  const stageBox = await stage.boundingBox();
  const barBox = await bar.boundingBox();
  expect(stageBox!.y).toBeGreaterThanOrEqual(barBox!.y + barBox!.height);
  await expect(stage.getByRole('heading', { name: 'Time Machine', exact: true })).toBeVisible();
  await expect(stage.getByRole('combobox', { name: 'Energy focus', exact: true })).toHaveValue('1');
  await expect(stage.getByRole('combobox', { name: 'Magic focus', exact: true })).toHaveValue('1');
  await expect(stage.getByRole('combobox', { name: 'Finish stage by', exact: true })).toHaveValue('0');
  await expect(editor.locator('.stage-panel select[aria-label="Gear goal"], .stage-panel input[aria-label="Black-bar lookahead (s)"]')).toHaveCount(0);
  await expect(editor.getByText('Cap buffer (seconds)', { exact: false })).toHaveCount(0);
  await expect(editor.getByRole('button', { name: /^Move (earlier|later)$/, includeHidden: true })).toHaveCount(0);
  await expect(bar.locator('.handle, [data-handle]')).toHaveCount(0);
  await expect(stage.getByRole('checkbox', { name: 'Fight on entry', exact: true })).not.toBeChecked();
  await stage.getByRole('combobox', { name: 'Focus preset', exact: true }).selectOption({ label: 'Wandoos' });
  await editor.getByRole('combobox', { name: 'Stage timing', exact: true }).selectOption('time');
  const duration = stage.getByRole('textbox', { name: 'Duration', exact: true });
  await duration.fill('25m');
  await duration.press('Enter');
  await stage.getByRole('textbox', { name: 'Energy priorities', exact: true }).fill('WAN, NGU, TM');
  await stage.getByRole('textbox', { name: 'Magic priorities', exact: true }).fill('WAN, RIT, NGU, TM');
  await expect(stage.getByRole('combobox', { name: 'Focus preset', exact: true }).locator('option:checked')).toHaveText('Wandoos');
  await stage.getByRole('combobox', { name: 'Energy focus', exact: true }).selectOption('1');
  await expect(stage.getByRole('combobox', { name: 'Focus preset', exact: true }).locator('option:checked')).toHaveText('Custom');
  const gear = await openPlanDetails(page, 'Gear settings');
  await gear.getByRole('combobox', { name: 'Gear goal', exact: true }).selectOption('4');
  await openPlanDetails(page, 'Rebirth settings');
  await editor.getByRole('spinbutton', { name: 'Rebirth after (s)', exact: true }).fill('7200');
  await openPlanDetails(page, 'NGU weights');
  const weights = editor.locator('#ngu-weights');
  await expect(weights).toHaveCount(1);
  await weights.getByRole('spinbutton', { name: 'Magic EXP weight', exact: true }).fill('3');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites.length)).toBe(1);
  const saved = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
  expect(saved).toMatchObject({
    TimeTriggerSeconds: 7200, GearGoal: 4, GoalPresets: [{ Name: 'Default', GearGoal: 4 }],
    EnergyNguWeights: [0, 0, 0, 0, 1, 0, 1, 0, 0], MagicNguWeights: [1, 3, 0, 0, 0, 0, 0],
    Stages: [
      { Name: 'Wandoos', DurationSeconds: 1500, EnergyFocus: 1, MagicFocus: 3, GearGoal: 12, EnergyPriorities: ['WAN', 'NGU', 'TM'], MagicPriorities: ['WAN', 'RIT', 'NGU', 'TM'] },
      { Name: 'NGU sustain', DurationSeconds: 900 }
    ]
  });
  const rawDetails = page.locator('details').filter({ has: page.getByText('Advanced plan JSON', { exact: true }) });
  await expect(rawDetails).not.toHaveAttribute('open', '');
  await expect(page.getByRole('textbox', { name: 'Advanced Plan JSON', exact: true })).toHaveCount(0);
  const rawBox = await rawDetails.boundingBox();
  const weightsBox = await weights.boundingBox();
  expect(rawBox!.y).toBeGreaterThanOrEqual(weightsBox!.y + weightsBox!.height);
  await rawDetails.locator('summary').first().click();
  const raw = page.getByRole('textbox', { name: 'Advanced Plan JSON', exact: true });
  await expect(raw).toBeVisible();
  await expect.poll(async () => JSON.parse(await raw.inputValue())).toMatchObject({ TimeTriggerSeconds: 7200, GearGoal: 4, Stages: saved.Stages });
  await openPlanDetails(page, 'Rebirth history');
  await expect(editor.getByText('No rebirths yet.', { exact: true })).toBeVisible();
  await openPlanDetails(page, 'Plan library');
  await expect(management.getByRole('heading', { level: 2, name: 'Plans', exact: true })).toBeVisible();
  await expect(management.getByRole('combobox', { name: 'Plan to edit', exact: true })).toHaveValue('default');
  await expect(page.getByRole('button', { name: 'Dashboard', exact: true })).toHaveAttribute('aria-current', 'page');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('local duration and Plan JSON drafts survive every tab without saving or reloading', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
  const duration = page.getByRole('textbox', { name: 'Duration', exact: true }).first();
  // An incomplete duration cannot commit on blur; it must remain a component-local draft.
  await duration.fill('25m pending');
  await page.getByText('Advanced plan JSON', { exact: true }).click();
  const raw = page.getByRole('textbox', { name: 'Advanced Plan JSON', exact: true });
  const original = await raw.inputValue();
  const draft = JSON.stringify({ ...JSON.parse(original), TimeTriggerSeconds: 8100 }, null, 2);
  await raw.fill(draft);
  const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
  const navigation = page.getByRole('navigation', { name: 'Primary', exact: true });
  for (const tab of ['Adventure', 'Misc', 'Logs', 'Advanced']) {
    await navigation.getByRole('button', { name: tab, exact: true }).click();
    await expect(page.locator('#plan-editor')).toHaveCount(1);
    await expect(page.locator('#plan-editor')).toBeHidden();
    await expect(page.locator('textarea[aria-label="Advanced Plan JSON"]')).toHaveValue(draft);
    await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
    await expect(duration).toHaveValue('25m pending');
    await expect(raw).toBeVisible();
    await expect(raw).toHaveValue(draft);
  }
  await page.waitForTimeout(700);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length)).toBe(reads);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(3600);
  await page.locator('.json-editor').filter({ has: raw }).getByRole('button', { name: 'Discard edits', exact: true }).click();
  await expect(raw).toHaveValue(original);
  await expect(duration).toHaveValue('25m pending');
  await duration.press('Escape');
  await expect(duration).toHaveValue('50%');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
});

for (const field of ['Duration', 'Schedule length']) {
  test(`uncommitted ${field} drafts protect plan switches and closing without blocking other autosaves`, async ({ page }) => {
    await installNativeFixture(page);
    await page.goto('/');
    if (field === 'Duration') await page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
    else await openPlanDetails(page, 'Schedule & templates');
    const input = page.getByRole('textbox', { name: field, exact: true }).first();
    await input.fill('25m pending');
    await expect(page.getByRole('button', { name: 'Enable plan', exact: true })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Auto-rebirth: Off', exact: true })).toBeDisabled();
    await page.evaluate(() => { (window as FixtureWindow).__fixtureConfirm = false; });
    await openPlanDetails(page, 'Plan library');
    const selected = page.getByRole('combobox', { name: 'Plan to edit', exact: true });
    const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
    await selected.selectOption('24hr');
    await expect(selected).toHaveValue('default');
    await expect(input).toHaveValue('25m pending');
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length)).toBe(reads);
    const closeBlocked = () => page.evaluate(() => {
      const event = new Event('beforeunload', { cancelable: true });
      window.dispatchEvent(event);
      return event.defaultPrevented;
    });
    expect(await closeBlocked()).toBe(true);
    await openPlanDetails(page, 'Rebirth settings');
    await page.getByRole('spinbutton', { name: 'Rebirth after (s)', exact: true }).fill('7200');
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(7200);
    await expect(input).toHaveValue('25m pending');
    await input.press('Escape');
    await expect.poll(closeBlocked).toBe(false);
    await expect(page.getByRole('button', { name: 'Enable plan', exact: true })).toBeEnabled();
    await selected.selectOption('24hr');
    await expect(selected).toHaveValue('24hr');
    if (field === 'Duration') await page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
    await expect(input).not.toHaveValue('25m pending');
  });
}

test('dashboard plan management selects shortcuts and creates, copies and deletes without activating a plan', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  const management = await openPlanDetails(page, 'Plan library');
  await expect(management.getByText('Changes save automatically.', { exact: false })).toBeVisible();
  const selected = management.getByRole('combobox', { name: 'Plan to edit', exact: true });
  const shortcuts = management.locator('[aria-label="Built-in plan shortcuts"]');
  await expect(shortcuts.getByRole('button')).toHaveText(['Default', '24h', '1h / 3m', 'Infinite']);
  await expect(shortcuts.getByRole('button', { name: '1h / 3m', exact: true })).toBeDisabled();
  await expect(shortcuts.getByRole('button', { name: 'Infinite', exact: true })).toBeDisabled();
  await shortcuts.getByRole('button', { name: '24h', exact: true }).click();
  await expect(selected).toHaveValue('24hr');
  await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('24hr');
  await management.getByText('Delete plan', { exact: true }).click();
  const remove = management.getByRole('button', { name: 'Delete', exact: true });
  await expect(remove).toBeDisabled();
  await expect(management).toContainText('Built-in plans cannot be deleted');
  await shortcuts.getByRole('button', { name: 'Default', exact: true }).click();
  await expect(selected).toHaveValue('default');
  const source = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
  const name = management.getByRole('textbox', { name: 'New plan name', exact: true });
  await name.fill('browser-created');
  await management.getByRole('button', { name: 'Create', exact: true }).click();
  await expect(selected).toHaveValue('browser-created');
  await expect(selected.locator('option')).toHaveText(['Select plan', 'default', '24hr', 'browser-created']);
  await expect(page.locator('.plan-main').getByRole('heading', { name: 'browser-created', exact: true })).toBeVisible();
  await name.fill('browser-copy');
  await management.getByRole('button', { name: 'Copy selected', exact: true }).click();
  await expect(selected).toHaveValue('browser-copy');
  const { __externalUiRevision: revision, ...copySource } = source;
  expect(revision).toMatch(/^[0-9a-f]{64}$/);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCreatedPlans)).toEqual([
    { name: 'browser-created', source: null }, { name: 'browser-copy', source: { ...copySource, RebirthHistory: [] } }
  ]);
  await expect(remove).toBeEnabled();
  await remove.click();
  await expect(selected).toHaveValue('default');
  await expect(selected.locator('option')).toHaveText(['Select plan', 'default', '24hr', 'browser-created']);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDeletedPlans)).toEqual(['browser-copy']);
  await expect(page.getByRole('button', { name: 'Dashboard', exact: true })).toHaveAttribute('aria-current', 'page');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
});

test('Adventure checkbox and select changes autosave and survive refresh and reload', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  const boost = page.getByRole('checkbox', { name: 'Auto boost', exact: true });
  await expect(boost).toBeChecked();
  await boost.uncheck();
  await page.clock.runFor(1);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customAutoBoostEnabled)).toBe(false);
  await page.getByRole('combobox', { name: 'Manual adventure zone', exact: true }).selectOption('1');
  await page.clock.runFor(1);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._manualAdventureZone)).toBe(1);
  await expect(page.locator('.status-line')).toContainText(/saved/i);
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(boost).not.toBeChecked();
  await expect(page.getByRole('button', { name: /^Save (settings|plan)/ })).toHaveCount(0);
  await page.reload();
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(boost).not.toBeChecked();
  await expect(page.getByRole('combobox', { name: 'Manual adventure zone', exact: true })).toHaveValue('1');
});

test('plan stages reorder, autosave and persist without breaking desktop or mobile layout', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Stages', exact: true })).toBeVisible();
  const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
  await expect(bar.locator('strong')).toHaveText(['Time Machine', 'Energy NGU']);
  const keys = await bar.locator('.bar-segment').evaluateAll(segments => segments.map(segment => segment.getAttribute('data-stage')));
  await bar.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
  await page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).press('Alt+ArrowRight');
  await expect(bar.locator('strong')).toHaveText(['Energy NGU', 'Time Machine']);
  expect(await bar.locator('.bar-segment').evaluateAll(segments => segments.map(segment => segment.getAttribute('data-stage')))).toEqual([...keys].reverse());
  await expect(page.getByRole('region', { name: 'Stage 2 settings', exact: true }).getByRole('combobox', { name: 'Focus preset', exact: true }).locator('option:checked')).toHaveText('Time Machine');
  await expect(bar.getByRole('button', { name: 'Edit stage 2: Time Machine', exact: true })).toHaveAttribute('aria-expanded', 'true');
  await expect(page.getByRole('button', { name: 'Edit stage 2: Time Machine', exact: true })).toBeFocused();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.Stages)).toMatchObject([{ Name: 'NGU sustain' }, { Name: 'Time Machine opener' }]);
  await expect(page.locator('.plan-main').getByRole('heading', { name: 'default', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save plan', exact: true })).toHaveCount(0);
  await page.reload();
  await expect(bar.locator('strong')).toHaveText(['Energy NGU', 'Time Machine']);
  await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
  await openPlanDetails(page, 'Schedule & templates');
  await expect(page.getByRole('combobox', { name: 'Repeat from', exact: true })).toHaveValue('0');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('dashboard-plan-editor.png'), fullPage: true });
});

test('primary pages keep the plan editor on Dashboard and tools on their own tabs', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  const navigation = page.getByRole('navigation', { name: 'Primary', exact: true });
  await expect(navigation.getByRole('button')).toHaveText(['Dashboard', 'Adventure', 'Misc', 'Logs', 'Advanced']);
  const planEditor = page.getByRole('region', { name: 'Plan editor', exact: true });
  const weights = page.getByRole('article', { name: 'NGU weights', exact: true });

  await navigation.getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(page.locator('#plan-editor')).toHaveCount(1);
  await expect(planEditor).toBeHidden();
  await expect(page.locator('#ngu-weights')).toHaveCount(1);
  await expect(weights).toBeHidden();
  await expect(page.getByRole('combobox', { name: 'Adventure mode', exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Manual adventure zone', exact: true })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Adventure goal', exact: true })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Auto boost', exact: true })).toBeVisible();
  await expect(page.getByRole('spinbutton', { name: 'Cube Power:Toughness ratio', exact: true })).toBeVisible();
  await expect(page.getByRole('spinbutton', { name: 'Saving time (s)', exact: true })).toHaveCount(0);
  await expect(page.getByRole('checkbox', { name: 'Energy', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Preview gear', exact: true })).toHaveCount(0);

  await navigation.getByRole('button', { name: 'Misc', exact: true }).click();
  await expect(weights).toBeHidden();
  await expect(page.getByRole('spinbutton', { name: /^(Energy|Magic) .+ weight$/ })).toHaveCount(0);
  await expect(page.getByRole('checkbox', { name: 'Energy', exact: true })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Auto money pit', exact: true })).toBeVisible();
  await expect(page.getByRole('spinbutton', { name: 'Saving time (s)', exact: true })).toBeVisible();
  await expect(page.getByRole('spinbutton', { name: 'Minimum worth', exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Goal', exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Fight scoring', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Preview gear', exact: true })).toBeEnabled();
  await expect(page.getByRole('heading', { name: 'Utilities', exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Adventure mode', exact: true })).toHaveCount(0);
  await expect(page.getByRole('checkbox', { name: 'Auto boost', exact: true })).toHaveCount(0);
  await expect(page.getByRole('spinbutton', { name: 'Cube Power:Toughness ratio', exact: true })).toHaveCount(0);
  await expect(page.locator('.sticky-card')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Discard changes', exact: true })).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);

  await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(weights).toBeHidden();
  await expect(page.locator('.plan-disclosure')).not.toHaveAttribute('open', '');
  await openPlanDetails(page, 'NGU weights');
  await expect(weights).toBeVisible();
  await expect(weights.getByRole('heading', { name: 'NGU weights', exact: true })).toBeVisible();
  await expect(weights.getByRole('spinbutton')).toHaveCount(16);
  await expect(planEditor).toBeVisible();
  await openPlanDetails(page, 'Plan library');
  await expect(page.getByRole('combobox', { name: 'Plan to edit', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Stages', exact: true })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Auto boost', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Preview gear', exact: true })).toHaveCount(0);

  await navigation.getByRole('button', { name: 'Logs', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Log file', exact: true })).toBeVisible();
  await expect(page.locator('.log-output')).toContainText('Fixture safe log');
  await navigation.getByRole('button', { name: 'Advanced', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Advanced Settings JSON', exact: true })).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Advanced Plan JSON', exact: true })).toHaveCount(0);
  await expect(page.getByText('Game data', { exact: true })).toBeVisible();
  await expect(planEditor).toBeHidden();
  await expect(page.getByRole('heading', { name: 'Save changes', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /^Save (settings|plan)/ })).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('numeric settings restart the 350ms debounce and persist only the final value', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  const ratio = page.getByRole('spinbutton', { name: 'Cube Power:Toughness ratio', exact: true });
  await ratio.fill('3');
  await page.clock.runFor(200);
  await ratio.fill('4');
  await page.clock.runFor(349);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customAutoBoostCubePowerRatio)).toBe(2);
  await expect(ratio).toHaveValue('4');
  await page.clock.runFor(1);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customAutoBoostCubePowerRatio)).toBe(4);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([{ _customAutoBoostCubePowerRatio: 4 }]);
  await expect(page.locator('.status-line')).toContainText(/saved/i);
  await page.reload();
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(ratio).toHaveValue('4');
});

test('plan preset and numeric edits stay pending until the debounced save completes', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  const button = page.getByRole('list', { name: 'Plan stages', exact: true }).locator('.stage-select').first();
  await expect(button).toHaveAttribute('aria-expanded', 'false');
  await button.click();
  await expect(button).toHaveAttribute('aria-expanded', 'true');
  const preset = page.getByRole('combobox', { name: 'Focus preset', exact: true }).first();
  await expect(preset.locator('option:checked')).toHaveText('Time Machine');
  await preset.selectOption({ label: 'Wandoos' });
  await expect(preset.locator('option:checked')).toHaveText('Wandoos');
  await page.clock.runFor(200);
  await openPlanDetails(page, 'Rebirth settings');
  await page.getByRole('spinbutton', { name: 'Rebirth after (s)', exact: true }).fill('7200');
  await page.clock.runFor(349);
  await expect(page.locator('.plan-main').getByRole('heading', { name: 'default *', exact: true })).toBeVisible();
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan)).toMatchObject({ TimeTriggerSeconds: 3600, Stages: [{ Name: 'Time Machine opener', EnergyFocus: 1, MagicFocus: 1, GearGoal: 5 }, { Name: 'NGU sustain' }] });
  await page.clock.runFor(1);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan)).toMatchObject({ TimeTriggerSeconds: 7200, Stages: [{ Name: 'Wandoos', EnergyFocus: 3, MagicFocus: 3, GearGoal: 12, EnergyPriorities: ['WAN', 'NGU', 'TM', 'AUG', 'AT'], MagicPriorities: ['WAN', 'RIT', 'NGU', 'TM'] }, { Name: 'NGU sustain' }] });
  await expect(preset.locator('option:checked')).toHaveText('Wandoos');
  await expect(page.locator('.plan-main').getByRole('heading', { name: 'default', exact: true })).toBeVisible();
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites.length)).toBe(1);
  await expect(button).toHaveAttribute('aria-label', 'Edit stage 1: Wandoos');
  await page.getByRole('button', { name: 'Close stage settings', exact: true }).click();
  await expect(button).toBeFocused();
  await expect(button).toHaveAttribute('aria-expanded', 'false');
  await expect(page.locator('.stage-panel')).toHaveCount(2);
  await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
  await button.click();
  await expect(preset.locator('option:checked')).toHaveText('Wandoos');
  await button.click();
  await expect(button).toHaveAttribute('aria-expanded', 'false');
  await page.reload();
  await expect(button).toHaveAttribute('aria-expanded', 'false');
  await button.click();
  await expect(preset.locator('option:checked')).toHaveText('Wandoos');
  await openPlanDetails(page, 'Rebirth settings');
  await expect(page.getByRole('spinbutton', { name: 'Rebirth after (s)', exact: true })).toHaveValue('7200');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('failed settings autosave preserves edits for the header Retry saving button', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await page.evaluate(() => { (window as FixtureWindow).__fixtureFailNextSetting = true; });
  const boost = page.getByRole('checkbox', { name: 'Auto boost', exact: true });
  await boost.uncheck();
  await expect(page.getByRole('alert')).toContainText('Setting save failed.');
  await expect(boost).not.toBeChecked();
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customAutoBoostEnabled)).toBe(true);
  await page.getByRole('button', { name: 'Misc', exact: true }).click();
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(boost).not.toBeChecked();
  const retry = page.locator('.app-header').getByRole('button', { name: 'Retry saving', exact: true });
  await expect(retry).toBeEnabled();
  await retry.click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customAutoBoostEnabled)).toBe(false);
  await expect(retry).toHaveCount(0);
  await expect(page.getByRole('alert')).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([{ _customAutoBoostEnabled: false }, { _customAutoBoostEnabled: false }]);
  await page.reload();
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(boost).not.toBeChecked();
});

test('failed plan autosave keeps the pending edit until header retry persists it', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await openPlanDetails(page, 'Rebirth settings');
  await page.evaluate(() => { (window as FixtureWindow).__fixtureFailNextPlan = true; });
  const duration = page.getByRole('spinbutton', { name: 'Rebirth after (s)', exact: true });
  await duration.fill('7200');
  await expect(page.getByRole('alert')).toContainText('Plan save failed.');
  await expect(duration).toHaveValue('7200');
  await expect(page.locator('.plan-main').getByRole('heading', { name: 'default *', exact: true })).toBeVisible();
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(3600);
  await page.getByRole('button', { name: 'Misc', exact: true }).click();
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(duration).toHaveValue('7200');
  const retry = page.locator('.app-header').getByRole('button', { name: 'Retry saving', exact: true });
  await expect(retry).toBeEnabled();
  await retry.click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(7200);
  await expect(page.locator('.plan-main').getByRole('heading', { name: 'default', exact: true })).toBeVisible();
  await expect(retry).toHaveCount(0);
  await expect(page.getByRole('alert')).toHaveCount(0);
  const writes = await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites);
  expect(writes).toHaveLength(2);
  expect(writes[0]).toMatchObject({ TimeTriggerSeconds: 7200 });
  expect(writes[1]).toEqual(writes[0]);
  await page.reload();
  await openPlanDetails(page, 'Rebirth settings');
  await expect(duration).toHaveValue('7200');
});

for (const target of ['Settings', 'Plan'] as const) {
  test(`Advanced ${target} Apply edits autosaves without a separate Save action`, async ({ page }) => {
    await installNativeFixture(page);
    await page.goto('/');
    await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('default');
    if (target === 'Settings') await page.getByRole('button', { name: 'Advanced', exact: true }).click();
    else await page.getByText('Advanced plan JSON', { exact: true }).click();
    const editor = page.getByRole('textbox', { name: `Advanced ${target} JSON`, exact: true });
    const draft = JSON.parse(await editor.inputValue());
    const change = target === 'Settings' ? { _manageEnergy: false } : { TimeTriggerSeconds: 7200 };
    await editor.fill(JSON.stringify({ ...draft, ...change }));
    // Raw buffers remain drafts until Apply, unlike normal settings inputs.
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    await page.locator('.json-editor').filter({ has: editor }).getByRole('button', { name: 'Apply edits', exact: true }).click();
    await expect.poll(() => page.evaluate(target => {
      const saved = (window as FixtureWindow).__fixtureState();
      return target === 'Settings' ? saved.settings : saved.plan;
    }, target)).toMatchObject(change);
    await expect(page.locator('.status-line')).toContainText(/saved/i);
    await expect(page.getByRole('button', { name: /^Save (settings|plan)/ })).toHaveCount(0);
    await page.reload();
    if (target === 'Settings') await page.getByRole('button', { name: 'Advanced', exact: true }).click();
    else await page.getByText('Advanced plan JSON', { exact: true }).click();
    await expect.poll(async () => JSON.parse(await editor.inputValue())).toMatchObject(change);
  });
}

test('disconnected dashboard cannot enqueue live actions', async ({ page }) => {
  await installNativeFixture(page, false);
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Rebirth', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Enable plan', exact: true })).toBeDisabled();
  expect(await page.evaluate(() => (window as unknown as { __fixtureCalls: string[] }).__fixtureCalls)).not.toContain('enqueue_command');
});

test('blood estimates and automatic Iron Pill history display actual runtime data', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await expect(page.getByText('about 1m 30s', { exact: false }).filter({ visible: true })).toBeVisible();
  await expect(page.getByText('Blocked: Custom auto-rebirth is disabled.', { exact: true }).filter({ visible: true })).toBeVisible();
  await page.locator('article').filter({ has: page.getByRole('heading', { name: 'Iron Pill', exact: true }) }).getByText('Recent casts', { exact: true }).first().click();
  await expect(page.getByText('2026-10-14 12:30:00 UTC', { exact: true }).filter({ visible: true })).toBeVisible();
  await expect(page.getByText('0h 15m 0s', { exact: false }).filter({ visible: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('Iron Pill edits persist while history remains backend-owned', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await page.getByRole('button', { name: 'Misc', exact: true }).click();
  await page.getByRole('spinbutton', { name: 'Saving time (s)', exact: true }).fill('1200');
  await page.getByRole('spinbutton', { name: 'Minimum worth', exact: true }).fill('5000');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings)).toMatchObject({ _customIronPillSaveSeconds: 1200, _customIronPillMinWorth: 5000 });
  await expect(page.locator('.status-line')).toContainText(/saved/i);
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(page.getByRole('spinbutton', { name: 'Saving time (s)', exact: true })).toHaveValue('1200');
  await expect(page.getByRole('spinbutton', { name: 'Minimum worth', exact: true })).toHaveValue('5000');
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await page.locator('article').filter({ has: page.getByRole('heading', { name: 'Iron Pill', exact: true }) }).getByText('Recent casts', { exact: true }).first().click();
  await expect(page.getByText('2026-10-14 12:30:00 UTC', { exact: true }).filter({ visible: true })).toBeVisible();
});

test('dashboard enables a plan and auto-rebirth without popups', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('default');
  await page.getByRole('button', { name: 'Enable plan', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Disable plan', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Auto-rebirth: Off', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Auto-rebirth: On', exact: true })).toHaveAttribute('aria-pressed', 'true');
  const calls = await page.evaluate(() => (window as unknown as { __fixtureCalls: string[] }).__fixtureCalls);
  expect(calls).toContain('save_plan');
  expect(calls).toContain('save_settings_patch');
  expect(calls).not.toContain('plugin:dialog|message');
  expect(calls).not.toContain('plugin:dialog|ask');
  await page.getByRole('button', { name: 'Disable plan', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Enable plan', exact: true })).toHaveAttribute('aria-pressed', 'false');
  await expect(page.getByRole('button', { name: 'Auto-rebirth: Off', exact: true })).toHaveAttribute('aria-pressed', 'false');
});

test('editor rebirth numeric inputs autosave and survive refresh without inline rebirth controls', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  const controls = page.getByRole('article', { name: 'Plan controls', exact: true });
  await expect(controls.getByRole('spinbutton')).toHaveCount(0);
  await expect(controls.locator('[aria-label="Rebirth conditions"]')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Edit Rebirth after (s)', exact: true })).toHaveCount(0);
  await controls.getByRole('button', { name: 'Edit plan', exact: true }).click();
  const editor = page.getByRole('region', { name: 'Plan editor', exact: true });
  await expect(editor).toBeInViewport();
  await openPlanDetails(page, 'Rebirth settings');
  const duration = editor.getByRole('spinbutton', { name: 'Rebirth after (s)', exact: true });
  await duration.fill('7200');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(7200);
  await expect(page.locator('.status-line')).toContainText(/saved/i);
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(duration).toHaveValue('7200');
  await expect(page.getByRole('button', { name: 'Save plan', exact: true })).toHaveCount(0);
  const calls = await page.evaluate(() => (window as unknown as { __fixtureCalls: string[] }).__fixtureCalls);
  expect(calls).toContain('save_plan');
  expect(calls).not.toContain('plugin:dialog|message');
});

test('blood goal edits preserve the independently autosaved Adventure settings', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await page.getByRole('checkbox', { name: 'Auto boost', exact: true }).uncheck();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customAutoBoostEnabled)).toBe(false);
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true }).click();
  await page.getByRole('spinbutton', { name: 'Spaghetti goal (%)', exact: true }).fill('60');
  await page.getByRole('spinbutton', { name: 'Spaghetti goal (%)', exact: true }).press('Enter');
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('60%');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings)).toMatchObject({ _customAutoBoostEnabled: false, _customBloodSpaghettiGoalPercent: 60, _customBloodCounterfeitGoalPercent: 400 });
  const writes = await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites);
  expect(writes).toEqual([{ _customAutoBoostEnabled: false }, { _customBloodSpaghettiGoalPercent: 60 }]);
  await page.reload();
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('60%');
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(page.getByRole('checkbox', { name: 'Auto boost', exact: true })).not.toBeChecked();
  await expect(page.getByRole('button', { name: /^Save settings/ })).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('Quick duration can be edited without activating the quick action', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await page.getByRole('button', { name: 'Edit Augments + Blood duration (s)', exact: true }).click();
  await page.getByRole('spinbutton', { name: 'Augments + Blood duration (s)', exact: true }).fill('450');
  await page.getByRole('spinbutton', { name: 'Augments + Blood duration (s)', exact: true }).press('Enter');
  await expect(page.getByRole('button', { name: 'Edit Augments + Blood duration (s)', exact: true })).toHaveText('7m 30s');
  const calls = await page.evaluate(() => (window as unknown as { __fixtureCalls: string[] }).__fixtureCalls);
  expect(calls).toContain('save_settings_patch');
  expect(calls).not.toContain('enqueue_command');
});

test('cancelling a failed inline save removes only that attempted edit', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await page.getByRole('checkbox', { name: 'Auto boost', exact: true }).uncheck();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customAutoBoostEnabled)).toBe(false);
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await page.evaluate(() => { (window as FixtureWindow).__fixtureFailNextSetting = true; });
  await page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true }).click();
  const input = page.getByRole('spinbutton', { name: 'Spaghetti goal (%)', exact: true });
  await input.fill('60');
  await input.press('Enter');
  await expect(page.getByRole('group', { name: 'Spaghetti goal (%)', exact: true }).getByRole('alert')).toHaveText('Not saved. Your edit is kept.');
  await input.press('Escape');
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('30%');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings)).toMatchObject({ _customAutoBoostEnabled: false, _customBloodSpaghettiGoalPercent: 30 });
  await page.reload();
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(page.getByRole('checkbox', { name: 'Auto boost', exact: true })).not.toBeChecked();
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('30%');
});

test('Quick uses applied state instead of highlighting a requested or failed goal', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await page.evaluate(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { activeFocusGoal: 'TimeMachine', appliedFocusGoal: '', focusStatus: 'failed' }; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  const quick = page.getByRole('button', { name: 'Time Machine', exact: true });
  await expect(quick).toHaveAttribute('aria-pressed', 'false');
  await page.evaluate(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { activeFocusGoal: 'TimeMachine', appliedFocusGoal: '', focusStatus: 'pending' }; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(quick).toHaveAttribute('aria-pressed', 'false');
  await expect(quick).toHaveAttribute('aria-busy', 'true');
  await expect(quick).toContainText('Applying...');
  await page.evaluate(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { activeFocusGoal: 'TimeMachine', appliedFocusGoal: 'TimeMachine', focusStatus: 'applied' }; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(quick).toHaveAttribute('aria-pressed', 'true');
  await expect(quick).toHaveAttribute('aria-busy', 'false');
});

test('bar keyboard navigation selects one mounted panel without reading or saving a plan', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
  const first = bar.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true });
  const second = bar.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true });
  await expect(bar.locator('.bar-segment')).toHaveCount(2);
  const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
  for (const [from, key, to, stage] of [
    [first, 'ArrowRight', second, 2], [second, 'ArrowLeft', first, 1],
    [first, 'End', second, 2], [second, 'Home', first, 1]
  ] as const) {
    await from.press(key);
    await expect(to).toBeFocused();
    await expect(to).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByRole('region', { name: `Stage ${stage} settings`, exact: true })).toBeVisible();
    await expect(page.locator('.stage-panel:visible')).toHaveCount(1);
    await expect(page.locator('.stage-panel')).toHaveCount(2);
  }
  await first.press('ArrowLeft');
  await expect(first).toBeFocused();
  await page.getByRole('button', { name: 'Close stage settings', exact: true }).click();
  await expect(first).toBeFocused();
  await expect(first).toHaveAttribute('aria-expanded', 'false');
  await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length)).toBe(reads);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
});

test('Add stage opens its panel with AT fields, direct dragging, repeat and removal', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  await page.getByRole('button', { name: 'Add stage', exact: true }).click();
  const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
  await expect(bar.locator('.bar-segment')).toHaveCount(3);
  await expect(bar.getByRole('button', { name: 'Edit stage 3: Time Machine', exact: true })).toHaveAttribute('aria-expanded', 'true');
  const key = await bar.locator('.bar-segment').last().getAttribute('data-stage');
  const panel = page.locator(`.stage-panel[data-stage="${key}"]`);
  await expect(panel).toBeVisible();
  await expect(bar.getByRole('button', { name: 'Edit stage 3: Time Machine', exact: true })).toHaveAttribute('draggable', 'false');
  await panel.getByRole('combobox', { name: 'Finish stage by', exact: true }).selectOption('1');
  await expect(panel.getByRole('heading', { name: 'AT Adventure', exact: true })).toBeVisible();
  await expect(panel.getByRole('textbox', { name: 'Duration', exact: true })).toHaveCount(0);
  await panel.getByRole('textbox', { name: 'AT power target', exact: true }).fill('6000');
  await panel.getByRole('textbox', { name: 'AT toughness target', exact: true }).fill('7000');
  await dragStage(page, 2, 1);
  await expect(panel).toHaveAttribute('aria-label', 'Stage 2 settings');
  await expect(panel).toBeVisible();
  await expect(page.locator('.stage-panel:visible')).toHaveCount(1);
  await expect(bar.locator(`[data-select="${key}"]`)).toHaveAttribute('aria-expanded', 'true');
  await expect(bar.locator('strong')).toHaveText(['Time Machine', 'AT Adventure', 'Energy NGU']);
  await panel.getByRole('button', { name: 'Repeat from here', exact: true }).click();
  await expect(panel.getByRole('button', { name: 'Repeat from here', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await panel.getByRole('button', { name: 'Repeat from here', exact: true }).click();
  await expect(panel.getByRole('button', { name: 'Repeat from here', exact: true })).toHaveAttribute('aria-pressed', 'false');
  await panel.getByRole('button', { name: 'Repeat from here', exact: true }).click();
  await dragStage(page, 1, 2);
  await expect(panel).toHaveAttribute('aria-label', 'Stage 3 settings');
  await expect(panel).toBeVisible();
  await expect(page.locator('.stage-panel:visible')).toHaveCount(1);
  await expect(bar.locator(`[data-select="${key}"]`)).toHaveAttribute('aria-expanded', 'true');
  await expect(bar.locator('.bar-segment').last().getByLabel('Loop start', { exact: true })).toHaveText('Repeat');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan)).toMatchObject({
    StageLoopStartIndex: 2,
    Stages: [{ Name: 'Time Machine opener' }, { Name: 'NGU sustain' }, { Name: 'AT Adventure', CompletionMode: 1, TargetAdvancedTrainingPower: '6000', TargetAdvancedTrainingToughness: '7000' }]
  });
  await expect(page.locator('.status-line')).toContainText("Saved plan 'default'.");
  await expect(panel).toBeVisible();
  await panel.getByRole('button', { name: 'Remove stage', exact: true }).click();
  await expect(bar.locator('.bar-segment')).toHaveCount(2);
  await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan)).toMatchObject({ StageLoopStartIndex: -1, Stages: [{ Name: 'Time Machine opener' }, { Name: 'NGU sustain' }] });
  await expect(page.locator('.status-line')).toContainText("Saved plan 'default'.");
  await page.reload();
  await expect(bar.locator('strong')).toHaveText(['Time Machine', 'Energy NGU']);
  await openPlanDetails(page, 'Schedule & templates');
  await expect(page.getByRole('combobox', { name: 'Repeat from', exact: true })).toHaveValue('-1');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('duration drafts follow logical stages through selection, save readback, keyboard reorder and tabs', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
  await expect(bar.locator('.bar-segment')).toHaveCount(2);
  const keys = await bar.locator('.bar-segment').evaluateAll(segments => segments.map(segment => segment.getAttribute('data-stage')));
  const first = page.locator(`.stage-panel[data-stage="${keys[0]}"] input[aria-label="Duration"]`);
  const second = page.locator(`.stage-panel[data-stage="${keys[1]}"] input[aria-label="Duration"]`);
  await bar.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
  await first.fill('25m pending');
  await bar.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).click();
  await expect(first).toBeHidden();
  await second.fill('35m pending');
  await openPlanDetails(page, 'Rebirth settings');
  await page.getByRole('spinbutton', { name: 'Rebirth after (s)', exact: true }).fill('7200');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(7200);
  await expect(page.locator('.plan-main').getByRole('heading', { name: 'default', exact: true })).toBeVisible();
  await expect(first).toHaveValue('25m pending');
  await expect(second).toHaveValue('35m pending');
  await page.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).press('Alt+ArrowLeft');
  await expect(bar.locator('strong')).toHaveText(['Energy NGU', 'Time Machine']);
  await expect(page.locator(`.stage-panel[data-stage="${keys[1]}"]`)).toHaveAttribute('aria-label', 'Stage 1 settings');
  await expect(second).toBeVisible();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.Stages)).toMatchObject([{ Name: 'NGU sustain' }, { Name: 'Time Machine opener' }]);
  await expect(page.locator('.status-line')).toContainText("Saved plan 'default'.");
  const navigation = page.getByRole('navigation', { name: 'Primary', exact: true });
  await navigation.getByRole('button', { name: 'Misc', exact: true }).click();
  await expect(second).toBeHidden();
  await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(second).toHaveValue('35m pending');
  await bar.getByRole('button', { name: 'Edit stage 2: Time Machine', exact: true }).click();
  await expect(first).toHaveValue('25m pending');
  const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(first).toHaveValue('25m pending');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length)).toBe(reads);
  await first.press('Escape');
  await expect(first).toHaveValue('50%');
  await bar.getByRole('button', { name: 'Edit stage 1: Energy NGU', exact: true }).click();
  await expect(second).toHaveValue('35m pending');
  await second.press('Escape');
  await expect(second).toHaveValue('50%');
  const writes = await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites);
  expect(writes).toHaveLength(2);
  expect(writes.every(plan => (plan.Stages as Record<string, unknown>[]).every(stage => stage.Percent === 50 && !stage.DurationSeconds))).toBe(true);
});

test('matching live progress overlays the selected bar without selecting or navigating stages', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.addInitScript(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides = { planProgress: progress }; }, planProgressFixture());
  await page.goto('/');
  const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
  const first = bar.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true });
  await expect(page.locator('.stage-bar')).toHaveCount(1);
  await expect(page.getByRole('region', { name: 'Plan progression', exact: true })).toHaveCount(0);
  await expect(page.getByRole('progressbar', { name: 'Stage 1 progress', exact: true })).toHaveAttribute('aria-valuenow', '50');
  await expect(bar.locator('.bar-segment').first()).toHaveAttribute('aria-current', 'step');
  await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
  await first.click();
  const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
  const progress = { ...planProgressFixture(), activeStageIndex: 1, stageProgress: .25, stageElapsedSeconds: 225, elapsedSeconds: 1125 };
  await page.evaluate(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides = { planProgress: progress }; }, progress);
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(page.getByRole('progressbar', { name: 'Stage 2 progress', exact: true })).toHaveAttribute('aria-valuenow', '25');
  await expect(bar.locator('.bar-segment').first()).toHaveClass(/completed/);
  expect(await bar.locator('.bar-segment').first().locator('.runtime-fill').evaluate(element => (element as HTMLElement).style.width)).toBe('100%');
  expect(await bar.locator('.bar-segment').nth(1).locator('.runtime-fill').evaluate(element => (element as HTMLElement).style.width)).toBe('25%');
  await expect(bar.locator('.bar-segment').nth(1)).toHaveAttribute('aria-current', 'step');
  await expect(first).toHaveAttribute('aria-expanded', 'true');
  await expect(page.getByRole('region', { name: 'Stage 1 settings', exact: true })).toBeVisible();
  await expect(bar.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true })).toHaveAttribute('aria-expanded', 'false');
  const duration = page.getByRole('textbox', { name: 'Duration', exact: true });
  await duration.fill('25m pending');
  await expect(page.getByRole('progressbar')).toHaveCount(0);
  await expect(bar.locator('[aria-current="step"]')).toHaveCount(0);
  await expect(page.locator('.live-note')).toContainText('Live position is separate');
  await duration.press('Escape');
  await expect(page.getByRole('progressbar', { name: 'Stage 2 progress', exact: true })).toHaveAttribute('aria-valuenow', '25');
  await expect(first).toHaveAttribute('aria-expanded', 'true');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length)).toBe(reads);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
  await openPlanDetails(page, 'Rebirth settings');
  await page.getByRole('spinbutton', { name: 'Rebirth after (s)', exact: true }).fill('7200');
  await expect(page.getByRole('progressbar')).toHaveCount(0);
  await expect(page.locator('.live-note')).toContainText('Live position is separate');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(7200);
  await expect(page.getByRole('progressbar', { name: 'Stage 2 progress', exact: true })).toHaveAttribute('aria-valuenow', '25');
  await expect(first).toHaveAttribute('aria-expanded', 'true');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites.length)).toBe(1);
});

test('runtime overlay rejects mismatched full sequences while the editor bar stays usable', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
  await bar.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).click();
  const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
  const progress = planProgressFixture();
  const revision = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.__externalUiRevision);
  const mismatches = [
    { label: 'different revision', data: { ...progress, planRevision: 'f'.repeat(64) } },
    { label: 'different version', data: { ...progress, planVersion: 15 } },
    { label: 'different repeat boundary', data: { ...progress, repeating: false, loopStartIndex: -1 } },
    { label: 'different scheduler', data: { ...progress, schedulerMode: 'sequential' } },
    { label: 'removed stage', data: { ...progress, loopStartIndex: 0, stages: progress.stages.slice(0, 1) } },
    { label: 'reordered stages with matching names', data: { ...progress, stages: [...progress.stages].reverse().map((stage, index) => ({ ...stage, index, name: progress.stages[index].name })) } },
    { label: 'non-current duration', data: { ...progress, stages: [progress.stages[0], { ...progress.stages[1], durationSeconds: 901 }] } },
    { label: 'non-current completion', data: { ...progress, stages: [progress.stages[0], { ...progress.stages[1], completionMode: 1 }] } },
    { label: 'non-current allocation', data: { ...progress, stages: [progress.stages[0], { ...progress.stages[1], magicFocus: 1 }] } },
    { label: 'configured priority mismatch', data: { ...progress, stages: [progress.stages[0], { ...progress.stages[1], configuredEnergyPriorities: ['NGU', 'TM'] }] } },
    { label: 'missing configured metadata', data: { ...progress, stages: [progress.stages[0], { ...progress.stages[1], configuredMagicPriorities: undefined }] } }
  ];
  for (const { label, data } of mismatches) {
    await page.evaluate(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides = { planProgress: progress }; }, data);
    await page.getByRole('button', { name: 'Refresh', exact: true }).click();
    await expect(page.getByRole('progressbar'), label).toHaveCount(0);
    await expect(bar.locator('[aria-current="step"]'), label).toHaveCount(0);
    await expect(page.locator('.live-note'), label).toContainText('Live position is separate');
    await expect(bar.locator('strong'), label).toHaveText(['Time Machine', 'Energy NGU']);
    await expect(page.getByRole('region', { name: 'Stage 2 settings', exact: true }), label).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Focus preset', exact: true }), label).toBeEnabled();
  }
  await bar.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Stage 1 settings', exact: true })).toBeVisible();
  await bar.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).click();
  // Revisions are optional, but even a matching revision does not bypass the sequence check.
  await page.evaluate(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides = { planProgress: progress }; }, { ...mismatches[6].data, planRevision: revision });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(page.getByRole('progressbar')).toHaveCount(0);
  await page.evaluate(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides = { planProgress: progress }; }, {
    ...progress, planRevision: revision,
    stages: progress.stages.map(stage => ({ ...stage, name: 'Duplicated legacy name', energyPriorities: ['NGU'], magicPriorities: ['RIT'] }))
  });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(page.getByRole('progressbar', { name: 'Stage 1 progress', exact: true })).toHaveAttribute('aria-valuenow', '50');
  await expect(bar.locator('strong')).toHaveText(['Time Machine', 'Energy NGU']);
  await expect(page.locator('.live-position')).toContainText('Current: 1. Time Machine');
  await expect(page.getByRole('region', { name: 'Stage 2 settings', exact: true })).toBeVisible();
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length)).toBe(reads);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
});

for (const connected of [true, false]) {
  test(`${connected ? 'another runtime plan' : 'offline runtime data'} never replaces or disables the selected editor bar`, async ({ page }) => {
    await installNativeFixture(page, connected);
    await page.addInitScript(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides = { planProgress: progress }; }, planProgressFixture());
    await page.goto('/');
    await page.getByRole('combobox', { name: 'Plan', exact: true }).selectOption('24hr');
    await expect(page.locator('.plan-main').getByRole('heading', { name: '24hr', exact: true })).toBeVisible();
    const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
    const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
    await expect(bar.locator('strong')).toHaveText(['Time Machine', 'Energy NGU']);
    await expect(page.getByRole('progressbar')).toHaveCount(0);
    await expect(page.locator('.live-note')).toContainText(connected ? 'Editing 24hr, not the running plan.' : 'Offline editing.');
    await bar.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).click();
    const panel = page.getByRole('region', { name: 'Stage 2 settings', exact: true });
    await expect(panel).toBeVisible();
    await page.getByRole('button', { name: 'Refresh', exact: true }).click();
    await expect(panel).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('24hr');
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length)).toBe(reads);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    await panel.getByRole('combobox', { name: 'Focus preset', exact: true }).selectOption({ label: 'Wandoos' });
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixturePlanNames)).toEqual(['24hr']);
    await expect(bar.locator('strong')).toHaveText(['Time Machine', 'Wandoos']);
    await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('24hr');
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
