import { expect, test, type Page } from '@playwright/test';

const plan = {
  Name: 'test', Version: 18, __externalUiRevision: 'a'.repeat(64),
  GearGoal: 2, ActiveGoalPresetIndex: 1,
  GoalPresets: [{ Name: 'Fight', GearGoal: 9 }, { Name: 'NGU', GearGoal: 2 }, { Name: 'Time Machine', GearGoal: 5 }],
  Stages: [{ Name: 'Training', Percent: 100, CompletionMode: 1, TargetAdvancedTrainingPower: '9223372036854775806', TargetAdvancedTrainingToughness: '6000', FutureStageField: 'keep' }],
  RebirthHistory: [], FuturePlanField: 'keep'
};

// Mount only the component under test. No App, controller, game or native actions run.
async function mountComponent(page: Page, component: string, value: string, props: Record<string, unknown> = {}): Promise<void> {
  await page.route(/http:\/\/127\.0\.0\.1:\d+\/$/, route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><head><link rel="stylesheet" href="/src/styles.css"></head><body><main id="fixture"></main></body></html>' }));
  await page.goto('/');
  const main = await (await page.request.get('/src/main.ts')).text();
  const runtimePath = main.match(/import\s*\{\s*mount\s*\}\s*from\s*["']([^"']+)["']/)?.[1];
  if (!runtimePath) throw new Error('Could not find the served Svelte runtime.');
  await page.evaluate(async ({ runtimePath, component, value, props }) => {
    const { mount } = await import(runtimePath);
    const { default: Component } = await import(`/src/lib/${component}.svelte`);
    const apiPath = '/src/lib/api.ts';
    const { parseJson, stringifyJson } = await import(apiPath);
    const modelPath = '/src/lib/model.ts';
    const { validatePlan, assertPlanMetadata } = await import(modelPath);
    const state = window as unknown as { __componentEdits: string; __componentCommands: unknown[]; __TAURI_INTERNALS__: unknown };
    state.__componentEdits = '';
    state.__componentCommands = [];
    state.__TAURI_INTERNALS__ = { invoke: async (command: string) => {
      if (command !== 'plugin:dialog|message') throw new Error(`Unexpected native call: ${command}`);
      return 'Yes';
    } };
    const data = parseJson(value);
    mount(Component, { target: document.getElementById('fixture'), props: {
      ...props,
      ...(['PlanEditor', 'PlanGear'].includes(component) ? { plan: data, ...(component === 'PlanEditor' ? { name: 'test', dirty: false } : {}), onchange: (next: unknown) => { state.__componentEdits = stringifyJson(next); } } : {}),
      ...(component === 'AugmentTools' ? { oncommand: async (command: string, args: unknown) => { state.__componentCommands.push({ command, args }); } } : {}),
      ...(component === 'InlineNumber' ? { onbegin: () => ({ commit: async (next: number) => { state.__componentCommands.push(next); return props.saveSucceeds !== false; }, cancel: () => {} }) } : {}),
      ...(component === 'JsonEditor' ? { title: 'Plan', value: data, validate: (next: unknown, base: unknown) => { assertPlanMetadata(base, next); validatePlan(next); }, onapply: (next: unknown) => { state.__componentEdits = stringifyJson(next); }, ondirty: () => {} } : {})
    } });
  }, { runtimePath, component, value, props });
  await expect(page.locator('#fixture')).not.toBeEmpty();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

async function edits(page: Page): Promise<typeof plan & { ActivePresetId?: string }> {
  return page.evaluate(() => JSON.parse((window as unknown as { __componentEdits: string }).__componentEdits));
}

for (const scenario of [
  { selected: 0, active: 1 },
  { selected: 1, active: 1 },
  { selected: 2, active: 1 },
  { selected: 2, active: 2 }
]) {
  test(`global selection ${scenario.selected + 1} preserves legacy active gear preset ${scenario.active + 1}`, async ({ page }) => {
    const source = { ...plan, ActiveGoalPresetIndex: scenario.active, GearGoal: plan.GoalPresets[scenario.active].GearGoal };
    await mountComponent(page, 'PlanGear', JSON.stringify(source), { presetLibrary: { Version: 1, Presets: [{ Id: 'shared_0', Name: 'Shared zero' }, { Id: 'shared_1', Name: 'Shared one' }, { Id: 'shared_2', Name: 'Shared two' }], __externalUiRevision: 'b'.repeat(64) } });
    await expect(page.getByRole('combobox', { name: 'Fight scoring', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: /^Remove preset/ })).toHaveCount(0);
    await page.getByLabel('Active preset', { exact: true }).selectOption(`shared_${scenario.selected}`);
    const next = await edits(page);
    expect(next.ActivePresetId).toBe(`shared_${scenario.selected}`);
    expect(next.ActiveGoalPresetIndex).toBe(source.ActiveGoalPresetIndex);
    expect(next.GearGoal).toBe(source.GearGoal);
    expect(next.GoalPresets).toEqual(source.GoalPresets);
    expect(next.Stages).toEqual(source.Stages);
    expect(next.FuturePlanField).toBe('keep');
  });
}

test('standalone gear scoring preserves legacy presets, stages and unknown fields', async ({ page }) => {
  const source = { ...plan, FightScoreMode: 0 };
  await mountComponent(page, 'PlanGear', JSON.stringify(source));
  const scoring = page.getByRole('combobox', { name: 'Fight scoring', exact: true });
  await scoring.selectOption('1');
  expect(await edits(page)).toEqual({ ...source, FightScoreMode: 1 });
});

test('disabled standalone gear controls cannot emit edits', async ({ page }) => {
  await mountComponent(page, 'PlanGear', JSON.stringify(plan), { disabled: true });
  await expect(page.getByRole('combobox', { name: 'Fight scoring', exact: true })).toBeDisabled();
  await expect(page.getByRole('combobox', { name: 'Active preset', exact: true })).toBeDisabled();
  expect(await page.evaluate(() => (window as unknown as { __componentEdits: string }).__componentEdits)).toBe('');
});

test('AT edits keep large targets exact and unrelated numeric tokens untouched', async ({ page }) => {
  const source = JSON.stringify(plan).replace('"FuturePlanField":"keep"', '"FuturePlanField":9007199254740993');
  await mountComponent(page, 'PlanEditor', source);
  await page.getByRole('button', { name: 'Edit stage 1: Custom', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'AT power target' })).toHaveValue('9223372036854775806');
  await page.getByRole('textbox', { name: 'AT power target' }).fill('9223372036854775805');
  const captured = await page.evaluate(() => (window as unknown as { __componentEdits: string }).__componentEdits);
  expect(captured).toContain('"TargetAdvancedTrainingPower":"9223372036854775805"');
  expect(captured).toContain('"FuturePlanField":9007199254740993');
  expect(captured).toContain('"FutureStageField":"keep"');
});

test('switching completion mode preserves hidden inexact target numbers', async ({ page }) => {
  const source = JSON.stringify({ ...plan, Stages: [{ ...plan.Stages[0], CompletionMode: 0 }] })
    .replace('"9223372036854775806"', '9223372036854775806');
  await mountComponent(page, 'PlanEditor', source);
  await page.getByRole('button', { name: 'Edit stage 1: Custom', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'AT power target' })).toHaveCount(0);
  await page.getByRole('combobox', { name: /^Finish stage by/ }).selectOption('target');
  const captured = await page.evaluate(() => (window as unknown as { __componentEdits: string }).__componentEdits);
  expect(captured).toContain('"CompletionMode":1');
  expect(captured).toContain('"TargetAdvancedTrainingPower":9223372036854775806');
  expect(captured).toContain('"TargetAdvancedTrainingToughness":"6000"');
});

test('saved duration, overrides and validation retain their command semantics', async ({ page }) => {
  await mountComponent(page, 'AugmentTools', '{}', { canPreview: true, canAllocate: true, busy: false, savedEstimate: 300, estimatePending: true, result: null });
  await expect(page.getByRole('spinbutton', { name: 'Duration (s)', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Preview', exact: true }).click();
  await page.getByRole('checkbox', { name: 'Use saved duration', exact: true }).uncheck();
  await expect(page.getByText('Duration changes pending.', { exact: true })).toHaveCount(0);
  for (const invalid of ['', '0', '1.5']) {
    await page.getByRole('spinbutton', { name: 'Duration (s)', exact: true }).fill(invalid);
    await expect(page.getByRole('button', { name: 'Preview', exact: true })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Allocate', exact: true })).toBeDisabled();
    await expect(page.getByRole('alert')).toBeVisible();
  }
  await page.getByRole('spinbutton', { name: 'Duration (s)', exact: true }).fill('450');
  await page.getByRole('button', { name: 'Allocate', exact: true }).click();
  const commands = await page.evaluate(() => (window as unknown as { __componentCommands: unknown[] }).__componentCommands);
  expect(commands).toEqual([{ command: 'previewAugments', args: {} }, { command: 'allocateAugments', args: { expectedSeconds: 450 } }]);
});

test('advanced editor keeps read-only metadata protected and requires existing fields', async ({ page }) => {
  await mountComponent(page, 'JsonEditor', JSON.stringify(plan));
  const editor = page.getByRole('textbox', { name: 'Advanced Plan JSON', exact: true });
  await editor.fill(JSON.stringify({ ...plan, Name: 'changed' }));
  await expect(page.getByRole('alert')).toContainText('Name');
  const { FuturePlanField: _, ...missing } = plan;
  await editor.fill(JSON.stringify(missing));
  await expect(page.getByRole('alert')).toHaveText('Keep the existing FuturePlanField field.');
  await editor.fill(JSON.stringify({ ...plan, FuturePlanField: 'updated' }));
  await expect.poll(() => page.evaluate(() => (window as unknown as { __componentEdits: string }).__componentEdits)).not.toBe('');
  expect((await edits(page)).FuturePlanField).toBe('updated');
  expect((await edits(page)).Stages[0].TargetAdvancedTrainingPower).toBe('9223372036854775806');
});

test('inline numbers select the value, autosave, and cancel unsaved changes with Escape', async ({ page }) => {
  await mountComponent(page, 'InlineNumber', '{}', { label: 'Target', value: 20, min: 0, max: 100 });
  await page.getByRole('button', { name: 'Edit Target', exact: true }).click();
  const input = page.getByRole('spinbutton', { name: 'Target', exact: true });
  await expect(input).toBeFocused();
  await expect(input).toHaveValue('20');
  await input.fill('35');
  await expect.poll(() => page.evaluate(() => (window as unknown as { __componentCommands: unknown[] }).__componentCommands)).toEqual([35]);
  await input.fill('90');
  await input.press('Escape');
  await expect(input).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Edit Target', exact: true })).toBeFocused();
  const commits = await page.evaluate(() => (window as unknown as { __componentCommands: unknown[] }).__componentCommands);
  expect(commits).toEqual([35]);
});

test('inline numbers reject blanks, fractions, and out-of-range edits without saving', async ({ page }) => {
  await mountComponent(page, 'InlineNumber', '{}', { label: 'Target', value: 20, min: 0, max: 100 });
  await page.getByRole('button', { name: 'Edit Target', exact: true }).click();
  const input = page.getByRole('spinbutton', { name: 'Target', exact: true });
  for (const invalid of ['', '1.5', '-1', '101']) {
    await input.fill(invalid);
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(input).toHaveValue(invalid);
  }
  const commits = await page.evaluate(() => (window as unknown as { __componentCommands: unknown[] }).__componentCommands);
  expect(commits).toEqual([]);
});

test('failed inline saves keep the edit available for retry', async ({ page }) => {
  await mountComponent(page, 'InlineNumber', '{}', { label: 'Target', value: 20, min: 0, max: 100, saveSucceeds: false });
  await page.getByRole('button', { name: 'Edit Target', exact: true }).click();
  const input = page.getByRole('spinbutton', { name: 'Target', exact: true });
  await input.fill('35');
  await expect(page.getByRole('alert')).toHaveText('Not saved. Your edit is kept. Edit the value to retry.');
  await expect(input).toHaveValue('35');
  await expect(input).toBeEnabled();
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toHaveCount(0);
});
