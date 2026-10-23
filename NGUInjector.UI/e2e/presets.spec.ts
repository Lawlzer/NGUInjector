import { expect, test, type Page } from '@playwright/test';
import definitions from '../../NGUInjector/Data/PresetDefinitions.json' with { type: 'json' };
import activityPolicies from '../../NGUInjector/Data/ActivityPolicies.json' with { type: 'json' };
import { globalPresets as legacyGlobalPresets } from './fixtures/global-presets';

const globalPresets = legacyGlobalPresets.map(row => ({ ...row, Weights: [...row.Weights, ...Array(definitions.objectives.length - row.Weights.length).fill(0)] }));

type FixtureWindow = typeof window & {
  __presetActions: { command: string; args: Record<string, unknown> }[];
  __presetWrites: Record<string, unknown>[];
  __presetPlanWrites: Record<string, unknown>[];
  __presetSettingWrites: Record<string, unknown>[];
  __presetNative: string[];
  __presetDialogs: string[];
  __presetConfirm: boolean;
  __presetRuntime: Record<string, unknown>;
  __presetPreviewId: string;
  __presetScore: number | null;
  __presetApplyFailure: boolean;
  __presetProduction: boolean;
  __presetLazyShifter: boolean;
  __presetZeroBlood: boolean;
  __presetDelaySave: boolean;
  __presetReleaseSave: (() => void) | null;
  __presetTaskIndex: number;
};

async function openPresets(page: Page, rows?: Record<string, unknown>[]) {
  await page.addInitScript(({ rows, objectiveKeys }) => {
    const fixture = window as FixtureWindow;
    fixture.__presetActions = []; fixture.__presetWrites = []; fixture.__presetPlanWrites = []; fixture.__presetNative = []; fixture.__presetDialogs = []; fixture.__presetConfirm = true;
    fixture.__presetSettingWrites = [];
    const settings = { _energyNguWeights: [0, 0, 0, 0, 1, 0, 1, 0, 0], _magicNguWeights: [1, 0, 0, 0, 0, 0, 0] };
     fixture.__presetPreviewId = ''; fixture.__presetScore = 50; fixture.__presetApplyFailure = false;
     fixture.__presetProduction = false;
      fixture.__presetLazyShifter = false;
     fixture.__presetZeroBlood = false;
     fixture.__presetDelaySave = false; fixture.__presetReleaseSave = null;
     fixture.__presetTaskIndex = 3;
    fixture.__presetRuntime = { externalUiEnabled: true, status: 'running', protocolVersion: 3, settingsCasVersion: 1, planVersion: 17, stageTimingVersion: 1, globalPresetsVersion: 2, loadoutPresetsVersion: 2, globalNguWeightsVersion: 1, startedAt: 'session-one', supportedCommands: ['readPlan', 'savePlan', 'applySettingsPatch', 'previewGlobalPreset', 'applyGlobalPreset', 'equipGlobalPreset', 'status'] };
    const row = { Id: 'balanced', Name: 'Balanced NGU', Weights: [100, 100, 0, 0, 0, 0], HorizonSeconds: 3600, MagicBloodPercent: -1, SupportMode: 'current', DiggerBudgetPercent: 75, Zone: -1, MinimumPower: 0, MinimumToughness: 0, SwapSeconds: 5, BeardPriority: [], DiggerWeights: [], Future: { keep: 'unknown field' } };
    let plan: Record<string, unknown> = { Version: 17, Name: 'plan-a', __externalUiRevision: 'a'.repeat(64), Stages: [{ Name: 'NGU', Percent: 100 }], GoalPresets: [{ Name: 'Legacy', GearGoal: 2 }], GearGoal: 2, ActiveGoalPresetIndex: 0, LoadoutPresets: [row, { ...row, Id: 'background', Name: 'Background blood', Weights: [300, 0, 50, 0, 0, 0] }], UnknownRoot: 42 };
    let library: Record<string, unknown> = { Version: 1, Presets: structuredClone(rows ?? plan.LoadoutPresets), __externalUiRevision: 'c'.repeat(64), UnknownLibrary: 42 };
    let response: Record<string, unknown> | null = null;
    const metrics = (value: number) => [
      { key: 'energyNgu', label: 'Energy NGU', value: fixture.__presetProduction ? 500 : value, unit: 'levels/s', quality: 'estimated', normalized: value / 400, relativeToCurrent: value / 100, ...(fixture.__presetProduction ? { specialistReferenceKey: 'frozen-ngu', specialistProduction: 400, retainedProduction: 1.25, retainedProductionPercent: 125, weightedContribution: .625 } : {}) },
      { key: 'blood', label: 'Blood', value: fixture.__presetZeroBlood ? 0 : 12, unit: 'blood/s', quality: 'proxy', normalized: .4, relativeToCurrent: fixture.__presetZeroBlood ? null : 1.2, ...(fixture.__presetProduction || fixture.__presetZeroBlood ? { specialistReferenceKey: null, specialistProduction: 0, retainedProduction: null, retainedProductionPercent: null, weightedContribution: 0 } : {}) },
      { key: 'zone', label: 'Zone farming', value: null, unit: 'drops/s', quality: 'unavailable', normalized: null, relativeToCurrent: null, reason: 'Locked zone', ...(fixture.__presetProduction ? { specialistReferenceKey: null, specialistProduction: null, retainedProduction: null, retainedProductionPercent: null, weightedContribution: null } : {}) },
      { key: 'adventurePower', label: 'Adventure power', value: value * 1000, unit: 'power', quality: 'proxy', relativeToCurrent: value / 100, specialistReferenceKey: 'power-reference', specialistProduction: 400000, retainedProductionPercent: value / 4 },
      { key: 'adventureToughness', label: 'Adventure toughness', value: value * 500, unit: 'toughness', quality: 'proxy', relativeToCurrent: value / 100, specialistReferenceKey: 'toughness-reference', specialistProduction: 200000, retainedProductionPercent: value / 4 },
      { key: 'energyCap', label: 'Energy Cap', value: value * 10000, unit: 'energy', quality: 'exact', relativeToCurrent: value / 100, specialistProduction: 4000000, retainedProductionPercent: value / 4 },
      { key: 'magicCap', label: 'Magic Cap', value: value * 5000, unit: 'magic', quality: 'exact', relativeToCurrent: value / 100, specialistProduction: 2000000, retainedProductionPercent: value / 4 }
    ];
   const candidate = (key: string, label: string, value: number, feasible = true) => ({ key, label, itemIds: [123], items: [{ id: 123, name: 'Owned NGU hat', slot: -1, sourceSlot: 4, level: 100, factor: 1.5 }], metrics: metrics(value), score: key === 'current' ? null : fixture.__presetScore, magicBloodPercent: 15, energyAllocated: 1000, magicNguAllocated: 850, bloodAllocated: 150, bloodRitual: 2, zone: null, itopodFloor: fixture.__presetProduction ? 0 : null, expectedHits: null, feasible, support: { mode: 'planned', beards: [0, 3], diggerWeights: [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0], diggers: [{ id: 2, name: 'Stats', level: 10, drain: 3 }], goldBudget: 75, goldDrain: 3, goldRemaining: 72, notes: ['Gold drain accounted for'] }, warnings: feasible ? ['Frozen owned gear'] : ['Insufficient gold'] });
    Object.assign(window, { __TAURI_INTERNALS__: { invoke: async (command: string, args: Record<string, unknown> = {}) => {
      fixture.__presetNative.push(command);
      const casPath = '/e2e/fixtures/settings-cas.ts';
      const cas = await import(casPath);
      if (command === 'get_ngui_state') return { runtime_dir: 'C:\\Fixture\\NGUInjector', connected: true, marker_enabled: true, ...await cas.settingsSnapshot(settings), protocol: {}, profiles: [], plans: ['plan-a', 'plan-b'], logs: [], command_results: [], runtime: { updatedAt: new Date().toISOString(), ...structuredClone(fixture.__presetRuntime) } };
      if (command === 'save_settings_patch_json') {
        const receipt = await cas.commitSettings(settings, args, 'connected');
        fixture.__presetSettingWrites.push(cas.settingsPatch(args));
        return receipt;
      }
      if (command === 'read_plan') return JSON.stringify({ ...plan, Name: args.name });
      if (command === 'save_plan') { const next = JSON.parse(String(args.planJson)); fixture.__presetPlanWrites.push(next); plan = { ...next, __externalUiRevision: 'b'.repeat(64) }; return { success: true, mode: 'connected' }; }
      if (command === 'read_preset_library') return JSON.stringify(library);
      if (command === 'save_preset_library') {
        const next = JSON.parse(String(args.libraryJson)); fixture.__presetWrites.push(next);
        if (fixture.__presetDelaySave) await new Promise<void>(resolve => {
          fixture.__presetReleaseSave = () => { fixture.__presetDelaySave = false; fixture.__presetReleaseSave = null; resolve(); };
        });
        library = { ...next, __externalUiRevision: (fixture.__presetWrites.length % 2 ? 'd' : 'e').repeat(64) };
        return { success: true };
      }
      if (command === 'plugin:dialog|message' || command === 'plugin:dialog|ask') { fixture.__presetDialogs.push(String(args.message)); return command.endsWith('message') ? fixture.__presetConfirm ? 'Yes' : 'No' : fixture.__presetConfirm; }
      if (command === 'enqueue_command') {
        const action = args.envelope as { command: string; args: Record<string, unknown> };
        fixture.__presetActions.push(structuredClone(action)); const id = `cmd-${fixture.__presetActions.length}`;
        const preset = (library.Presets as Record<string, unknown>[]).find(row => row.Id === action.args.presetId)!;
        const fixedWeights = preset?.Weights as number[] | undefined ?? Array(objectiveKeys.length).fill(0);
        const effectiveWeights = [...fixedWeights, ...Array(Math.max(0, objectiveKeys.length - fixedWeights.length)).fill(0)];
        const dynamicWeight = Number(preset?.AdventureDynamicWeight ?? 0);
         effectiveWeights[fixture.__presetTaskIndex] += dynamicWeight;
        const resolved = { effectiveWeights, ...(dynamicWeight > 0 ? { adventureTask: fixture.__presetTaskIndex === 3 ? 'PP (ITOPOD)' : fixture.__presetTaskIndex === 4 ? 'Boosts' : 'Zone farming', adventureTaskKey: fixture.__presetTaskIndex === 3 ? 'pp' : fixture.__presetTaskIndex === 4 ? 'boosts' : 'zone' } : {}) };
        let data = {};
        if (action.command === 'previewGlobalPreset') {
          fixture.__presetPreviewId = crypto.randomUUID().replace(/-/g, '');
             data = { result: { version: 1, previewId: fixture.__presetPreviewId, presetId: preset.Id, presetName: preset.Name, capturedAt: new Date().toISOString(), startedAt: fixture.__presetRuntime.startedAt, horizonSeconds: 3600, objectiveKeys, baselineKey: 'current', candidates: [candidate('current', 'Current gear / planned support', 100), candidate('mixed', 'Shared NGU + blood', 200), candidate('infeasible', 'Over-budget support', 300, false)], specialists: [candidate('energyNgu', 'Energy full focus', 400)], normalization: [400, 300, 30, 10, 20, 1, 1, 1, 1, 1, 1, 1, 1, 1], sequence: { firstKey: 'energyNgu', secondKey: 'mixed', firstSeconds: 1500, secondSeconds: 2095, swapSeconds: 5, metrics: metrics(210), score: 52, improvementPercent: 4, quality: 'proxy', assumptions: ['Target-aware NGU phases; other bonuses and refill held fixed'] }, warnings: ['Bounded search'], assumptions: ['Same snapshot'], search: { optimality: 'heuristic', evaluated: 42, elapsedMilliseconds: 8, termination: 'budget' } } };
          if (fixture.__presetProduction) Object.assign((data as { result: object }).result, { normalizedWeights: [.5, .5, 0, 0, 0, 0], productionReferences: [{ key: 'energyNgu', candidateKey: 'frozen-ngu', production: 400 }, { key: 'blood', candidateKey: null, production: 0 }, { key: 'zone', candidateKey: null, production: null }], scoring: 'Frozen production utility, not improvement-only scoring' });
          Object.assign((data as { result: object }).result, { lazyItopodShifterActive: fixture.__presetLazyShifter });
          Object.assign((data as { result: object }).result, resolved);
        }
        if (action.command === 'applyGlobalPreset') {
          if (!fixture.__presetPreviewId || action.args.previewId !== fixture.__presetPreviewId) throw new Error('Missing, mismatched or consumed preview token');
          fixture.__presetPreviewId = '';
          if (fixture.__presetApplyFailure) throw new Error('Transport lost after enqueue');
          const normalized: Record<string, unknown> = { AutoUpdate: true, AdvancedTrainingWeights: [1, 1, 0, 0, 0], ...preset, Weights: [...preset.Weights as number[], ...Array(Math.max(0, objectiveKeys.length - (preset.Weights as number[]).length)).fill(0)] };
          delete normalized.HorizonSeconds;
          if (!dynamicWeight) delete normalized.AdventureDynamicWeight;
          fixture.__presetRuntime.loadoutPreset = { id: preset.Id, name: preset.Name, candidateKey: action.args.candidateKey, appliedAt: new Date().toISOString(), preset: structuredClone(normalized), energyNguWeights: [...settings._energyNguWeights], magicNguWeights: [...settings._magicNguWeights], status: 'applied', active: false, requiresReapply: true, mode: 'applied-snapshot', ...resolved };
        }
        response = { id, command: action.command, success: true, data }; return id;
      }
      if (command === 'read_command_result') return response;
      if (command === 'tail_log') return '';
      throw new Error(`Unexpected native invocation: ${command}`);
    } } });
    localStorage.clear();
  }, { rows, objectiveKeys: definitions.objectives.map(row => row.key) });
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Presets', exact: true }).click();
  const presets = page.getByRole('region', { name: 'Loadout presets', exact: true });
  await expect(presets.getByLabel('Browse preset')).toBeEnabled();
  await expect(presets.getByRole('button', { name: 'Save library', exact: true })).toHaveCount(0);
  return presets;
}

async function waitForLibrary(page: Page, row: Record<string, unknown>): Promise<void> {
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__presetWrites.at(-1)?.Presets)).toEqual(expect.arrayContaining([expect.objectContaining(row)]));
  const presets = page.getByRole('region', { name: 'Loadout presets', exact: true });
  await expect(presets.getByRole('button', { name: 'Discard library edits', exact: true })).toHaveCount(0);
  await expect(presets).not.toContainText(/autosave|all changes saved|storage|polling/i);
}

test('resource lane review is explicit and independent orders survive edits, reload and cloning', async ({ page }) => {
  const preset = { ...definitions.defaults, Id: 'lane_review', Name: 'Lane review', AdventureDynamicWeight: 2, Weights: [2, 3, 4, 0, 0, 0, 5, 1, 1, 0, 0, 0, 0, 0] };
  const presets = await openPresets(page, [preset]);
  await presets.getByLabel('Browse preset').selectOption(preset.Id);
  const energy = presets.getByRole('group', { name: 'Energy funding', exact: true });
  const magic = presets.getByRole('group', { name: 'Magic funding', exact: true });
  const energyNames = () => energy.getByRole('listitem').locator('.priority-name');
  const magicNames = () => magic.getByRole('listitem').locator('.priority-name');
  await expect(energyNames()).toHaveText(['Time Machine', 'Energy NGU', 'Augments', 'Wandoos']);
  await expect(magicNames()).toHaveText(['Time Machine', 'Blood', 'Magic NGU', 'Wandoos']);
  await expect(presets).toContainText('Legacy proportional allocation');
  await expect(energy.getByRole('button', { name: 'Move Time Machine down', exact: true })).toBeDisabled();
  await page.waitForTimeout(500);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  await presets.getByRole('button', { name: 'Review & enable lane order', exact: true }).click();
  await waitForLibrary(page, { Id: preset.Id, ResourcePrioritiesReviewed: true, EnergyPriorities: ['TM', 'NGU', 'AUG', 'WAN'], MagicPriorities: ['TM', 'RIT', 'NGU', 'WAN'] });
  await expect(presets).not.toContainText('Legacy proportional allocation');
  await energy.getByRole('button', { name: 'Move Energy NGU up', exact: true }).click();
  await expect(energyNames()).toHaveText(['Energy NGU', 'Time Machine', 'Augments', 'Wandoos']);
  await expect(magicNames()).toHaveText(['Time Machine', 'Blood', 'Magic NGU', 'Wandoos']);
  await magic.getByRole('button', { name: 'Move Blood up', exact: true }).click();
  await waitForLibrary(page, { Id: preset.Id, EnergyPriorities: ['NGU', 'TM', 'AUG', 'WAN'], MagicPriorities: ['RIT', 'TM', 'NGU', 'WAN'], AdventureDynamicWeight: 2, Weights: preset.Weights });
  await presets.getByLabel('Energy NGU weight', { exact: true }).fill('100');
  await waitForLibrary(page, { Id: preset.Id, EnergyPriorities: ['NGU', 'TM', 'AUG', 'WAN'], MagicPriorities: ['RIT', 'TM', 'NGU', 'WAN'], Weights: [100, ...preset.Weights.slice(1)] });
  await presets.getByRole('button', { name: 'Reload library', exact: true }).click();
  await expect(magicNames()).toHaveText(['Blood', 'Time Machine', 'Magic NGU', 'Wandoos']);
  await presets.getByRole('button', { name: 'Clone preset', exact: true }).click();
  await expect(presets.getByLabel('Preset name', { exact: true })).toHaveValue('Lane review copy');
  await waitForLibrary(page, { Name: 'Lane review copy', ResourcePrioritiesReviewed: true, EnergyPriorities: ['NGU', 'TM', 'AUG', 'WAN'], MagicPriorities: ['RIT', 'TM', 'NGU', 'WAN'], AdventureDynamicWeight: 2 });
  await expect(presets).toContainText('Update the injector to use reviewed lane order.');
  await expect(presets.getByRole('button', { name: 'Preview preset', exact: true })).toBeDisabled();
  await expect(presets.getByRole('button', { name: 'Equip preset', exact: true })).toBeDisabled();
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('reviewed lane actions require the capability but remain editable on older runtimes', async ({ page }) => {
  const preset = { ...definitions.defaults, Id: 'reviewed', Name: 'Reviewed', ResourcePrioritiesReviewed: true, EnergyPriorities: ['NGU'], MagicPriorities: [] };
  const presets = await openPresets(page, [preset]);
  await presets.getByLabel('Browse preset').selectOption(preset.Id);
  await expect(presets.getByRole('button', { name: 'Equip preset', exact: true })).toBeDisabled();
  const magic = presets.getByRole('group', { name: 'Magic funding', exact: true });
  await expect(magic).toContainText('No activity');
  await magic.getByLabel('Add magic funding', { exact: true }).selectOption({ label: 'Magic NGU' });
  await waitForLibrary(page, { Id: preset.Id, MagicPriorities: ['NGU'], EnergyPriorities: ['NGU'], ResourcePrioritiesReviewed: true });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  await page.evaluate(() => { (window as FixtureWindow).__presetRuntime.resourceLanePrioritiesVersion = 1; });
  // A mocked heartbeat refresh is enough; no native or game action is used to refresh capability.
  await expect(presets.getByRole('button', { name: 'Equip preset', exact: true })).toBeEnabled({ timeout: 15000 });
  await presets.getByRole('button', { name: 'Equip preset', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([{ command: 'equipGlobalPreset', args: { presetId: preset.Id, libraryRevision: 'd'.repeat(64) } }]);
});

test('cap goals edit independently of the dynamic sentinel and preview cap outputs', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-19T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-19T12:00:01Z'));
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  for (const goal of ['Energy NGU', 'Magic NGU']) await presets.getByRole('button', { name: `Remove ${goal} goal`, exact: true }).click();
  const add = presets.getByLabel('Add goal', { exact: true });
  await add.selectOption('12');
  const energy = presets.getByLabel('Energy Cap weight', { exact: true });
  await expect(energy).toBeFocused();
  await energy.fill('7');
  await expect(presets.getByLabel('Dynamic adventure weight', { exact: true })).toHaveCount(0);
  await expect(presets).not.toContainText('Follows Adventure mode');
  await add.selectOption('13');
  const magic = presets.getByLabel('Magic Cap weight', { exact: true });
  await expect(magic).toBeFocused();
  await magic.fill('3');
  await add.selectOption(String(definitions.objectives.length));
  const dynamic = presets.getByLabel('Dynamic adventure weight', { exact: true });
  await expect(dynamic).toBeFocused();
  await dynamic.fill('2');
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', Weights: [...Array(12).fill(0), 7, 3], AdventureDynamicWeight: 2 });
  await energy.fill('1e');
  await expect(presets.getByRole('alert')).toContainText('Energy Cap');
  await page.clock.runFor(500);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toHaveLength(1);
  await energy.fill('7');
  await presets.getByRole('button', { name: 'Remove Dynamic adventure goal', exact: true }).click();
  await expect(dynamic).toHaveCount(0);
  await expect(energy).toHaveValue('7');
  await expect(magic).toHaveValue('3');
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', Weights: [...Array(12).fill(0), 7, 3], AdventureDynamicWeight: 0 });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  const shared = presets.getByRole('region', { name: 'Shared NGU + blood', exact: true });
  for (const [label, output] of [['Energy Cap exact', '2,000,000 energy'], ['Magic Cap exact', '1,000,000 magic']]) {
    await expect(shared.getByRole('row').filter({ has: page.getByRole('rowheader', { name: label, exact: true }) }).getByRole('cell').first()).toHaveText(output);
  }
  await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood', exact: true })).toBeEnabled();
  await presets.getByRole('button', { name: 'Remove Energy Cap goal', exact: true }).click();
  await expect(energy).toHaveCount(0);
  await expect(magic).toBeFocused();
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', Weights: [...Array(13).fill(0), 3], AdventureDynamicWeight: 0 });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

for (const id of ['builtin_energyCap', 'builtin_magicCap']) {
  test(`${id} renders with current support and auto-update enabled`, async ({ page }) => {
    const row = definitions.presets.find(preset => preset.Id === id)!;
    const presets = await openPresets(page, [{ ...definitions.defaults, ...row }]);
    await presets.getByLabel('Browse preset').selectOption(id);
    await expect(presets.getByRole('table', { name: 'Goal weights', exact: true }).getByRole('rowheader')).toHaveText([row.Name]);
    await expect(presets.getByLabel(`${row.Name} weight`, { exact: true })).toHaveValue('1');
    await expect(presets.getByLabel('Support mode', { exact: true })).toHaveValue('current');
    await expect(presets.getByLabel('Auto-update preset')).toBeChecked();
    await expect(presets.getByLabel('Dynamic adventure weight', { exact: true })).toHaveCount(0);
    await expect(presets.getByRole('button', { name: 'Equip preset', exact: true })).toBeEnabled();
    const navigation = page.getByRole('navigation', { name: 'Primary' });
    await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
    await expect(presets.getByRole('button', { name: `Use ${row.Name}`, exact: true })).toBeEnabled();
    expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}

test('Dynamic adventure is an ordinary weighted goal that preserves edited farming presets', async ({ page }) => {
  const legacy = { ...globalPresets[5], Id: 'builtin_pp', Name: 'My edited farming', Weights: [0, 0, 0, 7, 4, 2, 0, 0, 0, 0, 0, 0], Future: { keep: 'user data' } };
  const presets = await openPresets(page, [legacy]);
  await presets.getByLabel('Browse preset').selectOption('builtin_pp');
  await expect(presets.getByLabel('PP (ITOPOD) weight', { exact: true })).toHaveValue('7');
  await expect(presets.getByLabel('Boosts weight', { exact: true })).toHaveValue('4');
  await presets.getByLabel('Zone farming weight', { exact: true }).fill('3');
  await waitForLibrary(page, { ...legacy, Weights: [0, 0, 0, 7, 4, 3, 0, 0, 0, 0, 0, 0, 0, 0] });
  await expect(presets.getByRole('button', { name: 'Add adventure-dynamic', exact: true })).toHaveCount(0);
  await presets.getByLabel('Add goal', { exact: true }).selectOption({ label: 'Dynamic adventure' });
  const dynamic = presets.getByLabel('Dynamic adventure weight', { exact: true });
  await expect(dynamic).toHaveValue('1');
  await expect(dynamic).toBeFocused();
  await waitForLibrary(page, { Id: 'builtin_pp', AdventureDynamicWeight: 1, Weights: [0, 0, 0, 7, 4, 3, 0, 0, 0, 0, 0, 0, 0, 0] });
  await expect(presets.getByLabel('Browse preset')).toHaveValue('builtin_pp');
  await presets.getByLabel('Add goal', { exact: true }).selectOption('2');
  await expect(presets.getByLabel('Blood weight', { exact: true })).toBeFocused();
  await dynamic.fill('2.5');
  await waitForLibrary(page, { Id: 'builtin_pp', AdventureDynamicWeight: 2.5, Weights: [0, 0, 1, 7, 4, 3, 0, 0, 0, 0, 0, 0, 0, 0] });
  const saved = await page.evaluate(() => (window as FixtureWindow).__presetWrites.at(-1)!.Presets as Record<string, unknown>[]);
  expect(saved).toHaveLength(1);
  expect(saved[0]).toMatchObject({ ...legacy, AdventureDynamicWeight: 2.5, Weights: [0, 0, 1, 7, 4, 3, 0, 0, 0, 0, 0, 0, 0, 0] });
  await expect(presets).not.toContainText('90% farming output');
  await expect(presets.getByLabel('Add goal', { exact: true }).locator('option[value="3"], option[value="4"], option[value="5"]')).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('dynamic goal edits retain invalid drafts and allow adding dynamic to non-adventure goals', async ({ page }) => {
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await presets.getByLabel('Add goal', { exact: true }).selectOption(String(definitions.objectives.length));
  const dynamic = presets.getByLabel('Dynamic adventure weight', { exact: true });
  await expect(dynamic).toBeFocused();
  await waitForLibrary(page, { Id: 'balanced', AdventureDynamicWeight: 1, Weights: [100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] });
  await dynamic.fill('1e');
  await expect(presets.getByRole('alert')).toContainText('Dynamic adventure weight');
  await presets.getByLabel('Browse preset').selectOption('background');
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await expect(dynamic).toHaveValue('1e');
  await dynamic.fill('0');
  await expect(dynamic).toBeVisible();
  await waitForLibrary(page, { Id: 'balanced', AdventureDynamicWeight: 0 });
  await presets.getByRole('button', { name: 'Remove Dynamic adventure goal', exact: true }).click();
  await expect(dynamic).toHaveCount(0);
  await expect(presets.getByLabel('Add goal', { exact: true }).locator(`option[value="${definitions.objectives.length}"]`)).toHaveCount(1);
});

test('dynamic actions require runtime capability and display resolved preview and applied tasks', async ({ page }) => {
  const row = { ...globalPresets[5], AdventureDynamicWeight: 1, Weights: Array(12).fill(0) };
  const presets = await openPresets(page, [row]);
  await presets.getByLabel('Browse preset').selectOption('global_5');
  await expect(presets.getByRole('button', { name: 'Preview preset', exact: true })).toBeDisabled();
  await expect(presets.getByRole('button', { name: 'Equip preset', exact: true })).toBeDisabled();
  await expect(presets).toContainText('Update the injector to use Dynamic adventure.');
  const custom = presets.getByLabel('Digger weights', { exact: true }).locator('option[value="custom"]');
  await expect(custom).toHaveJSProperty('disabled', true);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(presets.getByRole('button', { name: 'Use All NGU', exact: true })).toBeDisabled();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Presets', exact: true }).click();
  await page.evaluate(() => { const fixture = window as FixtureWindow; fixture.__presetRuntime.adventureDynamicPresetsVersion = 1; fixture.__presetTaskIndex = 5; });
  await expect(presets.getByRole('button', { name: 'Preview preset', exact: true })).toBeEnabled();
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  const comparison = presets.getByRole('article', { name: 'Preset comparison', exact: true });
  await expect(comparison).toContainText('Resolved adventure task: Zone farming');
  const zone = comparison.getByRole('table', { name: 'Projected production' }).first().getByRole('row').filter({ has: page.getByRole('rowheader', { name: /^Zone farming/ }) });
  await expect(zone.getByRole('cell').first()).toContainText('Unknown');
  await expect(custom).toHaveJSProperty('disabled', false);
  await presets.getByRole('button', { name: 'Apply Shared NGU + blood', exact: true }).click();
  await expect(presets.getByRole('article', { name: 'Last applied preset', exact: true })).toContainText('Resolved adventure task: Zone farming');
  const actions = await page.evaluate(() => (window as FixtureWindow).__presetActions);
  expect(actions).toHaveLength(2);
  expect(actions[0]).toEqual({ command: 'previewGlobalPreset', args: { presetId: 'global_5', libraryRevision: 'c'.repeat(64) } });
  expect(actions[1]).toEqual({ command: 'applyGlobalPreset', args: { presetId: 'global_5', libraryRevision: 'c'.repeat(64), candidateKey: 'mixed', previewId: expect.stringMatching(/^[a-f0-9]{32}$/) } });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('goals show active rows and Add and Remove move focus while saving objective indices', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-19T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-19T12:00:01Z'));
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  const goals = presets.getByRole('table', { name: 'Goal weights', exact: true });
  const add = presets.getByRole('combobox', { name: 'Add goal', exact: true });
  await expect(goals.getByRole('rowheader')).toHaveText(['Energy NGU', 'Magic NGU']);
  expect(await add.locator('option').evaluateAll(options => options.map(option => ({ value: option.value, label: option.textContent })))).toEqual([
    { value: '', label: '+ Add goal' },
    ...definitions.objectives.map((goal, index) => ({ value: String(index), label: goal.label })).filter(goal => ![0, 1, 3, 4, 5].includes(Number(goal.value))),
    { value: String(definitions.objectives.length), label: 'Dynamic adventure' }
  ]);
  await add.selectOption('2');
  const blood = presets.getByLabel('Blood weight', { exact: true });
  await expect(blood).toHaveValue('1');
  await expect(blood).toBeFocused();
  await expect(add).toHaveValue('');
  await expect(add.locator('option[value="2"]')).toHaveCount(0);
  await expect(goals.getByRole('rowheader')).toHaveText(['Energy NGU', 'Magic NGU', 'Blood']);
  await presets.getByRole('button', { name: 'Remove Energy NGU goal', exact: true }).click();
  await expect(presets.getByLabel('Energy NGU weight', { exact: true })).toHaveCount(0);
  await expect(presets.getByLabel('Magic NGU weight', { exact: true })).toBeFocused();
  await presets.getByRole('button', { name: 'Remove Blood goal', exact: true }).click();
  await expect(blood).toHaveCount(0);
  await expect(presets.getByLabel('Magic NGU weight', { exact: true })).toBeFocused();
  await presets.getByRole('button', { name: 'Remove Magic NGU goal', exact: true }).click();
  await expect(goals.getByRole('rowheader')).toHaveCount(0);
  await expect(add).toBeFocused();
  await expect(presets.getByRole('alert')).toContainText('At least one objective weight must be positive');
  await page.clock.runFor(500);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  await expect(presets.getByRole('button', { name: 'Preview preset', exact: true })).toBeDisabled();
  await expect(presets.getByRole('button', { name: 'Equip preset', exact: true })).toBeDisabled();
  await add.selectOption('10');
  await expect(presets.getByLabel('Adventure power weight', { exact: true })).toHaveValue('1');
  await expect(presets.getByLabel('Adventure power weight', { exact: true })).toBeFocused();
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', Weights: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0] });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toHaveLength(1);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('zero goal edits retain the current row and invalid partial drafts stay visible across selection', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-19T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-19T12:00:01Z'));
  const presets = await openPresets(page);
  const browse = presets.getByLabel('Browse preset');
  await browse.selectOption('balanced');
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  const energy = presets.getByLabel('Energy NGU weight', { exact: true });
  await energy.fill('0');
  await expect(energy).toBeVisible();
  await expect(energy).toBeFocused();
  await expect(presets.getByLabel('Add goal', { exact: true }).locator('option[value="0"]')).toHaveCount(0);
  await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood', exact: true })).toBeDisabled();
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', Weights: [0, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] });
  await expect(energy).toHaveValue('0');
  await expect(energy).toBeFocused();
  await browse.selectOption('background');
  await browse.selectOption('balanced');
  await expect(energy).toHaveCount(0);
  await presets.getByLabel('Add goal', { exact: true }).selectOption('0');
  await energy.fill('1e');
  await expect(energy).toHaveValue('1e');
  await expect(presets.getByRole('alert')).toContainText('Energy NGU');
  await browse.selectOption('background');
  await browse.selectOption('balanced');
  await expect(energy).toBeVisible();
  await expect(energy).toHaveValue('1e');
  await page.clock.runFor(500);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toHaveLength(1);
  for (const action of ['Preview preset', 'Equip preset', 'Apply Shared NGU + blood']) await expect(presets.getByRole('button', { name: action, exact: true })).toBeDisabled();
  await energy.fill('1000001');
  await expect(presets.getByRole('alert')).toContainText('Energy NGU');
  await page.clock.runFor(500);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toHaveLength(1);
  await energy.fill('2');
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', Weights: [2, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] });
  expect((await page.evaluate(() => (window as FixtureWindow).__presetActions)).map(action => action.command)).toEqual(['previewGlobalPreset']);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('Advanced Training ratios require a positive goal and survive removing and readding it', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-19T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-19T12:00:01Z'));
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  const ratios = presets.getByRole('table', { name: 'Advanced Training ratios', exact: true });
  const goal = presets.getByLabel('Advanced Training weight', { exact: true });
  await expect(ratios).toHaveCount(0);
  await expect(presets.locator('input[aria-label^="Preset AT "]')).toHaveCount(0);
  await presets.getByLabel('Add goal', { exact: true }).selectOption('9');
  await expect(goal).toHaveValue('1');
  await expect(goal).toBeFocused();
  await expect(ratios).toBeVisible();
  await expect(ratios.getByRole('rowheader')).toHaveText(['Toughness', 'Power', 'Block', 'Wandoos energy', 'Wandoos magic']);
  expect(await ratios.getByRole('textbox').evaluateAll(inputs => inputs.map(input => Number((input as HTMLInputElement).value)))).toEqual(definitions.defaults.AdvancedTrainingWeights);
  await presets.getByLabel('Preset AT Toughness ratio', { exact: true }).fill('3');
  await presets.getByLabel('Preset AT Power ratio', { exact: true }).fill('4');
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', Weights: [100, 100, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0], AdvancedTrainingWeights: [3, 4, 0, 0, 0] });
  await goal.fill('-');
  await expect(ratios).toHaveCount(0);
  await expect(goal).toHaveValue('-');
  await page.clock.runFor(500);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toHaveLength(1);
  await goal.fill('0');
  await expect(goal).toBeVisible();
  await expect(ratios).toHaveCount(0);
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', Weights: [100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], AdvancedTrainingWeights: [3, 4, 0, 0, 0] });
  await presets.getByRole('button', { name: 'Remove Advanced Training goal', exact: true }).click();
  await expect(goal).toHaveCount(0);
  await expect(ratios).toHaveCount(0);
  await presets.getByRole('button', { name: 'Reload library', exact: true }).click();
  await expect(goal).toHaveCount(0);
  await presets.getByLabel('Add goal', { exact: true }).selectOption('9');
  await expect(goal).toBeFocused();
  await expect(ratios).toBeVisible();
  expect(await ratios.getByRole('textbox').evaluateAll(inputs => inputs.map(input => Number((input as HTMLInputElement).value)))).toEqual([3, 4, 0, 0, 0]);
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', Weights: [100, 100, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0], AdvancedTrainingWeights: [3, 4, 0, 0, 0] });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

for (const action of ['remove', 'zero']) {
  test(`invalid AT ratios remain recoverable after ${action} disables the goal`, async ({ page }) => {
    await page.clock.install({ time: new Date('2026-10-19T12:00:00Z') });
    await page.clock.pauseAt(new Date('2026-10-19T12:00:01Z'));
    const presets = await openPresets(page);
    await presets.getByLabel('Browse preset').selectOption('balanced');
    await presets.getByLabel('Add goal', { exact: true }).selectOption('9');
    await presets.getByLabel('Preset AT Power ratio', { exact: true }).fill('-');
    const disable = async () => {
      if (action === 'remove') await presets.getByRole('button', { name: 'Remove Advanced Training goal', exact: true }).click();
      else await presets.getByLabel('Advanced Training weight', { exact: true }).fill('0');
    };
    await disable();
    await presets.getByLabel('Preset name', { exact: true }).fill('Recovered AT ratios');
    await expect(presets.locator('input[aria-label^="Preset AT "]')).toHaveCount(0);
    await expect(presets.getByRole('button', { name: 'Reset AT ratios', exact: true })).toBeVisible();
    await page.clock.runFor(500);
    expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
    if (action === 'remove') await presets.getByLabel('Add goal', { exact: true }).selectOption('9');
    else await presets.getByLabel('Advanced Training weight', { exact: true }).fill('1');
    await expect(presets.getByLabel('Preset AT Power ratio', { exact: true })).toHaveValue('-');
    await disable();
    await presets.getByRole('button', { name: 'Reset AT ratios', exact: true }).click();
    await expect(presets.getByRole('button', { name: 'Reset AT ratios', exact: true })).toHaveCount(0);
    await expect(presets.getByRole('alert')).toHaveCount(0);
    await expect(presets.locator('input[aria-label^="Preset AT "]')).toHaveCount(0);
    await page.clock.runFor(350);
    await waitForLibrary(page, { Id: 'balanced', Name: 'Recovered AT ratios', Weights: [100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], AdvancedTrainingWeights: definitions.defaults.AdvancedTrainingWeights });
    expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  });
}

test('invalid goals cannot initialize Custom diggers but Automatic can clear invalid custom drafts', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-19T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-19T12:00:01Z'));
  const presets = await openPresets(page, [globalPresets[5]]);
  await presets.getByLabel('Browse preset').selectOption('global_5');
  const mode = presets.getByLabel('Digger weights', { exact: true });
  const energy = presets.getByLabel('Energy NGU weight', { exact: true });
  for (const value of ['-', '1e', '1000001']) {
    await energy.fill(value);
    await expect(mode.locator('option[value="custom"]')).toHaveJSProperty('disabled', true);
    await expect(mode).toHaveValue('automatic');
    await expect(presets.locator('input[aria-label$=" digger weight"]')).toHaveCount(0);
    await page.clock.runFor(500);
  }
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  await energy.fill('3');
  await expect(mode.locator('option[value="custom"]')).toHaveJSProperty('disabled', false);
  await mode.selectOption('custom');
  await expect(presets.getByLabel('Energy NGU digger weight', { exact: true })).toHaveValue('0.75');
  await expect(presets.getByLabel('Magic NGU digger weight', { exact: true })).toHaveValue('0.25');
  await presets.getByLabel('Energy NGU digger weight', { exact: true }).fill('-');
  await mode.selectOption('automatic');
  await expect(presets.getByRole('alert')).toHaveCount(0);
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'global_5', Weights: [3, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], DiggerWeights: [] });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
});

test('preset edits autosave without explanatory copy or an applied timestamp', async ({ page }) => {
  const presets = await openPresets(page);
  const appliedAt = '2026-10-19T14:23:07.4258872Z';
  await page.evaluate(appliedAt => {
    (window as FixtureWindow).__presetRuntime.loadoutPreset = { id: 'balanced', appliedName: 'Last applied setup', appliedAutoUpdate: true, appliedAt, status: 'applied' };
  }, appliedAt);
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await expect(presets.getByRole('article', { name: 'Last applied preset', exact: true })).toContainText('Last applied setup');
  for (const autoUpdate of [false, true]) {
    await presets.getByLabel('Auto-update preset').setChecked(autoUpdate);
    await waitForLibrary(page, { Id: 'balanced', AutoUpdate: autoUpdate });
    for (const text of ['On: re-optimize and apply', 'Off: reuse the last valid policy', 'Reuses the last setup', 'Preview and apply once to create it', 'Execution time belongs to plan stages', 'Scheduled presets use stage timing', 'standalone presets do not expire', '1-hour forecast', 'optimize immediate stats, not Advanced Training gains', 'Acquired policy auto-update', 'Re-equip explicitly to update', 'No background timer', appliedAt]) await expect(presets).not.toContainText(text);
  }
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('preset autosave keeps focused controls editable during a delayed save and retains newer edits after readback', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-19T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-19T12:00:01Z'));
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood', exact: true })).toBeEnabled();
  await presets.getByLabel('Support mode', { exact: true }).selectOption('planned');
  for (const beard of definitions.beards) await presets.getByLabel(`${beard.name} beard`, { exact: true }).selectOption([0, 3].includes(beard.id) ? 'enabled' : 'off');
  await page.evaluate(() => { (window as FixtureWindow).__presetDelaySave = true; });
  const name = presets.getByLabel('Preset name', { exact: true });
  const element = await name.elementHandle();
  await name.fill('Saving name');
  await page.clock.runFor(350);
  await expect.poll(() => page.evaluate(() => typeof (window as FixtureWindow).__presetReleaseSave)).toBe('function');
  await expect(name).toBeFocused();
  await expect(name).toBeEditable();
  const settings = presets.getByRole('article', { name: 'Preset settings', exact: true });
  // Unreviewed proposal lanes are intentionally read-only, independent of save state.
  expect(await settings.locator('input, select').evaluateAll(controls => controls.filter(control => control.matches(':disabled')).map(control => control.getAttribute('aria-label') || control.closest('label')?.textContent))).toEqual(['Add energy funding', 'Add magic funding']);
  await expect(settings.getByRole('button', { name: 'Review & enable lane order', exact: true })).toBeEnabled();
  await expect(settings.getByRole('button', { name: 'Move Fu Manchu beard down', exact: true })).toBeEnabled();
  await expect(settings.getByLabel('NGU beard', { exact: true })).toBeEnabled();
  await expect(settings.getByRole('button', { name: 'Use default beards', exact: true })).toBeEnabled();
  for (const button of await settings.locator('.preview-action button').all()) await expect(button).toBeDisabled();
  await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood', exact: true })).toBeDisabled();
  await expect(presets.getByLabel('Browse preset')).toBeDisabled();
  for (const action of ['Add preset', 'Clone preset', 'Delete preset', 'Discard library edits', 'Reload library']) {
    await expect(presets.getByRole('button', { name: action, exact: true })).toBeDisabled();
  }
  await name.pressSequentially(' updated');
  await page.clock.runFor(350);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toHaveLength(1);
  await expect(name).toBeFocused();
  await expect(name).toHaveValue('Saving name updated');
  await page.evaluate(() => { (window as FixtureWindow).__presetReleaseSave!(); });
  await expect(presets.getByLabel('Browse preset')).toBeEnabled();
  await expect(name).toBeFocused();
  await expect(name).toHaveValue('Saving name updated');
  expect(await name.evaluate((current, previous) => current === previous, element)).toBe(true);
  await expect(presets).toContainText('Preset changes pending.');
  await expect(presets).not.toContainText(/autosave|all changes saved/i);
  const first = await page.evaluate(() => (window as FixtureWindow).__presetWrites);
  expect(first).toHaveLength(1);
  expect((first[0].Presets as Record<string, unknown>[])[0].Name).toBe('Saving name');
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', Name: 'Saving name updated', SupportMode: 'planned', BeardPriority: [0, 3] });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toHaveLength(2);
  await expect(name).toBeFocused();
  await expect(name).toBeEditable();
  expect((await page.evaluate(() => (window as FixtureWindow).__presetActions)).map(action => action.command)).toEqual(['previewGlobalPreset']);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  await element?.dispose();
});

test('CRUD is global, preserves unknowns and never applies from selection, render or save', async ({ page }) => {
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await presets.getByRole('button', { name: 'Clone preset', exact: true }).click();
  await expect(presets.getByLabel('Preset name', { exact: true })).toHaveValue('Balanced NGU copy');
  await presets.getByLabel('Preset name', { exact: true }).fill('My mixed setup');
  await waitForLibrary(page, { Name: 'My mixed setup' });
  const writes = await page.evaluate(() => (window as FixtureWindow).__presetWrites);
  const rows = writes.at(-1)!.Presets as Record<string, unknown>[];
  expect(rows).toHaveLength(3); expect(rows[2].Future).toEqual({ keep: 'unknown field' }); expect(rows[2].Id).not.toBe('balanced');
  for (const row of rows) { expect(row.Weights).toHaveLength(14); expect(row).not.toHaveProperty('HorizonSeconds'); }
  expect(writes.at(-1)).toMatchObject({ Version: 1, UnknownLibrary: 42 });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  await presets.getByRole('button', { name: 'Delete preset', exact: true }).click();
  await presets.getByRole('button', { name: 'Add preset', exact: true }).click();
  await expect(presets.getByLabel('Energy NGU weight', { exact: true })).toHaveValue('1');
  const addedId = await presets.getByLabel('Browse preset').inputValue();
  await waitForLibrary(page, { Id: addedId, Weights: definitions.defaults.Weights });
  const addedRows = (await page.evaluate(() => (window as FixtureWindow).__presetWrites.at(-1)!.Presets)) as Record<string, unknown>[];
  expect(addedRows).toHaveLength(3);
  expect(addedRows.some(row => row.Id === rows[2].Id)).toBe(false);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetDialogs)).toEqual([]);
  const selectedId = await presets.getByLabel('Browse preset').inputValue();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Dashboard', exact: true }).click();
  await page.getByRole('article', { name: 'Plan controls' }).getByRole('combobox').selectOption('plan-b');
  await page.getByRole('button', { name: 'Manage presets', exact: true }).click();
  await expect(presets.getByLabel('Browse preset')).toHaveValue(selectedId);
  await expect(presets.getByLabel('Preset plan')).toHaveCount(0);
});

test('direct adventure priorities save at indices ten and eleven without a duration editor', async ({ page }) => {
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await expect(presets.getByLabel('Duration (seconds)', { exact: true })).toHaveCount(0);
  for (const text of ['Execution time belongs to plan stages', 'standalone presets do not expire', 'optimize immediate stats, not Advanced Training gains']) await expect(presets).not.toContainText(text);
  await presets.getByLabel('Add goal', { exact: true }).selectOption('10');
  await presets.getByLabel('Adventure power weight', { exact: true }).fill('70');
  await presets.getByLabel('Add goal', { exact: true }).selectOption('11');
  await presets.getByLabel('Adventure toughness weight', { exact: true }).fill('30');
  await expect(presets.getByLabel('Advanced Training weight', { exact: true })).toHaveCount(0);
  await waitForLibrary(page, { Id: 'balanced', Weights: [100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 70, 30, 0, 0] });
  const writes = await page.evaluate(() => (window as FixtureWindow).__presetWrites);
  expect((writes.at(-1)!.Presets as Record<string, unknown>[])[0]).toMatchObject({ Weights: [100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 70, 30, 0, 0], Future: { keep: 'unknown field' } });
  expect((writes.at(-1)!.Presets as Record<string, unknown>[])[0]).not.toHaveProperty('HorizonSeconds');
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  await expect(presets.getByRole('article', { name: 'Preset comparison' })).toContainText('3,600s projection');
  await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood' })).toBeEnabled();
  const shared = presets.getByRole('region', { name: 'Shared NGU + blood', exact: true });
  await expect(shared.getByRole('row').filter({ has: page.getByRole('rowheader', { name: 'Adventure power proxy', exact: true }) }).getByRole('cell').first()).toHaveText('200,000 power');
  await expect(shared.getByRole('row').filter({ has: page.getByRole('rowheader', { name: 'Adventure toughness proxy', exact: true }) }).getByRole('cell').first()).toHaveText('100,000 toughness');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

for (const autoUpdate of [true, false]) {
  test(`saving 14-objective priorities with auto-update ${autoUpdate ? 'on' : 'off'} never reoptimizes during polling or navigation`, async ({ page }) => {
    await page.clock.install({ time: new Date('2026-10-18T12:00:00Z') });
    await page.clock.pauseAt(new Date('2026-10-18T12:00:01Z'));
    const presets = await openPresets(page);
    await presets.getByLabel('Browse preset').selectOption('balanced');
    await presets.getByLabel('Auto-update preset').setChecked(autoUpdate);
    await presets.getByLabel('Add goal', { exact: true }).selectOption('10');
    await presets.getByLabel('Adventure power weight', { exact: true }).fill('80');
    await presets.getByLabel('Add goal', { exact: true }).selectOption('11');
    await presets.getByLabel('Adventure toughness weight', { exact: true }).fill('20');
    await expect(presets.getByRole('button', { name: 'Preview preset', exact: true })).toBeDisabled();
    await expect(presets.getByRole('button', { name: 'Equip preset', exact: true })).toBeDisabled();
    await page.clock.runFor(349);
    expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
    await page.clock.runFor(1);
    await waitForLibrary(page, { Id: 'balanced', AutoUpdate: autoUpdate, Weights: [100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 80, 20, 0, 0] });
    await expect(presets.getByRole('button', { name: 'Preview preset', exact: true })).toBeEnabled();
    await page.clock.runFor(60000);
    const navigation = page.getByRole('navigation', { name: 'Primary' });
    await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
    await presets.getByRole('button', { name: 'Manage presets', exact: true }).click();
    await expect(presets.getByLabel('Auto-update preset')).toBeChecked({ checked: autoUpdate });
    await expect(presets.getByLabel('Adventure power weight', { exact: true })).toHaveValue('80');
    await expect(presets.getByLabel('Adventure toughness weight', { exact: true })).toHaveValue('20');
    const writes = await page.evaluate(() => (window as FixtureWindow).__presetWrites);
    expect(writes).toHaveLength(1);
    expect((writes[0].Presets as Record<string, unknown>[])[0]).toMatchObject({ AutoUpdate: autoUpdate, Weights: [100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 80, 20, 0, 0] });
    expect((writes[0].Presets as Record<string, unknown>[])[0]).not.toHaveProperty('HorizonSeconds');
    expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  });
}

test('preview leads with actionable results, hides the baseline and keeps details collapsed', async ({ page }) => {
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  const comparison = presets.getByRole('article', { name: 'Preset comparison' });
  await expect(comparison).toContainText('estimated'); await expect(comparison).toContainText('proxy'); await expect(comparison).toContainText('n/a');
  await expect(comparison.getByRole('button', { name: 'Apply Shared NGU + blood' })).toBeEnabled();
  await expect(comparison.getByRole('button', { name: 'Apply Over-budget support' })).toBeDisabled();
  await expect(comparison.getByRole('region', { name: 'Current gear / planned support', exact: true })).toHaveCount(0);
  await expect(comparison.getByRole('button', { name: /^Choose / })).toHaveCount(0);
  await expect(comparison).not.toContainText('Scores are comparable');
  await expect(comparison).not.toContainText('Normalization:');
  await expect(comparison).toContainText('Insufficient gold');
  await expect(presets.getByRole('article', { name: 'Last applied preset' })).toHaveCount(0);
  await page.screenshot({ path: test.info().outputPath('presets-default.png'), fullPage: true });
  const shared = comparison.getByRole('region', { name: 'Shared NGU + blood', exact: true });
  await expect(shared).toContainText('2x'); await expect(shared).toContainText('50%');
  await expect(shared.getByText('Owned NGU hat', { exact: true })).not.toBeVisible();
  await shared.locator('summary').filter({ hasText: /^Gear / }).click();
  await expect(shared.getByText('Owned NGU hat', { exact: true })).toBeVisible();
  await expect(shared.getByText('Head', { exact: true })).toBeVisible();
  await shared.locator('summary').filter({ hasText: /^Beards & diggers / }).click();
  await expect(shared.getByText('Gold budget', { exact: true })).toBeVisible();
  await expect(shared).toContainText('75');
  await expect(shared.getByRole('table', { name: 'Digger allocation' }).getByRole('cell')).toHaveText(['2', '10', '3']);
  await expect(shared.getByRole('table', { name: 'Projected production' }).getByRole('columnheader', { name: /Two-phase/ })).toBeVisible();
  await expect(shared.getByRole('table', { name: 'Projected production' }).getByRole('row').nth(1).getByRole('cell')).toHaveText(['200 levels/s', '2x', '50%', '210 levels/s']);
  await expect(comparison).toContainText('preview only');
  await expect(comparison).not.toContainText('Target-aware NGU phases');
  for (const label of ['Full-focus alternatives', 'Preview details', 'Warnings & notes', 'Bounded search', 'Frozen owned gear']) await expect(comparison).not.toContainText(label);
  await expect(presets).not.toContainText('Quick priorities');
  const actions = await page.evaluate(() => (window as FixtureWindow).__presetActions);
  expect(actions).toHaveLength(1); expect(actions[0]).toEqual({ command: 'previewGlobalPreset', args: { presetId: 'balanced', libraryRevision: 'c'.repeat(64) } });
  expect(actions[0].args).not.toHaveProperty('previewId');
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetNative)).not.toContain('save_settings_patch_json');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('presets.png'), fullPage: true });
});

test('custom digger weights are visible with support controls and preserve partial edits', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-18T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-18T12:00:01Z'));
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  const budget = presets.getByLabel('Digger budget (% gold)', { exact: true });
  await expect(budget).toHaveCount(0);
  await expect(budget).toBeHidden();
  await expect(presets.getByRole('combobox', { name: 'Digger weights', exact: true })).toHaveCount(0);
  await expect(presets.getByLabel('Minimum power', { exact: true })).not.toBeVisible();
  await presets.getByLabel('Support mode', { exact: true }).selectOption('planned');
  await expect(budget).toBeEnabled();
  await expect(budget).toBeVisible();
  await budget.fill('50');
  const mode = presets.getByRole('combobox', { name: 'Digger weights', exact: true });
  await expect(mode).toHaveValue('automatic');
  await expect(presets.locator('input[aria-label$=" digger weight"]')).toHaveCount(0);
  await mode.selectOption('custom');
  await expect(presets.locator('input[aria-label$=" digger weight"]')).toHaveCount(12);
  await expect(presets.getByLabel('Magic NGU digger weight', { exact: true })).toHaveValue('0.5');
  await expect(presets.getByLabel('Energy beards digger weight', { exact: true })).toHaveValue('0.1');
  await presets.getByLabel('Energy NGU digger weight', { exact: true }).fill('3');
  const blood = presets.getByLabel('Blood digger weight', { exact: true });
  await blood.fill('-');
  await page.clock.runFor(500);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  await expect(presets.getByRole('button', { name: 'Preview preset', exact: true })).toBeDisabled();
  await blood.fill('1');
  await expect(presets.getByLabel('Drop digger weight', { exact: true })).toHaveValue('0');
  await presets.locator('summary').filter({ hasText: /^Minimum adventure stats/ }).click();
  await presets.getByLabel('Minimum power', { exact: true }).fill('100');
  for (const beard of definitions.beards) await presets.getByLabel(`${beard.name} beard`, { exact: true }).selectOption([0, 3].includes(beard.id) ? 'enabled' : 'off');
  await presets.getByLabel('Support mode', { exact: true }).selectOption('current');
  await expect(budget).toHaveCount(0);
  await expect(budget).toBeHidden();
  await expect(presets.getByLabel('NGU beard', { exact: true })).toHaveCount(0);
  await expect(blood).toHaveCount(0);
  await presets.getByLabel('Support mode', { exact: true }).selectOption('planned');
  await expect(budget).toHaveValue('50');
  await expect(presets.getByLabel('Fu Manchu beard', { exact: true })).toHaveValue('enabled');
  await expect(presets.getByLabel('NGU beard', { exact: true })).toHaveValue('enabled');
  await expect(blood).toHaveValue('1');
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', SupportMode: 'planned', DiggerWeights: [0, 0, 0, 0, 3, .5, .1, .1, 0, 0, 1, 0], BeardPriority: [0, 3] });
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  expect((await page.evaluate(() => (window as FixtureWindow).__presetActions))[0]).toEqual({ command: 'previewGlobalPreset', args: { presetId: 'balanced', libraryRevision: 'd'.repeat(64) } });
  expect((await page.evaluate(() => (window as FixtureWindow).__presetWrites))[0].Presets).toEqual(expect.arrayContaining([expect.objectContaining({ SupportMode: 'planned', DiggerWeights: [0, 0, 0, 0, 3, .5, .1, .1, 0, 0, 1, 0], DiggerBudgetPercent: 50, MinimumPower: 100, BeardPriority: [0, 3] })]));
  await expect(presets.getByRole('button', { name: 'Reset digger weights', exact: true })).toHaveCount(0);
  await mode.selectOption('automatic');
  await expect(blood).toHaveCount(0);
  await expect(presets.locator('input[aria-label$=" digger weight"]')).toHaveCount(0);
  await expect(presets).toContainText('Changed since preview');
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toHaveLength(1);
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', DiggerWeights: [] });
  await mode.selectOption('custom');
  await expect(blood).toHaveValue('0');
  await expect(presets.getByLabel('Adventure digger weight', { exact: true })).toHaveValue('1');
  await expect(presets.getByLabel('Energy NGU digger weight', { exact: true })).toHaveValue('0.5');
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', DiggerWeights: [0, 0, 0, 1, .5, .5, .1, .1, 0, 0, 0, 0] });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetDialogs)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('fresh presets hide automatic digger fields and custom edits remain independent of goal weights', async ({ page }) => {
  const presets = await openPresets(page);
  await presets.getByRole('button', { name: 'Add preset', exact: true }).click();
  const id = await presets.getByLabel('Browse preset').inputValue();
  await expect(presets.getByLabel('Preset name', { exact: true })).toHaveValue(definitions.newPreset.Name);
  await expect(presets.getByLabel('Support mode', { exact: true })).toHaveValue('planned');
  const mode = presets.getByRole('combobox', { name: 'Digger weights', exact: true });
  await expect(mode).toHaveValue('automatic');
  await expect(mode.locator('option')).toHaveText(['Automatic', 'Custom']);
  expect(await mode.locator('option').evaluateAll(options => options.map(option => option.value))).toEqual(['automatic', 'custom']);
  const weights = presets.locator('input[aria-label$=" digger weight"]');
  const derived = (goals: number[]) => definitions.diggers.map(digger => digger.objectiveWeights.reduce((total, coefficient, i) => total + coefficient * goals[i] / goals.reduce((sum, weight) => sum + weight, 0), 0));
  const defaults = derived(definitions.defaults.Weights);
  await expect(weights).toHaveCount(0);
  for (const beard of definitions.beards) await expect(presets.getByLabel(`${beard.name} beard`, { exact: true })).toHaveValue(activityPolicies.coreBeards.includes(beard.id) ? 'enabled' : 'rng');
  await expect(presets.getByRole('button', { name: 'Reset digger weights', exact: true })).toHaveCount(0);
  await waitForLibrary(page, { Id: id, Weights: definitions.defaults.Weights, DiggerWeights: [], BeardPriority: activityPolicies.coreBeards, BeardRotation: activityPolicies.optionalBeards });
  await mode.selectOption('custom');
  await expect(weights).toHaveCount(12);
  expect(await weights.evaluateAll(inputs => inputs.map(input => Number((input as HTMLInputElement).value)))).toEqual(defaults);
  await presets.getByLabel('Energy NGU digger weight', { exact: true }).fill('3');
  const edited = [...defaults]; edited[4] = 3;
  await waitForLibrary(page, { Id: id, DiggerWeights: edited });
  await presets.getByRole('button', { name: 'Reload library', exact: true }).click();
  expect(await weights.evaluateAll(inputs => inputs.map(input => Number((input as HTMLInputElement).value)))).toEqual(edited);
  await presets.getByLabel('Energy NGU weight', { exact: true }).fill('3');
  await waitForLibrary(page, { Id: id, Weights: [3, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], DiggerWeights: edited });
  expect(await weights.evaluateAll(inputs => inputs.map(input => Number((input as HTMLInputElement).value)))).toEqual(edited);
  await mode.selectOption('automatic');
  await waitForLibrary(page, { Id: id, DiggerWeights: [] });
  await expect(weights).toHaveCount(0);
  await mode.selectOption('custom');
  const reseeded = derived([3, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
  expect(await weights.evaluateAll(inputs => inputs.map(input => Number((input as HTMLInputElement).value)))).toEqual(reseeded);
  await waitForLibrary(page, { Id: id, DiggerWeights: reseeded });
  await mode.selectOption('automatic');
  await waitForLibrary(page, { Id: id, DiggerWeights: [] });
  await presets.getByLabel('Magic NGU weight', { exact: true }).fill('3');
  await waitForLibrary(page, { Id: id, Weights: [3, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], DiggerWeights: [] });
  await expect(weights).toHaveCount(0);
  await mode.selectOption('custom');
  expect(await weights.evaluateAll(inputs => inputs.map(input => Number((input as HTMLInputElement).value)))).toEqual(defaults);
  await waitForLibrary(page, { Id: id, DiggerWeights: defaults });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('Custom materializes shared derived digger weights from an existing preset and reseeds after Automatic', async ({ page }) => {
  const row = { ...globalPresets[6], MinimumToughness: 100 };
  const presets = await openPresets(page, [row]);
  await presets.getByLabel('Browse preset').selectOption(row.Id);
  const mode = presets.getByRole('combobox', { name: 'Digger weights', exact: true });
  const fields = presets.locator('input[aria-label$=" digger weight"]');
  const derived = (goals: number[]) => definitions.diggers.map(digger => digger.objectiveWeights.reduce((total, coefficient, i) => total + coefficient * (goals[i] / goals.reduce((sum, weight) => sum + weight, 0)), 0) + digger.minimumAdventureWeight);
  await expect(mode).toHaveValue('automatic');
  await expect(fields).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  await mode.selectOption('custom');
  await expect(fields).toHaveCount(12);
  const seeded = derived(row.Weights);
  expect(await fields.evaluateAll(inputs => inputs.map(input => Number((input as HTMLInputElement).value)))).toEqual(seeded);
  await waitForLibrary(page, { Id: row.Id, Weights: row.Weights, MinimumToughness: 100, DiggerWeights: seeded });
  await presets.getByLabel('Blood digger weight', { exact: true }).fill('4');
  const edited = [...seeded]; edited[10] = 4;
  await waitForLibrary(page, { Id: row.Id, Weights: row.Weights, DiggerWeights: edited });
  await presets.getByLabel('Advanced Training weight', { exact: true }).fill('5');
  const changedGoals = [...row.Weights]; changedGoals[9] = 5;
  await waitForLibrary(page, { Id: row.Id, Weights: changedGoals, DiggerWeights: edited });
  expect(await fields.evaluateAll(inputs => inputs.map(input => Number((input as HTMLInputElement).value)))).toEqual(edited);
  await mode.selectOption('automatic');
  await expect(fields).toHaveCount(0);
  await waitForLibrary(page, { Id: row.Id, Weights: changedGoals, DiggerWeights: [] });
  await mode.selectOption('custom');
  expect(await fields.evaluateAll(inputs => inputs.map(input => Number((input as HTMLInputElement).value)))).toEqual(derived(changedGoals));
  await waitForLibrary(page, { Id: row.Id, Weights: changedGoals, DiggerWeights: derived(changedGoals) });
  await expect(presets.getByRole('button', { name: 'Reset digger weights', exact: true })).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('beard rows follow preferred priority then Rotate and Off with keyed focus and announcements', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-19T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-19T12:00:01Z'));
  const presets = await openPresets(page, [{ ...globalPresets[5], BeardPriority: [5, 0, 3], BeardRotation: [6, 2] }]);
  await presets.getByLabel('Browse preset').selectOption('global_5');
  const beards = presets.getByRole('region', { name: 'Beard settings', exact: true });
  const table = beards.getByRole('table', { name: 'Beard modes and priority', exact: true });
  await expect(table.getByRole('rowheader')).toHaveText(['Adventure', 'Fu Manchu', 'NGU', 'NUMBER', 'Gold', 'Drop chance', 'Ladybeard']);
  expect(await table.getByRole('combobox').evaluateAll(selects => selects.map(select => (select as HTMLSelectElement).value))).toEqual(['enabled', 'enabled', 'enabled', 'rng', 'rng', 'off', 'off']);
  const mode = beards.getByLabel('Fu Manchu beard', { exact: true });
  await expect(mode.locator('option')).toHaveText(['Preferred', 'Rotate', 'Off']);
  expect(await mode.locator('option').evaluateAll(options => options.map(option => option.value))).toEqual(['enabled', 'rng', 'off']);
  const row = table.getByRole('row').filter({ has: page.getByRole('rowheader', { name: 'Fu Manchu', exact: true }) });
  const originalRow = await row.elementHandle();
  const up = row.getByRole('button', { name: 'Move Fu Manchu beard up', exact: true });
  const down = row.getByRole('button', { name: 'Move Fu Manchu beard down', exact: true });
  await down.focus();
  await page.keyboard.press('Enter');
  await expect(table.getByRole('rowheader')).toHaveText(['Adventure', 'NGU', 'Fu Manchu', 'NUMBER', 'Gold', 'Drop chance', 'Ladybeard']);
  await expect(down).toBeDisabled();
  await expect(up).toBeFocused();
  await expect(beards.getByRole('status')).toHaveText('Fu Manchu, priority 3');
  expect(await row.evaluate((current, previous) => current === previous, originalRow)).toBe(true);
  await page.keyboard.press('Enter');
  await expect(up).toBeFocused();
  await expect(table.getByRole('rowheader')).toHaveText(['Adventure', 'Fu Manchu', 'NGU', 'NUMBER', 'Gold', 'Drop chance', 'Ladybeard']);
  await expect(beards.getByRole('status')).toHaveText('Fu Manchu, priority 2');
  await page.keyboard.press('Enter');
  await expect(up).toBeDisabled();
  await expect(down).toBeFocused();
  await expect(table.getByRole('rowheader')).toHaveText(['Fu Manchu', 'Adventure', 'NGU', 'NUMBER', 'Gold', 'Drop chance', 'Ladybeard']);
  await expect(beards.getByRole('status')).toHaveText('Fu Manchu, priority 1');
  expect(await row.evaluate((current, previous) => current === previous, originalRow)).toBe(true);
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'global_5', BeardPriority: [0, 5, 3], BeardRotation: [6, 2] });
  await expect(down).toBeFocused();
  await presets.getByRole('button', { name: 'Reload library', exact: true }).click();
  await expect(table.getByRole('rowheader')).toHaveText(['Fu Manchu', 'Adventure', 'NGU', 'NUMBER', 'Gold', 'Drop chance', 'Ladybeard']);
  await beards.getByRole('button', { name: 'Use default beards', exact: true }).click();
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'global_5', BeardPriority: activityPolicies.coreBeards, BeardRotation: activityPolicies.optionalBeards });
  const defaultOrder = [...activityPolicies.coreBeards, ...definitions.beards.filter(beard => !activityPolicies.coreBeards.includes(beard.id) && activityPolicies.optionalBeards.includes(beard.id)).map(beard => beard.id), ...definitions.beards.filter(beard => !activityPolicies.coreBeards.includes(beard.id) && !activityPolicies.optionalBeards.includes(beard.id)).map(beard => beard.id)];
  await expect(table.getByRole('rowheader')).toHaveText(defaultOrder.map(id => definitions.beards.find(beard => beard.id === id)!.name));
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await originalRow?.dispose();
});

test('beard mode changes retain focus when their row moves between groups', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-19T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-19T12:00:01Z'));
  const presets = await openPresets(page, [{ ...globalPresets[5], BeardPriority: [5, 0, 3], BeardRotation: [6, 2] }]);
  await presets.getByLabel('Browse preset').selectOption('global_5');
  const table = presets.getByRole('table', { name: 'Beard modes and priority', exact: true });
  const mode = presets.getByLabel('Adventure beard', { exact: true });
  const original = await mode.elementHandle();
  for (const [value, expected] of [
    ['off', ['Fu Manchu', 'NGU', 'NUMBER', 'Gold', 'Drop chance', 'Ladybeard', 'Adventure']],
    ['rng', ['Fu Manchu', 'NGU', 'NUMBER', 'Adventure', 'Gold', 'Drop chance', 'Ladybeard']],
    ['enabled', ['Fu Manchu', 'NGU', 'Adventure', 'NUMBER', 'Gold', 'Drop chance', 'Ladybeard']]
  ] as const) {
    await mode.focus();
    await mode.selectOption(value);
    await expect(mode).toBeFocused();
    expect(await mode.evaluate((current, previous) => current === previous, original)).toBe(true);
    await expect(table.getByRole('rowheader')).toHaveText([...expected]);
  }
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'global_5', BeardPriority: [0, 3, 5], BeardRotation: [6, 2] });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  await original?.dispose();
});

test('goal, training and custom support tables fit a 320px viewport', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 800 });
  const presets = await openPresets(page, [globalPresets[6]]);
  await presets.getByLabel('Browse preset').selectOption('global_6');
  await presets.getByLabel('Digger weights', { exact: true }).selectOption('custom');
  await expect(presets.getByRole('table', { name: 'Goal weights', exact: true })).toBeVisible();
  await expect(presets.getByRole('table', { name: 'Advanced Training ratios', exact: true })).toBeVisible();
  await expect(presets.getByRole('table', { name: 'Beard modes and priority', exact: true })).toBeVisible();
  await expect(presets.getByRole('table', { name: 'Custom digger weights', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('preset-editor-320.png'), fullPage: true });
});

test('saved legacy overrides are reset-only and unrelated edits preserve them until explicit reset', async ({ page }) => {
  const overrides = { Zone: 7, MagicBloodPercent: 35, SwapSeconds: 12 };
  const presets = await openPresets(page, [{ ...globalPresets[5], ...overrides, Future: { keep: 'legacy unknown' } }]);
  await presets.getByLabel('Browse preset').selectOption('global_5');
  const settings = presets.getByRole('article', { name: 'Preset settings', exact: true });
  const saved = settings.locator('[aria-label="Saved overrides"]');
  await expect(saved).toBeVisible();
  for (const text of ['Zone: Clock Dimension', 'Blood magic: 35%', 'Swap cost: 12s']) await expect(saved).toContainText(text);
  await expect(saved.locator('input, select')).toHaveCount(0);
  await expect(saved.getByRole('button')).toHaveText(['Reset to automatic']);
  await expect(settings.locator('summary').filter({ hasText: 'Advanced settings' })).toHaveCount(0);
  for (const name of ['Zone', 'Magic blood percent', 'Swap overhead (seconds)']) await expect(settings.getByLabel(name, { exact: true })).toHaveCount(0);
  await expect(settings.getByLabel('Minimum power', { exact: true })).not.toBeVisible();
  await expect(settings.getByLabel('Minimum toughness', { exact: true })).not.toBeVisible();
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  await settings.getByLabel('Preset name', { exact: true }).fill('Legacy overrides retained');
  await waitForLibrary(page, { Id: 'global_5', Name: 'Legacy overrides retained', ...overrides, Future: { keep: 'legacy unknown' } });
  await settings.getByLabel('Energy NGU weight', { exact: true }).fill('3');
  await waitForLibrary(page, { Id: 'global_5', Weights: [3, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], ...overrides });
  await settings.locator('summary').filter({ hasText: /^Minimum adventure stats/ }).click();
  await settings.getByLabel('Minimum power', { exact: true }).fill('50');
  await settings.getByLabel('Minimum toughness', { exact: true }).fill('75');
  await waitForLibrary(page, { Id: 'global_5', MinimumPower: 50, MinimumToughness: 75, ...overrides });
  await presets.getByRole('button', { name: 'Reload library', exact: true }).click();
  await expect(saved).toBeVisible();
  await settings.getByRole('button', { name: 'Preview preset', exact: true }).click();
  await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood', exact: true })).toBeEnabled();
  const beforeReset = await page.evaluate(() => (window as FixtureWindow).__presetWrites);
  for (const write of beforeReset) expect((write.Presets as Record<string, unknown>[])[0]).toMatchObject(overrides);
  await saved.getByRole('button', { name: 'Reset to automatic', exact: true }).click();
  await expect(saved).toHaveCount(0);
  await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood', exact: true })).toBeDisabled();
  await waitForLibrary(page, {
    Id: 'global_5', Name: 'Legacy overrides retained', Weights: [3, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], MinimumPower: 50, MinimumToughness: 75,
    Zone: definitions.defaults.Zone, MagicBloodPercent: definitions.defaults.MagicBloodPercent, SwapSeconds: definitions.defaults.SwapSeconds, Future: { keep: 'legacy unknown' }
  });
  await presets.getByRole('button', { name: 'Reload library', exact: true }).click();
  await expect(saved).toHaveCount(0);
  expect((await page.evaluate(() => (window as FixtureWindow).__presetActions)).map(action => action.command)).toEqual(['previewGlobalPreset']);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetDialogs)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

for (const priority of [[], [0, 3]]) {
  test(`legacy beard priorities ${JSON.stringify(priority)} remain unchanged until explicit default conversion`, async ({ page }) => {
    const presets = await openPresets(page, [{ ...globalPresets[5], BeardPriority: priority }]);
    await presets.getByLabel('Browse preset').selectOption('global_5');
    await expect(presets).not.toContainText('Legacy selection');
    for (const beard of definitions.beards) await expect(presets.getByLabel(`${beard.name} beard`, { exact: true })).toHaveValue(priority.length ? priority.includes(beard.id) ? 'enabled' : 'off' : activityPolicies.coreBeards.includes(beard.id) ? 'enabled' : 'rng');
    expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
    await presets.getByLabel('Preset name', { exact: true }).fill('Legacy renamed');
    await waitForLibrary(page, { Id: 'global_5', Name: 'Legacy renamed', BeardPriority: priority });
    const rows = await page.evaluate(() => (window as FixtureWindow).__presetWrites.at(-1)!.Presets as Record<string, unknown>[]);
    expect(rows[0]).not.toHaveProperty('BeardRotation');
    await presets.getByRole('button', { name: 'Use default beards', exact: true }).click();
    await waitForLibrary(page, { Id: 'global_5', BeardPriority: activityPolicies.coreBeards, BeardRotation: activityPolicies.optionalBeards });
    await expect(presets).not.toContainText('Legacy selection');
    expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  });
}

test('beard modes persist unique disjoint pools, enabled order and explicit none after readback', async ({ page }) => {
  const presets = await openPresets(page, [{ ...globalPresets[5], BeardPriority: [0, 3] }]);
  await presets.getByLabel('Browse preset').selectOption('global_5');
  await presets.getByLabel('Fu Manchu beard', { exact: true }).selectOption('rng');
  await presets.getByLabel('Adventure beard', { exact: true }).selectOption('enabled');
  await presets.getByLabel('Drop chance beard', { exact: true }).selectOption('enabled');
  await presets.getByRole('button', { name: 'Move NGU beard down', exact: true }).click();
  await waitForLibrary(page, { Id: 'global_5', BeardPriority: [5, 3, 1], BeardRotation: [0] });
  await presets.getByRole('button', { name: 'Move NGU beard up', exact: true }).focus();
  await page.keyboard.press('Enter');
  await waitForLibrary(page, { Id: 'global_5', BeardPriority: [3, 5, 1], BeardRotation: [0] });
  await presets.getByRole('button', { name: 'Reload library', exact: true }).click();
  await expect(presets.getByRole('button', { name: 'Move NGU beard up', exact: true })).toBeDisabled();
  await expect(presets.getByRole('button', { name: 'Move Drop chance beard down', exact: true })).toBeDisabled();
  await expect(presets.getByLabel('Fu Manchu beard', { exact: true })).toHaveValue('rng');
  await expect(presets.getByLabel('Ladybeard beard', { exact: true })).toHaveValue('off');
  await presets.getByLabel('Fu Manchu beard', { exact: true }).selectOption('enabled');
  await presets.getByLabel('NGU beard', { exact: true }).selectOption('rng');
  await waitForLibrary(page, { Id: 'global_5', BeardPriority: [5, 1, 0], BeardRotation: [3] });
  for (const beard of definitions.beards) await presets.getByLabel(`${beard.name} beard`, { exact: true }).selectOption('off');
  await waitForLibrary(page, { Id: 'global_5', BeardPriority: [], BeardRotation: [] });
  await presets.getByRole('button', { name: 'Reload library', exact: true }).click();
  for (const beard of definitions.beards) await expect(presets.getByLabel(`${beard.name} beard`, { exact: true })).toHaveValue('off');
  await expect(presets.getByRole('button', { name: /^Move .* beard (up|down)$/ })).toHaveCount(0);
  await expect(presets).not.toContainText('Legacy selection');
  const writes = await page.evaluate(() => (window as FixtureWindow).__presetWrites);
  for (const write of writes) {
    const row = (write.Presets as { BeardPriority: number[]; BeardRotation: number[] }[])[0];
    expect(new Set(row.BeardPriority).size).toBe(row.BeardPriority.length);
    expect(new Set(row.BeardRotation).size).toBe(row.BeardRotation.length);
    expect(row.BeardPriority.filter(id => row.BeardRotation.includes(id))).toEqual([]);
  }
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
});

test('Dashboard inspection shares details and discards dirty edits on either screen without game actions', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-19T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-19T12:00:01Z'));
  const presets = await openPresets(page);
  const navigation = page.getByRole('navigation', { name: 'Primary' });
  await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await presets.getByLabel('Inspect preset', { exact: true }).selectOption('background');
  await expect(presets.getByLabel('Preset name', { exact: true })).toBeVisible();
  await expect(presets.getByLabel('Preset name', { exact: true })).toHaveValue('Background blood');
  await expect(presets.getByLabel('Blood weight', { exact: true })).toHaveValue('50');
  await presets.getByLabel('Energy NGU weight', { exact: true }).fill('-');
  await page.clock.runFor(500);
  await expect(presets.getByRole('alert')).toContainText('Energy NGU');
  await expect(presets.getByRole('button', { name: 'Use Background blood', exact: true })).toBeDisabled();
  await presets.getByRole('button', { name: 'Discard library edits', exact: true }).click();
  await expect(presets.getByLabel('Energy NGU weight', { exact: true })).toHaveValue('300');
  await expect(presets.getByRole('button', { name: 'Discard library edits', exact: true })).toHaveCount(0);
  await presets.getByLabel('Preset name', { exact: true }).fill('Dashboard draft');
  await presets.getByRole('button', { name: 'Manage presets', exact: true }).click();
  await expect(presets.getByLabel('Browse preset')).toHaveValue('background');
  await expect(presets.getByLabel('Preset name', { exact: true })).toHaveValue('Dashboard draft');
  await presets.getByRole('button', { name: 'Discard library edits', exact: true }).click();
  await expect(presets.getByLabel('Preset name', { exact: true })).toHaveValue('Background blood');
  await expect(presets.getByRole('button', { name: 'Discard library edits', exact: true })).toHaveCount(0);
  await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await presets.getByLabel('Inspect preset', { exact: true }).selectOption('balanced');
  await expect(presets.getByLabel('Preset name', { exact: true })).toHaveValue('Balanced NGU');
  await page.clock.runFor(1000);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetSettingWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetDialogs)).toEqual(Array(2).fill('Discard unsaved global preset library edits and reload? Plan edits will be kept.'));
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('production keeps exact outputs, frozen percentages above 100 and unknown values on both layouts', async ({ page }) => {
  const presets = await openPresets(page);
  await page.evaluate(() => { (window as FixtureWindow).__presetProduction = true; });
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  const comparison = presets.getByRole('article', { name: 'Preset comparison' });
  const shared = comparison.getByRole('region', { name: 'Shared NGU + blood', exact: true });
  const energy = shared.getByRole('row').filter({ has: page.getByRole('rowheader', { name: 'Energy NGU estimated', exact: true }) });
  await expect(energy.getByRole('cell')).toHaveText(['500 levels/s', '2x', '125%', '500 levels/s']);
  const blood = shared.getByRole('row').filter({ has: page.getByRole('rowheader', { name: 'Blood proxy', exact: true }) });
  await expect(blood.getByRole('cell').nth(2)).toHaveText('n/a');
  await expect(blood.getByRole('cell').first()).toHaveText('12 blood/s');
  await expect(comparison).not.toContainText('frozen-ngu');
  await shared.getByText('Allocation & route', { exact: true }).click();
  await expect(shared).toContainText('ITOPOD floor: 0');
  await expect(shared.getByRole('row').filter({ has: page.getByRole('rowheader', { name: 'Zone farming', exact: true }) }).getByRole('cell')).toHaveText(['n/a', 'n/a', 'n/a', 'n/a']);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toHaveLength(1);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('zero-to-zero production is n/a while genuinely unavailable weighted goals remain Unknown', async ({ page }) => {
  const presets = await openPresets(page, [{ ...globalPresets[5], Id: 'balanced', Weights: [100, 100, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0] }]);
  await page.evaluate(() => { (window as FixtureWindow).__presetZeroBlood = true; });
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  const shared = presets.getByRole('region', { name: 'Shared NGU + blood', exact: true });
  await expect(shared.getByRole('row').filter({ has: page.getByRole('rowheader', { name: 'Blood', exact: true }) }).getByRole('cell')).toHaveText(['n/a', 'n/a', 'n/a', 'n/a']);
  await presets.getByLabel('Zone farming weight', { exact: true }).fill('100');
  await waitForLibrary(page, { Id: 'balanced', Weights: [100, 100, 0, 0, 0, 100, 0, 0, 0, 0, 0, 0, 0, 0] });
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  await expect(shared.getByRole('row').filter({ has: page.getByRole('rowheader', { name: 'Zone farming unavailable: Locked zone', exact: true }) }).getByRole('cell')).toHaveText(['Unknown drops/s', 'Unknown', 'Unknown', 'Unknown drops/s']);
});

test('Lazy ITOPOD notice stays tied to the preview', async ({ page }) => {
  const presets = await openPresets(page);
  await page.evaluate(() => { (window as FixtureWindow).__presetLazyShifter = true; });
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  const comparison = presets.getByRole('article', { name: 'Preset comparison' });
  await expect(comparison).toContainText('Lazy ITOPOD controls the floor; it may change with your stats.');
  await page.evaluate(() => { (window as FixtureWindow).__presetLazyShifter = false; });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(comparison).toContainText('Lazy ITOPOD controls the floor');
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  await expect(comparison).not.toContainText('Lazy ITOPOD controls the floor');
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toHaveLength(2);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('invalid intermediate edits survive tabs and stale definitions disable candidate comparisons', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-18T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-18T12:00:01Z'));
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  await presets.getByLabel('Energy NGU weight', { exact: true }).fill('-');
  await expect(presets.getByRole('alert')).toContainText('Energy NGU');
  await expect(presets).toContainText('Changed since preview');
  await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood' })).toBeDisabled();
  await page.clock.runFor(500);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  await expect(presets.getByRole('button', { name: 'Preview preset', exact: true })).toBeDisabled();
  const navigation = page.getByRole('navigation', { name: 'Primary' });
  await navigation.getByRole('button', { name: 'Misc', exact: true }).click();
  await navigation.getByRole('button', { name: 'Presets', exact: true }).click();
  await expect(presets.getByLabel('Energy NGU weight', { exact: true })).toHaveValue('-');
  await presets.getByLabel('Energy NGU weight', { exact: true }).fill('300');
  await presets.locator('summary').filter({ hasText: /^Minimum adventure stats/ }).click();
  const minimum = presets.getByLabel('Minimum power', { exact: true });
  await minimum.fill('1.');
  await expect(minimum).toHaveValue('1.');
  await expect(presets.getByRole('alert')).toContainText('MinimumPower');
  await page.clock.runFor(500);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  await minimum.pressSequentially('5');
  await expect(minimum).toHaveValue('1.5');
  await expect(presets.getByRole('alert')).toHaveCount(0);
  await minimum.fill('-1');
  await expect(presets.getByRole('alert')).toContainText('MinimumPower');
  await page.clock.runFor(500);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  await expect(presets.getByRole('button', { name: 'Preview preset', exact: true })).toBeDisabled();
  await minimum.fill('5');
  await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood' })).toBeDisabled();
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', MinimumPower: 5, SwapSeconds: definitions.defaults.SwapSeconds, Weights: [300, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] });
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood' })).toBeEnabled();
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toHaveLength(1);
});

test('Apply runs without any popup and edited definitions require a fresh preview', async ({ page }) => {
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  const token = await page.evaluate(() => (window as FixtureWindow).__presetPreviewId);
  await page.evaluate(() => { (window as FixtureWindow).__presetConfirm = false; });
  await presets.getByRole('button', { name: 'Apply Shared NGU + blood' }).click();
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toHaveLength(2);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetDialogs)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetNative)).not.toContain('save_settings_patch_json');
  await expect(presets.getByRole('article', { name: 'Last applied preset' })).toContainText('balanced', { ignoreCase: true });
  expect((await page.evaluate(() => (window as FixtureWindow).__presetActions))[1]).toMatchObject({ command: 'applyGlobalPreset', args: { candidateKey: 'mixed', previewId: token } });
  await expect(presets.getByRole('article', { name: 'Preset comparison' })).toHaveCount(0);
  await expect(presets).not.toContainText('No background timer.');
  await presets.getByLabel('Preset name', { exact: true }).fill('Edited applied definition');
  await expect(presets).toContainText('Preset changed. Re-equip or preview and reapply.');
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toHaveLength(2);
  await waitForLibrary(page, { Id: 'balanced', Name: 'Edited applied definition' });
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  const nextToken = await page.evaluate(() => (window as FixtureWindow).__presetPreviewId);
  expect(nextToken).not.toBe(token);
  await presets.getByRole('button', { name: 'Apply Shared NGU + blood' }).click();
  await expect(presets.getByRole('article', { name: 'Last applied preset' })).toContainText('Edited applied definition');
  expect((await page.evaluate(() => (window as FixtureWindow).__presetActions))[3]).toMatchObject({ command: 'applyGlobalPreset', args: { candidateKey: 'mixed', previewId: nextToken } });
});

test('unknown scores disable Apply even for feasible candidates', async ({ page }) => {
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await page.evaluate(() => { (window as FixtureWindow).__presetScore = null; });
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood' })).toBeDisabled();
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toHaveLength(1);
});

for (const [label, key, weights] of [['Energy Augments weight', '_energyNguWeights', [2, 0, 0, 0, 1, 0, 1, 0, 0]], ['Magic Yggdrasil weight', '_magicNguWeights', [2, 0, 0, 0, 0, 0, 0]]] as const) {
  test(`global ${label} edits invalidate previews and mark the applied setup for re-equip`, async ({ page }) => {
    const presets = await openPresets(page);
    await presets.getByLabel('Browse preset').selectOption('balanced');
    await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
    await presets.getByRole('button', { name: 'Apply Shared NGU + blood' }).click();
    const lastApplied = presets.getByRole('article', { name: 'Last applied preset' });
    await expect(lastApplied).toContainText('Balanced NGU');
    await expect(lastApplied).not.toContainText('Preset changed.');
    await expect(presets.locator('input[aria-label^="Preset energy"], input[aria-label^="Preset magic"]')).toHaveCount(0);
    await expect(presets.locator('input[aria-label^="Preset AT "]')).toHaveCount(0);
    await expect(presets.getByRole('table', { name: 'Advanced Training ratios', exact: true })).toHaveCount(0);
    await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
    await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood' })).toBeEnabled();
    await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Misc', exact: true }).click();
    await page.getByRole('spinbutton', { name: label, exact: true }).fill('2');
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__presetSettingWrites)).toEqual([{ [key]: weights }]);
    await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Presets', exact: true }).click();
    await expect(presets.getByRole('article', { name: 'Preset comparison' })).toHaveCount(0);
    await expect(lastApplied).toContainText('Preset changed. Re-equip or preview and reapply.');
    await expect(lastApplied).not.toContainText('Applied NGU ratios unavailable');
    await expect(presets.getByRole('textbox', { name: 'Preset name', exact: true })).toHaveValue('Balanced NGU');
    expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toHaveLength(3);
    expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
    expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  });
}

test('zone reference lives in Misc, retains its selection and never edits presets', async ({ page }) => {
  const presets = await openPresets(page);
  await expect(presets.getByRole('heading', { name: 'Zone reference', exact: true })).toHaveCount(0);
  const navigation = page.getByRole('navigation', { name: 'Primary' });
  await navigation.getByRole('button', { name: 'Misc', exact: true }).click();
  const reference = page.getByRole('article', { name: 'Zone reference', exact: true });
  await expect(reference.getByRole('combobox', { name: 'Reference zone', exact: true })).toHaveValue('-1');
  await expect(reference.getByRole('link')).toHaveCount(0);
  await reference.getByRole('combobox', { name: 'Reference zone', exact: true }).selectOption('7');
  await expect(reference).toContainText('Clock Dimension');
  await expect(reference).toContainText('3,850'); await expect(reference).toContainText('3,600');
  await expect(reference.getByRole('region', { name: 'Idle recommendations' })).toContainText('Unknown');
  await expect(reference.getByText('Overview and localZoneStats idle recommendation is 4500/3000', { exact: false })).not.toBeVisible();
  await reference.getByText('Source notes', { exact: true }).click();
  await expect(reference).toContainText('Overview and localZoneStats idle recommendation is 4500/3000');
  await expect(reference.getByRole('link', { name: 'Zone wiki source', exact: true })).toHaveAttribute('href', 'https://ngu-idle.fandom.com/wiki/Clock_Dimension');
  await reference.getByRole('combobox', { name: 'Reference zone', exact: true }).selectOption('10');
  await expect(reference).toContainText('422,550'); await expect(reference).toContainText('282,966');
  await expect(reference).toContainText('With Spoopy set bonus');
  await navigation.getByRole('button', { name: 'Presets', exact: true }).click();
  await expect(presets.getByLabel('Browse preset')).toHaveValue('');
  await expect(presets).not.toContainText(/autosave|all changes saved|storage|polling/i);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await navigation.getByRole('button', { name: 'Misc', exact: true }).click();
  await expect(reference.getByRole('combobox', { name: 'Reference zone', exact: true })).toHaveValue('10');
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('uncertain Apply consumes the local token and requires a fresh preview without replay', async ({ page }) => {
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  const token = await page.evaluate(() => (window as FixtureWindow).__presetPreviewId);
  await page.evaluate(() => { (window as FixtureWindow).__presetApplyFailure = true; });
  await presets.getByRole('button', { name: 'Apply Shared NGU + blood' }).click();
  await expect(page.getByRole('alert')).toContainText('Outcome may be unknown');
  await expect(presets.getByRole('article', { name: 'Preset comparison' })).toHaveCount(0);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Misc', exact: true }).click();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Presets', exact: true }).click();
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toHaveLength(2);
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  expect(await page.evaluate(() => (window as FixtureWindow).__presetPreviewId)).not.toBe(token);
  await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood' })).toBeEnabled();
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toHaveLength(3);
});

test('injector sessions clear preview and capability loss disables commands', async ({ page }) => {
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  await page.evaluate(() => { (window as FixtureWindow).__presetRuntime.startedAt = 'session-two'; });
  await expect(presets.getByRole('article', { name: 'Preset comparison' })).toHaveCount(0);
  await page.evaluate(() => { (window as FixtureWindow).__presetRuntime.globalPresetsVersion = 0; });
  await expect(presets.getByRole('button', { name: 'Preview preset', exact: true })).toBeDisabled();
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toHaveLength(1);
});

test('Dashboard lists every global definition and shares one safe editor with the standalone tab', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-18T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-18T12:00:01Z'));
  const presets = await openPresets(page);
  const navigation = page.getByRole('navigation', { name: 'Primary' });
  await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
  const quick = presets.locator('[aria-label="Global quick presets"]');
  await expect(quick.getByRole('button', { name: /^Use / })).toHaveCount(2);
  await expect(quick.getByRole('button')).toHaveCount(2);
  await expect(quick.getByRole('button').first()).toHaveText('Balanced NGU');
  await expect(quick.getByRole('button', { name: /^(Equip|Edit) / })).toHaveCount(0);
  await expect(presets.getByLabel('Preset name', { exact: true })).toBeHidden();
  await expect(presets.getByLabel('Browse preset')).toBeHidden();
  for (const name of ['Add preset', 'Clone preset', 'Delete preset', 'Save library', 'Reload library']) await expect(presets.getByRole('button', { name, exact: true })).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  await quick.getByRole('button', { name: 'Use Background blood', exact: true }).click();
  await expect(presets.getByLabel('Preset name', { exact: true })).toHaveValue('Background blood');
  await expect(presets.getByLabel('Preset name', { exact: true })).toBeVisible();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([{ command: 'equipGlobalPreset', args: { presetId: 'background', libraryRevision: 'c'.repeat(64) } }]);
  await presets.getByRole('button', { name: 'Close preset details', exact: true }).click();
  await expect(presets.getByLabel('Preset name', { exact: true })).toBeHidden();
  await presets.getByRole('button', { name: 'Manage presets', exact: true }).click();
  await expect(navigation.getByRole('button', { name: 'Presets', exact: true })).toHaveAttribute('aria-current', 'page');
  await expect(presets.getByLabel('Browse preset')).toHaveValue('background');
  await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(presets.getByLabel('Preset name', { exact: true })).toBeHidden();
  await quick.getByRole('button', { name: 'Use Background blood', exact: true }).click();
  await presets.getByLabel('Preset name', { exact: true }).fill('Shared from Dashboard');
  await presets.getByLabel('Auto-update preset').uncheck();
  await expect(quick.getByRole('button', { name: 'Use Shared from Dashboard', exact: true })).toBeDisabled();
  await navigation.getByRole('button', { name: 'Presets', exact: true }).click();
  await expect(presets.getByLabel('Preset name', { exact: true })).toHaveValue('Shared from Dashboard');
  await expect(presets.getByLabel('Auto-update preset')).not.toBeChecked();
  expect(await page.evaluate(() => {
    const event = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  })).toBe(true);
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'background', Name: 'Shared from Dashboard', AutoUpdate: false });
  expect(await page.evaluate(() => {
    const event = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  })).toBe(false);
  expect((await page.evaluate(() => (window as FixtureWindow).__presetActions)).map(row => row.command)).toEqual(['equipGlobalPreset', 'equipGlobalPreset']);
  await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await quick.getByRole('button', { name: 'Use Shared from Dashboard', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([
    { command: 'equipGlobalPreset', args: { presetId: 'background', libraryRevision: 'c'.repeat(64) } },
    { command: 'equipGlobalPreset', args: { presetId: 'background', libraryRevision: 'c'.repeat(64) } },
    { command: 'equipGlobalPreset', args: { presetId: 'background', libraryRevision: 'd'.repeat(64) } }
  ]);
  const writes = await page.evaluate(() => (window as FixtureWindow).__presetWrites);
  expect((writes[0].Presets as Record<string, unknown>[])[1]).toMatchObject({ Id: 'background', Name: 'Shared from Dashboard', AutoUpdate: false });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  await expect(page.getByRole('button', { name: 'All NGU', exact: true })).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('Dashboard preset details keep controls and safety states without explanatory clutter', async ({ page }) => {
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await presets.getByLabel('Support mode').selectOption('planned');
  await waitForLibrary(page, { Id: 'balanced', SupportMode: 'planned' });
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Dashboard', exact: true }).click();
  await presets.getByLabel('Inspect preset').selectOption('balanced');
  await expect(presets.getByRole('article', { name: 'Preset settings', exact: true })).toBeVisible();
  await expect(presets).toContainText('Quick actions pause the plan and auto-rebirth.');
  await expect(presets.getByRole('region', { name: 'Beard settings', exact: true })).toBeVisible();
  const diggerMode = presets.getByRole('combobox', { name: 'Digger weights', exact: true });
  await expect(diggerMode).toHaveValue('automatic');
  await expect(presets.locator('input[aria-label$=" digger weight"]')).toHaveCount(0);
  const unnecessaryCopy = ['On: re-optimize and apply', 'Off: reuse the last valid policy', 'Execution time belongs to plan stages', 'Scheduled presets use stage timing', 'standalone presets do not expire', '1-hour forecast', 'optimize immediate stats, not Advanced Training gains', 'Enabled fill slots in priority order', 'Default weights follow preset priorities', 'Edited weights. Reset', '0 excludes a digger'];
  for (const autoUpdate of [false, true]) {
    await presets.getByLabel('Auto-update preset').setChecked(autoUpdate);
    await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__presetWrites.at(-1)?.Presets)).toEqual(expect.arrayContaining([expect.objectContaining({ Id: 'balanced', AutoUpdate: autoUpdate })]));
    for (const text of unnecessaryCopy) await expect(presets).not.toContainText(text);
  }
  await diggerMode.selectOption('custom');
  await presets.getByLabel('Stats digger weight', { exact: true }).fill('2');
  await expect(diggerMode).toHaveValue('custom');
  await diggerMode.selectOption('automatic');
  await expect(diggerMode).toHaveValue('automatic');
  await expect(presets.locator('input[aria-label$=" digger weight"]')).toHaveCount(0);
  await expect(presets.getByRole('button', { name: 'Reset digger weights', exact: true })).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('dashboard-copy.png'), fullPage: true });
});

test('Dashboard plan switching retains global preview and pending library drafts', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-18T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-18T12:00:01Z'));
  const presets = await openPresets(page);
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await presets.getByRole('button', { name: 'Preview preset', exact: true }).click();
  const navigation = page.getByRole('navigation', { name: 'Primary' });
  await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
  const plans = page.getByRole('article', { name: 'Plan controls' }).getByRole('combobox');
  await plans.selectOption('plan-b');
  await presets.getByRole('button', { name: 'Manage presets', exact: true }).click();
  await expect(presets.getByRole('button', { name: 'Apply Shared NGU + blood', exact: true })).toBeEnabled();
  await presets.getByRole('button', { name: 'Apply Shared NGU + blood', exact: true }).click();
  await presets.getByLabel('Preset name', { exact: true }).fill('Unfinished shared draft');
  await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await plans.selectOption('plan-a');
  await presets.getByRole('button', { name: 'Manage presets', exact: true }).click();
  await expect(presets.getByLabel('Preset name', { exact: true })).toHaveValue('Unfinished shared draft');
  await expect(presets.getByRole('button', { name: 'Equip preset', exact: true })).toBeDisabled();
  await page.clock.runFor(350);
  await waitForLibrary(page, { Id: 'balanced', Name: 'Unfinished shared draft' });
  await expect(presets.getByRole('button', { name: 'Equip preset', exact: true })).toBeEnabled();
  expect((await page.evaluate(() => (window as FixtureWindow).__presetActions)).map(row => row.command)).toEqual(['previewGlobalPreset', 'applyGlobalPreset']);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetDialogs)).toEqual([]);
});

test('closing Dashboard details preserves incomplete preset drafts for Manage presets without replaying Equip', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-18T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-10-18T12:00:01Z'));
  const presets = await openPresets(page);
  const navigation = page.getByRole('navigation', { name: 'Primary' });
  await presets.getByLabel('Browse preset').selectOption('balanced');
  await navigation.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(presets.getByLabel('Preset name', { exact: true })).toBeHidden();
  await presets.getByRole('button', { name: 'Use Balanced NGU', exact: true }).click();
  const weight = presets.getByLabel('Energy NGU weight', { exact: true });
  await weight.fill('-');
  await expect(presets.getByRole('button', { name: 'Preview preset', exact: true })).toBeDisabled();
  await expect(presets.getByRole('button', { name: 'Use Balanced NGU', exact: true })).toBeDisabled();
  await presets.getByRole('button', { name: 'Close preset details', exact: true }).click();
  await expect(weight).toBeHidden();
  await presets.getByRole('button', { name: 'Manage presets', exact: true }).click();
  await expect(presets.getByLabel('Browse preset')).toHaveValue('balanced');
  await expect(weight).toHaveValue('-');
  await expect(weight).toBeVisible();
  await page.clock.runFor(500);
  await expect(presets.getByRole('alert')).toContainText('Energy NGU');
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([{ command: 'equipGlobalPreset', args: { presetId: 'balanced', libraryRevision: 'c'.repeat(64) } }]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetDialogs)).toEqual([]);
});

test('plan and new stages use saved global selectors while retaining timing and automatic special mode', async ({ page }) => {
  await openPresets(page);
  const navigation = page.getByRole('navigation', { name: 'Primary' });
  await navigation.getByRole('button', { name: 'Misc', exact: true }).click();
  const gear = page.getByRole('article', { name: 'Gear settings', exact: true });
  await expect(gear.getByRole('heading', { name: 'Gear settings', exact: true })).toBeVisible();
  await expect(gear).toContainText('Plan:');
  await expect(gear).toContainText('plan-a');
  await expect(gear.getByRole('combobox', { name: 'Fight scoring', exact: true })).toBeVisible();
  await gear.getByLabel('Active preset', { exact: true }).selectOption('balanced');
  await expect.poll(() => page.evaluate(() => (window as FixtureWindow).__presetPlanWrites.length)).toBeGreaterThan(0);
  await navigation.getByRole('button', { name: 'Plan & Rebirth', exact: true }).click();
  const editor = page.getByRole('region', { name: 'Plan editor', exact: true });
  await expect(editor.getByLabel('Active preset', { exact: true })).toHaveCount(0);
  await editor.getByRole('button', { name: 'Add stage', exact: true }).click();
  const panel = editor.getByRole('region', { name: 'Stage 2 settings', exact: true });
  await expect(panel.getByLabel('Focus preset', { exact: true })).toHaveValue('@active');
  await expect(panel.getByLabel('Energy focus', { exact: true })).toHaveCount(0);
  await panel.getByLabel('Focus preset', { exact: true }).selectOption('background');
  await panel.getByLabel('Fight on entry', { exact: true }).check();
  await panel.getByLabel('Duration', { exact: true }).fill('7m');
  await panel.getByLabel('Duration', { exact: true }).press('Enter');
  await panel.getByLabel('Stage mode', { exact: true }).selectOption('1');
  await expect(panel).toContainText('Maximum 24 hours per automatic stage.');
  await panel.getByLabel('Stage mode', { exact: true }).selectOption('0');
  await expect(panel.getByLabel('Focus preset', { exact: true })).toHaveValue('background');
  await expect(panel.getByLabel('Duration', { exact: true })).toHaveValue('7m');
  await expect(panel.getByLabel('Fight on entry', { exact: true })).toBeChecked();
  await expect.poll(async () => {
    const writes = await page.evaluate(() => (window as FixtureWindow).__presetPlanWrites);
    return (writes.at(-1)?.Stages as Record<string, unknown>[] | undefined)?.[1];
  }).toMatchObject({ PresetId: 'background', DurationSeconds: 420, FightOnEntry: true, ExecutionMode: 0 });
  expect(await page.evaluate(() => (window as FixtureWindow).__presetActions)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__presetWrites)).toEqual([]);
});
