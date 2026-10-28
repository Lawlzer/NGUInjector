import { expect, test, type Page } from '@playwright/test';
import { presetLibrary } from './fixtures/global-presets';
import { setPriorities } from './fixtures/priority-picker';

type FixtureWindow = typeof window & {
  __fixtureCalls: string[];
  __fixtureSettingWrites: Record<string, unknown>[];
  __fixturePlanWrites: Record<string, unknown>[];
  __fixtureLibraryWrites: Record<string, unknown>[];
  __fixturePresetCommands: { command: string; args: Record<string, unknown> }[];
  __fixturePlanNames: string[];
  __fixtureCreatedPlans: { name: string; source: Record<string, unknown> | null }[];
  __fixtureDeletedPlans: string[];
  __fixtureState: () => { settings: Record<string, unknown>; plan: Record<string, unknown>; library: typeof presetLibrary };
  __fixtureFailNextSetting?: boolean;
  __fixtureFailNextPlan?: boolean;
  __fixtureConfirm?: boolean;
  __fixtureRuntimeOverrides?: Record<string, unknown>;
  __fixtureStageCommands: { command: string; args: Record<string, unknown> }[];
  __fixtureDialogs: string[];
  __fixtureStagePending?: boolean;
  __fixtureStageReject?: string;
  __fixtureStopReject?: string;
};

// Mock only native IPC. The real Svelte components, controller, validation, and styles run in Chromium.
async function installNativeFixture(page: Page, connected = true, planPatch: Record<string, unknown> = {}, runningPlan = false): Promise<void> {
  await page.addInitScript(({ connected, planPatch, runningPlan, presetLibrary }) => {
    const calls: string[] = [];
    const settingWrites: Record<string, unknown>[] = [];
    const planWrites: Record<string, unknown>[] = [];
    const libraryWrites: Record<string, unknown>[] = [];
    const presetCommands: FixtureWindow['__fixturePresetCommands'] = [];
    let library = structuredClone(presetLibrary);
    let applied: Record<string, unknown> = {};
    const planNames: string[] = [];
    const plans = ['default', '24hr'];
    const createdPlans: { name: string; source: Record<string, unknown> | null }[] = [];
    const deletedPlans: string[] = [];
    let revision = 10;
    const settings = {
      _energyNguWeights: [0, 0, 0, 0, 1, 0, 1, 0, 0], _magicNguWeights: [1, 0, 0, 0, 0, 0, 0],
      _customPlanFile: 'default', _allocationFile: 'default', _autoRebirth: false,
      _useCustomRebirthPlan: runningPlan, _customAutoBoostEnabled: true, _manageEnergy: true,
      _customAdventureMode: 0, _customAdventureEnabled: false, _manualAdventureZone: 0,
      _customAutoBoostCubePowerRatio: 2, _customAugmentsBloodEstimateSeconds: 300,
      _customBloodSpaghettiGoalPercent: 30, _customBloodCounterfeitGoalPercent: 400,
      _customIronPillSaveSeconds: 900, _customIronPillMinWorth: 0,
      _customIronPillHistory: [{ CastAtUtc: '2026-10-14T12:30:00Z', WaitSeconds: 900, Worth: 1234.5, RebirthSeconds: 7200 }]
    };
    let plan = {
      Version: 18, Name: 'default', AutoRebirthEnabled: false, TimeTriggerEnabled: true,
      TimeTriggerSeconds: 3600, NumberTriggerEnabled: false, NumberMultiplierTarget: 100,
      NumberClimbEnabled: false, NumberClimbMinSeconds: 480, NumberClimbMultiplierTarget: 100,
      TriggerMode: 0, StageSchedulerEnabled: true, StageDurationSeconds: 1800, StageLoopStartIndex: 1,
      GearGoal: 9, FightScoreMode: 0, ActiveGoalPresetIndex: 0, ActivePresetId: 'global_3',
      GoalPresets: [{ Name: 'Default', GearGoal: 9 }], RebirthHistory: [],
      Stages: [
        { Name: 'Time Machine opener', Percent: 50, DurationSeconds: 900, EnergyFocus: 1, MagicFocus: 1, GearGoal: 5, CompletionMode: 0, EnergyPriorities: [], MagicPriorities: [], FightSchedule: null },
        { Name: 'NGU sustain', Percent: 50, DurationSeconds: 900, EnergyFocus: 5, MagicFocus: 6, GearGoal: 2, CompletionMode: 0, EnergyPriorities: [], MagicPriorities: [], FightSchedule: null }
      ],
      __externalUiRevision: revision.toString(16).padStart(64, '0'),
      ...planPatch
    };
    // Native data outlives an App remount; only acknowledged writes survive a browser reload.
    const persisted = JSON.parse(sessionStorage.getItem('native-fixture') ?? 'null') as { settings: typeof settings; plan: typeof plan; library?: typeof library } | null;
    if (persisted) {
      Object.assign(settings, persisted.settings);
      plan = persisted.plan;
      library = persisted.library ?? library;
      revision = Number.parseInt(plan.__externalUiRevision, 16);
    }
    const persist = () => sessionStorage.setItem('native-fixture', JSON.stringify({ settings, plan, library }));
    const results: Record<string, object> = {};
    const stageCommands: FixtureWindow['__fixtureStageCommands'] = [];
    const dialogs: string[] = [];
    const state = async () => ({
      runtime_dir: 'C:\\Fixture\\NGUInjector', marker_enabled: true, connected,
      protocol: { version: 3, globalPresetsVersion: 2 }, ...await (await import('/e2e/fixtures/settings-cas.ts')).settingsSnapshot(settings), profiles: ['default'], plans,
      logs: [{ name: 'inject.log', size: 30 }], command_results: [],
      runtime: {
        externalUiEnabled: connected, status: connected ? 'running' : 'unloaded', protocolVersion: 3, settingsCasVersion: 1,
        startedAt: '2026-10-14T10:00:00Z', updatedAt: new Date().toISOString(),
        customStatus: 'Fixture automation idle.', version: 'test', activeFocusGoal: '',
        supportedCommands: ['readPlan', 'applySettingsPatch', 'savePlan', 'createPlan', 'deletePlan', 'focusGoal', 'stopFocus', 'equipGlobalPreset', 'manualFightPrep', 'manualRebirth', 'quickSave', 'safeProbe', 'previewGear', 'setPlanStage'],
        globalPresetsVersion: 2, loadoutPresetsVersion: 2, globalNguWeightsVersion: 1, resourceLanePrioritiesVersion: 2, loadoutPreset: applied,
        stageOverridesV1: true,
        character: { rebirthSeconds: 420 }, settings: { customPlanFile: settings._customPlanFile, useCustomRebirthPlan: settings._useCustomRebirthPlan, autoRebirth: settings._autoRebirth },
        bloodGoals: {
          available: true, status: 'available', reason: '', planName: settings._useCustomRebirthPlan ? settings._customPlanFile : '',
          spaghetti: { currentPercent: 10, goalPercent: 30, requiredBlood: 100000, remainingBlood: 90000, bloodPerSecond: 1000, estimatedSeconds: 90, status: 'Casting', productionAssumption: 'Optimal ritual with all available magic' },
          counterfeit: { currentPercent: 400, goalPercent: 400, requiredBlood: 100000, remainingBlood: 0, bloodPerSecond: 1000, estimatedSeconds: 0, status: 'Goal reached', productionAssumption: 'Optimal ritual with all available magic' }
        },
        ironPill: { available: true, planName: settings._useCustomRebirthPlan ? settings._customPlanFile : '', eligible: false, status: 'blocked', reason: 'Custom auto-rebirth is disabled.' },
        planBloodMagicVersion: 1,
        planVersion: 18, stageTimingVersion: 1, fightScheduleVersion: 1,
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
          case 'read_preset_library': return JSON.stringify(library);
          case 'save_preset_library': {
            const next = JSON.parse(String(args.libraryJson));
            libraryWrites.push(structuredClone(next));
            library = { ...next, __externalUiRevision: 'd'.repeat(64) };
            persist(); return { success: true, mode: connected ? 'connected' : 'offline' };
          }
          case 'create_plan': {
            const name = String(args.requestedName);
            if (plans.includes(name)) throw new Error('Plan already exists.');
            createdPlans.push({ name, source: args.sourceJson ? JSON.parse(String(args.sourceJson)) : null });
            plans.push(name);
            return name;
          }
          case 'delete_plan': {
            const name = String(args.name);
            if (['24hr', '15m-NUMBER-increase', '30m-NUMBER-increase', '1h-1m-NUMBER-increase'].includes(name) || !plans.includes(name)) throw new Error('Cannot delete this plan.');
            deletedPlans.push(name);
            plans.splice(plans.indexOf(name), 1);
            settings._customPlanFile = '24hr';
            return { success: true, mode: connected ? 'connected' : 'offline', data: { selectedPlan: '24hr' }, result: { data: { selectedPlan: '24hr' } } };
          }
          case 'save_settings_patch_json': {
            const casPath = '/e2e/fixtures/settings-cas.ts';
            const cas = await import(casPath);
            settingWrites.push(cas.settingsPatch(args));
            const fixture = window as FixtureWindow;
            if (fixture.__fixtureFailNextSetting) { fixture.__fixtureFailNextSetting = false; throw new Error('Setting save failed.'); }
            const receipt = await cas.commitSettings(settings, args, connected ? 'connected' : 'offline');
            persist();
            return receipt;
          }
          case 'save_plan': {
            const next = JSON.parse(String(args.planJson));
            planWrites.push(next);
            planNames.push(String(args.name));
            const fixture = window as FixtureWindow;
            if (fixture.__fixtureFailNextPlan) { fixture.__fixtureFailNextPlan = false; throw new Error('Plan save failed.'); }
            const permissionChanged = plan.AutoRebirthEnabled !== next.AutoRebirthEnabled;
            plan = { ...next, __externalUiRevision: (++revision).toString(16).padStart(64, '0') };
            if (permissionChanged && connected && settings._useCustomRebirthPlan && settings._customPlanFile === String(args.name)) settings._autoRebirth = plan.AutoRebirthEnabled;
            persist();
            return { success: true, mode: connected ? 'connected' : 'offline' };
          }
          case 'tail_log': return 'Injected successfully\nFixture safe log';
          case 'enqueue_command': {
            const envelope = args.envelope as { command: string; args: Record<string, unknown> };
            const id = `fixture-${calls.length}`;
            results[id] = { id, command: envelope.command, success: true, message: 'Fixture command completed.' };
            if (['equipGlobalPreset', 'stopFocus'].includes(envelope.command)) presetCommands.push(structuredClone(envelope));
            if (envelope.command === 'equipGlobalPreset') {
              const preset = library.Presets.find(row => row.Id === envelope.args.presetId)!;
              applied = { id: preset.Id, name: preset.Name, appliedName: preset.Name, preset: structuredClone(preset), appliedAutoUpdate: preset.AutoUpdate, autoUpdate: preset.AutoUpdate, owner: 'dashboard', currentOwner: 'dashboard', active: true, status: 'applied', energyNguWeights: [...settings._energyNguWeights], magicNguWeights: [...settings._magicNguWeights] };
              settings._useCustomRebirthPlan = false; settings._autoRebirth = false;
            }
            if (envelope.command === 'stopFocus') {
              const rejection = (window as FixtureWindow).__fixtureStopReject;
              if (rejection) results[id] = { id, command: envelope.command, success: false, message: rejection };
              else applied = { ...applied, active: false, currentOwner: 'displaced', status: 'historical' };
            }
            if (envelope.command === 'setPlanStage') {
              stageCommands.push(structuredClone(envelope));
              const rejection = (window as FixtureWindow).__fixtureStageReject;
              if (rejection) results[id] = { id, command: envelope.command, success: false, message: rejection };
            }
            return id;
          }
          case 'read_command_result': return (window as FixtureWindow).__fixtureStagePending ? null : results[String(args.id)] ?? null;
          case 'plugin:dialog|ask': {
            dialogs.push(String(args.message));
            return (window as FixtureWindow).__fixtureConfirm !== false;
          }
          case 'plugin:dialog|message': {
            dialogs.push(String(args.message));
            return (window as FixtureWindow).__fixtureConfirm === false ? 'No' : 'Yes';
          }
          case 'set_external_ui_enabled': return null;
          default: throw new Error(`Unexpected native invocation: ${command}`);
        }
      }
    };
    Object.assign(window, {
      __TAURI_INTERNALS__: native, __fixtureCalls: calls,
      __fixtureSettingWrites: settingWrites, __fixturePlanWrites: planWrites,
      __fixtureLibraryWrites: libraryWrites, __fixturePresetCommands: presetCommands,
      __fixturePlanNames: planNames,
      __fixtureCreatedPlans: createdPlans, __fixtureDeletedPlans: deletedPlans,
      __fixtureState: () => structuredClone({ settings, plan, library }),
      __fixtureStageCommands: stageCommands, __fixtureDialogs: dialogs
    });
    localStorage.clear();
  }, { connected, planPatch, runningPlan, presetLibrary });
}

function planProgressFixture(planName = 'default', activeStageIndex = 0) {
  return {
    available: true, status: 'running', planName, planVersion: 18, schedulerMode: 'stages',
    activeStageIndex, stageProgress: .5, elapsedSeconds: 450, stageElapsedSeconds: 450,
    planRevision: 'a'.padStart(64, '0'), stageEntry: `entry-${activeStageIndex}`,
    cycle: 0, repeating: true, loopStartIndex: 1, reason: '',
    stages: [
      { index: 0, state: activeStageIndex === 0 ? 'current' : 'completed', appliedSeconds: 450, elapsedSeconds: 600, visits: 2, name: 'Time Machine opener', durationSeconds: 900, configuredDurationSeconds: 900, flexWeight: 50, timingKind: 'fixed', completionMode: 0, energyFocus: 1, magicFocus: 1, gearGoal: 5, configuredEnergyPriorities: [], configuredMagicPriorities: [], energyPriorities: ['TM', 'NGU'], magicPriorities: ['TM', 'NGU'] },
      { index: 1, state: activeStageIndex === 1 ? 'current' : 'pending', appliedSeconds: 0, elapsedSeconds: 0, visits: 0, name: 'NGU sustain', durationSeconds: 900, configuredDurationSeconds: 900, flexWeight: 50, timingKind: 'fixed', completionMode: 0, energyFocus: 5, magicFocus: 6, gearGoal: 2, configuredEnergyPriorities: [], configuredMagicPriorities: [], energyPriorities: ['NGU', 'TM'], magicPriorities: ['NGU', 'RIT', 'TM'] }
    ]
  };
}

async function installManualStageFixture(page: Page, overrides: Record<string, unknown> = {}, connected = true, planPatch: Record<string, unknown> = {}) {
  await installNativeFixture(page, connected, planPatch);
  await page.addInitScript(overrides => {
    (window as FixtureWindow).__fixtureRuntimeOverrides = {
      settings: { customPlanFile: 'default', useCustomRebirthPlan: true, autoRebirth: false }, ...overrides
    };
  }, { planProgress: planProgressFixture(), ...overrides });
}

function noSelectionStageFixture(activeStageIndex: number | null = null) {
  const source = planProgressFixture();
  const progress = {
    ...source, activeStageIndex, scheduleComplete: true, stageEntry: 'generation:0', stageProgress: null,
    stages: source.stages.map(row => ({ ...row, configuredDurationSeconds: 0, durationSeconds: 0, timingKind: 'flexible', state: 'skipped', appliedSeconds: 0, elapsedSeconds: 0, visits: 0 }))
  };
  const planPatch = {
    StageDurationSeconds: 0, TimeTriggerEnabled: false,
    Stages: progress.stages.map(row => ({
      Name: row.name, Percent: row.flexWeight, DurationSeconds: 0, DurationUnit: '%', CompletionMode: 0,
      EnergyFocus: row.energyFocus, MagicFocus: row.magicFocus, GearGoal: row.gearGoal,
      EnergyPriorities: row.configuredEnergyPriorities, MagicPriorities: row.configuredMagicPriorities
    }))
  };
  return { progress, planPatch };
}

async function selectPlanTab(page: Page): Promise<void> {
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Plan & Rebirth', exact: true }).click();
  await expect(page.locator('#plan-editor')).toBeVisible();
}

async function openPlanDetails(page: Page, name: string) {
  const advanced = name === 'Advanced plan JSON' || name === 'Plan library';
  if (advanced) await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Advanced', exact: true }).click();
  else await selectPlanTab(page);
  const details = page.locator(`${advanced ? '.raw-grid' : '#plan-editor'} details`).filter({ has: page.locator('summary').filter({ hasText: new RegExp(`^${name}$`) }) });
  if (!await details.evaluate(element => element.hasAttribute('open'))) await details.locator(':scope > summary').press('Enter');
  await expect(details).toHaveAttribute('open', '');
  return details;
}

async function openNguWeights(page: Page) {
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Misc', exact: true }).click();
  const card = page.getByRole('article', { name: 'NGU weights', exact: true });
  await expect(card.getByRole('heading', { name: 'NGU weights', exact: true })).toBeVisible();
  await expect(card.getByRole('spinbutton')).toHaveCount(16);
  return card;
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

for (const connected of [true, false]) {
  test(`Auto use EXP setup autosaves and persists (${connected ? 'connected' : 'offline'})`, async ({ page }) => {
    await installNativeFixture(page, connected);
    await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
    await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
    await page.goto('/');
    await page.getByRole('button', { name: 'Misc', exact: true }).click();
    const card = page.getByRole('article', { name: 'Auto use EXP', exact: true });
    const toggle = card.getByRole('checkbox', { name: 'Auto use EXP', exact: true });
    await expect(toggle).not.toBeChecked();
    await expect(card.getByRole('spinbutton')).toHaveCount(8);
    const fields = [
      ['_customExpEnergyRatio', 'Energy BASE cap (E:M) ratio', '5', '7'],
      ['_customExpMagicRatio', 'Magic BASE cap (E:M) ratio', '1', '2'],
      ['_customExpEnergyPowerRatio', 'Energy BASE Power ratio', '5', '6'],
      ['_customExpEnergyCapRatio', 'Energy BASE Cap ratio', '160000', '170000'],
      ['_customExpEnergyBarsRatio', 'Energy BASE Bars ratio', '4', '3'],
      ['_customExpMagicPowerRatio', 'Magic BASE Power ratio', '5', '8'],
      ['_customExpMagicCapRatio', 'Magic BASE Cap ratio', '160000', '180000'],
      ['_customExpMagicBarsRatio', 'Magic BASE Bars ratio', '4', '9']
    ] as const;
    await page.clock.runFor(500);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
    for (const [, label, defaultValue, next] of fields) {
      const input = card.getByRole('spinbutton', { name: label, exact: true });
      await expect(input).toHaveValue(defaultValue);
      await expect(input).toBeEnabled();
      await expect(input).toHaveAttribute('min', '1');
      await expect(input).toHaveAttribute('max', '1000000000');
      await expect(input).toHaveAttribute('step', '1');
      await input.fill(next);
    }
    await page.clock.runFor(349);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
    await page.clock.runFor(1);
    const patch = Object.fromEntries(fields.map(([key, , , next]) => [key, Number(next)]));
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([patch]);
    await toggle.check();
    await page.clock.runFor(0);
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customAutoExpEnabled)).toBe(true);
    await toggle.uncheck();
    await page.clock.runFor(0);
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([patch, { _customAutoExpEnabled: true }, { _customAutoExpEnabled: false }]);
    await page.reload();
    await page.getByRole('button', { name: 'Misc', exact: true }).click();
    await expect(toggle).not.toBeChecked();
    for (const [, label, , next] of fields) await expect(card.getByRole('spinbutton', { name: label, exact: true })).toHaveValue(next);
    const rules = card.locator('details');
    await expect(rules).not.toHaveAttribute('open', '');
    await rules.getByText('Purchase rules', { exact: true }).click();
    await expect(rules.locator('p')).toBeVisible();
    await expect(rules).toContainText('Ratios target base stats, not gear totals or EXP spending.');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    for (const input of await card.getByRole('spinbutton').all()) {
      const box = (await input.boundingBox())!;
      expect(box.width).toBeGreaterThanOrEqual(80);
      expect(box.x).toBeGreaterThanOrEqual(0);
      expect(box.x + box.width).toBeLessThanOrEqual(page.viewportSize()!.width);
    }
  });
}

for (const action of ['restart', 'set'] as const) {
  test(`manual ${action} acts immediately without confirmation and sends the saved revision and current-entry token without optimistic movement`, async ({ page }) => {
    await installManualStageFixture(page);
    await page.goto('/');
    await selectPlanTab(page);
    const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
    const selectedStage = action === 'restart' ? 'Edit stage 1: Time Machine' : 'Edit stage 2: Energy NGU';
    await bar.getByRole('button', { name: selectedStage, exact: true }).click();
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([]);
    const button = action === 'set'
      ? page.getByRole('button', { name: 'Skip to selected stage', exact: true })
      : page.getByRole('button', { name: 'Restart current stage', exact: true });
    await expect(button).toBeEnabled();
    await page.evaluate(() => { (window as FixtureWindow).__fixtureConfirm = false; });
    await button.click();
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([{
      command: 'setPlanStage', args: { name: 'default', expectedRevision: 'a'.padStart(64, '0'), expectedEntry: 'entry-0', action, stageIndex: action === 'set' ? 1 : 0 }
    }]);
    await expect(button).toBeEnabled();
    await expect(page.getByRole('alert')).toHaveCount(0);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
    await expect(bar.locator('[aria-current="step"]')).toHaveCount(1);
    await expect(bar.locator('.bar-segment').first()).toHaveAttribute('aria-current', 'step');
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
    await expect(bar.getByRole('button', { name: selectedStage, exact: true })).toHaveAttribute('aria-expanded', 'true');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

test('stage selection switches between restart and jump without moving the active stage', async ({ page }) => {
  await installManualStageFixture(page);
  await page.goto('/');
  await selectPlanTab(page);
  const restart = page.getByRole('button', { name: 'Restart current stage', exact: true });
  const jump = page.getByRole('button', { name: 'Skip to selected stage', exact: true });
  const first = page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true });
  const second = page.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true });
  await expect(restart).toBeEnabled();
  await expect(jump).toHaveCount(0);
  await expect(page.locator('.stage-actions button')).toHaveCount(1);
  await expect(page.getByRole('button', { name: 'Skip current stage', exact: true })).toHaveCount(0);
  await first.click();
  await expect(restart).toBeEnabled();
  await first.press('ArrowRight');
  await expect(jump).toBeEnabled();
  await expect(restart).toHaveCount(0);
  await expect(page.locator('.stage-actions button')).toHaveCount(1);
  await expect(second).toHaveAttribute('aria-expanded', 'true');
  await second.click();
  await expect(restart).toBeEnabled();
  await expect(jump).toHaveCount(0);
  await second.click();
  await page.getByRole('button', { name: 'Close stage settings', exact: true }).click();
  await expect(restart).toBeEnabled();
  await second.click();
  await first.click();
  await expect(restart).toBeEnabled();
  await expect(jump).toHaveCount(0);
  await expect(page.locator('.bar-segment').first()).toHaveAttribute('aria-current', 'step');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([]);
});

test('skip to a selected earlier stage uses its index and returns to restart after live readback', async ({ page }) => {
  await installManualStageFixture(page, { planProgress: planProgressFixture('default', 1) });
  await page.goto('/');
  await selectPlanTab(page);
  await page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
  const jump = page.getByRole('button', { name: 'Skip to selected stage', exact: true });
  await expect(jump).toBeEnabled();
  await page.evaluate(() => { (window as FixtureWindow).__fixtureConfirm = false; });
  await jump.click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([{
    command: 'setPlanStage', args: { name: 'default', expectedRevision: 'a'.padStart(64, '0'), expectedEntry: 'entry-1', action: 'set', stageIndex: 0 }
  }]);
  await expect(page.locator('.bar-segment').nth(1)).toHaveAttribute('aria-current', 'step');
  await expect(jump).toBeEnabled();
  await page.evaluate(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides!.planProgress = progress; }, {
    ...planProgressFixture(), stageEntry: 'entry-backward', stageProgress: 0
  });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Restart current stage', exact: true })).toBeEnabled();
  await expect(jump).toHaveCount(0);
  await expect(page.locator('.bar-segment').first()).toHaveAttribute('aria-current', 'step');
  await expect(page.getByRole('region', { name: 'Stage 1 settings', exact: true })).toBeVisible();
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

for (const action of ['restart', 'set'] as const) {
  test(`automatic recovery clears without UI writes before an ordinary ${action} override`, async ({ page }) => {
    const base = planProgressFixture();
    const reason = 'Checkpoint write failed: file locked. Retrying automatically.';
    const combatPrep = { schemaVersion: 1, active: false, status: 'failed', cleanupPending: false, cleanupRecoveryRequired: true, cleanupRecoveryReason: 'Retiring historical restoration ownership.', cleanupAttemptCount: 0, runId: '' };
    const progress = { ...base, reason: 'Retiring historical restoration ownership.', cleanupRecoveryRequired: true, combatPrep, stages: base.stages.map((row, index) => index === 0 ? { ...row, executionMode: 1, timingKind: 'elapsed' } : row) };
    const Stages = progress.stages.map((row, index) => ({ Name: row.name, Percent: row.flexWeight, DurationSeconds: row.configuredDurationSeconds, EnergyFocus: row.energyFocus, MagicFocus: row.magicFocus, GearGoal: row.gearGoal, CompletionMode: row.completionMode, ExecutionMode: index === 0 ? 1 : 0, EnergyPriorities: row.configuredEnergyPriorities, MagicPriorities: row.configuredMagicPriorities }));
    await installManualStageFixture(page, { planProgress: progress, combatPrep, resourceAllocation: { state: 'Failed', reason } }, true, { Stages });
    await page.goto('/');
    await selectPlanTab(page);
    const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
    await bar.getByRole('button', { name: /^Edit stage 1:/ }).click();
    const panel = page.getByRole('region', { name: 'Stage 1 settings', exact: true });
    const restart = page.getByRole('button', { name: 'Restart current stage', exact: true });
    const set = panel.getByRole('button', { name: 'Set as current stage', exact: true });
    for (const button of [restart, set]) await expect(button).toBeDisabled();
    await expect(page.locator('.runtime-controls').getByRole('status')).toHaveText(`Reconciling: ${reason}`);
    await expect(restart).toHaveAttribute('title', `Reconciling: ${reason}`);
    await expect(page.locator('.runtime-controls')).not.toContainText(/acknowledge|Recovery required/);
    await expect(page.locator('.runtime-controls')).not.toContainText('Schedule complete');
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([]);
    await page.getByRole('button', { name: 'Refresh', exact: true }).click();
    await expect(restart).toBeDisabled();
    expect(await page.evaluate(() => ((window as FixtureWindow).__fixtureRuntimeOverrides!.combatPrep as Record<string, unknown>).cleanupRecoveryRequired)).toBe(true);

    await page.evaluate(progress => {
      const runtime = (window as FixtureWindow).__fixtureRuntimeOverrides!;
      runtime.combatPrep = { schemaVersion: 1, active: false, cleanupPending: false, cleanupRecoveryRequired: false };
      runtime.planProgress = { ...progress, activeStageIndex: 1, stageEntry: 'entry-successor', stageProgress: .01, stageElapsedSeconds: 4, reason: '', cleanupRecoveryRequired: false, combatPrep: runtime.combatPrep,
        stages: progress.stages.map((row, index) => ({ ...row, state: index === 0 ? 'completed' : 'current', appliedSeconds: index === 0 ? row.appliedSeconds : 4, visits: index === 0 ? row.visits : 1 })) };
      runtime.resourceAllocation = { state: 'Applied', owner: 'stage:default:1:entry-successor', reason: '' };
    }, progress);
    await page.getByRole('button', { name: 'Refresh', exact: true }).click();
    await expect(page.locator('.live-position')).toContainText('Current: 2. Energy NGU');
    await expect(bar.locator('[aria-current="step"]')).toHaveCount(1);
    await expect(page.locator('.runtime-controls')).not.toContainText('Reconciling');
    await bar.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).click();
    const successor = page.getByRole('region', { name: 'Stage 2 settings', exact: true });
    await successor.getByText('Runtime history', { exact: true }).click();
    await expect(successor.getByLabel('Stage runtime history', { exact: true })).toContainText('Actual applied: 4s');
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
    if (action === 'set') await bar.getByRole('button', { name: /^Edit stage 1:/ }).click();
    await (action === 'set' ? set : restart).click();
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([{
      command: 'setPlanStage', args: { name: 'default', expectedRevision: 'a'.padStart(64, '0'), expectedEntry: 'entry-successor', action, stageIndex: action === 'set' ? 0 : 1 }
    }]);
    await expect(page.locator('.live-position')).toContainText('Current: 2. Energy NGU');
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

for (const terminalState of ['completed', 'skipped']) {
  for (const action of ['restart', 'set']) {
    test(`${action} works on a finished ${terminalState} holding row and re-enters only on backend telemetry`, async ({ page }) => {
      const terminal = { ...planProgressFixture('default', 1), scheduleComplete: true, stageProgress: null };
      terminal.stages[1].state = terminalState;
      await installManualStageFixture(page, { planProgress: terminal });
      await page.goto('/');
      await selectPlanTab(page);
      await page.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).click();
      const button = page.getByRole('button', { name: action === 'restart' ? 'Restart current stage' : 'Set as current stage', exact: true });
      await expect(button).toBeEnabled();
      await expect(page.locator('.live-position')).toContainText('Sustaining: 2. Energy NGU');
      await expect(page.locator('.bar-segment[aria-current="step"]')).toHaveCount(0);
      const history = page.locator('.stage-panel:visible .stage-history');
      await expect(history).not.toHaveAttribute('open', '');
      await history.getByText('Runtime history', { exact: true }).click();
      await expect(history.getByLabel('Stage runtime history', { exact: true })).toBeVisible();
      await page.evaluate(() => {
        (window as FixtureWindow).__fixtureConfirm = false;
        (window as FixtureWindow).__fixtureStagePending = true;
      });
      await button.click();
      await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([{
        command: 'setPlanStage', args: { name: 'default', expectedRevision: 'a'.padStart(64, '0'), expectedEntry: 'entry-1', action, stageIndex: 1 }
      }]);
      await expect(button).toBeDisabled();
      await expect(history.getByLabel('Stage runtime history', { exact: true })).toContainText(`State: ${terminalState === 'completed' ? 'Completed' : 'Skipped'}`);
      await page.evaluate(() => { (window as FixtureWindow).__fixtureStagePending = false; });
      await expect(button).toBeEnabled();
      await expect(page.getByRole('alert')).toHaveCount(0);
      await expect(page.locator('.live-position')).toContainText('Sustaining: 2. Energy NGU');
      await expect(page.locator('.bar-segment[aria-current="step"]')).toHaveCount(0);
      const next = { ...planProgressFixture('default', 1), stageEntry: 'entry-reentered', scheduleComplete: false, stageProgress: 0 };
      await page.evaluate(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides!.planProgress = progress; }, next);
      await page.getByRole('button', { name: 'Refresh', exact: true }).click();
      await expect(page.locator('.live-position')).toContainText('Current: 2. Energy NGU');
      await expect(page.locator('.bar-segment').nth(1)).toHaveAttribute('aria-current', 'step');
      await expect(page.getByRole('progressbar', { name: 'Stage 2 progress', exact: true })).toHaveAttribute('aria-valuenow', '0');
      expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
      expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
      expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
    });
  }
}

for (const active of [null, -1]) {
  for (const outcome of ['current', 'holding']) {
    test(`no-selection terminal ${active} allows Set and waits for backend ${outcome} telemetry`, async ({ page }) => {
      const { progress, planPatch } = noSelectionStageFixture(active);
      await installManualStageFixture(page, { planProgress: progress }, true, planPatch);
      await page.goto('/');
      await selectPlanTab(page);
      await page.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).click();
      const set = page.getByRole('button', { name: 'Skip to selected stage', exact: true });
      await expect(set).toBeEnabled();
      await expect(page.getByText('Schedule complete', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Restart current stage', exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Skip current stage', exact: true })).toHaveCount(0);
      expect(await page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([]);
      expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
      await page.evaluate(() => {
        (window as FixtureWindow).__fixtureConfirm = false;
        (window as FixtureWindow).__fixtureStagePending = true;
      });
      await set.click();
      await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([{
        command: 'setPlanStage', args: { name: 'default', expectedRevision: 'a'.padStart(64, '0'), expectedEntry: 'generation:0', action: 'set', stageIndex: 1 }
      }]);
      await expect(set).toBeDisabled();
      await expect(page.getByRole('progressbar')).toHaveCount(0);
      await expect(page.locator('.bar-segment[aria-current="step"]')).toHaveCount(0);
      await page.evaluate(() => { (window as FixtureWindow).__fixtureStagePending = false; });
      await expect(set).toBeEnabled();
      await expect(page.getByRole('alert')).toHaveCount(0);
      await expect(page.getByText('Schedule complete', { exact: true })).toBeVisible();
      await expect(page.getByRole('progressbar')).toHaveCount(0);
      const next = { ...progress, activeStageIndex: 1, scheduleComplete: outcome === 'holding', stageEntry: 'generation:1', stageProgress: 0 };
      next.stages[1].state = outcome === 'current' ? 'current' : 'skipped';
      await page.evaluate(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides!.planProgress = progress; }, next);
      await page.getByRole('button', { name: 'Refresh', exact: true }).click();
      await expect(page.locator('.live-position')).toContainText(`${outcome === 'current' ? 'Current' : 'Sustaining'}: 2. Energy NGU`);
      await expect(page.locator('.bar-segment[aria-current="step"]')).toHaveCount(outcome === 'current' ? 1 : 0);
      await expect(page.getByRole('progressbar', { name: 'Stage 2 progress', exact: true })).toHaveAttribute('aria-valuenow', '0');
      expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
      expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
      expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
    });
  }
}

for (const scenario of ['dirty', 'stale revision', 'missing entry', 'unknown', 'paused']) {
  test(`no-selection terminal Set stays disabled for ${scenario}`, async ({ page }) => {
    const { progress, planPatch } = noSelectionStageFixture();
    if (scenario === 'stale revision') progress.planRevision = 'b'.repeat(64);
    if (scenario === 'missing entry') progress.stageEntry = '';
    if (scenario === 'unknown') progress.stages[1].state = 'unknown';
    if (scenario === 'paused') progress.status = 'paused';
    await installManualStageFixture(page, { planProgress: progress }, true, planPatch);
    await page.goto('/');
    await selectPlanTab(page);
    await page.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).click();
    if (scenario === 'dirty') await page.getByRole('textbox', { name: 'Flexible weight', exact: true }).fill('unfinished');
    for (const name of ['Skip to selected stage', 'Set as current stage']) await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
    await page.getByRole('button', { name: 'Set as current stage', exact: true }).dispatchEvent('click');
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  });
}

for (const reason of ['Stage entry changed.', 'Saved plan revision changed.']) {
  test(`no-selection terminal Set cannot retarget a backend stale rejection: ${reason}`, async ({ page }) => {
    const { progress, planPatch } = noSelectionStageFixture();
    await installManualStageFixture(page, { planProgress: progress }, true, planPatch);
    await page.goto('/');
    await selectPlanTab(page);
    await page.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).click();
    await page.evaluate(reason => {
      (window as FixtureWindow).__fixtureConfirm = false;
      (window as FixtureWindow).__fixtureStageReject = reason;
    }, reason);
    await page.getByRole('button', { name: 'Set as current stage', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText(reason);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([{
      command: 'setPlanStage', args: { name: 'default', expectedRevision: 'a'.padStart(64, '0'), expectedEntry: 'generation:0', action: 'set', stageIndex: 1 }
    }]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
    await expect(page.getByText('Schedule complete', { exact: true })).toBeVisible();
    await expect(page.locator('.bar-segment[aria-current="step"]')).toHaveCount(0);
    await expect(page.getByRole('progressbar')).toHaveCount(0);
  });
}

test('pending manual actions disable all override buttons and only backend telemetry changes progress or history', async ({ page }) => {
  await installManualStageFixture(page);
  await page.goto('/');
  await selectPlanTab(page);
  await page.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).click();
  const history = page.locator('.stage-panel:visible .stage-history');
  await expect(history).not.toHaveAttribute('open', '');
  await history.getByText('Runtime history', { exact: true }).click();
  const historyText = history.getByLabel('Stage runtime history', { exact: true });
  await expect(historyText).toBeVisible();
  await expect(historyText).toContainText('State: Pending');
  await page.evaluate(() => { (window as FixtureWindow).__fixtureStagePending = true; });
  await page.getByRole('button', { name: 'Skip to selected stage', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands.length)).toBe(1);
  for (const name of ['Skip to selected stage', 'Set as current stage']) await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
  await expect(page.locator('.status-line')).toHaveText('Working...');
  await expect(historyText).toContainText('State: Pending');
  await expect(page.getByRole('progressbar', { name: 'Stage 1 progress', exact: true })).toBeVisible();
  await page.evaluate(() => { (window as FixtureWindow).__fixtureStagePending = false; });
  await expect(page.getByRole('button', { name: 'Skip to selected stage', exact: true })).toBeEnabled();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(historyText).toContainText('State: Pending');
  await expect(page.getByRole('progressbar', { name: 'Stage 1 progress', exact: true })).toBeVisible();
  const next = { ...planProgressFixture('default', 1), lastStageOverride: 'set stage 2 at 500', lastStageOverrideAt: 500, stageProgress: 0 };
  next.stages[0].state = 'skipped';
  await page.evaluate(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides!.planProgress = progress; }, next);
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(page.getByRole('progressbar', { name: 'Stage 2 progress', exact: true })).toHaveAttribute('aria-valuenow', '0');
  const first = page.locator('.bar-segment').first();
  await expect(first).toContainText('Skipped');
  await expect(first).not.toHaveClass(/completed/);
  expect(await first.locator('.runtime-fill').evaluate(element => (element as HTMLElement).style.width)).toBe('0%');
  await expect(historyText).toContainText('State: Current');
  await expect(page.getByText('Last manual action:', { exact: false })).toHaveCount(0);
  await page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
  const skippedHistory = page.locator('.stage-panel:visible .stage-history');
  await skippedHistory.getByText('Runtime history', { exact: true }).click();
  await expect(skippedHistory.getByLabel('Stage runtime history', { exact: true })).toBeVisible();
  await expect(skippedHistory.getByLabel('Stage runtime history', { exact: true })).toHaveText('Fixed duration\nActual applied: 7m 30s\nElapsed selected (including offline): 10m\nVisits: 2\nState: Skipped');
  await page.getByRole('button', { name: 'Skip to selected stage', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands.length)).toBe(2);
  await expect(page.getByRole('button', { name: 'Skip to selected stage', exact: true })).toBeEnabled();
  await expect(page.getByRole('alert')).toHaveCount(0);
  expect((await page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands))[1]).toEqual({
    command: 'setPlanStage', args: { name: 'default', expectedRevision: 'a'.padStart(64, '0'), expectedEntry: 'entry-1', action: 'set', stageIndex: 0 }
  });
  await expect(page.getByRole('progressbar', { name: 'Stage 2 progress', exact: true })).toBeVisible();
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
});

test('manual stage actions fail closed when a stale revision arrives before clicking', async ({ page }) => {
  await installManualStageFixture(page);
  await page.goto('/');
  await selectPlanTab(page);
  const restart = page.getByRole('button', { name: 'Restart current stage', exact: true });
  await expect(restart).toBeEnabled();
  await page.evaluate(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides!.planProgress = progress; }, {
    ...planProgressFixture('default', 1), planRevision: 'b'.repeat(64)
  });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(restart).toBeDisabled();
  await restart.dispatchEvent('click');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  await expect(page.locator('.bar-segment[aria-current="step"]')).toHaveCount(0);
});

for (const reason of ['Boss push is active.', 'Rebirth prep is active.', 'Stage entry changed.', 'Saved plan revision changed.']) {
  test(`manual stage backend rejection remains visible without moving the current marker: ${reason}`, async ({ page }) => {
    await installManualStageFixture(page);
    await page.goto('/');
    await selectPlanTab(page);
    await page.evaluate(reason => {
      (window as FixtureWindow).__fixtureConfirm = false;
      (window as FixtureWindow).__fixtureStageReject = reason;
    }, reason);
    await page.getByRole('button', { name: 'Restart current stage', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText(reason);
    await expect(page.getByRole('progressbar', { name: 'Stage 1 progress', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Restart current stage', exact: true })).toBeEnabled();
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([{
      command: 'setPlanStage', args: { name: 'default', expectedRevision: 'a'.padStart(64, '0'), expectedEntry: 'entry-0', action: 'restart', stageIndex: 0 }
    }]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  });
}

test('manual rebirth still requires confirmation and cancellation does not enqueue', async ({ page }) => {
  await installManualStageFixture(page);
  await page.goto('/');
  await page.evaluate(() => { (window as FixtureWindow).__fixtureConfirm = false; });
  await page.getByRole('button', { name: 'Rebirth', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual(['Rebirth now? This resets the current run.']);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
});

for (const scenario of ['offline', 'unsupported', 'no capability', 'wrong revision', 'missing entry', 'paused', 'inactive plan', 'timing draft', 'JSON draft', 'settings draft']) {
  test(`manual stage actions stay disabled for ${scenario} without saving drafts`, async ({ page }) => {
    const progress: Record<string, unknown> = planProgressFixture();
    const overrides: Record<string, unknown> = { planProgress: progress };
    if (scenario === 'unsupported') overrides.supportedCommands = ['readPlan', 'savePlan', 'applySettingsPatch'];
    if (scenario === 'no capability') overrides.stageOverridesV1 = false;
    if (scenario === 'wrong revision') progress.planRevision = 'b'.repeat(64);
    if (scenario === 'missing entry') delete progress.stageEntry;
    if (scenario === 'paused') progress.status = 'paused';
    await installManualStageFixture(page, overrides, scenario !== 'offline');
    if (scenario === 'settings draft') {
      await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
      await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
    }
    await page.goto('/');
    if (scenario === 'inactive plan') await page.getByRole('combobox', { name: 'Plan', exact: true }).selectOption('24hr');
    await selectPlanTab(page);
    await page.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).click();
    if (scenario === 'timing draft') await page.getByRole('textbox', { name: 'Duration', exact: true }).fill('unfinished');
    if (scenario === 'JSON draft') {
      await openPlanDetails(page, 'Advanced plan JSON');
      await page.getByRole('textbox', { name: 'Advanced Plan JSON', exact: true }).fill('{');
      await selectPlanTab(page);
    }
    if (scenario === 'settings draft') {
      await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
      await page.getByRole('spinbutton', { name: 'Cube Power:Toughness ratio', exact: true }).fill('3');
      await selectPlanTab(page);
    }
    for (const name of ['Skip to selected stage', 'Set as current stage']) await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
    await page.getByRole('button', { name: 'Skip to selected stage', exact: true }).dispatchEvent('click');
    await page.getByRole('button', { name: 'Set as current stage', exact: true }).dispatchEvent('click');
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  });
}

test('stage hover and keyboard descriptions expose actual history, with identical touch-panel details', async ({ page }) => {
  const progress = planProgressFixture();
  progress.stages[1].state = 'skipped';
  for (const key of ['appliedSeconds', 'elapsedSeconds', 'visits']) delete (progress.stages[1] as Record<string, unknown>)[key];
  await installManualStageFixture(page, { planProgress: progress });
  await page.goto('/');
  await selectPlanTab(page);
  const stage = page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true });
  const details = 'Fixed duration\nActual applied: 7m 30s\nElapsed selected (including offline): 10m\nVisits: 2\nState: Current';
  await stage.hover();
  await expect(stage).toHaveAttribute('title', `${details}\nClick to edit; drag left or right to reorder. Keyboard: Alt+Left / Alt+Right.`);
  await stage.focus();
  await expect(stage).toHaveAccessibleDescription(details.replaceAll('\n', ' '));
  await stage.press('Enter');
  const panel = page.getByRole('region', { name: 'Stage 1 settings', exact: true });
  await expect(panel.locator('.stage-history')).not.toHaveAttribute('open', '');
  await expect(panel.getByLabel('Stage runtime history', { exact: true })).toBeHidden();
  await panel.getByText('Runtime history', { exact: true }).click();
  await expect(panel.getByLabel('Stage runtime history', { exact: true })).toBeVisible();
  await expect(panel.getByLabel('Stage runtime history', { exact: true })).toHaveText(details);
  await page.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).click();
  const secondPanel = page.getByRole('region', { name: 'Stage 2 settings', exact: true });
  await expect(secondPanel.locator('.stage-history')).not.toHaveAttribute('open', '');
  await secondPanel.getByText('Runtime history', { exact: true }).click();
  const secondHistory = secondPanel.getByLabel('Stage runtime history', { exact: true });
  await expect(secondHistory).toBeVisible();
  await expect(secondHistory).toContainText('State: Skipped');
  await expect(secondHistory).toContainText('Actual applied: Not recorded');
  await expect(secondHistory).toContainText('Visits: Not recorded');
  await expect(page.locator('.bar-segment').nth(1)).toContainText('Skipped');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureStageCommands)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('Auto use EXP invalid ratios stay local until corrected', async ({ page }) => {
  await installNativeFixture(page, false);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  await page.getByRole('button', { name: 'Misc', exact: true }).click();
  const input = page.getByRole('spinbutton', { name: 'Energy BASE Cap ratio', exact: true });
  for (const invalid of ['', '0', '-1', '1.5', '1000000001', '1e-400', '1.0000000000000001']) {
    await input.fill(invalid);
    await page.clock.runFor(350);
    await expect(input).toHaveValue(invalid);
    await expect(page.getByRole('alert').first()).toContainText('_customExpEnergyCapRatio');
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  }
  for (const valid of ['1', '1000000000', '160000']) {
    await input.fill(valid);
    await page.clock.runFor(350);
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customExpEnergyCapRatio)).toBe(Number(valid));
  }
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('Auto use EXP failed saves retain edits for explicit retry', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  await page.getByRole('button', { name: 'Misc', exact: true }).click();
  await page.evaluate(() => { (window as FixtureWindow).__fixtureFailNextSetting = true; });
  const input = page.getByRole('spinbutton', { name: 'Magic BASE Bars ratio', exact: true });
  await input.fill('12');
  await page.clock.runFor(350);
  await expect(page.getByRole('alert').first()).toContainText('Setting save failed');
  await page.clock.runFor(1000);
  await expect(input).toHaveValue('12');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([{ _customExpMagicBarsRatio: 12 }]);
  await page.getByRole('button', { name: 'Retry saving', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customExpMagicBarsRatio)).toBe(12);
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('NGU weight controls label all targets, emit correct arrays, autosave, and fit mobile', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  await openNguWeights(page);
  await expect(page.getByRole('button', { name: 'Misc', exact: true })).toHaveAttribute('aria-current', 'page');
  const card = page.getByRole('article', { name: 'NGU weights', exact: true });
  const energy = ['Augments', 'Wandoos', 'Respawn', 'Gold', 'Adventure Alpha', 'Power', 'Drop Chance', 'Magic NGU', 'PP'];
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
  const rules = card.locator('details');
  await expect(rules).not.toHaveAttribute('open', '');
  await rules.getByText('Weight rules', { exact: true }).click();
  await expect(rules.locator('p')).toBeVisible();
  await expect(rules).toContainText('Excess at a buffered cap goes to other targets.');
  await expect(rules).toContainText('All-zero weights skip NGU for that resource, allowing later priorities.');
  const before = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
  const adventure = card.getByRole('spinbutton', { name: 'Energy Adventure Alpha weight', exact: true });
  await adventure.fill('2');
  await page.clock.runFor(200);
  await card.getByRole('spinbutton', { name: 'Magic EXP weight', exact: true }).fill('3');
  await page.clock.runFor(349);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  await page.clock.runFor(1);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites.length)).toBe(1);
  const saved = await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites[0]);
  expect(saved).toEqual({ _energyNguWeights: [0, 0, 0, 0, 2, 0, 1, 0, 0], _magicNguWeights: [1, 3, 0, 0, 0, 0, 0] });
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan)).toEqual(before);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('Misc directly edits PAWGs and Yggdrasil-only weights and persists them across reload', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  const before = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
  const card = await openNguWeights(page);
  await expect(card).toContainText('Global weights for all plans, presets, and Quick actions.');
  await expect(card).not.toContainText(/autosave|save automatically|storage|polling/i);
  const energy = ['Augments', 'Wandoos', 'Respawn', 'Gold', 'Adventure Alpha', 'Power', 'Drop Chance', 'Magic NGU', 'PP'];
  const magic = ['Yggdrasil', 'EXP', 'Power Beta', 'Number', 'Time Machine', 'Energy NGU', 'Adventure Beta'];
  for (const [index, name] of energy.entries()) await card.getByRole('spinbutton', { name: `Energy ${name} weight`, exact: true }).fill([0, 1, 3, 5].includes(index) ? '1' : '0');
  for (const [index, name] of magic.entries()) await card.getByRole('spinbutton', { name: `Magic ${name} weight`, exact: true }).fill(index === 0 ? '1' : '0');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites.length)).toBe(1);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites[0])).toEqual({ _energyNguWeights: [1, 1, 0, 1, 0, 1, 0, 0, 0], _magicNguWeights: [1, 0, 0, 0, 0, 0, 0] });
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan)).toEqual(before);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  await page.reload();
  await openNguWeights(page);
  for (const [index, name] of energy.entries()) await expect(card.getByRole('spinbutton', { name: `Energy ${name} weight`, exact: true })).toHaveValue([0, 1, 3, 5].includes(index) ? '1' : '0');
  for (const [index, name] of magic.entries()) await expect(card.getByRole('spinbutton', { name: `Magic ${name} weight`, exact: true })).toHaveValue(index === 0 ? '1' : '0');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('NGU weight invalids stay local and corrected all-zero weights save without normalization', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  await openNguWeights(page);
  const input = page.getByRole('spinbutton', { name: 'Magic Yggdrasil weight', exact: true });
  for (const invalid of ['', '-1', '0.5', '2147483648', '1e-400', '1.0000000000000001']) {
    await input.fill(invalid);
    await page.clock.runFor(350);
    await expect(input).toHaveValue(invalid);
    await expect(page.getByRole('alert').first()).toContainText('_magicNguWeights[0]');
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._magicNguWeights)).toEqual([1, 0, 0, 0, 0, 0, 0]);
  }
  await input.fill('0');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._magicNguWeights)).toEqual([0, 0, 0, 0, 0, 0, 0]);
});

test('NGU weight inputs accept exact whole decimal and exponent forms', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  await openNguWeights(page);
  const input = page.getByRole('spinbutton', { name: 'Magic Yggdrasil weight', exact: true });
  for (const [token, weight] of [['2.0', 2], ['3e0', 3], ['0e0', 0]] as const) {
    await input.fill(token);
    await page.clock.runFor(350);
    await expect.poll(() => page.evaluate(() => ((window as FixtureWindow).__fixtureState().settings._magicNguWeights as number[])[0])).toBe(weight);
  }
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites.length)).toBe(3);
});

test('Misc NGU weights preserve the selected plan without loading the active plan', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await openPlanDetails(page, 'Plan library');
  await page.getByRole('combobox', { name: 'Plan to edit', exact: true }).selectOption('24hr');
  await expect(page.locator('.plan-main')).toContainText('Editing 24hr; default is active.');
  const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
  await openNguWeights(page);
  await expect(page.getByRole('button', { name: 'Misc', exact: true })).toHaveAttribute('aria-current', 'page');
  const card = page.getByRole('article', { name: 'NGU weights', exact: true });
  await expect(card).toHaveAttribute('id', 'ngu-weights');
  await expect(card).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Plan to edit', exact: true, includeHidden: true })).toHaveValue('24hr');
  await expect(card).toContainText('Global weights for all plans, presets, and Quick actions.');
  await page.waitForTimeout(700);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length)).toBe(reads);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  await card.getByRole('spinbutton', { name: 'Energy Adventure Alpha weight', exact: true }).fill('4');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([{ _energyNguWeights: [0, 0, 0, 0, 4, 0, 1, 0, 0] }]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanNames)).toEqual([]);
});

test('NGU weights are disabled for unsupported runtime capability without affecting saved values', async ({ page }) => {
  await installNativeFixture(page);
  await page.addInitScript(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { globalNguWeightsVersion: 0 }; });
  await page.goto('/');
  await openNguWeights(page);
  const input = page.getByRole('spinbutton', { name: 'Energy Adventure Alpha weight', exact: true });
  await expect(input).toBeDisabled();
  await expect(input).toHaveValue('1');
  const notice = page.getByRole('article', { name: 'NGU weights', exact: true }).getByRole('status');
  await expect(notice).toHaveText('Weight editing unavailable. Update the injector, reconnect, and reload.');
  for (const weight of await page.getByRole('article', { name: 'NGU weights', exact: true }).getByRole('spinbutton').all()) await expect(weight).toBeDisabled();
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
});

for (const missing of ['plan', 'settings'] as const) {
  test(`global NGU editor handles missing ${missing} without creating defaults or plan overrides`, async ({ page }) => {
    await installNativeFixture(page);
    await page.addInitScript(missing => {
      const native = (window as typeof window & { __TAURI_INTERNALS__: { invoke: (command: string, args?: Record<string, unknown>) => Promise<unknown> } }).__TAURI_INTERNALS__;
      const invoke = native.invoke;
      native.invoke = async (command, args) => {
        const result = await invoke(command, args);
        if (command !== 'get_ngui_state') return result;
        const state = structuredClone(result) as { plans: string[]; settings_json: string; settings_revision: string };
        if (missing === 'plan') state.plans = [];
        else {
          const casPath = '/e2e/fixtures/settings-cas.ts';
          const cas = await import(casPath);
          const settings = cas.parseSettingsJson(state.settings_json);
          delete settings._energyNguWeights;
          delete settings._magicNguWeights;
          Object.assign(state, await cas.settingsSnapshot(settings));
        }
        return state;
      };
    }, missing);
    await page.goto('/');
    await page.getByRole('button', { name: 'Misc', exact: true }).click();
    const card = page.getByRole('article', { name: 'NGU weights', exact: true });
    if (missing === 'plan') {
      await expect(card.getByRole('spinbutton')).toHaveCount(16);
      await card.getByRole('spinbutton', { name: 'Energy Power weight', exact: true }).fill('3');
      await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([{ _energyNguWeights: [0, 0, 0, 0, 1, 3, 1, 0, 0] }]);
      expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('read_plan');
    } else {
      await expect(card.getByRole('spinbutton')).toHaveCount(0);
      await expect(card).toContainText('Global NGU weights unavailable.');
      expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
    }
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

test('dashboard loads without JS errors and fits the viewport', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await installNativeFixture(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'NGU Injector', exact: true })).toBeVisible();
  await expect(page.locator('div[aria-label="Actions"]')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Fight bosses', exact: true })).toBeEnabled();
  await expect(page.getByRole('heading', { name: 'Plan & Rebirth', exact: true })).toBeVisible();
  await expect(page.getByRole('article', { name: 'Quick', exact: true }).getByRole('heading', { name: 'Quick', exact: true })).toBeVisible();
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

test('dashboard keeps routine noise hidden and settings in compact labeled groups', async ({ page }) => {
  await installManualStageFixture(page);
  await page.goto('/');
  const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
  await expect(page.locator('#plan-editor')).toBeHidden();
  await expect(page.locator('.status-line')).toBeEmpty();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Actions', exact: true })).toHaveCount(0);
  const actions = page.locator('div[aria-label="Actions"]');
  await expect(actions.getByRole('button')).toHaveText(['Fight / Boss Push', 'Manual Titans', 'Clear quick', 'Fight bosses', 'Rebirth', 'Quicksave']);
  for (const button of await actions.getByRole('button').all()) await expect(button).toBeEnabled();
  const activity = page.locator('.current-state details');
  await expect(activity).not.toHaveAttribute('open', '');
  await expect(activity.locator('p')).toBeHidden();
  await activity.getByText('Activity details', { exact: true }).click();
  await expect(activity.locator('p')).toBeVisible();
  await expect(activity.locator('p')).toHaveText('Fixture automation idle.');
  await activity.locator('summary').click();
  await expect(page.getByText('Last manual action:', { exact: false })).toHaveCount(0);
  await selectPlanTab(page);
  await expect(bar).toBeVisible();
  await expect(page.locator('dl[aria-label="Schedule budget"], .schedule-budget')).toHaveCount(0);
  await expect(bar.locator('.segment-duration')).toHaveText(['15m fixed', '15m fixed']);
  await bar.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
  const stage = page.getByRole('region', { name: 'Stage 1 settings', exact: true });
  await expect(stage.locator('.resource-fields > fieldset')).toHaveCount(2);
  for (const resource of ['Energy', 'Magic']) {
    const group = stage.getByRole('group', { name: resource, exact: true });
    await expect(group.getByRole('combobox', { name: `${resource} focus`, exact: true })).toBeVisible();
    await expect(group.getByRole('group', { name: `${resource} priorities`, exact: true })).toBeVisible();
  }
  const history = stage.locator('.stage-history');
  await expect(history).not.toHaveAttribute('open', '');
  await expect(history.locator('summary')).toHaveText('Runtime history');
  await expect(history.getByLabel('Stage runtime history', { exact: true })).toBeHidden();
  await stage.getByRole('button', { name: 'Close stage settings', exact: true }).click();
  const weights = await openNguWeights(page);
  await expect(weights.locator('.weight-groups > fieldset')).toHaveCount(2);
  await expect(weights.getByRole('group', { name: 'Energy', exact: true }).getByRole('spinbutton')).toHaveCount(9);
  await expect(weights.getByRole('group', { name: 'Magic', exact: true }).getByRole('spinbutton')).toHaveCount(7);
  await expect(weights.locator('details')).not.toHaveAttribute('open', '');
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  const ironPill = page.locator('.dashboard-support > article').filter({ has: page.getByRole('heading', { name: 'Iron Pill', exact: true }) });
  await expect(ironPill.locator('details')).toHaveCount(0);
  await expect(ironPill).not.toContainText('Casting rules');
  await expect(page.getByRole('region', { name: 'Iron Pill cast history', exact: true })).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('flexible stage segments show estimated time and remaining share instead of a budget summary', async ({ page }) => {
  const stages = [
    { Name: 'Fixed opener', Percent: 100, DurationSeconds: 7200, CompletionMode: 0, EnergyFocus: 1, MagicFocus: 1 },
    { Name: 'Flexible sustain', Percent: 100, DurationSeconds: 0, CompletionMode: 0, EnergyFocus: 5, MagicFocus: 6 }
  ];
  await installNativeFixture(page, true, { TimeTriggerSeconds: 86400, Stages: stages });
  await page.goto('/');
  await selectPlanTab(page);
  const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
  await expect(page.locator('dl[aria-label="Schedule budget"], .schedule-budget')).toHaveCount(0);
  await expect(bar.locator('.segment-duration')).toHaveText(['2h fixed', '22h flexible']);
  await expect(bar.locator('.bar-segment').nth(1).getByText('100% of remaining', { exact: true })).toBeVisible();
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan)).toMatchObject({ TimeTriggerSeconds: 86400, Stages: stages });
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('Dashboard keeps plan controls while Edit plan opens one full-width editor on Plan & Rebirth', async ({ page }) => {
  await installNativeFixture(page, true, {}, true);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  const editor = page.locator('#plan-editor');
  await expect(editor).toHaveCount(1);
  await expect(editor).toBeHidden();
  const controls = page.getByRole('article', { name: 'Plan controls', exact: true });
  await expect(controls.getByRole('combobox', { name: 'Plan', exact: true })).toBeVisible();
  await expect(controls.getByRole('button', { name: 'Disable plan', exact: true })).toBeEnabled();
  await expect(controls.getByRole('button', { name: /^Auto-rebirth:/ })).toHaveCount(0);
  const dashboardBox = (await page.locator('.dashboard-grid').boundingBox())!;
  const controlsBox = (await controls.boundingBox())!;
  if (page.viewportSize()!.width > 740) {
    const nowBox = (await page.getByRole('article', { name: 'Now', exact: true }).boundingBox())!;
    expect(controlsBox.width).toBeCloseTo(nowBox.width, 0);
    expect(controlsBox.width).toBeGreaterThan(dashboardBox.width * .47);
  } else expect(controlsBox.width).toBeCloseTo(dashboardBox.width, 0);
  const blood = page.getByRole('article', { name: 'Blood Magic', exact: true });
  await expect(blood).toContainText('90K blood left');
  await expect(blood).toContainText('ETA ~1m 30s');
  await expect(blood.getByText('ETA ~1m 30s', { exact: true })).toHaveAttribute('title', 'ETA assumes all magic with current gear.');
  await expect(blood.locator('details')).toHaveCount(0);
  await controls.getByRole('button', { name: 'Edit plan', exact: true }).click();
  await page.clock.runFor(500);
  await expect(page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Plan & Rebirth', exact: true })).toHaveAttribute('aria-current', 'page');
  await expect(editor).toBeVisible();
  await expect(blood).toBeHidden();
  await expect(editor).toHaveAttribute('id', 'plan-editor');
  await expect(page.locator('.plan-main')).toHaveCount(1);
  await expect(page.locator('.plan-sidebar')).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Plan progression', exact: true })).toHaveCount(0);
  await expect(editor.locator('.stage-row, .stage-list, table')).toHaveCount(0);
  await expect(editor.locator(':scope > *')).toHaveCount(1);
  await expect(editor.locator(':scope > .plan-main')).toHaveCount(1);
  await expect(editor.locator('.advanced-plan, .plan-management, .history-list')).toHaveCount(0);
  await expect(editor.getByText('Rebirth history', { exact: true })).toHaveCount(0);
  const schedule = editor.locator('.schedule-settings');
  await expect(schedule).not.toHaveAttribute('open', '');
  await expect(schedule.locator(':scope > summary')).toHaveText('Schedule & templates');
  await expect(schedule.locator(':scope > summary')).toBeVisible();
  const advanced = page.locator('.raw-grid');
  const management = advanced.locator('details.card.plan-management');
  for (const disclosure of [advanced.locator('.advanced-plan'), management]) {
    await expect(disclosure).toHaveCount(1);
    await expect(disclosure).not.toHaveAttribute('open', '');
    await expect(disclosure).toBeHidden();
  }
  await expect(editor.getByRole('article', { name: 'Rebirth conditions', exact: true })).toBeVisible();
  await expect(editor.getByRole('combobox', { name: 'Active preset', exact: true })).toHaveCount(0);
  await expect(editor.getByRole('combobox', { name: 'Fight scoring', exact: true })).toHaveCount(0);
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
  const mainBox = await editor.locator('.plan-main').boundingBox();
  expect(editorBox!.width).toBeGreaterThan(dashboardBox.width * .94);
  expect(mainBox!.width).toBeGreaterThan(editorBox!.width * .94);
  await expect(editor.locator(':scope > details')).toHaveCount(0);
  await expect(editor.getByText('Editing plan', { exact: true })).toHaveCount(0);
  await expect(editor.locator('.selection-hint, .stages-heading')).toHaveCount(0);

  await bar.scrollIntoViewIfNeeded();
  await expect(bar).toBeInViewport();
  await bar.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
  const stage = editor.getByRole('region', { name: 'Stage 1 settings', exact: true });
  await expect(stage).toBeVisible();
  const stageBox = await stage.boundingBox();
  const barBox = await bar.boundingBox();
  expect(stageBox!.y).toBeGreaterThanOrEqual(barBox!.y + barBox!.height);
  await expect(stage.getByRole('heading', { name: 'Time Machine', exact: true })).toBeVisible();
  await expect(stage.getByRole('combobox', { name: 'Energy focus', exact: true })).toHaveValue('1');
  await expect(stage.getByRole('combobox', { name: 'Magic focus', exact: true })).toHaveValue('1');
  await expect(stage.getByRole('combobox', { name: /^Finish stage by/ })).toHaveValue('fixed');
  await expect(editor.locator('.stage-panel select[aria-label="Gear goal"], .stage-panel input[aria-label="Black-bar lookahead (s)"]')).toHaveCount(0);
  await expect(editor.getByText('Cap buffer (seconds)', { exact: false })).toHaveCount(0);
  await expect(editor.getByRole('button', { name: /^Move (earlier|later)$/, includeHidden: true })).toHaveCount(0);
  await expect(bar.locator('.handle, [data-handle]')).toHaveCount(0);
  await expect(stage.getByRole('checkbox', { name: 'Fights enabled', exact: true })).not.toBeChecked();
  await expect(editor.getByRole('combobox', { name: 'Stage timing', exact: true })).toHaveCount(0);
  await expect(editor.getByRole('checkbox', { name: 'Protect fixed stages', exact: true })).toHaveCount(0);
  const duration = stage.getByRole('textbox', { name: 'Duration', exact: true });
  await duration.fill('25m');
  await duration.press('Enter');
  await setPriorities(stage, 'Energy priorities', ['Wandoos', 'NGU (weighted allocation)', 'Time Machine']);
  await setPriorities(stage, 'Magic priorities', ['Wandoos', 'Blood Magic (automatic ritual)', 'NGU (weighted allocation)', 'Time Machine']);
  await expect(stage.getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('');
  await stage.getByRole('combobox', { name: 'Energy focus', exact: true }).selectOption('1');
  await stage.getByRole('combobox', { name: 'Focus preset', exact: true }).selectOption({ label: 'Wandoos' });
  await expect(stage.getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('global_2');
  await expect(stage.getByRole('group', { name: 'Energy priorities', exact: true })).toHaveCount(0);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Misc', exact: true }).click();
  const gear = page.getByRole('article', { name: 'Gear settings', exact: true });
  await gear.getByRole('combobox', { name: 'Active preset', exact: true }).selectOption('global_5');
  await selectPlanTab(page);
  await editor.getByRole('textbox', { name: 'Rebirth after', exact: true }).fill('2h');
  await editor.getByRole('textbox', { name: 'Rebirth after', exact: true }).press('Enter');
  await openNguWeights(page);
  const weights = page.locator('#ngu-weights');
  await expect(weights).toHaveCount(1);
  await weights.getByRole('spinbutton', { name: 'Magic EXP weight', exact: true }).fill('3');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites.length)).toBe(1);
  const saved = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
  expect(saved).toMatchObject({
    TimeTriggerSeconds: 7200, ActivePresetId: 'global_5', GearGoal: 9, GoalPresets: [{ Name: 'Default', GearGoal: 9 }],
    Stages: [
      { Name: 'Wandoos', PresetId: 'global_2', DurationSeconds: 1500, EnergyFocus: 1, MagicFocus: 1, GearGoal: 5, EnergyPriorities: ['WAN', 'NGU', 'TM'], MagicPriorities: ['WAN', 'RIT', 'NGU', 'TM'] },
      { Name: 'NGU sustain', DurationSeconds: 900 }
    ]
  });
  expect(saved).not.toHaveProperty('EnergyNguWeights');
  expect(saved).not.toHaveProperty('MagicNguWeights');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([{ _magicNguWeights: [1, 3, 0, 0, 0, 0, 0] }]);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Advanced', exact: true }).click();
  const rawDetails = advanced.locator('.advanced-plan');
  await expect(rawDetails).not.toHaveAttribute('open', '');
  await expect(page.getByRole('textbox', { name: 'Advanced Plan JSON', exact: true })).toHaveCount(0);
  await expect(weights).toBeHidden();
  await expect(editor).toBeHidden();
  await expect(management.locator(':scope > summary')).toBeVisible();
  await openPlanDetails(page, 'Advanced plan JSON');
  const raw = page.getByRole('textbox', { name: 'Advanced Plan JSON', exact: true });
  await expect(raw).toBeVisible();
  await expect.poll(async () => JSON.parse(await raw.inputValue())).toMatchObject({ TimeTriggerSeconds: 7200, ActivePresetId: 'global_5', GearGoal: 9, GoalPresets: saved.GoalPresets, Stages: saved.Stages });
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Logs', exact: true }).click();
  const history = page.getByRole('article', { name: 'Rebirth history', exact: true });
  await expect(history.getByRole('heading', { level: 2, name: 'Rebirth history', exact: true })).toBeVisible();
  await expect(history.getByText('No rebirths yet.', { exact: true })).toBeVisible();
  await expect(history.getByRole('table')).toHaveCount(0);
  await openPlanDetails(page, 'Plan library');
  await expect(management.getByRole('heading', { level: 2, name: 'Plans', exact: true })).toHaveCount(0);
  await expect(management.getByRole('combobox', { name: 'Plan to edit', exact: true })).toHaveValue('default');
  await expect(page.getByRole('button', { name: 'Advanced', exact: true })).toHaveAttribute('aria-current', 'page');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([{ _magicNguWeights: [1, 3, 0, 0, 0, 0, 0] }]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('local duration and Plan JSON drafts survive every tab without saving or reloading', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await selectPlanTab(page);
  await page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
  const duration = page.locator('.stage-panel input[aria-label="Duration"]').first();
  // An incomplete duration cannot commit on blur; it must remain a component-local draft.
  await duration.fill('25m pending');
  await openPlanDetails(page, 'Advanced plan JSON');
  const raw = page.locator('.raw-grid textarea[aria-label="Advanced Plan JSON"]');
  const original = await raw.inputValue();
  // An incomplete JSON buffer must remain local even after the debounce elapses.
  const draft = JSON.stringify({ ...JSON.parse(original), TimeTriggerSeconds: 8100 }, null, 2).slice(0, -1);
  await raw.fill(draft);
  const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
  const navigation = page.getByRole('navigation', { name: 'Primary', exact: true });
  for (const tab of ['Dashboard', 'Presets', 'Adventure', 'Misc', 'Logs', 'Advanced']) {
    await navigation.getByRole('button', { name: tab, exact: true }).click();
    await expect(page.locator('#plan-editor')).toHaveCount(1);
    await expect(page.locator('#plan-editor')).toBeHidden();
    await expect(raw).toHaveCount(1);
    await expect(raw).toHaveValue(draft);
    if (tab === 'Advanced') await expect(raw).toBeVisible();
    else await expect(raw).toBeHidden();
    await selectPlanTab(page);
    await expect(duration).toHaveValue('25m pending');
    await expect(duration).toBeVisible();
    await expect(raw).toBeHidden();
    await expect(raw).toHaveValue(draft);
  }
  await page.waitForTimeout(700);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length)).toBe(reads);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(3600);
  await openPlanDetails(page, 'Advanced plan JSON');
  await page.locator('.raw-grid .advanced-plan').getByRole('button', { name: 'Discard edits', exact: true }).click();
  await expect(raw).toHaveValue(original);
  await expect(duration).toHaveValue('25m pending');
  await selectPlanTab(page);
  await duration.press('Escape');
  await expect(duration).toHaveValue('15m');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
});

for (const field of ['Duration', 'Fallback time budget']) {
  test(`uncommitted ${field} drafts protect plan switches and closing without blocking other autosaves`, async ({ page }) => {
    await installNativeFixture(page);
    await page.goto('/');
    await selectPlanTab(page);
    if (field === 'Duration') await page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
    else await openPlanDetails(page, 'Schedule & templates');
    const input = page.getByRole('textbox', { name: field, exact: true, includeHidden: true }).first();
    await input.fill('25m pending');
    await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Enable plan', exact: true })).toBeDisabled();
    await expect(page.getByRole('button', { name: /^Auto-rebirth:/ })).toHaveCount(0);
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
    await selectPlanTab(page);
    await page.getByRole('textbox', { name: 'Rebirth after', exact: true }).fill('2h');
    await page.getByRole('textbox', { name: 'Rebirth after', exact: true }).press('Enter');
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(7200);
    await expect(input).toHaveValue('25m pending');
    await input.press('Escape');
    await expect.poll(closeBlocked).toBe(false);
    await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Enable plan', exact: true })).toBeEnabled();
    await openPlanDetails(page, 'Plan library');
    await selected.selectOption('24hr');
    await expect(selected).toHaveValue('24hr');
    if (field === 'Duration') {
      await selectPlanTab(page);
      await page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
    } else await openPlanDetails(page, 'Schedule & templates');
    await expect(input).not.toHaveValue('25m pending');
  });
}

test('plan library selects shortcuts and creates, copies and deletes without activating a plan', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  const management = await openPlanDetails(page, 'Plan library');
  await expect(management.getByText('Changes save automatically.', { exact: false })).toHaveCount(0);
  const selected = management.getByRole('combobox', { name: 'Plan to edit', exact: true });
  const shortcuts = management.locator('[aria-label="Built-in plan shortcuts"]');
  await expect(shortcuts.getByRole('button')).toHaveText(['24h NGU', '15m NUMBER', '30m NUMBER', '1h1m NUMBER']);
  for (const label of ['15m NUMBER', '30m NUMBER', '1h1m NUMBER']) {
    await expect(shortcuts.getByRole('button', { name: label, exact: true })).toBeDisabled();
  }
  await shortcuts.getByRole('button', { name: '24h NGU', exact: true }).click();
  await expect(selected).toHaveValue('24hr');
  await expect(page.locator('#plan-editor')).toBeHidden();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('24hr');
  await openPlanDetails(page, 'Plan library');
  await management.getByText('Delete plan', { exact: true }).click();
  const remove = management.getByRole('button', { name: 'Delete', exact: true });
  await expect(remove).toBeDisabled();
  await expect(management).toContainText('Built-in plans cannot be deleted');
  await selected.selectOption('default');
  await expect(selected).toHaveValue('default');
  await expect(remove).toBeEnabled();
  const source = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
  const name = management.getByRole('textbox', { name: 'New plan name', exact: true });
  await name.fill('browser-created');
  await management.getByRole('button', { name: 'Create', exact: true }).click();
  await expect(selected).toHaveValue('browser-created');
  await expect(selected.locator('option')).toHaveText(['Select plan', 'default', '24hr', 'browser-created']);
  await expect(page.locator('.plan-main')).toContainText('Editing browser-created; default is active.');
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
  await expect(selected).toHaveValue('');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customPlanFile)).toBe('24hr');
  await expect(selected.locator('option')).toHaveText(['Select plan', 'default', '24hr', 'browser-created']);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDeletedPlans)).toEqual(['browser-copy']);
  await expect(page.getByRole('button', { name: 'Advanced', exact: true })).toHaveAttribute('aria-current', 'page');
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
  await page.getByRole('checkbox', { name: 'Manual adventure', exact: true }).check();
  await page.clock.runFor(1);
  await page.getByRole('combobox', { name: 'Manual adventure zone', exact: true }).selectOption('1');
  await page.clock.runFor(1);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._manualAdventureZone)).toBe(1);
  await expect(page.locator('.status-line')).toBeEmpty();
  await expect(page.locator('.app-header')).not.toContainText('Saving changes...');
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
  await selectPlanTab(page);
  await expect(page.getByRole('list', { name: 'Plan stages', exact: true })).toBeVisible();
  const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
  await expect(bar.locator('strong')).toHaveText(['Time Machine', 'Energy NGU']);
  const keys = await bar.locator('.bar-segment').evaluateAll(segments => segments.map(segment => segment.getAttribute('data-stage')));
  await bar.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
  await page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).press('Alt+ArrowRight');
  await expect(bar.locator('strong')).toHaveText(['Energy NGU', 'Time Machine']);
  expect(await bar.locator('.bar-segment').evaluateAll(segments => segments.map(segment => segment.getAttribute('data-stage')))).toEqual([...keys].reverse());
  await expect(page.getByRole('region', { name: 'Stage 2 settings', exact: true }).getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('');
  await expect(bar.getByRole('button', { name: 'Edit stage 2: Time Machine', exact: true })).toHaveAttribute('aria-expanded', 'true');
  await expect(page.getByRole('button', { name: 'Edit stage 2: Time Machine', exact: true })).toBeFocused();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.Stages)).toMatchObject([{ Name: 'NGU sustain' }, { Name: 'Time Machine opener' }]);
  await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('default');
  await expect(page.getByRole('button', { name: 'Save plan', exact: true })).toHaveCount(0);
  await page.reload();
  await selectPlanTab(page);
  await expect(bar.locator('strong')).toHaveText(['Energy NGU', 'Time Machine']);
  await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
  await openPlanDetails(page, 'Schedule & templates');
  await expect(page.getByRole('combobox', { name: 'Repeat from', exact: true })).toHaveValue('0');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('plan-rebirth-editor.png'), fullPage: true });
});

test('primary pages isolate Plan & Rebirth, Misc gear settings and other tools', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  const navigation = page.getByRole('navigation', { name: 'Primary', exact: true });
  await expect(navigation.getByRole('button')).toHaveText(['Dashboard', 'Plan & Rebirth', 'Presets', 'Adventure', 'Misc', 'Logs', 'Advanced']);
  const planEditor = page.getByRole('region', { name: 'Plan editor', exact: true });
  const weights = page.getByRole('article', { name: 'NGU weights', exact: true });
  await expect(page.locator('#plan-editor')).toHaveCount(1);
  await expect(page.locator('#plan-editor')).toBeHidden();
  await expect(page.getByRole('article', { name: 'Plan controls', exact: true })).toBeVisible();
  const allocation = page.getByRole('article', { name: 'Observed NGU allocation', exact: true });
  await expect(allocation).toBeHidden();
  await expect(page.getByRole('article', { name: 'Now', exact: true }).getByRole('region', { name: 'NGU allocation', exact: true })).toHaveCount(0);

  await navigation.getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(allocation).toBeHidden();
  await expect(page.locator('#plan-editor')).toHaveCount(1);
  await expect(planEditor).toBeHidden();
  await expect(page.locator('#ngu-weights')).toHaveCount(1);
  await expect(weights).toBeHidden();
  await expect(page.getByRole('combobox', { name: 'Adventure mode', exact: true })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Manual adventure', exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Manual adventure zone', exact: true })).toHaveCount(0);
  await expect(page.getByRole('checkbox', { name: 'Adventure goal', exact: true })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Auto boost', exact: true })).toBeVisible();
  await expect(page.getByRole('spinbutton', { name: 'Cube Power:Toughness ratio', exact: true })).toBeVisible();
  await expect(page.getByRole('spinbutton', { name: 'Saving time (s)', exact: true })).toHaveCount(0);
  await expect(page.getByRole('checkbox', { name: 'Energy', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Preview gear', exact: true })).toHaveCount(0);

  await navigation.getByRole('button', { name: 'Misc', exact: true }).click();
  const gear = page.getByRole('article', { name: 'Gear settings', exact: true });
  await expect(gear.getByRole('heading', { name: 'Gear settings', exact: true })).toBeVisible();
  await expect(gear).toContainText('Plan: default');
  await expect(gear.getByRole('combobox', { name: 'Active preset', exact: true })).toBeVisible();
  await expect(gear.getByRole('combobox', { name: 'Fight scoring', exact: true })).toBeVisible();
  await expect(gear.locator('details')).toHaveCount(0);
  await expect(allocation).toBeVisible();
  await expect(allocation.getByRole('status')).toHaveText('NGU allocation unavailable.');
  await expect(weights).toBeVisible();
  await expect(page.getByRole('spinbutton', { name: /^(Energy|Magic) .+ weight$/ })).toHaveCount(16);
  await expect(page.getByRole('checkbox', { name: 'Energy', exact: true })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Auto money pit', exact: true })).toBeVisible();
  await expect(page.getByRole('spinbutton', { name: 'Money pit gold threshold', exact: true })).toBeVisible();
  for (const heading of ['Targets', 'Iron Pill', 'Manual inventory', 'Utilities']) await expect(page.getByRole('heading', { name: heading, exact: true })).toHaveCount(0);
  await expect(page.getByRole('article', { name: 'Combat prep planner', exact: true })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Preview gear', exact: true })).toHaveCount(0);
  for (const label of ['Legacy options', 'Legacy global automation', 'Legacy combat automation', 'Allocation logging']) await expect(page.getByText(label, { exact: true })).toHaveCount(0);
  await expect(page.getByRole('checkbox', { name: 'Activate fruits', exact: true })).toHaveCount(0);
  await expect(page.getByRole('combobox', { name: 'Adventure mode', exact: true })).toHaveCount(0);
  await expect(page.getByRole('checkbox', { name: 'Auto boost', exact: true })).toHaveCount(0);
  await expect(page.getByRole('spinbutton', { name: 'Cube Power:Toughness ratio', exact: true })).toHaveCount(0);
  await expect(page.locator('.sticky-card')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Discard changes', exact: true })).toHaveCount(0);
  await openNguWeights(page);
  await expect(weights).toBeVisible();
  await expect(weights.getByRole('spinbutton')).toHaveCount(16);
  await expect(page.locator('#ngu-weights')).toHaveCount(1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);

  await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(weights).toBeHidden();
  await expect(allocation).toBeHidden();
  await expect(page.locator('#plan-editor #ngu-weights')).toHaveCount(0);
  await expect(planEditor).toBeHidden();
  await selectPlanTab(page);
  await expect(planEditor).toBeVisible();
  await expect(page.locator('#plan-editor > *')).toHaveCount(1);
  await expect(page.locator('#plan-editor > .plan-main')).toHaveCount(1);
  await expect(page.locator('#plan-editor .advanced-plan, #plan-editor .plan-management')).toHaveCount(0);
  await expect(page.getByRole('combobox', { name: 'Plan to edit', exact: true })).toHaveCount(0);
  await expect(page.getByRole('list', { name: 'Plan stages', exact: true })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Auto boost', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Preview gear', exact: true })).toHaveCount(0);

  await navigation.getByRole('button', { name: 'Logs', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Log file', exact: true })).toBeVisible();
  await expect(page.locator('.log-output')).toContainText('Fixture safe log');
  const history = page.getByRole('article', { name: 'Rebirth history', exact: true });
  await expect(history.getByRole('heading', { level: 2, name: 'Rebirth history', exact: true })).toBeVisible();
  await expect(history.locator('.card-header span')).toHaveText('default');
  await expect(history.getByText('No rebirths yet.', { exact: true })).toBeVisible();
  await navigation.getByRole('button', { name: 'Advanced', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Advanced Settings JSON', exact: true })).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Advanced Plan JSON', exact: true })).toHaveCount(0);
  await expect(page.locator('.raw-grid .advanced-plan > summary')).toBeVisible();
  await expect(page.locator('.raw-grid .plan-management > summary')).toBeVisible();
  await openPlanDetails(page, 'Plan library');
  await expect(page.getByRole('combobox', { name: 'Plan to edit', exact: true })).toBeVisible();
  await expect(page.getByRole('list', { name: 'Plan stages', exact: true })).toBeHidden();
  await expect(page.getByText('Game data', { exact: true })).toBeVisible();
  await expect(page.getByRole('article', { name: 'Combat prep planner', exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Goal', exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Fight scoring', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Preview gear', exact: true })).toBeEnabled();
  await expect(page.getByRole('heading', { name: 'Utilities', exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Allocation profile', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Use old window', exact: true })).toHaveCount(0);
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
  await expect(page.locator('.status-line')).toBeEmpty();
  await expect(page.locator('.app-header')).not.toContainText('Saving changes...');
  await page.reload();
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(ratio).toHaveValue('4');
});

test('plan preset and numeric edits stay pending until the debounced save completes', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  await selectPlanTab(page);
  const button = page.getByRole('list', { name: 'Plan stages', exact: true }).locator('.stage-select').first();
  await expect(button).toHaveAttribute('aria-expanded', 'false');
  await button.click();
  await expect(button).toHaveAttribute('aria-expanded', 'true');
  const preset = page.getByRole('combobox', { name: 'Focus preset', exact: true }).first();
  await expect(preset).toHaveValue('');
  await preset.selectOption({ label: 'Wandoos' });
  await expect(preset.locator('option:checked')).toHaveText('Wandoos');
  await page.clock.runFor(200);
  await page.getByRole('textbox', { name: 'Rebirth after', exact: true }).fill('2h');
  await page.getByRole('textbox', { name: 'Rebirth after', exact: true }).press('Enter');
  await page.clock.runFor(349);
  await expect(page.locator('.app-header')).not.toContainText('Saving changes...');
  await expect(page.getByRole('button', { name: 'Enable plan', exact: true })).toBeDisabled();
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan)).toMatchObject({ TimeTriggerSeconds: 3600, Stages: [{ Name: 'Time Machine opener', EnergyFocus: 1, MagicFocus: 1, GearGoal: 5 }, { Name: 'NGU sustain' }] });
  await page.clock.runFor(1);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan)).toMatchObject({ TimeTriggerSeconds: 7200, Stages: [{ Name: 'Wandoos', PresetId: 'global_2', EnergyFocus: 1, MagicFocus: 1, GearGoal: 5, EnergyPriorities: [], MagicPriorities: [] }, { Name: 'NGU sustain' }] });
  await expect(preset.locator('option:checked')).toHaveText('Wandoos');
  await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('default');
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
  await selectPlanTab(page);
  await expect(button).toHaveAttribute('aria-expanded', 'false');
  await button.click();
  await expect(preset.locator('option:checked')).toHaveText('Wandoos');
  await expect(page.getByRole('textbox', { name: 'Rebirth after', exact: true })).toHaveValue('2h');
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
  await selectPlanTab(page);
  await page.evaluate(() => { (window as FixtureWindow).__fixtureFailNextPlan = true; });
  const duration = page.getByRole('textbox', { name: 'Rebirth after', exact: true });
  await duration.fill('2h');
  await duration.press('Enter');
  await expect(page.getByRole('alert')).toContainText('Plan save failed.');
  await expect(duration).toHaveValue('2h');
  await expect(page.locator('.app-header')).toContainText('Changes not saved');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(3600);
  await page.getByRole('button', { name: 'Misc', exact: true }).click();
  await selectPlanTab(page);
  await expect(duration).toHaveValue('2h');
  const retry = page.locator('.app-header').getByRole('button', { name: 'Retry saving', exact: true });
  await expect(retry).toBeEnabled();
  await retry.click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(7200);
  await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('default');
  await expect(retry).toHaveCount(0);
  await expect(page.getByRole('alert')).toHaveCount(0);
  const writes = await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites);
  expect(writes).toHaveLength(2);
  expect(writes[0]).toMatchObject({ TimeTriggerSeconds: 7200 });
  expect(writes[1]).toEqual(writes[0]);
  await page.reload();
  await selectPlanTab(page);
  await expect(duration).toHaveValue('2h');
});

for (const target of ['Settings', 'Plan'] as const) {
  test(`Advanced ${target} valid JSON autosaves after debounce without Apply or Save`, async ({ page }) => {
    await installNativeFixture(page);
    await page.clock.install({ time: new Date('2026-10-18T12:00:00Z') });
    await page.clock.pauseAt(new Date('2026-10-18T12:00:01Z'));
    await page.goto('/');
    await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('default');
    if (target === 'Settings') await page.getByRole('button', { name: 'Advanced', exact: true }).click();
    else await openPlanDetails(page, 'Advanced plan JSON');
    const editor = page.getByRole('textbox', { name: `Advanced ${target} JSON`, exact: true });
    const draft = JSON.parse(await editor.inputValue());
    const change = target === 'Settings' ? { _manageEnergy: false } : { TimeTriggerSeconds: 7200 };
    await editor.fill(JSON.stringify({ ...draft, ...change }));
    await page.clock.runFor(349);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    await expect(page.getByRole('button', { name: 'Apply edits', exact: true })).toHaveCount(0);
    await page.clock.runFor(1);
    // JsonEditor validates first; the controller then debounces persistence separately.
    await expect(page.locator('.json-editor').filter({ has: editor }).getByRole('button', { name: 'Discard edits', exact: true })).toBeDisabled();
    await page.clock.runFor(350);
    await expect.poll(() => page.evaluate(target => {
      const saved = (window as FixtureWindow).__fixtureState();
      return target === 'Settings' ? saved.settings : saved.plan;
    }, target)).toMatchObject(change);
    await expect(page.locator('.status-line')).toBeEmpty();
    await expect(page.locator('.app-header')).not.toContainText('Saving changes...');
    await expect(page.getByRole('button', { name: /^Save (settings|plan)/ })).toHaveCount(0);
    await page.reload();
    if (target === 'Settings') await page.getByRole('button', { name: 'Advanced', exact: true }).click();
    else await openPlanDetails(page, 'Advanced plan JSON');
    await expect.poll(async () => JSON.parse(await editor.inputValue())).toMatchObject(change);
  });
}

test('disconnected dashboard cannot enqueue live actions', async ({ page }) => {
  await installNativeFixture(page, false);
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Rebirth', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Enable plan', exact: true })).toBeDisabled();
  const controls = page.getByRole('article', { name: 'Plan controls', exact: true });
  await expect(controls).toHaveAccessibleDescription('Disconnected');
  await expect(controls).not.toHaveClass(/\b(running|stopped)\b/);
  expect(await page.evaluate(() => (window as unknown as { __fixtureCalls: string[] }).__fixtureCalls)).not.toContain('enqueue_command');
});

test('Logs owns the selected plan rebirth history as a read-only scrollable table', async ({ page }) => {
  const entries = [{
    TimestampLocal: '2026-10-14 12:34:56', Reason: 'Time trigger reached after protected stages completed',
    RebirthSeconds: 86461, BossId: 158, NumberBefore: '1.25E+120', NumberAfter: '5E+122', NumberMultiplier: 400,
    FutureDetails: { source: 'Selected plan history', retained: true }
  }];
  await installNativeFixture(page, true, { Name: 'Stored name differs from file', RebirthHistory: entries }, true);
  await page.addInitScript(() => {
    const native = (window as typeof window & { __TAURI_INTERNALS__: { invoke: (command: string, args?: Record<string, unknown>) => Promise<unknown> } }).__TAURI_INTERNALS__;
    const invoke = native.invoke;
    native.invoke = async (command, args) => {
      const result = await invoke(command, args);
      // The running default plan has no history; only the selected 24hr file has these records.
      return command === 'read_plan' && args?.name === 'default'
        ? JSON.stringify({ ...JSON.parse(String(result)), RebirthHistory: [] }) : result;
    };
  });
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.goto('/');
  await page.getByRole('combobox', { name: 'Plan', exact: true }).selectOption('24hr');
  await selectPlanTab(page);
  await expect(page.locator('.plan-main')).toContainText('Editing 24hr; default is active.');
  await expect(page.locator('#plan-editor')).not.toContainText('Rebirth history');
  const before = await page.evaluate(() => (window as FixtureWindow).__fixtureState());
  const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
  const history = page.getByRole('article', { name: 'Rebirth history', exact: true });
  await expect(history).toHaveCount(0);
  const navigation = page.getByRole('navigation', { name: 'Primary', exact: true });
  await navigation.getByRole('button', { name: 'Logs', exact: true }).click();
  await expect(history.getByRole('heading', { level: 2, name: 'Rebirth history', exact: true })).toBeVisible();
  await expect(history.locator('.card-header span')).toHaveText('24hr');
  const table = history.getByRole('table', { name: 'Rebirth history', exact: true });
  await expect(table.locator('caption')).toHaveText('Rebirth history');
  await expect(table.getByRole('columnheader')).toHaveText(['Local timestamp', 'Reason', 'Duration', 'Boss', 'NUMBER before', 'NUMBER after', 'Gain', 'Details']);
  await expect(table.locator('th[scope="col"]')).toHaveCount(8);
  await expect(table.locator('tbody tr')).toHaveCount(1);
  await expect(table.locator('tbody td')).toHaveText([
    entries[0].TimestampLocal, entries[0].Reason, '24h 1m 1s', '158', '1.25E+120', '5E+122', '400x', /Details/
  ]);
  await expect(history.getByText('No rebirths yet.', { exact: true })).toHaveCount(0);
  await expect(history.getByRole('button')).toHaveCount(0);
  await expect(history.locator('input, select, textarea, [contenteditable="true"]')).toHaveCount(0);
  const scroll = history.getByRole('region', { name: 'Rebirth history table', exact: true });
  await expect(scroll).toHaveAttribute('tabindex', '0');
  await scroll.focus();
  await expect(scroll).toBeFocused();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const details = table.locator('tbody details');
  await details.locator('summary').click();
  await expect(details.locator('pre')).toBeVisible();
  expect(JSON.parse(await details.locator('pre').innerText())).toEqual(entries[0]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('24hr');
  await navigation.getByRole('button', { name: 'Logs', exact: true }).click();
  await expect(table.locator('tbody tr')).toHaveCount(1);
  await page.clock.runFor(1000);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length)).toBe(reads);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState())).toEqual(before);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureLibraryWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('Logs owns recent Iron Pill casts while Dashboard keeps live blood status', async ({ page }) => {
  await installNativeFixture(page, true, {}, true);
  await page.goto('/');
  await expect(page.getByText('ETA ~1m 30s', { exact: true })).toBeVisible();
  await expect(page.getByText('Blocked: Custom auto-rebirth is disabled.', { exact: true }).filter({ visible: true })).toBeVisible();
  const history = page.getByRole('article', { name: 'Iron Pill history', exact: true });
  await expect(history).toHaveCount(0);
  await expect(page.getByText('Casting rules', { exact: true })).toHaveCount(0);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Logs', exact: true }).click();
  await expect(history.getByRole('heading', { name: 'Recent casts', exact: true })).toBeVisible();
  await expect(history.getByText('All plans', { exact: true })).toBeVisible();
  await expect(history.getByText('2026-10-14 12:30:00 UTC', { exact: true })).toBeVisible();
  await expect(history).toContainText('Wait: 0h 15m 0s');
  await expect(history).toContainText('Worth: 1,234.5');
  await expect(history.getByRole('button')).toHaveCount(0);
  await expect(page.locator('.log-output')).toContainText('Fixture safe log');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('Iron Pill edits persist while history remains backend-owned', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await page.getByRole('button', { name: 'Edit Iron Pill saving time (s)', exact: true }).click();
  const savingTime = page.getByRole('spinbutton', { name: 'Iron Pill saving time (s)', exact: true });
  await savingTime.fill('1200');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.IronPillSaveSeconds)).toBe(1200);
  await page.getByRole('button', { name: 'Edit Iron Pill minimum worth', exact: true }).click();
  await page.getByRole('spinbutton', { name: 'Iron Pill minimum worth', exact: true }).fill('5000');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan)).toMatchObject({ IronPillSaveSeconds: 1200, IronPillMinWorth: 5000 });
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  await expect(page.locator('.status-line')).toBeEmpty();
  await expect(page.locator('.app-header')).not.toContainText('Saving changes...');
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(savingTime).toHaveValue('1200');
  await expect(page.getByRole('spinbutton', { name: 'Iron Pill minimum worth', exact: true })).toHaveValue('5000');
  await savingTime.press('Escape');
  await page.getByRole('spinbutton', { name: 'Iron Pill minimum worth', exact: true }).press('Escape');
  await expect(page.getByRole('button', { name: 'Edit Iron Pill saving time (s)', exact: true })).toHaveText('20m');
  await expect(page.getByRole('button', { name: 'Edit Iron Pill minimum worth', exact: true })).toHaveText('5000');
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Logs', exact: true }).click();
  await expect(page.getByText('2026-10-14 12:30:00 UTC', { exact: true }).filter({ visible: true })).toBeVisible();
});

test('plan card highlights only a connected running selection', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  const controls = page.getByRole('article', { name: 'Plan controls', exact: true });
  await expect(controls).toHaveClass(/\bstopped\b/);
  await expect(controls.locator('.pill')).toHaveCount(0);
  await expect(controls.getByText('Running', { exact: true })).toHaveCount(0);
  await controls.getByRole('button', { name: 'Enable plan', exact: true }).click();
  await expect(controls).toHaveClass(/\brunning\b/);
  await expect(controls).not.toHaveClass(/\bstopped\b/);
  await expect(controls).toHaveCSS('background-color', 'rgb(24, 42, 32)');
  await expect(controls).toHaveAccessibleDescription('Selected plan is running');
  await controls.getByRole('combobox', { name: 'Plan', exact: true }).selectOption('24hr');
  await expect(controls).toHaveClass(/\bstopped\b/);
  await expect(controls).not.toHaveClass(/\brunning\b/);
  await expect(page.getByRole('article', { name: 'Now', exact: true })).toContainText('Running plan');
  await controls.getByRole('combobox', { name: 'Plan', exact: true }).selectOption('default');
  await expect(controls).toHaveClass(/\brunning\b/);
  await controls.getByRole('button', { name: 'Disable plan', exact: true }).click();
  await expect(controls).toHaveClass(/\bstopped\b/);
  await expect(controls).toHaveAccessibleDescription('Selected plan is stopped');
  await controls.getByRole('button', { name: 'Enable plan', exact: true }).click();
  await expect(controls).toHaveClass(/\brunning\b/);
  await page.evaluate(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { status: 'unloaded' }; });
  await expect(controls).toHaveAccessibleDescription('Disconnected');
  await expect(controls).not.toHaveClass(/\b(running|stopped)\b/);
  await expect(controls.getByRole('button', { name: 'Disable plan', exact: true })).toBeDisabled();
});

test('unrelated running-plan autosave preserves a live rebirth opt-out and the checked saved permission', async ({ page }) => {
  await installNativeFixture(page, true, { AutoRebirthEnabled: true }, true);
  await page.goto('/');
  await selectPlanTab(page);
  const checkbox = page.getByRole('checkbox', { name: 'Allow auto-rebirth', exact: true });
  await expect(checkbox).toBeChecked();
  await page.getByRole('combobox', { name: 'Match conditions', exact: true }).selectOption('1');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TriggerMode)).toBe(1);
  await expect(checkbox).toBeChecked();
  await expect(page.getByRole('button', { name: 'Disable plan', exact: true })).toBeVisible();
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._autoRebirth)).toBe(false);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
});

test('dashboard plan header owns execution while the saved checkbox owns rebirth', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('default');
  await page.getByRole('button', { name: 'Enable plan', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Disable plan', exact: true })).toHaveAttribute('aria-pressed', 'true');
  const controls = page.getByRole('article', { name: 'Plan controls', exact: true });
  await expect(controls.locator('.card-header').getByRole('button', { name: 'Disable plan', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: /^Auto-rebirth:/ })).toHaveCount(0);
  await selectPlanTab(page);
  const checkbox = page.getByRole('checkbox', { name: 'Allow auto-rebirth', exact: true });
  await checkbox.check();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._autoRebirth)).toBe(true);
  await checkbox.uncheck();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._autoRebirth)).toBe(false);
  await expect(page.getByRole('button', { name: 'Disable plan', exact: true })).toBeVisible();
  const calls = await page.evaluate(() => (window as unknown as { __fixtureCalls: string[] }).__fixtureCalls);
  expect(calls).toContain('save_plan');
  expect(calls).toContain('save_settings_patch_json');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toHaveLength(1);
  await page.getByRole('button', { name: 'Disable plan', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Enable plan', exact: true })).toHaveAttribute('aria-pressed', 'false');
  await expect(checkbox).not.toBeChecked();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

for (const autoRebirth of [true, false]) {
  test(`Enable plan honors saved auto-rebirth permission ${autoRebirth}`, async ({ page }) => {
    await installNativeFixture(page, true, { AutoRebirthEnabled: autoRebirth });
    await page.goto('/');
    await page.getByRole('button', { name: 'Enable plan', exact: true }).click();
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings)).toMatchObject({
      _customPlanFile: 'default', _useCustomRebirthPlan: true, _autoRebirth: autoRebirth
    });
    await expect(page.getByRole('button', { name: /^Auto-rebirth:/ })).toHaveCount(0);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureDialogs)).toEqual([]);
  });
}

test('editor rebirth duration inputs autosave and survive refresh without inline rebirth controls', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  const controls = page.getByRole('article', { name: 'Plan controls', exact: true });
  await expect(controls.getByRole('spinbutton')).toHaveCount(0);
  await expect(controls.locator('[aria-label="Rebirth conditions"]')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Edit Rebirth after (s)', exact: true })).toHaveCount(0);
  await controls.getByRole('button', { name: 'Edit plan', exact: true }).click();
  const editor = page.getByRole('region', { name: 'Plan editor', exact: true });
  await expect(editor).toBeVisible();
  const duration = editor.getByRole('textbox', { name: 'Rebirth after', exact: true });
  await duration.fill('2h');
  await duration.press('Enter');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(7200);
  await expect(page.locator('.status-line')).toBeEmpty();
  await expect(page.locator('.app-header')).not.toContainText('Saving changes...');
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(duration).toHaveValue('2h');
  await expect(page.getByRole('button', { name: 'Save plan', exact: true })).toHaveCount(0);
  const calls = await page.evaluate(() => (window as unknown as { __fixtureCalls: string[] }).__fixtureCalls);
  expect(calls).toContain('save_plan');
  expect(calls).not.toContain('plugin:dialog|message');
});

test('rebirth duration text stays local until Enter or blur and saves whole seconds', async ({ page }) => {
  await installNativeFixture(page, true, { NumberClimbEnabled: true, FutureRebirthField: 'keep' });
  await page.clock.install({ time: new Date('2026-10-18T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-18T12:00:01Z'));
  await page.goto('/');
  await selectPlanTab(page);
  const conditions = page.getByRole('article', { name: 'Rebirth conditions', exact: true });
  const before = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
  const after = conditions.getByRole('textbox', { name: 'Rebirth after', exact: true });
  await after.fill('1.1h');
  await page.clock.runFor(500);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  await after.press('Enter');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(3960);
  await after.fill('4.1m');
  await page.clock.runFor(500);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toHaveLength(1);
  await after.press('Tab');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(246);
  const saved = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
  expect(saved).toEqual({ ...before, TimeTriggerSeconds: 246, __externalUiRevision: saved.__externalUiRevision });
  await page.reload();
  await selectPlanTab(page);
  await expect(after).toHaveValue('246s');
  await expect(conditions.getByLabel('Minimum rebirth age', { exact: true })).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('NUMBER gain saves without changing retired fields or enabling auto-rebirth', async ({ page }) => {
  await installNativeFixture(page, true, { NumberTriggerEnabled: true, NumberClimbEnabled: true, FutureRebirthField: 'keep' });
  await page.goto('/');
  await selectPlanTab(page);
  const conditions = page.getByRole('article', { name: 'Rebirth conditions', exact: true });
  const before = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
  await conditions.getByRole('spinbutton', { name: 'NUMBER gain (x)', exact: true }).fill('2.5');
  await expect(conditions.getByRole('spinbutton', { name: 'NUMBER gain after minimum age (x)', exact: true })).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan)).toMatchObject({ NumberMultiplierTarget: 2.5 });
  const saved = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
  expect(saved).toEqual({ ...before, NumberMultiplierTarget: 2.5, __externalUiRevision: saved.__externalUiRevision });
  await page.reload();
  await selectPlanTab(page);
  await expect(conditions.getByRole('spinbutton', { name: 'NUMBER gain (x)', exact: true })).toHaveValue('2.5');
  await expect(conditions.getByRole('checkbox', { name: 'Allow auto-rebirth', exact: true })).not.toBeChecked();
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
});

test('invalid rebirth durations remain unsaved drafts and Escape restores the saved value', async ({ page }) => {
  await installNativeFixture(page, true, { NumberClimbEnabled: true, NumberClimbMinSeconds: 1800 });
  await page.clock.install({ time: new Date('2026-10-18T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-18T12:00:01Z'));
  await page.goto('/');
  await selectPlanTab(page);
  for (const [name, original] of [['Rebirth after', '1h']]) {
    const input = page.getByRole('textbox', { name, exact: true });
    for (const invalid of ['', 'unfinished', '-1s', '1.5s', '2147483648s']) {
      await input.fill(invalid);
      await input.press('Enter');
      await page.clock.runFor(500);
      await expect(page.getByRole('alert').first()).toBeVisible();
      await expect(input).toHaveValue(invalid);
      expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    }
    await input.press('Escape');
    await expect(input).toHaveValue(original);
    await expect(page.getByRole('alert')).toHaveCount(0);
  }
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
});

for (const [initialMode, initialLabel, nextMode, nextLabel] of [[0, 'Any condition', 1, 'All conditions'], [1, 'All conditions', 0, 'Any condition']] as const) {
  test(`rebirth match mode changes from ${initialLabel} to ${nextLabel} without changing conditions`, async ({ page }) => {
    await installNativeFixture(page, true, { TriggerMode: initialMode, NumberTriggerEnabled: true, NumberClimbEnabled: true });
    await page.goto('/');
    await selectPlanTab(page);
    const conditions = page.getByRole('article', { name: 'Rebirth conditions', exact: true });
    const mode = conditions.getByRole('combobox', { name: 'Match conditions', exact: true });
    await expect(mode.locator('option')).toHaveText(['Any condition', 'All conditions']);
    await expect(mode.locator('option:checked')).toHaveText(initialLabel);
    const before = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
    await mode.selectOption({ label: nextLabel });
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TriggerMode)).toBe(nextMode);
    const saved = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
    expect(saved).toEqual({ ...before, TriggerMode: nextMode, __externalUiRevision: saved.__externalUiRevision });
    await page.reload();
    await selectPlanTab(page);
    await expect(mode.locator('option:checked')).toHaveText(nextLabel);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
  });
}

for (const connected of [true, false]) {
  test(`rebirth condition inputs follow their checkboxes and explicit false survives saves (${connected ? 'connected' : 'offline'})`, async ({ page }) => {
    await installNativeFixture(page, connected, { AutoRebirthEnabled: false, TimeTriggerEnabled: false, NumberTriggerEnabled: false, NumberClimbEnabled: true, PrepRequireMaxedFruit: true });
    await page.goto('/');
    await selectPlanTab(page);
    const conditions = page.getByRole('article', { name: 'Rebirth conditions', exact: true });
    const auto = conditions.getByRole('checkbox', { name: 'Allow auto-rebirth', exact: true });
    await expect(auto).not.toBeChecked();
    for (const name of ['Rebirth after', 'NUMBER gain (x)', 'Minimum rebirth age', 'NUMBER gain after minimum age (x)']) await expect(conditions.getByLabel(name, { exact: true })).toHaveCount(0);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    for (const [name, key, fields] of [
      ['After a set time', 'TimeTriggerEnabled', ['Rebirth after']],
      ['When NUMBER grows', 'NumberTriggerEnabled', ['NUMBER gain (x)']]
    ] as const) {
      const checkbox = conditions.getByRole('checkbox', { name, exact: true });
      await checkbox.check();
      for (const field of fields) await expect(conditions.getByLabel(field, { exact: true })).toBeVisible();
      await expect.poll(() => page.evaluate(key => (window as FixtureWindow).__fixtureState().plan[key], key)).toBe(true);
      await checkbox.uncheck();
      for (const field of fields) await expect(conditions.getByLabel(field, { exact: true })).toHaveCount(0);
      await expect.poll(() => page.evaluate(key => (window as FixtureWindow).__fixtureState().plan[key], key)).toBe(false);
    }
    await expect(conditions.getByRole('checkbox', { name: 'When NUMBER grows after a minimum time', exact: true })).toHaveCount(0);
    await expect(conditions.getByRole('checkbox', { name: 'Require maxed prep fruit', exact: true })).toHaveCount(0);
    await expect(conditions.getByRole('status')).toHaveText('Choose at least one condition to rebirth automatically.');
    await auto.check();
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.AutoRebirthEnabled)).toBe(true);
    await auto.uncheck();
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.AutoRebirthEnabled)).toBe(false);
    await page.reload();
    await selectPlanTab(page);
    await expect(auto).not.toBeChecked();
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan)).toMatchObject({ AutoRebirthEnabled: false, TimeTriggerEnabled: false, TimeTriggerSeconds: 3600, NumberTriggerEnabled: false, NumberMultiplierTarget: 100, NumberClimbEnabled: true, NumberClimbMinSeconds: 480, NumberClimbMultiplierTarget: 100, PrepRequireMaxedFruit: true });
    expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
  });
}

test('blood goal edits preserve the independently autosaved Adventure settings', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await page.getByRole('checkbox', { name: 'Auto boost', exact: true }).uncheck();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customAutoBoostEnabled)).toBe(false);
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true }).click();
  await page.getByRole('spinbutton', { name: 'Spaghetti goal (%)', exact: true }).fill('60');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.BloodSpaghettiGoalPercent)).toBe(60);
  await expect(page.getByRole('spinbutton', { name: 'Spaghetti goal (%)', exact: true })).toHaveValue('60');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings)).toMatchObject({ _customAutoBoostEnabled: false, _customBloodSpaghettiGoalPercent: 30, _customBloodCounterfeitGoalPercent: 400 });
  const writes = await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites);
  expect(writes).toEqual([{ _customAutoBoostEnabled: false }]);
  await page.reload();
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('60%');
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(page.getByRole('checkbox', { name: 'Auto boost', exact: true })).not.toBeChecked();
  await expect(page.getByRole('button', { name: /^Save settings/ })).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('preset definition edits save only the global library without equipping', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-18T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-18T12:00:01Z'));
  await page.goto('/');
  await page.getByRole('button', { name: 'Manage presets', exact: true }).click();
  const library = page.getByRole('region', { name: 'Loadout presets', exact: true });
  await library.getByLabel('Browse preset', { exact: true }).selectOption('global_1');
  await expect(library.getByLabel('Duration (seconds)', { exact: true })).toHaveCount(0);
  await library.getByRole('textbox', { name: 'Preset name', exact: true }).fill('Edited Augments + Blood');
  await expect(library.getByRole('button', { name: 'Equip preset', exact: true })).toBeDisabled();
  await page.clock.runFor(349);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureLibraryWrites)).toEqual([]);
  await page.clock.runFor(1);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().library.Presets[1].Name)).toBe('Edited Augments + Blood');
  await expect(library.getByRole('button', { name: 'Discard library edits', exact: true })).toHaveCount(0);
  await expect(library).not.toContainText(/autosave|all changes saved|storage|polling/i);
  await expect(library.getByRole('button', { name: 'Save library', exact: true })).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customAugmentsBloodEstimateSeconds)).toBe(300);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  const calls = await page.evaluate(() => (window as unknown as { __fixtureCalls: string[] }).__fixtureCalls);
  expect(calls).toContain('save_preset_library');
  expect(calls).not.toContain('save_settings_patch_json');
  expect(calls).not.toContain('enqueue_command');
});

test('Activate fruits autosaves only the selected plan and persists across reload', async ({ page }) => {
  await installNativeFixture(page, true, { AutoActivateFruits: false });
  await page.goto('/');
  await selectPlanTab(page);
  const fruits = page.getByRole('checkbox', { name: 'Activate fruits', exact: true });
  await expect(fruits).toBeVisible();
  await expect(fruits).not.toBeChecked();
  await expect(page.getByText('For rebirths of at least 1 hour', { exact: true })).toBeVisible();
  const before = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
  await fruits.check();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.AutoActivateFruits)).toBe(true);
  const saved = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan);
  expect(saved).toEqual({ ...before, AutoActivateFruits: true, __externalUiRevision: saved.__externalUiRevision });
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
  await page.reload();
  await selectPlanTab(page);
  await expect(fruits).toBeChecked();
  await fruits.uncheck();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.AutoActivateFruits)).toBe(false);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('Dashboard tuning remains editable offline without live Blood estimates', async ({ page }) => {
  await installNativeFixture(page, false);
  await page.addInitScript(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { bloodGoals: { available: false } }; });
  await page.goto('/');
  for (const [label, value, key] of [
    ['Spaghetti goal (%)', '45', 'BloodSpaghettiGoalPercent'],
    ['Counterfeit goal (%)', '500', 'BloodCounterfeitGoalPercent'],
    ['Iron Pill saving time (s)', '1200', 'IronPillSaveSeconds'],
    ['Iron Pill minimum worth', '5000', 'IronPillMinWorth']
  ]) {
    await page.getByRole('button', { name: `Edit ${label}`, exact: true }).click();
    const input = page.getByRole('spinbutton', { name: label, exact: true });
    await input.fill(value);
    await expect.poll(() => page.evaluate(key => { const saved = (window as FixtureWindow).__fixtureState(); return (key.startsWith('_') ? saved.settings : saved.plan)[key]; }, key)).toBe(Number(value));
  }
  await page.reload();
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('45%');
  await expect(page.getByRole('button', { name: 'Edit Counterfeit goal (%)', exact: true })).toHaveText('500%');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('cancelling a failed inline save removes only that attempted edit', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await page.getByRole('checkbox', { name: 'Auto boost', exact: true }).uncheck();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings._customAutoBoostEnabled)).toBe(false);
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await page.evaluate(() => { (window as FixtureWindow).__fixtureFailNextPlan = true; });
  await page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true }).click();
  const input = page.getByRole('spinbutton', { name: 'Spaghetti goal (%)', exact: true });
  await input.fill('60');
  await expect(page.getByRole('group', { name: 'Spaghetti goal (%)', exact: true }).getByRole('alert')).toHaveText('Not saved. Your edit is kept. Edit the value to retry.');
  await input.press('Escape');
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('30%');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().settings)).toMatchObject({ _customAutoBoostEnabled: false, _customBloodSpaghettiGoalPercent: 30 });
  await page.reload();
  await page.getByRole('navigation', { name: 'Primary', exact: true }).getByRole('button', { name: 'Adventure', exact: true }).click();
  await expect(page.getByRole('checkbox', { name: 'Auto boost', exact: true })).not.toBeChecked();
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('30%');
});

test('Fight action uses applied state instead of highlighting a requested or failed goal', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await page.evaluate(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { activeFocusGoal: 'Fight', appliedFocusGoal: '', focusStatus: 'failed' }; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  const quick = page.getByRole('button', { name: 'Fight / Boss Push', exact: true });
  await expect(quick).toHaveAttribute('aria-pressed', 'false');
  await page.evaluate(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { activeFocusGoal: 'Fight', appliedFocusGoal: '', focusStatus: 'pending' }; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(quick).toHaveAttribute('aria-pressed', 'false');
  await expect(quick).toHaveAttribute('aria-busy', 'true');
  await expect(quick).toContainText('Applying...');
  await page.evaluate(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { activeFocusGoal: 'Fight', appliedFocusGoal: 'Fight', focusStatus: 'applied' }; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(quick).toHaveAttribute('aria-pressed', 'true');
  await expect(quick).toHaveAttribute('aria-busy', 'false');
});

test('bar keyboard navigation selects one mounted panel without reading or saving a plan', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await selectPlanTab(page);
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
  await selectPlanTab(page);
  await page.getByRole('button', { name: 'Add stage', exact: true }).click();
  const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
  await expect(bar.locator('.bar-segment')).toHaveCount(3);
  await expect(bar.getByRole('button', { name: 'Edit stage 3: Plan active preset', exact: true })).toHaveAttribute('aria-expanded', 'true');
  const key = await bar.locator('.bar-segment').last().getAttribute('data-stage');
  const panel = page.locator(`.stage-panel[data-stage="${key}"]`);
  await expect(panel).toBeVisible();
  await expect(bar.getByRole('button', { name: 'Edit stage 3: Plan active preset', exact: true })).toHaveAttribute('draggable', 'false');
  await panel.getByRole('combobox', { name: 'Focus preset', exact: true }).selectOption('global_6');
  await panel.getByRole('combobox', { name: /^Finish stage by/ }).selectOption('target');
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
  await expect(page.locator('.status-line')).toBeEmpty();
  await expect(page.locator('.app-header')).not.toContainText('Saving changes...');
  await expect(panel.getByRole('textbox', { name: 'AT power target', exact: true })).toHaveValue('6000');
  await expect(panel.getByRole('textbox', { name: 'AT toughness target', exact: true })).toHaveValue('7000');
  await expect(panel).toBeVisible();
  await panel.getByRole('button', { name: 'Remove stage', exact: true }).click();
  await expect(bar.locator('.bar-segment')).toHaveCount(2);
  await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan)).toMatchObject({ StageLoopStartIndex: -1, Stages: [{ Name: 'Time Machine opener' }, { Name: 'NGU sustain' }] });
  await expect(page.locator('.status-line')).toBeEmpty();
  await expect(page.locator('.app-header')).not.toContainText('Saving changes...');
  await page.reload();
  await selectPlanTab(page);
  await expect(bar.locator('strong')).toHaveText(['Time Machine', 'Energy NGU']);
  await openPlanDetails(page, 'Schedule & templates');
  await expect(page.getByRole('combobox', { name: 'Repeat from', exact: true })).toHaveValue('-1');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('new inherited stages require an explicitly selected active preset without blocking legacy edits', async ({ page }) => {
  await installNativeFixture(page, true, { ActivePresetId: '' });
  await page.goto('/');
  await selectPlanTab(page);
  await expect(page.locator('.bar-segment')).toHaveCount(2);
  await page.getByRole('button', { name: 'Add stage', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Choose an active saved global preset');
  await expect(page.locator('.bar-segment')).toHaveCount(2);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  await page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true }).click();
  const stage = page.getByRole('region', { name: 'Stage 1 settings', exact: true });
  await expect(stage.getByRole('combobox', { name: 'Energy focus', exact: true })).toBeEnabled();
  await expect(stage.getByRole('combobox', { name: 'Focus preset', exact: true }).locator('option[value="@active"]')).toHaveJSProperty('disabled', true);
  await page.getByRole('button', { name: 'Misc', exact: true }).click();
  const gear = page.getByRole('article', { name: 'Gear settings', exact: true });
  await gear.getByLabel('Active preset', { exact: true }).selectOption('global_3');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.ActivePresetId)).toBe('global_3');
  await selectPlanTab(page);
  await page.getByRole('button', { name: 'Add stage', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Stage 3 settings', exact: true }).getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('@active');
  await expect.poll(() => page.evaluate(() => ((window as FixtureWindow).__fixtureState().plan.Stages as { PresetId?: string }[])[2]?.PresetId)).toBe('@active');
  await expect(page.getByRole('alert')).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
});

test('Quick receipt distinguishes acquired metadata from saved changes and Clear quick releases it', async ({ page }) => {
  await installNativeFixture(page, true, {}, true);
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Loadout presets', exact: true });
  const usePreset = library.getByRole('button', { name: 'Use Time Machine', exact: true });
  await expect(page.getByRole('button', { name: 'Disable plan', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await usePreset.click();
  const now = page.locator('.current-state');
  const quick = now.locator('.stats > div').filter({ has: page.locator('dt').filter({ hasText: /^Quick$/ }) }).locator('dd');
  await expect(quick).toHaveText('Time Machine');
  await expect(usePreset).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('button', { name: 'Enable plan', exact: true })).toHaveAttribute('aria-pressed', 'false');
  const receipt = library.getByRole('article', { name: 'Last applied preset', exact: true });
  await expect(receipt.locator('strong')).toHaveText('Time Machine');
  await expect(receipt.locator('.pill')).toHaveText('applied');
  await expect(receipt.getByRole('status')).toHaveCount(0);
  await page.evaluate(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { loadoutPreset: { id: 'global_0', active: true, owner: 'dashboard', currentOwner: 'dashboard', status: 'applied', appliedName: 'Acquired Time Machine', appliedAutoUpdate: false, name: 'Saved Time Machine', autoUpdate: true, savedChangesRequireReequip: true } }; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(receipt.locator('strong')).toHaveText('Acquired Time Machine');
  await expect(receipt.locator('.pill')).toHaveText('applied');
  await expect(receipt.getByRole('status')).toHaveText('Preset changed. Re-equip or preview and reapply.');
  await expect(usePreset).toHaveAttribute('aria-pressed', 'true');
  await expect(quick).toContainText('Acquired Time Machine');
  await expect(quick).toContainText('Saved changes require re-equip');
  await expect(quick).not.toContainText('Saved Time Machine');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePresetCommands.length)).toBe(1);
  await page.evaluate(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = {}; });
  await library.getByRole('button', { name: 'Equip preset', exact: true }).click();
  await expect(receipt.locator('strong')).toHaveText('Time Machine');
  await expect(receipt.getByRole('status')).toHaveCount(0);
  await expect(quick).toHaveText('Time Machine');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePresetCommands)).toEqual([
    { command: 'equipGlobalPreset', args: { presetId: 'global_0', libraryRevision: presetLibrary.__externalUiRevision } },
    { command: 'equipGlobalPreset', args: { presetId: 'global_0', libraryRevision: presetLibrary.__externalUiRevision } }
  ]);
  await page.getByRole('button', { name: 'Clear quick', exact: true }).click();
  await expect(quick).toHaveText('None');
  await expect(usePreset).toHaveAttribute('aria-pressed', 'false');
  await expect(receipt.locator('.pill')).toHaveText('historical');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
});

test('Quick preset clicks toggle active manual ownership off and back on', async ({ page }) => {
  await installNativeFixture(page, true, {}, true);
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Loadout presets', exact: true });
  const tile = library.getByRole('button', { name: 'Use Time Machine', exact: true });
  const quick = page.locator('.current-state .stats > div').filter({ has: page.locator('dt').filter({ hasText: /^Quick$/ }) }).locator('dd');
  await tile.click();
  await expect(tile).toHaveAttribute('aria-pressed', 'true');
  await tile.click();
  await expect(tile).toHaveAttribute('aria-pressed', 'false');
  await expect(quick).toHaveText('None');
  await expect(page.getByRole('button', { name: 'Enable plan', exact: true })).toHaveAttribute('aria-pressed', 'false');
  await expect(library.getByLabel('Preset name', { exact: true })).toHaveValue('Time Machine');
  await tile.click();
  await expect(tile).toHaveAttribute('aria-pressed', 'true');
  await expect(quick).toHaveText('Time Machine');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePresetCommands)).toEqual([
    { command: 'equipGlobalPreset', args: { presetId: 'global_0', libraryRevision: presetLibrary.__externalUiRevision } },
    { command: 'stopFocus', args: {} },
    { command: 'equipGlobalPreset', args: { presetId: 'global_0', libraryRevision: presetLibrary.__externalUiRevision } }
  ]);
});

test('Quick preset switches equip the other preset instead of stopping', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  const tiles = page.getByLabel('Global quick presets', { exact: true }).getByRole('button');
  await tiles.nth(0).click();
  await expect(tiles.nth(0)).toHaveAttribute('aria-pressed', 'true');
  await tiles.nth(1).click();
  await expect(tiles.nth(0)).toHaveAttribute('aria-pressed', 'false');
  await expect(tiles.nth(1)).toHaveAttribute('aria-pressed', 'true');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePresetCommands)).toEqual([
    { command: 'equipGlobalPreset', args: { presetId: presetLibrary.Presets[0].Id, libraryRevision: presetLibrary.__externalUiRevision } },
    { command: 'equipGlobalPreset', args: { presetId: presetLibrary.Presets[1].Id, libraryRevision: presetLibrary.__externalUiRevision } }
  ]);
});

test('Quick preset stopping remains available with invalid drafts and only stop capability', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Loadout presets', exact: true });
  const tile = library.getByRole('button', { name: 'Use Time Machine', exact: true });
  await tile.click();
  await expect(tile).toHaveAttribute('aria-pressed', 'true');
  await library.getByLabel('Time Machine weight', { exact: true }).fill('-');
  await expect(library.getByRole('alert')).toBeVisible();
  await page.evaluate(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { supportedCommands: ['equipGlobalPreset'] }; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(tile).toBeDisabled();
  await page.evaluate(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { supportedCommands: ['stopFocus'] }; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(tile).toBeEnabled();
  await expect(library.getByRole('button', { name: 'Equip preset', exact: true })).toBeDisabled();
  await tile.click();
  await expect(tile).toHaveAttribute('aria-pressed', 'false');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePresetCommands.map(row => row.command))).toEqual(['equipGlobalPreset', 'stopFocus']);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureLibraryWrites)).toEqual([]);
});

test('Quick preset stop rejection keeps the active state without replaying', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Loadout presets', exact: true });
  const quick = library.getByLabel('Global quick presets', { exact: true });
  await expect(quick.getByRole('button')).toHaveCount(presetLibrary.Presets.length);
  await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('default');
  await expect(library.getByRole('combobox', { name: 'Inspect preset', exact: true })).toBeEnabled();
  const tile = quick.getByRole('button', { name: 'Use Time Machine', exact: true });
  await tile.click();
  await expect(tile).toHaveAttribute('aria-pressed', 'true');
  await page.evaluate(() => { (window as FixtureWindow).__fixtureStopReject = 'Cannot release Quick ownership.'; });
  await tile.click();
  const error = page.getByRole('alert', { name: 'Injector errors', exact: true }).locator('li > p');
  await expect(error).toHaveText('Command failed: Clear quick: Cannot release Quick ownership.');
  await expect(error).toBeVisible();
  await expect(tile).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(error).toHaveText('Command failed: Clear quick: Cannot release Quick ownership.');
  await expect(tile).toHaveAttribute('aria-pressed', 'true');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePresetCommands.map(row => row.command))).toEqual(['equipGlobalPreset', 'stopFocus']);
});

test('Quick presets stay unhighlighted for plan-owned receipts until clicked for manual takeover', async ({ page }) => {
  await installNativeFixture(page, true, {}, true);
  await page.goto('/');
  await page.evaluate(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = { loadoutPreset: { id: 'global_0', active: true, owner: 'stage:0', currentOwner: 'stage:0', status: 'applied' } }; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  const tile = page.getByRole('button', { name: 'Use Time Machine', exact: true });
  const tiles = page.getByLabel('Global quick presets', { exact: true });
  await expect(tiles.locator('[aria-pressed="true"]')).toHaveCount(0);
  await expect(tiles.locator('button.active')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Disable plan', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.evaluate(() => { (window as FixtureWindow).__fixtureRuntimeOverrides = {}; });
  await tile.click();
  await expect(page.getByRole('button', { name: 'Enable plan', exact: true })).toHaveAttribute('aria-pressed', 'false');
  await expect(tile).toHaveAttribute('aria-pressed', 'true');
  await expect(tile).toHaveClass(/\bactive\b/);
  await expect(tiles.locator('button.active')).toHaveCount(1);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureState().settings)).toMatchObject({ _useCustomRebirthPlan: false, _autoRebirth: false });
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePresetCommands)).toEqual([
    { command: 'equipGlobalPreset', args: { presetId: 'global_0', libraryRevision: presetLibrary.__externalUiRevision } }
  ]);
});

test('duration drafts follow logical stages through selection, save readback, keyboard reorder and tabs', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await selectPlanTab(page);
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
  await page.getByRole('textbox', { name: 'Rebirth after', exact: true }).fill('2h');
  await page.getByRole('textbox', { name: 'Rebirth after', exact: true }).press('Enter');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(7200);
  await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('default');
  await expect(first).toHaveValue('25m pending');
  await expect(second).toHaveValue('35m pending');
  await page.getByRole('button', { name: 'Edit stage 2: Energy NGU', exact: true }).press('Alt+ArrowLeft');
  await expect(bar.locator('strong')).toHaveText(['Energy NGU', 'Time Machine']);
  await expect(page.locator(`.stage-panel[data-stage="${keys[1]}"]`)).toHaveAttribute('aria-label', 'Stage 1 settings');
  await expect(second).toBeVisible();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.Stages)).toMatchObject([{ Name: 'NGU sustain' }, { Name: 'Time Machine opener' }]);
  await expect(page.locator('.status-line')).toBeEmpty();
  await expect(page.locator('.app-header')).not.toContainText('Saving changes...');
  const navigation = page.getByRole('navigation', { name: 'Primary', exact: true });
  await navigation.getByRole('button', { name: 'Misc', exact: true }).click();
  await expect(second).toBeHidden();
  await selectPlanTab(page);
  await expect(second).toHaveValue('35m pending');
  await bar.getByRole('button', { name: 'Edit stage 2: Time Machine', exact: true }).click();
  await expect(first).toHaveValue('25m pending');
  const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(first).toHaveValue('25m pending');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length)).toBe(reads);
  await first.press('Escape');
  await expect(first).toHaveValue('15m');
  await bar.getByRole('button', { name: 'Edit stage 1: Energy NGU', exact: true }).click();
  await expect(second).toHaveValue('35m pending');
  await second.press('Escape');
  await expect(second).toHaveValue('15m');
  const writes = await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites);
  expect(writes).toHaveLength(2);
  expect(writes.every(plan => (plan.Stages as Record<string, unknown>[]).every(stage => stage.Percent === 50 && stage.DurationSeconds === 900))).toBe(true);
});

test('disabled stages show a stable state rather than claiming to sync', async ({ page }) => {
  await installNativeFixture(page);
  const progress = { ...planProgressFixture(), status: 'disabled', activeStageIndex: -1, reason: 'Stage scheduler is disabled.' };
  await page.addInitScript(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides = { planProgress: progress }; }, progress);
  await page.goto('/');
  await selectPlanTab(page);
  await expect(page.locator('.runtime-controls > .live-note')).toHaveText('Stages disabled');
  await expect(page.getByRole('progressbar')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Restart current stage', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true })).toBeEnabled();
});

test('matching live progress overlays the selected bar without selecting or navigating stages', async ({ page }) => {
  await installNativeFixture(page);
  await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
  await page.addInitScript(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides = { planProgress: progress }; }, planProgressFixture());
  await page.goto('/');
  await selectPlanTab(page);
  const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
  const first = bar.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true });
  await expect(page.locator('.stage-bar')).toHaveCount(1);
  await expect(page.getByRole('region', { name: 'Plan progression', exact: true })).toHaveCount(0);
  await expect(page.getByRole('progressbar', { name: 'Stage 1 progress', exact: true })).toHaveAttribute('aria-valuenow', '50');
  await expect(bar.locator('.bar-segment').first()).toHaveAttribute('aria-current', 'step');
  await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
  await first.click();
  const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
  const progress = { ...planProgressFixture('default', 1), stageProgress: .25, stageElapsedSeconds: 225, elapsedSeconds: 1125 };
  await page.evaluate(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides = { planProgress: progress }; }, progress);
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(page.getByRole('progressbar', { name: 'Stage 2 progress', exact: true })).toHaveAttribute('aria-valuenow', '25');
  await expect(bar.locator('.bar-segment').first()).toContainText('Completed');
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
  await expect(page.locator('.runtime-controls > .live-note')).toHaveText('Syncing live progress');
  await duration.press('Escape');
  await expect(page.getByRole('progressbar', { name: 'Stage 2 progress', exact: true })).toHaveAttribute('aria-valuenow', '25');
  await expect(first).toHaveAttribute('aria-expanded', 'true');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length)).toBe(reads);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
  await page.getByRole('textbox', { name: 'Rebirth after', exact: true }).fill('2h');
  await page.getByRole('textbox', { name: 'Rebirth after', exact: true }).press('Enter');
  await expect(page.getByRole('progressbar')).toHaveCount(0);
  await expect(page.locator('.runtime-controls > .live-note')).toHaveText('Syncing live progress');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.TimeTriggerSeconds)).toBe(7200);
  const revision = await page.evaluate(() => (window as FixtureWindow).__fixtureState().plan.__externalUiRevision);
  await page.evaluate(({ progress, revision }) => { (window as FixtureWindow).__fixtureRuntimeOverrides = { planProgress: { ...progress, planRevision: revision } }; }, { progress, revision });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(page.getByRole('progressbar', { name: 'Stage 2 progress', exact: true })).toHaveAttribute('aria-valuenow', '25');
  await expect(first).toHaveAttribute('aria-expanded', 'true');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites.length)).toBe(1);
});

test('runtime overlay rejects mismatched full sequences while the editor bar stays usable', async ({ page }) => {
  await installNativeFixture(page);
  await page.goto('/');
  await selectPlanTab(page);
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
    { label: 'non-current configured duration', data: { ...progress, stages: [progress.stages[0], { ...progress.stages[1], configuredDurationSeconds: 901 }] } },
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
    await expect(page.locator('.runtime-controls > .live-note'), label).toHaveText(['different version', 'different scheduler'].includes(label) ? 'Waiting for live progress' : 'Syncing live progress');
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

test('scheduled preset blockers follow matching telemetry without issuing actions', async ({ page }) => {
  const source = planProgressFixture();
  const progress = { ...source, activePresetId: 'global_3', stages: source.stages.map(row => ({ ...row, presetId: '@active', resolvedPresetId: 'global_3', presetName: 'Captured NGU policy', presetStatus: 'failed', presetReason: 'Use Preview + Apply first.' })) };
  const Stages = source.stages.map(row => ({ Name: row.name, PresetId: '@active', Percent: row.flexWeight, DurationSeconds: row.configuredDurationSeconds, CompletionMode: row.completionMode }));
  await installManualStageFixture(page, { planProgress: progress }, true, { Stages });
  await page.goto('/');
  await selectPlanTab(page);
  const status = page.locator('.live-position').getByRole('status');
  const meter = page.getByRole('progressbar', { name: 'Stage 1 progress', exact: true });
  await expect(status).toHaveText('Preset failed: Use Preview + Apply first.');
  await expect(meter).toHaveAttribute('aria-valuetext', 'Preset failed: Use Preview + Apply first.');
  for (const patch of [{ planName: 'other' }, { planRevision: 'b'.repeat(64) }]) {
    await page.evaluate(planProgress => { (window as FixtureWindow).__fixtureRuntimeOverrides!.planProgress = planProgress; }, { ...progress, ...patch });
    await page.getByRole('button', { name: 'Refresh', exact: true }).click();
    await expect(status).toHaveCount(0);
    await expect(page.locator('#plan-editor')).not.toContainText('Use Preview + Apply first.');
  }
  await page.evaluate(planProgress => { (window as FixtureWindow).__fixtureRuntimeOverrides!.planProgress = planProgress; }, { ...progress, stages: progress.stages.map(row => ({ ...row, presetStatus: 'applied', presetReason: '' })) });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(meter).toHaveAttribute('aria-valuetext', 'running: Captured NGU policy; 50% complete');
  await expect(status).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
});

test('weighted runtime matching uses configured and resolved bindings without treating retained tuples as live allocation', async ({ page }) => {
  const source = planProgressFixture();
  const progress = { ...source, activePresetId: 'global_3', stages: source.stages.map((row, index) => ({ ...row, presetId: index === 0 ? 'global_3' : '@active', resolvedPresetId: 'global_3', presetName: 'Captured NGU policy' })) };
  const stages = [
    { Name: 'Time Machine opener', PresetId: 'global_3', Percent: 50, DurationSeconds: 900, CompletionMode: 0, EnergyFocus: 1, MagicFocus: 1, GearGoal: 5, EnergyPriorities: [], MagicPriorities: [] },
    { Name: 'Old inherited descriptor', PresetId: '@active', Percent: 50, DurationSeconds: 900, CompletionMode: 0 }
  ];
  await installManualStageFixture(page, { planProgress: progress }, true, { Stages: stages });
  await page.goto('/');
  await selectPlanTab(page);
  const meter = page.getByRole('progressbar', { name: 'Stage 1 progress', exact: true });
  await expect(meter).toHaveAttribute('aria-valuetext', 'running: Captured NGU policy; 50% complete');
  await expect(page.locator('.live-position')).toContainText('Current: 1. Captured NGU policy');
  await expect(page.locator('.live-position')).not.toContainText('Time Machine');
  await expect(page.getByRole('button', { name: 'Restart current stage', exact: true })).toBeEnabled();
  await expect(page.getByRole('button', { name: 'Edit stage 2: Plan active preset', exact: true })).toBeEnabled();
  const mismatches = [
    { ...progress, activePresetId: 'global_4' },
    { ...progress, stages: [{ ...progress.stages[0], presetId: '@active' }, progress.stages[1]] },
    { ...progress, stages: [progress.stages[0], { ...progress.stages[1], resolvedPresetId: 'global_4' }] },
    { ...progress, stages: [progress.stages[0], { ...progress.stages[1], presetId: undefined, resolvedPresetId: undefined, presetName: undefined }] }
  ];
  for (const mismatch of mismatches) {
    await page.evaluate(planProgress => { (window as FixtureWindow).__fixtureRuntimeOverrides = { ...((window as FixtureWindow).__fixtureRuntimeOverrides ?? {}), planProgress }; }, mismatch);
    await page.getByRole('button', { name: 'Refresh', exact: true }).click();
    await expect(page.getByRole('progressbar')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Restart current stage', exact: true })).toBeDisabled();
    await expect(page.locator('.runtime-controls > .live-note')).toHaveText('Syncing live progress');
  }
  await page.evaluate(planProgress => { (window as FixtureWindow).__fixtureRuntimeOverrides = { ...((window as FixtureWindow).__fixtureRuntimeOverrides ?? {}), planProgress }; }, progress);
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(meter).toHaveAttribute('aria-valuetext', 'running: Captured NGU policy; 50% complete');
  expect(await page.evaluate(() => (window as FixtureWindow).__fixturePlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__fixtureCalls)).not.toContain('enqueue_command');
});

for (const connected of [true, false]) {
  test(`${connected ? 'another runtime plan' : 'offline runtime data'} never replaces or disables the selected editor bar`, async ({ page }) => {
    await installNativeFixture(page, connected);
    await page.addInitScript(progress => { (window as FixtureWindow).__fixtureRuntimeOverrides = { planProgress: progress }; }, planProgressFixture());
    await page.goto('/');
    await page.getByRole('combobox', { name: 'Plan', exact: true }).selectOption('24hr');
    await selectPlanTab(page);
    await expect(page.locator('.plan-main')).toContainText('Editing 24hr; default is active.');
    const reads = await page.evaluate(() => (window as FixtureWindow).__fixtureCalls.filter(call => call === 'read_plan').length);
    const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
    await expect(bar.locator('strong')).toHaveText(['Time Machine', 'Energy NGU']);
    await expect(page.getByRole('progressbar')).toHaveCount(0);
    await expect(page.locator('.runtime-controls > .live-note')).toHaveText(connected ? 'Editing another plan' : 'Offline');
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
