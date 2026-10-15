import { expect, test, type Page } from '@playwright/test';
import { stagePresets } from '../src/lib/stage-presets';

const source = {
  Name: 'stages', Version: 15, __externalUiRevision: 'a'.repeat(64),
  StageDurationSeconds: 1200, StageLoopStartIndex: 1,
  Stages: [
    { Name: 'Opener', Percent: .2, EnergyFocus: 1, MagicFocus: 1, GearGoal: 5, EnergyPriorities: ['TM'], MagicPriorities: ['RIT'], Future: 'A' },
    { Name: 'Training', Percent: .3, EnergyFocus: 7, MagicFocus: 4, GearGoal: 11, CompletionMode: 1, TargetAdvancedTrainingPower: '9223372036854775806', TargetAdvancedTrainingToughness: '6000', Future: 'B' },
    { Name: 'Sustain', Percent: .8, EnergyFocus: 5, MagicFocus: 6, GearGoal: 2, Future: 'C' }
  ], Future: 'keep'
};
const diagnostics = new WeakMap<Page, string[]>();
type SavedPlan = Omit<typeof source, 'Stages'> & { Stages: ((typeof source.Stages)[number] & { DurationSeconds?: number; FightOnEntry?: boolean; BlackBarLookaheadSeconds?: number })[] };
test.afterEach(async ({ page }) => { expect(diagnostics.get(page) ?? []).toEqual([]); });

async function mount(page: Page, plan: SavedPlan = source, cloneEdits = false): Promise<void> {
  const errors: string[] = [];
  diagnostics.set(page, errors);
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    // Chromium can deny Vite's dev-only loopback socket in this isolated fixture.
    if (message.text().startsWith('WebSocket connection to \'ws://127.0.0.1:1420/') || message.text().startsWith('[vite] failed to connect to websocket.')) return;
    if (message.type() === 'warning' || message.type() === 'error') errors.push(message.text());
  });
  await page.route('http://127.0.0.1:1420/', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><head><link rel="stylesheet" href="/src/styles.css"></head><body><main id="fixture" style="padding:8px"></main></body></html>' }));
  await page.goto('/');
  const main = await (await page.request.get('/src/main.ts')).text();
  const runtimePath = main.match(/import\s*\{\s*mount\s*\}\s*from\s*["']([^"']+)["']/)?.[1];
  if (!runtimePath) throw new Error('Missing Svelte runtime');
  await page.evaluate(async ({ source, runtimePath, cloneEdits }) => {
    const legacyPath = runtimePath.replace(/svelte\.js(?=\?|$)/, 'svelte_legacy.js');
    const { createClassComponent } = await import(legacyPath);
    const componentPath = '/src/lib/PlanEditor.svelte';
    const { default: Component } = await import(componentPath);
    const apiPath = '/src/lib/api.ts';
    const { parseJson, stringifyJson } = await import(apiPath);
    const state = window as unknown as { __stagePlan: string; __stageEdits: number; __stageNamespace: (name: string) => void; __stageName: (name: string) => void; __replaceStages: () => void; __stageReadback: (mode?: string) => void };
    state.__stagePlan = source;
    state.__stageEdits = 0;
    const component = createClassComponent({ component: Component, target: document.getElementById('fixture'), props: {
      plan: parseJson(source), name: 'stages', namespace: 'folder-a', dirty: false,
      onchange: (next: unknown) => {
        state.__stagePlan = stringifyJson(next);
        state.__stageEdits++;
        component.$set({ plan: cloneEdits ? parseJson(state.__stagePlan) : next, dirty: true });
      }
    } });
    state.__stageNamespace = (namespace: string) => component.$set({ namespace });
    state.__stageName = (name: string) => component.$set({ name });
    state.__replaceStages = () => component.$set({ plan: parseJson(source) });
    state.__stageReadback = (mode?: string) => {
      const next = parseJson(state.__stagePlan);
      next.__externalUiRevision = 'b'.repeat(64);
      if (mode !== 'clone') next.Stages = next.Stages.map((row: Record<string, unknown>) => ({ CompletionMode: 0, DurationSeconds: 0, DurationUnit: '%', FightOnEntry: false, TargetAdvancedTrainingPower: '0', TargetAdvancedTrainingToughness: '0', EnergyPriorities: [], MagicPriorities: [], BlackBarLookaheadSeconds: 5, ...row }));
      if (mode === 'targets') for (const row of next.Stages) {
        for (const key of ['TargetAdvancedTrainingPower', 'TargetAdvancedTrainingToughness']) row[key] = BigInt(row[key]).toString();
      }
      if (mode === 'reorder') next.Stages.reverse();
      if (mode === 'ambiguous') next.Stages[1] = { ...next.Stages[0] };
      if (mode === 'name') next.Name = 'other-plan';
      state.__stagePlan = stringifyJson(next);
      component.$set({ plan: next, dirty: false });
    };
  }, { source: JSON.stringify(plan).replace('"Future":"keep"', '"Future":9007199254740993'), runtimePath, cloneEdits });
  await expect(page.getByRole('list', { name: 'Plan stages' }).locator('.bar-segment')).toHaveCount(plan.Stages.length);
}

async function saved(page: Page): Promise<SavedPlan> {
  return page.evaluate(() => JSON.parse((window as unknown as { __stagePlan: string }).__stagePlan));
}

async function readback(page: Page, mode?: string): Promise<void> {
  await page.evaluate(mode => (window as unknown as { __stageReadback: (mode?: string) => void }).__stageReadback(mode), mode);
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

test('fight on entry survives normalized readback and presets without losing a duration draft', async ({ page }) => {
  await mount(page);
  const panel = await selectStage(page, 0);
  const duration = panel.getByRole('textbox', { name: 'Duration', exact: true });
  await duration.fill('unfinished');
  await readback(page);
  await expect(panel).toBeVisible();
  await expect(duration).toHaveValue('unfinished');
  await duration.press('Escape');
  const fight = panel.getByRole('checkbox', { name: 'Fight on entry', exact: true });
  await expect(fight).not.toBeChecked();
  await fight.check();
  expect((await saved(page)).Stages.map(row => row.FightOnEntry)).toEqual([true, false, false]);
  await readback(page);
  await expect(fight).toBeChecked();
  await panel.getByRole('combobox', { name: 'Focus preset', exact: true }).selectOption({ label: 'Energy NGU' });
  expect((await saved(page)).Stages[0].FightOnEntry).toBe(true);
  await fight.uncheck();
  await readback(page);
  await expect(fight).not.toBeChecked();
  expect((await saved(page)).Stages[0].FightOnEntry).toBe(false);
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

for (const label of ['30m NUMBER cycle', 'AT Adventure 6k/6k to NGU']) {
  test(`confirming ${label} applies the complete template and preserves plan metadata`, async ({ page }) => {
    await mount(page);
    const before = await saved(page);
    const advancedTraining = label === 'AT Adventure 6k/6k to NGU';
    await chooseTemplate(page, label, 'Yes');
    const rows = page.locator('.bar-segment');
    await expect(rows).toHaveCount(advancedTraining ? 2 : 8);
    const next = await saved(page);
    expect(next).toEqual({ ...before, Stages: next.Stages, StageSchedulerEnabled: true, StageDurationSeconds: advancedTraining ? 1200 : 1800, StageLoopStartIndex: advancedTraining ? -1 : 5 });
    expect(await page.evaluate(() => (window as unknown as { __stagePlan: string }).__stagePlan)).toContain('"Future":9007199254740993');
    expect(next.Stages.every(row => row.Future === undefined)).toBe(true);
    await expect(page.getByRole('checkbox', { name: 'Enable stages', exact: true })).toBeChecked();
    await expect(page.getByRole('textbox', { name: 'Stage name', exact: true })).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: 'Stage timing', exact: true })).toHaveValue('time');
    await expect(page.getByRole('combobox', { name: 'Repeat from', exact: true })).toHaveValue(advancedTraining ? '-1' : '5');
    const names = advancedTraining ? ['AT Adventure', 'Energy NGU'] : ['Time Machine', 'Augments + Blood', 'Time Machine', 'Augments + Blood', 'Wandoos', 'Time Machine', 'Augments + Blood', 'Wandoos'];
    expect(next.Stages.map(row => row.Name)).toEqual(names);
    for (const [index, name] of names.entries()) {
      const expected = stagePresets.find(preset => preset.label === name)!;
      expect([next.Stages[index].EnergyPriorities, next.Stages[index].MagicPriorities]).toEqual([expected.EnergyPriorities, expected.MagicPriorities]);
      const panel = await selectStage(page, index);
      await expect(panel.getByRole('combobox', { name: 'Focus preset', exact: true }).locator('option:checked')).toHaveText(name);
      await expect(rows.nth(index).getByRole('button', { name: `Edit stage ${index + 1}: ${name}`, exact: true })).toHaveAttribute('draggable', 'false');
    }
    if (advancedTraining) {
      expect(next.Stages.map(row => [row.CompletionMode, row.DurationSeconds, row.TargetAdvancedTrainingPower, row.TargetAdvancedTrainingToughness])).toEqual([
        [1, 0, '6000', '6000'], [0, 1200, '0', '0']
      ]);
      expect(next.Stages.map(row => [row.EnergyFocus, row.MagicFocus, row.GearGoal])).toEqual([[7, 4, 11], [5, 6, 2]]);
      const panel = await selectStage(page, 0);
      await expect(panel.getByRole('textbox', { name: 'Duration', exact: true })).toHaveCount(0);
      await expect(panel.getByRole('textbox', { name: 'AT power target', exact: true })).toHaveValue('6000');
      await expect(panel.getByRole('textbox', { name: 'AT toughness target', exact: true })).toHaveValue('6000');
      await expectDurations(page, ['20m']);
      await expect(page.locator('.dashboard-field').filter({ hasText: 'Total time' }).getByText('20m', { exact: true })).toBeVisible();
    } else {
      expect(next.Stages.map(row => row.DurationSeconds)).toEqual([300, 180, 180, 60, 180, 300, 300, 300]);
      expect(next.Stages.reduce((total, row) => total + row.DurationSeconds!, 0)).toBe(1800);
      expect(next.Stages.map(row => [row.EnergyFocus, row.MagicFocus, row.GearGoal])).toEqual([
        [1, 1, 5], [2, 4, 10], [1, 1, 5], [2, 4, 10], [3, 3, 12], [1, 1, 5], [2, 4, 10], [3, 3, 12]
      ]);
      expect(next.Stages.every(row => row.CompletionMode === 0 && row.TargetAdvancedTrainingPower === '0' && row.TargetAdvancedTrainingToughness === '0')).toBe(true);
      await expect(page.getByRole('textbox', { name: 'AT power target', exact: true })).toHaveCount(0);
      await expectDurations(page, ['5m', '3m', '3m', '1m', '3m', '5m', '5m', '5m']);
      await expect(rows.nth(5).getByLabel('Loop start', { exact: true })).toBeVisible();
      await expect(page.locator('.dashboard-field').filter({ hasText: 'Total time' }).getByText('30m', { exact: true })).toBeVisible();
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

test('Energy NGU preset pairs magic NGUs with energy-focused gear', async ({ page }) => {
  await mount(page);
  const first = await selectStage(page, 0);
  await first.getByRole('combobox', { name: 'Focus preset', exact: true }).selectOption({ label: 'Energy NGU' });
  await expect(first.getByRole('combobox', { name: 'Energy focus', exact: true })).toHaveValue('5');
  await expect(first.getByRole('combobox', { name: 'Magic focus', exact: true })).toHaveValue('6');
  await expect(first.getByRole('combobox', { name: 'Gear goal', exact: true })).toHaveCount(0);
  await expect(page.locator('.bar-segment').first().getByRole('button', { name: 'Edit stage 1: Energy NGU', exact: true })).toHaveAttribute('aria-expanded', 'true');
  const next = await saved(page);
  expect(next.Stages[0]).toEqual({ ...source.Stages[0], Name: 'Energy NGU', EnergyFocus: 5, MagicFocus: 6, GearGoal: 2, EnergyPriorities: ['NGU', 'WAN', 'TM', 'AUG', 'AT'], MagicPriorities: ['NGU', 'RIT', 'WAN', 'TM'] });
  expect(next.Stages.slice(1)).toEqual(source.Stages.slice(1));
  expect(next.StageLoopStartIndex).toBe(source.StageLoopStartIndex);
});

test('unrelated stage edits preserve hidden custom gear and cap until an explicit preset action', async ({ page }) => {
  const custom = { ...source, Stages: source.Stages.map((row, index) => index === 0 ? { ...row, Name: 'Time Machine', GearGoal: 9, BlackBarLookaheadSeconds: 37, FightOnEntry: true } : row) };
  await mount(page, custom);
  const panel = await selectStage(page, 0);
  await expect(panel.getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('-1');
  expect((await saved(page)).Stages).toEqual(custom.Stages);
  const duration = panel.getByRole('textbox', { name: 'Duration', exact: true });
  await duration.fill('5m');
  await duration.press('Enter');
  await panel.getByRole('combobox', { name: 'Energy focus', exact: true }).selectOption('3');
  expect((await saved(page)).Stages[0]).toMatchObject({ GearGoal: 9, BlackBarLookaheadSeconds: 37, FightOnEntry: true, DurationSeconds: 300, EnergyFocus: 3 });
  await readback(page);
  await expect(panel.getByRole('checkbox', { name: 'Fight on entry', exact: true })).toBeChecked();
  expect((await saved(page)).Stages[0]).toMatchObject({ GearGoal: 9, BlackBarLookaheadSeconds: 37, FightOnEntry: true });
  await panel.getByRole('combobox', { name: 'Focus preset', exact: true }).selectOption({ label: 'Wandoos' });
  expect((await saved(page)).Stages[0]).toMatchObject({ Name: 'Wandoos', EnergyFocus: 3, MagicFocus: 3, GearGoal: 12, EnergyPriorities: ['WAN', 'NGU', 'TM', 'AUG', 'AT'], MagicPriorities: ['WAN', 'RIT', 'NGU', 'TM'], BlackBarLookaheadSeconds: 37, FightOnEntry: true, DurationSeconds: 300 });
  await readback(page);
  expect((await saved(page)).Stages[0]).toMatchObject({ GearGoal: 12, BlackBarLookaheadSeconds: 37, FightOnEntry: true });
  await expect(panel.getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('2');
});

for (const [index, label] of ['Time Machine', 'Augments + Blood', 'Wandoos', 'Energy NGU', 'Magic NGU', 'All NGU', 'AT Adventure', 'AT Wandoos'].entries()) {
  test(`${label} base preset survives priority customization and normalized readback`, async ({ page }) => {
    await mount(page, { ...source, Stages: source.Stages.map((row, index) => index === 0 ? { ...row, GearGoal: 9, BlackBarLookaheadSeconds: 37, FightOnEntry: true } : row) });
    await expect((await selectStage(page, 1)).getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('6');
    await expect((await selectStage(page, 2)).getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('3');
    const first = await selectStage(page, 0);
    const preset = first.getByRole('combobox', { name: 'Focus preset', exact: true });
    await expect(preset).toHaveValue('-1');
    await preset.selectOption({ label });
    await expect(preset).toHaveValue(String(index));
    const GearGoal = [5, 10, 12, 2, 3, 4, 11, 11][index];
    expect((await saved(page)).Stages[0]).toMatchObject({ Name: label, GearGoal, BlackBarLookaheadSeconds: 37, FightOnEntry: true });
    const energy = first.getByRole('textbox', { name: 'Energy priorities', exact: true });
    const magic = first.getByRole('textbox', { name: 'Magic priorities', exact: true });
    await expect(energy).toHaveValue(stagePresets[index].EnergyPriorities.join(', '));
    await expect(magic).toHaveValue(stagePresets[index].MagicPriorities.join(', '));
    await energy.fill('TM, NGU-0');
    await expect(preset).toHaveValue(String(index));
    await magic.fill('RIT-1');
    await expect(preset).toHaveValue(String(index));
    await readback(page);
    await expect(preset).toHaveValue(String(index));
    await expect(first).toBeVisible();
    await expect(page.getByRole('button', { name: `Edit stage 1: ${label}`, exact: true })).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByRole('button', { name: `Edit stage 1: ${label}`, exact: true })).toHaveAttribute('draggable', 'false');
    await expect(first.getByRole('checkbox', { name: 'Fight on entry', exact: true })).toBeChecked();
    await openSchedule(page);
    await expect(page.getByRole('combobox', { name: 'Repeat from', exact: true }).locator('option').first()).toHaveText('Do not repeat');
    await expect(page.getByRole('combobox', { name: 'Repeat from', exact: true }).locator('option').nth(1)).toHaveText(`1: ${label}`);
    expect((await saved(page)).Stages[0]).toMatchObject({ Percent: .2, Future: 'A', GearGoal, BlackBarLookaheadSeconds: 37, FightOnEntry: true });
    await expect(first.getByRole('heading', { name: label, exact: true })).toBeVisible();
    await expect(energy).toHaveValue('TM, NGU-0');
    await expect(magic).toHaveValue('RIT-1');
    await preset.selectOption({ label });
    await expect(energy).toHaveValue(stagePresets[index].EnergyPriorities.join(', '));
    await expect(magic).toHaveValue(stagePresets[index].MagicPriorities.join(', '));
    await energy.fill('');
    await magic.fill('');
    await readback(page);
    await expect(preset).toHaveValue(String(index));
    await expect(page.getByRole('button', { name: `Edit stage 1: ${label}`, exact: true })).toHaveAttribute('aria-expanded', 'true');
  });
}

test('preset selection resets orders while AT Wandoos keeps its identity after order edits', async ({ page }) => {
  await mount(page);
  const row = await selectStage(page, 1);
  const completion = row.getByRole('combobox', { name: 'Finish stage by', exact: true });
  await completion.selectOption('0');
  const preset = row.getByRole('combobox', { name: 'Focus preset', exact: true });
  const energy = row.getByRole('textbox', { name: 'Energy priorities', exact: true });
  const magic = row.getByRole('textbox', { name: 'Magic priorities', exact: true });
  await energy.fill('TM, NGU-0');
  await magic.fill('RIT-1');
  await expect(preset).toHaveValue('6');
  await preset.selectOption({ label: 'AT Wandoos' });
  await expect(energy).toHaveValue('AT-3, AT-4, WAN, NGU, TM, AUG, AT');
  await expect(magic).toHaveValue('WAN, RIT, NGU, TM');
  expect((await saved(page)).Stages[1]).toEqual({
    ...source.Stages[1], Name: 'AT Wandoos', MagicFocus: 3, CompletionMode: 0, DurationSeconds: 300, DurationUnit: 'min',
    EnergyPriorities: ['AT-3', 'AT-4', 'WAN', 'NGU', 'TM', 'AUG', 'AT'], MagicPriorities: ['WAN', 'RIT', 'NGU', 'TM']
  });
  await readback(page);
  await expect(preset).toHaveValue('7');
  await energy.fill('AT-4, AT-3, WAN, NGU, TM');
  await expect(preset).toHaveValue('7');
  expect((await saved(page)).Stages[1].Name).toBe('AT Wandoos');
  await preset.selectOption({ label: 'AT Wandoos' });
  await magic.fill('RIT, WAN, NGU, TM');
  await expect(preset).toHaveValue('7');
  await preset.selectOption({ label: 'AT Adventure' });
  await expect(energy).toHaveValue('AT, NGU, WAN, TM, AUG');
  await expect(magic).toHaveValue('RIT, NGU, WAN, TM');
  await expect(preset).toHaveValue('6');
  await expect(row.getByRole('textbox', { name: 'Duration', exact: true })).toHaveValue('5m');
  await completion.selectOption({ label: 'AT power/toughness targets' });
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
    await expect(page.getByRole('alert')).toHaveText('AT power/toughness targets require AT Adventure. Change "Finish stage by" to Duration before choosing another preset.');
    await expect(preset).toHaveValue('6');
    await expect(row.getByRole('textbox', { name: 'AT power target', exact: true })).toHaveValue('9223372036854775806');
    await expect(row.getByRole('textbox', { name: 'AT toughness target', exact: true })).toHaveValue('6000');
    expect(await saved(page)).toEqual(before);
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(0);
  }
});

test('switching to AT completion applies AT Adventure and cancels an invalid duration draft', async ({ page }) => {
  await mount(page);
  const row = await selectStage(page, 1);
  const completion = row.getByRole('combobox', { name: 'Finish stage by', exact: true });
  await completion.selectOption('0');
  const preset = row.getByRole('combobox', { name: 'Focus preset', exact: true });
  await preset.selectOption({ label: 'AT Wandoos' });
  const duration = row.getByRole('textbox', { name: 'Duration', exact: true });
  await duration.fill('0s');
  await duration.press('Tab');
  await expect(page.getByRole('alert')).toContainText('Duration must be');
  await expect(duration).toHaveValue('0s');
  await completion.selectOption({ label: 'AT power/toughness targets' });
  await expect(duration).toHaveCount(0);
  await expect(preset).toHaveValue('6');
  await expect(row.getByRole('textbox', { name: 'Energy priorities', exact: true })).toHaveValue('AT, NGU, WAN, TM, AUG');
  await expect(row.getByRole('textbox', { name: 'Magic priorities', exact: true })).toHaveValue('RIT, NGU, WAN, TM');
  await expect(row.getByRole('textbox', { name: 'AT power target', exact: true })).toHaveValue('9223372036854775806');
  await expect(row.getByRole('textbox', { name: 'AT toughness target', exact: true })).toHaveValue('6000');
  await expect(page.getByRole('alert')).toHaveCount(0);
  expect((await saved(page)).Stages[1]).toEqual({
    ...source.Stages[1], Name: 'AT Adventure', DurationSeconds: 300, DurationUnit: 'min', EnergyPriorities: ['AT', 'NGU', 'WAN', 'TM', 'AUG'], MagicPriorities: ['RIT', 'NGU', 'WAN', 'TM']
  });
  const timing = page.getByRole('combobox', { name: 'Stage timing', exact: true });
  await timing.selectOption('time');
  await expect(timing).toHaveValue('time');
  await expectDurations(page, ['5m', '20m']);
  await expect(page.getByRole('alert')).toHaveCount(0);
  await completion.selectOption('0');
  await expect(duration).toHaveValue('5m');
  expect((await saved(page)).Stages.map(row => row.DurationSeconds)).toEqual([300, 300, 1200]);
});

for (const dormantSeconds of [undefined, 420]) {
  for (const mode of ['%', 'time']) {
    test(`AT row returning to ${mode} timing ${dormantSeconds ? 'restores dormant fixed time' : 'defaults to its own five minutes'} without changing other seconds`, async ({ page }) => {
      await mount(page, { ...source, Stages: source.Stages.map((row, index) => index === 1 && dormantSeconds ? { ...row, DurationSeconds: dormantSeconds } : row) });
      const timing = page.getByRole('combobox', { name: 'Stage timing', exact: true });
      if (mode === 'time') await timing.selectOption('time');
      const before = await saved(page);
      const row = await selectStage(page, 1);
      await row.getByRole('combobox', { name: 'Finish stage by', exact: true }).selectOption('0');
      const seconds = dormantSeconds ?? 300;
      await expect(row.getByRole('textbox', { name: 'Duration', exact: true })).toHaveValue(dormantSeconds ? '7m' : '5m');
      const next = await saved(page);
      expect(next.Stages[1]).toEqual({ ...source.Stages[1], CompletionMode: 0, DurationSeconds: seconds, DurationUnit: 'min' });
      expect([next.Stages[0], next.Stages[2]]).toEqual([before.Stages[0], before.Stages[2]]);
      expect(next.StageDurationSeconds).toBe(mode === '%' ? 1200 + seconds : 1200);
      expect(next.StageLoopStartIndex).toBe(1);
      await timing.selectOption('time');
      expect((await saved(page)).Stages.map(row => row.DurationSeconds)).toEqual([240, seconds, 960]);
      await expect(page.getByRole('alert')).toHaveCount(0);
    });
  }
}

for (const scope of ['namespace', 'name', 'plan.Name']) {
  test(`invalid schedule draft is cleared after ${scope} changes and cannot leak on blur`, async ({ page }) => {
    await mount(page);
    await openSchedule(page);
    const schedule = page.getByRole('textbox', { name: 'Schedule length', exact: true });
    await expect(schedule).toHaveValue('20m');
    await schedule.fill('0m');
    await schedule.press('Enter');
    await expect(page.getByRole('alert')).toHaveText('Schedule length must be a positive whole-second time, such as 30m or 2h.');
    await expect(schedule).toHaveValue('0m');
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

test('global stage timing converts every timed row without changing seconds or AT targets', async ({ page }) => {
  await mount(page);
  const timing = page.getByRole('combobox', { name: 'Stage timing', exact: true });
  await expect(timing).toHaveValue('%');
  await expectDurations(page, ['20%', '80%']);
  await timing.selectOption('time');
  await expect(timing).toHaveValue('time');
  await expectDurations(page, ['4m', '16m']);
  expect((await saved(page)).Stages.map(row => row.DurationSeconds)).toEqual([240, undefined, 960]);
  expect((await saved(page)).Stages[1]).toEqual(source.Stages[1]);
  await timing.selectOption('%');
  await expectDurations(page, ['20%', '80%']);
  expect((await saved(page)).Stages.map(row => row.DurationSeconds)).toEqual([0, undefined, 0]);
  await timing.selectOption('time');
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
  await second.getByRole('combobox', { name: 'Finish stage by', exact: true }).selectOption('0');
  const timing = page.getByRole('combobox', { name: 'Stage timing', exact: true });
  await timing.selectOption('time');
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
  await expect(page.locator('.dashboard-field').filter({ hasText: 'Total time' }).getByText('3h', { exact: true })).toBeVisible();
  await readback(page);
  await expectDurations(page, ['25m', '35m', '2h']);
  // Global time mode stores minutes, but the last row is displayed in hours.
  await timing.selectOption('time');
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
  for (const summary of ['Schedule & templates', 'Gear settings', 'Rebirth settings', 'Rebirth history']) {
    const details = page.locator('details').filter({ has: page.getByText(summary, { exact: true }) });
    await expect(details).not.toHaveAttribute('open');
  }
  const first = await selectStage(page, 0);
  await expect(first.getByRole('combobox', { name: 'Focus preset', exact: true })).toBeVisible();
  await expect(first.getByText('Focuses and gear identify the base preset.', { exact: false })).toBeVisible();
  await expect(first.getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('0');
  await expect(first.getByRole('combobox', { name: 'Finish stage by', exact: true })).toBeVisible();
  await expect(first.getByRole('textbox', { name: 'Duration', exact: true })).toBeVisible();
  await expect(first.getByRole('combobox', { name: 'Energy focus', exact: true })).toBeVisible();
  await expect(first.getByRole('combobox', { name: 'Magic focus', exact: true })).toBeVisible();
  await expect(page.locator('.stage-panel select[aria-label="Gear goal"]')).toHaveCount(0);
  await expect(first.getByRole('textbox', { name: 'Energy priorities', exact: true })).toBeVisible();
  await expect(first.getByRole('textbox', { name: 'Magic priorities', exact: true })).toBeVisible();
  await expect(first.getByRole('checkbox', { name: 'Fight on entry', exact: true })).toBeVisible();
  await expect(page.getByRole('spinbutton', { name: 'Black-bar lookahead (s)', includeHidden: true })).toHaveCount(0);
  await expect(page.getByText('Cap buffer (seconds)', { exact: false })).toHaveCount(0);
  await expect(first.locator('details, [aria-haspopup="menu"]')).toHaveCount(0);
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

for (const mode of ['%', 'time']) test(`adding a stage in ${mode} mode applies Time Machine and opens its settings`, async ({ page }) => {
  await mount(page);
  if (mode === 'time') await page.getByRole('combobox', { name: 'Stage timing', exact: true }).selectOption('time');
  const before = await saved(page);
  const previous = await selectStage(page, 1);
  await page.getByRole('button', { name: 'Add stage', exact: true }).click();
  await expect(page.locator('.bar-segment')).toHaveCount(4);
  await expect(page.getByRole('button', { name: 'Edit stage 4: Time Machine', exact: true })).toHaveAttribute('aria-expanded', 'true');
  const panel = page.getByRole('region', { name: 'Stage 4 settings', exact: true });
  await expect(panel).toBeVisible();
  await expect(previous).toBeHidden();
  await expect(page.locator('.stage-panel:visible')).toHaveCount(1);
  await expect(panel.getByRole('combobox', { name: 'Focus preset', exact: true })).toHaveValue('0');
  expect((await saved(page)).Stages.slice(0, 3)).toEqual(before.Stages);
  expect((await saved(page)).Stages[3]).toEqual({
    Name: 'Time Machine', Percent: 10, CompletionMode: 0, DurationSeconds: mode === 'time' ? 300 : 0, DurationUnit: mode === 'time' ? 'min' : '%',
    EnergyFocus: 1, MagicFocus: 1, GearGoal: 5, EnergyPriorities: ['TM', 'NGU', 'WAN', 'AUG', 'AT'], MagicPriorities: ['TM', 'RIT', 'NGU', 'WAN'],
    TargetAdvancedTrainingPower: '0', TargetAdvancedTrainingToughness: '0', BlackBarLookaheadSeconds: 5
  });
  await expect(panel.getByRole('textbox', { name: 'Energy priorities', exact: true })).toHaveValue('TM, NGU, WAN, AUG, AT');
  await expect(panel.getByRole('textbox', { name: 'Magic priorities', exact: true })).toHaveValue('TM, RIT, NGU, WAN');
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
  await expect(page.getByRole('combobox', { name: 'Stage timing', exact: true })).toHaveValue('mixed');
  expect((await saved(page)).Stages[2].DurationSeconds).toBeUndefined();
  await input.fill('4.5');
  await input.press('Tab');
  await expect(input).toHaveValue('270s');
  expect((await saved(page)).StageDurationSeconds).toBe(1230);
  expect((await saved(page)).Stages[0].Percent).toBe(.2);
  await first.getByRole('textbox', { name: 'Energy priorities', exact: true }).fill('TM, NGU-0');
  expect((await saved(page)).Stages[0].EnergyPriorities).toEqual(['TM', 'NGU-0']);
  expect(await first.getAttribute('data-stage')).toBe(stageKey);
  await page.getByRole('combobox', { name: 'Stage timing', exact: true }).selectOption('%');
  expect((await saved(page)).Stages[0].DurationSeconds).toBe(0);
  expect(await page.evaluate(() => (window as unknown as { __stagePlan: string }).__stagePlan)).toContain('"Future":9007199254740993');
  expect((await saved(page)).Stages[1].TargetAdvancedTrainingPower).toBe('9223372036854775806');
});

test('stage panel exposes completion mode and removes the loop boundary using its successor', async ({ page }) => {
  await mount(page);
  const panel = await selectStage(page, 1);
  await panel.getByRole('combobox', { name: 'Finish stage by', exact: true }).selectOption('0');
  await expect(page.getByRole('textbox', { name: 'AT power target' })).toHaveCount(0);
  expect((await saved(page)).Stages[1].TargetAdvancedTrainingPower).toBe('9223372036854775806');
  await panel.getByRole('button', { name: 'Remove stage', exact: true }).click();
  expect((await saved(page)).Stages.map(row => row.Name)).toEqual(['Opener', 'Sustain']);
  expect((await saved(page)).StageLoopStartIndex).toBe(1);
});

for (const mode of ['normalized', 'reorder']) {
  test(`focused duration draft survives ${mode} autosave readback and commits to its logical row`, async ({ page }) => {
    await mount(page);
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
    expect((await saved(page)).StageDurationSeconds).toBe(1050);
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
    await expect(page.getByRole('alert')).toContainText('Duration must be');
    if (mode === 'unrelated field edit') {
      await page.getByText('Gear settings', { exact: true }).click();
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
    await expect(page.getByRole('alert')).toContainText('Duration must be');
    expect((await saved(page)).Stages.map(row => row.DurationSeconds ?? 0)).toEqual([0, 0, 0, 0]);
    expect(await page.evaluate(() => (window as unknown as { __stageEdits: number }).__stageEdits)).toBe(mode === 'unrelated field edit' ? 1 : 0);
    await selected.getByRole('button', { name: 'Close stage settings', exact: true }).click();
    await selectStage(page, 1);
    await expect(duration).toHaveValue('0s');
    await duration.fill('120s');
    await duration.press('Enter');
    expect((await saved(page)).Stages.map(row => row.DurationSeconds ?? 0)).toEqual([0, 120, 0, 0]);
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
  { text: '0s', message: 'Duration must be' },
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
