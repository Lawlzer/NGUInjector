import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { invoke } from '@tauri-apps/api/core';
import { LosslessNumber } from 'lossless-json';
import { render } from 'svelte/server';
import * as api from '../src/lib/api';
import type { JsonObject, JsonValue, NguiState } from '../src/lib/api';
import { createController } from '../src/lib/controller';
import { equal, object, validatePlan } from '../src/lib/model';
import { presetZones } from '../src/lib/options';
import { diggerNames, focusEfficiency, newPreset, objectiveKeys, objectiveLabels, presetFields, presetArgs, presetMetric, presetNumberInput, globalPresetRows, globalPresetArgs, globalPreviewMatches, normalizePreset, normalizePresetLibrary, rebasePresetLibrary, readPresetPreview, relativeProduction, validatePreset, validatePresetArgs, validatePresetLibrary } from '../src/lib/presets';
import Presets from '../src/lib/Presets.svelte';
import { proposedResourcePriorities, resourceAllocationNotice, resourceLaneOptions, resourceLanePrioritiesReady } from '../src/lib/presets';
import ZoneReference from '../src/lib/ZoneReference.svelte';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));
const native = vi.mocked(invoke);
beforeEach(() => { native.mockReset(); });

const row = (): JsonObject => ({ ...newPreset(), Id: 'balanced', ResourcePrioritiesReviewed: false, EnergyPriorities: null, MagicPriorities: null });
const previewId = '0123456789abcdef0123456789abcdef';
const library = (): JsonObject => ({ Version: 1, Presets: [row()], __externalUiRevision: 'a'.repeat(64) });
const globalArgs = (): JsonObject => globalPresetArgs('balanced', library());
function plan(): JsonObject {
  return { Version: 18, Name: 'test', __externalUiRevision: 'a'.repeat(64), Stages: [{ Name: 'NGU', Percent: 100, FightSchedule: null }], LoadoutPresets: [row()], GoalPresets: [{ Name: 'Legacy', GearGoal: 2 }], ActiveGoalPresetIndex: 0, GearGoal: 2, Future: { token: api.parseJson('9007199254740993') } };
}
function candidate(key = 'mixed'): JsonObject {
  return { key, label: key === 'current' ? 'Current gear' : 'Shared NGU + blood', itemIds: [123], items: [{ id: 123, name: 'Owned NGU hat', slot: -1, sourceSlot: 4, level: 100, factor: 1.5 }], metrics: [
    { key: 'energyNgu', label: 'Energy NGU', value: 200, unit: 'levels/s', quality: 'estimated', normalized: .5, relativeToCurrent: 2 },
    { key: 'blood', label: 'Blood', value: 12, unit: 'blood/s', quality: 'proxy', normalized: .4, relativeToCurrent: 1.2 },
    { key: 'zone', label: 'Zone farming', value: null, unit: 'drops/s', quality: 'unavailable', reason: 'Locked zone', normalized: null, relativeToCurrent: null }
  ], score: key === 'current' ? null : 50, magicBloodPercent: 15, energyAllocated: 1000, magicNguAllocated: 850, bloodAllocated: 150, bloodRitual: 2, zone: null, expectedHits: null, feasible: true,
    support: { mode: 'planned', beards: [0, 3], diggerWeights: [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0], diggers: [{ id: 2, name: 'Stats', level: 10, drain: 3 }], goldBudget: 75, goldDrain: 3, goldRemaining: 72, notes: ['Budget accounts for drain'] }, warnings: ['Frozen owned gear'] };
}
function result(args = presetArgs(row())): JsonObject {
  const preset = args.presetId ? row() : object(args.preset);
  return { version: 1, previewId, presetId: preset.Id, presetName: preset.Name, capturedAt: new Date().toISOString(), startedAt: 'session-one', horizonSeconds: 3600, objectiveKeys, baselineKey: 'current', candidates: [candidate('current'), candidate()], specialists: [{ ...candidate('energyNgu'), metrics: [{ key: 'energyNgu', value: 400, quality: 'estimated' }] }], sequence: { firstKey: 'energyNgu', secondKey: 'mixed', firstSeconds: 1500, secondSeconds: 2095, swapSeconds: 5, metrics: [], score: 52, improvementPercent: 4, quality: 'proxy', assumptions: ['Target-aware NGU phases; other bonuses and refill held fixed'] }, normalization: [400, 300, 30, 10, 20, 1, 1, 1, 1, 1, 1, 1, 1, 1], warnings: ['Bounded search'], assumptions: ['Same captured snapshot'], search: { optimality: 'heuristic', evaluated: 42, elapsedMilliseconds: 8, termination: 'budget' } };
}
function setup() {
  let savedPlan = plan();
  let savedLibrary = library();
  const snapshot: NguiState = { runtime_dir: 'A:/runtime', connected: true, marker_enabled: true, settings_revision: 'a'.repeat(64), settings: { _energyNguWeights: [0, 0, 0, 0, 1, 0, 1, 0, 0], _magicNguWeights: [1, 0, 0, 0, 0, 0, 0] }, protocol: {}, profiles: [], plans: ['test', 'other'], logs: [], command_results: [], runtime: { externalUiEnabled: true, status: 'running', protocolVersion: 3, stageTimingVersion: 1, fightScheduleVersion: 1, planVersion: 18, startedAt: 'session-one', globalPresetsVersion: 2, loadoutPresetsVersion: 2, globalNguWeightsVersion: 1, updatedAt: new Date().toISOString(), supportedCommands: ['previewGlobalPreset', 'applyGlobalPreset', 'equipGlobalPreset', 'status', 'readPlan', 'savePlan', 'previewLoadoutPreset', 'applyLoadoutPreset', 'applySettingsPatch'] } };
  let submitted: JsonObject = {};
  let command = '';
  const backend = { ...api, getState: vi.fn(async () => ({ ...snapshot, settings: api.cloneObject(snapshot.settings), runtime: api.cloneObject(snapshot.runtime) })), storeRuntimeDir: vi.fn(),
    readPlan: vi.fn(async () => api.cloneObject(savedPlan)),
    readPresetLibrary: vi.fn(async () => api.cloneObject(savedLibrary)),
    savePresetLibrary: vi.fn(async (_dir: string, payload: JsonObject) => { savedLibrary = { ...api.cloneObject(payload), __externalUiRevision: 'b'.repeat(64) }; return { success: true }; }),
    savePlan: vi.fn(async (_dir: string, _name: string, payload: JsonObject) => { savedPlan = { ...api.cloneObject(payload), __externalUiRevision: 'b'.repeat(64) }; return { success: true }; }),
    saveSettingsPatch: vi.fn(async (_dir: string, patch: JsonObject, _expectedRevision: string) => { snapshot.settings = { ...snapshot.settings, ...api.cloneObject(patch) }; return { success: true, data: { settings: api.cloneObject(snapshot.settings), revision: 'b'.repeat(64) } }; }),
    enqueueCommand: vi.fn(async (_dir: string, name: string, args: JsonObject = {}) => { submitted = api.cloneObject(args); command = name; return 'cmd'; }),
    readCommandResult: vi.fn(async (): Promise<JsonObject | null> => ({ id: 'cmd', command, success: true, data: ['previewGlobalPreset', 'previewLoadoutPreset'].includes(command) ? { result: result(submitted) } : {} })) };
  const confirm = vi.fn(async (_message: string) => true);
  const ui = createController(backend, confirm, snapshot.runtime_dir);
  return { ui, backend, snapshot, confirm };
}
const props = (state: ReturnType<ReturnType<typeof createController>['current']>) => ({ state, onchange: vi.fn(), onselect: vi.fn(), oncandidate: vi.fn(), onpreview: vi.fn(async () => {}), onapply: vi.fn(async () => {}), onequip: vi.fn(async () => {}), onstop: vi.fn(async () => {}), onreload: vi.fn(async () => {}), ondiscard: vi.fn(async () => {}) });

describe('reviewed resource priorities', () => {
  const reviewed = (): JsonObject => ({ ...row(), ResourcePrioritiesReviewed: true, EnergyPriorities: ['TM', 'NGU'], MagicPriorities: ['NGU', 'RIT'], AdventureDynamicWeight: 2 });

  it.each<JsonValue | undefined>([undefined, null, false, true, '2', 0, 1, 3])('blocks plan execution against committed reviewed bindings on capability %j', async capability => {
    const { ui, backend, snapshot } = setup();
    if (capability !== undefined) snapshot.runtime.resourceLanePrioritiesVersion = capability;
    const selectedPlan = { ...plan(), ActivePresetId: 'balanced', AutoRebirthEnabled: false };
    backend.readPlan.mockResolvedValue(selectedPlan);
    await ui.refresh(); await ui.loadPlan('test');
    // A later committed review must win over the older unreviewed library in the editor.
    backend.readPresetLibrary.mockResolvedValue({ ...library(), Presets: [reviewed()] });
    for (const action of ['activate', 'run'] as const) {
      await ui.planAction(action, true);
      expect(ui.current().error).toContain('resourceLanePrioritiesVersion');
      expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
      expect(backend.savePlan).not.toHaveBeenCalled();
    }
    await ui.planAction('stop');
    expect(backend.saveSettingsPatch).toHaveBeenCalledExactlyOnceWith('A:/runtime', { _useCustomRebirthPlan: false, _autoRebirth: false }, 'a'.repeat(64));
    expect(backend.enqueueCommand).not.toHaveBeenCalled(); ui.dispose();
  });

  it.each(['active', 'stage', 'inherited'])('starts a reviewed %s binding only on capable runtimes', async binding => {
    const { ui, backend, snapshot } = setup();
    snapshot.runtime.resourceLanePrioritiesVersion = 2;
    const selectedPlan = { ...plan(), ActivePresetId: binding === 'stage' ? '' : 'balanced', Stages: [{ Name: 'Bound', Percent: 100, PresetId: binding === 'stage' ? 'balanced' : binding === 'inherited' ? '@active' : '' }] };
    backend.readPlan.mockResolvedValue(selectedPlan);
    backend.readPresetLibrary.mockResolvedValue({ ...library(), Presets: [reviewed()] });
    await ui.refresh(); await ui.loadPlan('test'); await ui.planAction('run', false);
    expect(ui.current().error).toBe('');
    expect(backend.saveSettingsPatch).toHaveBeenCalledExactlyOnceWith('A:/runtime', { _customPlanFile: 'test', _useCustomRebirthPlan: true, _autoRebirth: false }, 'a'.repeat(64));
    expect(backend.savePresetLibrary).not.toHaveBeenCalled(); ui.dispose();
  });

  it.each(['bulk', 'inline', 'command'])('blocks old-runtime settings execution through the %s path', async mode => {
    const { ui, backend, snapshot } = setup();
    snapshot.settings._customPlanFile = 'test'; snapshot.settings._useCustomRebirthPlan = false;
    backend.readPlan.mockResolvedValue({ ...plan(), ActivePresetId: 'balanced' });
    backend.readPresetLibrary.mockResolvedValue({ ...library(), Presets: [reviewed()] });
    await ui.refresh();
    if (mode === 'inline') expect(await ui.saveSettingValue('_useCustomRebirthPlan', true)).toBe(false);
    else if (mode === 'command') await ui.runCommand('applySettingsPatch', { patch: { _useCustomRebirthPlan: true } });
    else { ui.setSetting('_useCustomRebirthPlan', true); await ui.saveSettings(); }
    expect(ui.current().error).toContain('resourceLanePrioritiesVersion');
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled(); ui.dispose();
  });

  it('refuses plan execution when the injector session changes during the library read', async () => {
    const { ui, backend, snapshot } = setup();
    snapshot.runtime.resourceLanePrioritiesVersion = 2;
    backend.readPlan.mockResolvedValue({ ...plan(), ActivePresetId: 'balanced' });
    await ui.refresh(); await ui.loadPlan('test');
    let release!: (library: JsonObject) => void;
    backend.readPresetLibrary.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
    const starting = ui.planAction('run');
    await vi.waitFor(() => expect(backend.readPresetLibrary).toHaveBeenCalledTimes(2));
    snapshot.runtime.startedAt = 'session-two'; await ui.refresh(true, true, false);
    release({ ...library(), Presets: [reviewed()] }); await starting;
    expect(ui.current().error).toContain('session changed');
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled(); ui.dispose();
  });

  it('keeps legacy review proposals goal-derived without limiting selectable activities', () => {
    const preset = { ...row(), Weights: [2, 5, 5, 99, 99, 99, 5, 2, 2, 0, 99, 99, 99, 99] };
    const before = api.stringifyJson(preset);
    expect(proposedResourcePriorities(preset, true)).toEqual(['TM', 'NGU', 'AUG', 'WAN']);
    expect(proposedResourcePriorities(preset, false)).toEqual(['NGU', 'RIT', 'TM', 'WAN']);
    expect(resourceLaneOptions(preset, true).map(row => row.value)).toEqual(['NGU', 'TM', 'AUG', 'WAN', 'AT']);
    expect(proposedResourcePriorities({ ...preset, Weights: Array(14).fill(0), AdventureDynamicWeight: 100 }, true)).toEqual([]);
    expect(api.stringifyJson(preset)).toBe(before);
  });

  it('offers every valid resource activity for non-production goals without changing gear or support weights', () => {
    const preset = { ...row(), Weights: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 2, 0, 0], DiggerWeights: Array(12).fill(4), EnergyPriorities: [], MagicPriorities: [] };
    const before = api.stringifyJson(preset);
    expect(resourceLaneOptions(preset, true)).toEqual([
      { value: 'NGU', label: 'Energy NGU', group: 'Activities' }, { value: 'TM', label: 'Time Machine', group: 'Activities' },
      { value: 'AUG', label: 'Augments', group: 'Activities' }, { value: 'WAN', label: 'Wandoos', group: 'Activities' },
      { value: 'AT', label: 'Advanced Training', group: 'Activities' }
    ]);
    expect(resourceLaneOptions(preset, false)).toEqual([
      { value: 'NGU', label: 'Magic NGU', group: 'Activities' }, { value: 'RIT', label: 'Blood', group: 'Activities' },
      { value: 'TM', label: 'Time Machine', group: 'Activities' }, { value: 'WAN', label: 'Wandoos', group: 'Activities' }
    ]);
    expect(validatePreset({ ...preset, ResourcePrioritiesReviewed: true, EnergyPriorities: ['NGU', 'TM', 'AUG', 'WAN', 'AT'], MagicPriorities: ['NGU', 'RIT', 'TM', 'WAN'] })).toBeUndefined();
    expect(api.stringifyJson(preset)).toBe(before);
  });

  const allocationOnly = (): JsonObject => ({ ...row(), Weights: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0], ResourcePrioritiesReviewed: true, EnergyPriorities: ['NGU'], MagicPriorities: ['NGU'] });

  it('explains NGU-first allocation separately from non-production gear goals without mutating the preset', () => {
    const preset = allocationOnly();
    const before = api.stringifyJson(preset);
    expect(resourceAllocationNotice(preset)).toBe('This preset does not optimize Energy/Magic production. Energy and Magic prioritize NGUs using your Misc settings.');
    expect(api.stringifyJson(preset)).toBe(before);
  });

  it.each([
    [0, 'Magic'], [1, 'Energy'], [2, 'Energy'], [6, ''], [7, 'Magic'], [8, ''], [9, 'Magic'],
    [12, 'Energy/Magic'], [13, 'Energy/Magic']
  ] as const)('only explains pools without production goals when objective %i is positive', (index, pool) => {
    const preset = allocationOnly();
    (preset.Weights as number[])[index] = 2;
    const message = resourceAllocationNotice(preset);
    expect(message).toBe(pool ? `This preset does not optimize ${pool} production. ${pool === 'Energy/Magic' ? 'Energy and Magic prioritize' : `${pool} prioritizes`} NGUs using your Misc settings.` : '');
  });

  it.each<JsonObject>([
    { ResourcePrioritiesReviewed: false }, { EnergyPriorities: [], MagicPriorities: [] },
    { EnergyPriorities: ['TM', 'NGU'], MagicPriorities: ['RIT', 'NGU'] },
    { EnergyPriorities: null, MagicPriorities: null }
  ])('does not promise NGU allocation for unreviewed, empty or later NGU orders %j', fields => {
    expect(resourceAllocationNotice({ ...allocationOnly(), ...fields })).toBe('');
  });

  it('places the resource explanation before the selected preset controls', async () => {
    const { ui, backend } = setup();
    backend.readPresetLibrary.mockResolvedValue({ ...library(), Presets: [allocationOnly()] });
    await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const body = render(Presets, { props: props(ui.current()) }).body;
    expect(body).toContain('aria-label="Resource allocation"');
    expect(body.indexOf(resourceAllocationNotice(allocationOnly()))).toBeLessThan(body.indexOf('Preset name'));
    expect(body).toContain('Gear goals');
    expect(body).toContain('Energy priorities');
    expect(body).toContain('Magic priorities');
    expect(body).toContain('Priority #1 funds to its useful cap; only leftovers go to #2 and later priorities.');
    ui.dispose();
  });

  it.each([true, false])('shows allocation-only AT ratios only for reviewed priorities, reviewed=%s', async reviewed => {
    const { ui, backend } = setup();
    const preset = { ...allocationOnly(), ResourcePrioritiesReviewed: reviewed, EnergyPriorities: ['NGU', 'AT'], AdvancedTrainingWeights: [3, 4, 0, 0, 0] };
    backend.readPresetLibrary.mockResolvedValue({ ...library(), Presets: [preset] });
    await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const body = render(Presets, { props: props(ui.current()) }).body;
    expect(body.includes('aria-label="Preset AT Power ratio"')).toBe(reviewed);
    expect(body).not.toContain('aria-label="Advanced Training weight"');
    expect(globalPresetRows(ui.current().presetLibrary)[0]).toMatchObject(preset);
    ui.dispose();
  });

  it('renders legacy proposals without conversion or autosave', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup();
    try {
      const legacy: JsonObject = { ...row(), Weights: [2, 1, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0] };
      delete legacy.EnergyPriorities; delete legacy.MagicPriorities; delete legacy.ResourcePrioritiesReviewed;
      backend.readPresetLibrary.mockResolvedValue({ ...library(), Presets: [legacy] });
      await ui.refresh(); ui.selectLoadoutPreset('balanced');
      const before = api.stringifyJson(ui.current().presetLibrary);
      const callbacks = props(ui.current());
      const body = render(Presets, { props: callbacks }).body;
      expect(body).toContain('Legacy proportional allocation');
      expect(body).toContain('Review &amp; enable priorities');
      expect(callbacks.onchange).not.toHaveBeenCalled();
      await vi.advanceTimersByTimeAsync(1000);
      expect(backend.savePresetLibrary).not.toHaveBeenCalled();
      expect(ui.current().presetLibraryDirty).toBe(false);
      expect(api.stringifyJson(ui.current().presetLibrary)).toBe(before);
    } finally { ui.dispose(); vi.useRealTimers(); }
  });

  it.each<JsonObject>([
    { ResourcePrioritiesReviewed: null }, { ResourcePrioritiesReviewed: 'true' },
    { ResourcePrioritiesReviewed: true }, { ResourcePrioritiesReviewed: true, EnergyPriorities: [], MagicPriorities: null },
    { EnergyPriorities: ['RIT'] }, { MagicPriorities: ['AUG'] }, { MagicPriorities: ['AT'] },
    { EnergyPriorities: ['ngu'] }, { EnergyPriorities: [' NGU'] }, { EnergyPriorities: ['NGU-0'] },
    { EnergyPriorities: ['NGU', 'NGU'] }, { MagicPriorities: ['NGU', 'RIT', 'TM', 'WAN', 'NGU'] },
    { EnergyPriorities: [1] }, { MagicPriorities: 'NGU' }
  ])('rejects invalid lane schema %j', fields => {
    const preset = { ...row(), ...fields };
    expect(() => validatePreset(preset)).toThrow(/Priorities/);
    expect(() => presetArgs(preset)).toThrow(/Priorities/);
  });

  for (const reviewed of [undefined, false, true]) {
    for (const missing of [undefined, null]) {
      it.each(['EnergyPriorities', 'MagicPriorities'] as const)(`rejects asymmetric %s with reviewed=${reviewed} and other lane=${missing}`, async lane => {
        const { ui, backend } = setup(); await ui.refresh();
        const preset = row();
        delete preset.EnergyPriorities; delete preset.MagicPriorities; delete preset.ResourcePrioritiesReviewed;
        preset[lane] = ['NGU'];
        if (missing === null) preset[lane === 'EnergyPriorities' ? 'MagicPriorities' : 'EnergyPriorities'] = null;
        if (reviewed !== undefined) preset.ResourcePrioritiesReviewed = reviewed;
        const before = api.stringifyJson(preset);
        expect(() => validatePreset(preset)).toThrow('must both be arrays or both null');
        expect(() => presetArgs(preset)).toThrow('must both be arrays or both null');
        ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [preset] });
        expect(await ui.savePresetLibrary()).toBe(false);
        expect(backend.savePresetLibrary).not.toHaveBeenCalled();
        expect(api.stringifyJson(preset)).toBe(before);
        ui.dispose();
      });
    }
  }

  it.each([undefined, false])('preserves paired unreviewed proposals with review flag %j', async reviewed => {
    const { ui, backend } = setup(); await ui.refresh();
    const preset: JsonObject = { ...row(), EnergyPriorities: ['TM', 'NGU'], MagicPriorities: [], Future: { exact: api.parseJson('9007199254740993') } };
    delete preset.ResourcePrioritiesReviewed;
    if (reviewed !== undefined) preset.ResourcePrioritiesReviewed = reviewed;
    const before = api.stringifyJson(preset);
    expect(validatePreset(preset)).toBeUndefined();
    expect(newPreset(preset)).toMatchObject({ EnergyPriorities: ['TM', 'NGU'], MagicPriorities: [], ResourcePrioritiesReviewed: false });
    expect(object(presetArgs(preset).preset)).toMatchObject({ EnergyPriorities: ['TM', 'NGU'], MagicPriorities: [], ResourcePrioritiesReviewed: false });
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [preset] });
    expect(await ui.savePresetLibrary()).toBe(true);
    await ui.loadPresetLibrary();
    expect(globalPresetRows(ui.current().presetLibrary)[0]).toEqual(normalizePreset(preset));
    expect(api.stringifyJson(preset)).toBe(before);
    expect(backend.enqueueCommand).not.toHaveBeenCalled(); ui.dispose();
  });

  it('round-trips reviewed empty lanes and cloning without deriving new orders', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    const preset = { ...reviewed(), EnergyPriorities: [], MagicPriorities: ['RIT', 'NGU'] };
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [preset] });
    expect(await ui.savePresetLibrary()).toBe(true);
    await ui.loadPresetLibrary();
    expect(globalPresetRows(ui.current().presetLibrary)).toEqual([preset]);
    const clone = newPreset(preset);
    expect(clone).toMatchObject({ ResourcePrioritiesReviewed: true, EnergyPriorities: [], MagicPriorities: ['RIT', 'NGU'], AdventureDynamicWeight: 2 });
    expect(clone.MagicPriorities).not.toBe(preset.MagicPriorities);
    expect(presetArgs(clone).preset).toEqual(clone);
    expect(presetFields).toEqual(expect.arrayContaining(['EnergyPriorities', 'MagicPriorities', 'ResourcePrioritiesReviewed']));
    expect(backend.enqueueCommand).not.toHaveBeenCalled(); ui.dispose();
  });

  it('merges independent lane edits without losing external or pending data', () => {
    const base = { ...library(), Presets: [reviewed()] };
    const draft = { ...base, Presets: [{ ...reviewed(), EnergyPriorities: ['NGU', 'TM'] }] };
    const fresh = { ...base, __externalUiRevision: 'b'.repeat(64), Presets: [{ ...reviewed(), MagicPriorities: ['RIT', 'NGU'], Future: { exact: api.parseJson('9007199254740993') } }] };
    const merged = globalPresetRows(rebasePresetLibrary(base, draft, fresh))[0];
    expect(merged).toMatchObject({ EnergyPriorities: ['NGU', 'TM'], MagicPriorities: ['RIT', 'NGU'], ResourcePrioritiesReviewed: true });
    expect(String(object(merged.Future).exact)).toBe('9007199254740993');
    expect(() => rebasePresetLibrary(base, draft, { ...fresh, Presets: [{ ...reviewed(), EnergyPriorities: [] }] })).toThrow('Source changed');
  });

  it.each<JsonValue | undefined>([undefined, null, 0, 1, 3, '2', true])('blocks reviewed global and inline actions on unsupported capability %j', async capability => {
    const { ui, backend, snapshot } = setup();
    if (capability === undefined) delete snapshot.runtime.resourceLanePrioritiesVersion; else snapshot.runtime.resourceLanePrioritiesVersion = capability;
    snapshot.runtime.adventureDynamicPresetsVersion = 1;
    backend.readPresetLibrary.mockResolvedValue({ ...library(), Presets: [reviewed()] });
    await ui.refresh(); ui.selectLoadoutPreset('balanced');
    expect(resourceLanePrioritiesReady(ui.current().snapshot, reviewed())).toBe(false);
    for (const command of ['previewGlobalPreset', 'applyGlobalPreset', 'equipGlobalPreset', 'previewLoadoutPreset', 'applyLoadoutPreset']) {
      const args = command.includes('Global') ? globalArgs() : presetArgs(reviewed());
      await ui.runCommand(command, { ...args, ...(command.startsWith('apply') ? { candidateKey: 'mixed', previewId } : {}) });
      expect(ui.current().error).toContain('resourceLanePrioritiesVersion');
    }
    const body = render(Presets, { props: props(ui.current()) }).body;
    expect(body).toContain('Update the injector to use reviewed resource priorities.');
    expect(backend.enqueueCommand).not.toHaveBeenCalled(); ui.dispose();
  });

  it('sends reviewed actions with the existing ID/revision contract on capable runtimes', async () => {
    const { ui, backend, snapshot } = setup();
    snapshot.runtime.resourceLanePrioritiesVersion = 2;
    backend.readPresetLibrary.mockResolvedValue({ ...library(), Presets: [{ ...reviewed(), AdventureDynamicWeight: 0 }] });
    await ui.refresh(); await ui.equipGlobalPreset('balanced');
    expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'equipGlobalPreset', globalArgs());
    expect(resourceLanePrioritiesReady(snapshot, row())).toBe(true); ui.dispose();
  });
});

describe('preset definitions', () => {
  it('defaults absent dynamic weight to zero without migrating fixed farming goals or beard omissions', () => {
    const source: JsonObject = { Id: 'builtin_pp', Name: 'Edited PP', Weights: [0, 0, 0, 7, 0, 0], Future: api.parseJson('9007199254740993') };
    const before = api.stringifyJson(source);
    const normalized = normalizePreset(source);
    expect(normalized).toMatchObject({ Id: source.Id, Name: source.Name, AdventureDynamicWeight: 0, Weights: [0, 0, 0, 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] });
    expect(normalized).not.toHaveProperty('BeardRotation');
    expect(String(normalized.Future)).toBe('9007199254740993');
    expect(api.stringifyJson(source)).toBe(before);
  });
  it.each([.25, 1, 1e6])('accepts dynamic-only and combined goals at %s and forwards the known numeric field', AdventureDynamicWeight => {
    const source = { ...row(), AdventureDynamicWeight, Weights: Array(14).fill(0) };
    expect(validatePreset(source)).toBeUndefined();
    expect(presetFields).toContain('AdventureDynamicWeight');
    expect(object(presetArgs(source).preset)).toMatchObject(source);
    expect(validatePreset({ ...source, Weights: [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] })).toBeUndefined();
    const persisted = { ...source, AdventureDynamicWeight: new LosslessNumber(String(AdventureDynamicWeight)) };
    expect(normalizePreset(persisted).AdventureDynamicWeight).toBe(AdventureDynamicWeight);
    expect(object(presetArgs(persisted).preset).AdventureDynamicWeight).toBe(AdventureDynamicWeight);
    expect(persisted.AdventureDynamicWeight).toBeInstanceOf(LosslessNumber);
  });
  it.each<JsonValue>([null, false, '1', '', -1, 1e6 + .01, Infinity, NaN, {}])('rejects invalid dynamic weight %j even with positive fixed goals', AdventureDynamicWeight => {
    expect(() => validatePreset({ ...row(), AdventureDynamicWeight })).toThrow('Dynamic adventure weight');
  });
  it('rejects all-zero goals including explicit zero dynamic weight', () => {
    expect(() => validatePreset({ ...row(), Weights: Array(12).fill(0), AdventureDynamicWeight: 0 })).toThrow('positive');
  });
  it('defaults new presets to optimized support', () => {
    expect(newPreset()).toMatchObject({ SupportMode: 'planned', DiggerBudgetPercent: 75, BeardPriority: [1, 3, 5, 6], BeardRotation: [0, 2, 4], DiggerWeights: [] });
  });
  it.each([undefined, 'current', 'planned'])('normalizes missing support without replacing explicit %s choices', SupportMode => {
    const source = row();
    if (SupportMode === undefined) delete source.SupportMode; else source.SupportMode = SupportMode;
    const before = api.stringifyJson(source);
    const expected = SupportMode ?? 'planned';
    expect(validatePreset(source)).toBeUndefined();
    expect(normalizePreset(source).SupportMode).toBe(expected);
    expect(newPreset(source).SupportMode).toBe(expected);
    expect(globalPresetRows(normalizePresetLibrary({ ...library(), Presets: [source] }))[0].SupportMode).toBe(expected);
    expect(object(presetArgs(source).preset).SupportMode).toBe(expected);
    expect(validatePlan({ ...plan(), LoadoutPresets: [source] })).toBeUndefined();
    expect(api.stringifyJson(source)).toBe(before);
  });
  it('uses JSON-string IPC for the global library and preserves unknown exact numeric tokens', async () => {
    const source = { ...library(), Future: api.parseJson('9007199254740993') };
    native.mockResolvedValueOnce(api.stringifyJson(source)).mockResolvedValueOnce({ success: true });
    const loaded = await api.readPresetLibrary('A:/runtime');
    expect(String(loaded.Future)).toBe('9007199254740993');
    await api.savePresetLibrary('A:/runtime', loaded);
    expect(native).toHaveBeenNthCalledWith(1, 'read_preset_library', { runtimeDir: 'A:/runtime' });
    expect(native).toHaveBeenNthCalledWith(2, 'save_preset_library', { runtimeDir: 'A:/runtime', libraryJson: api.stringifyJson(source) });
  });
  it.each([{}, { Version: 1, Presets: [] }, { Version: 2, Presets: [], __externalUiRevision: 'a'.repeat(64) }])('rejects malformed native library reads %j', async source => {
    native.mockResolvedValueOnce(api.stringifyJson(source));
    await expect(api.readPresetLibrary('A:/runtime')).rejects.toThrow();
  });
  it('rejects object IPC for native library reads', async () => {
    native.mockResolvedValueOnce(library());
    await expect(api.readPresetLibrary('A:/runtime')).rejects.toThrow('JSON string');
  });
  it.each<[string, JsonValue]>([
    ['AutoUpdate', 'false'], ['AutoUpdate', null], ['Weights', Array(9).fill(1)],
    ['AdvancedTrainingWeights', [1, 1, -1, 0, 0]], ['AdvancedTrainingWeights', [1, 1, 0, 0]]
  ])('rejects invalid global %s values %j', (key, value) => {
    expect(() => validatePreset({ ...row(), [key]: value })).toThrow();
  });
  it.each([6, 10, 12, 14])('normalizes %i objectives and defaults AT ratios without overriding strict false', count => {
    const source: JsonObject = { ...row(), Weights: Array.from({ length: count }, (_, i) => i + 1), AutoUpdate: false, Future: api.parseJson('9007199254740993') };
    delete source.EnergyNguWeights; delete source.MagicNguWeights; delete source.AdvancedTrainingWeights;
    const before = api.stringifyJson(source);
    const normalized = normalizePreset(source);
    expect(normalized).toMatchObject({ AutoUpdate: false, Weights: [...source.Weights as number[], ...Array(14 - count).fill(0)], AdvancedTrainingWeights: [1, 1, 0, 0, 0] });
    expect(validatePreset(source)).toBeUndefined();
    expect(object(presetArgs(source).preset).Weights).toEqual(normalized.Weights);
    expect(normalized).not.toHaveProperty('EnergyNguWeights');
    expect(normalized).not.toHaveProperty('MagicNguWeights');
    expect(String(normalized.Future)).toBe('9007199254740993');
    expect(api.stringifyJson(source)).toBe(before);
    const absent: JsonObject = { ...source }; delete absent.AutoUpdate;
    expect(normalizePreset(absent).AutoUpdate).toBe(true);
  });
  it.each<JsonValue>([3600, 0, -1, 'ignored legacy duration', null, { Future: true }])('ignores and strips legacy horizon %j while preserving other unknown fields', HorizonSeconds => {
    const source = { ...row(), HorizonSeconds, Future: { exact: api.parseJson('9007199254740993') } };
    const before = api.stringifyJson(source);
    expect(validatePreset(source)).toBeUndefined();
    for (const normalized of [normalizePreset(source), newPreset(source), globalPresetRows(normalizePresetLibrary({ ...library(), Presets: [source] }))[0]]) {
      expect(normalized).not.toHaveProperty('HorizonSeconds');
      expect(equal(normalized.Future, source.Future)).toBe(true);
    }
    expect(presetFields).not.toContain('HorizonSeconds');
    expect(object(presetArgs(source).preset)).not.toHaveProperty('HorizonSeconds');
    expect(api.stringifyJson(source)).toBe(before);
  });
  it.each([10, 11])('accepts direct adventure priority at index %i independently of training', index => {
    expect(objectiveKeys).toEqual(['energyNgu', 'magicNgu', 'blood', 'pp', 'boosts', 'zone', 'timeMachine', 'augments', 'wandoos', 'advancedTraining', 'adventurePower', 'adventureToughness', 'energyCap', 'magicCap']);
    expect(objectiveLabels.slice(10, 12)).toEqual(['Adventure power', 'Adventure toughness']);
    const direct = { ...row(), Weights: Array.from({ length: 14 }, (_, i) => i === index ? 100 : 0), AdvancedTrainingWeights: [0, 0, 0, 0, 0] };
    expect(validatePreset(direct)).toBeUndefined();
    expect(object(presetArgs(direct).preset).Weights).toEqual(direct.Weights);
    for (const value of [-1, Infinity, 'invalid', 1e6 + 1]) expect(() => validatePreset({ ...direct, Weights: direct.Weights.map((weight, i) => i === index ? value : weight) })).toThrow(objectiveLabels[index]);
  });
  it.each([12, 13])('accepts exclusive cap weight at index %i without enabling training or dynamic goals', index => {
    const cap = { ...row(), Weights: Array.from({ length: 14 }, (_, i) => i === index ? .25 : 0), AdvancedTrainingWeights: [0, 0, 0, 0, 0] };
    const before = api.stringifyJson(cap);
    expect(normalizePreset(cap)).toEqual(cap);
    expect(object(presetArgs(cap).preset)).toEqual(cap);
    expect(api.stringifyJson(cap)).toBe(before);
    for (const value of [-1, Infinity, NaN, '1', null, 1e6 + 1]) expect(() => validatePreset({ ...cap, Weights: cap.Weights.map((weight, i) => i === index ? value : weight) })).toThrow(objectiveLabels[index]);
    expect(validatePreset({ ...cap, Weights: cap.Weights.map(weight => weight > 0 ? 1e6 : 0) })).toBeUndefined();
  });
  it('merges external preset additions without replacing locally edited definitions', () => {
    const base = library(), draft = { ...base, Presets: [{ ...row(), Name: 'Local' }] };
    const fresh = { ...base, __externalUiRevision: 'b'.repeat(64), Presets: [row(), { ...row(), Id: 'other', Name: 'External' }] };
    expect(rebasePresetLibrary(base, draft, fresh)).toMatchObject({ __externalUiRevision: 'b'.repeat(64), Presets: [{ ...row(), Name: 'Local' }, { ...row(), Id: 'other', Name: 'External' }] });
    expect(() => rebasePresetLibrary(base, draft, { ...fresh, Presets: [{ ...row(), Name: 'Conflict' }] })).toThrow('Source changed');
  });
  it.each([1e16, 1e18, 1e20, Number.MAX_VALUE])('sends known doubles through real object IPC at %s without changing lossless persisted fields', async minimum => {
    native.mockResolvedValue('native-command');
    const fraction = api.parseJson('0.123456789012345678901');
    const persisted = api.cloneObject({ ...row(), MinimumPower: minimum, MinimumToughness: minimum, Weights: [fraction, 0, 0, 0, 0, 0], DiggerWeights: Array(12).fill(fraction), Future: { exact: api.parseJson('9007199254740993') } });
    const before = api.stringifyJson(persisted);
    const args = presetArgs(persisted);
    const actionPreset = object(args.preset);
    expect(actionPreset.MinimumPower).toBe(minimum); expect(actionPreset.MinimumToughness).toBe(minimum);
    expect(actionPreset.Weights).toEqual([Number(fraction), ...Array(13).fill(0)]); expect(actionPreset.DiggerWeights).toEqual(Array(12).fill(Number(fraction)));
    expect(actionPreset).not.toHaveProperty('Future');
    // Keep api.enqueueCommand and its recursive object-IPC guard real; mock only native invoke.
    expect(await api.enqueueCommand('A:/runtime', 'previewLoadoutPreset', args)).toBe('native-command');
    const applyArgs = { ...args, candidateKey: 'mixed', previewId };
    expect(await api.enqueueCommand('A:/runtime', 'applyLoadoutPreset', applyArgs)).toBe('native-command');
    expect(native).toHaveBeenNthCalledWith(1, 'enqueue_command', { runtimeDir: 'A:/runtime', envelope: { command: 'previewLoadoutPreset', args } });
    expect(native).toHaveBeenNthCalledWith(2, 'enqueue_command', { runtimeDir: 'A:/runtime', envelope: { command: 'applyLoadoutPreset', args: applyArgs } });
    expect(api.stringifyJson(persisted)).toBe(before); expect(object(persisted.Future).exact).toBeInstanceOf(LosslessNumber);
    expect(String(object(persisted.Future).exact)).toBe('9007199254740993');
  });
  it('preserves blank, partial decimal, exponent and invalid input without turning it into zero', () => {
    for (const text of ['', '-', '.', '1.', '1e', '1e-', 'abc', '1e999']) expect(presetNumberInput(text)).toBe(text);
    expect(presetNumberInput('1.5')).toBe(1.5); expect(presetNumberInput('1e3')).toBe(1000); expect(presetNumberInput('0')).toBe(0);
  });
  it('creates useful defaults and clones unknown fields losslessly with a fresh ID', () => {
    const original: JsonObject = { ...row(), Future: { exact: api.parseJson('9007199254740993') } };
    const clone = newPreset(original);
    expect(clone.Id).not.toBe(original.Id);
    expect(clone.Weights).toEqual([1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
    expect(clone).not.toHaveProperty('HorizonSeconds');
    expect(equal(clone.Future, original.Future)).toBe(true);
    expect(clone.Future).not.toBe(original.Future);
    expect(validatePreset(clone)).toBeUndefined();
    expect(diggerNames).toHaveLength(12);
    expect(diggerNames[4]).toBe('Energy NGU');
    expect(diggerNames[10]).toBe('Blood');
  });
  it('allows absent/empty libraries independently of legacy gear presets without mutation', () => {
    for (const library of [undefined, [], [row()]]) {
      const value = plan(); if (library === undefined) delete value.LoadoutPresets; else value.LoadoutPresets = library;
      const before = api.stringifyJson(value);
      expect(validatePlan(value)).toBeUndefined();
      expect(api.stringifyJson(value)).toBe(before);
      expect(value.GearGoal).toBe(2);
      expect(value.ActiveGoalPresetIndex).toBe(0);
    }
  });
  it.each<[string, JsonValue]>([
    ['Id', '../bad'], ['Id', 'x'.repeat(65)], ['Name', ''], ['Name', 'x'.repeat(101)],
    ['Weights', [0, 0, 0, 0, 0, 0]], ['Weights', [1]], ['Weights', [1, -1, 0, 0, 0, 0]], ['Weights', [1, Infinity, 0, 0, 0, 0]], ['Weights', [1, 1e6 + 1, 0, 0, 0, 0]],
    ['Weights', Array(11).fill(1)], ['Weights', Array(13).fill(1)], ['MagicBloodPercent', -.5], ['MagicBloodPercent', 100.1], ['SupportMode', 'unlimited'], ['SupportMode', null], ['SupportMode', false], ['SupportMode', 0], ['DiggerBudgetPercent', 75.5], ['DiggerBudgetPercent', 96], ['SwapSeconds', 5.5],
    ['Zone', 6], ['Zone', 1000], ['Zone', -2], ['MinimumPower', -1], ['MinimumToughness', NaN], ['SwapSeconds', 3601],
    ['BeardPriority', [0, 0]], ['BeardPriority', [7]], ['BeardPriority', [.5]], ['DiggerWeights', [1]], ['DiggerWeights', Array(12).fill(-1)]
  ])('rejects invalid %s = %j without altering the draft', (key, value) => {
    const draft = { ...row(), [key]: value };
    expect(() => validatePreset(draft)).toThrow();
    expect(draft[key]).toEqual(value);
  });
  it('accepts maximum boundaries and rejects duplicate IDs and oversized libraries', () => {
    expect(validatePreset({ ...row(), Id: 'a'.repeat(64), Name: 'n'.repeat(100), Weights: [1e6, 0, 0, 0, 0, 0], MagicBloodPercent: 100, DiggerBudgetPercent: 95, SwapSeconds: 3600, BeardPriority: [6, 5, 4, 3, 2, 1, 0], BeardRotation: [], DiggerWeights: Array(12).fill(1e6) })).toBeUndefined();
    expect(() => validatePresetLibrary([row(), row()])).toThrow('unique');
    expect(validatePresetLibrary(Array.from({ length: 32 }, (_, i) => ({ ...row(), Id: `p${i}` })))).toBeUndefined();
    expect(() => validatePresetLibrary(Array.from({ length: 33 }, (_, i) => ({ ...row(), Id: `p${i}` })))).toThrow('32');
    for (const zone of presetZones) expect(validatePreset({ ...row(), Zone: zone.value })).toBeUndefined();
    expect(presetZones.map(zone => zone.value)).not.toContain(42);
  });
  it('submits only the inline definition without a plan or NGU overrides', () => {
    const preset = { ...row(), EnergyNguWeights: [3], MagicNguWeights: null, Unknown: api.parseJson('9007199254740993') };
    const args = presetArgs(preset);
    expect(args).toEqual({ preset: row() });
    expect(preset.Unknown?.toString()).toBe('9007199254740993');
    expect(() => validatePresetArgs('previewLoadoutPreset', { ...args, extra: true })).toThrow('exactly');
    expect(() => validatePresetArgs('applyLoadoutPreset', { ...args, candidateKey: '../mixed', previewId })).toThrow('candidate');
    expect(() => validatePresetArgs('previewLoadoutPreset', { ...args, energyNguWeights: [1] })).toThrow('exactly');
    expect(() => validatePresetArgs('previewLoadoutPreset', { ...args, magicNguWeights: Array(7).fill(.1) })).toThrow('exactly');
  });
  it.each(['EnergyNguWeights', 'MagicNguWeights'])('strips retired preset %s from definitions and rejects it in inline IPC', key => {
    const source = { ...row(), [key]: ['invalid retired data'] };
    expect(validatePreset(source)).toBeUndefined();
    expect(normalizePreset(source)).toEqual(row());
    expect(presetFields).not.toContain(key);
    expect(() => validatePresetArgs('previewLoadoutPreset', { preset: source })).toThrow('global settings');
  });
  it('does not let raw preset-library edits resurrect overrides or affect configuration identity', async () => {
    const { ui, backend } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced'); await ui.previewLoadoutPreset();
    const before = api.cloneObject(ui.current().presetLibrary!), preview = ui.current().loadoutPresetPreview;
    ui.setPresetLibrary({ ...before, Presets: [{ ...row(), EnergyNguWeights: [5], MagicNguWeights: null }] });
    await ui.flushAutosave();
    expect(ui.current().presetLibrary).toEqual(before);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(ui.current().loadoutPresetPreview).toBe(preview);
    expect(backend.savePresetLibrary).not.toHaveBeenCalled(); ui.dispose();
  });
  it.each<JsonValue>(['', 'a'.repeat(31), 'a'.repeat(33), 'g'.repeat(32), ' ../token ', null, 123])('requires a valid preview token in Apply args and responses: %j', token => {
    const args = presetArgs(row());
    expect(() => validatePresetArgs('applyLoadoutPreset', { ...args, candidateKey: 'mixed', previewId: token })).toThrow('previewId');
    expect(() => readPresetPreview({ data: { result: { ...result(), previewId: token } } }, args)).toThrow('previewId');
  });
  it('rejects missing and extra token fields at the command boundary', () => {
    const args = presetArgs(row());
    expect(() => validatePresetArgs('applyLoadoutPreset', { ...args, candidateKey: 'mixed' })).toThrow('exactly');
    expect(() => validatePresetArgs('previewLoadoutPreset', { ...args, previewId })).toThrow('exactly');
    const preview = result(); delete preview.previewId;
    expect(() => readPresetPreview({ data: { result: preview } }, args)).toThrow('previewId');
    expect(validatePresetArgs('applyLoadoutPreset', { ...args, candidateKey: 'mixed', previewId })).toBeUndefined();
  });
});

describe('preset double readback', () => {
  it('acknowledges a saved fixed preset when backend readback omits zero dynamic weight', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    backend.savePresetLibrary.mockImplementationOnce(async (_dir, payload) => {
      const saved = api.cloneObject(payload);
      delete (saved.Presets as JsonObject[])[0].AdventureDynamicWeight;
      saved.__externalUiRevision = 'b'.repeat(64);
      backend.readPresetLibrary.mockResolvedValue(saved);
      return { success: true };
    });
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'Edited fixed preset', AdventureDynamicWeight: 0 }] });
    expect(await ui.savePresetLibrary()).toBe(true);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(ui.current().error).toBe('');
    expect(ui.current().presetLibrary).toEqual(ui.current().presetLibraryBase);
    expect(globalPresetRows(ui.current().presetLibrary)[0]).toMatchObject({ Name: 'Edited fixed preset', AdventureDynamicWeight: 0 });
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    ui.dispose();
  });
  it('acknowledges native double formatting after materializing default diggers', async () => {
    const { ui, backend } = setup();
    await ui.refresh();
    backend.savePresetLibrary.mockImplementationOnce(async (_dir, payload) => {
      const saved = api.cloneObject(payload);
      (saved.Presets as JsonObject[])[0].DiggerWeights = Array(12).fill(api.parseJson('0.33333333333333331'));
      saved.__externalUiRevision = 'b'.repeat(64);
      backend.readPresetLibrary.mockResolvedValue(saved);
      return { success: true };
    });
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [{ ...row(), DiggerWeights: Array(12).fill(1 / 3), Future: api.parseJson('9007199254740993') }] });
    expect(await ui.savePresetLibrary()).toBe(true);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(ui.current().error).toBe('');
    expect(ui.current().presetLibraryBase!.__externalUiRevision).toBe('b'.repeat(64));
    expect(String((ui.current().presetLibrary!.Presets as JsonObject[])[0].Future)).toBe('9007199254740993');
    ui.dispose();
  });
});

describe('snapshot comparisons', () => {
  it.each([false, true])('retains twelve-objective resolved preview compatibility, dynamic=%s', dynamic => {
    const preset = { ...row(), AdventureDynamicWeight: dynamic ? 1 : 0 };
    const args = globalArgs();
    const captured = { ...result(args), objectiveKeys: objectiveKeys.slice(0, 12), normalization: Array(12).fill(1), effectiveWeights: [0, 0, 0, .9, 0, 0, 0, 0, 0, 0, .1, 0], ...(dynamic ? { adventureTask: 'PP (ITOPOD)', adventureTaskKey: 'pp' } : {}) };
    const before = api.stringifyJson(captured);
    expect(readPresetPreview({ data: { result: captured } }, args, preset)).toBe(captured);
    expect(api.stringifyJson(captured)).toBe(before);
  });
  it.each([12, 13])('accepts a fourteen-objective cap preview with resolved weight at index %i', index => {
    const preset = { ...row(), Weights: Array.from({ length: 14 }, (_, i) => i === index ? 1 : 0) };
    const args = presetArgs(preset);
    const captured = { ...result(args), effectiveWeights: preset.Weights };
    expect(readPresetPreview({ data: { result: captured } }, args)).toBe(captured);
    for (const value of [-1, Infinity, '1', null]) expect(() => readPresetPreview({ data: { result: { ...captured, effectiveWeights: preset.Weights.map((weight, i) => i === index ? value : weight) } } }, args)).toThrow('resolved adventure weights');
  });
  it.each([false, true])('accepts backend fixed previews with effective weights but no task metadata, global=%s', global => {
    const preset = row(); delete preset.AdventureDynamicWeight;
    const args = global ? globalArgs() : presetArgs(preset);
    const captured = { ...result(args), effectiveWeights: preset.Weights };
    expect(captured).not.toHaveProperty('adventureTask');
    expect(captured).not.toHaveProperty('adventureTaskKey');
    expect(captured).not.toHaveProperty('preset');
    const before = api.stringifyJson(captured);
    expect(readPresetPreview({ data: { result: captured } }, args, global ? preset : undefined)).toBe(captured);
    expect(api.stringifyJson(captured)).toBe(before);
  });
  it.each<JsonValue>([null, [], Array(11).fill(1), Array(12).fill(1), Array(13).fill(1), Array(15).fill(1), Array(14).fill(0), [-1, ...Array(13).fill(0)], [Infinity, ...Array(13).fill(0)], ['1', ...Array(13).fill(0)]])('validates effective weights independently on fixed previews: %j', effectiveWeights => {
    expect(() => readPresetPreview({ data: { result: { ...result(globalArgs()), effectiveWeights } } }, globalArgs(), row())).toThrow('resolved adventure weights');
  });
  it.each<JsonObject>([
    { adventureTask: 'PP (ITOPOD)' }, { adventureTaskKey: 'pp' },
    { adventureTask: '', adventureTaskKey: 'pp' }, { adventureTask: 'PP (ITOPOD)', adventureTaskKey: null }
  ])('requires paired valid task metadata when either field is present on fixed previews: %j', metadata => {
    expect(() => readPresetPreview({ data: { result: { ...result(globalArgs()), effectiveWeights: row().Weights, ...metadata } } }, globalArgs(), row())).toThrow('resolved adventure task');
  });
  it('accepts and applies a backend-shaped fixed global preview without an embedded preset or task metadata', async () => {
    const { ui, backend } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const captured = { ...result(globalArgs()), effectiveWeights: row().Weights };
    backend.readCommandResult.mockResolvedValueOnce({ id: 'cmd', command: 'previewGlobalPreset', success: true, data: { result: captured } });
    await ui.previewLoadoutPreset();
    expect(ui.current().error).toBe('');
    expect(ui.current().loadoutPresetPreview?.result).toEqual(captured);
    ui.selectLoadoutPresetCandidate('mixed'); await ui.applyLoadoutPreset();
    expect(ui.current().error).toBe('');
    expect(backend.enqueueCommand).toHaveBeenLastCalledWith('A:/runtime', 'applyGlobalPreset', { ...globalArgs(), candidateKey: 'mixed', previewId });
    expect(backend.savePresetLibrary).not.toHaveBeenCalled();
    ui.dispose();
  });
  it('requires a complete resolved preview for a committed dynamic definition without resolving it locally', () => {
    const preset = { ...row(), AdventureDynamicWeight: 2, Weights: Array(14).fill(0) };
    const args = globalArgs();
    const resolved = { ...result(args), effectiveWeights: [0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], adventureTask: 'PP (ITOPOD)', adventureTaskKey: 'pp' };
    expect(readPresetPreview({ data: { result: resolved } }, args, preset)).toBe(resolved);
    expect(args).toEqual({ presetId: 'balanced', libraryRevision: 'a'.repeat(64) });
    expect(() => readPresetPreview({ data: { result: result(args) } }, args, preset)).toThrow('resolved adventure');
    expect(() => readPresetPreview({ data: { result: { ...result(args), effectiveWeights: resolved.effectiveWeights } } }, args, preset)).toThrow('resolved adventure task');
    for (const malformed of [
      { effectiveWeights: Array(11).fill(1) }, { effectiveWeights: Array(14).fill(0) },
      { effectiveWeights: [Infinity, ...Array(13).fill(0)] }, { effectiveWeights: ['1', ...Array(13).fill(0)] },
      { effectiveWeights: [-1, ...Array(13).fill(0)] }, { adventureTask: '' }, { adventureTask: null }, { adventureTaskKey: '' }, { adventureTaskKey: 3 }
    ]) expect(() => readPresetPreview({ data: { result: { ...resolved, ...malformed } } }, args, preset)).toThrow('resolved adventure');
  });
  it('uses resolved weights rather than saved fixed zeroes for applicability and shows captured tasks', async () => {
    const { ui, backend } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const resolved = { ...result(), effectiveWeights: [0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0], adventureTask: 'Zone farming', adventureTaskKey: 'zone' };
    backend.readCommandResult.mockResolvedValueOnce({ id: 'cmd', command: 'previewGlobalPreset', success: true, data: { result: resolved } });
    await ui.previewLoadoutPreset();
    const state = ui.current();
    const body = render(Presets, { props: props(state) }).body.replace(/<!--[\s\S]*?-->/g, '');
    expect(body).toContain('Resolved adventure task: Zone farming');
    expect(body).toMatch(/Zone farming<small[^>]*>unavailable: Locked zone<\/small><\/th><td[^>]*>Unknown/);
    ui.dispose();
  });
  it.each([6, 10, 12, 14])('accepts exact %i-objective inline captures without comparing a legacy preset horizon', count => {
    const args = presetArgs({ ...row(), HorizonSeconds: 17 });
    const captured = { ...result(args), horizonSeconds: 7200, objectiveKeys: objectiveKeys.slice(0, count), normalization: (result(args).normalization as number[]).slice(0, count) };
    expect(readPresetPreview({ data: { result: captured } }, args)).toBe(captured);
    expect(args.preset).not.toHaveProperty('HorizonSeconds');
  });
  it.each([10, 12, 14])('accepts exact %i-objective global captures with a runtime forecast', count => {
    const args = globalArgs();
    const captured = { ...result(args), horizonSeconds: 3600, objectiveKeys: objectiveKeys.slice(0, count), normalization: (result(args).normalization as number[]).slice(0, count) };
    expect(readPresetPreview({ data: { result: captured } }, args)).toBe(captured);
  });
  it.each([false, true])('rejects malformed objective contracts, global=%s', global => {
    const args = global ? globalArgs() : presetArgs(row());
    const captured = result(args);
    for (const count of [0, 7, 9, 11, 13]) expect(() => readPresetPreview({ data: { result: { ...captured, objectiveKeys: Array(count).fill('energyNgu'), normalization: Array(count).fill(1) } } }, args)).toThrow('contract');
    for (const count of global ? [10, 12, 14] : [6, 10, 12, 14]) {
      const keys = objectiveKeys.slice(0, count), normalization = Array(count).fill(1);
      for (const malformed of [
        { objectiveKeys: [...keys].reverse(), normalization },
        { objectiveKeys: keys.map((key, i) => i === count - 1 ? 'unknown' : key), normalization },
        { objectiveKeys: keys, normalization: Array(count - 1).fill(1) },
        { objectiveKeys: keys, normalization: Array(count + 1).fill(1) }
      ]) expect(() => readPresetPreview({ data: { result: { ...captured, ...malformed } } }, args)).toThrow('contract');
    }
    for (const horizonSeconds of [null, '3600', 0, -1, NaN, Infinity]) expect(() => readPresetPreview({ data: { result: { ...captured, horizonSeconds } } }, args)).toThrow('contract');
    const missingHorizon = { ...captured }; delete missingHorizon.horizonSeconds;
    expect(() => readPresetPreview({ data: { result: missingHorizon } }, args)).toThrow('contract');
    if (global) expect(() => readPresetPreview({ data: { result: { ...captured, objectiveKeys: objectiveKeys.slice(0, 6), normalization: Array(6).fill(1) } } }, args)).toThrow('contract');
  });
  it.each<JsonValue | undefined>([true, false, undefined, null, 'true'])('only warns of native Lazy ITOPOD floor control when explicitly active: %j', lazyItopodShifterActive => {
    const { ui } = setup();
    const args = presetArgs(row());
    const value = { ...result(), ...(lazyItopodShifterActive === undefined ? {} : { lazyItopodShifterActive }) };
    const before = api.stringifyJson(value);
    const state = { ...ui.current(), loadoutPresetPreview: { result: value, args, runtimeDir: 'A:/runtime', session: 'session-one', planName: 'test', editorVersion: 0 } };
    const body = render(Presets, { props: props(state) }).body;
    expect(body.includes('Lazy ITOPOD controls the floor')).toBe(lazyItopodShifterActive === true);
    if (lazyItopodShifterActive === true) expect(body).toContain('may change with your stats');
    expect(api.stringifyJson(value)).toBe(before);
    ui.dispose();
  });
  it('uses frozen production percentages above 100 without renormalizing from displayed specialists', () => {
    const metric = { key: 'energyNgu', quality: 'proxy', value: 500, specialistReferenceKey: 'frozen-reference', specialistProduction: 400, retainedProduction: 1.25, retainedProductionPercent: 125, weightedContribution: .625 };
    const before = api.stringifyJson(metric);
    expect(focusEfficiency(metric, [{ feasible: true, metrics: [{ key: 'energyNgu', value: 1000 }] }])).toBe('125%');
    expect(focusEfficiency(metric, [])).toBe('125%');
    expect(api.stringifyJson(metric)).toBe(before);
    expect(focusEfficiency({ ...metric, value: 0, retainedProduction: 0, retainedProductionPercent: 0 }, [])).toBe('0%');
    expect(focusEfficiency({ ...metric, retainedProductionPercent: new LosslessNumber('125.000000000000001') }, [])).toBe('125%');
  });
  it.each<JsonValue | undefined>([undefined, null, -1, NaN, Infinity])('does not invent a percentage for unavailable frozen production %j', specialistProduction => {
    const metric: JsonObject = { key: 'energyNgu', quality: 'proxy', value: 0, retainedProductionPercent: null, ...(specialistProduction === undefined ? {} : { specialistProduction }) };
    expect(focusEfficiency(metric, result().specialists as JsonObject[])).toBe('Unknown');
  });
  it('distinguishes zero denominators from unavailable production', () => {
    expect(focusEfficiency({ quality: 'proxy', value: 0, specialistProduction: 0, retainedProductionPercent: null }, [])).toBe('n/a');
    expect(focusEfficiency({ quality: 'proxy', value: 5, specialistProduction: new LosslessNumber('0'), retainedProductionPercent: null }, [])).toBe('n/a');
    expect(focusEfficiency({ quality: 'unavailable', value: null, specialistProduction: 0 }, [])).toBe('Unknown');
    expect(relativeProduction({ quality: 'proxy', value: 0, relativeToCurrent: null }, { quality: 'proxy', value: 0 })).toBe('n/a');
    expect(relativeProduction({ quality: 'proxy', value: new LosslessNumber('5'), relativeToCurrent: null }, { quality: 'proxy', value: new LosslessNumber('0') })).toBe('New');
    expect(relativeProduction({ quality: 'proxy', value: 0, relativeToCurrent: 0 }, { quality: 'proxy', value: 5 })).toBe('0x');
    expect(relativeProduction({ quality: 'proxy', value: 0, relativeToCurrent: null }, { quality: 'unavailable', value: null })).toBe('Unknown');
    expect(relativeProduction({ quality: 'unavailable', value: null }, { quality: 'proxy', value: 0 })).toBe('Unknown');
  });
  it('excludes infeasible legacy references and preserves explicit unknown percentages', () => {
    expect(focusEfficiency({ key: 'energyNgu', quality: 'proxy', value: 10 }, [{ feasible: false, metrics: [{ key: 'energyNgu', value: 20 }] }])).toBe('Unknown');
    expect(focusEfficiency({ key: 'energyNgu', quality: 'proxy', value: 10, specialistProduction: 20, retainedProductionPercent: null }, result().specialists as JsonObject[])).toBe('Unknown');
  });
  it('renders production percentages and floor zero without exposing scoring internals or mutating replay data', async () => {
    const { ui, backend } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const value: JsonObject = { ...result(), normalizedWeights: [.5, .5, 0, 0, 0, 0], scoring: 'Frozen production utility', productionReferences: [{ key: 'energyNgu', candidateKey: 'frozen-reference', production: 400 }, { key: 'blood', candidateKey: null, production: 0 }],
      candidates: [candidate('current'), { ...candidate(), itopodFloor: 0, metrics: [{ key: 'energyNgu', label: 'Energy NGU', quality: 'proxy', value: 500, unit: 'levels/s', specialistReferenceKey: 'frozen-reference', specialistProduction: 400, retainedProduction: 1.25, retainedProductionPercent: 125, weightedContribution: .625 }, { key: 'blood', label: 'Blood', quality: 'proxy', value: 0, specialistProduction: 0, retainedProductionPercent: null, weightedContribution: 0 }] }] };
    backend.readCommandResult.mockResolvedValueOnce({ id: 'cmd', command: 'previewGlobalPreset', success: true, data: { result: value } });
    await ui.previewLoadoutPreset();
    const state = ui.current(); const before = api.stringifyJson(state);
    const body = render(Presets, { props: props(state) }).body.replace(/<!--[\s\S]*?-->/g, '');
    for (const text of ['125%', '500', 'levels/s', 'ITOPOD floor: 0', '% of full focus']) expect(body).toContain(text);
    for (const text of ['frozen-reference', '0.625', 'Frozen production utility', 'Normalized goal weights', 'Production scoring', 'Current setup baseline']) expect(body).not.toContain(text);
    expect(api.stringifyJson(state)).toBe(before);
    ui.dispose();
  });
  it('keeps unknown metrics unknown and calculates full-focus efficiency only from available references', () => {
    expect(presetMetric(null)).toBe('Unknown'); expect(presetMetric(NaN)).toBe('Unknown'); expect(presetMetric(0)).toBe('0');
    expect(focusEfficiency({ key: 'energyNgu', value: 200, quality: 'proxy' }, result().specialists as JsonObject[])).toBe('50%');
    expect(focusEfficiency({ key: 'zone', value: null, quality: 'unavailable' }, result().specialists as JsonObject[])).toBe('Unknown');
    expect(focusEfficiency({ key: 'energyNgu', value: 200, quality: 'estimated' }, [])).toBe('Unknown');
    expect(focusEfficiency({ key: 'energyNgu', value: 200, quality: 'estimated' }, [{ metrics: [{ key: 'energyNgu', value: Infinity, quality: 'estimated' }] }])).toBe('Unknown');
  });
  it('rejects mismatched responses and ambiguous candidate keys', () => {
    const args = presetArgs(row());
    expect(() => readPresetPreview({ data: { result: { ...result(), presetId: 'different' } } }, args)).toThrow('match');
    expect(() => readPresetPreview({ data: { result: { ...result(), candidates: [candidate(), candidate()] } } }, args)).toThrow('ambiguous');
    expect(() => readPresetPreview({ data: { result: { ...result(), candidates: [candidate()] } } }, args)).toThrow('baseline');
  });
});

describe('preset library autosave', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('retains reviewed lane edits made while an unrelated library save is pending', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    const base = api.cloneObject(ui.current().presetLibrary!);
    let release!: () => void;
    const save = backend.savePresetLibrary.getMockImplementation()!;
    backend.savePresetLibrary.mockImplementationOnce(async (dir, payload) => {
      await new Promise<void>(resolve => { release = resolve; }); return save(dir, payload);
    });
    ui.setPresetLibrary({ ...base, Presets: [{ ...row(), Name: 'Pending name' }] });
    await vi.advanceTimersByTimeAsync(350);
    const newer = { ...row(), Name: 'Pending name', EnergyPriorities: ['TM', 'NGU'], MagicPriorities: [], ResourcePrioritiesReviewed: true };
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [newer] });
    release(); await vi.advanceTimersByTimeAsync(0);
    expect(globalPresetRows(ui.current().presetLibrary)).toEqual([newer]);
    expect(globalPresetRows(ui.current().presetLibraryBase)[0].ResourcePrioritiesReviewed).toBe(false);
    await vi.advanceTimersByTimeAsync(350);
    expect(globalPresetRows(ui.current().presetLibraryBase)).toEqual([newer]);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(2);
    expect(backend.enqueueCommand).not.toHaveBeenCalled(); ui.dispose();
  });

  it('does not replace an acknowledged autosave with an older pending reload response', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    const original = api.cloneObject(ui.current().presetLibrary!);
    let release!: (library: JsonObject) => void;
    backend.readPresetLibrary.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
    const reload = ui.loadPresetLibrary();
    ui.setPresetLibrary({ ...original, Presets: [{ ...row(), Name: 'Newer saved name' }] });
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(1);
    const saved = api.cloneObject(ui.current().presetLibraryBase!);
    release(original); await reload;
    expect(ui.current().presetLibrary).toEqual(saved);
    expect(ui.current().presetLibraryBase).toEqual(saved);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(ui.current().error).toContain('Library changed while reloading');
    expect(globalPresetRows(saved)[0].Name).toBe('Newer saved name');
    expect(backend.enqueueCommand).not.toHaveBeenCalled(); ui.dispose();
  });

  it.each([true, false])('debounces library edits without game actions or unrelated writes with AutoUpdate=%s', async AutoUpdate => {
    const { ui, backend, confirm } = setup(); await ui.refresh();
    const base = api.cloneObject(ui.current().presetLibraryBase!);
    ui.setSetting('_manageEnergy', false);
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 999 });
    const pendingPlan = api.cloneObject(ui.current().plan!);
    ui.setPresetLibrary({ ...base, Presets: [{ ...row(), Name: 'Intermediate', AutoUpdate }] });
    await vi.advanceTimersByTimeAsync(200);
    const final = { ...row(), Name: 'Final', AutoUpdate, Weights: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 70, 30, 0, 0] };
    ui.setPresetLibrary({ ...base, Presets: [final] });
    await vi.advanceTimersByTimeAsync(349);
    expect(backend.savePresetLibrary).not.toHaveBeenCalled();
    expect(backend.readPresetLibrary).toHaveBeenCalledTimes(1);
    expect(ui.current().presetLibraryBase).toEqual(base);
    expect(ui.current().presetLibraryDirty).toBe(true);
    await vi.advanceTimersByTimeAsync(1);
    expect(backend.savePresetLibrary).toHaveBeenCalledExactlyOnceWith('A:/runtime', { ...base, Presets: [final] });
    expect(backend.readPresetLibrary).toHaveBeenCalledTimes(3);
    expect(ui.current().presetLibrary).toEqual({ ...base, Presets: [final], __externalUiRevision: 'b'.repeat(64) });
    expect(ui.current().presetLibrary).toEqual(ui.current().presetLibraryBase);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(ui.current().saveError).toBe('');
    await vi.advanceTimersByTimeAsync(10000);
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(1);
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(confirm).not.toHaveBeenCalled();
    expect(ui.current().patch).toEqual({ _manageEnergy: false });
    expect(ui.current().plan).toEqual(pendingPlan);
    expect(ui.current().planDirty).toBe(true);
    expect(vi.getTimerCount()).toBe(0); ui.dispose();
  });

  it('does not write a draft reverted before the debounce expires', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    const base = api.cloneObject(ui.current().presetLibraryBase!);
    ui.setPresetLibrary({ ...base, Presets: [{ ...row(), Name: 'Temporary' }] });
    await vi.advanceTimersByTimeAsync(200);
    ui.setPresetLibrary(base);
    await vi.advanceTimersByTimeAsync(10000);
    expect(backend.savePresetLibrary).not.toHaveBeenCalled();
    expect(backend.readPresetLibrary).toHaveBeenCalledTimes(1);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(vi.getTimerCount()).toBe(0); ui.dispose();
  });

  it.each(['', '-', '1e'])('retains invalid numeric draft %j without retrying until corrected', async SwapSeconds => {
    const { ui, backend } = setup(); await ui.refresh();
    const base = api.cloneObject(ui.current().presetLibraryBase!);
    const invalid = { ...base, Presets: [{ ...row(), Name: 'Keep this edit', SwapSeconds }] };
    ui.setPresetLibrary(invalid);
    await vi.advanceTimersByTimeAsync(350);
    expect(ui.current().presetLibrary).toEqual(invalid);
    expect(ui.current().presetLibraryBase).toEqual(base);
    expect(ui.current().presetLibraryDirty).toBe(true);
    expect(ui.current().saveError).toContain('Swap seconds');
    await vi.advanceTimersByTimeAsync(10000);
    expect(backend.savePresetLibrary).not.toHaveBeenCalled();
    expect(backend.readPresetLibrary).toHaveBeenCalledTimes(1);
    expect(vi.getTimerCount()).toBe(0);
    const corrected = { ...row(), Name: 'Keep this edit', SwapSeconds: 7 };
    ui.setPresetLibrary({ ...invalid, Presets: [corrected] });
    await vi.advanceTimersByTimeAsync(349);
    expect(backend.savePresetLibrary).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    expect(backend.savePresetLibrary).toHaveBeenCalledExactlyOnceWith('A:/runtime', { ...base, Presets: [corrected] });
    expect(globalPresetRows(ui.current().presetLibraryBase)).toEqual([corrected]);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(ui.current().saveError).toBe('');
    expect(backend.enqueueCommand).not.toHaveBeenCalled(); ui.dispose();
  });

  it('queues library edits when the debounce fires during another busy action', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    let release!: (value: JsonObject) => void;
    backend.readPlan.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
    const loading = ui.loadPlan('other');
    expect(ui.current().busy).toBe(true);
    const draft = { ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'Queued' }] };
    ui.setPresetLibrary(draft);
    await vi.advanceTimersByTimeAsync(1000);
    expect(backend.savePresetLibrary).not.toHaveBeenCalled();
    expect(ui.current().presetLibrary).toEqual(draft);
    release(plan()); await loading;
    await vi.advanceTimersByTimeAsync(349);
    expect(backend.savePresetLibrary).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    expect(backend.savePresetLibrary).toHaveBeenCalledExactlyOnceWith('A:/runtime', draft);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(ui.current().planName).toBe('other');
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(vi.getTimerCount()).toBe(0); ui.dispose();
  });

  it('saves newer field edits, additions and removals after an in-flight library autosave', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    const first = { ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'First' }, { ...row(), Id: 'remove' }] };
    let release!: () => void;
    const save = backend.savePresetLibrary.getMockImplementation()!;
    backend.savePresetLibrary.mockImplementationOnce(async (dir, payload) => {
      await new Promise<void>(resolve => { release = resolve; }); return save(dir, payload);
    });
    ui.setPresetLibrary(first);
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.savePresetLibrary).toHaveBeenCalledExactlyOnceWith('A:/runtime', first);
    expect(ui.current().busy).toBe(true);
    const newer = [{ ...row(), Name: 'Newer', AutoUpdate: false }, { ...row(), Id: 'added' }];
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: newer });
    await vi.advanceTimersByTimeAsync(1000);
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(1);
    release(); await vi.advanceTimersByTimeAsync(0);
    expect(globalPresetRows(ui.current().presetLibraryBase)).toEqual(first.Presets);
    expect(globalPresetRows(ui.current().presetLibrary)).toEqual(newer);
    expect(ui.current().presetLibraryDirty).toBe(true);
    await vi.advanceTimersByTimeAsync(349);
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(2);
    expect(backend.savePresetLibrary).toHaveBeenLastCalledWith('A:/runtime', { ...first, Presets: newer, __externalUiRevision: 'b'.repeat(64) });
    expect(globalPresetRows(ui.current().presetLibraryBase)).toEqual(newer);
    expect(ui.current().presetLibrary).toEqual(ui.current().presetLibraryBase);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(ui.current().saveError).toBe('');
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(vi.getTimerCount()).toBe(0); ui.dispose();
  });

  it('autosaves a reversion to the old baseline made during an in-flight library write', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    const original = api.cloneObject(ui.current().presetLibrary!);
    let release!: () => void;
    const save = backend.savePresetLibrary.getMockImplementation()!;
    backend.savePresetLibrary.mockImplementationOnce(async (dir, payload) => {
      await new Promise<void>(resolve => { release = resolve; }); return save(dir, payload);
    });
    ui.setPresetLibrary({ ...original, Presets: [{ ...row(), Name: 'In flight' }] });
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(1);
    ui.setPresetLibrary(original);
    await vi.advanceTimersByTimeAsync(350);
    release(); await vi.advanceTimersByTimeAsync(0);
    expect(globalPresetRows(ui.current().presetLibraryBase)[0].Name).toBe('In flight');
    expect(globalPresetRows(ui.current().presetLibrary)).toEqual(original.Presets);
    expect(ui.current().presetLibraryDirty).toBe(true);
    await vi.advanceTimersByTimeAsync(1000);
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(2);
    expect(backend.savePresetLibrary).toHaveBeenLastCalledWith('A:/runtime', { ...original, __externalUiRevision: 'b'.repeat(64) });
    expect(globalPresetRows(ui.current().presetLibraryBase)).toEqual(original.Presets);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(vi.getTimerCount()).toBe(0); ui.dispose();
  });

  it.each(['transport', 'acknowledgement', 'readback', 'conflict'] as const)('retains drafts after a %s failure without an automatic retry loop', async failure => {
    const { ui, backend } = setup(); await ui.refresh();
    const base = api.cloneObject(ui.current().presetLibraryBase!);
    const draft = { ...base, Presets: [{ ...row(), Name: 'Retain me' }] };
    const errors = { transport: 'Disk full', acknowledgement: 'did not report success', readback: 'readback did not acknowledge', conflict: 'Source changed' };
    if (failure === 'transport') backend.savePresetLibrary.mockRejectedValueOnce(new Error('Disk full'));
    if (failure === 'acknowledgement') backend.savePresetLibrary.mockResolvedValueOnce({ success: false });
    if (failure === 'readback') backend.savePresetLibrary.mockResolvedValueOnce({ success: true });
    if (failure === 'conflict') backend.readPresetLibrary.mockResolvedValueOnce({ ...base, __externalUiRevision: 'c'.repeat(64), Presets: [{ ...row(), Name: 'Other writer' }] });
    ui.setPresetLibrary(draft);
    await vi.advanceTimersByTimeAsync(350);
    expect(ui.current().saveError).toContain(errors[failure]);
    expect(ui.current().error).toContain(errors[failure]);
    expect(ui.current().presetLibrary).toEqual(draft);
    expect(ui.current().presetLibraryBase).toEqual(base);
    expect(ui.current().presetLibraryDirty).toBe(true);
    const reads = backend.readPresetLibrary.mock.calls.length;
    await vi.advanceTimersByTimeAsync(10000);
    ui.tick(); await ui.refresh();
    await vi.advanceTimersByTimeAsync(10000);
    expect(backend.readPresetLibrary).toHaveBeenCalledTimes(reads);
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(failure === 'conflict' ? 0 : 1);
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(vi.getTimerCount()).toBe(0); ui.dispose();
  });

  it.each(['transport', 'acknowledgement', 'readback', 'readback I/O'] as const)('retains queued library edits after a %s failure until explicit retry', async failure => {
    const { ui, backend } = setup(); await ui.refresh();
    try {
      const base = api.cloneObject(ui.current().presetLibraryBase!);
      const first = { ...base, Presets: [{ ...globalPresetRows(base)[0], Name: 'In flight' }] };
      const latest = { ...first, Presets: [{ ...globalPresetRows(first)[0], AutoUpdate: false }] };
      const errors = { transport: 'Write transport lost', acknowledgement: 'did not report success', readback: 'readback did not acknowledge', 'readback I/O': 'Library readback failed' };
      let release!: () => void;
      const pending = new Promise<void>(resolve => { release = resolve; });
      const save = backend.savePresetLibrary.getMockImplementation()!;
      backend.savePresetLibrary.mockImplementationOnce(async (dir, payload) => {
        if (failure === 'transport' || failure === 'acknowledgement') {
          await pending;
          if (failure === 'transport') throw new Error(errors.transport);
          return { success: false };
        }
        const acknowledgement = await save(dir, payload);
        backend.readPresetLibrary.mockImplementationOnce(async () => {
          await pending;
          if (failure === 'readback I/O') throw new Error(errors['readback I/O']);
          return api.cloneObject(base);
        });
        return acknowledgement;
      });
      ui.setPresetLibrary(first);
      await vi.advanceTimersByTimeAsync(350);
      expect(backend.savePresetLibrary).toHaveBeenCalledExactlyOnceWith('A:/runtime', first);
      expect(ui.current().busy).toBe(true);
      ui.setPresetLibrary(latest);
      await vi.advanceTimersByTimeAsync(350);
      release(); await vi.advanceTimersByTimeAsync(0);
      expect(ui.current().saveError).toContain(errors[failure]);
      expect(ui.current().error).toContain(errors[failure]);
      expect(ui.current().presetLibrary).toEqual(latest);
      expect(ui.current().presetLibraryBase).toEqual(base);
      expect(ui.current().presetLibraryDirty).toBe(true);
      const reads = backend.readPresetLibrary.mock.calls.length;
      await vi.advanceTimersByTimeAsync(10000);
      expect(backend.savePresetLibrary).toHaveBeenCalledTimes(1);
      expect(backend.readPresetLibrary).toHaveBeenCalledTimes(reads);
      expect(ui.current().presetLibrary).toEqual(latest);
      expect(ui.current().saveError).toContain(errors[failure]);
      await ui.retryAutosave();
      expect(backend.savePresetLibrary).toHaveBeenCalledTimes(2);
      expect(backend.savePresetLibrary).toHaveBeenLastCalledWith('A:/runtime', {
        ...latest, __externalUiRevision: (failure.startsWith('readback') ? 'b' : 'a').repeat(64)
      });
      expect(globalPresetRows(ui.current().presetLibraryBase)).toEqual(latest.Presets);
      expect(ui.current().presetLibrary).toEqual(ui.current().presetLibraryBase);
      expect(ui.current().presetLibraryDirty).toBe(false);
      expect(ui.current().saveError).toBe('');
      expect(ui.current().error).toBe('');
      expect(backend.enqueueCommand).not.toHaveBeenCalled();
      expect(backend.savePlan).not.toHaveBeenCalled();
      expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    } finally { ui.dispose(); }
  });

  it('retries a failed library autosave explicitly and clears its save error', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    const draft = { ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'Retry me' }] };
    backend.savePresetLibrary.mockRejectedValueOnce(new Error('Disk full'));
    ui.setPresetLibrary(draft);
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(1);
    expect(ui.current().saveError).toBe('Disk full');
    await ui.retryAutosave();
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(2);
    expect(backend.savePresetLibrary).toHaveBeenLastCalledWith('A:/runtime', draft);
    expect(globalPresetRows(ui.current().presetLibraryBase)).toEqual(draft.Presets);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(ui.current().saveError).toBe('');
    expect(ui.current().error).toBe('');
    await vi.advanceTimersByTimeAsync(10000);
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(2);
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(vi.getTimerCount()).toBe(0); ui.dispose();
  });

  it('saves the independent library while an invalid advanced JSON buffer blocks plan and settings autosave', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    ui.advancedDirty(true);
    ui.error(new Error('Invalid advanced JSON'));
    ui.editSetting('_manageEnergy', false, 350);
    ui.editPlan({ ...ui.current().plan!, StageDurationSeconds: 999 });
    const pendingPlan = api.cloneObject(ui.current().plan!);
    const draft = { ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'Independent' }] };
    ui.setPresetLibrary(draft);
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.savePresetLibrary).toHaveBeenCalledExactlyOnceWith('A:/runtime', draft);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(ui.current().advancedDirty).toBe(true);
    expect(ui.current().patch).toEqual({ _manageEnergy: false });
    expect(ui.current().plan).toEqual(pendingPlan);
    expect(ui.current().planDirty).toBe(true);
    expect(ui.current().saveError).toBe('');
    await vi.advanceTimersByTimeAsync(10000);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(1);
    expect(vi.getTimerCount()).toBe(0); ui.dispose();
  });

  it('flushes a valid library before switching folders without a discard prompt', async () => {
    const { ui, backend, snapshot, confirm } = setup(); await ui.refresh();
    const draft = { ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'Save in original folder' }] };
    ui.setPresetLibrary(draft);
    backend.getState.mockImplementation(async () => {
      expect(backend.savePresetLibrary).toHaveBeenCalledExactlyOnceWith('A:/runtime', draft);
      return { ...snapshot, runtime_dir: 'A:/other', runtime: {}, protocol: {}, plans: [] };
    });
    await ui.changeRuntimeDir('A:/other');
    expect(ui.current().runtimeDir).toBe('A:/other');
    expect(ui.current().presetLibrary).toBeNull();
    expect(ui.current().presetLibraryBase).toBeNull();
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(ui.current().presetLibraryLoading).toBe(false);
    expect(ui.current().saveError).toBe('');
    expect(confirm).not.toHaveBeenCalled();
    expect(vi.getTimerCount()).toBe(0);
    await vi.advanceTimersByTimeAsync(10000); await ui.retryAutosave();
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(1);
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled(); ui.dispose();
  });

  it('clears failed library drafts and errors only after an explicitly confirmed folder switch', async () => {
    const { ui, backend, snapshot, confirm } = setup(); await ui.refresh();
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [{ ...row(), SwapSeconds: '-' }] });
    await vi.advanceTimersByTimeAsync(350);
    expect(ui.current().saveError).toContain('Swap seconds');
    backend.getState.mockResolvedValue({ ...snapshot, runtime_dir: 'A:/other', runtime: {}, protocol: {}, plans: [] });
    await ui.changeRuntimeDir('A:/other');
    expect(confirm).toHaveBeenCalledExactlyOnceWith('Discard unsaved changes and switch data folder?');
    expect(ui.current().runtimeDir).toBe('A:/other');
    expect(ui.current().presetLibrary).toBeNull();
    expect(ui.current().presetLibraryBase).toBeNull();
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(ui.current().saveError).toBe('');
    expect(vi.getTimerCount()).toBe(0);
    await ui.retryAutosave(); await vi.advanceTimersByTimeAsync(10000);
    expect(backend.savePresetLibrary).not.toHaveBeenCalled();
    expect(vi.getTimerCount()).toBe(0); ui.dispose();
  });

  it('cancels the pending library debounce on dispose', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'Pending' }] });
    expect(vi.getTimerCount()).toBe(1);
    ui.dispose();
    expect(vi.getTimerCount()).toBe(0);
    await vi.advanceTimersByTimeAsync(10000);
    expect(backend.savePresetLibrary).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
  });
});

describe('preset controller safety', () => {
  it.each(['previewGlobalPreset', 'applyGlobalPreset', 'equipGlobalPreset', 'previewLoadoutPreset', 'applyLoadoutPreset'])('blocks dynamic %s unless runtime capability is exactly 1', async command => {
    const { ui, backend, snapshot } = setup();
    const preset = { ...row(), AdventureDynamicWeight: 1, Weights: Array(14).fill(0) };
    backend.readPresetLibrary.mockResolvedValue({ ...library(), Presets: [preset] });
    for (const version of [undefined, 0, 2, '1', true]) {
      if (version === undefined) delete snapshot.runtime.adventureDynamicPresetsVersion; else snapshot.runtime.adventureDynamicPresetsVersion = version;
      await ui.refresh();
      const args = command.includes('Global') ? globalArgs() : presetArgs(preset);
      await ui.runCommand(command, command.startsWith('apply') ? { ...args, candidateKey: 'mixed', previewId } : args);
      expect(ui.current().error).toContain('adventureDynamicPresetsVersion');
      expect(backend.enqueueCommand).not.toHaveBeenCalled();
    }
    ui.dispose();
  });
  it('previews a supported dynamic preset by committed ID/revision and blocks apply after capability withdrawal', async () => {
    const { ui, backend, snapshot } = setup();
    const preset = { ...row(), AdventureDynamicWeight: 1, Weights: Array(14).fill(0) };
    backend.readPresetLibrary.mockResolvedValue({ ...library(), Presets: [preset] });
    snapshot.runtime.adventureDynamicPresetsVersion = 1;
    backend.readCommandResult.mockResolvedValue({ id: 'cmd', command: 'previewGlobalPreset', success: true, data: { result: { ...result(globalArgs()), effectiveWeights: [0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], adventureTask: 'PP (ITOPOD)', adventureTaskKey: 'pp' } } });
    await ui.refresh(); ui.selectLoadoutPreset('balanced'); await ui.previewLoadoutPreset();
    expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'previewGlobalPreset', globalArgs());
    expect(ui.current().loadoutPresetPreview?.preset).toEqual(preset);
    ui.selectLoadoutPresetCandidate('mixed');
    snapshot.runtime.adventureDynamicPresetsVersion = 0; await ui.refresh(); await ui.applyLoadoutPreset();
    expect(ui.current().error).toContain('adventureDynamicPresetsVersion');
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(1);
    expect(backend.savePresetLibrary).not.toHaveBeenCalled();
    ui.dispose();
  });
  it('does not rewrite an unchanged 14-objective library or spend its preview when Save is called directly', async () => {
    const { ui, backend } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    await ui.previewLoadoutPreset(); ui.selectLoadoutPresetCandidate('mixed');
    const preview = ui.current().loadoutPresetPreview;
    const reads = backend.readPresetLibrary.mock.calls.length;
    expect(globalPresetRows(ui.current().presetLibraryBase)[0].Weights).toHaveLength(14);
    expect(await ui.savePresetLibrary()).toBe(true);
    expect(await ui.savePresetLibrary()).toBe(true);
    ui.tick(); await ui.refresh(); await ui.flushAutosave();
    expect(backend.readPresetLibrary).toHaveBeenCalledTimes(reads);
    expect(backend.savePresetLibrary).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'previewGlobalPreset', globalArgs());
    expect(backend.savePlan).not.toHaveBeenCalled(); expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(ui.current().loadoutPresetPreview).toBe(preview);
    expect(ui.current().loadoutPresetCandidateKey).toBe('mixed');
    expect(ui.current().presetLibraryBase?.__externalUiRevision).toBe('a'.repeat(64)); ui.dispose();
  });
  it.each([true, false])('saving direct adventure weights with AutoUpdate=%s never previews, optimizes, or applies in the background', async AutoUpdate => {
    const { ui, backend } = setup(); await ui.refresh();
    const definition = { ...row(), AutoUpdate, Weights: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 70, 30, 0, 0], AdvancedTrainingWeights: [0, 0, 0, 0, 0] };
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [definition] });
    expect(await ui.savePresetLibrary()).toBe(true);
    expect(globalPresetRows(ui.current().presetLibraryBase)[0]).toEqual(definition);
    expect(globalPresetRows(ui.current().presetLibraryBase)[0]).not.toHaveProperty('HorizonSeconds');
    ui.tick(); await ui.refresh(); await ui.loadPresetLibrary(); await ui.flushAutosave();
    expect(await ui.savePresetLibrary()).toBe(true);
    expect(backend.savePresetLibrary).toHaveBeenCalledTimes(1);
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled(); expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    ui.dispose();
  });
  it.each([6, 10, 12])('sends canonical 14-objective definitions when saving a legacy %i-objective draft', async count => {
    const { ui, backend } = setup(); await ui.refresh();
    const definition = { ...row(), Weights: Array(count).fill(1), AutoUpdate: false, HorizonSeconds: 'retired legacy value', Future: api.parseJson('9007199254740993') };
    const before = api.stringifyJson(definition);
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [definition] });
    expect(await ui.savePresetLibrary()).toBe(true);
    const sent = globalPresetRows(backend.savePresetLibrary.mock.calls[0][1])[0];
    expect(sent.Weights).toEqual([...Array(count).fill(1), ...Array(14 - count).fill(0)]);
    expect(sent.AutoUpdate).toBe(false);
    expect(sent).not.toHaveProperty('HorizonSeconds');
    expect(String(sent.Future)).toBe('9007199254740993');
    expect(globalPresetRows(ui.current().presetLibrary)[0]).toEqual(sent);
    expect(ui.current().presetLibraryDirty).toBe(false);
    expect(api.stringifyJson(definition)).toBe(before);
    expect(backend.enqueueCommand).not.toHaveBeenCalled(); expect(backend.savePlan).not.toHaveBeenCalled(); ui.dispose();
  });
  it('loads the native global library offline without requiring a plan or enabling game actions', async () => {
    const { ui, backend, snapshot } = setup();
    snapshot.connected = false; snapshot.runtime = {}; snapshot.protocol.globalPresetsVersion = 2; snapshot.plans = [];
    await ui.refresh();
    expect(ui.current().presetLibrary).toEqual(library());
    expect(ui.current().plan).toBeNull(); expect(backend.readPlan).not.toHaveBeenCalled();
    ui.selectLoadoutPreset('balanced'); await ui.equipGlobalPreset('balanced');
    expect(backend.enqueueCommand).not.toHaveBeenCalled(); ui.dispose();
  });
  it('keeps the shared library and unrelated preview valid across plan switches and plan ratio edits', async () => {
    const { ui, backend } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    await ui.previewLoadoutPreset(); ui.selectLoadoutPresetCandidate('mixed');
    const shared = ui.current().presetLibrary, preview = ui.current().loadoutPresetPreview;
    await ui.loadPlan('other');
    ui.setPlan({ ...ui.current().plan!, EnergyNguWeights: Array(9).fill(7) });
    expect(ui.current().presetLibrary).toBe(shared);
    expect(ui.current().loadoutPresetPreview).toBe(preview);
    expect(ui.current().loadoutPresetSelectedId).toBe('balanced');
    await ui.applyLoadoutPreset();
    expect(ui.current().error).toBe('');
    expect(backend.enqueueCommand).toHaveBeenLastCalledWith('A:/runtime', 'applyGlobalPreset', { ...globalArgs(), previewId, candidateKey: 'mixed' });
    expect(backend.savePlan).not.toHaveBeenCalled(); ui.dispose();
  });
  it('retains unsaved library edits across plan switches without a discard prompt or any game action', async () => {
    const { ui, backend, confirm } = setup(); await ui.refresh();
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'Unfinished', AutoUpdate: false }] });
    await ui.loadPlan('other');
    expect(globalPresetRows(ui.current().presetLibrary)[0]).toMatchObject({ Name: 'Unfinished', AutoUpdate: false });
    expect(ui.current().presetLibraryDirty).toBe(true);
    expect(confirm).not.toHaveBeenCalled(); expect(backend.enqueueCommand).not.toHaveBeenCalled(); ui.dispose();
  });
  it('saves strict false without applying, then equips only the committed ID and revision', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [{ ...row(), AutoUpdate: false }] });
    await ui.equipGlobalPreset('balanced');
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(await ui.savePresetLibrary()).toBe(true);
    expect(globalPresetRows(ui.current().presetLibraryBase)[0].AutoUpdate).toBe(false);
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    await ui.equipGlobalPreset('balanced');
    expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'equipGlobalPreset', { presetId: 'balanced', libraryRevision: 'b'.repeat(64) });
    expect(backend.savePlan).not.toHaveBeenCalled(); ui.dispose();
  });
  it('surfaces an unseeded cached Equip rejection without silently previewing or optimizing', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [{ ...row(), AutoUpdate: false }] });
    expect(await ui.savePresetLibrary()).toBe(true);
    backend.readCommandResult.mockResolvedValueOnce({ id: 'cmd', command: 'equipGlobalPreset', success: false, message: 'No reusable policy. Use Preview + Apply first.' });
    await ui.equipGlobalPreset('balanced');
    expect(ui.current().error).toContain('Use Preview + Apply first.');
    expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'equipGlobalPreset', { presetId: 'balanced', libraryRevision: 'b'.repeat(64) });
    expect(ui.current().loadoutPresetPreview).toBeNull();
    expect(globalPresetRows(ui.current().presetLibraryBase)[0].AutoUpdate).toBe(false); ui.dispose();
  });
  it('retains newer invalid field edits, additions and removals while a library save is pending', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [row(), { ...row(), Id: 'remove' }] });
    let release!: () => void;
    const save = backend.savePresetLibrary.getMockImplementation()!;
    backend.savePresetLibrary.mockImplementationOnce(async (dir, payload) => { await new Promise<void>(resolve => { release = resolve; }); return save(dir, payload); });
    const pending = ui.savePresetLibrary();
    await vi.waitFor(() => expect(backend.savePresetLibrary).toHaveBeenCalledOnce());
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'Edited while waiting', SwapSeconds: '-', AutoUpdate: false }, { ...row(), Id: 'new' }] });
    release(); expect(await pending).toBe(true);
    expect(globalPresetRows(ui.current().presetLibrary)).toEqual([{ ...row(), Name: 'Edited while waiting', SwapSeconds: '-', AutoUpdate: false }, { ...row(), Id: 'new' }]);
    expect(globalPresetRows(ui.current().presetLibraryBase).map(row => row.Id)).toEqual(['balanced', 'remove']);
    expect(ui.current().presetLibraryDirty).toBe(true); expect(backend.enqueueCommand).not.toHaveBeenCalled(); ui.dispose();
  });
  it('retains the baseline and local draft on a CAS conflict without writing', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'Local' }] });
    backend.readPresetLibrary.mockResolvedValueOnce({ ...library(), __externalUiRevision: 'c'.repeat(64), Presets: [{ ...row(), Name: 'Other writer' }] });
    expect(await ui.savePresetLibrary()).toBe(false);
    expect(backend.savePresetLibrary).not.toHaveBeenCalled();
    expect(globalPresetRows(ui.current().presetLibrary)[0].Name).toBe('Local');
    expect(ui.current().presetLibraryBase?.__externalUiRevision).toBe('a'.repeat(64));
    expect(ui.current().error).toContain('Source changed'); ui.dispose();
  });
  it('retains drafts on a failed save acknowledgement or mismatched readback', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'Local' }] });
    backend.savePresetLibrary.mockResolvedValueOnce({ success: false });
    expect(await ui.savePresetLibrary()).toBe(false);
    expect(ui.current().presetLibraryDirty).toBe(true);
    backend.savePresetLibrary.mockResolvedValueOnce({ success: true });
    expect(await ui.savePresetLibrary()).toBe(false);
    expect(ui.current().error).toContain('readback');
    expect(globalPresetRows(ui.current().presetLibrary)[0].Name).toBe('Local'); ui.dispose();
  });
  it('warns before abandoning an invalid library on runtime folder navigation', async () => {
    const { ui, backend, confirm } = setup(); await ui.refresh();
    const draft = { ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'Local', SwapSeconds: '-' }] };
    const base = api.cloneObject(ui.current().presetLibraryBase!);
    ui.setPresetLibrary(draft);
    confirm.mockResolvedValue(false);
    const reads = backend.getState.mock.calls.length;
    await ui.changeRuntimeDir('A:/other');
    expect(confirm).toHaveBeenCalledWith('Discard unsaved changes and switch data folder?');
    expect(backend.getState.mock.calls).toHaveLength(reads);
    expect(backend.savePresetLibrary).not.toHaveBeenCalled();
    expect(ui.current().presetLibrary).toEqual(draft);
    expect(ui.current().presetLibraryBase).toEqual(base);
    expect(ui.current().runtimeDir).toBe('A:/runtime'); expect(ui.current().presetLibraryDirty).toBe(true); ui.dispose();
  });
  it.each(['missingCapability', 'wrongCapability', 'oldCapability', 'missingGlobalWeights', 'stale', 'unsupported', 'session', 'folder'])('blocks global actions with %s', async reason => {
    const { ui, backend, snapshot } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    if (reason === 'missingCapability') delete snapshot.runtime.globalPresetsVersion;
    if (reason === 'wrongCapability') snapshot.runtime.globalPresetsVersion = '1';
    if (reason === 'oldCapability') snapshot.runtime.globalPresetsVersion = 1;
    if (reason === 'missingGlobalWeights') delete snapshot.runtime.globalNguWeightsVersion;
    const clock = reason === 'stale' ? vi.spyOn(Date, 'now').mockReturnValue(Date.now() + 30000) : null;
    if (reason === 'unsupported') snapshot.runtime.supportedCommands = ['readPlan'];
    if (reason === 'session') snapshot.runtime.startedAt = '';
    if (reason === 'folder') snapshot.runtime_dir = 'A:/wrong';
    await ui.refresh(); await ui.previewLoadoutPreset(); await ui.equipGlobalPreset('balanced');
    clock?.mockRestore();
    expect(backend.enqueueCommand).not.toHaveBeenCalled(); expect(ui.current().error).not.toBe(''); ui.dispose();
  });
  it.each(['previewGlobalPreset', 'previewCombatPrep'])('accepts a bounded %s payload with runtime/envelope bytes beyond 64 KiB', async command => {
    const { ui, backend, snapshot } = setup();
    snapshot.protocol = { maxResultBytes: 256 * 1024, combatPrep: { maxPreviewBytes: 256 * 1024 } };
    (snapshot.runtime.supportedCommands as string[]).push('previewCombatPrep');
    await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const field = command === 'previewGlobalPreset' ? 'result' : 'combatPrepPreview';
    const preview: JsonObject = command === 'previewGlobalPreset' ? { ...result(), padding: '' } : { schemaVersion: 1, previewId, generatedAt: new Date().toISOString(), durationSeconds: 7200, backgroundEnergyPercent: 3, backgroundMagicPercent: 3, recommendedCandidateId: 'full', candidates: [{ id: 'full', available: true }], padding: '' };
    const byteLength = (value: JsonObject) => new TextEncoder().encode(api.stringifyJson(value)).length;
    const payloadBytes = command === 'previewGlobalPreset' ? 63 * 1024 : 60 * 1024 - 1;
    preview.padding = 'p'.repeat(payloadBytes - byteLength(preview));
    const response: JsonObject = { id: 'cmd', command, success: true, data: { [field]: preview, runtime: { startedAt: 'session-one', context: 'r'.repeat(64 * 1024) } } };
    expect(byteLength(preview)).toBe(payloadBytes);
    expect(byteLength(response)).toBeGreaterThan(64 * 1024);
    expect(byteLength(response)).toBeLessThan(256 * 1024);
    backend.readCommandResult.mockResolvedValueOnce(response);
    if (command === 'previewGlobalPreset') await ui.previewLoadoutPreset(); else await ui.previewCombatPrep();
    expect(ui.current().error).toBe('');
    expect(command === 'previewGlobalPreset' ? ui.current().loadoutPresetPreview?.result : ui.current().combatPrepPreview).toEqual(preview);
    expect(backend.savePlan).not.toHaveBeenCalled(); expect(backend.saveSettingsPatch).not.toHaveBeenCalled(); ui.dispose();
  });
  it('still rejects an oversized combat-prep response from an older native advertising 64 KiB', async () => {
    const { ui, backend, snapshot } = setup();
    snapshot.protocol = { maxResultBytes: 256 * 1024, combatPrep: { maxPreviewBytes: 64 * 1024 } };
    (snapshot.runtime.supportedCommands as string[]).push('previewCombatPrep'); await ui.refresh();
    backend.readCommandResult.mockResolvedValueOnce({ id: 'cmd', command: 'previewCombatPrep', success: true, data: { combatPrepPreview: { padding: 'p'.repeat(64 * 1024) } } });
    await ui.previewCombatPrep();
    expect(ui.current().error).toBe('Combat prep response exceeds 64 KB.'); expect(ui.current().combatPrepPreview).toBeNull(); ui.dispose();
  });
  it('browses and previews without any settings/plan writes or unrelated draft flush', async () => {
    const { ui, backend, confirm } = setup(); await ui.refresh();
    ui.selectLoadoutPreset('balanced'); ui.setSetting('_manageEnergy', false);
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 999 });
    await ui.previewLoadoutPreset();
    expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'previewGlobalPreset', globalArgs());
    expect(backend.savePlan).not.toHaveBeenCalled(); expect(backend.saveSettingsPatch).not.toHaveBeenCalled(); expect(confirm).not.toHaveBeenCalled();
    expect(ui.current().patch).toEqual({ _manageEnergy: false }); expect(ui.current().plan?.StageDurationSeconds).toBe(999);
    await ui.runCommand('status');
    expect(ui.current().loadoutPresetPreview?.result.presetId).toBe('balanced');
    ui.dispose();
  });
  it('saves only the library through revision-aware merging while retaining unrelated edits and unknowns', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    const rows = globalPresetRows(ui.current().presetLibrary).map(row => ({ ...row, Name: 'Edited', Future: api.parseJson('9007199254740993') }));
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 999 });
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: rows });
    expect(await ui.savePresetLibrary()).toBe(true);
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(backend.savePresetLibrary.mock.calls[0][1].__externalUiRevision).toBe('a'.repeat(64));
    expect(ui.current().plan?.StageDurationSeconds).toBe(999);
    expect(api.stringifyJson(ui.current().presetLibrary?.Presets)).toContain('9007199254740993');
    expect(ui.current().plan?.GearGoal).toBe(2); expect(ui.current().plan?.ActiveGoalPresetIndex).toBe(0);
    ui.dispose();
  });
  it.each(['definition', 'ratios', 'selection', 'session', 'folder'])('blocks application after %s changes', async change => {
    const { ui, backend, snapshot, confirm } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced'); await ui.previewLoadoutPreset(); ui.selectLoadoutPresetCandidate('mixed');
    if (change === 'definition') ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'Changed' }] });
    if (change === 'ratios') { snapshot.settings._energyNguWeights = Array(9).fill(2); await ui.refresh(); }
    if (change === 'selection') ui.selectLoadoutPreset('other');
    if (change === 'session') { snapshot.runtime = { ...snapshot.runtime, startedAt: 'session-two' }; await ui.refresh(); }
    if (change === 'folder') { snapshot.runtime_dir = 'A:/other'; await ui.changeRuntimeDir('A:/other'); }
    await ui.applyLoadoutPreset();
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(1); expect(confirm).not.toHaveBeenCalled(); expect(ui.current().error).not.toBe('');
    if (change === 'session' || change === 'folder') expect(ui.current().loadoutPresetPreview).toBeNull();
    ui.dispose();
  });
  it('applies the chosen latest candidate without confirmation or settings/plan writes', async () => {
    const { ui, backend, confirm } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced'); await ui.previewLoadoutPreset(); ui.selectLoadoutPresetCandidate('mixed');
    confirm.mockResolvedValue(false);
    await ui.applyLoadoutPreset();
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).toHaveBeenLastCalledWith('A:/runtime', 'applyGlobalPreset', { ...globalArgs(), candidateKey: 'mixed', previewId });
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled(); expect(backend.savePlan).not.toHaveBeenCalled();
    expect(ui.current().loadoutPresetPreview).toBeNull();
    await ui.applyLoadoutPreset(); expect(backend.enqueueCommand).toHaveBeenCalledTimes(2);
    ui.dispose();
  });
  it('rejects an old or forged token even when the definition and candidate still match', async () => {
    const { ui, backend, confirm } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced'); await ui.previewLoadoutPreset();
    const nextId = 'f'.repeat(32);
    backend.readCommandResult.mockResolvedValueOnce({ id: 'cmd', command: 'previewGlobalPreset', success: true, data: { result: { ...result(), previewId: nextId } } });
    await ui.previewLoadoutPreset(); ui.selectLoadoutPresetCandidate('mixed');
    await ui.runCommand('applyGlobalPreset', { ...globalArgs(), candidateKey: 'mixed', previewId });
    expect(ui.current().error).toContain('token does not match'); expect(confirm).not.toHaveBeenCalled(); expect(backend.enqueueCommand).toHaveBeenCalledTimes(2);
    await ui.applyLoadoutPreset();
    expect(backend.enqueueCommand).toHaveBeenLastCalledWith('A:/runtime', 'applyGlobalPreset', { ...globalArgs(), candidateKey: 'mixed', previewId: nextId }); ui.dispose();
  });
  it.each<JsonValue>([null, NaN, Infinity, -Infinity])('rejects feasible candidates with an unknown/nonfinite score: %j', async score => {
    const { ui, backend, confirm } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const unavailable = result(); (unavailable.candidates as JsonObject[])[1].score = score;
    backend.readCommandResult.mockResolvedValueOnce({ id: 'cmd', command: 'previewGlobalPreset', success: true, data: { result: unavailable } });
    await ui.previewLoadoutPreset(); ui.selectLoadoutPresetCandidate('mixed'); await ui.applyLoadoutPreset();
    expect(ui.current().error).toContain('finite score'); expect(backend.enqueueCommand).toHaveBeenCalledTimes(1); expect(confirm).not.toHaveBeenCalled(); ui.dispose();
  });
  it('retains submitted arguments when the definition changes during a pending preview', async () => {
    const { ui, backend } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    let resolve!: (response: JsonObject) => void;
    backend.readCommandResult.mockImplementationOnce(() => new Promise(yes => { resolve = yes; }));
    const pending = ui.previewLoadoutPreset();
    await vi.waitFor(() => expect(backend.readCommandResult).toHaveBeenCalledOnce());
    ui.setPresetLibrary({ ...ui.current().presetLibrary!, Presets: [{ ...row(), Name: 'Edited while waiting' }] });
    resolve({ id: 'cmd', command: 'previewGlobalPreset', success: true, data: { result: result() } }); await pending;
    expect(ui.current().loadoutPresetPreview?.preset?.Name).toBe('All NGU');
    ui.selectLoadoutPresetCandidate('mixed'); await ui.applyLoadoutPreset();
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(1); expect(ui.current().error).toContain('Wait for preset changes to save'); ui.dispose();
  });
  it.each(['_energyNguWeights', '_magicNguWeights'])('clears preview and candidate on acknowledged global %s changes with the same library revision', async key => {
    const { ui, backend } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    await ui.previewLoadoutPreset(); ui.selectLoadoutPresetCandidate('mixed');
    const revision = ui.current().presetLibraryBase!.__externalUiRevision;
    const weights = (ui.current().snapshot!.settings[key] as number[]).map(() => 2);
    ui.editSetting(key, weights, 350);
    expect(ui.current().loadoutPresetPreview?.result.previewId).toBe(previewId);
    await ui.flushAutosave();
    expect(backend.saveSettingsPatch).toHaveBeenCalledExactlyOnceWith('A:/runtime', { [key]: weights }, 'a'.repeat(64));
    expect(ui.current().loadoutPresetPreview).toBeNull();
    expect(ui.current().loadoutPresetCandidateKey).toBe('');
    expect(ui.current().presetLibraryBase!.__externalUiRevision).toBe(revision);
    await ui.runCommand('applyGlobalPreset', { ...globalArgs(), previewId, candidateKey: 'mixed' });
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(1);
    expect(ui.current().error).toContain('No matching latest preview'); ui.dispose();
  });
  it.each(['previewGlobalPreset', 'previewLoadoutPreset'])('does not resurrect an in-flight %s after global weights change away and back', async command => {
    const { ui, backend, snapshot } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const args = command === 'previewGlobalPreset' ? globalArgs() : presetArgs(row());
    let resolve!: (value: JsonObject) => void;
    backend.readCommandResult.mockImplementationOnce(() => new Promise(yes => { resolve = yes; }));
    const pending = ui.runCommand(command, args);
    await vi.waitFor(() => expect(backend.readCommandResult).toHaveBeenCalledOnce());
    const original = snapshot.settings._energyNguWeights;
    snapshot.settings._energyNguWeights = Array(9).fill(3); await ui.refresh();
    snapshot.settings._energyNguWeights = original; await ui.refresh();
    resolve({ id: 'cmd', command, success: true, data: { result: result(args) } }); await pending;
    expect(ui.current().loadoutPresetPreview).toBeNull();
    expect(ui.current().loadoutPresetCandidateKey).toBe(''); ui.dispose();
  });
  it('previews and applies an inline definition without loading any plan', async () => {
    const { ui, backend, snapshot } = setup(); snapshot.plans = []; await ui.refresh();
    const args = presetArgs(row());
    await ui.runCommand('previewLoadoutPreset', args);
    expect(ui.current().loadoutPresetPreview?.args).toEqual({ preset: row() });
    ui.selectLoadoutPresetCandidate('mixed');
    await ui.runCommand('applyLoadoutPreset', { ...args, previewId, candidateKey: 'mixed' });
    expect(backend.enqueueCommand).toHaveBeenLastCalledWith('A:/runtime', 'applyLoadoutPreset', { preset: row(), previewId, candidateKey: 'mixed' });
    expect(backend.readPlan).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled(); ui.dispose();
  });
  it.each([undefined, 1, '2'])('rejects inline preset commands with loadoutPresetsVersion %s', async version => {
    const { ui, backend, snapshot } = setup();
    if (version === undefined) delete snapshot.runtime.loadoutPresetsVersion;
    else snapshot.runtime.loadoutPresetsVersion = version;
    await ui.refresh();
    await ui.runCommand('previewLoadoutPreset', presetArgs(row()));
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('loadoutPresetsVersion 2'); ui.dispose();
  });
  it('does not invalidate a preview on a failed global settings save', async () => {
    const { ui, backend } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    await ui.previewLoadoutPreset(); ui.selectLoadoutPresetCandidate('mixed');
    const preview = ui.current().loadoutPresetPreview;
    backend.saveSettingsPatch.mockRejectedValueOnce(new Error('Save rejected'));
    ui.editSetting('_magicNguWeights', Array(7).fill(0)); await ui.flushAutosave();
    expect(ui.current().loadoutPresetPreview).toBe(preview);
    expect(ui.current().loadoutPresetCandidateKey).toBe('mixed');
    expect(ui.current().saveError).toContain('Save rejected'); ui.dispose();
  });
  it('spends an uncertain Apply request without replaying it', async () => {
    const { ui, backend } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced'); await ui.previewLoadoutPreset(); ui.selectLoadoutPresetCandidate('mixed');
    backend.enqueueCommand.mockRejectedValueOnce(new Error('Transport lost after enqueue'));
    await ui.applyLoadoutPreset(); expect(ui.current().error).toContain('Outcome may be unknown'); expect(ui.current().loadoutPresetPreview).toBeNull();
    await ui.applyLoadoutPreset(); await ui.refresh(); expect(backend.enqueueCommand).toHaveBeenCalledTimes(2); ui.dispose();
  });
  it('rejects infeasible and arbitrary candidate keys even when bypassing UI buttons', async () => {
    const { ui, backend } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const unavailable = result(); (unavailable.candidates as JsonObject[])[1].feasible = false;
    backend.readCommandResult.mockResolvedValueOnce({ id: 'cmd', command: 'previewGlobalPreset', success: true, data: { result: unavailable } });
    await ui.previewLoadoutPreset(); ui.selectLoadoutPresetCandidate('mixed'); await ui.applyLoadoutPreset();
    expect(ui.current().error).toContain('feasible candidate');
    ui.selectLoadoutPresetCandidate('arbitrary'); await ui.applyLoadoutPreset(); expect(ui.current().error).toContain('feasible candidate'); expect(backend.enqueueCommand).toHaveBeenCalledTimes(1); ui.dispose();
  });
  it.each(['capability', 'session', 'stale'])('gates previews on missing %s', async reason => {
    const { ui, backend, snapshot } = setup();
    if (reason === 'capability') delete snapshot.runtime.globalPresetsVersion;
    if (reason === 'session') delete snapshot.runtime.startedAt;
    if (reason === 'stale') snapshot.runtime.updatedAt = '2000-01-01T00:00:00Z';
    await ui.refresh(); ui.selectLoadoutPreset('balanced'); await ui.previewLoadoutPreset(); expect(backend.enqueueCommand).not.toHaveBeenCalled(); ui.dispose();
  });
});

describe('Presets SSR', () => {
  it.each([false, true])('omits empty preset helper cards with compact=%s', async compact => {
    const { ui } = setup(); await ui.refresh();
    try {
      const state = ui.current();
      state.presetLibrary = { ...state.presetLibrary!, Presets: [] };
      state.presetLibraryBase = api.cloneObject(state.presetLibrary);
      const input = { ...props(state), compact };
      const body = render(Presets, { props: input }).body;
      expect(body).toContain(compact ? 'Manage presets' : 'Add preset');
      expect(body).toContain(`aria-label="${compact ? 'Inspect preset' : 'Browse preset'}"`);
      expect(body).not.toContain('aria-label="Preset settings"');
      expect(body).not.toContain('results-empty');
      expect(body).not.toContain('Add a global preset to get started.');
      expect(body).not.toContain('Select a preset above or add one to get started.');
      expect(body).not.toContain('Choose a preset to get started.');
      expect(input.onchange).not.toHaveBeenCalled();
      expect(input.onequip).not.toHaveBeenCalled();
    } finally { ui.dispose(); }
  });

  it('renders cap goals and dynamic weight as separate rows without mutating the library', async () => {
    const { ui } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const state = ui.current();
    state.presetLibrary = { ...library(), Presets: [{ ...row(), Weights: [...Array(12).fill(0), 7, 3], AdventureDynamicWeight: 2 }] };
    state.presetLibraryBase = api.cloneObject(state.presetLibrary);
    const before = api.stringifyJson(state), input = props(state);
    const body = render(Presets, { props: input }).body;
    for (const [index, value] of [[12, 7], [13, 3], [14, 2]]) expect(body).toMatch(new RegExp(`data-goal-index="${index}"[^>]*value="${value}"`));
    for (const label of [objectiveLabels[12], objectiveLabels[13], 'Dynamic adventure']) expect(body).toContain(`aria-label="${label} weight"`);
    expect(body).not.toContain('Add adventure-dynamic');
    expect(body).not.toContain('90% farming output');
    expect(body).not.toContain('aria-label="Advanced Training ratios"');
    expect(api.stringifyJson(state)).toBe(before);
    expect(input.onchange).not.toHaveBeenCalled(); ui.dispose();
  });
  it.each([0, .5])('compares omitted applied dynamic weight as zero against saved weight %s', AdventureDynamicWeight => {
    const { ui, snapshot } = setup(); ui.selectLoadoutPreset('balanced');
    const state = ui.current();
    const saved: JsonObject = { ...row(), AdventureDynamicWeight };
    const applied = row(); delete applied.AdventureDynamicWeight;
    state.presetLibrary = { ...library(), Presets: [saved] };
    state.presetLibraryBase = api.cloneObject(state.presetLibrary);
    state.snapshot = { ...snapshot, runtime: { loadoutPreset: { id: saved.Id, preset: applied, savedChangesRequireReequip: false, energyNguWeights: snapshot.settings._energyNguWeights, magicNguWeights: snapshot.settings._magicNguWeights } } };
    const before = api.stringifyJson(state);
    const body = render(Presets, { props: props(state) }).body;
    expect(body.includes('Preset changed. Re-equip or preview and reapply.')).toBe(AdventureDynamicWeight > 0);
    expect(body).not.toMatch(/autosave|all changes saved|storage|polling/i);
    expect(api.stringifyJson(state)).toBe(before);
    ui.dispose();
  });
  it.each([true, false])('renders active goals without redundant guidance or manual saving with AutoUpdate=%s', AutoUpdate => {
    const { ui } = setup(); ui.selectLoadoutPreset('balanced');
    const state = ui.current();
    state.presetLibrary = { ...library(), Presets: [{ ...row(), AutoUpdate }] };
    state.presetLibraryBase = api.cloneObject(state.presetLibrary);
    const before = api.stringifyJson(state), input = props(state);
    const body = render(Presets, { props: input }).body;
    for (const label of ['Energy NGU weight', 'Magic NGU weight', 'Add goal']) expect(body).toContain(`aria-label="${label}"`);
    for (const label of ['Adventure power weight', 'Adventure toughness weight', 'Advanced Training weight']) expect(body).not.toContain(`aria-label="${label}"`);
    for (const label of ['Adventure power', 'Adventure toughness', 'Advanced Training']) expect(body).toContain(`>${label}</option>`);
    expect(body).not.toContain('Preset energy');
    expect(body).not.toContain('Preset magic');
    expect(body).not.toContain('aria-label="Preset AT Power ratio"');
    expect(body).toContain('aria-label="Digger weights"');
    expect(body).not.toMatch(/aria-label="[^"]+ digger weight"/);
    expect(body).not.toContain('Advanced settings');
    expect(body).not.toContain('results-empty');
    expect(body).not.toContain('Preview to find a setup for your goals.');
    for (const text of ['Blood share of magic', 'Regular farming zone', 'Swap overhead']) expect(body).not.toContain(text);
    for (const text of ['On: re-optimize and apply', 'Off: reuse the last valid policy', 'Reuses the last setup', 'Preview and apply once to create it', 'Execution time belongs to plan stages', 'Scheduled presets use stage timing', 'standalone presets do not expire', '1-hour forecast', 'optimize immediate stats, not Advanced Training gains', 'Acquired policy auto-update', 'Re-equip explicitly to update', 'No background timer', 'Independent of the plan being browsed', 'Zero excludes a target', 'Save library', 'Enabled fill slots in priority order', 'Default weights follow preset priorities', 'Edited weights. Reset', '0 excludes a digger']) expect(body).not.toContain(text);
    expect(body).not.toContain('Duration'); expect(body).not.toContain('HorizonSeconds');
    expect(api.stringifyJson(state)).toBe(before); expect(input.onchange).not.toHaveBeenCalled(); ui.dispose();
  });
  it('ignores removed duration and legacy padding when comparing the acquired preset', async () => {
    const { ui } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const state = ui.current(), savedRow = globalPresetRows(state.presetLibrary)[0];
    state.snapshot!.runtime.loadoutPreset = { id: savedRow.Id, preset: { ...savedRow, Weights: (savedRow.Weights as number[]).slice(0, 6), HorizonSeconds: 17, EnergyNguWeights: [99], MagicNguWeights: null }, energyNguWeights: state.snapshot!.settings._energyNguWeights, magicNguWeights: state.snapshot!.settings._magicNguWeights };
    const body = render(Presets, { props: props(state) }).body;
    expect(body).not.toContain('Preset changed.'); ui.dispose();
  });
  it.each([true, false])('keeps acquired metadata distinct from saved metadata and respects the runtime re-equip flag: %s', savedChangesRequireReequip => {
    const { ui } = setup();
    const state = ui.current();
    state.snapshot = { runtime_dir: state.runtimeDir, connected: true, marker_enabled: true, settings: {}, protocol: {}, profiles: [], plans: [], logs: [], command_results: [], runtime: { loadoutPreset: { id: 'balanced', appliedName: 'Acquired name', appliedAutoUpdate: false, appliedAt: '2026-10-19T14:23:07.4258872Z', name: 'Saved name', autoUpdate: true, savedChangesRequireReequip } } };
    const before = api.stringifyJson(state), input = props(state);
    const body = render(Presets, { props: input }).body.replace(/<!--[\s\S]*?-->/g, '');
    expect(body).toContain('Acquired name');
    expect(body).not.toContain('Acquired policy');
    expect(body).not.toContain('2026-10-19T14:23:07.4258872Z');
    expect(body.includes('Preset changed. Re-equip or preview and reapply.')).toBe(savedChangesRequireReequip);
    expect(body).not.toContain('Saved name');
    expect(api.stringifyJson(state)).toBe(before);
    expect(input.onapply).not.toHaveBeenCalled(); expect(input.onequip).not.toHaveBeenCalled(); ui.dispose();
  });
  it.each(['_energyNguWeights', '_magicNguWeights'])('compares applied telemetry to acknowledged global %s for re-equip notices', async key => {
    const { ui } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const state = ui.current(); const savedRow = globalPresetRows(state.presetLibrary)[0];
    state.snapshot!.runtime.loadoutPreset = { id: savedRow.Id, preset: savedRow, energyNguWeights: state.snapshot!.settings._energyNguWeights, magicNguWeights: state.snapshot!.settings._magicNguWeights };
    state.snapshot!.settings = { ...state.snapshot!.settings, [key]: (state.snapshot!.settings[key] as number[]).map((weight, i) => i === 0 ? weight + 1 : weight) };
    const body = render(Presets, { props: props(state) }).body;
    expect(body).toContain('Preset changed. Re-equip or preview and reapply.');
    expect(body).not.toContain('Applied NGU ratios unavailable'); expect(globalPresetRows(state.presetLibraryBase)[0]).toEqual(savedRow); ui.dispose();
  });
  it('reports global weight changes for the applied setup even when no preset is selected', async () => {
    const { ui } = setup(); await ui.refresh();
    const state = ui.current();
    state.snapshot!.runtime.loadoutPreset = { id: 'balanced', preset: row(), energyNguWeights: Array(9).fill(0), magicNguWeights: state.snapshot!.settings._magicNguWeights };
    expect(state.loadoutPresetSelectedId).toBe('');
    const body = render(Presets, { props: props(state) }).body;
    expect(body).toContain('Preset changed. Re-equip or preview and reapply.'); ui.dispose();
  });
  it('labels missing legacy applied ratios unknown without claiming a definite change', async () => {
    const { ui } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const state = ui.current(); state.snapshot!.runtime.loadoutPreset = { id: 'balanced', preset: globalPresetRows(state.presetLibrary)[0] };
    state.plan = { ...state.plan!, EnergyNguWeights: Array(9).fill(2) };
    const body = render(Presets, { props: props(state) }).body;
    expect(body).toContain('Applied NGU ratios unavailable'); expect(body).not.toContain('Preset changed.'); ui.dispose();
  });
  it('keeps zone reference independent of presets with no settings or action callbacks', async () => {
    const { ui } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced');
    const state = ui.current(); state.presetLibrary = { ...state.presetLibrary!, Presets: [{ ...row(), Zone: 10 }] };
    const input = props(state); const before = api.stringifyJson(state);
    const body = render(Presets, { props: input }).body;
    expect(body).not.toContain('Zone reference');
    const reference = render(ZoneReference).body;
    expect(reference).toContain('aria-label="Zone reference"');
    expect(reference).toContain('Reference zone');
    expect(reference).toContain('Select a zone');
    expect(reference).toContain('Ancient Battlefield');
    expect(reference).not.toContain('Follow preset zone');
    expect(reference).not.toContain('aria-label="Idle recommendations"');
    expect(api.stringifyJson(state)).toBe(before); expect(input.onchange).not.toHaveBeenCalled(); ui.dispose();
  });
  it('renders sourced zone recommendations with unknown stats in the independent reference panel', () => {
    const body = render(ZoneReference, { props: { zoneId: 10 } }).body.replace(/<!--[\s\S]*?-->/g, '');
    expect(body).toContain('<option value="10" selected="">Ancient Battlefield</option>');
    expect(body).toMatch(/<h3[^>]*>Ancient Battlefield \/ normal<\/h3>/);
    expect(body).toMatch(/<a\b[^>]*href="https:\/\/ngu-idle\.fandom\.com\/wiki\/Ancient_Battlefield"[^>]*target="_blank"[^>]*rel="noreferrer"/);
    const idle = body.match(/<section[^>]*aria-label="Idle recommendations"[^>]*>([\s\S]*?)<\/section>/)?.[1] ?? '';
    const oneHit = body.match(/<section[^>]*aria-label="One-hit recommendations"[^>]*>([\s\S]*?)<\/section>/)?.[1] ?? '';
    expect(idle).toMatch(/<dt[^>]*>Power<\/dt><dd[^>]*>17,000<\/dd>/);
    expect(idle).toMatch(/<dt[^>]*>Toughness<\/dt><dd[^>]*>16,000<\/dd>/);
    expect(idle).toMatch(/<dt[^>]*>HP<\/dt><dd[^>]*>Unknown<\/dd>/);
    const recommendations = [...oneHit.matchAll(/<dl\b[^>]*>([\s\S]*?)<\/dl>\s*<p\b[^>]*>([^<]*)<\/p>/g)];
    expect(recommendations).toHaveLength(2);
    expect(recommendations[0][1]).toMatch(/<dt[^>]*>Power<\/dt><dd[^>]*>422,550<\/dd>/);
    expect(recommendations[0][2]).toBe('Without the separately listed Spoopy set bonus');
    expect(recommendations[1][1]).toMatch(/<dt[^>]*>Power<\/dt><dd[^>]*>282,966<\/dd>/);
    expect(recommendations[1][2]).toBe('With Spoopy set bonus');
    for (const recommendation of recommendations) {
      for (const label of ['Toughness', 'HP']) expect(recommendation[1]).toMatch(new RegExp(`<dt[^>]*>${label}</dt><dd[^>]*>Unknown</dd>`));
    }
    expect(body).toContain('localZoneStats stores only the Spoopy-bonus one-hit value');
    expect(body).not.toMatch(/<input\b|<button\b/);
  });
  it('renders actionable results and collapsed details without the baseline, explanations, mutation or actions', async () => {
    const { ui } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced'); await ui.previewLoadoutPreset();
    const state = ui.current(); const before = api.stringifyJson(state); const input = props(state);
    const body = render(Presets, { props: input }).body;
    for (const label of ['Results', 'estimated', 'proxy', 'n/a', '50%', '2x', 'Head', 'Owned NGU hat', 'Gold budget', 'Digger allocation', 'Two-phase estimate', 'preview only', 'Apply Shared NGU + blood']) expect(body).toContain(label);
    for (const label of ['Browsing is not applying', 'Last applied', 'Current gear</h3>', 'Current setup baseline', 'Choose Shared', 'Not a full-game simulation', 'Target-aware NGU phases', 'Only one adventure route', 'Normalization:', 'Weighted contribution', 'Quick priorities', 'Warnings &amp; notes', 'Full-focus alternatives', 'Preview details', 'Bounded search', 'Frozen owned gear', 'Budget accounts for drain']) expect(body).not.toContain(label);
    expect(api.stringifyJson(state)).toBe(before); expect(input.onchange).not.toHaveBeenCalled(); expect(input.onpreview).not.toHaveBeenCalled(); expect(input.onapply).not.toHaveBeenCalled(); ui.dispose();
  });
  it('clearly marks changed preview and edited last-applied definitions', async () => {
    const { ui } = setup(); await ui.refresh(); ui.selectLoadoutPreset('balanced'); await ui.previewLoadoutPreset();
    const state = ui.current(); state.snapshot!.runtime.loadoutPreset = { id: 'balanced', name: 'Balanced NGU', candidateKey: 'mixed', status: 'applied', appliedAt: 'yesterday', preset: row(), active: false, requiresReapply: true, mode: 'applied-snapshot' };
    state.presetLibrary = { ...state.presetLibrary!, Presets: [{ ...row(), Name: 'Edited applied preset' }] }; state.presetLibraryDirty = true;
    const body = render(Presets, { props: props(state) }).body;
    expect(body).toContain('Changed since preview'); expect(body).toContain('Preset changed. Re-equip or preview and reapply.'); expect(body).not.toContain('50%'); expect(body).not.toContain('2x');
    expect(body).not.toContain('yesterday');
    expect(globalPreviewMatches(state.loadoutPresetPreview, globalArgs(), state.runtimeDir, state.snapshot!.runtime.startedAt, false)).toBe(false); ui.dispose();
  });
});
