import { beforeEach, describe, expect, it, vi } from 'vitest';
import { invoke } from '@tauri-apps/api/core';
import { LosslessNumber } from 'lossless-json';
import * as api from '../src/lib/api';
import { createController } from '../src/lib/controller';
import { assertPlanMetadata, diffObject, equal, freshEdits, freshPlan, mergePatch, pretty, reorderStages, validatePlan } from '../src/lib/model';
import type { JsonObject, NguiState } from '../src/lib/api';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));
const native = vi.mocked(invoke);
const first = '9007199254740993';
const second = '123456789012345678901234567890';
const revision = (value: string) => value.repeat(64);
const source = () => `{
  "Version":18,"Name":"plan","__externalUiRevision":"${revision('a')}",
  "StageDurationSeconds":1800,"StageLoopStartIndex":0,"GearGoal":0,"ActiveGoalPresetIndex":0,
  "Future":${first},"Nested":{"value":${second},"array":[${first},${second}]},
  "GoalPresets":[{"Name":"Default","GearGoal":0,"Future":${second}}],
  "RebirthHistory":[{"Future":${second}}],
  "Stages":[
    {"Name":"First","Percent":60,"GearGoal":-1,"FightSchedule":null,"Future":{"value":${first},"nested":{"value":${second}}},"TargetAdvancedTrainingPower":"9223372036854775807"},
    {"Name":"Second","Percent":40,"GearGoal":-1,"FightSchedule":null,"Future":{"value":${second}}}
  ]
}`;
const plan = () => api.parseJson(source()) as JsonObject;
const savedSettingsResponse = (settings: JsonObject) => api.stringifyJson({ success: true, mode: 'offline', data: { settings, revision: revision('b') } });
const ready = (): NguiState => ({ runtime_dir: 'A:/runtime', marker_enabled: true, connected: true, protocol: {}, settings: {}, settings_revision: revision('a'), runtime: { status: 'running', externalUiEnabled: true, protocolVersion: 3, stageTimingVersion: 1, fightScheduleVersion: 1, planVersion: 18, updatedAt: new Date().toISOString(), supportedCommands: ['readPlan', 'savePlan', 'createPlan', 'applySettingsPatch'] }, profiles: [], plans: [], logs: [], command_results: [] });

beforeEach((): void => {
  // Returning the mock would register it as a teardown callback in Vitest.
  native.mockReset();
});

describe('lossless JSON helpers', () => {
  it('keeps small known values as numbers and unsafe unknown tokens as numeric scalars', () => {
    const value = plan();
    expect(value.Version).toBe(18);
    expect(value.StageDurationSeconds).toBe(1800);
    expect(value.Future).toBeInstanceOf(LosslessNumber);
    expect(String(value.Future)).toBe(first);
    expect(String((value.Nested as JsonObject).value)).toBe(second);
    expect(api.isObject(value.Future)).toBe(false);
    expect(api.isObject(value.Nested)).toBe(true);
    expect((value.Stages as JsonObject[])[0].TargetAdvancedTrainingPower).toBe('9223372036854775807');
  });
  it('round-trips nested unknown numeric tokens repeatedly without quoting or rounding', () => {
    let value = plan();
    for (let i = 0; i < 5; i++) {
      value = api.cloneObject(value);
      const json = pretty(value);
      expect(json).toContain(`"Future": ${first}`);
      expect(json).toContain(`"value": ${second}`);
      expect(json).not.toContain(`"${first}"`);
      expect(json).not.toContain('9007199254740992');
      expect(String((value.Nested as JsonObject).value)).toBe(second);
      validatePlan(value);
    }
  });
  it('treats lossless numbers as exact scalar values in equality and diffs', () => {
    const a = new LosslessNumber(first);
    const b = new LosslessNumber(second);
    expect(equal(a, new LosslessNumber(first))).toBe(true);
    expect(equal(a, b)).toBe(false);
    expect(equal(a, Number(first))).toBe(false);
    expect(diffObject({ value: a }, { value: b })).toEqual({ value: b });
    expect(api.stringifyJson(mergePatch({ value: a, untouched: b }, { value: b }))).toBe(`{"value":${second},"untouched":${second}}`);
  });
  it('preserves large decimal, exponent, and underflow tokens without native number coercion', () => {
    const text = '{"decimal":0.12345678901234567890123456789,"exponent":9007199254740993e0,"tiny":1e-999}';
    expect(api.stringifyJson(api.parseJson(text))).toBe(text);
  });
  it('retains the unknown scalar while applying non-conflicting fresh edits', () => {
    const original = { value: new LosslessNumber(first), changed: false };
    const latest = { ...original, added: new LosslessNumber(second) };
    const merged = freshEdits(original, { ...original, changed: true }, latest);
    expect(api.stringifyJson(merged)).toBe(`{"value":${first},"changed":true,"added":${second}}`);
  });
  it('detects external changes to an unsafe numeric scalar without rounding either value', () => {
    const original = { value: new LosslessNumber(first) };
    expect(() => freshEdits(original, { value: new LosslessNumber(second) }, { value: new LosslessNumber('9007199254740994') })).toThrow('Source changed at value');
  });
});

describe('revision metadata', () => {
  it('uses the fresh revision and normalized version rather than the draft transport token', () => {
    const base = plan();
    const draft = { ...base, StageDurationSeconds: 3600, __externalUiRevision: revision('c') };
    const fresh = { ...base, Version: 15, __externalUiRevision: revision('b'), RebirthHistory: [{ Future: new LosslessNumber(second), fresh: true }] };
    const merged = freshPlan(base, draft, fresh);
    expect(merged.__externalUiRevision).toBe(revision('b'));
    expect(merged.Version).toBe(15);
    expect(merged.StageDurationSeconds).toBe(3600);
    expect(merged.RebirthHistory).toEqual(fresh.RebirthHistory);
    expect(String(merged.Future)).toBe(first);
  });
  it.each(['Name', 'Version', 'RebirthHistory', '__externalUiRevision'])('rejects editing backend-owned %s', key => {
    const base = plan();
    expect(() => assertPlanMetadata(base, { ...base, [key]: null })).toThrow(`${key} is backend-owned`);
    const omitted = { ...base }; delete omitted[key];
    expect(() => assertPlanMetadata(base, omitted)).toThrow(`${key} is backend-owned`);
  });
  it('requires a revision for validation even when all model fields are valid', () => {
    const value = plan(); delete value.__externalUiRevision;
    expect(() => validatePlan(value)).toThrow('SHA-256 revision');
  });
  it('rejects a legacy version rather than silently bypassing backend migrations', () => {
    expect(() => validatePlan({ ...plan(), Version: 13 })).toThrow('migrate legacy plans');
  });
});

describe('native plan string IPC', () => {
  it('strips retired overrides on read, save, and copy while preserving version and unknown data', async () => {
    const original = { ...plan(), Version: 18, EnergyNguWeights: [3], MagicNguWeights: null, LoadoutPresets: [{ Id: 'legacy', EnergyNguWeights: [4], MagicNguWeights: [5], AdvancedTrainingWeights: [1, 1, 0, 0, 0] }], Stages: [{ Name: 'First', EnergyNguWeights: [6], MagicNguWeights: [7], Future: new LosslessNumber(first) }] };
    const before = api.stringifyJson(original);
    native.mockResolvedValueOnce(before).mockResolvedValueOnce({ success: true }).mockResolvedValueOnce('copy');
    const loaded = await api.readPlan('A:/runtime', 'plan');
    await api.savePlan('A:/runtime', 'plan', original);
    await api.createPlan('A:/runtime', 'copy', original);
    const saved = api.parseJson((native.mock.calls[1][1] as { planJson: string }).planJson) as JsonObject;
    const copied = api.parseJson((native.mock.calls[2][1] as { sourceJson: string }).sourceJson) as JsonObject;
    for (const value of [loaded, saved, copied]) {
      expect(value.Version).toBe(18);
      expect(value).not.toHaveProperty('EnergyNguWeights'); expect(value).not.toHaveProperty('MagicNguWeights');
      expect(value.Stages).toEqual([{ Name: 'First', FightSchedule: null, Future: new LosslessNumber(first) }]);
      expect(value.LoadoutPresets).toEqual([{ Id: 'legacy', AdvancedTrainingWeights: [1, 1, 0, 0, 0] }]);
      expect(String(value.Future)).toBe(first);
    }
    expect(api.stringifyJson(original)).toBe(before);
  });
  it('parses normalized read_plan strings without rounding numeric tokens', async () => {
    native.mockResolvedValueOnce(source());
    const value = await api.readPlan('A:/runtime', 'plan');
    expect(native).toHaveBeenCalledWith('read_plan', { runtimeDir: 'A:/runtime', name: 'plan' });
    expect(String(value.Future)).toBe(first);
    expect(value.__externalUiRevision).toBe(revision('a'));
  });
  it('rejects the obsolete object read_plan response', async () => {
    native.mockResolvedValueOnce(plan());
    await expect(api.readPlan('A:/runtime', 'plan')).rejects.toThrow('must return a JSON string');
  });
  it.each([null, '', 'a'.repeat(63), 'A'.repeat(64)])('rejects an invalid read revision: %s', token => {
    native.mockResolvedValueOnce(api.stringifyJson({ ...plan(), __externalUiRevision: token }));
    return expect(api.readPlan('A:/runtime', 'plan')).rejects.toThrow('SHA-256 revision');
  });
  it('rejects an offline legacy read and retains the native migration error', async () => {
    native.mockRejectedValueOnce(new Error('Legacy plan cannot be edited offline. Connect to migrate.'));
    await expect(api.readPlan('A:/runtime', 'legacy')).rejects.toThrow('Connect to migrate');
  });
  it('rejects a non-normalized successful native response', async () => {
    native.mockResolvedValueOnce(api.stringifyJson({ ...plan(), Version: 13 }));
    await expect(api.readPlan('A:/runtime', 'legacy')).rejects.toThrow('version 18');
  });
  it('sends only planJson as a string containing unquoted exact numbers and the CAS token', async () => {
    native.mockResolvedValueOnce({ success: true, mode: 'connected' });
    await api.savePlan('A:/runtime', 'plan', plan());
    const args = native.mock.calls[0][1] as Record<string, unknown>;
    expect(native.mock.calls[0][0]).toBe('save_plan');
    expect(Object.keys(args).sort()).toEqual(['name', 'planJson', 'runtimeDir']);
    expect(typeof args.planJson).toBe('string');
    expect(args.planJson).toContain(`"Future":${first}`);
    expect(args.planJson).toContain(`"value":${second}`);
    expect((api.parseJson(args.planJson as string) as JsonObject).__externalUiRevision).toBe(revision('a'));
  });
  it('blocks a save with no revision before invoking the native backend', async () => {
    const value = plan(); delete value.__externalUiRevision;
    await expect(api.savePlan('A:/runtime', 'plan', value)).rejects.toThrow('Reload the plan');
    expect(native).not.toHaveBeenCalled();
  });
  it('sends optional copy sourceJson as a lossless string without the original revision', async () => {
    native.mockResolvedValueOnce('copy');
    const original = plan();
    await api.createPlan('A:/runtime', 'copy', original);
    const args = native.mock.calls[0][1] as Record<string, unknown>;
    expect(native.mock.calls[0][0]).toBe('create_plan');
    expect(Object.keys(args).sort()).toEqual(['requestedName', 'runtimeDir', 'sourceJson']);
    expect(typeof args.sourceJson).toBe('string');
    expect(args.sourceJson).toContain(`"Future":${first}`);
    expect(args.sourceJson).toContain(`"value":${second}`);
    expect(args.sourceJson).not.toContain('__externalUiRevision');
    expect(original.__externalUiRevision).toBe(revision('a'));
  });
  it('omits the source when creating backend defaults', async () => {
    native.mockResolvedValueOnce('new');
    await api.createPlan('A:/runtime', 'new');
    expect(native).toHaveBeenCalledWith('create_plan', { runtimeDir: 'A:/runtime', requestedName: 'new', sourceJson: undefined });
  });
  it('sends exact settings numeric tokens and the revision through string IPC', async () => {
    native.mockResolvedValueOnce(savedSettingsResponse({ _moneyPitThreshold: new LosslessNumber(first) }));
    await api.saveSettingsPatch('A:/runtime', { _moneyPitThreshold: new LosslessNumber(first) }, revision('a'));
    expect(native).toHaveBeenCalledExactlyOnceWith('save_settings_patch_json', { runtimeDir: 'A:/runtime', patchJson: `{"_moneyPitThreshold":${first}}`, expectedRevision: revision('a') });
  });
  it('does not submit nested lossless wrappers over action object IPC', async () => {
    await expect(api.enqueueCommand('A:/runtime', 'focusGoal', { unknown: { nested: [new LosslessNumber(second)] } })).rejects.toThrow('Object IPC');
    expect(native).not.toHaveBeenCalled();
  });
});

describe('frontend save regressions: numeric settings IPC', () => {
  it.each(['autosave', 'inline'])('preserves a valid native large double through %s', async mode => {
    const settings: JsonObject = { _moneyPitThreshold: 0 };
    native.mockImplementation(async (command, args) => {
      if (command !== 'save_settings_patch_json') throw new Error(`Unexpected command: ${command}`);
      Object.assign(settings, api.parseJson((args as { patchJson: string }).patchJson));
      return savedSettingsResponse(settings);
    });
    const ui = createController({ ...api, getState: async () => ({ ...ready(), settings: { ...settings } }), storeRuntimeDir: () => {} }, async () => true, 'A:/runtime');
    try {
      await ui.refresh();
      if (mode === 'autosave') {
        ui.editSetting('_moneyPitThreshold', 1e16, 350);
        await ui.flushAutosave();
      } else expect(await ui.saveSettingValue('_moneyPitThreshold', 1e16)).toBe(true);
      expect(native).toHaveBeenCalledExactlyOnceWith('save_settings_patch_json', { runtimeDir: 'A:/runtime', patchJson: api.stringifyJson({ _moneyPitThreshold: 1e16 }), expectedRevision: revision('a') });
      expect(ui.current().patch).toEqual({});
      expect(String(ui.current().snapshot!.settings._moneyPitThreshold)).toBe('10000000000000000');
      expect(ui.current().error).toBe('');
    } finally { ui.dispose(); }
  });

  for (const mode of ['autosave', 'inline']) {
    it.each(['unknown', '_moneyPitThreshold'])(`transports genuine unsafe tokens without rounding for %s through ${mode}`, async key => {
      const exact = new LosslessNumber(first);
      native.mockImplementation(async (_command, args) => {
        expect((args as { patchJson: string }).patchJson).toBe(`{"${key}":${first}}`);
        if (key === 'unknown') throw new Error('Unknown settings field: unknown');
        return savedSettingsResponse({ _moneyPitThreshold: exact });
      });
      const ui = createController({ ...api, getState: async () => ({ ...ready(), settings: { _moneyPitThreshold: 0 } }), storeRuntimeDir: () => {} }, async () => true, 'A:/runtime');
      try {
        await ui.refresh();
        if (mode === 'autosave') {
          ui.editSetting(key, exact, 350);
          await ui.flushAutosave();
        } else expect(await ui.saveSettingValue(key, exact)).toBe(key !== 'unknown');
        expect(native).toHaveBeenCalledTimes(1);
        expect(api.stringifyJson(ui.current().patch)).toBe(key === 'unknown' ? `{"${key}":${first}}` : '{}');
        expect(ui.current().error).toBe(key === 'unknown' ? 'Unknown settings field: unknown' : '');
      } finally { ui.dispose(); }
    });
  }

  it('keeps a newer native large-double draft numeric across another setting readback', async () => {
    const settings: JsonObject = { _customIronPillSaveSeconds: 900, _moneyPitThreshold: 0 };
    let release!: () => void;
    native.mockImplementation(async (command, args) => {
      if (command !== 'save_settings_patch_json') throw new Error(`Unexpected command: ${command}`);
      if (native.mock.calls.length === 1) await new Promise<void>(resolve => { release = resolve; });
      Object.assign(settings, api.parseJson((args as { patchJson: string }).patchJson));
      return savedSettingsResponse(settings);
    });
    const ui = createController({ ...api, getState: async () => ({ ...ready(), settings: { ...settings } }), storeRuntimeDir: () => {} }, async () => true, 'A:/runtime');
    try {
      await ui.refresh();
      const saving = ui.saveSettingValue('_customIronPillSaveSeconds', 1200);
      await vi.waitFor(() => expect(native).toHaveBeenCalledTimes(1));
      ui.setSetting('_moneyPitThreshold', 1e16);
      release();
      expect(await saving).toBe(true);
      expect(ui.current().patch).toEqual({ _moneyPitThreshold: 1e16 });
      await ui.saveSettings();
      expect(native).toHaveBeenLastCalledWith('save_settings_patch_json', { runtimeDir: 'A:/runtime', patchJson: api.stringifyJson({ _moneyPitThreshold: 1e16 }), expectedRevision: revision('a') });
      expect(native).toHaveBeenCalledTimes(2);
      expect(ui.current().patch).toEqual({});
      expect(ui.current().error).toBe('');
    } finally { ui.dispose(); }
  });
});

describe('controller and string IPC round trips', () => {
  it('retains automatic adventure weight through saves, readback, and standard mode switching', async () => {
    const original = plan();
    original.Version = 18;
    let disk = api.stringifyJson(original);
    const writes: JsonObject[] = [];
    native.mockImplementation(async (command, args) => {
      if (command === 'read_plan') return disk;
      if (command === 'save_plan') {
        const value = api.parseJson((args as { planJson: string }).planJson) as JsonObject;
        writes.push(value);
        disk = api.stringifyJson({ ...value, __externalUiRevision: revision(String(writes.length)) });
        return { success: true };
      }
      throw new Error(`Unexpected command: ${command}`);
    });
    const ui = createController({ ...api, getState: async () => ready(), storeRuntimeDir: () => {} }, async () => true, 'A:/runtime');
    try {
      await ui.refresh(); await ui.loadPlan('plan');
      for (const ExecutionMode of [1, 0, 1]) {
        const current = ui.current().plan!;
        ui.setPlan({ ...current, Stages: (current.Stages as JsonObject[]).map((row, index) => index === 0 ? { ...row, ExecutionMode, AdventureDynamicWeight: .125 } : row) });
        await ui.saveActivePlan();
        expect(ui.current().error).toBe('');
        expect(ui.current().planDirty).toBe(false);
        expect((ui.current().plan!.Stages as JsonObject[])[0]).toEqual({ ...(original.Stages as JsonObject[])[0], ExecutionMode, AdventureDynamicWeight: .125 });
        expect(ui.current().planBase).toEqual(ui.current().plan);
      }
      expect(writes.map(value => (value.Stages as JsonObject[])[0].AdventureDynamicWeight)).toEqual([.125, .125, .125]);
      expect(writes.map(value => (value.Stages as JsonObject[])[0].ExecutionMode)).toEqual([1, 0, 1]);
      expect(api.stringifyJson(ui.current().plan)).toContain(`"value":${first}`);
    } finally { ui.dispose(); }
  });

  it('preserves exact nested stage numbers through reorder and repeated saves while renewing revisions', async () => {
    let disk = source();
    let writes = 0;
    const sent: string[] = [];
    native.mockImplementation(async (command, args) => {
      const values = args as Record<string, unknown>;
      if (command === 'read_plan') return disk;
      if (command === 'save_plan') {
        const json = values.planJson as string;
        const value = api.parseJson(json) as JsonObject;
        expect(value.__externalUiRevision).toBe((api.parseJson(disk) as JsonObject).__externalUiRevision);
        sent.push(json);
        writes++;
        disk = api.stringifyJson({ ...value, __externalUiRevision: revision(String(writes)) });
        return { success: true, mode: 'connected' };
      }
      throw new Error(`Unexpected command: ${command}`);
    });
    const backend = { ...api, getState: async () => ready(), storeRuntimeDir: () => {} };
    const ui = createController(backend, async () => true, 'A:/runtime');
    await ui.refresh(); await ui.loadPlan('plan');
    for (let i = 0; i < 3; i++) {
      const edited = reorderStages(ui.current().plan!, 0, 1);
      ui.setPlan({ ...edited, StageDurationSeconds: 2000 + i });
      await ui.saveActivePlan();
      expect(ui.current().error).toBe('');
      expect(ui.current().planDirty).toBe(false);
      expect(ui.current().plan!.__externalUiRevision).toBe(revision(String(i + 1)));
      expect(ui.current().planBase!.__externalUiRevision).toBe(revision(String(i + 1)));
      const rows = ui.current().plan!.Stages as JsonObject[];
      const originalFirst = rows.find(stage => stage.Name === 'First')!;
      expect(String((originalFirst.Future as JsonObject).value)).toBe(first);
      expect(String(((originalFirst.Future as JsonObject).nested as JsonObject).value)).toBe(second);
      expect(String(ui.current().plan!.Future)).toBe(first);
    }
    expect(sent).toHaveLength(3);
    expect(native.mock.calls.map(([command]) => command)).toEqual([
      'read_plan',
      'read_plan', 'save_plan', 'read_plan',
      'read_plan', 'save_plan', 'read_plan',
      'read_plan', 'save_plan', 'read_plan'
    ]);
    for (const json of sent) {
      expect(json).toContain(`"Future":${first}`);
      expect(json).toContain(`"value":${second}`);
      expect(json).not.toContain(`"${first}"`);
      expect(json).not.toContain('9007199254740992');
    }
  });
  it('keeps untouched rounded settings snapshot numbers out of pending patches', async () => {
    const settings = { known: true, unknown: Number(first) };
    const ui = createController({ ...api, getState: async () => ({ ...ready(), settings }), storeRuntimeDir: () => {} }, async () => true, 'A:/runtime');
    await ui.refresh();
    const edited = api.parseJson(pretty(settings)) as JsonObject;
    edited.known = false;
    ui.applyRawSettings(edited, settings);
    native.mockResolvedValueOnce(savedSettingsResponse({ ...settings, known: false }));
    await ui.saveSettings();
    expect(native).toHaveBeenCalledWith('save_settings_patch_json', { runtimeDir: 'A:/runtime', patchJson: '{"known":false}', expectedRevision: revision('a') });
    expect(native).toHaveBeenCalledTimes(1);
  });
});
