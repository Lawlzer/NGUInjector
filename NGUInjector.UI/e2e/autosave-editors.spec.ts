import { expect, test, type Page } from '@playwright/test';

interface AutosaveHarness {
  attempts: { session: number; value: string }[];
  applied: string[];
  bases: string[];
  dirty: boolean[];
  cancels: number[];
  resolve: (success?: boolean) => void;
  setDisabled: (disabled: boolean) => void;
  setValue: (text: string) => void;
  changeEditor: (text: string) => void;
  setConnected: (connected: boolean) => void;
  destroy: () => Promise<void>;
}

interface AppAutosaveHarness {
  answer: boolean;
  prompts: string[];
  folders: string[];
  plans: Record<string, Record<string, unknown>>;
  settings: Record<string, unknown>;
  writes: { name: string; plan: Record<string, unknown> }[];
  patches: Record<string, unknown>[];
}

declare global {
  interface Window { __autosave: AutosaveHarness; }
  interface Window { __appAutosave: AppAutosaveHarness; }
}

// Isolated component mounts: no App, native dialogs, controller writes or game actions.
async function mountEditor(page: Page, component: 'InlineNumber' | 'JsonEditor' | 'BloodGoals', source = '20', options: { deferred?: boolean; fail?: boolean; integer?: boolean } = {}): Promise<void> {
  await page.route(/http:\/\/127\.0\.0\.1:\d+\/$/, route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><body><main id="fixture"></main></body></html>' }));
  await page.goto('/');
  const main = await (await page.request.get('/src/main.ts')).text();
  const runtimePath = main.match(/import\s*\{\s*mount\s*\}\s*from\s*["']([^"']+)["']/)?.[1];
  if (!runtimePath) throw new Error('Could not find the served Svelte runtime.');
  const controller = await (await page.request.get('/src/lib/controller.ts')).text();
  const storePath = controller.match(/from\s*["']([^"']*svelte_store[^"']*)["']/)?.[1];
  if (!storePath) throw new Error('Could not find the served Svelte store runtime.');
  await page.evaluate(async ({ runtimePath, storePath, component, source, options }) => {
    const { mount, unmount } = await import(runtimePath);
    const { default: Component } = await import(`/src/lib/${component}.svelte`);
    const apiPath = '/src/lib/api.ts';
    const modelPath = '/src/lib/model.ts';
    const { parseJson, stringifyJson } = await import(apiPath);
    const { assertPlanMetadata } = await import(modelPath);
    const { writable, fromStore } = await import(storePath);
    const store = writable({ value: parseJson(source), disabled: false, connected: true });
    const state = fromStore(store);
    const pending: ((success: boolean) => void)[] = [];
    let sessionId = 0;
    let context = 0;
    const harness: AutosaveHarness = window.__autosave = {
      attempts: [], applied: [], bases: [], dirty: [], cancels: [],
      resolve: (success = true) => { const next = pending.shift(); if (!next) throw new Error('No pending write.'); next(success); },
      setDisabled: disabled => store.update((s: typeof state.current) => ({ ...s, disabled })),
      setValue: text => store.update((s: typeof state.current) => ({ ...s, value: parseJson(text) })),
      changeEditor: text => { ++context; harness.setValue(text); },
      setConnected: connected => store.update((s: typeof state.current) => ({ ...s, connected })),
      destroy: async () => { await unmount(instance); }
    };
    const begin = (key?: string) => {
      const id = ++sessionId;
      const editor = context;
      let closed = false;
      let attempted = false;
      return {
        async commit(next: number) {
          if (closed) throw new Error('Cannot reuse a successful session.');
          if (editor !== context) return false;
          attempted = true;
          harness.attempts.push({ session: id, value: stringifyJson(next) });
          harness.setDisabled(true);
          const success = options.deferred ? await new Promise<boolean>(resolve => pending.push(resolve)) : !options.fail;
          if (success) {
            closed = true;
            store.update((s: typeof state.current) => ({ ...s, value: key ? { ...s.value, [key]: next } : next }));
          }
          harness.setDisabled(false);
          return success;
        },
        cancel() { if (!closed && attempted) harness.cancels.push(id); closed = true; }
      };
    };
    const props = component === 'JsonEditor' ? {
      title: 'Plan', get value() { return state.current.value; },
      get disabled() { return state.current.disabled; },
      validate: (draft: unknown, base: unknown) => assertPlanMetadata(base, draft),
      async onapply(draft: unknown, base: unknown) {
        harness.applied.push(stringifyJson(draft));
        harness.bases.push(stringifyJson(base));
        if (options.deferred && !await new Promise<boolean>(resolve => pending.push(resolve))) throw new Error('Source changed. Edits retained.');
        if (options.fail) throw new Error('Source changed. Edits retained.');
        harness.setValue(stringifyJson(draft));
      },
      ondirty: (dirty: boolean) => harness.dirty.push(dirty)
    } : component === 'BloodGoals' ? {
      get connected() { return state.current.connected; },
      get editable() { return !state.current.disabled; },
      get savedGoals() { return state.current.value; },
      data: { available: true, spaghetti: { goalPercent: 20, currentPercent: 10 }, counterfeit: { goalPercent: 400 } },
      beginGoalEdit: begin, ondirty: (dirty: boolean) => harness.dirty.push(dirty)
    } : {
      label: 'Target', get value() { return state.current.value; }, min: 0, max: 100,
      integer: options.integer ?? true,
      get disabled() { return state.current.disabled; }, onbegin: begin,
      ondirty: (dirty: boolean) => harness.dirty.push(dirty)
    };
    const instance = mount(Component, { target: document.getElementById('fixture'), props });
  }, { runtimePath, storePath, component, source, options });
  await expect(page.locator('#fixture')).not.toBeEmpty();
  await page.clock.install({ time: new Date('2026-01-01T00:00:00Z') });
  await page.clock.pauseAt(new Date('2026-01-01T00:00:01Z'));
}

// Only native IPC is mocked. Dirty propagation, navigation guards and edit sessions are real.
async function mountApp(page: Page, noPlans = false): Promise<void> {
  await page.clock.install({ time: new Date('2026-01-01T00:00:00Z') });
  await page.clock.pauseAt(new Date('2026-01-01T00:00:01Z'));
  await page.addInitScript(({ noPlans }) => {
    const base = { Version: 18, __externalUiRevision: 'a'.repeat(64), StageDurationSeconds: 1800, Stages: [{ Name: 'Training', Percent: 100, EnergyFocus: 5, MagicFocus: 6 }], RebirthHistory: [] };
    const fixture: AppAutosaveHarness = window.__appAutosave = {
      answer: false, prompts: [], folders: [], writes: [], patches: [],
      plans: noPlans ? {} : { alpha: { ...base, Name: 'alpha', BloodSpaghettiGoalPercent: 35, IronPillSaveSeconds: 900 }, beta: { ...base, Name: 'beta', BloodSpaghettiGoalPercent: 10 } },
      settings: { _customPlanFile: 'alpha', _customBloodSpaghettiGoalPercent: 35, _customBloodCounterfeitGoalPercent: 450, _customIronPillSaveSeconds: 900, _customIronPillMinWorth: 0 }
    };
    let revision = 1;
    Object.assign(window, { __TAURI_INTERNALS__: { invoke: async (command: string, args: Record<string, unknown> = {}) => {
      const casPath = '/e2e/fixtures/settings-cas.ts';
      const cas = await import(casPath);
      switch (command) {
        case 'get_ngui_state': {
          const dir = String(args.runtimeDir || 'C:\\AutosaveFixture');
          fixture.folders.push(dir);
          return { runtime_dir: dir, marker_enabled: false, connected: false, protocol: { version: 3 }, ...await cas.settingsSnapshot(fixture.settings), plans: Object.keys(fixture.plans), profiles: [], logs: [], command_results: [], runtime: { externalUiEnabled: false, status: 'unloaded', updatedAt: new Date().toISOString(), startedAt: 'autosave-session', protocolVersion: 3, settingsCasVersion: 1, supportedCommands: [], planVersion: 18 } };
        }
        case 'read_plan': return JSON.stringify(fixture.plans[String(args.name)]);
        case 'save_plan': {
          const name = String(args.name);
          const plan = JSON.parse(String(args.planJson));
          fixture.writes.push({ name, plan: structuredClone(plan) });
          fixture.plans[name] = { ...plan, __externalUiRevision: (++revision).toString(16).padStart(64, '0') };
          return { success: true, mode: 'offline' };
        }
        case 'save_settings_patch_json': {
          const receipt = await cas.commitSettings(fixture.settings, args);
          fixture.patches.push(cas.settingsPatch(args));
          return receipt;
        }
        case 'plugin:dialog|message': fixture.prompts.push(String(args.message)); return fixture.answer ? 'Yes' : 'No';
        default: throw new Error(`Unexpected native call: ${command}`);
      }
    } } });
    localStorage.clear();
  }, { noPlans });
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toBeEnabled();
  if (!noPlans) await expect(page.getByRole('combobox', { name: 'Plan', exact: true })).toHaveValue('alpha');
}

async function unloadPrevented(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    const event = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  });
}

async function openNumber(page: Page) {
  await page.getByRole('button', { name: 'Edit Target', exact: true }).click();
  return page.getByRole('spinbutton', { name: 'Target', exact: true });
}

const source = '{"Name":"test","Version":17,"Future":{"counter":9007199254740993},"Target":20}';
const changed = (target: number) => source.replace('"Target":20', `"Target":${target}`);
const jsonInput = (page: Page) => page.getByRole('textbox', { name: 'Advanced Plan JSON', exact: true });
const attempts = (page: Page) => page.evaluate(() => window.__autosave.attempts);

test('inline numbers debounce rapid valid edits without Save or Enter and renew sessions', async ({ page }) => {
  await mountEditor(page, 'InlineNumber');
  const input = await openNumber(page);
  await expect(input).toBeFocused();
  await input.fill('30');
  await page.clock.runFor(200);
  await input.fill('35');
  await page.clock.runFor(349);
  expect(await attempts(page)).toEqual([]);
  await page.clock.runFor(1);
  await expect.poll(() => attempts(page)).toEqual([{ session: 1, value: '35' }]);
  await expect(input).toHaveValue('35');
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toHaveCount(0);
  await input.fill('40');
  await page.clock.runFor(350);
  await expect.poll(() => attempts(page)).toEqual([{ session: 1, value: '35' }, { session: 2, value: '40' }]);
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  expect(await page.evaluate(() => window.__autosave.cancels)).toEqual([]);
  await expect(page.getByRole('button', { name: 'Edit Target', exact: true })).toHaveText('40');
});

test('inline numbers retain the last edit made during an in-flight write', async ({ page }) => {
  await mountEditor(page, 'InlineNumber', '20', { deferred: true });
  const input = await openNumber(page);
  await input.fill('30');
  await page.clock.runFor(350);
  await expect.poll(() => attempts(page)).toHaveLength(1);
  await expect(input).toBeEnabled();
  await expect(page.getByRole('group', { name: 'Target', exact: true })).toHaveAttribute('aria-busy', 'true');
  await expect(page.locator('#fixture')).not.toContainText('Saving...');
  await input.fill('40');
  await page.clock.runFor(200);
  await input.fill('45');
  await page.clock.runFor(350);
  expect(await attempts(page)).toHaveLength(1);
  await page.evaluate(() => window.__autosave.resolve());
  await expect.poll(() => attempts(page)).toEqual([{ session: 1, value: '30' }, { session: 2, value: '45' }]);
  await page.evaluate(() => window.__autosave.resolve());
  await expect(input).toHaveValue('45');
  await expect(page.getByRole('group', { name: 'Target', exact: true })).toHaveAttribute('aria-busy', 'false');
});

test('inline dirty state covers debounce, in-flight work and newer text, not an untouched open editor', async ({ page }) => {
  await mountEditor(page, 'InlineNumber', '20', { deferred: true });
  const input = await openNumber(page);
  expect(await page.evaluate(() => window.__autosave.dirty.at(-1))).toBe(false);
  await input.fill('30');
  expect(await page.evaluate(() => window.__autosave.dirty.at(-1))).toBe(true);
  await page.clock.runFor(350);
  await expect.poll(() => attempts(page)).toHaveLength(1);
  await input.fill('40');
  await page.evaluate(() => window.__autosave.resolve());
  await expect(page.getByRole('group', { name: 'Target', exact: true })).toHaveAttribute('aria-busy', 'false');
  expect(await page.evaluate(() => window.__autosave.dirty)).toEqual([false, true, true, true]);
  await page.clock.runFor(350);
  await expect.poll(() => attempts(page)).toHaveLength(2);
  await page.evaluate(() => window.__autosave.resolve());
  await expect.poll(() => page.evaluate(() => window.__autosave.dirty.at(-1))).toBe(false);
  await expect(input).toBeVisible();
  await input.fill('');
  expect(await page.evaluate(() => window.__autosave.dirty.at(-1))).toBe(true);
  await page.evaluate(() => window.__autosave.destroy());
  expect(await page.evaluate(() => window.__autosave.dirty.at(-1))).toBe(false);
});

test('Enter immediately saves dirty inline input without requiring a manual save button', async ({ page }) => {
  await mountEditor(page, 'InlineNumber');
  const input = await openNumber(page);
  await input.fill('35');
  await input.press('Enter');
  await expect.poll(() => attempts(page)).toEqual([{ session: 1, value: '35' }]);
  expect(await page.evaluate(() => window.__autosave.dirty.at(-1))).toBe(false);
  await input.press('Enter');
  expect(await attempts(page)).toHaveLength(1);
  await input.press('Escape');
  expect(await page.evaluate(() => window.__autosave.cancels)).toEqual([]);
  await expect(page.getByRole('button', { name: 'Edit Target', exact: true })).toHaveText('35');
});

test('a successful inline save keeps subsequent edits tied to the original editor context', async ({ page }) => {
  await mountEditor(page, 'InlineNumber');
  const input = await openNumber(page);
  await input.fill('30');
  await page.clock.runFor(350);
  await expect.poll(() => attempts(page)).toEqual([{ session: 1, value: '30' }]);
  await page.evaluate(() => window.__autosave.changeEditor('70'));
  await input.fill('40');
  await page.clock.runFor(350);
  await expect(page.getByRole('alert')).toContainText('Not saved. Your edit is kept.');
  await expect(input).toHaveValue('40');
  expect(await attempts(page)).toEqual([{ session: 1, value: '30' }]);
  await input.press('Escape');
  await expect(page.getByRole('button', { name: 'Edit Target', exact: true })).toHaveText('70');
});

test('inline cancel drops queued input but never rolls back a successful in-flight write', async ({ page }) => {
  await mountEditor(page, 'InlineNumber', '20', { deferred: true });
  const input = await openNumber(page);
  await input.fill('30');
  await page.clock.runFor(350);
  await expect.poll(() => attempts(page)).toHaveLength(1);
  await input.fill('40');
  await input.press('Escape');
  await page.evaluate(() => window.__autosave.resolve());
  await page.clock.runFor(1000);
  expect(await attempts(page)).toEqual([{ session: 1, value: '30' }]);
  await expect(page.getByRole('button', { name: 'Edit Target', exact: true })).toHaveText('30');
});

test('inline validation stays local and newer invalid text survives an earlier success', async ({ page }) => {
  await mountEditor(page, 'InlineNumber', '20', { deferred: true });
  const input = await openNumber(page);
  for (const invalid of ['', '1.5', '-1', '101']) {
    await input.fill(invalid);
    await expect(page.getByRole('alert')).toBeVisible();
    await page.clock.runFor(400);
  }
  expect(await attempts(page)).toEqual([]);
  await input.fill('30');
  await page.clock.runFor(350);
  await expect.poll(() => attempts(page)).toHaveLength(1);
  await input.fill('');
  await page.evaluate(() => window.__autosave.resolve());
  await expect(input).toHaveValue('');
  await expect(page.getByRole('alert')).toContainText('required');
  await page.clock.runFor(400);
  expect(await attempts(page)).toHaveLength(1);
});

test('inline failures retain the draft and retry the same isolated session on correction', async ({ page }) => {
  await mountEditor(page, 'InlineNumber', '20', { deferred: true, integer: false });
  const input = await openNumber(page);
  await input.fill('30.5');
  await page.clock.runFor(350);
  await expect.poll(() => attempts(page)).toHaveLength(1);
  await page.evaluate(() => window.__autosave.resolve(false));
  await expect(page.getByRole('alert')).toContainText('Not saved. Your edit is kept.');
  await expect(input).toHaveValue('30.5');
  await page.clock.runFor(1000);
  expect(await attempts(page)).toHaveLength(1);
  await input.fill('35.5');
  await page.clock.runFor(350);
  await expect.poll(() => attempts(page)).toEqual([{ session: 1, value: '30.5' }, { session: 1, value: '35.5' }]);
  await page.evaluate(() => window.__autosave.resolve());
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('inline edits wait for external busy work and destruction cancels debounce', async ({ page }) => {
  await mountEditor(page, 'InlineNumber');
  const input = await openNumber(page);
  await input.fill('30');
  await page.evaluate(() => window.__autosave.setDisabled(true));
  await expect(input).toBeDisabled();
  await page.clock.runFor(400);
  expect(await attempts(page)).toEqual([]);
  await page.evaluate(() => window.__autosave.setDisabled(false));
  await expect.poll(() => attempts(page)).toHaveLength(1);
  await input.fill('40');
  await page.evaluate(() => window.__autosave.destroy());
  await page.clock.runFor(400);
  expect(await attempts(page)).toHaveLength(1);
});

test('blood goal drafts stay mounted across telemetry availability changes', async ({ page }) => {
  await mountEditor(page, 'BloodGoals', '{"spaghetti":20,"counterfeit":400}');
  await page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true }).click();
  const input = page.getByRole('spinbutton', { name: 'Spaghetti goal (%)', exact: true });
  await input.fill('35');
  await page.evaluate(() => window.__autosave.setConnected(false));
  await expect(input).toHaveValue('35');
  await page.evaluate(() => window.__autosave.setConnected(true));
  await expect(input).toHaveValue('35');
  await page.clock.runFor(350);
  await expect.poll(() => attempts(page)).toEqual([{ session: 1, value: '35' }]);
});

test('advanced JSON autosaves valid edits losslessly without confirmation or Apply', async ({ page }) => {
  await mountEditor(page, 'JsonEditor', source);
  await jsonInput(page).fill(changed(30));
  await page.clock.runFor(200);
  await jsonInput(page).fill(changed(35));
  await page.clock.runFor(349);
  expect(await page.evaluate(() => window.__autosave.applied)).toEqual([]);
  await page.clock.runFor(1);
  await expect.poll(() => page.evaluate(() => window.__autosave.applied)).toEqual([changed(35)]);
  await expect(jsonInput(page)).toHaveValue(/9007199254740993/);
  await expect(page.getByRole('button', { name: 'Apply edits', exact: true })).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => window.__autosave.dirty.at(-1))).toBe(false);
  expect(await page.evaluate(() => window.__autosave.dirty)).toContain(true);
});

test('advanced JSON invalid or incomplete edits remain local with feedback and discard', async ({ page }) => {
  await mountEditor(page, 'JsonEditor', source);
  const editor = jsonInput(page);
  for (const invalid of ['{', '[]', source.replace('"test"', '"changed"'), '{"Name":"test","Version":17,"Target":20}']) {
    await editor.fill(invalid);
    await expect(page.getByRole('alert')).toBeVisible();
    await page.clock.runFor(400);
    await expect(editor).toHaveValue(invalid);
  }
  expect(await page.evaluate(() => window.__autosave.applied)).toEqual([]);
  await expect(page.getByRole('alert')).toHaveText('Keep the existing Future field.');
  await page.evaluate(() => window.__autosave.setValue('{"Name":"test","Version":17,"Future":{"counter":9007199254740993},"Target":50}'));
  await expect(editor).toHaveValue('{"Name":"test","Version":17,"Target":20}');
  await expect(page.getByText('Source changed. Resolve conflicts or discard edits.')).toBeVisible();
  await page.getByRole('button', { name: 'Discard edits', exact: true }).click();
  await expect(editor).toHaveValue(/"Target": 50/);
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => window.__autosave.dirty.at(-1))).toBe(false);
});

test('advanced JSON serializes async applies without losing newer edits or their baseline', async ({ page }) => {
  await mountEditor(page, 'JsonEditor', source, { deferred: true });
  const editor = jsonInput(page);
  await editor.fill(changed(30));
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => window.__autosave.applied)).toHaveLength(1);
  await page.evaluate(() => window.__autosave.setDisabled(true));
  await expect(editor).toBeEnabled();
  await editor.fill(changed(40));
  await page.clock.runFor(350);
  await page.evaluate(() => { window.__autosave.setDisabled(false); window.__autosave.resolve(); });
  await expect.poll(() => page.evaluate(() => window.__autosave.applied)).toEqual([changed(30), changed(40)]);
  expect(await page.evaluate(() => window.__autosave.bases)).toEqual([source, changed(30)]);
  await expect(editor).toHaveValue(changed(40));
  await page.evaluate(() => window.__autosave.resolve());
  await expect.poll(() => page.evaluate(() => window.__autosave.dirty.at(-1))).toBe(false);
});

test('advanced JSON callback errors keep dirty drafts without an automatic retry loop', async ({ page }) => {
  await mountEditor(page, 'JsonEditor', source, { fail: true });
  await jsonInput(page).fill(changed(30));
  await page.clock.runFor(350);
  await expect(page.getByRole('alert')).toHaveText('Source changed. Edits retained.');
  await expect(jsonInput(page)).toHaveValue(changed(30));
  expect(await page.evaluate(() => window.__autosave.dirty.at(-1))).toBe(true);
  await page.clock.runFor(1000);
  expect(await page.evaluate(() => window.__autosave.applied)).toHaveLength(1);
});

test('advanced JSON discard cancels queued edits, including edits after an in-flight apply', async ({ page }) => {
  await mountEditor(page, 'JsonEditor', source, { deferred: true });
  await jsonInput(page).fill(changed(25));
  await page.getByRole('button', { name: 'Discard edits', exact: true }).click();
  await page.clock.runFor(400);
  expect(await page.evaluate(() => window.__autosave.applied)).toEqual([]);
  await jsonInput(page).fill(changed(30));
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => window.__autosave.applied)).toHaveLength(1);
  await jsonInput(page).fill(changed(40));
  await page.getByRole('button', { name: 'Discard edits', exact: true }).click();
  await page.evaluate(() => window.__autosave.resolve());
  await page.clock.runFor(400);
  expect(await page.evaluate(() => window.__autosave.applied)).toEqual([changed(30)]);
  await expect(jsonInput(page)).toHaveValue(/"Target": 30/);
  await expect.poll(() => page.evaluate(() => window.__autosave.dirty.at(-1))).toBe(false);
});

for (const label of ['Spaghetti goal (%)', 'Iron Pill saving time (s)', 'Iron Pill minimum worth']) {
  test(`App guards a plan switch before the debounce for ${label}`, async ({ page }) => {
    await mountApp(page);
    await page.getByRole('button', { name: `Edit ${label}`, exact: true }).click();
    const input = page.getByRole('spinbutton', { name: label, exact: true });
    await input.fill('72');
    const select = page.getByRole('combobox', { name: 'Plan', exact: true });
    await select.selectOption('beta');
    await expect(select).toHaveValue('alpha');
    await expect(input).toHaveValue('72');
    expect(await page.evaluate(() => window.__appAutosave.prompts)).toEqual(['Discard unsaved plan / advanced JSON edits and load another plan?']);
    expect(await page.evaluate(() => window.__appAutosave.writes)).toEqual([]);
    expect(await unloadPrevented(page)).toBe(true);
    await page.evaluate(() => { window.__appAutosave.answer = true; });
    await select.selectOption('beta');
    await expect(select).toHaveValue('beta');
    await expect(input).toHaveCount(0);
    await page.clock.runFor(400);
    expect(await page.evaluate(() => window.__appAutosave.writes)).toEqual([]);
    expect(await unloadPrevented(page)).toBe(false);
  });
}

for (const noPlans of [false, true]) {
  const mode = noPlans ? 'global settings' : 'plan settings';
  test(`App prevents synchronous unload before debounce in ${mode}`, async ({ page }) => {
    await mountApp(page, noPlans);
    await page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true }).click();
    expect(await unloadPrevented(page)).toBe(false);
    const prevented = await page.evaluate(() => {
      const input = document.querySelector<HTMLInputElement>('input[aria-label="Spaghetti goal (%)"]')!;
      input.value = '72';
      input.dispatchEvent(new Event('input', { bubbles: true }));
      const event = new Event('beforeunload', { cancelable: true });
      window.dispatchEvent(event);
      return event.defaultPrevented;
    });
    expect(prevented).toBe(true);
    expect(await page.evaluate(() => [window.__appAutosave.writes, window.__appAutosave.patches])).toEqual([[], []]);
    await page.getByRole('spinbutton', { name: 'Spaghetti goal (%)', exact: true }).press('Escape');
    expect(await unloadPrevented(page)).toBe(false);
  });

  test(`App guards folder switches before debounce in ${mode}`, async ({ page }) => {
    await mountApp(page, noPlans);
    if (!noPlans) {
      await page.getByRole('button', { name: 'Plan & Rebirth', exact: true }).click();
      await page.getByText('Schedule & templates', { exact: true }).click();
      await page.getByRole('textbox', { name: 'Fallback time budget', exact: true }).fill('');
      await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
    }
    await page.getByRole('button', { name: 'Edit Iron Pill saving time (s)', exact: true }).click();
    await page.getByRole('spinbutton', { name: 'Iron Pill saving time (s)', exact: true }).fill('1200');
    await page.getByRole('button', { name: 'Advanced', exact: true }).click();
    await page.getByText('Data & Setup', { exact: true }).click();
    const folder = page.getByRole('textbox', { name: 'Data folder', exact: true });
    await folder.fill('C:\\AnotherAutosaveFixture');
    await page.getByRole('button', { name: 'Use folder', exact: true }).click();
    await expect(folder).toHaveValue('C:\\AutosaveFixture');
    expect(await page.evaluate(() => window.__appAutosave.prompts)).toEqual(['Discard unsaved changes and switch data folder?']);
    expect(await page.evaluate(() => window.__appAutosave.folders)).not.toContain('C:\\AnotherAutosaveFixture');
    expect(await unloadPrevented(page)).toBe(true);
    await page.evaluate(() => { window.__appAutosave.answer = true; });
    await folder.fill('C:\\AnotherAutosaveFixture');
    await page.getByRole('button', { name: 'Use folder', exact: true }).click();
    await expect(folder).toHaveValue('C:\\AnotherAutosaveFixture');
    await page.clock.runFor(400);
    expect(await page.evaluate(() => [window.__appAutosave.writes, window.__appAutosave.patches])).toEqual([[], []]);
    expect(await page.evaluate(() => window.__appAutosave.prompts)).toEqual(Array(2).fill('Discard unsaved changes and switch data folder?'));
    expect(await unloadPrevented(page)).toBe(false);
  });
}

test('App combines both BloodGoals drafts without a clean or canceled sibling clearing dirty work', async ({ page }) => {
  await mountApp(page);
  await page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true }).click();
  const spaghetti = page.getByRole('spinbutton', { name: 'Spaghetti goal (%)', exact: true });
  await spaghetti.fill('');
  await page.getByRole('button', { name: 'Edit Counterfeit goal (%)', exact: true }).click();
  expect(await unloadPrevented(page)).toBe(true);
  const counterfeit = page.getByRole('spinbutton', { name: 'Counterfeit goal (%)', exact: true });
  await counterfeit.fill('');
  await spaghetti.press('Escape');
  expect(await unloadPrevented(page)).toBe(true);
  await counterfeit.press('Escape');
  expect(await unloadPrevented(page)).toBe(false);
});

test('App aggregates PlanEditor and inline drafts instead of letting their callbacks overwrite each other', async ({ page }) => {
  await mountApp(page);
  await page.getByRole('button', { name: 'Plan & Rebirth', exact: true }).click();
  await page.getByText('Schedule & templates', { exact: true }).click();
  const schedule = page.getByRole('textbox', { name: 'Fallback time budget', exact: true });
  await schedule.fill('');
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await page.getByRole('button', { name: 'Edit Iron Pill minimum worth', exact: true }).click();
  const worth = page.getByRole('spinbutton', { name: 'Iron Pill minimum worth', exact: true });
  await worth.fill('');
  await worth.press('Escape');
  expect(await unloadPrevented(page)).toBe(true);
  await page.getByRole('button', { name: 'Edit Iron Pill minimum worth', exact: true }).click();
  await worth.fill('');
  await page.getByRole('button', { name: 'Plan & Rebirth', exact: true }).click();
  await schedule.press('Escape');
  expect(await unloadPrevented(page)).toBe(true);
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await worth.press('Escape');
  expect(await unloadPrevented(page)).toBe(false);
});

test('App saves Enter input before a fast plan switch and Cancel never rolls back the saved value', async ({ page }) => {
  await mountApp(page);
  await page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true }).click();
  const input = page.getByRole('spinbutton', { name: 'Spaghetti goal (%)', exact: true });
  await input.fill('72');
  await input.press('Enter');
  await page.getByRole('combobox', { name: 'Plan', exact: true }).selectOption('beta');
  await expect.poll(() => page.evaluate(() => window.__appAutosave.plans.alpha.BloodSpaghettiGoalPercent)).toBe(72);
  expect(await page.evaluate(() => window.__appAutosave.prompts)).toEqual([]);
  expect(await unloadPrevented(page)).toBe(false);
  await page.getByRole('combobox', { name: 'Plan', exact: true }).selectOption('alpha');
  await page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true }).click();
  await input.fill('80');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => window.__appAutosave.plans.alpha.BloodSpaghettiGoalPercent)).toBe(80);
  await expect(page.locator('.status-line')).toBeEmpty();
  expect(await unloadPrevented(page)).toBe(false);
  await input.fill('90');
  await input.press('Escape');
  expect(await unloadPrevented(page)).toBe(false);
  await page.clock.runFor(400);
  expect(await page.evaluate(() => window.__appAutosave.plans.alpha.BloodSpaghettiGoalPercent)).toBe(80);
  await expect(page.getByRole('button', { name: 'Edit Spaghetti goal (%)', exact: true })).toHaveText('80%');
});
