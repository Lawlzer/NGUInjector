import { expect, test, type Page } from '@playwright/test';
import { stagePresets } from '../src/lib/stage-presets';

function snapshot(patch: Record<string, unknown> = {}) {
  return {
    available: true, status: 'running', planName: '8/15/30m NUMBER', planVersion: 14,
    schedulerMode: 'time', activeStageIndex: 2, stageProgress: .25, elapsedSeconds: 675,
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
      name, index, durationSeconds: 300, completionMode: 0, energyFocus, magicFocus, gearGoal,
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
  await page.route('http://127.0.0.1:1420/', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><head><link rel="stylesheet" href="/src/styles.css"></head><body><main class="shell" id="fixture"></main></body></html>' }));
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
    await expect(stages.nth(i).locator('.duration')).toHaveText('5m');
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

test('runtime base preset labels ignore customized or missing priority orders', async ({ page }) => {
  const row = snapshot().stages[0];
  const stages = stagePresets.map((preset, index) => ({
    ...row, index, name: 'Unrelated saved descriptor', energyFocus: preset.EnergyFocus, magicFocus: preset.MagicFocus, gearGoal: preset.GearGoal,
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
  await expect(page.locator('.completed')).toHaveCount(5);
  await expect(page.locator('.context')).toContainText('Repeat 3 / Repeat from 6');
  await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '0');
});

for (const status of ['paused', 'interrupted']) {
  test(`${status} Quick override stays explicit and never appears running`, async ({ page }) => {
    await mountProgress(page, snapshot({ status, reason: 'Quick: Time Machine overrides the plan.', elapsedSeconds: 999999 }));
    await expect(page.getByRole('status')).toHaveText('Quick: Time Machine overrides the plan.');
    await expect(page.locator('.status')).toHaveText(status === 'paused' ? 'Paused' : 'Interrupted');
    await expect(page.locator('li.current')).toHaveCount(0);
    await expect(page.locator('li.held')).toHaveCount(1);
    await expect(page.locator('[aria-current="step"]')).toContainText('Time Machine');
    await expect(page.getByRole('progressbar')).toHaveAccessibleName('Stage 3 progress');
    await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuetext', `${status === 'paused' ? 'Paused' : 'Interrupted'}: Time Machine; 25% complete`);
  });
}

test('sequential AT progress stays indeterminate despite a very long elapsed duration', async ({ page }) => {
  const data = snapshot({ schedulerMode: 'sequential', activeStageIndex: 0, stageProgress: null, elapsedSeconds: 999999 });
  data.stages[0] = { ...data.stages[0], completionMode: 1, targetAdvancedTrainingPower: '9223372036854775806', targetAdvancedTrainingToughness: '6000' } as typeof data.stages[number];
  await mountProgress(page, data, true, false);
  await expect(page.locator('[aria-current="step"]')).toContainText('AT: P 9223372036854775806 / T 6000');
  await expect(page.getByRole('progressbar')).not.toHaveAttribute('aria-valuenow');
  await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuetext', 'Running: Time Machine; progress not reported');
  await expect(page.getByRole('button')).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
