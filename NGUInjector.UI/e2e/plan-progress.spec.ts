import { expect, test, type Page } from '@playwright/test';
import { stagePresets } from './fixtures/activity-policy';

function snapshot(patch: Record<string, unknown> = {}) {
  const active = typeof patch.activeStageIndex === 'number' ? patch.activeStageIndex : 2;
  return {
    available: true, status: 'running', planName: '8/15/30m NUMBER', planVersion: 18,
    schedulerMode: 'stages', activeStageIndex: 2, stageProgress: .25, elapsedSeconds: 675,
    stageElapsedSeconds: 75, cycle: 2, repeating: true, loopStartIndex: 5,
    stages: ([
      ['Time Machine opener', 1, 1, 5, 'TM', 'TM'],
      ['Augments + Blood', 2, 4, 10, 'AUG', 'RIT-0'],
      ['Time Machine retry', 1, 1, 5, 'TM', 'TM'],
      ['Augments top-up', 2, 4, 10, 'AUG', 'RIT-0'],
      ['Wandoos push', 3, 3, 12, 'WAN', 'WAN'],
      ['Time Machine long', 1, 1, 5, 'TM', 'TM'],
      ['Augments + Blood long', 2, 4, 10, 'AUG', 'RIT-0'],
      ['Wandoos long', 3, 3, 12, 'WAN', 'WAN']
    ] as const).map(([name, energyFocus, magicFocus, gearGoal, energy, magic], index) => ({
      name, index, durationSeconds: 300, configuredDurationSeconds: 300, flexWeight: 100, timingKind: 'fixed', completionMode: 0, energyFocus, magicFocus, gearGoal,
      state: index === active ? 'current' : index < active ? 'completed' : 'pending', appliedSeconds: index === active ? 75 : index < active ? 300 : 0, elapsedSeconds: index === active ? 100 : index < active ? 350 : 0, visits: index <= active ? 1 : 0,
      configuredEnergyPriorities: [], configuredMagicPriorities: [], energyPriorities: [energy], magicPriorities: [magic]
    })), ...patch
  };
}

type FixtureWindow = Window & {
  __planEdits: number[];
  __nativeCalls: string[];
  __updatePlanProgress: (next: Record<string, unknown>, fresh: boolean) => void;
};

// Isolated component fixture: App/controller never mount and there is no native/game transport.
async function mountProgress(page: Page, data = snapshot(), connected = true, editable = true) {
  await page.route(/http:\/\/127\.0\.0\.1:\d+\/$/, route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><head><link rel="stylesheet" href="/src/styles.css"></head><body><main class="shell" id="fixture"></main></body></html>' }));
  await page.goto('/');
  const main = await (await page.request.get('/src/main.ts')).text();
  const runtimePath = main.match(/import\s*\{\s*mount\s*\}\s*from\s*["']([^"']+)["']/)?.[1];
  if (!runtimePath) throw new Error('Could not find the served Svelte runtime.');
  await page.evaluate(async ({ runtimePath, data, connected, editable }) => {
    const { mount } = await import(runtimePath);
    const fixturePath = '/e2e/fixtures/PlanProgressHarness.svelte';
    const { default: Fixture } = await import(fixturePath);
    const state = window as unknown as FixtureWindow;
    state.__planEdits = [];
    state.__nativeCalls = [];
    Object.assign(state, { __TAURI_INTERNALS__: { invoke: async (command: string) => {
      state.__nativeCalls.push(command);
      throw new Error(`Unexpected native action: ${command}`);
    } } });
    const instance = mount(Fixture, { target: document.getElementById('fixture'), props: {
      data, connected, onedit: editable ? (index: number) => state.__planEdits.push(index) : undefined
    } });
    state.__updatePlanProgress = instance.update;
  }, { runtimePath, data, connected, editable });
  await expect(page.getByRole('region', { name: 'Plan progression', exact: true })).toBeVisible();
}

test('all eight parts are readable in one desktop row or wrapped on mobile, without page overflow', async ({ page }, testInfo) => {
  await mountProgress(page);
  const stages = page.getByRole('list', { name: 'Active runtime plan stages' }).getByRole('listitem');
  await expect(stages).toHaveCount(8);
  const labels = ['Time Machine', 'Augments + Blood', 'Time Machine', 'Augments + Blood', 'Wandoos', 'Time Machine', 'Augments + Blood', 'Wandoos'];
  const allocations: Record<string, string[]> = {
    'Time Machine': ['E Time Machine / TM', 'M Time Machine / TM'],
    'Augments + Blood': ['E Augments / AUG', 'M Blood Magic / RIT-0'],
    Wandoos: ['E Wandoos / WAN', 'M Wandoos / WAN']
  };
  for (let i = 0; i < 8; i++) {
    await expect(stages.nth(i)).toBeVisible();
    await expect(stages.nth(i).locator('.stage-name')).toHaveText(labels[i]);
    await expect(stages.nth(i).getByRole('button')).toHaveAccessibleName(`Edit stage ${i + 1}: ${labels[i]}`);
    await expect(stages.nth(i).locator('.duration').first()).toHaveText('5m');
    await expect(stages.nth(i).locator('.timing-badge')).toHaveText('Fixed duration');
    await expect(stages.nth(i).locator('.duration')).toHaveCount(1);
    await expect(stages.nth(i)).not.toContainText('Configured:');
    await expect(stages.nth(i).locator('.allocation').nth(0)).toHaveText(allocations[labels[i]][0]);
    await expect(stages.nth(i).locator('.allocation').nth(1)).toHaveText(allocations[labels[i]][1]);
  }
  const boxes = await stages.evaluateAll(elements => elements.map(element => {
    const box = element.getBoundingClientRect();
    return { x: box.x, y: box.y, width: box.width, height: box.height };
  }));
  if (testInfo.project.name === 'desktop') {
    expect(new Set(boxes.map(box => box.y)).size).toBe(1);
    expect(Math.max(...boxes.map(box => box.height))).toBeLessThan(320);
  } else {
    expect(new Set(boxes.map(box => box.y)).size).toBe(4);
    expect(Math.min(...boxes.map(box => box.width))).toBeGreaterThan(120);
  }
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await expect(page.locator('[aria-current="step"]')).toHaveCount(1);
  await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '25');
  await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuetext', 'Running: Time Machine; 25% complete');
});

test('keyboard and pointer stage navigation only emit the zero-based editor index', async ({ page }) => {
  await mountProgress(page);
  const second = page.getByRole('button', { name: 'Edit stage 2: Augments + Blood', exact: true });
  await second.focus();
  await second.press('Enter');
  await second.press('Space');
  await page.getByRole('button', { name: 'Edit stage 8: Wandoos', exact: true }).click();
  expect(await page.evaluate(() => (window as unknown as FixtureWindow).__planEdits)).toEqual([1, 1, 7]);
  expect(await page.evaluate(() => (window as unknown as FixtureWindow).__nativeCalls)).toEqual([]);
  await expect(page.locator('[aria-current="step"]')).toContainText('Time Machine');
  await expect(page.locator('[aria-current="step"]')).not.toContainText('Time Machine retry');
  await expect(page.locator('[aria-current="step"]').getByRole('button')).toHaveAccessibleName('Edit stage 3: Time Machine');
  await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '25');
});

for (const presetId of ['global_3', '@active']) {
  test(`weighted runtime ${presetId} renders its NGU binding instead of the retained Time Machine tuple`, async ({ page }) => {
    const data = snapshot({ activePresetId: 'global_3' });
    Object.assign(data.stages[2], { presetId, resolvedPresetId: 'global_3', presetName: 'Captured Energy NGU' });
    await mountProgress(page, data);
    const current = page.locator('[aria-current="step"]');
    await expect(current.locator('.stage-name')).toHaveText('Captured Energy NGU');
    await expect(current.getByRole('button')).toHaveAccessibleName('Edit stage 3: Captured Energy NGU');
    await expect(current.locator('.allocation')).toHaveText(['Energy, magic & gear: preset settings']);
    await expect(current.locator('.gear')).toHaveCount(0);
    await expect(current).not.toContainText('Time Machine');
    await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuetext', 'Running: Captured Energy NGU; 25% complete');
    await current.getByRole('button').click();
    expect(await page.evaluate(() => (window as unknown as FixtureWindow).__planEdits)).toEqual([2]);
    expect(await page.evaluate(() => (window as unknown as FixtureWindow).__nativeCalls)).toEqual([]);
    const next = { ...data, activePresetId: 'changed_runtime_id', stages: data.stages.map((row, index) => index === 2 ? { ...row, presetName: 'Next captured policy' } : row) };
    await page.evaluate(next => (window as unknown as FixtureWindow).__updatePlanProgress(next, true), next);
    await expect(current.locator('.stage-name')).toHaveText('Next captured policy');
    await page.evaluate(next => (window as unknown as FixtureWindow).__updatePlanProgress(next, false), next);
    await expect(page.locator('[aria-current="step"]')).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

test('automatic elapsed-budget stages show phase ownership instead of preserved standard controls', async ({ page }) => {
  const data = snapshot();
  const stages = data.stages.map((row, index) => index === 2 ? { ...row, executionMode: 1, timingKind: 'elapsed' } : row);
  await mountProgress(page, snapshot({ stages }));
  const current = page.locator('[aria-current="step"]');
  await expect(current.locator('.stage-name')).toHaveText('Augments + Wandoos (automatic, includes AT)');
  await expect(current.locator('.timing-badge')).toHaveText('Total elapsed budget');
  await expect(current).toContainText('Automatic phase gear');
  await expect(current).not.toContainText('M Time Machine');
  await expect(page.locator('.current-summary')).toContainText('1m 15s elapsed budget used');
  await current.getByText('Runtime history', { exact: true }).click();
  await expect(current).toContainText('Total elapsed budget (includes AT, planning and setup)');
  await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '25');
  expect(await page.evaluate(() => (window as unknown as FixtureWindow).__nativeCalls)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('cleanup recovery does not present a consumed budget as a finished run or resumed stage', async ({ page }) => {
  const combatPrep = { schemaVersion: 1, active: true, cleanupPending: true, status: 'failed', owner: 'scheduled' };
  await mountProgress(page, snapshot({ stageProgress: 1, scheduleComplete: true, combatPrep }));
  const region = page.getByRole('region', { name: 'Plan progression', exact: true });
  await expect(region.locator('.status')).toHaveText('Cleanup pending');
  await expect(region).toContainText('still owns equipment and resources');
  await expect(region).not.toContainText('Sustaining');
  await expect(region).not.toContainText('Schedule complete');
  await expect(region.getByRole('progressbar')).toHaveAttribute('aria-valuetext', 'Cleanup pending: combat prep still owns equipment and resources.');
  expect(await page.evaluate(() => (window as unknown as FixtureWindow).__nativeCalls)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('inactive recovery shows the persistence blocker without requiring stage acknowledgement', async ({ page }) => {
  const data = snapshot({ cleanupRecoveryRequired: true, reason: 'Checkpoint write failed: file locked. Retrying automatically.', scheduleComplete: true, stageProgress: 1, combatPrep: { schemaVersion: 1, active: false, cleanupPending: false } });
  await mountProgress(page, data);
  const region = page.getByRole('region', { name: 'Plan progression', exact: true });
  await expect(region.locator('.status')).toHaveText('Reconciling');
  await expect(region.getByRole('status')).toHaveText('Reconciling: Checkpoint write failed: file locked. Retrying automatically.');
  await expect(region).not.toContainText(/acknowledge|Restart current stage|Set as current stage|Skip to selected stage/);
  await expect(region).not.toContainText('Sustaining');
  await expect(region).not.toContainText('Schedule complete');
  await expect(region.getByRole('progressbar')).toHaveAttribute('aria-valuetext', 'Reconciling: Checkpoint write failed: file locked. Retrying automatically.');
  expect(await page.evaluate(() => (window as unknown as FixtureWindow).__nativeCalls)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('runtime base preset labels ignore customized or missing priority orders', async ({ page }) => {
  const row = snapshot().stages[0];
  const stages = stagePresets.map((preset, index) => ({
    ...row, index, state: index === 2 ? 'current' : index < 2 ? 'completed' : 'pending', name: 'Unrelated saved descriptor', energyFocus: preset.EnergyFocus, magicFocus: preset.MagicFocus, gearGoal: preset.GearGoal,
    configuredEnergyPriorities: index % 2 ? ['AT'] : undefined,
    configuredMagicPriorities: index % 2 ? ['RIT-1'] : undefined,
    energyPriorities: ['TM'], magicPriorities: ['RIT-0']
  }));
  await mountProgress(page, snapshot({ stages }));
  await expect(page.locator('.stage-name')).toHaveText(stagePresets.map(preset => preset.label));
  await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuetext', 'Running: Wandoos; 25% complete');
  for (const [index, preset] of stagePresets.entries()) {
    await expect(page.getByRole('button', { name: `Edit stage ${index + 1}: ${preset.label}`, exact: true })).toBeVisible();
  }
  expect(await page.evaluate(() => (window as unknown as FixtureWindow).__nativeCalls)).toEqual([]);
});

test('runtime unknown focus and gear stay Custom despite a saved preset name', async ({ page }) => {
  const data = snapshot();
  data.stages[2] = { ...data.stages[2], name: 'Time Machine', gearGoal: -1 };
  await mountProgress(page, data);
  await expect(page.locator('.stage-name').nth(2)).toHaveText('Custom');
  await expect(page.getByRole('button', { name: 'Edit stage 3: Custom', exact: true })).toBeVisible();
  await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuetext', 'Running: Custom; 25% complete');
});

test('loss of freshness removes current highlighting and returning live data uses only the new position', async ({ page }) => {
  await mountProgress(page);
  await page.evaluate(data => (window as unknown as FixtureWindow).__updatePlanProgress(data, false), snapshot());
  await expect(page.getByRole('heading', { name: 'Waiting for plan progress' })).toBeVisible();
  await expect(page.locator('[aria-current="step"]')).toHaveCount(0);
  await expect(page.getByRole('progressbar')).toHaveCount(0);
  await expect(page.getByRole('button')).toHaveCount(0);
  await page.evaluate(data => (window as unknown as FixtureWindow).__updatePlanProgress(data, true), snapshot({ activeStageIndex: 5, cycle: 3, stageProgress: 0 }));
  await expect(page.locator('[aria-current="step"]')).toContainText('Time Machine');
  await expect(page.getByRole('progressbar')).toHaveAccessibleName('Stage 6 progress');
  await expect(page.locator('.stage-state').filter({ hasText: 'Completed' })).toHaveCount(5);
  await expect(page.locator('.context')).toContainText('Repeat 3 / Repeat from 6');
  await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '0');
});

for (const status of ['paused', 'interrupted']) {
  test(`${status} Quick override stays explicit and never appears running`, async ({ page }) => {
    await mountProgress(page, snapshot({ status, reason: 'Quick: Time Machine overrides the plan.', elapsedSeconds: 999999, timingReason: 'Restored saved progress; offline time is not protected training time.' }));
    await expect(page.getByRole('status')).toHaveText('Quick: Time Machine overrides the plan.');
    await expect(page.locator('.status')).toHaveText(status === 'paused' ? 'Paused' : 'Interrupted');
    await expect(page.locator('li.current')).toHaveCount(0);
    await expect(page.locator('li.held')).toHaveCount(1);
    await expect(page.locator('[aria-current="step"]')).toContainText('Time Machine');
    await expect(page.getByRole('progressbar')).toHaveAccessibleName('Stage 3 progress');
    await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuetext', `${status === 'paused' ? 'Paused' : 'Interrupted'}: Time Machine; 25% complete`);
    await expect(page.locator('.plan-progress')).not.toContainText('Restored saved progress');
    await expect(page.locator('.plan-progress')).not.toContainText('Fixed credited time is paused');
  });
}

test('mandatory AT progress stays indeterminate despite a very long elapsed duration', async ({ page }) => {
  const data = snapshot({ activeStageIndex: 0, stageProgress: null, elapsedSeconds: 999999 });
  data.stages[0] = { ...data.stages[0], completionMode: 1, timingKind: 'target', durationSeconds: 0, targetAdvancedTrainingPower: '9223372036854775806', targetAdvancedTrainingToughness: '6000' } as typeof data.stages[number];
  await mountProgress(page, data, true, false);
  await expect(page.locator('[aria-current="step"]')).toContainText('AT: P 9223372036854775806 / T 6000');
  await expect(page.locator('[aria-current="step"] .timing-badge')).toHaveText('AT targets');
  await expect(page.locator('[aria-current="step"] .duration')).toHaveCount(0);
  await expect(page.getByRole('progressbar')).not.toHaveAttribute('aria-valuenow');
  await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuetext', 'Running: Time Machine; progress not reported');
  await expect(page.locator('.plan-progress')).not.toContainText(/progress not reported/i);
  await expect(page.locator('.current-summary p')).not.toContainText('/');
  await expect(page.getByRole('button')).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('100% progress stays on the reported stage until fresh backend telemetry moves it', async ({ page }) => {
  await mountProgress(page, snapshot({ stageProgress: 1, stageElapsedSeconds: 999999 }));
  await expect(page.getByRole('progressbar')).toHaveAccessibleName('Stage 3 progress');
  await page.waitForTimeout(350);
  await expect(page.locator('[aria-current="step"]')).toContainText('Current');
  await expect(page.getByRole('progressbar')).toHaveAccessibleName('Stage 3 progress');
  await page.evaluate(data => (window as unknown as FixtureWindow).__updatePlanProgress(data, true), snapshot({ activeStageIndex: 3, stageProgress: 0 }));
  await expect(page.getByRole('progressbar')).toHaveAccessibleName('Stage 4 progress');
  await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '0');
  await expect(page.locator('.stage-state').filter({ hasText: 'Completed' })).toHaveCount(3);
  expect(await page.evaluate(() => (window as unknown as FixtureWindow).__nativeCalls)).toEqual([]);
});

test('routine telemetry is absent while compact estimates and sustaining stay readable', async ({ page }) => {
  const data = snapshot({ protectedWorkComplete: false, protectedSecondsRemaining: 0, reason: 'Waiting for mandatory AT targets', timingReason: 'Restored saved progress; offline time is not protected training time.', lastStageOverride: 'Set stage 3 as current at 2026-10-15T12:34:56Z', lastStageOverrideAt: 123456, scheduleComplete: false });
  data.stages[1] = { ...data.stages[1], timingKind: 'flexible', configuredDurationSeconds: 0, flexWeight: 35, durationSeconds: 0 };
  data.stages[2].configuredDurationSeconds = 600;
  await mountProgress(page, data);
  const region = page.getByRole('region', { name: 'Plan progression', exact: true });
  for (const noise of ['Auto-rebirth blocked', 'fixed time remaining', 'AT targets must also finish', 'Waiting for mandatory AT targets', 'Restored saved progress', 'Last manual action', '2026-10-15T12:34:56Z', '123456']) {
    expect(await region.innerHTML()).not.toContain(noise);
  }
  await expect(region.locator('.reason')).toHaveCount(0);
  await expect(page.locator('.duration').nth(1)).toHaveText('0s est.');
  await expect(page.locator('.duration').nth(1)).toHaveAttribute('title', 'Assigned estimate / weight 35');
  await expect(page.locator('.duration').nth(2)).toHaveText('5m / configured 10m');
  await expect(page.getByText('1m 15s credited in stage', { exact: false })).toBeVisible();
  data.stages[2].configuredDurationSeconds = 300;
  await page.evaluate(data => (window as unknown as FixtureWindow).__updatePlanProgress(data, true), { ...data, protectedWorkComplete: true, scheduleComplete: true });
  await expect(page.locator('.duration').nth(2)).toHaveText('5m');
  expect(await region.innerHTML()).not.toContain('Mandatory work complete.');
  expect(await region.innerHTML()).not.toContain('sustaining the reported allocation');
  expect(await region.innerHTML()).not.toContain('This does not mean skipped');
  await expect(page.locator('.status')).toHaveText('Sustaining');
  await expect(page.locator('.current-summary')).toContainText('Sustaining: 3. Time Machine');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('Blood goal catch-up is concise and only appears for fresh active catch-up', async ({ page }) => {
  const data = snapshot({ bloodGoalsPrioritized: true });
  await mountProgress(page, data);
  const catchUp = page.getByRole('status');
  await expect(catchUp).toHaveText('Blood goal catch-up');
  await expect(catchUp).toHaveAttribute('title', 'Spaghetti / Counterfeit Gold goals; energy and stage gear unchanged');
  await expect(page.locator('.plan-progress')).not.toContainText('Blood Magic is catching up');
  for (const patch of [{ bloodReservedForIronPill: true }, { bloodGoalsPrioritized: false }, { status: 'paused' }]) {
    await page.evaluate(data => (window as unknown as FixtureWindow).__updatePlanProgress(data, true), { ...data, ...patch });
    await expect(page.getByText('Blood goal catch-up', { exact: true })).toHaveCount(0);
  }
  await page.evaluate(data => (window as unknown as FixtureWindow).__updatePlanProgress(data, false), data);
  await expect(page.getByText('Blood goal catch-up', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Waiting for plan progress' })).toBeVisible();
});

for (const patch of [{ planVersion: 16 }, { schedulerMode: 'time' }, { schedulerMode: 'sequential' }, { schedulerMode: 'protected' }]) {
  test(`unsupported runtime ${JSON.stringify(patch)} never highlights progress`, async ({ page }) => {
    await mountProgress(page, snapshot(patch));
    await expect(page.getByRole('heading', { name: 'Waiting for plan progress' })).toBeVisible();
    await expect(page.locator('[aria-current="step"]')).toHaveCount(0);
    await expect(page.getByRole('progressbar')).toHaveCount(0);
    await expect(page.getByRole('button')).toHaveCount(0);
  });
}

test('runtime history is keyboard accessible and explicit skipped/completed states survive a backward override', async ({ page }) => {
  const data = snapshot({ activeStageIndex: 1, lastStageOverride: 'Set stage 2 as current', lastStageOverrideAt: 700 });
  data.stages[0].state = 'skipped';
  data.stages[0].appliedSeconds = 0;
  data.stages[4].state = 'completed';
  data.stages[4].appliedSeconds = 300;
  data.stages[6].state = 'skipped';
  const missing = data.stages[6] as Record<string, unknown>;
  for (const key of ['appliedSeconds', 'elapsedSeconds', 'visits']) delete missing[key];
  await mountProgress(page, data);
  const stages = page.getByRole('list', { name: 'Active runtime plan stages' }).getByRole('listitem');
  await expect(stages.locator('details.history')).toHaveCount(8);
  await expect(stages.locator('details.history[open]')).toHaveCount(0);
  await expect(stages.nth(0).locator('.stage-state')).toHaveText('Skipped');
  await expect(stages.nth(4).locator('.stage-state')).toHaveText('Completed');
  await expect(stages.nth(6).locator('.stage-state')).toHaveText('Skipped');
  await expect(page.locator('[aria-current="step"]')).toHaveCount(1);
  const name = stages.nth(1).getByRole('button');
  await name.focus();
  await expect(name).toHaveAccessibleDescription(/Actual applied: 1m 15s.*Elapsed selected \(including offline\): 1m 40s.*Visits: 1.*State: Current/);
  const details = stages.nth(6).locator('details.history');
  await expect(details.locator('p')).not.toBeVisible();
  await details.locator('summary').focus();
  await details.locator('summary').press('Enter');
  await expect(details.locator('p')).toContainText('Actual applied: Not recorded');
  await expect(details.locator('p')).toContainText('Elapsed selected (including offline): Not recorded');
  await expect(details.locator('p')).toContainText('Visits: Not recorded');
  expect(await page.locator('.plan-progress').innerHTML()).not.toContain('Set stage 2 as current');
  expect(await page.evaluate(() => (window as unknown as FixtureWindow).__nativeCalls)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
