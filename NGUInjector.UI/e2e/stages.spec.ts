import { expect, test, type Page } from '@playwright/test';
import { globalPresets, presetLibrary } from './fixtures/global-presets';
import type { JsonObject } from '../src/lib/api';
import { setPriorities } from './fixtures/priority-picker';

const source = {
  Name: 'stages', Version: 18, __externalUiRevision: 'a'.repeat(64),
  ActivePresetId: 'global_3',
  StageDurationSeconds: 1200, StageLoopStartIndex: 1,
  Stages: [
    { Name: 'Opener', Percent: .2, DurationSeconds: 240, EnergyFocus: 1, MagicFocus: 1, GearGoal: 5, EnergyPriorities: ['TM'], MagicPriorities: ['RIT'], FightSchedule: null as JsonObject | null, Future: 'A' },
    { Name: 'Training', Percent: .3, EnergyFocus: 7, MagicFocus: 4, GearGoal: 11, CompletionMode: 1, TargetAdvancedTrainingPower: '9223372036854775806', TargetAdvancedTrainingToughness: '6000', FightSchedule: null as JsonObject | null, Future: 'B' },
    { Name: 'Sustain', Percent: .8, DurationSeconds: 960, EnergyFocus: 5, MagicFocus: 6, GearGoal: 2, FightSchedule: null as JsonObject | null, Future: 'C' }
  ], Future: 'keep'
};
const diagnostics = new WeakMap<Page, string[]>();
type SavedPlan = Omit<typeof source, 'Stages'> & { TimeTriggerEnabled?: boolean; TimeTriggerSeconds?: number; Stages: (Omit<(typeof source.Stages)[number], 'FightSchedule'> & { PresetId?: string; ExecutionMode?: number; AdventureDynamicWeight?: number; DurationSeconds?: number; FightOnEntry?: boolean; FightSchedule?: JsonObject | null; BlackBarLookaheadSeconds?: number })[] };
type TimingFixtureWindow = Window & { __stageLive: (props: Record<string, unknown>) => void; __stageActions: unknown[]; __stageNativeCalls: string[]; __stageEdits: number; __stageDraftDirty: boolean; __stageDestroy: () => void };
test.afterEach(async ({ page }) => { expect(diagnostics.get(page) ?? []).toEqual([]); });

async function mount(page: Page, plan: SavedPlan = source, cloneEdits = false): Promise<void> {
  const errors: string[] = [];
  diagnostics.set(page, errors);
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    // Chromium can deny Vite's dev-only loopback socket in this isolated fixture.
    if (/^WebSocket connection to 'ws:\/\/127\.0\.0\.1:\d+\//.test(message.text()) || message.text().startsWith('[vite] failed to connect to websocket.')) return;
    if (message.type() === 'warning' || message.type() === 'error') errors.push(message.text());
  });
  await page.route(/http:\/\/127\.0\.0\.1:\d+\/$/, route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><head><link rel="stylesheet" href="/src/styles.css"></head><body><main id="fixture" style="padding:8px"></main></body></html>' }));
  await page.goto('/');
  const main = await (await page.request.get('/src/main.ts')).text();
  const runtimePath = main.match(/import\s*\{\s*mount\s*\}\s*from\s*["']([^"']+)["']/)?.[1];
  if (!runtimePath) throw new Error('Missing Svelte runtime');
  await page.evaluate(async ({ source, runtimePath, cloneEdits, presetLibrary }) => {
    const legacyPath = runtimePath.replace(/svelte\.js(?=\?|$)/, 'svelte_legacy.js');
    const { createClassComponent } = await import(legacyPath);
    const componentPath = '/src/lib/PlanEditor.svelte';
    const { default: Component } = await import(componentPath);
    const apiPath = '/src/lib/api.ts';
    const { parseJson, stringifyJson, normalizeFightSchedules } = await import(apiPath);
    source = stringifyJson(normalizeFightSchedules(parseJson(source)));
    const state = window as unknown as { __stagePlan: string; __stageEdits: number; __stageNamespace: (name: string) => void; __stageName: (name: string) => void; __replaceStages: () => void; __stageReadback: (mode?: string) => void };
    state.__stagePlan = source;
    state.__stageEdits = 0;
    const gearPath = '/src/lib/PlanGear.svelte';
    const { default: Gear } = await import(gearPath);
    let gear: { $set: (props: Record<string, unknown>) => void };
    const component = createClassComponent({ component: Component, target: document.getElementById('fixture'), props: {
      plan: parseJson(source), presetLibrary, name: 'stages', namespace: 'folder-a', dirty: false,
      ondirty: (dirty: boolean) => { (window as unknown as TimingFixtureWindow).__stageDraftDirty = dirty; },
      onchange: (next: unknown) => {
        state.__stagePlan = stringifyJson(next);
        state.__stageEdits++;
        component.$set({ plan: cloneEdits ? parseJson(state.__stagePlan) : next, dirty: true });
        gear.$set({ plan: cloneEdits ? parseJson(state.__stagePlan) : next });
      }
    } });
    const timingState = window as unknown as TimingFixtureWindow;
    timingState.__stageActions = [];
    timingState.__stageLive = props => component.$set({ ...props, onstageaction: async (...args: unknown[]) => { timingState.__stageActions.push(args); } });
    gear = createClassComponent({ component: Gear, target: document.getElementById('fixture'), props: {
      plan: parseJson(source), presetLibrary,
      onchange: (next: unknown) => {
        state.__stagePlan = stringifyJson(next);
        state.__stageEdits++;
        const plan = cloneEdits ? parseJson(state.__stagePlan) : next;
        component.$set({ plan, dirty: true });
        gear.$set({ plan });
      }
    } });
    state.__stageNamespace = (namespace: string) => component.$set({ namespace });
    timingState.__stageDestroy = () => { component.$destroy(); gear.$destroy(); };
    state.__stageName = (name: string) => component.$set({ name });
    state.__replaceStages = () => component.$set({ plan: parseJson(source) });
    state.__stageReadback = (mode?: string) => {
      const next = parseJson(state.__stagePlan);
      next.__externalUiRevision = 'b'.repeat(64);
      if (mode !== 'clone') next.Stages = next.Stages.map((row: Record<string, unknown>) => ({ CompletionMode: 0, AdventureDynamicWeight: 0, DurationSeconds: 0, DurationUnit: '%', FightSchedule: null, TargetAdvancedTrainingPower: '0', TargetAdvancedTrainingToughness: '0', EnergyPriorities: [], MagicPriorities: [], BlackBarLookaheadSeconds: 5, ...row }));
      if (mode === 'targets') for (const row of next.Stages) {
        for (const key of ['TargetAdvancedTrainingPower', 'TargetAdvancedTrainingToughness']) row[key] = BigInt(row[key]).toString();
      }
      if (mode === 'reorder') next.Stages.reverse();
      if (mode === 'ambiguous') next.Stages[1] = { ...next.Stages[0] };
      if (mode === 'remove') next.Stages.splice(0, 1);
      if (mode === 'name') next.Name = 'other-plan';
      state.__stagePlan = stringifyJson(next);
      component.$set({ plan: next, dirty: false });
      gear.$set({ plan: next });
    };
  }, { source: JSON.stringify(plan).replace('"Future":"keep"', '"Future":9007199254740993'), runtimePath, cloneEdits, presetLibrary });
  await expect(page.getByRole('list', { name: 'Plan stages' }).locator('.bar-segment')).toHaveCount(plan.Stages.length);
}

async function saved(page: Page): Promise<SavedPlan> {
  return page.evaluate(() => JSON.parse((window as unknown as { __stagePlan: string }).__stagePlan));
}

async function readback(page: Page, mode?: string): Promise<void> {
  await page.evaluate(mode => (window as unknown as { __stageReadback: (mode?: string) => void }).__stageReadback(mode), mode);
}

async function pauseDurationClock(page: Page): Promise<void> {
  await page.clock.install({ time: new Date('2026-10-19T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-19T12:00:01Z'));
}

async function selectStage(page: Page, index: number) {
  const segment = page.locator('.bar-segment').nth(index);
  const button = segment.getByRole('button', { name: /^Edit stage/ });
  if (await button.getAttribute('aria-expanded') !== 'true') await button.click();
  const panel = page.getByRole('region', { name: `Stage ${index + 1} settings`, exact: true });
  await expect(panel).toBeVisible();
  await expect(page.locator('.stage-panel:visible')).toHaveCount(1);
  return panel;
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

async function openSchedule(page: Page): Promise<void> {
  const details = page.locator('details.schedule-settings');
  if (await details.getAttribute('open') === null) await details.getByText('Schedule & templates', { exact: true }).click();
}

async function expectStageEstimates(page: Page, durations: string[], shares: string[] = []): Promise<void> {
  await expect(page.locator('dl[aria-label="Schedule budget"], .schedule-budget')).toHaveCount(0);
  const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
  await expect(bar.locator('.segment-duration')).toHaveText(durations);
  await expect(bar.locator('.segment-share')).toHaveText(shares);
  for (const label of await bar.locator('.segment-duration, .segment-share').all()) await expect(label).toBeVisible();
}

async function expectDurations(page: Page, values: string[]): Promise<void> {
  const selected = await page.locator('.stage-select[aria-expanded="true"]').evaluateAll(buttons => buttons[0]?.getAttribute('data-select') ?? null);
  const panels = page.locator('.stage-panel');
  await expect(panels.locator('input[aria-label="Duration"]')).toHaveCount(values.length);
  let valueIndex = 0;
  for (let index = 0; index < await panels.count(); index++) {
    if (await panels.nth(index).locator('input[aria-label="Duration"]').count()) {
      const panel = await selectStage(page, index);
      await expect(panel.getByRole('textbox', { name: 'Duration', exact: true })).toHaveValue(values[valueIndex++]);
    }
  }
  if (selected !== null) {
    const button = page.locator(`.stage-select[data-select="${selected}"]`);
    if (await button.getAttribute('aria-expanded') !== 'true') await button.click();
  } else await page.getByRole('button', { name: 'Close stage settings', exact: true }).click();
}

async function chooseTemplate(page: Page, label: string, answer: 'Yes' | 'No'): Promise<void> {
  await page.evaluate(answer => {
    const state = window as unknown as { __TAURI_INTERNALS__: unknown; __stageDialogs: unknown[] };
    state.__stageDialogs = [];
    state.__TAURI_INTERNALS__ = { invoke: async (command: string, args: unknown) => {
      state.__stageDialogs.push({ command, args });
      if (command !== 'plugin:dialog|message') throw new Error(`Unexpected native call: ${command}`);
      return answer;
    } };
  }, answer);
  await openSchedule(page);
  await page.getByRole('button', { name: label, exact: true }).click();
  expect(await page.evaluate(() => (window as unknown as { __stageDialogs: unknown[] }).__stageDialogs)).toEqual([{
    command: 'plugin:dialog|message',
    args: { message: 'Replace all stages? Current stage settings will be replaced and saved automatically.', title: 'Replace stages', kind: 'warning', buttons: 'YesNo' }
  }]);
}

test('default timing converts only the selected stage without growing the budget or offering a mode switch', async ({ page }) => {
  await mount(page, { ...source, Stages: source.Stages.map(row => ({ ...row, DurationSeconds: 0 })) });
  await expect(page.getByRole('checkbox', { name: 'Protect fixed stages', exact: true })).toHaveCount(0);
  const before = await saved(page);
  await expectStageEstimates(page, ['4m flexible', 'AT targets', '16m flexible'], ['20% of remaining', '80% of remaining']);
  await expect(page.getByRole('combobox', { name: 'Stage timing', exact: true })).toHaveCount(0);
  const panel = await selectStage(page, 0);
  const completion = panel.getByRole('combobox', { name: /^Finish stage by/ });
  await expect(completion.locator('option')).toHaveText(['Time', '% of remaining', 'Advanced training targets']);
  await expect(completion).toHaveValue('flexible');
  await completion.selectOption('fixed');
  expect((await saved(page)).Stages.slice(1)).toEqual(before.Stages.slice(1));
  const duration = panel.getByRole('textbox', { name: 'Duration', exact: true });
  await expect(duration).toHaveValue('4m');
  await duration.fill('25m');
  await duration.press('Enter');
  expect((await saved(page)).StageDurationSeconds).toBe(1200);
  await expectStageEstimates(page, ['25m fixed', 'AT targets', '0s flexible'], ['100% of remaining']);
  await expect(page.locator('.schedule-card').getByRole('status')).toHaveText('Fixed stages exceed budget by 5m. Increase the budget or shorten fixed stages.');
  await expect(page.getByRole('alert')).toHaveCount(0);
  await completion.selectOption('flexible');
  expect((await saved(page)).Stages[0].DurationSeconds).toBe(0);
  await expect(panel.getByRole('textbox', { name: 'Flexible weight', exact: true })).toHaveValue('0.2');
  expect((await saved(page)).Stages.slice(1)).toEqual(before.Stages.slice(1));
  await completion.selectOption('target');
  await expect(panel.getByRole('textbox', { name: 'AT power target', exact: true })).toBeVisible();
  expect((await saved(page)).Stages[0]).toMatchObject({ CompletionMode: 1, DurationSeconds: 0, EnergyFocus: 7, MagicFocus: 4, GearGoal: 11 });
  expect((await saved(page)).StageDurationSeconds).toBe(1200);
});

test('automatic stage mode preserves saved standard fields through editing and backend readback', async ({ page }) => {
  await mount(page);
  const before = await saved(page);
  const panel = await selectStage(page, 0);
  await expect(panel.getByRole('combobox', { name: 'Stage mode', exact: true })).toHaveValue('0');
  await panel.getByRole('combobox', { name: 'Stage mode', exact: true }).selectOption('1');
  await expect(panel.getByRole('heading', { name: 'Augments + Wandoos (automatic, includes AT)', exact: true })).toBeVisible();
  for (const control of ['Focus preset', 'Energy focus', 'Magic focus']) await expect(panel.getByRole('combobox', { name: control, exact: true })).toHaveCount(0);
  for (const control of ['Energy priorities', 'Magic priorities']) await expect(panel.getByRole('group', { name: control, exact: true })).toHaveCount(0);
  await expect(panel.getByRole('checkbox', { name: 'Fights enabled', exact: true })).toBeVisible();
  const budgetNote = panel.getByText('Preparation counts toward the budget. Maximum 24 hours per automatic stage.', { exact: true });
  await expect(budgetNote).toBeVisible();
  await expect(budgetNote).toHaveAttribute('title', 'Results are not guaranteed optimal.');
  await expect(panel).not.toContainText('unused but kept for switching back');
  expect((await saved(page)).Stages[0]).toEqual({ ...before.Stages[0], ExecutionMode: 1 });
  await panel.getByRole('checkbox', { name: 'Fights enabled', exact: true }).check();
  expect((await saved(page)).Stages[0].FightSchedule).toMatchObject({ Anchor: 0, DelaySeconds: 0, IntervalSeconds: 0 });
  const budget = panel.getByRole('textbox', { name: 'Total budget', exact: true });
  await budget.fill('10m'); await budget.press('Enter');
  await readback(page);
  await expect(budget).toHaveValue('10m');
  await expect(panel.getByRole('combobox', { name: 'Stage mode', exact: true })).toHaveValue('1');
  await expect(panel.getByRole('checkbox', { name: 'Fights enabled', exact: true })).toBeChecked();
  await panel.getByRole('combobox', { name: 'Stage mode', exact: true }).selectOption('0');
  const restored = (await saved(page)).Stages[0];
  expect(restored.FightSchedule).toMatchObject({ Anchor: 0, DelaySeconds: 0, IntervalSeconds: 0 });
  for (const key of ['EnergyFocus', 'MagicFocus', 'GearGoal', 'EnergyPriorities', 'MagicPriorities', 'Future'] as const) expect(restored[key]).toEqual(before.Stages[0][key]);
  await expect(panel.getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('');
  await expect(panel.getByRole('group', { name: 'Energy priorities', exact: true }).locator('.priority-name')).toHaveText(['Time Machine']);
  await expect(panel.getByRole('textbox', { name: 'Duration', exact: true })).toHaveValue('10m');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('automatic mode requires an explicit change away from mandatory AT completion', async ({ page }) => {
  await mount(page);
  const before = await saved(page);
  const panel = await selectStage(page, 1);
  await panel.getByRole('combobox', { name: 'Stage mode', exact: true }).selectOption('1');
  await expect(page.getByRole('alert')).toContainText('Choose Time or % of remaining');
  expect(await saved(page)).toEqual(before);
  await expect(panel.getByRole('combobox', { name: 'Stage mode', exact: true })).toHaveValue('0');
});

test('automatic adventure weight autosaves through native readback and mode switches', async ({ page }) => {
  const errors: string[] = [];
  diagnostics.set(page, errors);
  page.on('pageerror', error => errors.push(error.message));
  await page.addInitScript(({ source }) => {
    const fixture = {
      plan: { ...source, Stages: [{ ...source.Stages[0], ExecutionMode: 1 }], StageLoopStartIndex: -1 } as SavedPlan,
      writes: [] as SavedPlan[],
      actions: [] as string[]
    };
    Object.assign(window, { __adventureStage: fixture, __TAURI_INTERNALS__: { invoke: async (command: string, args: Record<string, unknown> = {}) => {
      const casPath = '/e2e/fixtures/settings-cas.ts';
      const cas = await import(casPath);
      switch (command) {
        case 'get_ngui_state': return {
          runtime_dir: 'C:\\AdventureStageFixture', marker_enabled: false, connected: false, protocol: { version: 3 },
          ...await cas.settingsSnapshot({ _customPlanFile: 'stages' }), plans: ['stages'], profiles: [], logs: [], command_results: [],
          runtime: { externalUiEnabled: false, status: 'unloaded', updatedAt: new Date().toISOString(), protocolVersion: 3, settingsCasVersion: 1, supportedCommands: [], planVersion: 18 }
        };
        case 'read_plan': return JSON.stringify(fixture.plan);
        case 'save_plan': {
          const plan = JSON.parse(String(args.planJson)) as SavedPlan;
          fixture.writes.push(structuredClone(plan));
          fixture.plan = { ...plan, __externalUiRevision: String(fixture.writes.length).padStart(64, '0'), Stages: plan.Stages.map(row => ({ AdventureDynamicWeight: 0, ...row })) };
          return { success: true, mode: 'offline' };
        }
        case 'enqueue_command': fixture.actions.push(String(args.command)); throw new Error('Stage editing must not issue game actions');
        default: throw new Error(`Unexpected native call: ${command}`);
      }
    } } });
    localStorage.clear();
  }, { source });
  await page.goto('/');
  await page.getByRole('button', { name: 'Plan & Rebirth', exact: true }).click();
  const panel = await selectStage(page, 0);
  const panelId = await panel.getAttribute('id');
  const weight = panel.getByRole('spinbutton', { name: 'Adventure weight', exact: true });
  await expect(weight).toHaveValue('0');
  await weight.fill('0.125');
  const persisted = () => page.evaluate(() => {
    const fixture = (window as unknown as { __adventureStage: { plan: SavedPlan; writes: SavedPlan[]; actions: string[] } }).__adventureStage;
    return { plan: fixture.plan, writes: fixture.writes, actions: fixture.actions };
  });
  await expect.poll(async () => (await persisted()).writes.length).toBe(1);
  await expect(weight).toBeEnabled();
  await expect(weight).toHaveValue('0.125');
  await expect(panel).toHaveAttribute('id', panelId!);
  expect((await persisted()).plan.Stages[0]).toEqual({ ...source.Stages[0], ExecutionMode: 1, AdventureDynamicWeight: .125 });
  await panel.getByRole('combobox', { name: 'Stage mode', exact: true }).selectOption('0');
  await expect(weight).toHaveCount(0);
  await expect.poll(async () => (await persisted()).writes.length).toBe(2);
  expect((await persisted()).plan.Stages[0].AdventureDynamicWeight).toBe(.125);
  await panel.getByRole('combobox', { name: 'Stage mode', exact: true }).selectOption('1');
  await expect.poll(async () => (await persisted()).writes.length).toBe(3);
  await expect(weight).toHaveValue('0.125');
  await expect(panel).toHaveAttribute('id', panelId!);
  expect((await persisted()).writes.map(plan => plan.Stages[0].AdventureDynamicWeight)).toEqual([.125, .125, .125]);
  expect((await persisted()).actions).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('persisted automatic stage loads with its own identity and supports flexible budget shares', async ({ page }) => {
  await mount(page, { ...source, Stages: [{ ...source.Stages[0], ExecutionMode: 1, DurationSeconds: 0 }], StageLoopStartIndex: -1 });
  await expectStageEstimates(page, ['20m flexible budget'], ['100% of remaining']);
  await expect(page.locator('.segment-duration')).toHaveAttribute('title', 'Total elapsed budget including AT, planning and setup');
  const panel = await selectStage(page, 0);
  await expect(panel.getByRole('combobox', { name: 'Stage mode', exact: true })).toHaveValue('1');
  await expect(panel.getByRole('combobox', { name: /^Finish stage by/ })).toHaveValue('flexible');
  await expect(panel).toContainText('(total elapsed budget)');
  await expect(panel.getByRole('textbox', { name: 'Flexible weight', exact: true })).toHaveValue('0.2');
  await expect(panel.getByRole('combobox', { name: /^Finish stage by/ }).locator('option')).toHaveText(['Time', '% of remaining']);
  await expect(panel).toContainText('Time and remaining shares are total elapsed budgets, including AT, planning and setup.');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

function automaticTimingPlan(): SavedPlan {
  return { ...source, StageLoopStartIndex: -1, Stages: [0, 1].map(index => ({ ...source.Stages[0], ExecutionMode: 1, CompletionMode: 0, DurationSeconds: index ? 480 : 240 })) };
}

function automaticTimingProgress(): JsonObject {
  return { available: true, status: 'running', planName: 'stages', planVersion: 18, planRevision: 'a'.repeat(64), schedulerMode: 'stages', stageEntry: 'generation:1',
    activeStageIndex: 0, stageProgress: .5, stageElapsedSeconds: 120, repeating: false, loopStartIndex: -1,
    stages: automaticTimingPlan().Stages.map((row, index) => ({ index, executionMode: 1, completionMode: 0, configuredDurationSeconds: row.DurationSeconds!, durationSeconds: row.DurationSeconds!,
      timingKind: 'elapsed', flexWeight: row.Percent, energyFocus: row.EnergyFocus, magicFocus: row.MagicFocus, gearGoal: row.GearGoal, configuredEnergyPriorities: row.EnergyPriorities ?? [], configuredMagicPriorities: row.MagicPriorities ?? [], state: index ? 'pending' : 'current' })),
    combatPrep: { schemaVersion: 1, owner: 'scheduled', planName: 'stages', stageIndex: 0, stageEntry: 'generation:1', runId: 'activity-run', active: true, status: 'running', phase: 'Augments',
      activityTimingScope: 'run', activityTimingSemantics: 'exclusive-phase-wall-time-including-setup',
      activityTimes: ['AT', 'Augments', 'Wandoos', 'NGU', 'Background'].map((kind, index) => ({ kind, elapsedSeconds: [61, 45, 0, 0, 0][index], active: kind === 'Augments' })) }
  };
}

async function updateActivityProgress(page: Page, progress: JsonObject, props: Record<string, unknown> = {}) {
  await page.evaluate(({ progress, props }) => (window as unknown as TimingFixtureWindow).__stageLive({ progress, connected: true, dirty: false, ...props }), { progress, props });
}

test('fight status is read-only and hidden for stale entries or schedule identities', async ({ page }) => {
  const plan = automaticTimingPlan();
  plan.Stages[0].FightSchedule = { Id: 'fight-status', Anchor: 1, DelaySeconds: 180, IntervalSeconds: 180 };
  await mount(page, plan);
  const progress = automaticTimingProgress();
  const fight: JsonObject = { stageEntry: 'generation:1', scheduleId: 'fight-status', state: 'scheduled', nextFightAtSeconds: 180, remainingSeconds: 180, reason: '' };
  progress.fightSchedule = fight;
  await updateActivityProgress(page, progress);
  await expect(page.getByRole('status', { exact: true }).filter({ hasText: 'Next fight in 3m' })).toBeVisible();
  for (const [state, reason, expected] of [
    ['waiting-anchor', 'Augments not funded', 'Fight waiting: Augments not funded'],
    ['fighting', '', 'Fighting'],
    ['completed', '', 'Fights completed']
  ]) {
    await updateActivityProgress(page, { ...progress, fightSchedule: { ...fight, state, reason, nextFightAtSeconds: null, remainingSeconds: null } });
    await expect(page.locator('.live-position')).toContainText(expected);
  }
  for (const patch of [{ stageEntry: 'old-entry' }, { scheduleId: 'different' }, { state: 'future-state' }]) {
    await updateActivityProgress(page, { ...progress, fightSchedule: { ...fight, ...patch } });
    await expect(page.locator('.live-position')).not.toContainText('Next fight');
    await expect(page.locator('.live-position')).not.toContainText('Fighting');
  }
  await updateActivityProgress(page, progress, { connected: false });
  await expect(page.getByText('Next fight in 3m', { exact: true })).toHaveCount(0);
  expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageActions)).toEqual([]);
  expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(0);
});

test('automatic activity timing follows live phase counters across replans without duplicate breakdowns or actions', async ({ page }) => {
  await mount(page, automaticTimingPlan());
  await page.evaluate(() => {
    const fixture = window as unknown as TimingFixtureWindow;
    fixture.__stageNativeCalls = [];
    Object.assign(window, { __TAURI_INTERNALS__: { invoke: async (command: string) => { fixture.__stageNativeCalls.push(command); throw new Error(`Unexpected action: ${command}`); } } });
  });
  const before = await saved(page);
  const progress = automaticTimingProgress();
  await updateActivityProgress(page, progress);
  await expect(page.locator('.live-position')).toContainText('Now: Augments / 45s');
  const visibleTiming = page.locator('.activity-timing:visible');
  await expect(visibleTiming).toHaveCount(1);
  await expect(visibleTiming.locator('.timing-rows dd')).toHaveText(['1m 1s', '45s', 'Not visited']);
  await expect(visibleTiming.locator('.active-badge')).toHaveText('Active');
  const panel = await selectStage(page, 0);
  await expect(panel.getByRole('region', { name: 'Actual activity timing' })).toBeVisible();
  await expect(visibleTiming).toHaveCount(1);
  const runtime = progress.combatPrep as JsonObject;
  const rows = runtime.activityTimes as JsonObject[];
  rows[1].active = false;
  Object.assign(runtime, { equipmentReplanPending: true, phase: 'Wandoos' });
  await updateActivityProgress(page, progress);
  await expect(page.getByText(/^Now:/)).toHaveCount(0);
  await expect(visibleTiming.locator('.timing-rows dd')).toHaveText(['1m 1s', '45s', 'Not visited']);
  Object.assign(runtime, { replanCount: 1, replanReason: 'AT verified', phase: 'Wandoos', phaseElapsedSeconds: 0 });
  runtime.equipmentReplanPending = false;
  rows[2].active = true;
  rows[2].elapsedSeconds = 12;
  await updateActivityProgress(page, progress);
  await expect(page.locator('.live-position')).toContainText('Now: Wandoos / 12s');
  await expect(visibleTiming.locator('.timing-rows dd')).toHaveText(['1m 1s', '45s', '12s']);
  await expect(visibleTiming.locator('.timing-rows [data-kind="Wandoos"] .active-badge')).toBeVisible();
  await visibleTiming.getByText('How time is counted', { exact: true }).click();
  for (const text of ['This run only', 'including phase setup', 'not separately added', 'Excludes entry preparation and cleanup']) await expect(visibleTiming).toContainText(text);
  await expect(visibleTiming).not.toContainText(/in memory|storage|injector session|polling/i);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('automatic-activity-timing.png'), fullPage: true });
  await selectStage(page, 1);
  await expect(page.getByRole('region', { name: 'Stage 2 settings', exact: true })).toContainText('Activity timing not recorded');
  await expect(page.locator('.timing-rows:visible')).toHaveCount(1);
  await selectStage(page, 0);
  // Even inconsistent stale active flags must never make cleanup look like training.
  Object.assign(runtime, { cleanupPending: true, status: 'failed' });
  await updateActivityProgress(page, progress);
  await expect(page.getByText(/^Now:/)).toHaveCount(0);
  await expect(visibleTiming.locator('.active-badge')).toHaveCount(0);
  await expect(visibleTiming.locator('.timing-rows dd')).toHaveText(['1m 1s', '45s', '12s']);
  expect(await saved(page)).toEqual(before);
  expect(await page.evaluate(() => { const fixture = window as unknown as TimingFixtureWindow; return [fixture.__stageEdits, fixture.__stageActions, fixture.__stageNativeCalls]; })).toEqual([0, [], []]);
});

test('automatic activity timing refuses old runs, stale progress, dirty drafts and mismatched stage identity', async ({ page }) => {
  await mount(page, automaticTimingPlan());
  const panel = await selectStage(page, 0);
  await expect(panel).toContainText('Activity timing not recorded');
  for (const mismatch of ['manual', 'plan-name', 'index', 'entry', 'previous-loop', 'freshness', 'dirty', 'revision', 'order', 'numbers', 'duplicates']) {
    const progress = automaticTimingProgress();
    const runtime = progress.combatPrep as JsonObject;
    if (mismatch === 'manual') runtime.owner = 'manual';
    if (mismatch === 'plan-name') runtime.planName = 'other';
    if (mismatch === 'index') runtime.stageIndex = 1;
    if (mismatch === 'entry') delete runtime.stageEntry;
    if (mismatch === 'previous-loop') progress.stageEntry = 'generation:2';
    if (mismatch === 'revision') progress.planRevision = 'b'.repeat(64);
    if (mismatch === 'order') (progress.stages as JsonObject[]).reverse();
    if (mismatch === 'numbers') (runtime.activityTimes as JsonObject[])[0].elapsedSeconds = '61';
    if (mismatch === 'duplicates') (runtime.activityTimes as JsonObject[])[2].kind = 'AT';
    await updateActivityProgress(page, progress, { connected: mismatch !== 'freshness', dirty: mismatch === 'dirty' });
    await expect(panel).toContainText('Activity timing not recorded');
    await expect(page.locator('.timing-rows:visible')).toHaveCount(0);
    await expect(page.getByText(/^Now:/)).toHaveCount(0);
  }
  const progress = automaticTimingProgress();
  await updateActivityProgress(page, progress);
  await expect(panel.locator('.timing-rows')).toBeVisible();
  const duration = panel.getByRole('textbox', { name: 'Total budget', exact: true });
  await duration.fill('unfinished');
  await expect(panel).toContainText('Activity timing not recorded');
  await expect(page.getByText(/^Now:/)).toHaveCount(0);
  await duration.press('Escape');
  await expect(panel.locator('.timing-rows')).toBeVisible();
  // A terminal receipt cannot be attached to the next selected stage or a new loop entry.
  Object.assign(progress, { activeStageIndex: 1, stageEntry: 'generation:2' });
  (progress.stages as JsonObject[])[0].state = 'completed';
  (progress.stages as JsonObject[])[1].state = 'current';
  Object.assign(progress.combatPrep as JsonObject, { status: 'completed', active: false });
  await updateActivityProgress(page, progress);
  await expect(panel).toContainText('Activity timing not recorded');
  await selectStage(page, 1);
  await expect(page.getByRole('region', { name: 'Stage 2 settings', exact: true })).toContainText('Activity timing not recorded');
  await expect(page.locator('.timing-rows:visible')).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(await page.evaluate(() => { const fixture = window as unknown as TimingFixtureWindow; return [fixture.__stageEdits, fixture.__stageActions]; })).toEqual([0, []]);
});

test('automatic fixed budgets are limited per stage, not by a plan longer than 24 hours', async ({ page }) => {
  await mount(page, { ...source, StageDurationSeconds: 172800, StageLoopStartIndex: -1, Stages: [0, 1].map(() => ({ ...source.Stages[0], ExecutionMode: 1, DurationSeconds: 86400 })) });
  await expect(page.getByRole('alert')).toHaveCount(0);
  const panel = await selectStage(page, 0);
  const duration = panel.getByRole('textbox', { name: 'Total budget', exact: true });
  await expect(duration).toHaveValue('24h');
  await expect(panel).toContainText('Maximum 24 hours per automatic stage.');
  await duration.fill('25h'); await duration.press('Enter');
  await expect(page.getByRole('alert')).toContainText('Stage 1: automatic Augments + Wandoos effective stage budget must not exceed 86400');
  expect((await saved(page)).Stages[0].DurationSeconds).toBe(90000);
  await duration.fill('24h'); await duration.press('Enter');
  await expect(page.getByRole('alert')).toHaveCount(0);
  expect((await saved(page)).StageDurationSeconds).toBe(172800);
  expect((await saved(page)).Stages[1].DurationSeconds).toBe(86400);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('automatic flexible validation uses nominal weights and the enabled positive rebirth horizon', async ({ page }) => {
  await mount(page, { ...source, StageDurationSeconds: 172801, StageLoopStartIndex: -1, Stages: [{ ...source.Stages[0], ExecutionMode: 1, DurationSeconds: 0, Percent: 1 }, { ...source.Stages[0], DurationSeconds: 0, Percent: 1 }] });
  await expect(page.getByRole('alert')).toContainText('including flexible allocation');
  await openSchedule(page);
  const horizon = page.getByRole('textbox', { name: 'Fallback time budget', exact: true });
  await horizon.fill('48h'); await horizon.press('Enter');
  await expect(page.getByRole('alert')).toHaveCount(0);
  const panel = await selectStage(page, 0);
  const weight = panel.getByRole('textbox', { name: 'Flexible weight', exact: true });
  await weight.fill('3'); await weight.press('Enter');
  await expect(page.getByRole('alert')).toContainText('effective stage budget');
  await page.getByRole('checkbox', { name: 'After a set time', exact: true }).check();
  const rebirthAfter = page.getByRole('textbox', { name: 'Rebirth after', exact: true });
  await rebirthAfter.fill('32h'); await rebirthAfter.press('Enter');
  await expect(page.getByRole('alert')).toHaveCount(0);
  await rebirthAfter.fill('115201s'); await rebirthAfter.press('Enter');
  await expect(page.getByRole('alert')).toContainText('effective stage budget');
  await rebirthAfter.fill('0s'); await rebirthAfter.press('Enter');
  await horizon.fill('32h'); await horizon.press('Enter');
  await expect(page.getByRole('alert')).toHaveCount(0);
  expect((await saved(page)).Stages[0].Percent).toBe(3);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('automatic flexible validation reserves standard fixed time before allocating its share', async ({ page }) => {
  await mount(page, { ...source, StageDurationSeconds: 259200, StageLoopStartIndex: -1, Stages: [{ ...source.Stages[0], DurationSeconds: 86400 }, { ...source.Stages[0], ExecutionMode: 1, DurationSeconds: 0, Percent: 1 }, { ...source.Stages[0], DurationSeconds: 0, Percent: 1 }] });
  await expect(page.getByRole('alert')).toHaveCount(0);
  const panel = await selectStage(page, 0);
  const duration = panel.getByRole('textbox', { name: 'Duration', exact: true });
  await duration.fill('23h'); await duration.press('Enter');
  await expect(page.getByRole('alert')).toContainText('Stage 2: automatic Augments + Wandoos effective stage budget');
  await duration.fill('24h'); await duration.press('Enter');
  await expect(page.getByRole('alert')).toHaveCount(0);
});

for (const flexible of [
  { mode: 'standard', ExecutionMode: 0, estimate: '22h flexible' },
  { mode: 'automatic', ExecutionMode: 1, estimate: '22h flexible budget' }
]) {
  test(`24h rebirth horizon shows 2h fixed and 22h ${flexible.mode} flexible time`, async ({ page }) => {
    await mount(page, { ...source, TimeTriggerEnabled: true, TimeTriggerSeconds: 86400, StageLoopStartIndex: -1, Stages: [
      { ...source.Stages[0], DurationSeconds: 7200, CompletionMode: 0 },
      { ...source.Stages[2], ExecutionMode: flexible.ExecutionMode, DurationSeconds: 0, CompletionMode: 0, Percent: 35 }
    ] });
    const before = await saved(page);
    await expectStageEstimates(page, ['2h fixed', flexible.estimate], ['100% of remaining']);
    const panel = await selectStage(page, 1);
    await expect(panel.getByRole('textbox', { name: 'Flexible weight', exact: true })).toHaveValue('35');
    await expect(panel.getByRole('combobox', { name: /^Finish stage by/ })).toHaveValue('flexible');
    expect(await saved(page)).toEqual(before);
    expect(before).toMatchObject({ StageDurationSeconds: 1200, TimeTriggerEnabled: true, TimeTriggerSeconds: 86400 });
    expect(before.Stages.map(row => [row.DurationSeconds, row.Percent])).toEqual([[7200, .2], [0, 35]]);
    expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(0);
    await expect(page.locator('.schedule-card').getByRole('status')).toHaveCount(0);
    await expect(page.getByRole('alert')).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

test('weighted shares reserve fixed tail time and allow a zero fallback budget', async ({ page }) => {
  const weightedPlan: SavedPlan = { ...source, Stages: [
    { ...source.Stages[0], DurationSeconds: 120, CompletionMode: 0 },
    { ...source.Stages[0], Name: 'Flex A', Percent: 35, DurationSeconds: 0, CompletionMode: 0 },
    { ...source.Stages[2], Name: 'Flex B', Percent: 50, DurationSeconds: 0, CompletionMode: 0 },
    { ...source.Stages[2], Name: 'Flex C', Percent: 15, DurationSeconds: 0, CompletionMode: 0 },
    { ...source.Stages[0], Name: 'Tail', DurationSeconds: 80, CompletionMode: 0 }
  ] };
  await mount(page, weightedPlan);
  await expectStageEstimates(page, ['2m fixed', '350s flexible', '500s flexible', '150s flexible', '80s fixed'], ['35% of remaining', '50% of remaining', '15% of remaining']);
  const panel = await selectStage(page, 1);
  await expect(panel.getByText('35% of remaining time.', { exact: false })).toBeVisible();
  const weight = panel.getByRole('textbox', { name: 'Flexible weight', exact: true });
  await expect(weight).toHaveValue('35');
  await weight.fill('50%');
  await weight.press('Enter');
  await expect(weight).toHaveValue('50');
  expect((await saved(page)).Stages.map(row => row.Percent)).toEqual([.2, 50, 50, 15, .2]);
  await expect(panel.getByText('43.478261% of remaining time.', { exact: false })).toBeVisible();
  await expectStageEstimates(page, ['2m fixed', '434s flexible', '435s flexible', '131s flexible', '80s fixed'], ['43.478261% of remaining', '43.478261% of remaining', '13.043478% of remaining']);
  expect((await saved(page)).Stages.map(row => row.DurationSeconds)).toEqual([120, 0, 0, 0, 80]);
  expect((await saved(page)).StageDurationSeconds).toBe(1200);
  await openSchedule(page);
  const budget = page.getByRole('textbox', { name: 'Fallback time budget', exact: true });
  await budget.fill('4.1m');
  await budget.press('Enter');
  expect((await saved(page)).StageDurationSeconds).toBe(246);
  await expectStageEstimates(page, ['2m fixed', '20s flexible', '20s flexible', '6s flexible', '80s fixed'], ['43.478261% of remaining', '43.478261% of remaining', '13.043478% of remaining']);
  await budget.fill('200s');
  await budget.press('Enter');
  expect((await saved(page)).StageDurationSeconds).toBe(200);
  await expectStageEstimates(page, ['2m fixed', '0s flexible', '0s flexible', '0s flexible', '80s fixed'], ['43.478261% of remaining', '43.478261% of remaining', '13.043478% of remaining']);
  await expect(page.locator('.schedule-card').getByRole('status')).toHaveText('No time for flexible stages. Increase the budget or shorten fixed stages.');
  await budget.fill('0s');
  await budget.press('Enter');
  expect((await saved(page)).StageDurationSeconds).toBe(0);
  await expectStageEstimates(page, ['2m fixed', '0s flexible', '0s flexible', '0s flexible', '80s fixed'], ['43.478261% of remaining', '43.478261% of remaining', '13.043478% of remaining']);
  await expect(page.locator('.schedule-card').getByRole('status')).toHaveText('Fixed stages exceed budget by 200s. Increase the budget or shorten fixed stages.');
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

for (const trigger of [{ TimeTriggerEnabled: false, TimeTriggerSeconds: 3600 }, { TimeTriggerEnabled: true, TimeTriggerSeconds: 0 }]) {
  test(`AT template refuses a zero horizon after confirmation with time trigger ${trigger.TimeTriggerEnabled ? 'zero' : 'disabled'}`, async ({ page }) => {
    await mount(page, { ...source, StageDurationSeconds: 0, ...trigger });
    const before = await page.evaluate(() => (window as unknown as { __stagePlan: string }).__stagePlan);
    await chooseTemplate(page, 'AT targets 6k/6k then active preset', 'Yes');
    await expect(page.getByRole('alert')).toHaveText('AT Adventure to NGU requires a positive time budget. Enable a positive time trigger or set a positive fallback time budget before applying this template.');
    expect(await page.evaluate(() => (window as unknown as { __stagePlan: string }).__stagePlan)).toBe(before);
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
    await expect(page.locator('.bar-segment')).toHaveCount(source.Stages.length);
  });
}

for (const budget of [
  { StageDurationSeconds: 0, TimeTriggerEnabled: true, TimeTriggerSeconds: 3600, horizon: '1h', source: 'Time trigger' },
  { StageDurationSeconds: 1200, TimeTriggerEnabled: true, TimeTriggerSeconds: 3600, horizon: '1h', source: 'Time trigger' },
  { StageDurationSeconds: 1200, TimeTriggerEnabled: false, TimeTriggerSeconds: 3600, horizon: '20m', source: 'Schedule length' }
]) {
  test(`AT template fills ${budget.horizon} from ${budget.source} with a flexible NGU row (fallback ${budget.StageDurationSeconds}s)`, async ({ page }) => {
    const { StageDurationSeconds, TimeTriggerEnabled, TimeTriggerSeconds } = budget;
    await mount(page, { ...source, StageDurationSeconds, TimeTriggerEnabled, TimeTriggerSeconds });
    const before = await saved(page);
    await chooseTemplate(page, 'AT targets 6k/6k then active preset', 'Yes');
    await expect(page.locator('.bar-segment')).toHaveCount(2);
    const next = await saved(page);
    expect(next).toEqual({ ...before, Stages: next.Stages, StageSchedulerEnabled: true, StageLoopStartIndex: -1 });
    expect(next.Stages[0]).toMatchObject({ CompletionMode: 1, DurationSeconds: 0, TargetAdvancedTrainingPower: '6000', TargetAdvancedTrainingToughness: '6000' });
    expect(next.Stages[1]).toMatchObject({ Name: 'Weighted preset', PresetId: '@active', CompletionMode: 0, DurationSeconds: 0, DurationUnit: '%', Percent: 100 });
    await expectStageEstimates(page, ['AT targets', `${budget.horizon} flexible`], ['100% of remaining']);
    const panel = await selectStage(page, 1);
    await expect(panel.getByRole('combobox', { name: /^Finish stage by/ })).toHaveValue('flexible');
    await expect(panel.getByRole('textbox', { name: 'Flexible weight', exact: true })).toHaveValue('100');
    await expect(panel.getByText('100% of remaining time.', { exact: false })).toBeVisible();
    await expect(page.getByRole('alert')).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(1);
  });
}

test('flexible weights enter exact 35/50/15 shares without changing fixed time or the horizon', async ({ page }) => {
  const initial: SavedPlan = { ...source, TimeTriggerEnabled: true, TimeTriggerSeconds: 1200, Stages: [
    { ...source.Stages[0], DurationSeconds: 120, CompletionMode: 0 },
    ...source.Stages.map(row => ({ ...row, CompletionMode: 0, DurationSeconds: 0, Percent: 100 })),
    { ...source.Stages[2], DurationSeconds: 80, CompletionMode: 0 }
  ] };
  await mount(page, initial);
  const before = await saved(page);
  for (const [index, text] of ['35', '50', '15'].entries()) {
    const panel = await selectStage(page, index + 1);
    const weight = panel.getByRole('textbox', { name: 'Flexible weight', exact: true });
    await weight.fill(text);
    await weight.press('Enter');
    await expect(weight).toHaveValue(text);
  }
  const next = await saved(page);
  expect(next).toEqual({ ...before, Stages: before.Stages.map((row, i) => i >= 1 && i <= 3 ? { ...row, Percent: [35, 50, 15][i - 1] } : row) });
  for (const [index, text] of ['35', '50', '15'].entries()) {
    const panel = await selectStage(page, index + 1);
    await expect(panel.getByRole('textbox', { name: 'Flexible weight', exact: true })).toHaveValue(text);
    await expect(panel.getByText(`${text}% of remaining time.`, { exact: false })).toBeVisible();
    const segment = page.locator('.bar-segment').nth(index + 1);
    await expect(segment.locator('.segment-share')).toHaveText(`${text}% of remaining`);
    await expect(segment.locator('.segment-duration')).toHaveAttribute('title', 'Estimated flexible share, not live time remaining');
  }
  await expectStageEstimates(page, ['2m fixed', '350s flexible', '500s flexible', '150s flexible', '80s fixed'], ['35% of remaining', '50% of remaining', '15% of remaining']);
  await expect(page.getByRole('alert')).toHaveCount(0);
  expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(3);
});

test('flexible weight drafts commit on Enter and blur, cancel on Escape, and survive readback', async ({ page }) => {
  await mount(page, { ...source, Stages: source.Stages.map(row => ({ ...row, CompletionMode: 0, DurationSeconds: 0, Percent: 100 })) }, true);
  await pauseDurationClock(page);
  const panel = await selectStage(page, 0);
  const weight = panel.getByRole('textbox', { name: 'Flexible weight', exact: true });
  await weight.fill('35');
  expect((await saved(page)).Stages[0].Percent).toBe(100);
  expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
  await readback(page);
  await expect(weight).toBeFocused();
  await expect(weight).toHaveValue('35');
  await weight.press('Enter');
  await expect(weight).toHaveValue('35');
  expect((await saved(page)).Stages[0].Percent).toBe(35);
  await weight.press('Tab');
  expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(1);
  await weight.fill('350%');
  await weight.press('Tab');
  await expect(weight).toHaveValue('350');
  expect((await saved(page)).Stages.map(row => row.Percent)).toEqual([350, 100, 100]);
  expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(2);
  await weight.fill('15');
  await weight.press('Escape');
  await expect(weight).toHaveValue('350');
  await weight.press('Tab');
  expect((await saved(page)).Stages[0].Percent).toBe(350);
  expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(2);
  await weight.fill('0');
  await weight.press('Enter');
  await expect(page.getByRole('alert')).toContainText('Flexible weight');
  await expect(weight).toHaveValue('0');
  expect((await saved(page)).Stages[0].Percent).toBe(350);
  await weight.press('Escape');
  await expect(weight).toHaveValue('350');
  await expect(page.getByRole('alert')).toHaveCount(0);
  await weight.press('Tab');
  expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(2);
  expect((await saved(page)).StageDurationSeconds).toBe(1200);
});

test('single flexible weight stays normalized to 100% without limiting its raw value', async ({ page }) => {
  await mount(page, { ...source, Stages: [{ ...source.Stages[0], DurationSeconds: 120, CompletionMode: 0 }, { ...source.Stages[2], CompletionMode: 0, DurationSeconds: 0, Percent: 35 }] });
  const panel = await selectStage(page, 1);
  const weight = panel.getByRole('textbox', { name: 'Flexible weight', exact: true });
  await expect(weight).toHaveValue('35');
  await expect(panel.getByText('100% of remaining time.', { exact: false })).toBeVisible();
  await weight.fill('150');
  await weight.press('Enter');
  await expect(weight).toHaveValue('150');
  await expect(panel.getByText('100% of remaining time.', { exact: false })).toBeVisible();
  await expectStageEstimates(page, ['2m fixed', '18m flexible'], ['100% of remaining']);
  expect((await saved(page)).Stages.map(row => row.Percent)).toEqual([.2, 150]);
  expect((await saved(page)).StageDurationSeconds).toBe(1200);
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('fight schedule durations retain drafts and identity through readback, reorder and mode changes', async ({ page }) => {
  await mount(page);
  let panel = await selectStage(page, 0);
  await panel.getByRole('checkbox', { name: 'Fights enabled', exact: true }).check();
  const initial = (await saved(page)).Stages[0].FightSchedule!;
  expect(initial.Id).toMatch(/^[a-f0-9-]{36}$/);
  await expect(panel.getByLabel('First fight after', { exact: true })).toHaveValue('0s');
  await panel.getByLabel('First fight after', { exact: true }).fill('3m');
  await page.waitForTimeout(450);
  expect((await saved(page)).Stages[0].FightSchedule!.DelaySeconds).toBe(0);
  await panel.getByLabel('First fight after', { exact: true }).press('Enter');
  await panel.getByLabel('Repeat fights', { exact: true }).check();
  await panel.getByLabel('Repeat every', { exact: true }).fill('3m');
  await panel.getByLabel('Repeat every', { exact: true }).press('Enter');
  await panel.getByLabel('Start from', { exact: true }).selectOption('1');
  expect((await saved(page)).Stages[0].FightSchedule).toEqual({ ...initial, Anchor: 1, DelaySeconds: 180, IntervalSeconds: 180 });
  await panel.getByLabel('First fight after', { exact: true }).fill('3.');
  await readback(page);
  await expect(panel.getByLabel('First fight after', { exact: true })).toHaveValue('3.');
  await expect(panel.getByLabel('First fight after', { exact: true })).toHaveAttribute('aria-invalid', 'true');
  await expect.poll(() => page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageDraftDirty)).toBe(true);
  await page.locator('.stage-select').first().press('Alt+ArrowRight');
  panel = await selectStage(page, 1);
  await expect(panel.getByLabel('First fight after', { exact: true })).toHaveValue('3.');
  expect((await saved(page)).Stages[1].FightSchedule!.Id).toBe(initial.Id);
  expect((await saved(page)).Stages[1].FightSchedule!.DelaySeconds).toBe(180);
  await panel.getByLabel('First fight after', { exact: true }).press('Escape');
  await panel.getByLabel('Stage mode', { exact: true }).selectOption('1');
  await expect(panel.getByLabel('Start from', { exact: true })).toHaveValue('1');
  await panel.getByLabel('Repeat fights', { exact: true }).uncheck();
  expect((await saved(page)).Stages[1].FightSchedule).toEqual({ ...initial, Anchor: 1, DelaySeconds: 180, IntervalSeconds: 0 });
  await panel.getByLabel('Fights enabled', { exact: true }).uncheck();
  expect((await saved(page)).Stages[1].FightSchedule).toBeNull();
  expect((await saved(page)).Stages[1]).not.toHaveProperty('FightOnEntry');
  await readback(page);
  await expect(panel.getByLabel('Fights enabled', { exact: true })).not.toBeChecked();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('legacy entry fight opens with once-at-entry defaults without writing on render', async ({ page }) => {
  await mount(page, { ...source, Stages: source.Stages.map(({ FightSchedule, ...row }, index) => index === 0 ? { ...row, FightOnEntry: true } : { ...row, FightSchedule }) });
  const panel = await selectStage(page, 0);
  await expect(panel.getByLabel('Fights enabled', { exact: true })).toBeChecked();
  await expect(panel.getByLabel('Start from', { exact: true })).toHaveValue('0');
  await expect(panel.getByLabel('First fight after', { exact: true })).toHaveValue('0s');
  await expect(panel.getByLabel('Repeat fights', { exact: true })).not.toBeChecked();
  expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(0);
});

test('fights survive normalized readback and presets without losing a duration draft', async ({ page }) => {
  await mount(page);
  const panel = await selectStage(page, 0);
  const duration = panel.getByRole('textbox', { name: 'Duration', exact: true });
  await duration.fill('unfinished');
  await readback(page);
  await expect(panel).toBeVisible();
  await expect(duration).toHaveValue('unfinished');
  await duration.press('Escape');
  const fight = panel.getByRole('checkbox', { name: 'Fights enabled', exact: true });
  await expect(fight).not.toBeChecked();
  await fight.check();
  const schedule = (await saved(page)).Stages[0].FightSchedule;
  expect(schedule).toMatchObject({ Anchor: 0, DelaySeconds: 0, IntervalSeconds: 0 });
  expect((await saved(page)).Stages.slice(1).map(row => row.FightSchedule)).toEqual([null, null]);
  await readback(page);
  await expect(fight).toBeChecked();
  await panel.getByRole('combobox', { name: 'Focus preset', exact: true }).selectOption({ label: 'Energy NGU' });
  expect((await saved(page)).Stages[0].FightSchedule).toEqual(schedule);
  await fight.uncheck();
  await readback(page);
  await expect(fight).not.toBeChecked();
  expect((await saved(page)).Stages[0].FightSchedule).toBeNull();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

for (const label of ['30m weighted cycle', 'AT targets 6k/6k then active preset']) {
  test(`confirming ${label} applies the complete template and preserves plan metadata`, async ({ page }) => {
    await mount(page);
    const before = await saved(page);
    const advancedTraining = label === 'AT targets 6k/6k then active preset';
    await chooseTemplate(page, label, 'Yes');
    const rows = page.locator('.bar-segment');
    await expect(rows).toHaveCount(advancedTraining ? 2 : 8);
    const next = await saved(page);
    expect(next).toEqual({ ...before, Stages: next.Stages, StageSchedulerEnabled: true, StageDurationSeconds: advancedTraining ? 1200 : 1800, StageLoopStartIndex: advancedTraining ? -1 : 5 });
    expect(await page.evaluate(() => (window as unknown as { __stagePlan: string }).__stagePlan)).toContain('"Future":9007199254740993');
    expect(next.Stages.every(row => row.Future === undefined)).toBe(true);
    await expect(page.getByRole('checkbox', { name: 'Enable stages', exact: true })).toBeChecked();
    await expect(page.getByRole('textbox', { name: 'Stage name', exact: true })).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: 'Stage timing', exact: true })).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: 'Repeat from', exact: true })).toHaveValue(advancedTraining ? '-1' : '5');
    const names = advancedTraining ? ['AT Adventure', 'Weighted preset'] : Array(8).fill('Weighted preset');
    expect(next.Stages.map(row => row.Name)).toEqual(names);
    for (const [index, name] of names.entries()) {
      const reference = advancedTraining && index === 0 ? 'global_6' : '@active';
      expect(next.Stages[index].PresetId).toBe(reference);
      expect(next.Stages[index]).not.toHaveProperty('EnergyPriorities');
      expect(next.Stages[index]).not.toHaveProperty('MagicPriorities');
      const panel = await selectStage(page, index);
      await expect(panel.getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue(reference);
      await expect(rows.nth(index).getByRole('button', { name: `Edit stage ${index + 1}: ${reference === '@active' ? 'Plan active preset' : name}`, exact: true })).toHaveAttribute('draggable', 'false');
    }
    if (advancedTraining) {
      expect(next.Stages.map(row => [row.CompletionMode, row.DurationSeconds, row.TargetAdvancedTrainingPower, row.TargetAdvancedTrainingToughness])).toEqual([
        [1, 0, '6000', '6000'], [0, 0, '0', '0']
      ]);
      expect(next.Stages.every(row => row.EnergyFocus === undefined && row.MagicFocus === undefined && row.GearGoal === undefined)).toBe(true);
      const panel = await selectStage(page, 0);
      await expect(panel.getByRole('textbox', { name: 'Duration', exact: true })).toHaveCount(0);
      await expect(panel.getByRole('textbox', { name: 'AT power target', exact: true })).toHaveValue('6000');
      await expect(panel.getByRole('textbox', { name: 'AT toughness target', exact: true })).toHaveValue('6000');
      await expectDurations(page, []);
      const sustain = await selectStage(page, 1);
      await expect(sustain.getByRole('textbox', { name: 'Flexible weight', exact: true })).toHaveValue('100');
      await expect(sustain.getByText('100% of remaining time.', { exact: false })).toBeVisible();
      await expectStageEstimates(page, ['AT targets', '20m flexible'], ['100% of remaining']);
    } else {
      expect(next.Stages.map(row => row.DurationSeconds)).toEqual([300, 180, 180, 60, 180, 300, 300, 300]);
      expect(next.Stages.reduce((total, row) => total + row.DurationSeconds!, 0)).toBe(1800);
      expect(next.Stages.every(row => row.EnergyFocus === undefined && row.MagicFocus === undefined && row.GearGoal === undefined)).toBe(true);
      expect(next.Stages.every(row => row.CompletionMode === 0 && row.TargetAdvancedTrainingPower === '0' && row.TargetAdvancedTrainingToughness === '0')).toBe(true);
      await expect(page.getByRole('textbox', { name: 'AT power target', exact: true })).toHaveCount(0);
      await expectDurations(page, ['5m', '3m', '3m', '1m', '3m', '5m', '5m', '5m']);
      await expect(rows.nth(5).getByLabel('Loop start', { exact: true })).toBeVisible();
      await expectStageEstimates(page, ['5m fixed', '3m fixed', '3m fixed', '1m fixed', '3m fixed', '5m fixed', '5m fixed', '5m fixed']);
      const scroll = page.locator('.bar-scroll');
      if (await page.evaluate(() => innerWidth < 740)) {
        expect(await scroll.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
        await rows.first().getByRole('button', { name: /^Edit stage/ }).scrollIntoViewIfNeeded();
        const start = await scroll.evaluate(element => element.scrollLeft);
        await rows.last().getByRole('button', { name: /^Edit stage/ }).scrollIntoViewIfNeeded();
        expect(await scroll.evaluate(element => element.scrollLeft)).toBeGreaterThan(start);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      }
    }
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(1);
    await expect(page.getByRole('alert')).toHaveCount(0);
  });

  test(`cancelling ${label} leaves all stages and saved data untouched`, async ({ page }) => {
    await mount(page);
    const before = await page.evaluate(() => (window as unknown as { __stagePlan: string }).__stagePlan);
    await chooseTemplate(page, label, 'No');
    await expect(page.locator('.bar-segment')).toHaveCount(3);
    expect(await page.evaluate(() => (window as unknown as { __stagePlan: string }).__stagePlan)).toBe(before);
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
    const panel = await selectStage(page, 1);
    await expect(panel.getByRole('textbox', { name: 'AT power target', exact: true })).toHaveValue('9223372036854775806');
    await expect(page.getByRole('alert')).toHaveCount(0);
  });
}

test('Energy NGU global selection references the saved definition without rewriting legacy fields', async ({ page }) => {
  await mount(page);
  const first = await selectStage(page, 0);
  await first.getByRole('combobox', { name: 'Focus preset', exact: true }).selectOption({ label: 'Energy NGU' });
  await expect(first.getByRole('combobox', { name: 'Energy focus', exact: true })).toHaveCount(0);
  await expect(first.getByRole('combobox', { name: 'Magic focus', exact: true })).toHaveCount(0);
  await expect(first.getByRole('combobox', { name: 'Gear goal', exact: true })).toHaveCount(0);
  await expect(page.locator('.bar-segment').first().getByRole('button', { name: 'Edit stage 1: Energy NGU', exact: true })).toHaveAttribute('aria-expanded', 'true');
  const next = await saved(page);
  expect(next.Stages[0]).toEqual({ ...source.Stages[0], Name: 'Energy NGU', PresetId: 'global_3' });
  expect(next.Stages.slice(1)).toEqual(source.Stages.slice(1));
  expect(next.StageLoopStartIndex).toBe(source.StageLoopStartIndex);
});

test('unrelated stage edits preserve hidden custom gear and cap until an explicit preset action', async ({ page }) => {
  const custom = { ...source, Stages: source.Stages.map((row, index) => index === 0 ? { ...row, Name: 'Time Machine', GearGoal: 9, BlackBarLookaheadSeconds: 37, FightSchedule: { Id: 'legacy-entry-v1-0', Anchor: 0, DelaySeconds: 0, IntervalSeconds: 0 } } : row) };
  await mount(page, custom);
  const panel = await selectStage(page, 0);
  await expect(panel.getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('');
  expect((await saved(page)).Stages).toEqual(custom.Stages);
  const duration = panel.getByRole('textbox', { name: 'Duration', exact: true });
  await duration.fill('5m');
  await duration.press('Enter');
  await panel.getByRole('combobox', { name: 'Energy focus', exact: true }).selectOption('3');
  expect((await saved(page)).Stages[0]).toMatchObject({ GearGoal: 9, BlackBarLookaheadSeconds: 37, FightSchedule: { Id: 'legacy-entry-v1-0' }, DurationSeconds: 300, EnergyFocus: 3 });
  await readback(page);
  await expect(panel.getByRole('checkbox', { name: 'Fights enabled', exact: true })).toBeChecked();
  expect((await saved(page)).Stages[0]).toMatchObject({ GearGoal: 9, BlackBarLookaheadSeconds: 37, FightSchedule: { Id: 'legacy-entry-v1-0' } });
  await panel.getByRole('combobox', { name: 'Focus preset', exact: true }).selectOption({ label: 'Wandoos' });
  expect((await saved(page)).Stages[0]).toMatchObject({ Name: 'Wandoos', PresetId: 'global_2', EnergyFocus: 3, MagicFocus: 1, GearGoal: 9, EnergyPriorities: ['TM'], MagicPriorities: ['RIT'], BlackBarLookaheadSeconds: 37, FightSchedule: { Id: 'legacy-entry-v1-0' }, DurationSeconds: 300 });
  await readback(page);
  expect((await saved(page)).Stages[0]).toMatchObject({ PresetId: 'global_2', GearGoal: 9, BlackBarLookaheadSeconds: 37, FightSchedule: { Id: 'legacy-entry-v1-0' } });
  await expect(panel.getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('global_2');
});

for (const [index, label] of ['Time Machine', 'Augments + Blood', 'Wandoos', 'Energy NGU', 'Magic NGU', 'All NGU', 'AT Adventure', 'AT Wandoos'].entries()) {
  test(`${label} global reference survives legacy customization and normalized readback`, async ({ page }) => {
    await mount(page, { ...source, Stages: source.Stages.map((row, index) => index === 0 ? { ...row, GearGoal: 9, BlackBarLookaheadSeconds: 37, FightSchedule: { Id: 'legacy-entry-v1-0', Anchor: 0, DelaySeconds: 0, IntervalSeconds: 0 } } : row) });
    await expect((await selectStage(page, 1)).getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('');
    await expect((await selectStage(page, 2)).getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('');
    const first = await selectStage(page, 0);
    const preset = first.getByRole('combobox', { name: 'Focus preset', exact: true });
    await expect(preset).toHaveValue('');
    const energy = first.getByRole('group', { name: 'Energy priorities', exact: true });
    const magic = first.getByRole('group', { name: 'Magic priorities', exact: true });
    await expect(energy.locator('.priority-name')).toHaveText(['Time Machine']);
    await expect(magic.locator('.priority-name')).toHaveText(['Blood Magic (automatic ritual)']);
    await setPriorities(first, 'Energy priorities', ['Time Machine', 'NGU: Augments']);
    await setPriorities(first, 'Magic priorities', ['Blood Magic: Fifty Papercuts']);
    await preset.selectOption({ label });
    await expect(preset).toHaveValue(globalPresets[index].Id);
    expect((await saved(page)).Stages[0]).toMatchObject({ Name: label, PresetId: globalPresets[index].Id, GearGoal: 9, BlackBarLookaheadSeconds: 37, FightSchedule: { Id: 'legacy-entry-v1-0' } });
    await readback(page);
    await expect(preset).toHaveValue(globalPresets[index].Id);
    await expect(first).toBeVisible();
    await expect(page.getByRole('button', { name: `Edit stage 1: ${label}`, exact: true })).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByRole('button', { name: `Edit stage 1: ${label}`, exact: true })).toHaveAttribute('draggable', 'false');
    await expect(first.getByRole('checkbox', { name: 'Fights enabled', exact: true })).toBeChecked();
    await openSchedule(page);
    await expect(page.getByRole('combobox', { name: 'Repeat from', exact: true }).locator('option').first()).toHaveText('Do not repeat');
    await expect(page.getByRole('combobox', { name: 'Repeat from', exact: true }).locator('option').nth(1)).toHaveText(`1: ${label}`);
    expect((await saved(page)).Stages[0]).toMatchObject({ Percent: .2, Future: 'A', GearGoal: 9, BlackBarLookaheadSeconds: 37, FightSchedule: { Id: 'legacy-entry-v1-0' } });
    await expect(first.getByRole('heading', { name: label, exact: true })).toBeVisible();
    await expect(energy).toHaveCount(0);
    await expect(magic).toHaveCount(0);
    expect((await saved(page)).Stages[0]).toMatchObject({ EnergyPriorities: ['TM', 'NGU-0'], MagicPriorities: ['RIT-1'] });
    await preset.selectOption({ label });
    await readback(page);
    await expect(preset).toHaveValue(globalPresets[index].Id);
    expect((await saved(page)).Stages[0]).toMatchObject({ EnergyPriorities: ['TM', 'NGU-0'], MagicPriorities: ['RIT-1'] });
    await expect(page.getByRole('button', { name: `Edit stage 1: ${label}`, exact: true })).toHaveAttribute('aria-expanded', 'true');
  });
}

test('global selection preserves legacy orders while AT references retain timing and targets', async ({ page }) => {
  await mount(page);
  const row = await selectStage(page, 1);
  const completion = row.getByRole('combobox', { name: /^Finish stage by/ });
  await completion.selectOption('fixed');
  const preset = row.getByRole('combobox', { name: 'Focus preset', exact: true });
  const energy = row.getByRole('group', { name: 'Energy priorities', exact: true });
  const magic = row.getByRole('group', { name: 'Magic priorities', exact: true });
  await setPriorities(row, 'Energy priorities', ['Time Machine', 'NGU: Augments']);
  await setPriorities(row, 'Magic priorities', ['Blood Magic: Fifty Papercuts']);
  await expect(preset).toHaveValue('');
  await preset.selectOption({ label: 'AT Wandoos' });
  await expect(energy).toHaveCount(0);
  await expect(magic).toHaveCount(0);
  expect((await saved(page)).Stages[1]).toEqual({
    ...source.Stages[1], Name: 'AT Wandoos', PresetId: 'global_7', CompletionMode: 0, DurationSeconds: 300, DurationUnit: 's',
    EnergyPriorities: ['TM', 'NGU-0'], MagicPriorities: ['RIT-1']
  });
  await readback(page);
  await expect(preset).toHaveValue('global_7');
  expect((await saved(page)).Stages[1].Name).toBe('AT Wandoos');
  await preset.selectOption({ label: 'AT Wandoos' });
  await expect(preset).toHaveValue('global_7');
  await preset.selectOption({ label: 'AT Adventure' });
  await expect(energy).toHaveCount(0);
  await expect(magic).toHaveCount(0);
  await expect(preset).toHaveValue('global_6');
  await expect(row.getByRole('textbox', { name: 'Duration', exact: true })).toHaveValue('5m');
  await completion.selectOption('target');
  await expect(row.getByRole('textbox', { name: 'AT power target', exact: true })).toHaveValue('9223372036854775806');
  await expect(row.getByRole('textbox', { name: 'AT toughness target', exact: true })).toHaveValue('6000');
  expect((await saved(page)).Stages[1]).toMatchObject({ Name: 'AT Adventure', CompletionMode: 1, Percent: .3, Future: 'B' });
});

test('AT target completion rejects incompatible presets without saving an edit', async ({ page }) => {
  await mount(page);
  const before = await saved(page);
  const row = await selectStage(page, 1);
  const preset = row.getByRole('combobox', { name: 'Focus preset', exact: true });
  for (const label of ['Time Machine', 'Augments + Blood', 'Wandoos', 'Energy NGU', 'Magic NGU', 'All NGU', 'AT Wandoos']) {
    await preset.selectOption({ label });
    await expect(page.getByRole('alert')).toHaveText('AT targets require a saved global preset with Advanced Training weight and positive toughness/power ratios. Choose a compatible preset first.');
    await expect(preset).toHaveValue('');
    await expect(row.getByRole('textbox', { name: 'AT power target', exact: true })).toHaveValue('9223372036854775806');
    await expect(row.getByRole('textbox', { name: 'AT toughness target', exact: true })).toHaveValue('6000');
    expect(await saved(page)).toEqual(before);
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
  }
});

test('switching to AT completion requires compatible weights and cancels an invalid duration draft', async ({ page }) => {
  await mount(page);
  const row = await selectStage(page, 1);
  const completion = row.getByRole('combobox', { name: /^Finish stage by/ });
  await completion.selectOption('fixed');
  const preset = row.getByRole('combobox', { name: 'Focus preset', exact: true });
  await preset.selectOption({ label: 'AT Wandoos' });
  const duration = row.getByRole('textbox', { name: 'Duration', exact: true });
  await duration.fill('0s');
  await duration.press('Tab');
  await expect(page.getByRole('alert')).toContainText('Fixed duration seconds must be');
  await expect(duration).toHaveValue('0s');
  await completion.selectOption('target');
  await expect(page.getByRole('alert').filter({ hasText: 'AT targets require a saved global preset' })).toBeVisible();
  await expect(row.getByRole('alert')).toContainText('Fixed duration seconds must be');
  await expect(completion).toHaveValue('fixed');
  await expect(duration).toHaveValue('0s');
  await preset.selectOption('global_6');
  await completion.selectOption('target');
  await expect(duration).toHaveCount(0);
  await expect(preset).toHaveValue('global_6');
  await expect(row.getByRole('group', { name: 'Energy priorities', exact: true })).toHaveCount(0);
  await expect(row.getByRole('group', { name: 'Magic priorities', exact: true })).toHaveCount(0);
  await expect(row.getByRole('textbox', { name: 'AT power target', exact: true })).toHaveValue('9223372036854775806');
  await expect(row.getByRole('textbox', { name: 'AT toughness target', exact: true })).toHaveValue('6000');
  await expect(page.getByRole('alert')).toHaveCount(0);
  expect((await saved(page)).Stages[1]).toEqual({
    ...source.Stages[1], Name: 'AT Adventure', PresetId: 'global_6', DurationSeconds: 300, DurationUnit: 's'
  });
  await expect(page.getByRole('combobox', { name: 'Stage timing', exact: true })).toHaveCount(0);
  await expectDurations(page, ['4m', '16m']);
  await expect(page.getByRole('alert')).toHaveCount(0);
  await selectStage(page, 1);
  await completion.selectOption('fixed');
  await expect(duration).toHaveValue('5m');
  expect((await saved(page)).Stages.map(row => row.DurationSeconds)).toEqual([240, 300, 960]);
});

for (const dormantSeconds of [undefined, 420]) {
  for (const kind of ['fixed', 'flexible']) {
    test(`AT row returning to ${kind} timing ${dormantSeconds ? 'retains dormant configuration' : 'uses the row default'} without changing the budget`, async ({ page }) => {
      await mount(page, { ...source, Stages: source.Stages.map((row, index) => index === 1 && dormantSeconds ? { ...row, DurationSeconds: dormantSeconds } : row) });
      const before = await saved(page);
      const row = await selectStage(page, 1);
      await row.getByRole('combobox', { name: /^Finish stage by/ }).selectOption(kind);
      const seconds = kind === 'fixed' ? dormantSeconds ?? 300 : 0;
      if (kind === 'fixed') await expect(row.getByRole('textbox', { name: 'Duration', exact: true })).toHaveValue(dormantSeconds ? '7m' : '5m');
      else {
        await expect(row.getByRole('textbox', { name: 'Flexible weight', exact: true })).toHaveValue('0.3');
        await expect(row.getByText('100% of remaining time.', { exact: false })).toBeVisible();
      }
      const next = await saved(page);
      expect(next.Stages[1]).toEqual({ ...source.Stages[1], CompletionMode: 0, DurationSeconds: seconds, DurationUnit: kind === 'fixed' ? 's' : '%' });
      expect([next.Stages[0], next.Stages[2]]).toEqual([before.Stages[0], before.Stages[2]]);
      expect(next.StageDurationSeconds).toBe(1200);
      expect(next.StageLoopStartIndex).toBe(1);
      expect(next.Stages.map(row => row.DurationSeconds)).toEqual([240, seconds, 960]);
      await expect(page.getByRole('alert')).toHaveCount(0);
    });
  }
}

for (const scope of ['namespace', 'name', 'plan.Name']) {
  test(`invalid schedule draft is cleared after ${scope} changes and cannot leak on blur`, async ({ page }) => {
    await mount(page);
    await openSchedule(page);
    const schedule = page.getByRole('textbox', { name: 'Fallback time budget', exact: true });
    await expect(schedule).toHaveValue('20m');
    await schedule.fill('invalid');
    await schedule.press('Enter');
    await expect(page.getByRole('alert')).toHaveText('Enter a duration such as 25m, 2h, or 30%.');
    await expect(schedule).toHaveValue('invalid');
    if (scope === 'plan.Name') await readback(page, 'name');
    else await page.evaluate(scope => {
      const state = window as unknown as { __stageNamespace: (name: string) => void; __stageName: (name: string) => void };
      if (scope === 'namespace') state.__stageNamespace('folder-b');
      else state.__stageName('other-plan');
    }, scope);
    await expect(schedule).toHaveValue('20m');
    await expect(page.getByRole('alert')).toHaveCount(0);
    await schedule.press('Tab');
    await schedule.dispatchEvent('blur');
    await expect(schedule).toHaveValue('20m');
    await expect(page.getByRole('alert')).toHaveCount(0);
    expect((await saved(page)).StageDurationSeconds).toBe(1200);
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
  });
}

test('row timing changes preserve other fixed rows and AT targets without a bulk conversion', async ({ page }) => {
  await mount(page);
  await expect(page.getByRole('combobox', { name: 'Stage timing', exact: true })).toHaveCount(0);
  await expectDurations(page, ['4m', '16m']);
  const panel = await selectStage(page, 0);
  const finish = panel.getByRole('combobox', { name: /^Finish stage by/ });
  await finish.selectOption('flexible');
  await expect(panel.getByRole('textbox', { name: 'Flexible weight', exact: true })).toHaveValue('0.2');
  await expect(panel.getByText('100% of remaining time.', { exact: false })).toBeVisible();
  expect((await saved(page)).Stages.slice(1)).toEqual(source.Stages.slice(1));
  await finish.selectOption('fixed');
  await expectDurations(page, ['4m', '16m']);
  const next = await saved(page);
  expect(next.Stages.map(row => row.DurationSeconds)).toEqual([240, undefined, 960]);
  expect(next.Stages[1]).toEqual(source.Stages[1]);
  expect(next.StageDurationSeconds).toBe(1200);
  expect(next.StageLoopStartIndex).toBe(1);
});

test('time sequence uses inline suffixes and bare numbers follow the displayed unit', async ({ page }) => {
  await mount(page);
  const second = await selectStage(page, 1);
  await second.getByRole('combobox', { name: /^Finish stage by/ }).selectOption('fixed');
  for (const [index, text] of ['25m', '35m', '2h'].entries()) {
    const duration = (await selectStage(page, index)).getByRole('textbox', { name: 'Duration', exact: true });
    await duration.fill(text);
    await duration.press('Enter');
  }
  await expectDurations(page, ['25m', '35m', '2h']);
  expect((await saved(page)).Stages.map(row => row.DurationSeconds)).toEqual([1500, 2100, 7200]);
  await expect(page.getByRole('textbox', { name: 'Stage name', exact: true })).toHaveCount(0);
  await expect(page.getByRole('combobox', { name: 'Duration unit', exact: true })).toHaveCount(0);
  await expect(page.getByRole('spinbutton', { name: 'Duration', exact: true })).toHaveCount(0);
  await openSchedule(page);
  await expectStageEstimates(page, ['25m fixed', '35m fixed', '2h fixed']);
  await expect(page.locator('.schedule-card').getByRole('status')).toHaveText('Fixed stages exceed budget by 160m. Increase the budget or shorten fixed stages.');
  expect((await saved(page)).StageDurationSeconds).toBe(1200);
  await readback(page);
  await expectDurations(page, ['25m', '35m', '2h']);
  for (const [index, text] of ['30', '40', '3'].entries()) {
    const duration = (await selectStage(page, index)).getByRole('textbox', { name: 'Duration', exact: true });
    await duration.fill(text);
    await duration.press('Enter');
  }
  await expectDurations(page, ['30m', '40m', '3h']);
  expect((await saved(page)).Stages.map(row => row.DurationSeconds)).toEqual([1800, 2400, 10800]);
});

test('collapsed stage bar opens one full settings panel and fits desktop and mobile', async ({ page }) => {
  await mount(page);
  const bar = page.getByRole('list', { name: 'Plan stages', exact: true });
  const segments = bar.locator('.bar-segment');
  await expect(segments).toHaveCount(3);
  for (const button of await bar.locator('.stage-select').all()) await expect(button).toHaveAttribute('aria-expanded', 'false');
  await expect(page.locator('.stage-panel')).toHaveCount(3);
  await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
  await expect(page.locator('.stage-table, .stage-row')).toHaveCount(0);
  await expect(page.locator('details.schedule-settings')).not.toHaveAttribute('open');
  await expect(page.getByText('Rebirth history', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Rebirth history table', exact: true })).toHaveCount(0);
  await expectStageEstimates(page, ['4m fixed', 'AT targets', '16m fixed']);
  await expect(page.getByRole('article', { name: 'Rebirth conditions', exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Active preset', exact: true })).toBeVisible();
  const first = await selectStage(page, 0);
  await expect(first.getByRole('combobox', { name: 'Focus preset', exact: true })).toBeVisible();
  await expect(first.getByRole('combobox', { name: 'Focus preset', exact: true }).locator('option')).toHaveText(['Existing settings', 'Use plan active preset: Energy NGU', ...globalPresets.map(row => row.Name)]);
  await expect(first.getByText("Choosing a preset replaces this stage's allocation and gear.", { exact: true })).toBeVisible();
  await expect(first.getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('');
  await expect(first.getByRole('combobox', { name: /^Finish stage by/ })).toBeVisible();
  await expect(first.getByRole('textbox', { name: 'Duration', exact: true })).toBeVisible();
  await expect(first.getByRole('combobox', { name: 'Energy focus', exact: true })).toBeVisible();
  await expect(first.getByRole('combobox', { name: 'Magic focus', exact: true })).toBeVisible();
  await expect(page.locator('.stage-panel select[aria-label="Gear goal"]')).toHaveCount(0);
  await expect(first.getByRole('group', { name: 'Energy priorities', exact: true })).toBeVisible();
  await expect(first.getByRole('group', { name: 'Magic priorities', exact: true })).toBeVisible();
  await expect(first.getByRole('checkbox', { name: 'Fights enabled', exact: true })).toBeVisible();
  await expect(page.getByRole('spinbutton', { name: 'Black-bar lookahead (s)', includeHidden: true })).toHaveCount(0);
  await expect(page.getByText('Cap buffer (seconds)', { exact: false })).toHaveCount(0);
  const history = first.locator('details').filter({ has: page.getByText('Runtime history', { exact: true }) });
  await expect(history).not.toHaveAttribute('open');
  const historyText = history.getByLabel('Stage runtime history', { exact: true });
  await expect(historyText).toBeHidden();
  await history.locator('summary').focus();
  await history.locator('summary').press('Enter');
  await expect(history).toHaveAttribute('open');
  await expect(historyText).toBeVisible();
  await expect(historyText).toContainText('Fixed duration');
  await expect(historyText).toContainText('Actual applied: Not recorded');
  await history.locator('summary').press('Enter');
  await expect(historyText).toBeHidden();
  await expect(first.locator('[aria-haspopup="menu"]')).toHaveCount(0);
  await expect(segments.first().locator('.segment-duration')).toHaveText('4m fixed');
  await expect(segments.first().locator('.segment-duration')).toHaveAttribute('title', 'Fixed duration');
  await expect(page.getByRole('button', { name: /^Move (earlier|later)$/, includeHidden: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /^Reorder stage/, includeHidden: true })).toHaveCount(0);
  await expect(bar.locator('.handle, [data-handle]')).toHaveCount(0);
  await expect(first.getByRole('button', { name: 'Repeat from here', exact: true })).toHaveAttribute('aria-pressed', 'false');
  expect(await segments.first().getAttribute('draggable')).toBeNull();
  await expect(segments.first().getByRole('button', { name: /^Edit stage/ })).toHaveAttribute('draggable', 'false');
  const second = await selectStage(page, 1);
  await expect(first).toBeHidden();
  await expect(second.getByRole('textbox', { name: 'AT power target', exact: true })).toHaveValue('9223372036854775806');
  await expect(second.getByRole('textbox', { name: 'AT toughness target', exact: true })).toBeVisible();
  await expect(second.getByRole('textbox', { name: 'Duration', exact: true })).toHaveCount(0);
  await expect(segments.nth(1).getByText('AT targets', { exact: true })).toBeVisible();
  await expect(segments.nth(1).locator('.segment-duration')).toHaveAttribute('title', 'Mandatory AT targets');
  await expect(second.getByRole('button', { name: 'Repeat from here', exact: true })).toHaveAttribute('aria-pressed', 'true');
  const barBox = (await bar.boundingBox())!;
  const panelBox = (await second.boundingBox())!;
  expect(panelBox.y).toBeGreaterThanOrEqual(barBox.y + barBox.height);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const segmentBoxes = await segments.evaluateAll(elements => elements.map(element => {
    const { x, y } = element.getBoundingClientRect();
    return { x, y };
  }));
  expect(segmentBoxes.every(box => box.y === segmentBoxes[0].y)).toBe(true);
  expect(segmentBoxes[1].x).toBeGreaterThan(segmentBoxes[0].x);
  await page.screenshot({ path: test.info().outputPath('stage-bar-expanded.png'), fullPage: true });
  await second.getByRole('button', { name: 'Close stage settings', exact: true }).click();
  await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
  await expect(segments.nth(1).getByRole('button', { name: 'Edit stage 2: AT Adventure', exact: true })).toBeFocused();
  await expect(segments.nth(1).locator('.stage-select')).toHaveAttribute('aria-expanded', 'false');
});

test('keyboard selection opens stages without changing the plan', async ({ page }) => {
  await mount(page);
  const before = await saved(page);
  const first = page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true });
  await first.focus();
  await first.press('ArrowRight');
  await expect(page.getByRole('region', { name: 'Stage 2 settings', exact: true })).toBeVisible();
  const second = page.getByRole('button', { name: 'Edit stage 2: AT Adventure', exact: true });
  await expect(second).toBeFocused();
  await second.press('End');
  const last = page.getByRole('button', { name: 'Edit stage 3: Energy NGU', exact: true });
  await expect(last).toBeFocused();
  await expect(page.getByRole('region', { name: 'Stage 3 settings', exact: true })).toBeVisible();
  await last.press('ArrowLeft');
  await expect(second).toBeFocused();
  await second.press('Home');
  await expect(first).toBeFocused();
  await expect(page.getByRole('region', { name: 'Stage 1 settings', exact: true })).toBeVisible();
  await expect(page.locator('.stage-panel:visible')).toHaveCount(1);
  expect(await saved(page)).toEqual(before);
  expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
});

for (const kind of ['fixed', 'flexible']) test(`adding a stage to ${kind} rows inherits the active preset and opens its settings`, async ({ page }) => {
  await mount(page, kind === 'fixed' ? source : { ...source, Stages: source.Stages.map(row => ({ ...row, DurationSeconds: 0 })) });
  const before = await saved(page);
  const previous = await selectStage(page, 1);
  await page.getByRole('button', { name: 'Add stage', exact: true }).click();
  await expect(page.locator('.bar-segment')).toHaveCount(4);
  await expect(page.getByRole('button', { name: 'Edit stage 4: Plan active preset', exact: true })).toHaveAttribute('aria-expanded', 'true');
  const panel = page.getByRole('region', { name: 'Stage 4 settings', exact: true });
  await expect(panel).toBeVisible();
  await expect(previous).toBeHidden();
  await expect(page.locator('.stage-panel:visible')).toHaveCount(1);
  await expect(panel.getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('@active');
  expect((await saved(page)).Stages.slice(0, 3)).toEqual(before.Stages);
  expect((await saved(page)).Stages[3]).toEqual({
    Name: 'Weighted preset', PresetId: '@active', ExecutionMode: 0, Percent: 10, CompletionMode: 0, DurationSeconds: 300, DurationUnit: 'min',
    TargetAdvancedTrainingPower: '0', TargetAdvancedTrainingToughness: '0', BlackBarLookaheadSeconds: 5, FightSchedule: null
  });
  await expect(panel.getByRole('group', { name: 'Energy priorities', exact: true })).toHaveCount(0);
  await expect(panel.getByRole('group', { name: 'Magic priorities', exact: true })).toHaveCount(0);
  expect((await saved(page)).StageDurationSeconds).toBe(before.StageDurationSeconds);
});

test('direct mouse dragging retains selection and repeat follows the selected stage', async ({ page }) => {
  await mount(page);
  const panel = await selectStage(page, 1);
  const key = await panel.getAttribute('data-stage');
  const selected = page.locator(`.stage-panel[data-stage="${key}"]`);
  await dragStage(page, 1, 0);
  await expect(selected).toHaveAccessibleName('Stage 1 settings');
  await expect(selected).toBeVisible();
  await expect(page.locator('.stage-panel:visible')).toHaveCount(1);
  expect((await saved(page)).Stages.map(row => row.Future)).toEqual(['B', 'A', 'C']);
  expect((await saved(page)).StageLoopStartIndex).toBe(0);
  await dragStage(page, 0, 1);
  await expect(selected).toHaveAccessibleName('Stage 2 settings');
  await expect(page.locator(`.bar-segment[data-stage="${key}"] .stage-select`)).toHaveAttribute('aria-expanded', 'true');
  expect((await saved(page)).Stages).toEqual(source.Stages);
  expect((await saved(page)).StageLoopStartIndex).toBe(1);
  const repeat = selected.getByRole('button', { name: 'Repeat from here', exact: true });
  await repeat.click();
  await expect(repeat).toHaveAttribute('aria-pressed', 'false');
  await expect(page.getByLabel('Loop start', { exact: true })).toHaveCount(0);
  expect((await saved(page)).StageLoopStartIndex).toBe(-1);
  await repeat.click();
  await expect(repeat).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator(`.bar-segment[data-stage="${key}"]`).getByLabel('Loop start', { exact: true })).toBeVisible();
  expect((await saved(page)).StageLoopStartIndex).toBe(1);
});

for (const [forward, backward] of [['ArrowRight', 'ArrowLeft'], ['ArrowDown', 'ArrowUp']]) {
  test(`Alt+${forward}/${backward} move the same focused row and keep the loop attached`, async ({ page }) => {
    await mount(page);
    await selectStage(page, 1);
    const segment = page.getByRole('button', { name: 'Edit stage 2: AT Adventure', exact: true });
    const key = await segment.getAttribute('data-select');
    for (const arrow of [forward, backward]) await expect(segment).toHaveAttribute('aria-keyshortcuts', new RegExp(`Alt\\+${arrow}`));
    await segment.focus();
    await segment.press(`Alt+${forward}`);
    const moved = page.locator(`[data-select="${key}"]`);
    await expect(moved).toBeFocused();
    await expect(moved).toHaveAccessibleName('Edit stage 3: AT Adventure');
    expect((await saved(page)).Stages.map(row => row.Name)).toEqual(['Opener', 'Sustain', 'Training']);
    expect((await saved(page)).StageLoopStartIndex).toBe(2);
    await moved.press(`Alt+${forward}`);
    expect((await saved(page)).StageLoopStartIndex).toBe(2);
    await expect(page.getByRole('region', { name: 'Stage 3 settings', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Edit stage 3: AT Adventure', exact: true })).toHaveAttribute('aria-expanded', 'true');
    await page.getByRole('textbox', { name: 'AT power target' }).fill('9223372036854775805');
    expect((await saved(page)).Stages[2].TargetAdvancedTrainingPower).toBe('9223372036854775805');
    expect((await saved(page)).Stages[1].Future).toBe('C');
    await expect(moved).toHaveCount(1);
    await moved.press(`Alt+${backward}`);
    await expect(moved).toBeFocused();
    await expect(moved).toHaveAccessibleName('Edit stage 2: AT Adventure');
    await expect(page.getByRole('region', { name: 'Stage 2 settings', exact: true })).toBeVisible();
    expect((await saved(page)).Stages.map(row => row.Future)).toEqual(['A', 'B', 'C']);
    expect((await saved(page)).Stages[1].TargetAdvancedTrainingPower).toBe('9223372036854775805');
    expect((await saved(page)).StageLoopStartIndex).toBe(1);
  });
}

for (const selection of ['closed', 'other stage'] as const) {
  test(`direct mouse drag reorders with ${selection === 'closed' ? 'all panels closed' : 'another stage selected'} without toggling a panel`, async ({ page }) => {
    await mount(page);
    const panel = selection === 'other stage' ? await selectStage(page, 0) : null;
    const selectedKey = await panel?.getAttribute('data-stage');
    await dragStage(page, 1, 0);
    await expect(page.locator('.bar-segment').first().getByRole('button', { name: 'Edit stage 1: AT Adventure', exact: true })).toHaveAttribute('aria-expanded', 'false');
    await expect(page.locator('.stage-panel:visible')).toHaveCount(panel ? 1 : 0);
    if (panel) {
      const selected = page.locator(`.stage-panel[data-stage="${selectedKey}"]`);
      await expect(selected).toBeVisible();
      await expect(selected).toHaveAccessibleName('Stage 2 settings');
      await expect(page.locator(`[data-select="${selectedKey}"]`)).toHaveAttribute('aria-expanded', 'true');
    }
    expect((await saved(page)).StageLoopStartIndex).toBe(0);
    expect((await saved(page)).Stages).toEqual([source.Stages[1], source.Stages[0], source.Stages[2]]);
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(1);
  });
}

test('mouse pointer drag reorders when HTML5 drag events are blocked', async ({ page }) => {
  await mount(page);
  await page.evaluate(() => {
    const counts = { dragstart: 0, dragover: 0, drop: 0, dragend: 0 };
    Object.assign(window, { __htmlDragEvents: counts });
    for (const type of ['dragstart', 'dragover', 'drop', 'dragend'] as const) {
      window.addEventListener(type, event => {
        counts[type]++;
        event.preventDefault();
        event.stopImmediatePropagation();
      }, { capture: true });
    }
  });
  for (const button of await page.locator('.stage-select').all()) await expect(button).toHaveAttribute('draggable', 'false');
  await dragStage(page, 1, 0);
  expect((await saved(page)).Stages).toEqual([source.Stages[1], source.Stages[0], source.Stages[2]]);
  expect((await saved(page)).StageLoopStartIndex).toBe(0);
  expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(1);
  expect(await page.evaluate(() => (window as unknown as { __htmlDragEvents: Record<string, number> }).__htmlDragEvents)).toEqual({ dragstart: 0, dragover: 0, drop: 0, dragend: 0 });
  await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
});

for (const invalidation of ['escape', 'namespace', 'replacement'] as const) {
  test(`drag cancellation on ${invalidation} cannot reorder a stale plan`, async ({ page }) => {
    await mount(page);
    const segment = page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true });
    const target = page.locator('.bar-segment').last();
    await segment.scrollIntoViewIfNeeded();
    await target.scrollIntoViewIfNeeded();
    const start = (await segment.boundingBox())!;
    const box = (await target.boundingBox())!;
    await page.mouse.move(start.x + start.width / 2, start.y + start.height / 2);
    await page.mouse.down();
    await page.mouse.move(start.x + start.width / 2 + 12, start.y + start.height / 2, { steps: 3 });
    await page.mouse.move(box.x + box.width - 12, box.y + box.height / 2, { steps: 12 });
    await expect(page.locator('.bar-segment.dragging')).toHaveCount(1);
    await expect(target).toHaveClass(/insert-after/);
    if (invalidation === 'escape') await page.keyboard.press('Escape');
    else await page.evaluate(kind => {
      const state = window as unknown as { __stageNamespace: (name: string) => void; __replaceStages: () => void };
      if (kind === 'namespace') state.__stageNamespace('folder-b');
      else state.__replaceStages();
    }, invalidation);
    await expect(page.locator('.dragging, .insert-before, .insert-after')).toHaveCount(0);
    await page.mouse.up();
    await expect(page.locator('.dragging, .insert-before, .insert-after')).toHaveCount(0);
    expect((await saved(page)).Stages).toEqual(source.Stages);
    expect((await saved(page)).StageLoopStartIndex).toBe(1);
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
    await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
  });
}

test('touch segment reorders through native pointer events without opening its panel', async ({ page, context }) => {
  await mount(page);
  const segment = page.getByRole('button', { name: 'Edit stage 1: Time Machine' });
  await segment.scrollIntoViewIfNeeded();
  const start = (await segment.boundingBox())!;
  const target = (await page.locator('.bar-segment').nth(1).boundingBox())!;
  const session = await context.newCDPSession(page);
  const y = start.y + start.height / 2;
  await session.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: start.x + start.width / 2, y }] });
  await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: target.x + target.width - 2, y }] });
  await session.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  await expect(page.locator('.bar-segment').nth(1).getByRole('button', { name: 'Edit stage 2: Time Machine', exact: true })).toHaveAttribute('aria-expanded', 'false');
  await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
  expect((await saved(page)).Stages.map(row => row.Name)).toEqual(['Training', 'Opener', 'Sustain']);
  expect((await saved(page)).StageLoopStartIndex).toBe(0);
  await session.detach();
});

test('one normal click opens a stage and a second click closes it without editing', async ({ page }) => {
  await mount(page);
  const segment = page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true });
  await segment.click();
  await expect(segment).toHaveAttribute('aria-expanded', 'true');
  await expect(page.getByRole('region', { name: 'Stage 1 settings', exact: true })).toBeVisible();
  await expect(page.locator('.stage-panel:visible')).toHaveCount(1);
  await segment.click();
  await expect(segment).toHaveAttribute('aria-expanded', 'false');
  await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
  expect((await saved(page)).Stages).toEqual(source.Stages);
  expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
});

for (const movement of [0, 3]) {
  test(`touch ${movement === 0 ? 'tap' : 'tiny movement'} toggles once without reordering`, async ({ page, context }) => {
    await mount(page);
    const segment = page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true });
    const session = await context.newCDPSession(page);
    try {
      for (const expanded of [true, false]) {
        await segment.scrollIntoViewIfNeeded();
        const start = (await segment.boundingBox())!;
        const x = start.x + start.width / 2;
        const y = start.y + start.height / 2;
        await session.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y }] });
        if (movement) await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: x + movement, y: y + 1 }] });
        await expect(page.locator('.dragging, .insert-before, .insert-after')).toHaveCount(0);
        await session.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        await expect(segment).toHaveAttribute('aria-expanded', String(expanded));
        await expect(page.locator('.stage-panel:visible')).toHaveCount(expanded ? 1 : 0);
        expect((await saved(page)).Stages).toEqual(source.Stages);
        expect((await saved(page)).StageLoopStartIndex).toBe(1);
        expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
      }
    } finally {
      await session.detach();
    }
  });
}

test('vertical touch gestures scroll the page instead of dragging or toggling a stage', async ({ page, context }, info) => {
  test.skip(info.project.name !== 'mobile', 'Vertical scrolling is a mobile regression.');
  await mount(page);
  const panel = await selectStage(page, 0);
  const segment = page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true });
  await segment.scrollIntoViewIfNeeded();
  const start = (await segment.boundingBox())!;
  const x = start.x + start.width / 2;
  const y = start.y + start.height / 2;
  const before = await page.evaluate(() => scrollY);
  const session = await context.newCDPSession(page);
  try {
    await session.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y }] });
    for (const delta of [12, 28, 44, 60]) {
      await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: x + 1, y: y - delta }] });
    }
    await session.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
    await expect.poll(() => page.evaluate(() => scrollY)).toBeGreaterThan(before);
    await expect(page.locator('.dragging, .insert-before, .insert-after')).toHaveCount(0);
    await expect(segment).toHaveAttribute('aria-expanded', 'true');
    await expect(panel).toBeVisible();
    await expect(page.locator('.stage-panel:visible')).toHaveCount(1);
    expect((await saved(page)).Stages).toEqual(source.Stages);
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
  } finally {
    await session.detach();
  }
});

test('touch cancellation clears horizontal insertion without saving a reorder', async ({ page, context }) => {
  await mount(page);
  const segment = page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true });
  const target = page.locator('.bar-segment').nth(1);
  await segment.scrollIntoViewIfNeeded();
  const start = (await segment.boundingBox())!;
  const destination = (await target.boundingBox())!;
  const y = start.y + start.height / 2;
  const session = await context.newCDPSession(page);
  await session.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: start.x + start.width / 2, y }] });
  await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: destination.x + destination.width - 2, y }] });
  await expect(page.locator('.bar-segment.dragging')).toHaveCount(1);
  await expect(page.locator('.insert-before, .insert-after')).toHaveCount(1);
  await session.send('Input.dispatchTouchEvent', { type: 'touchCancel', touchPoints: [] });
  await expect(page.locator('.dragging, .insert-before, .insert-after')).toHaveCount(0);
  expect((await saved(page)).Stages).toEqual(source.Stages);
  expect((await saved(page)).StageLoopStartIndex).toBe(1);
  expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
  await session.detach();
});

for (const ending of ['release', 'touchCancel', 'Escape'] as const) {
  test(`edge-held touch ${ending} ${ending === 'release' ? 'inserts at the scrolled logical boundary' : 'stops continuous scrolling without saving'}`, async ({ page, context }, info) => {
    test.skip(info.project.name !== 'mobile', 'Held-edge overflow is a mobile regression.');
    const many = { ...source, StageDurationSeconds: 960, StageLoopStartIndex: 0, Stages: Array.from({ length: 16 }, (_, index) => ({
      ...source.Stages[0], Name: `Stage ${index + 1}`, Percent: 1, DurationSeconds: 60, Future: `stage-${index + 1}`
    })) };
    await mount(page, many);
    const before = await saved(page);
    const scroll = page.locator('.bar-scroll');
    const segment = page.getByRole('button', { name: 'Edit stage 1: Time Machine', exact: true });
    await segment.scrollIntoViewIfNeeded();
    const start = (await segment.boundingBox())!;
    const bounds = (await scroll.boundingBox())!;
    const x = bounds.x + bounds.width - 8;
    const y = start.y + start.height / 2;
    expect(await scroll.evaluate(element => element.scrollWidth - element.clientWidth)).toBeGreaterThan(1000);
    await page.clock.install({ time: new Date('2026-10-14T12:00:00Z') });
    await page.clock.pauseAt(new Date('2026-10-14T12:00:01Z'));
    const session = await context.newCDPSession(page);
    try {
      await session.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: start.x + start.width / 2, y }] });
      await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x, y }] });
      await expect(page.locator('.bar-segment.dragging')).toHaveCount(1);
      const initial = await scroll.evaluate(element => element.scrollLeft);
      await page.clock.runFor(160);
      const firstHold = await scroll.evaluate(element => element.scrollLeft);
      expect(firstHold).toBeGreaterThan(initial + 60);
      // No further pointermove: the animation loop must keep scrolling by itself.
      await page.clock.runFor(480);
      const secondHold = await scroll.evaluate(element => element.scrollLeft);
      expect(secondHold).toBeGreaterThan(firstHold + 200);
      const boundary = await page.locator('.bar-segment').evaluateAll((segments, x) => {
        const index = segments.findIndex(segment => {
          const rect = segment.getBoundingClientRect();
          return x < rect.x + rect.width / 2;
        });
        return index < 0 ? segments.length : index;
      }, x);
      expect(boundary).toBeGreaterThan(4);
      await expect(page.locator('.bar-segment').nth(boundary)).toHaveClass(/insert-before/);
      if (ending === 'release') {
        // A scroll after the last animation frame must also be accounted for on release.
        await scroll.evaluate(element => { element.scrollLeft += 120; });
        const releaseBoundary = await page.locator('.bar-segment').evaluateAll((segments, x) => {
          const index = segments.findIndex(segment => {
            const rect = segment.getBoundingClientRect();
            return x < rect.x + rect.width / 2;
          });
          return index < 0 ? segments.length : index;
        }, x);
        expect(releaseBoundary).toBeGreaterThan(boundary);
        await session.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        const expected = [...before.Stages];
        const moved = expected.shift()!;
        expected.splice(releaseBoundary - 1, 0, moved);
        expect((await saved(page)).Stages).toEqual(expected);
        expect((await saved(page)).StageLoopStartIndex).toBe(releaseBoundary - 1);
        expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(1);
      } else {
        if (ending === 'touchCancel') await session.send('Input.dispatchTouchEvent', { type: 'touchCancel', touchPoints: [] });
        else await page.keyboard.press('Escape');
      }
      await expect(page.locator('.dragging, .insert-before, .insert-after')).toHaveCount(0);
      const stopped = await scroll.evaluate(element => element.scrollLeft);
      expect(await scroll.evaluate(element => element.scrollWidth - element.clientWidth - element.scrollLeft)).toBeGreaterThan(200);
      await page.clock.runFor(320);
      expect(await scroll.evaluate(element => element.scrollLeft)).toBe(stopped);
      if (ending !== 'release') {
        if (ending === 'Escape') await session.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        expect(await saved(page)).toEqual(before);
        expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
      }
      await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
      await expect(page.locator('.stage-select[aria-expanded="true"]')).toHaveCount(0);
    } finally {
      await session.detach();
    }
  });
}

test('duration conversions, edits and priorities preserve unrelated lossless values', async ({ page }) => {
  await mount(page);
  const first = await selectStage(page, 0);
  const stageKey = await first.getAttribute('data-stage');
  const input = first.getByRole('textbox', { name: 'Duration', exact: true });
  await input.fill('240s');
  await input.press('Enter');
  await expect(input).toHaveValue('4m');
  await expect(page.getByRole('combobox', { name: 'Stage timing', exact: true })).toHaveCount(0);
  expect((await saved(page)).Stages[2].DurationSeconds).toBe(960);
  await input.fill('4.5');
  await input.press('Tab');
  await expect(input).toHaveValue('270s');
  expect((await saved(page)).StageDurationSeconds).toBe(1200);
  expect((await saved(page)).Stages[0].Percent).toBe(.2);
  await setPriorities(first, 'Energy priorities', ['Time Machine', 'NGU: Augments']);
  expect((await saved(page)).Stages[0].EnergyPriorities).toEqual(['TM', 'NGU-0']);
  expect(await first.getAttribute('data-stage')).toBe(stageKey);
  await first.getByRole('combobox', { name: /^Finish stage by/ }).selectOption('flexible');
  expect((await saved(page)).Stages[0].DurationSeconds).toBe(0);
  expect(await page.evaluate(() => (window as unknown as { __stagePlan: string }).__stagePlan)).toContain('"Future":9007199254740993');
  expect((await saved(page)).Stages[1].TargetAdvancedTrainingPower).toBe('9223372036854775806');
});

test('stage panel exposes completion mode and removes the loop boundary using its successor', async ({ page }) => {
  await mount(page);
  const panel = await selectStage(page, 1);
  await panel.getByRole('combobox', { name: /^Finish stage by/ }).selectOption('fixed');
  await expect(page.getByRole('textbox', { name: 'AT power target' })).toHaveCount(0);
  expect((await saved(page)).Stages[1].TargetAdvancedTrainingPower).toBe('9223372036854775806');
  await panel.getByRole('button', { name: 'Remove stage', exact: true }).click();
  expect((await saved(page)).Stages.map(row => row.Name)).toEqual(['Opener', 'Sustain']);
  expect((await saved(page)).StageLoopStartIndex).toBe(1);
});

for (const mode of ['normalized', 'reorder']) {
  test(`focused duration draft survives ${mode} autosave readback and commits to its logical row`, async ({ page }) => {
    await mount(page);
    await pauseDurationClock(page);
    const row = await selectStage(page, 0);
    await row.getByRole('combobox', { name: 'Focus preset', exact: true }).selectOption({ label: 'Time Machine' });
    await row.getByRole('textbox', { name: 'Duration', exact: true }).fill('240s');
    await row.getByRole('textbox', { name: 'Duration', exact: true }).press('Enter');
    const key = await row.getAttribute('data-stage');
    const input = page.locator(`.stage-panel[data-stage="${key}"]`).getByRole('textbox', { name: 'Duration', exact: true });
    const element = await input.elementHandle();
    await input.fill('90s');
    expect((await saved(page)).Stages[0].DurationSeconds).toBe(240);
    await readback(page, mode);
    await expect(input).toBeFocused();
    await expect(input).toHaveValue('90s');
    expect(await input.evaluate((current, previous) => current === previous, element)).toBe(true);
    const index = mode === 'reorder' ? 2 : 0;
    await expect(page.locator(`.bar-segment[data-stage="${key}"] .stage-select`)).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByRole('region', { name: `Stage ${index + 1} settings`, exact: true })).toBeVisible();
    await expect(page.locator('.stage-panel:visible')).toHaveCount(1);
    expect((await saved(page)).Stages[index].DurationSeconds).toBe(240);
    await input.press('Tab');
    expect((await saved(page)).Stages[index].DurationSeconds).toBe(90);
    await expect(input).toHaveValue('90s');
    expect((await saved(page)).Stages[index].Name).toBe('Time Machine');
    expect((await saved(page)).Stages.find(row => row.Name === 'Sustain')!.Percent).toBe(.8);
    expect((await saved(page)).StageDurationSeconds).toBe(1200);
    expect((await saved(page)).__externalUiRevision).toBe('b'.repeat(64));
    expect(await page.evaluate(() => (window as unknown as { __stagePlan: string }).__stagePlan)).toContain('"Future":9007199254740993');
    await element?.dispose();
  });
}

for (const mode of ['clone', 'normalized', 'unrelated field edit']) {
  test(`duplicate stages retain selection and invalid draft after ${mode}`, async ({ page }) => {
    const duplicates = { ...source, Stages: Array.from({ length: 4 }, () => ({ ...source.Stages[0] })) };
    await mount(page, duplicates, mode === 'unrelated field edit');
    const panel = await selectStage(page, 1);
    const keys = await page.locator('.bar-segment').evaluateAll(segments => segments.map(segment => segment.getAttribute('data-stage')));
    const selected = page.locator(`.stage-panel[data-stage="${keys[1]}"]`);
    const duration = selected.getByRole('textbox', { name: 'Duration', exact: true });
    const element = await duration.elementHandle();
    await duration.fill('0s');
    await duration.press('Tab');
    await expect(page.getByRole('alert')).toContainText('Fixed duration seconds must be');
    if (mode === 'unrelated field edit') {
      await page.getByRole('combobox', { name: 'Fight scoring', exact: true }).selectOption('1');
      await expect(page.getByRole('combobox', { name: 'Fight scoring', exact: true })).toHaveValue('1');
      expect(await saved(page)).toMatchObject({ FightScoreMode: 1 });
    } else await readback(page, mode);
    expect(await page.locator('.bar-segment').evaluateAll(segments => segments.map(segment => segment.getAttribute('data-stage')))).toEqual(keys);
    await expect(selected).toBeVisible();
    await expect(panel).toHaveAttribute('data-stage', keys[1]!);
    await expect(page.locator('.stage-panel:visible')).toHaveCount(1);
    await expect(page.locator(`.stage-select[data-select="${keys[1]}"]`)).toHaveAttribute('aria-expanded', 'true');
    await expect(duration).toHaveValue('0s');
    expect(await duration.evaluate((current, previous) => current === previous, element)).toBe(true);
    await expect(page.getByRole('alert')).toContainText('Fixed duration seconds must be');
    expect((await saved(page)).Stages.map(row => row.DurationSeconds ?? 0)).toEqual([240, 240, 240, 240]);
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(mode === 'unrelated field edit' ? 1 : 0);
    await selected.getByRole('button', { name: 'Close stage settings', exact: true }).click();
    await selectStage(page, 1);
    await expect(duration).toHaveValue('0s');
    await duration.fill('120s');
    await duration.press('Enter');
    expect((await saved(page)).Stages.map(row => row.DurationSeconds ?? 0)).toEqual([240, 120, 240, 240]);
    await element?.dispose();
  });
}

for (const [target, normalized] of [['006000', '6000'], ['009223372036854775806', '9223372036854775806']]) {
  test(`leading-zero AT target ${target} retains the selected panel through normalized readback`, async ({ page }) => {
    const plan = { ...source, Stages: source.Stages.map(row => ({ ...row })) };
    plan.Stages[1].TargetAdvancedTrainingPower = target;
    plan.Stages[1].TargetAdvancedTrainingToughness = '00006000';
    await mount(page, plan);
    const panel = await selectStage(page, 1);
    const key = await panel.getAttribute('data-stage');
    const selected = page.locator(`.stage-panel[data-stage="${key}"]`);
    const power = selected.getByRole('textbox', { name: 'AT power target', exact: true });
    const element = await power.elementHandle();
    await expect(power).toHaveValue(target);
    await readback(page, 'targets');
    await expect(selected).toBeVisible();
    await expect(page.locator('.stage-panel:visible')).toHaveCount(1);
    await expect(page.locator(`.stage-select[data-select="${key}"]`)).toHaveAttribute('aria-expanded', 'true');
    await expect(power).toHaveValue(normalized);
    await expect(selected.getByRole('textbox', { name: 'AT toughness target', exact: true })).toHaveValue('6000');
    expect(await power.evaluate((current, previous) => current === previous, element)).toBe(true);
    expect((await saved(page)).Stages[1]).toMatchObject({ TargetAdvancedTrainingPower: normalized, TargetAdvancedTrainingToughness: '6000', Future: 'B' });
    expect(await page.evaluate(() => (window as unknown as { __stagePlan: string }).__stagePlan)).toContain('"Future":9007199254740993');
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
    await expect(page.getByRole('alert')).toHaveCount(0);
    await element?.dispose();
  });
}

for (const mode of ['namespace', 'name', 'ambiguous']) {
  test(`duration draft cannot leak to a different stage after ${mode} changes`, async ({ page }) => {
    await mount(page);
    await pauseDurationClock(page);
    const row = await selectStage(page, 0);
    const input = row.getByRole('textbox', { name: 'Duration', exact: true });
    await input.fill('240s');
    await input.press('Enter');
    await input.fill('90s');
    const element = await input.elementHandle();
    if (mode === 'namespace') await page.evaluate(() => (window as unknown as { __stageNamespace: (value: string) => void }).__stageNamespace('folder-b'));
    else await readback(page, mode);
    await expect(page.getByRole('alert')).toContainText(mode === 'ambiguous' ? 'could not be identified safely' : 'plan changed');
    await expect(page.locator('.stage-panel:visible')).toHaveCount(0);
    await element!.evaluate(element => element.dispatchEvent(new FocusEvent('blur')));
    expect((await saved(page)).Stages[0].DurationSeconds).toBe(240);
    expect((await saved(page)).Stages.some(row => row.DurationSeconds === 90)).toBe(false);
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(1);
    if (mode === 'namespace') {
      const current = (await selectStage(page, 0)).getByRole('textbox', { name: 'Duration', exact: true });
      await current.fill('120s');
      await element!.evaluate(element => element.dispatchEvent(new FocusEvent('blur')));
      expect((await saved(page)).Stages[0].DurationSeconds).toBe(240);
      await expect(current).toHaveValue('120s');
      await current.press('Escape');
    }
    await element?.dispose();
  });
}

for (const invalid of [
  { text: '', message: 'Enter a duration such as 25m, 2h, or 30%.' },
  { text: '0s', message: 'Fixed duration seconds must be' },
  { text: '1.5s', message: 'whole seconds' },
  { text: '2147483648s', message: 'Fixed duration seconds must be' },
  { text: '90ms', message: 'Enter a duration such as 25m, 2h, or 30%.' }
]) test(`invalid duration ${JSON.stringify(invalid.text)} remains available across readback until explicitly cancelled`, async ({ page }) => {
  await mount(page);
  const row = await selectStage(page, 0);
  const input = row.getByRole('textbox', { name: 'Duration', exact: true });
  await input.fill('240s');
  await input.press('Enter');
  await input.fill(invalid.text);
  await input.press('Tab');
  await expect(page.getByRole('alert')).toContainText(invalid.message);
  await selectStage(page, 1);
  await expect(row).toBeHidden();
  await readback(page);
  await selectStage(page, 0);
  await expect(input).toHaveValue(invalid.text);
  expect((await saved(page)).Stages[0].DurationSeconds).toBe(240);
  await input.focus();
  await input.press('Escape');
  await expect(input).toHaveValue('4m');
  await expect(page.getByRole('alert')).toHaveCount(0);
  await input.press('Tab');
  expect((await saved(page)).Stages[0].DurationSeconds).toBe(240);
});

type DurationEditorKind = 'fixed' | 'flexible' | 'fallback' | 'rebirth';

async function autosaveDurationEditor(page: Page, kind: DurationEditorKind) {
  const plan = { ...source, TimeTriggerEnabled: kind === 'rebirth', TimeTriggerSeconds: 1200,
    Stages: source.Stages.map((row, index) => kind === 'flexible' && index === 0 ? { ...row, DurationSeconds: 0 } : row) };
  await mount(page, plan, true);
  if (kind === 'fixed' || kind === 'flexible') await selectStage(page, 0);
  if (kind === 'fallback') await openSchedule(page);
  await pauseDurationClock(page);
  const label = kind === 'fixed' ? 'Duration' : kind === 'flexible' ? 'Flexible weight' : kind === 'fallback' ? 'Fallback time budget' : 'Rebirth after';
  return page.getByRole('textbox', { name: label, exact: true });
}

function durationValue(plan: SavedPlan, kind: DurationEditorKind): number | undefined {
  return kind === 'fixed' ? plan.Stages[0].DurationSeconds : kind === 'flexible' ? plan.Stages[0].Percent : kind === 'fallback' ? plan.StageDurationSeconds : plan.TimeTriggerSeconds;
}

for (const kind of ['fixed', 'flexible', 'fallback', 'rebirth'] as const) {
  test(`duration autosave debounces ${kind} edits for 350ms without blur or Enter`, async ({ page }) => {
    const input = await autosaveDurationEditor(page, kind);
    const before = await saved(page);
    const element = await input.elementHandle();
    await input.fill(kind === 'flexible' ? '150' : '6m');
    await page.clock.runFor(200);
    await input.fill(kind === 'flexible' ? '175' : '7m');
    await page.clock.runFor(349);
    expect(durationValue(await saved(page), kind)).toBe(durationValue(before, kind));
    expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(0);
    expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageDraftDirty)).toBe(true);
    await page.clock.runFor(1);
    await expect.poll(async () => durationValue(await saved(page), kind)).toBe(kind === 'flexible' ? 175 : 420);
    expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(1);
    expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageDraftDirty)).toBe(false);
    await expect(input).toBeFocused();
    expect(await input.evaluate((current, previous) => current === previous, element)).toBe(true);
    expect(await page.evaluate(() => (window as unknown as { __stagePlan: string }).__stagePlan)).toContain('"Future":9007199254740993');
    await input.press('Tab');
    await page.clock.runFor(500);
    expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(1);
    await element?.dispose();
  });

  test(`duration autosave keeps incomplete ${kind} drafts local and Escape cancels pending work`, async ({ page }) => {
    const input = await autosaveDurationEditor(page, kind);
    const before = await saved(page);
    const original = await input.inputValue();
    for (const invalid of kind === 'flexible' ? ['', '1.', '1e', '0', '-1'] : ['', '1.', '1e', '1.5s', '2147483648s']) {
      await input.fill(invalid);
      await expect(input).toHaveAttribute('aria-invalid', 'true');
      await expect(page.getByRole('alert')).toBeVisible();
      await page.clock.runFor(500);
      expect(durationValue(await saved(page), kind)).toBe(durationValue(before, kind));
    }
    await input.fill(kind === 'flexible' ? '175' : '7m');
    await page.clock.runFor(200);
    await input.fill('.');
    await page.clock.runFor(500);
    expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(0);
    await input.press('Escape');
    await expect(input).toHaveValue(original);
    await expect(input).toHaveAttribute('aria-invalid', 'false');
    await expect(page.getByRole('alert')).toHaveCount(0);
    await input.fill(kind === 'flexible' ? '200' : '8m');
    await page.clock.runFor(200);
    await input.press('Escape');
    await page.clock.runFor(500);
    expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(0);
    expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageDraftDirty)).toBe(false);
    await input.fill(kind === 'flexible' ? '225' : '9m');
    await page.clock.runFor(350);
    await expect.poll(async () => durationValue(await saved(page), kind)).toBe(kind === 'flexible' ? 225 : 540);
  });

  test(`duration autosave retries a ready ${kind} draft once busy clears`, async ({ page }) => {
    const input = await autosaveDurationEditor(page, kind);
    await input.fill(kind === 'flexible' ? '175' : '7m');
    await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageLive({ disabled: true }));
    await page.clock.runFor(500);
    expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(0);
    await expect(input).toHaveValue(kind === 'flexible' ? '175' : '7m');
    await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageLive({ disabled: false }));
    await expect.poll(async () => durationValue(await saved(page), kind)).toBe(kind === 'flexible' ? 175 : 420);
    await page.clock.runFor(1000);
    expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(1);
  });

  for (const context of ['namespace', 'name', 'plan.Name', 'unmount'] as const) {
    test(`duration autosave cancels pending ${kind} edits after ${context} changes`, async ({ page }) => {
      const input = await autosaveDurationEditor(page, kind);
      await input.fill(kind === 'flexible' ? '175' : '7m');
      await page.clock.runFor(200);
      if (context === 'plan.Name') await readback(page, 'name');
      else await page.evaluate(context => {
        const fixture = window as unknown as TimingFixtureWindow & { __stageNamespace: (value: string) => void; __stageName: (value: string) => void };
        if (context === 'namespace') fixture.__stageNamespace('folder-b');
        if (context === 'name') fixture.__stageName('other-plan');
        if (context === 'unmount') fixture.__stageDestroy();
      }, context);
      await page.clock.runFor(1000);
      expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(0);
      expect(durationValue(await saved(page), kind)).toBe(kind === 'fixed' ? 240 : kind === 'flexible' ? .2 : 1200);
    });
  }
}

for (const kind of ['fixed', 'flexible'] as const) {
  test(`duration autosave follows the stable ${kind} stage identity through normalized reorder readback`, async ({ page }) => {
    const input = await autosaveDurationEditor(page, kind);
    const element = await input.elementHandle();
    await input.fill(kind === 'flexible' ? '175' : '7m');
    await page.clock.runFor(200);
    await readback(page, 'reorder');
    await expect(input).toBeFocused();
    expect(await input.evaluate((current, previous) => current === previous, element)).toBe(true);
    await page.clock.runFor(150);
    await expect.poll(async () => {
      const plan = await saved(page);
      return kind === 'fixed' ? plan.Stages[2].DurationSeconds : plan.Stages[2].Percent;
    }).toBe(kind === 'fixed' ? 420 : 175);
    const plan = await saved(page);
    expect(plan.Stages.map(row => row.Future)).toEqual(['C', 'B', 'A']);
    expect(plan.Stages[0].DurationSeconds).toBe(960);
    expect(plan.__externalUiRevision).toBe('b'.repeat(64));
    await element?.dispose();
  });

  test(`duration autosave cancels the ${kind} timer when its stage is removed`, async ({ page }) => {
    const input = await autosaveDurationEditor(page, kind);
    await input.fill(kind === 'flexible' ? '175' : '7m');
    await page.clock.runFor(200);
    await readback(page, 'remove');
    await page.clock.runFor(1000);
    expect((await saved(page)).Stages.map(row => row.Future)).toEqual(['B', 'C']);
    expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(0);
  });
}

test('duration autosave cancels a pending rebirth edit when its condition is removed externally', async ({ page }) => {
  const input = await autosaveDurationEditor(page, 'rebirth');
  await input.fill('7m');
  await page.clock.runFor(200);
  const plan = await saved(page);
  await page.evaluate(plan => (window as unknown as TimingFixtureWindow).__stageLive({ plan: { ...plan, TimeTriggerEnabled: false } }), plan);
  await expect(input).toHaveCount(0);
  await page.clock.runFor(1000);
  expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(0);
  await page.evaluate(plan => (window as unknown as TimingFixtureWindow).__stageLive({ plan }), plan);
  await expect(input).toHaveValue('20m');
  await page.clock.runFor(500);
  expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(0);
});

test('duration autosave preserves all ready drafts when busy clears for multiple editors', async ({ page }) => {
  await mount(page, { ...source, TimeTriggerEnabled: true, TimeTriggerSeconds: 1200 }, true);
  await pauseDurationClock(page);
  // Keep focus unchanged so these independent drafts all await the same busy action.
  for (const [selector, text] of [
    ['.stage-panel:nth-of-type(1) input[data-duration-key]', '7m'],
    ['.stage-panel:nth-of-type(3) input[data-duration-key]', '9m'],
    ['input[aria-label="Fallback time budget"]', '40m'],
    ['input[aria-label="Rebirth after"]', '30m']
  ]) {
    await page.locator(selector).evaluate((element, text) => {
      (element as HTMLInputElement).value = text;
      element.dispatchEvent(new Event('input', { bubbles: true }));
    }, text);
  }
  await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageLive({ disabled: true }));
  await page.clock.runFor(350);
  expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(0);
  await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageLive({ disabled: false }));
  await expect.poll(async () => {
    const plan = await saved(page);
    return [plan.Stages[0].DurationSeconds, plan.Stages[2].DurationSeconds, plan.StageDurationSeconds, plan.TimeTriggerSeconds];
  }).toEqual([420, 540, 2400, 1800]);
  expect(await page.evaluate(() => (window as unknown as TimingFixtureWindow).__stageEdits)).toBe(4);
});
