import { expect, test, type Page } from '@playwright/test';
import { policies, stagePresets } from './fixtures/activity-policy';
import { presetLibrary } from './fixtures/global-presets';

const source = {
  Name: 'shared-policy', Version: 18, __externalUiRevision: 'a'.repeat(64),
  StageSchedulerEnabled: true, StageDurationSeconds: 1800, StageLoopStartIndex: -1,
  RebirthHistory: [{ Reason: 'User history', EnergyPriorities: ['TM', 'NGU-8'] }],
  Stages: [
    { Name: 'User order', Percent: 100, DurationSeconds: 300, CompletionMode: 0, EnergyFocus: 1, MagicFocus: 1, GearGoal: 5, EnergyPriorities: ['NGU-8', 'TM', 'TM'], MagicPriorities: ['RIT-7', 'WAN'], FightSchedule: null, Future: 'keep' },
    { Name: 'User allocation', Percent: 100, DurationSeconds: 300, CompletionMode: 0, EnergyFocus: 3, MagicFocus: 6, GearGoal: 9, EnergyPriorities: ['AT-4', 'WAN'], MagicPriorities: ['NGU-6', 'TM'], FightSchedule: null },
    { Name: 'Implicit defaults', Percent: 100, DurationSeconds: 300, CompletionMode: 0, EnergyFocus: 7, MagicFocus: 3, GearGoal: 11, EnergyPriorities: [], MagicPriorities: [], FightSchedule: null }
  ]
};

type FixtureWindow = Window & {
  __policyPlan: typeof source;
  __policyWrites: (typeof source)[];
  __policyCalls: string[];
};

async function mount(page: Page, connected = true): Promise<void> {
  await page.clock.install({ time: new Date('2026-10-17T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-17T12:00:01Z'));
  await page.addInitScript(({ source, connected, presetLibrary }) => {
    const fixture = window as FixtureWindow;
    fixture.__policyPlan = structuredClone(source);
    fixture.__policyWrites = [];
    fixture.__policyCalls = [];
    let revision = 10;
    const settings = { _customPlanFile: source.Name, _useCustomRebirthPlan: false, _autoRebirth: false };
    Object.assign(window, { __TAURI_INTERNALS__: {
      async invoke(command: string, args: Record<string, unknown> = {}) {
        fixture.__policyCalls.push(command);
        const casPath = '/e2e/fixtures/settings-cas.ts';
        const cas = await import(casPath);
        switch (command) {
          case 'get_ngui_state': return {
            runtime_dir: 'C:\\Fixture\\SharedPolicy', marker_enabled: true, connected,
            protocol: { version: 3, globalPresetsVersion: 2 }, ...await cas.settingsSnapshot(settings), profiles: [], plans: [source.Name], logs: [], command_results: [],
            runtime: {
              externalUiEnabled: connected, status: connected ? 'running' : 'unloaded', protocolVersion: 3, settingsCasVersion: 1,
              startedAt: '2026-10-17T10:00:00Z', updatedAt: new Date().toISOString(), planVersion: 18, stageTimingVersion: 1, fightScheduleVersion: 1,
              supportedCommands: ['readPlan', 'savePlan'],
              settings: { customPlanFile: source.Name, useCustomRebirthPlan: false, autoRebirth: false }
            }
          };
          case 'read_plan': return JSON.stringify(fixture.__policyPlan);
          case 'read_preset_library': return JSON.stringify(presetLibrary);
          case 'save_plan': {
            const next = JSON.parse(String(args.planJson));
            fixture.__policyWrites.push(structuredClone(next));
            fixture.__policyPlan = { ...next, __externalUiRevision: (++revision).toString(16).padStart(64, '0') };
            return { success: true, mode: connected ? 'connected' : 'offline' };
          }
          default: throw new Error(`Unexpected native invocation: ${command}`);
        }
      }
    } });
    localStorage.clear();
  }, { source, connected, presetLibrary });
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Plan & Rebirth', exact: true }).click();
  await expect(page.locator('.bar-segment')).toHaveCount(source.Stages.length);
  await expect(page.getByRole('alert')).toHaveCount(0);
}

async function selectStage(page: Page, index: number) {
  await page.locator('.bar-segment').nth(index).getByRole('button', { name: /^Edit stage/ }).click();
  return page.getByRole('region', { name: `Stage ${index + 1} settings`, exact: true });
}

test('Vite-served policy resolves all shared sets without mutating explicit priorities', async ({ page }) => {
  await mount(page);
  const result = await page.evaluate(async () => {
    const policyPath = '/src/lib/stage-presets.ts';
    const modelPath = '/src/lib/model.ts';
    const policy = await import(policyPath);
    const model = await import(modelPath);
    const saved = Object.freeze({
      EnergyFocus: 1, MagicFocus: 1, GearGoal: 5,
      EnergyPriorities: Object.freeze(['NGU-8', 'TM', 'TM']), MagicPriorities: Object.freeze(['RIT-7', 'WAN'])
    });
    model.validatePriorities(saved.EnergyPriorities, true, 'Energy');
    model.validatePriorities(saved.MagicPriorities, false, 'Magic');
    const first = policy.applyStagePreset(saved, 0);
    first.EnergyPriorities.reverse();
    first.MagicPriorities.push('TM');
    let wrongLaneRejected = false;
    try { model.validatePriorities(['NGU-7'], false, 'Magic'); } catch { wrongLaneRejected = true; }
    return { presets: policy.stagePresets, saved, index: policy.stagePresetIndex(saved), wrongLaneRejected };
  });
  expect(result.presets).toEqual(stagePresets);
  expect(result.saved).toEqual({ EnergyFocus: 1, MagicFocus: 1, GearGoal: 5, EnergyPriorities: ['NGU-8', 'TM', 'TM'], MagicPriorities: ['RIT-7', 'WAN'] });
  expect(result.index).toBe(0);
  expect(result.wrongLaneRejected).toBe(true);
  const sets: Record<string, string[]> = policies.prioritySets;
  expect(Object.keys(sets)).toHaveLength(18);
  expect(new Set(Object.values(sets).map(sequence => JSON.stringify(sequence))).size).toBe(18);
  const references = [
    ...policies.defaults.flatMap(row => [[row.energy, true], [row.magic, false]] as const),
    ...policies.stagePresets.flatMap(row => [[row.EnergyPriorities, true], [row.MagicPriorities, false]] as const),
    [policies.preparation.energy, true], [policies.preparation.magic, false]
  ] as const;
  const resolved = await page.evaluate(async references => {
    const path = '/src/lib/stage-presets.ts';
    const { resolvePrioritySet } = await import(path);
    return references.map(([key, energy]) => resolvePrioritySet(key, energy));
  }, references);
  expect(resolved).toEqual(references.map(([key]) => sets[key]));
  expect(new Set(references.map(([key]) => key)).size).toBe(18);
});

for (const connected of [true, false]) {
  test(`loading shared policies never rewrites saved explicit or implicit orders (${connected ? 'connected' : 'offline'})`, async ({ page }) => {
    await mount(page, connected);
    const energyLabels = [['NGU: PP', 'Time Machine', 'Time Machine'], ['Advanced Training: Wandoos magic', 'Wandoos'], []];
    const magicLabels = [['Blood Magic: Turn Yourself Inside Out', 'Wandoos'], ['NGU: Adventure Beta', 'Time Machine'], []];
    for (const index of [0, 1, 2]) {
      const panel = await selectStage(page, index);
      await expect(panel.getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('');
      await expect(panel.getByRole('group', { name: 'Energy priorities', exact: true }).locator('.priority-name')).toHaveText(energyLabels[index]);
      await expect(panel.getByRole('group', { name: 'Magic priorities', exact: true }).locator('.priority-name')).toHaveText(magicLabels[index]);
      if (index === 2) {
        for (const label of ['Energy priorities', 'Magic priorities']) await expect(panel.getByRole('group', { name: label, exact: true }).getByText('Use focus defaults', { exact: true })).toBeVisible();
      }
    }
    await page.getByRole('button', { name: 'Refresh', exact: true }).click();
    await page.clock.runFor(2000);
    expect(await page.evaluate(() => (window as FixtureWindow).__policyWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__policyPlan)).toEqual(source);
    await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Advanced', exact: true }).click();
    await page.getByText('Advanced plan JSON', { exact: true }).click();
    expect(JSON.parse(await page.getByRole('textbox', { name: 'Advanced Plan JSON', exact: true }).inputValue())).toEqual(source);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

test('explicit global selection preserves legacy orders and timing rather than rewriting tuples', async ({ page }) => {
  await mount(page);
  const panel = await selectStage(page, 0);
  const duration = panel.getByRole('textbox', { name: 'Duration', exact: true });
  await duration.fill('6m');
  await duration.press('Enter');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__policyWrites.length)).toBe(1);
  const afterTiming = await page.evaluate(() => (window as FixtureWindow).__policyWrites[0]);
  expect(afterTiming.Stages[0]).toEqual({ ...source.Stages[0], DurationSeconds: 360, DurationUnit: 'min' });
  expect(afterTiming.Stages.slice(1)).toEqual(source.Stages.slice(1));
  expect(afterTiming.RebirthHistory).toEqual(source.RebirthHistory);
  await panel.getByRole('combobox', { name: 'Focus preset', exact: true }).selectOption({ label: 'AT Wandoos' });
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__policyWrites.length)).toBe(2);
  const afterPreset = await page.evaluate(() => (window as FixtureWindow).__policyWrites[1]);
  expect(afterPreset.Stages[0]).toEqual({ ...afterTiming.Stages[0], PresetId: 'global_7', Name: 'AT Wandoos' });
  expect(afterPreset.Stages.slice(1)).toEqual(source.Stages.slice(1));
  expect(afterPreset.RebirthHistory).toEqual(source.RebirthHistory);
  expect(await page.evaluate(() => (window as FixtureWindow).__policyCalls.filter(call => !['read_plan', 'save_plan', 'get_ngui_state', 'read_preset_library'].includes(call)))).toEqual([]);
});

test('shared policy loading retains incomplete JSON drafts and valid priority edits autosave unchanged', async ({ page }) => {
  await mount(page);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Advanced', exact: true }).click();
  await page.getByText('Advanced plan JSON', { exact: true }).click();
  const json = page.locator('.raw-grid textarea[aria-label="Advanced Plan JSON"]');
  const draft = structuredClone(source);
  draft.Stages[0].EnergyPriorities = ['AT-4', 'NGU-8', 'TM'];
  const text = JSON.stringify(draft, null, 2).slice(0, -1);
  await json.fill(text);
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await page.clock.runFor(2000);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Plan & Rebirth', exact: true }).click();
  await expect(json).toBeHidden();
  await expect(json).toHaveValue(text);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Advanced', exact: true }).click();
  await expect(json).toBeVisible();
  await expect(json).toHaveValue(text);
  expect(await page.evaluate(() => (window as FixtureWindow).__policyWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__policyPlan)).toEqual(source);
  const discard = page.locator('.raw-grid .advanced-plan').getByRole('button', { name: 'Discard edits', exact: true });
  await discard.click();
  expect(JSON.parse(await json.inputValue())).toEqual(source);
  await page.clock.runFor(500);
  expect(await page.evaluate(() => (window as FixtureWindow).__policyWrites)).toEqual([]);
  await json.fill(JSON.stringify(draft, null, 2));
  await page.clock.runFor(349);
  expect(await page.evaluate(() => (window as FixtureWindow).__policyWrites)).toEqual([]);
  await page.clock.runFor(1);
  await expect(discard).toBeDisabled();
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__policyWrites)).toEqual([draft]);
  await expect.poll(async () => JSON.parse(await json.inputValue())).toEqual({ ...draft, __externalUiRevision: 'b'.padStart(64, '0') });
});
