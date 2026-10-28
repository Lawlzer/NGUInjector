import { afterEach, describe, expect, it, vi } from 'vitest';
import type { JsonObject, NguiState } from '../src/lib/api';
import { builtInPlans, canCommand, diffObject, equal, freshEdits, freshPlan, heartbeat, longTarget, mergePatch, parseNumber, planDeletionReason, pollResult, reorderStages, validateAugmentArgs, validatePlan, validatePriorities, validateSettings } from '../src/lib/model';

const plan = (): JsonObject => ({
  Version: 14, __externalUiRevision: 'a'.repeat(64), Name: 'test', GearGoal: 9, ActiveGoalPresetIndex: 0, GoalPresets: [{ Name: 'Default', GearGoal: 9, FuturePreset: true }],
  StageLoopStartIndex: 1, StageDurationSeconds: 1800, RebirthHistory: [{ TimestampLocal: 'old' }], Future: { keep: 1 },
  Stages: ['A', 'B', 'C'].map(Name => ({ Name, Percent: 10, EnergyFocus: 0, MagicFocus: 0, GearGoal: -1, CompletionMode: 0, BlackBarLookaheadSeconds: 5 }))
});

describe('patches and fresh snapshots', () => {
  it('compares JSON objects structurally while retaining array order', () => {
    expect(equal({ a: 1, b: { c: 2 } }, { b: { c: 2 }, a: 1 })).toBe(true);
    expect(equal([1, 2], [2, 1])).toBe(false);
  });
  it('preserves unknown prototype-like JSON keys as data', () => {
    const future = JSON.parse('{"__proto__":{"future":42}}');
    const next = mergePatch({}, diffObject({}, future));
    expect(Object.prototype.hasOwnProperty.call(next, '__proto__')).toBe(true);
    expect(next.__proto__).toEqual({ future: 42 });
    expect(Object.getPrototypeOf(next)).toBe(Object.prototype);
  });
  it('diffs nested changed keys without sending unrelated fields', () => {
    expect(diffObject({ enabled: true, nested: { keep: 1, change: 2 }, future: 3 }, { enabled: true, nested: { keep: 1, change: 4 }, future: 3 })).toEqual({ nested: { change: 4 } });
  });
  it('merges nested patches without deleting unknown fields or the input', () => {
    const base = { enabled: true, nested: { keep: 1, change: 2 }, future: 3 };
    expect(mergePatch(base, { enabled: false, nested: { change: 4 } })).toEqual({ enabled: false, nested: { keep: 1, change: 4 }, future: 3 });
    expect(base.nested.change).toBe(2);
  });
  it('uses fresh runtime history and future fields when merging plan edits', () => {
    const base = plan();
    const latest = { ...base, Future: { keep: 1, newlyAdded: 2 }, RebirthHistory: [{ TimestampLocal: 'new' }] };
    const draft = { ...base, Name: 'wrong', RebirthHistory: [], StageDurationSeconds: 3600 };
    expect(freshPlan(base, draft, latest)).toEqual({ ...latest, StageDurationSeconds: 3600 });
  });
  it('rejects overlapping changes made by another writer', () => {
    expect(() => freshPlan(plan(), { ...plan(), StageDurationSeconds: 3600 }, { ...plan(), StageDurationSeconds: 7200 })).toThrow('Source changed at StageDurationSeconds');
  });
  it('allows a stale raw editor to change a non-conflicting key', () => {
    expect(freshEdits({ a: 1, b: 2 }, { a: 3, b: 2 }, { a: 1, b: 4, future: 5 })).toEqual({ a: 3, b: 4, future: 5 });
  });
});

describe('plan validation', () => {
  it.each([undefined, 0, 900, 2147483647])('retains optional RebirthPrepStartSeconds without changing unknown fields: %s', RebirthPrepStartSeconds => {
    const value = plan();
    if (RebirthPrepStartSeconds !== undefined) value.RebirthPrepStartSeconds = RebirthPrepStartSeconds;
    const before = structuredClone(value);
    validatePlan(value);
    expect(value).toEqual(before);
  });
  it.each([null, -1, 1.5, 2147483648, NaN, Infinity, '900', true, {}, []])('rejects invalid RebirthPrepStartSeconds: %s', RebirthPrepStartSeconds => {
    expect(() => validatePlan({ ...plan(), RebirthPrepStartSeconds })).toThrow('RebirthPrepStartSeconds');
  });
  it.each([undefined, false, true])('retains optional UseWandoosWhenTimeMachineLocked without changing unknown fields: %s', UseWandoosWhenTimeMachineLocked => {
    const row: JsonObject = { Name: 'Time Machine', Future: { keep: 42 } };
    if (UseWandoosWhenTimeMachineLocked !== undefined) row.UseWandoosWhenTimeMachineLocked = UseWandoosWhenTimeMachineLocked;
    const value = { ...plan(), Stages: [row], StageLoopStartIndex: -1 };
    const before = structuredClone(value);
    validatePlan(value);
    expect(value).toEqual(before);
  });
  it.each([null, 0, 1, 'true', {}, []])('rejects a non-boolean UseWandoosWhenTimeMachineLocked: %s', UseWandoosWhenTimeMachineLocked => {
    expect(() => validatePlan({ ...plan(), Stages: [{ UseWandoosWhenTimeMachineLocked }], StageLoopStartIndex: -1 })).toThrow('Stage 1 UseWandoosWhenTimeMachineLocked must be boolean');
  });
  it.each([undefined, false, true])('retains optional FightOnEntry without migrating the plan: %s', FightOnEntry => {
    const row: JsonObject = { Name: 'After augments' };
    if (FightOnEntry !== undefined) row.FightOnEntry = FightOnEntry;
    const value: JsonObject = { ...plan(), Stages: [row], StageLoopStartIndex: -1 };
    validatePlan(value);
    expect(row.FightOnEntry).toBe(FightOnEntry);
    expect(value.Version).toBe(14);
  });
  it.each([null, 1, 'true', {}, []])('rejects a non-boolean FightOnEntry: %s', FightOnEntry => {
    expect(() => validatePlan({ ...plan(), Stages: [{ FightOnEntry }], StageLoopStartIndex: -1 })).toThrow('FightOnEntry must be boolean');
  });
  it('accepts the model default preset without changing gear or unknown preset fields', () => {
    const value = { ...plan(), GearGoal: 0, GoalPresets: [{ Name: 'Default', GearGoal: 0, Future: { preserved: true } }] };
    validatePlan(value);
    expect(value.GoalPresets).toEqual([{ Name: 'Default', GearGoal: 0, Future: { preserved: true } }]);
    expect(value.GearGoal).toBe(0);
  });
  it('accepts an implicit Default preset only at index zero', () => {
    const value = plan();
    delete value.GoalPresets;
    validatePlan(value);
    expect(value.ActiveGoalPresetIndex).toBe(0);
    expect(() => validatePlan({ ...value, ActiveGoalPresetIndex: 1 })).toThrow('implicit Default preset');
  });
  it.each([null, 1, false, '', ' '])('rejects a Name incompatible with the plan model: %s', Name => {
    expect(() => validatePlan({ ...plan(), Name })).toThrow('Name must be');
  });
  it('rejects an invalid type for a new goal preset without dropping its future fields', () => {
    const presets: JsonObject[] = [{ Name: 'Default', GearGoal: 9 }, { Name: 'New', GearGoal: '0', Future: 42 }];
    const value = { ...plan(), GoalPresets: presets };
    expect(() => validatePlan(value)).toThrow('Goal preset 2 GearGoal');
    expect(presets[1].Future).toBe(42);
  });
  it('accepts every stage field, exact Int64 strings, and leave-unchanged gear', () => {
    const value = plan();
    value.Stages = [{ Name: 'AT', Percent: 100, CompletionMode: 1, EnergyFocus: 7, MagicFocus: 4, GearGoal: -1, TargetAdvancedTrainingPower: '9223372036854775807', TargetAdvancedTrainingToughness: '6000', BlackBarLookaheadSeconds: 3, EnergyPriorities: ['AT-1', 'AT-0', 'AUG-13', 'NGU-8'], MagicPriorities: ['RIT-7', 'NGU-6'] }];
    value.StageLoopStartIndex = 0;
    expect(validatePlan(value)).toBeUndefined();
    expect(longTarget('9223372036854775807', 'AT')).toBe('9223372036854775807');
  });
  it.each([NaN, Infinity, -1, '', '6000', 1.5, 2147483648])('rejects invalid integer duration %s', value => {
    expect(() => validatePlan({ ...plan(), StageDurationSeconds: value })).toThrow('StageDurationSeconds');
  });
  it.each([0, -1, NaN, Infinity, ''])('rejects invalid positive time share %s', Percent => {
    expect(() => validatePlan({ ...plan(), Stages: [{ Name: 'Bad', Percent }], StageLoopStartIndex: 0 })).toThrow('Percent');
  });
  it('rejects fractional black-bar seconds', () => {
    expect(() => validatePlan({ ...plan(), Stages: [{ BlackBarLookaheadSeconds: 3.5 }], StageLoopStartIndex: 0 })).toThrow('BlackBarLookaheadSeconds');
  });
  it('rejects an out-of-range loop boundary', () => {
    expect(() => validatePlan({ ...plan(), StageLoopStartIndex: 3 })).toThrow('StageLoopStartIndex');
  });
  it('rejects an empty stage list rather than allowing C# to silently restore defaults', () => {
    expect(() => validatePlan({ ...plan(), Stages: [] })).toThrow('At least one stage');
  });
  it('rejects a gear goal that normalization would overwrite', () => {
    expect(() => validatePlan({ ...plan(), GearGoal: 5 })).toThrow('match the active GoalPresets');
  });
  it.each(['0', '-1', '6k', '1.5', '9223372036854775808', Number.MAX_SAFE_INTEGER + 1])('rejects invalid positive Int64 target %s', value => {
    expect(() => longTarget(value, 'AT power', true)).toThrow('AT power');
  });
  it.each(['NGU-9', 'AUG-14', 'AT-5', 'RIT', 'NGU-01', 'ngu', ' WAN', ''])('rejects invalid energy token %s', token => {
    expect(() => validatePriorities([token], true, 'Energy')).toThrow('Energy');
  });
  it.each(['NGU-7', 'RIT-8', 'AUG', 'AT', 'AUG-0'])('rejects invalid magic token %s', token => {
    expect(() => validatePriorities([token], false, 'Magic')).toThrow('Magic');
  });
});

describe('augment command arguments', () => {
  it('permits the live saved estimate or a positive Int32 override', () => {
    const args = { expectedSeconds: 2147483647 };
    validateAugmentArgs({});
    validateAugmentArgs(args);
    expect(args).toEqual({ expectedSeconds: 2147483647 });
  });
  it.each([0, -1, 1.5, NaN, Infinity, 2147483648, '300', null])('rejects an invalid estimate override: %s', expectedSeconds => {
    expect(() => validateAugmentArgs({ expectedSeconds })).toThrow('expectedSeconds');
  });
  it('rejects arguments the bridge does not accept', () => {
    expect(() => validateAugmentArgs({ seconds: 300 })).toThrow('only the optional expectedSeconds');
  });
});

describe('plan deletion safeguards', () => {
  const state = (): NguiState => ({ runtime_dir: 'A:/runtime', marker_enabled: true, connected: true, protocol: {}, settings: {}, runtime: { externalUiEnabled: true, status: 'running', updatedAt: new Date().toISOString(), protocolVersion: 3, supportedCommands: ['deletePlan'], settings: { customPlanFile: 'custom', useCustomRebirthPlan: true } }, profiles: [], plans: ['default', 'custom', 'other'], logs: [], command_results: [] });
  it('exposes exactly the current built-in catalog and labels', () => {
    expect(builtInPlans).toEqual([
      { name: '24hr', label: '24h NGU' },
      { name: '15m-NUMBER-increase', label: '15m NUMBER' },
      { name: '30m-NUMBER-increase', label: '30m NUMBER' },
      { name: '1h-1m-NUMBER-increase', label: '1h1m NUMBER' }
    ]);
  });
  it.each(['24hr', '15m-NUMBER-increase', '30m-NUMBER-increase', '1h-1m-NUMBER-increase'])('protects builtin %s case-insensitively even offline', name => {
    for (const variant of [name, name.toUpperCase(), name.toLowerCase()]) {
      expect(planDeletionReason({ ...state(), connected: false }, variant)).toContain('Built-in plans cannot be deleted');
    }
  });
  it.each(['default', 'DEFAULT', '1h-3m-rebirth', 'infinite', 'Infinite'])('allows deletion of retired builtin %s when other guards pass', name => {
    expect(planDeletionReason(state(), name)).toBe('');
    expect(planDeletionReason({ ...state(), connected: false }, name)).toBe('');
  });
  it('protects the actual running plan even if a pending patch would stop it', () => {
    expect(planDeletionReason(state(), 'CUSTOM', Date.now(), { _useCustomRebirthPlan: false })).toContain('active running plan');
  });
  it('allows a stopped active plan to be deleted', () => {
    const value = state();
    value.runtime.settings = { customPlanFile: 'custom', useCustomRebirthPlan: false };
    expect(planDeletionReason(value, 'custom')).toBe('');
  });
  it('requires the connected bridge to advertise deletion support', () => {
    const value = state();
    value.runtime.supportedCommands = [];
    expect(planDeletionReason(value, 'other')).toContain('does not support plan deletion');
  });
  it('allows offline deletion without advertised live commands', () => {
    const value = { ...state(), connected: false };
    value.runtime.supportedCommands = [];
    expect(planDeletionReason(value, 'custom')).toBe('');
  });
  it('protects the last remaining plan', () => {
    expect(planDeletionReason({ ...state(), plans: ['other'] }, 'other')).toContain('last plan');
  });
  it('blocks deleting a plan selected in an unsaved settings patch', () => {
    expect(planDeletionReason(state(), 'other', Date.now(), { _customPlanFile: 'other' })).toContain('pending selection');
  });
});

describe('stage ordering', () => {
  it('moves the loop boundary with its stage and preserves unknown stage fields', () => {
    const base = plan();
    (base.Stages as JsonObject[])[1].Future = 42;
    const moved = reorderStages(base, 1, 2);
    expect((moved.Stages as JsonObject[]).map(stage => stage.Name)).toEqual(['A', 'C', 'B']);
    expect(moved.StageLoopStartIndex).toBe(2);
    expect((moved.Stages as JsonObject[])[2].Future).toBe(42);
    expect(base.StageLoopStartIndex).toBe(1);
  });
  it('adjusts the boundary when a preceding stage is removed', () => {
    expect(reorderStages(plan(), 0, null).StageLoopStartIndex).toBe(0);
  });
  it('uses the successor when the loop start is deleted', () => {
    const next = reorderStages(plan(), 1, null);
    expect(next.StageLoopStartIndex).toBe(1);
    expect((next.Stages as JsonObject[])[1].Name).toBe('C');
  });
  it('disables looping when its last stage is deleted', () => {
    expect(reorderStages({ ...plan(), StageLoopStartIndex: 2 }, 2, null).StageLoopStartIndex).toBe(-1);
  });
  it('keeps no-loop mode during reordering', () => {
    expect(reorderStages({ ...plan(), StageLoopStartIndex: -1 }, 0, 2).StageLoopStartIndex).toBe(-1);
  });
});

describe('settings validation', () => {
  it.each([0, 900, 604800])('accepts Iron Pill saving seconds %s without an existing field', value => {
    expect(validateSettings({ _customIronPillSaveSeconds: value }, {})).toBeUndefined();
  });
  it.each([-1, 604801, 1.5, '', '900', NaN, Infinity, null])('rejects invalid Iron Pill saving seconds %s', value => {
    expect(() => validateSettings({ _customIronPillSaveSeconds: value }, {})).toThrow('_customIronPillSaveSeconds');
  });
  it.each([0, .125, 99999999.5, 1e8])('accepts Iron Pill worth %s', value => {
    expect(validateSettings({ _customIronPillMinWorth: value }, {})).toBeUndefined();
  });
  it.each([-1, 100000000.01, 100000001, 1e100, '', '1e8', NaN, Infinity, null])('rejects invalid Iron Pill worth %s', value => {
    expect(() => validateSettings({ _customIronPillMinWorth: value }, {})).toThrow('_customIronPillMinWorth');
  });
  it.each([[], null, [{ CastAtUtc: '2026-10-14T12:00:00Z' }]])('rejects backend-owned history in a settings patch: %s', value => {
    expect(() => validateSettings({ _customIronPillHistory: value }, { _customIronPillHistory: value })).toThrow('backend-owned');
  });
  it('does not turn a blank number into zero', () => {
    expect(() => parseNumber('', 'ratio')).toThrow('blank is not zero');
  });
  it('validates integer settings even when the old value is an integer-looking double', () => {
    expect(() => validateSettings({ _customAutoBoostCubePowerRatio: 1.5 }, {})).toThrow('integer');
    expect(() => validateSettings({ _customAutoBoostCubePowerRatio: 5 }, {})).toThrow('between 1 and 4');
  });
  it('allows large finite double thresholds', () => {
    expect(validateSettings({ _bloodNumberThreshold: 1e50 }, { _bloodNumberThreshold: 0 })).toBeUndefined();
  });
  it('rejects fractional integer array entries', () => {
    expect(() => validateSettings({ _priorityBoosts: [1.5] }, { _priorityBoosts: [] })).toThrow('integer');
  });
});

describe('heartbeat and command results', () => {
  afterEach(() => vi.useRealTimers());
  const state = (age: number): NguiState => ({ runtime_dir: 'A:/runtime', marker_enabled: true, connected: true, protocol: {}, settings: {}, runtime: { externalUiEnabled: true, status: 'running', updatedAt: new Date(10000 - age).toISOString(), protocolVersion: 3, supportedCommands: ['focusGoal', 'previewGear'] }, profiles: [], plans: [], logs: [], command_results: [] });
  it('requires a recent enabled heartbeat, not only a marker', () => {
    expect(heartbeat(state(5000), 10000).connected).toBe(true);
    expect(heartbeat(state(5001), 10000).connected).toBe(false);
    expect(heartbeat({ ...state(0), connected: false }, 10000).connected).toBe(false);
    expect(heartbeat(state(-2001), 10000).connected).toBe(false);
  });
  it.each(['failed', 'starting', 'unloaded', '', undefined])('rejects an unready runtime status: %s', status => {
    const runtime = { ...state(0).runtime };
    if (status === undefined) delete runtime.status;
    else runtime.status = status;
    const snapshot = { ...state(0), runtime };
    expect(heartbeat(snapshot, 10000).connected).toBe(false);
    expect(canCommand(snapshot, 'focusGoal', 10000)).toBe(false);
  });
  it('guards focus and preview with advertised protocol support', () => {
    expect(canCommand(state(0), 'focusGoal', 10000)).toBe(true);
    expect(canCommand(state(5001), 'previewGear', 10000)).toBe(false);
    expect(canCommand(state(0), 'optimizeGear', 10000)).toBe(false);
    expect(canCommand({ ...state(0), runtime: { ...state(0).runtime, protocolVersion: 1 } }, 'focusGoal', 10000)).toBe(false);
    expect(canCommand({ ...state(0), runtime: { ...state(0).runtime, protocolVersion: 2 } }, 'focusGoal', 10000)).toBe(false);
  });
  it.each(['LoadingSave', 'Quiescing', 'Unloaded', 'FutureLifecycle', '', null])('blocks ordinary commands during execution lifecycle %s without declaring transport disconnected', lifecycle => {
    const snapshot = state(0);
    snapshot.runtime.execution = { lifecycle };
    const commands = ['readPlan', 'savePlan', 'applySettingsPatch', 'focusGoal'];
    snapshot.runtime.supportedCommands = commands;
    expect(heartbeat(snapshot, 10000).connected).toBe(true);
    for (const command of commands) expect(canCommand(snapshot, command, 10000)).toBe(false);
  });
  it('admits ordinary commands after execution reaches Running', () => {
    const snapshot = state(0);
    snapshot.runtime.execution = { lifecycle: 'Running' };
    expect(canCommand(snapshot, 'focusGoal', 10000)).toBe(true);
  });
  it.each(['status', 'unload', 'restoreSaveTest'])('retains the injector quiescence exception for advertised %s only on a fresh bridge', command => {
    const snapshot = state(0);
    snapshot.runtime.execution = { lifecycle: 'LoadingSave' };
    expect(canCommand(snapshot, command, 10000)).toBe(false);
    snapshot.runtime.supportedCommands = [command];
    expect(canCommand(snapshot, command, 10000)).toBe(true);
    expect(canCommand(snapshot, command, 16000)).toBe(false);
    snapshot.runtime.protocolVersion = 2;
    expect(canCommand(snapshot, command, 10000)).toBe(false);
  });
  it('awaits a result and retains gear data', async () => {
    vi.useFakeTimers();
    const result = { id: 'id', command: 'previewGear', success: true, data: { itemIds: [1, 2], score: 42 } };
    const read = vi.fn().mockResolvedValueOnce(null).mockResolvedValueOnce(result);
    const pending = pollResult('id', 'previewGear', read);
    await vi.advanceTimersByTimeAsync(150);
    expect(await pending).toEqual(result);
    expect(read).toHaveBeenCalledTimes(2);
  });
  it('surfaces bridge failures', async () => {
    await expect(pollResult('id', 'manualRebirth', async () => ({ id: 'id', command: 'manualRebirth', success: false, message: 'Not ready' }))).rejects.toThrow('Not ready');
  });
  it('rejects mismatched identities', async () => {
    await expect(pollResult('id', 'focusGoal', async () => ({ id: 'wrong', command: 'focusGoal', success: true }))).rejects.toThrow('does not match');
  });
  it('times out without reporting queued as completed', async () => {
    vi.useFakeTimers();
    const pending = pollResult('id', 'focusGoal', async () => null, 300, 150);
    const assertion = expect(pending).rejects.toThrow('Outcome may be unknown');
    await vi.advanceTimersByTimeAsync(450);
    await assertion;
  });
  it('bounds a stalled result read by the same command timeout', async () => {
    vi.useFakeTimers();
    const pending = pollResult('id', 'focusGoal', () => new Promise(() => {}), 300);
    const assertion = expect(pending).rejects.toThrow('focusGoal timed out');
    await vi.advanceTimersByTimeAsync(300);
    await assertion;
  });
});
