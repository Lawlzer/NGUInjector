import { afterEach, describe, expect, it, vi } from 'vitest';
import { render } from 'svelte/server';
import { LosslessNumber } from 'lossless-json';
import * as api from '../src/lib/api';
import { type JsonObject, type JsonValue, type NguiState } from '../src/lib/api';
import { combatPrepReconciliationReason, createController } from '../src/lib/controller';
import { object, validateCombatPrepArgs } from '../src/lib/model';
import { combatAtShare, combatExactZero, combatMetric, combatObserved, combatOs, combatPrepArgs, combatPrepCommands, combatPrepDefaults, combatPrepStartReason, combatResourceCost, combatSupportBudget, combatSupportUsage, combatTime, readCombatPrepPreview } from '../src/lib/combat-prep';
import CombatPrep from '../src/lib/CombatPrep.svelte';

const args = { durationSeconds: 7200, backgroundEnergyPercent: 3, backgroundMagicPercent: 3 };
function support(policy = 'phase'): JsonObject {
  return { beards: [{ id: 4, name: 'Wandoos beard' }], diggers: [{ id: 7, name: 'Wandoos digger', level: '9007199254740993', drain: 1.25e100 }],
    grossGoldPerSecond: 2.5e100, drainPerSecond: 1.25e100, netGoldPerSecond: 1.25e100, budgetFraction: .5, policy, wandoosBeardMultiplier: 2, wandoosDiggerMultiplier: 3 };
}
function candidate(id = 'full'): JsonObject {
  return { id, label: id === 'full' ? 'Full focus' : 'Background casual', os: 2, available: true, reason: '', augmentSeconds: 1800, atSeconds: 600, wandoosSeconds: 4800, backgroundSeconds: 0, atEnergyShare: .5, augmentIndex: 3,
    projected: { augmentMultiplier: 12, wandoosMultiplier: 5, combinedMultiplier: 60, bossAttack: api.parseJson('12345678901234567890.5'), bossDefense: 2000, energyAtLevel: '9223372036854775807', magicAtLevel: '9007199254740993', wandoosEnergyLevel: '200', wandoosMagicLevel: '300', goldRemaining: 5000 }, gainFactor: 3, energyFocusEquivalentSeconds: null, magicFocusEquivalentSeconds: 3600,
    phases: [{ kind: 'at', label: 'Verify AT', seconds: 600, gearGoal: 'AdvancedTraining', gearIds: [10, 20], gearNames: ['Owned hat', 'Owned ring'], energyAllocation: '50% energy AT', magicAllocation: 'No Wandoos magic until verified', atEnergyTarget: '9223372036854775807', atMagicTarget: '9007199254740993', support: support('current') },
      { kind: 'wandoos', label: 'Train Wandoos', seconds: 4800, support: support(), supportVariantKey: 'wandoos-dedicated' }] };
}
function preview(): JsonObject {
  return { schemaVersion: 1, previewId: 'preview-token', generatedAt: new Date().toISOString(), ...args, recommendedCandidateId: 'full', evaluationCount: 25, elapsedMilliseconds: 10, assumptions: ['Owned gear frozen; future levels fixed'], baseline: { augmentMultiplier: 2, wandoosMultiplier: 10, combinedMultiplier: 20, bossAttack: 100, bossDefense: 200 }, candidates: [candidate(), candidate('casual')] };
}
function responseOfBytes(bytes: number): JsonObject {
  const result: JsonObject = { id: 'cmd', command: 'previewCombatPrep', success: true, data: { combatPrepPreview: preview() }, padding: '' };
  result.padding = 'x'.repeat(bytes - new TextEncoder().encode(api.stringifyJson(result)).length);
  return result;
}
function setup() {
  let snapshot: NguiState = { runtime_dir: 'A:/runtime', marker_enabled: true, connected: true, settings: { Future: 42 }, protocol: {}, profiles: [], plans: [], logs: [], command_results: [], runtime: { externalUiEnabled: true, status: 'running', protocolVersion: 3, startedAt: 'session-one', updatedAt: new Date().toISOString(), supportedCommands: combatPrepCommands } };
  let command = '';
  const backend = { ...api, getState: vi.fn(async () => snapshot), storeRuntimeDir: vi.fn(),
    enqueueCommand: vi.fn(async (_dir: string, name: string, _args?: JsonObject) => { command = name; return 'cmd'; }),
    readCommandResult: vi.fn(async (): Promise<JsonObject | null> => ({ id: 'cmd', command, success: true, data: command === 'previewCombatPrep' ? { combatPrepPreview: preview() } : {} })),
    saveSettingsPatch: vi.fn(), savePlan: vi.fn() };
  const ui = createController(backend, async () => true, snapshot.runtime_dir);
  return { ui, backend, snapshot, replace: (next: NguiState) => { snapshot = next; } };
}
afterEach(() => vi.useRealTimers());

describe('combat prep argument validation', () => {
  it('uses local two-hour defaults and accepts numeric boundaries', () => {
    expect(combatPrepArgs(combatPrepDefaults())).toEqual(args);
    for (const durationSeconds of [1, 86400]) for (const percent of [0, 3.5, 100]) {
      expect(validateCombatPrepArgs('previewCombatPrep', { durationSeconds, backgroundEnergyPercent: percent, backgroundMagicPercent: percent })).toBeUndefined();
    }
  });
  it.each<JsonValue>([0, -1, 86401, .5, NaN, Infinity, -Infinity, '7200', null, true, [], {}, api.parseJson('1.0000000000000001')])('rejects invalid duration %j', value => {
    expect(() => validateCombatPrepArgs('previewCombatPrep', { ...args, durationSeconds: value })).toThrow();
  });
  it.each<JsonValue>([-1, 100.1, NaN, Infinity, -Infinity, '3', null, false, [], {}, api.parseJson('3.0000000000000000001')])('rejects invalid background percent %j', value => {
    for (const key of ['backgroundEnergyPercent', 'backgroundMagicPercent']) expect(() => validateCombatPrepArgs('previewCombatPrep', { ...args, [key]: value })).toThrow();
  });
  it.each(['previewCombatPrep', 'startCombatPrep', 'cancelCombatPrep'])('rejects extra or missing fields for %s', command => {
    const valid = command === 'previewCombatPrep' ? args : command === 'startCombatPrep' ? { previewId: 'p', candidateId: 'c' } : { runId: 'r' };
    expect(validateCombatPrepArgs(command, valid as JsonObject)).toBeUndefined();
    expect(() => validateCombatPrepArgs(command, { ...valid, extra: true } as JsonObject)).toThrow('accepts exactly');
    expect(() => validateCombatPrepArgs(command, {})).toThrow('accepts exactly');
  });
  it.each<JsonValue>(['', '   ', 'x'.repeat(129), 123, null, true, [], {}])('rejects malformed ID %j', value => {
    expect(() => validateCombatPrepArgs('startCombatPrep', { previewId: value, candidateId: 'c' })).toThrow();
    expect(() => validateCombatPrepArgs('startCombatPrep', { previewId: 'p', candidateId: value })).toThrow();
    expect(() => validateCombatPrepArgs('cancelCombatPrep', { runId: value })).toThrow();
  });
  it('accepts maximum ID length without rewriting tokens', () => {
    expect(validateCombatPrepArgs('startCombatPrep', { previewId: 'x'.repeat(128), candidateId: ' c ' })).toBeUndefined();
  });
});

describe('preview safety', () => {
  it('keeps lane opportunity costs separate and requires each explicit known flag', () => {
    const value: JsonObject = { energyResourceFocusEquivalentSeconds: 0, energyResourceFocusCostKnown: true, magicResourceFocusEquivalentSeconds: 1800, magicResourceFocusCostKnown: true, energyFocusEquivalentSeconds: 900, magicFocusEquivalentSeconds: 600 };
    expect(combatResourceCost(value, 'energy')).toBe('0s');
    expect(combatResourceCost(value, 'magic')).toBe('30m');
    for (const known of [undefined, false, null, 1, 'true']) {
      expect(combatResourceCost({ ...value, ...(known === undefined ? { energyResourceFocusCostKnown: null } : { energyResourceFocusCostKnown: known }) }, 'energy')).toBe('Unavailable');
    }
    expect(combatResourceCost({ ...value, magicResourceFocusEquivalentSeconds: null }, 'magic')).toBe('Unavailable');
    expect(combatResourceCost({}, 'energy')).toBe('Unavailable');
  });
  it('names Wandoos operating systems without inventing an OS for missing telemetry', () => {
    expect([0, 1, 2].map(combatOs)).toEqual(['98', 'MEH', 'XL']);
    for (const value of [undefined, null, -1, 3, .5, NaN, '0']) expect(combatOs(value)).toBe('Unavailable');
  });
  it('displays AT weights as complementary energy percentages', () => {
    expect(combatAtShare(.5)).toBe('50% AT-3 / 50% AT-4');
    expect(combatAtShare(.25)).toBe('25% AT-3 / 75% AT-4');
    expect(combatAtShare(0)).toBe('0% AT-3 / 100% AT-4');
    expect(combatAtShare(1)).toBe('100% AT-3 / 0% AT-4');
    expect(combatAtShare(api.parseJson('0.250000000000000001'))).toBe('25% AT-3 / 75% AT-4');
    for (const value of [undefined, null, -1, 2, NaN, Infinity, '0']) expect(combatAtShare(value)).toBe('Unavailable');
  });
  it('formats support budget fractions without inventing a budget for missing or invalid readings', () => {
    expect([0, .125, 1].map(combatSupportBudget)).toEqual(['0%', '12.5%', '100%']);
    expect(combatSupportBudget(new LosslessNumber('0.333333333333333333'))).toBe('33.33%');
    for (const value of [undefined, null, -1, 1.1, NaN, Infinity, '0', true, {}]) expect(combatSupportBudget(value)).toBe('Unavailable');
  });
  it.each([[25, 100, '25%'], [0, 100, '0%'], [150, 100, '150%'], [1e-8, 1, '1.000000e-6%']])('calculates observed drain %s / gross %s without imposing a policy bound', (drain, gross, expected) => {
    expect(combatSupportUsage(drain, gross)).toBe(expected);
  });
  it('calculates observed usage from lossless financials without changing their exact tokens', () => {
    const drain = new LosslessNumber('1.250000000000000001e100');
    const gross = new LosslessNumber('2.500000000000000002e100');
    expect(combatSupportUsage(drain, gross)).toBe('50%');
    expect(drain.value).toBe('1.250000000000000001e100');
    expect(gross.value).toBe('2.500000000000000002e100');
    expect(combatSupportUsage(new LosslessNumber('0'), gross)).toBe('0%');
  });
  it.each<JsonValue | undefined>([undefined, null, '', '0', false, [], {}, NaN, Infinity, -Infinity, -1, new LosslessNumber('1e999')])('keeps unknown or invalid observed financial %j unavailable, never zero', value => {
    expect(combatSupportUsage(value, 100)).toBe('Unavailable');
    expect(combatSupportUsage(0, value)).toBe('Unavailable');
  });
  it('keeps zero gross GPS and unrepresentable usage unavailable, including zero drain over zero gross', () => {
    expect(combatSupportUsage(0, 0)).toBe('Unavailable');
    expect(combatSupportUsage(1, 0)).toBe('Unavailable');
    expect(combatSupportUsage(new LosslessNumber('0'), new LosslessNumber('0'))).toBe('Unavailable');
    expect(combatSupportUsage(Number.MAX_VALUE, Number.MIN_VALUE)).toBe('Unavailable');
  });
  it('accepts additive phase support and older schema-1 previews without mutating or requiring support', () => {
    const value = preview();
    const before = api.stringifyJson(value);
    expect(readCombatPrepPreview({ data: { combatPrepPreview: value } }, args)).toBe(value);
    expect(api.stringifyJson(value)).toBe(before);
    const older = { ...value, candidates: [{ ...candidate(), phases: [{ kind: 'wandoos', label: 'Legacy Wandoos' }] }] };
    expect(readCombatPrepPreview({ data: { combatPrepPreview: older } }, args)).toBe(older);
  });
  it('preserves observed decimal strings without coercing missing values to zero', () => {
    for (const value of ['9223372036854775807', '9007199254740993', '12345678901234567890.5', '0', '000']) expect(combatObserved(value)).toBe(value);
    for (const value of [undefined, null, '', ' ', 'unknown', NaN, Infinity]) expect(combatObserved(value)).toBe('Unavailable');
    expect(combatObserved(0)).toBe('0');
  });
  it('verifies exact zero without rounding small nonzero allocations or assuming missing means zero', () => {
    for (const value of ['0', '000', '0.000']) expect(combatExactZero(value)).toBe(true);
    for (const value of ['1', '9007199254740993', `0.${'0'.repeat(400)}1`]) expect(combatExactZero(value)).toBe(false);
    for (const value of [undefined, null, '', ' ', 'unknown', 0, NaN, Infinity]) expect(combatExactZero(value)).toBeNull();
  });
  it('rejects mismatched options and oversized compact responses', () => {
    expect(() => readCombatPrepPreview({ data: { combatPrepPreview: { ...preview(), durationSeconds: 60 } } }, args)).toThrow('options do not match');
    expect(() => readCombatPrepPreview({ data: { combatPrepPreview: preview() }, padding: 'x'.repeat(65536) }, args)).toThrow('64 KB');
  });
  it.each([64, 256])('bounds the complete response at the advertised %i KiB including exact boundary', kib => {
    const protocol = { maxResultBytes: 256 * 1024, combatPrep: { maxPreviewBytes: kib * 1024 } };
    const result = responseOfBytes(kib * 1024);
    const before = api.stringifyJson(result);
    expect(new TextEncoder().encode(before)).toHaveLength(kib * 1024);
    expect(readCombatPrepPreview(result, args, protocol)).toBe(object(result.data).combatPrepPreview);
    expect(api.stringifyJson(result)).toBe(before);
    expect(() => readCombatPrepPreview(responseOfBytes(kib * 1024 + 1), args, protocol)).toThrow(`exceeds ${kib} KB`);
  });
  it('retains all twelve support-rich families above 64 KiB when native advertises 256 KiB', () => {
    const value = { ...preview(), recommendedCandidateId: 'family-0', candidates: Array.from({ length: 12 }, (_, i) => ({ ...candidate(`family-${i}`),
      phases: [{ kind: 'wandoos', label: 'Train Wandoos', support: { ...support(), diggers: Array.from({ length: 8 }, (_, id) => ({ id, name: 'Wandoos digger'.repeat(60), level: '9007199254740993', drain: 1e100 })) } }] })) };
    const result = { data: { combatPrepPreview: value } };
    const bytes = new TextEncoder().encode(api.stringifyJson(result)).length;
    expect(bytes).toBeGreaterThan(64 * 1024);
    expect(bytes).toBeLessThanOrEqual(256 * 1024);
    expect(readCombatPrepPreview(result, args, { maxResultBytes: 256 * 1024, combatPrep: { maxPreviewBytes: 256 * 1024 } }).candidates).toHaveLength(12);
    expect(() => readCombatPrepPreview(result, args, { maxResultBytes: 256 * 1024, combatPrep: { maxPreviewBytes: 64 * 1024 } })).toThrow('exceeds 64 KB');
  });
  it('counts UTF-8 bytes rather than string length and includes non-preview data', () => {
    const result = responseOfBytes(64 * 1024);
    result.padding = String(result.padding).slice(1) + '\u00e9';
    expect(api.stringifyJson(result)).toHaveLength(64 * 1024);
    expect(new TextEncoder().encode(api.stringifyJson(result))).toHaveLength(64 * 1024 + 1);
    expect(() => readCombatPrepPreview(result, args, { maxResultBytes: 256 * 1024, combatPrep: { maxPreviewBytes: 64 * 1024 } })).toThrow('exceeds 64 KB');
  });
  it.each<JsonValue>([0, -1, 1.5, NaN, Infinity, -Infinity, '262144', null, true, [], {}, 256 * 1024 + 1, api.parseJson('262144.000000000000000001')])('fails closed on invalid advertised combat limit %j', maxPreviewBytes => {
    expect(() => readCombatPrepPreview({ data: { combatPrepPreview: preview() } }, args, { maxResultBytes: 256 * 1024, combatPrep: { maxPreviewBytes } })).toThrow('limit metadata is invalid');
  });
  it.each<JsonValue | undefined>([undefined, 0, -1, 1.5, NaN, Infinity, -Infinity, '262144', null, false, [], {}, 64 * 1024])('requires a valid native overall ceiling for advertised limit: %j', maxResultBytes => {
    const protocol: JsonObject = { combatPrep: { maxPreviewBytes: 256 * 1024 }, ...(maxResultBytes === undefined ? {} : { maxResultBytes }) };
    expect(() => readCombatPrepPreview({ data: { combatPrepPreview: preview() } }, args, protocol)).toThrow('limit metadata is invalid');
  });
  it.each<JsonObject>([{}, { maxResultBytes: 256 * 1024 }, { maxResultBytes: 256 * 1024, combatPrep: {} }])('keeps missing feature metadata at the legacy 64 KiB limit: %j', protocol => {
    expect(() => readCombatPrepPreview(responseOfBytes(64 * 1024 + 1), args, protocol)).toThrow('exceeds 64 KB');
    const result = responseOfBytes(64 * 1024);
    expect(readCombatPrepPreview(result, args, protocol)).toBe(object(result.data).combatPrepPreview);
  });
  it('never overrides a smaller native overall ceiling in the legacy fallback', () => {
    expect(() => readCombatPrepPreview(responseOfBytes(32 * 1024 + 1), args, { maxResultBytes: 32 * 1024 })).toThrow('exceeds 32 KB');
  });
  it.each<JsonValue>([null, false, '256', [], 256 * 1024])('rejects malformed feature metadata instead of treating it as absent: %j', combatPrep => {
    expect(() => readCombatPrepPreview({ data: { combatPrepPreview: preview() } }, args, { maxResultBytes: 256 * 1024, combatPrep })).toThrow('limit metadata is invalid');
  });
  it('blocks stale, unavailable, unknown and wrong-total selections', () => {
    const value = preview();
    expect(combatPrepStartReason(value, 'full', combatPrepDefaults())).toBe('');
    expect(combatPrepStartReason(value, 'full', combatPrepDefaults(), Date.parse(String(value.generatedAt)) + 120000)).toContain('expired');
    expect(combatPrepStartReason(value, 'unknown', combatPrepDefaults())).toBe('Select an available candidate.');
    expect(combatPrepStartReason({ ...value, candidates: [{ ...candidate(), available: false }] }, 'full', combatPrepDefaults())).toBe('Select an available candidate.');
    expect(combatPrepStartReason({ ...value, candidates: [{ ...candidate(), atSeconds: 601 }] }, 'full', combatPrepDefaults())).toContain('total does not match');
  });
  it('formats lossless doubles for display and never formats unknown as zero', () => {
    expect(combatMetric(api.parseJson('12345678901234567890.5'))).toBe('1.234568e+19');
    for (const value of [undefined, null, NaN, Infinity, '0', api.parseJson('1e999')]) expect(combatMetric(value)).toBe('Unavailable');
    expect(combatMetric(0)).toBe('0');
    expect(combatTime(api.parseJson('1.0000000000000001'))).toBe('1s');
    for (const value of [undefined, null, NaN, Infinity, '0', api.parseJson('1e999')]) expect(combatTime(value)).toBe('Unavailable');
  });
  it.each([
    ['1.234567890123456789e125', '1.234568e+125'],
    ['-1.234567890123456789e125', '-1.234568e+125'],
    ['1.234567890123456789e-125', '1.234568e-125'],
    ['-0.00000123456789123456789', '-1.234568e-6'],
    ['1000000000', '1.000000e+9'],
    ['0.0009999999', '9.999999e-4']
  ])('uses compact scientific notation for %s without changing its token', (token, expected) => {
    const value = new LosslessNumber(token);
    expect(combatMetric(value)).toBe(expected);
    expect(combatMetric(Number(token))).toBe(expected);
    expect(value.value).toBe(token);
    expect(api.stringifyJson({ value })).toBe(`{"value":${token}}`);
  });
  it.each(['0', '0.001', '-0.001', '1.23456789123456789', '999999900'])('keeps ordinary %s values at at most seven significant digits', token => {
    const value = new LosslessNumber(token);
    const expected = Number(token).toLocaleString(undefined, { maximumSignificantDigits: 7 });
    expect(combatMetric(value)).toBe(expected);
    expect(combatMetric(Number(token))).toBe(expected);
    expect(combatMetric(value)).not.toContain('e');
    expect(value.value).toBe(token);
  });
  it.each([
    ['5.26096123456789123', '5s'], ['5.5', '6s'], ['59.5', '1m'],
    ['3599.5', '1h'], ['7200', '2h'], ['0.49', '0s'], ['0.5', '1s']
  ])('rounds displayed %s seconds to %s without changing the model', (token, expected) => {
    const value = new LosslessNumber(token);
    expect(combatTime(value)).toBe(expected);
    expect(combatTime(Number(token))).toBe(expected);
    expect(value.value).toBe(token);
    expect(api.stringifyJson({ value })).toBe(`{"value":${token}}`);
  });
  it('rejects invalid durations before rounding', () => {
    for (const value of [-.1, NaN, Infinity, -Infinity, null, undefined, '5.26', new LosslessNumber('-0.1'), new LosslessNumber('1e999')]) expect(combatTime(value)).toBe('Unavailable');
  });
});

describe('combat prep controller', () => {
  it.each<JsonObject>([
    { cleanupRecoveryReason: 'Checkpoint write failed: access denied.' },
    { cleanupRecoveryRequired: true, reason: 'Checkpoint write failed: access denied.' },
    { combatPrep: { cleanupRecoveryReason: 'Checkpoint write failed: access denied.' } }
  ])('reports the backend reconciliation blocker from %j', progress => {
    expect(combatPrepReconciliationReason(progress)).toBe('Reconciling: Checkpoint write failed: access denied.');
  });
  it('does not invent a reconciliation reason from invalid or missing telemetry', () => {
    expect(combatPrepReconciliationReason({ cleanupRecoveryRequired: true, reason: 42, cleanupRecoveryReason: '  ', combatPrep: { cleanupRecoveryReason: null } })).toBe('Reconciling.');
  });
  it.each(['combatPrep', 'planProgress'] as const)('blocks manual Start and Cancel during %s reconciliation without reconstructing a run', async source => {
    const { ui, backend, snapshot } = setup(); await ui.refresh(); await ui.previewCombatPrep();
    snapshot.runtime.combatPrep = { schemaVersion: 1, active: false, cleanupPending: false, status: 'failed', runId: 'retired-run' };
    const reason = 'Automatic recovery checkpoint could not be saved; check runtime directory permissions or file locks. Retrying automatically.';
    const recovery = { cleanupRecoveryRequired: true, cleanupRecoveryReason: reason, reason, cleanupAttemptCount: 0 };
    snapshot.runtime[source] = { ...object(snapshot.runtime[source]), ...recovery };
    const before = api.stringifyJson(snapshot.runtime);
    await ui.refresh(); await ui.startCombatPrep();
    expect(ui.current().error).toBe(`Reconciling: ${reason}`);
    await ui.runCommand('cancelCombatPrep', { runId: 'retired-run' });
    expect(ui.current().error).toBe(`Reconciling: ${reason}`);
    expect(backend.enqueueCommand.mock.calls.map(call => call[1])).toEqual(['previewCombatPrep']);
    expect(api.stringifyJson(snapshot.runtime)).toBe(before);
    expect(ui.current().combatPrepUncertainRunId).toBe('');
    expect(backend.savePlan).not.toHaveBeenCalled(); expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    ui.dispose();
  });
  it('retains failed cleanup ownership, blocks Start, and never replays cancellation while recovery is pending', async () => {
    const { ui, backend, snapshot } = setup(); await ui.refresh(); await ui.previewCombatPrep();
    snapshot.runtime.combatPrep = { schemaVersion: 1, active: true, cleanupPending: true, status: 'failed', owner: 'scheduled', runId: 'cleanup-run' };
    await ui.refresh(); await ui.startCombatPrep();
    expect(ui.current().error).toBe('Combat prep still owns equipment and resources; cleanup is pending.');
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(1);
    await ui.cancelCombatPrep(); await ui.refresh();
    expect(object(ui.current().snapshot?.runtime.combatPrep)).toMatchObject({ active: true, cleanupPending: true, status: 'failed', runId: 'cleanup-run' });
    await ui.cancelCombatPrep();
    expect(ui.current().error).toContain('already submitted');
    expect(backend.enqueueCommand.mock.calls.map(call => call[1])).toEqual(['previewCombatPrep', 'cancelCombatPrep']);
    expect(backend.savePlan).not.toHaveBeenCalled(); expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    ui.dispose();
  });
  it.each([
    { advertised: 64 * 1024, bytes: 96 * 1024, error: 'exceeds 64 KB' },
    { advertised: 256 * 1024, bytes: 96 * 1024, error: '' },
    { advertised: 256 * 1024, bytes: 256 * 1024 + 1, error: 'exceeds 256 KB' },
    { advertised: 256 * 1024 + .5, bytes: 16 * 1024, error: 'limit metadata is invalid' }
  ])('uses native metadata and the full envelope without replay: $advertised / $bytes', async ({ advertised, bytes, error }) => {
    const { ui, backend, snapshot } = setup();
    snapshot.protocol = { maxResultBytes: 256 * 1024, combatPrep: { maxPreviewBytes: advertised } };
    await ui.refresh();
    const result = responseOfBytes(bytes);
    backend.readCommandResult.mockResolvedValueOnce(result);
    await ui.previewCombatPrep();
    expect(ui.current().error).toBe(error ? `Combat prep response ${error}${error.includes('metadata') ? '. Reload the native UI.' : '.'}` : '');
    expect(ui.current().combatPrepPreview).toEqual(error ? null : object(result.data).combatPrepPreview);
    await ui.refresh();
    expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'previewCombatPrep', args);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    ui.dispose();
  });
  it('invalidates an existing preview token when a refresh exceeds the negotiated limit', async () => {
    const { ui, backend, snapshot } = setup();
    snapshot.protocol = { maxResultBytes: 256 * 1024, combatPrep: { maxPreviewBytes: 256 * 1024 } };
    await ui.refresh(); await ui.previewCombatPrep();
    backend.readCommandResult.mockResolvedValueOnce(responseOfBytes(256 * 1024 + 1));
    await ui.previewCombatPrep();
    expect(ui.current().error).toBe('Combat prep response exceeds 256 KB.');
    await ui.startCombatPrep();
    expect(ui.current().combatPrepPreview).toBeNull();
    expect(ui.current().combatPrepSelectedId).toBe('');
    expect(ui.current().error).toBe('previewId must be a nonempty string of at most 128 characters.');
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(2);
    expect(backend.enqueueCommand.mock.calls.map(call => call[1])).toEqual(['previewCombatPrep', 'previewCombatPrep']);
    ui.dispose();
  });
  it.each<[string, JsonObject]>([
    ['previewCombatPrep', { ...args, extra: true }],
    ['startCombatPrep', { previewId: 'preview-token', candidateId: 'full', extra: true }],
    ['cancelCombatPrep', { runId: 'actual-run', extra: true }]
  ])('rejects extra %s arguments before native IPC', async (command, payload) => {
    const { ui, backend } = setup(); await ui.refresh();
    await ui.runCommand(command, payload);
    expect(ui.current().error).toContain('accepts exactly');
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    ui.dispose();
  });
  it.each(['unavailable', 'wrong total'])('does not enqueue Start for a %s candidate', async reason => {
    const { ui, backend } = setup(); await ui.refresh();
    backend.readCommandResult.mockResolvedValueOnce({ id: 'cmd', command: 'previewCombatPrep', success: true, data: { combatPrepPreview: { ...preview(), candidates: [{ ...candidate(), ...(reason === 'unavailable' ? { available: false } : { atSeconds: 601 }) }] } } });
    await ui.previewCombatPrep(); await ui.startCombatPrep();
    expect(ui.current().error).toContain(reason === 'unavailable' ? 'available candidate' : 'total does not match');
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(1);
    ui.dispose();
  });
  it('previews without settings or plan writes and retains selection through refresh', async () => {
    const { ui, backend } = setup();
    await ui.refresh();
    ui.setSetting('_manageEnergy', false);
    await ui.previewCombatPrep();
    ui.selectCombatPrep('casual');
    await ui.refresh();
    expect(ui.current().combatPrepSelectedId).toBe('casual');
    expect(ui.current().combatPrepPreview?.previewId).toBe('preview-token');
    expect(ui.current().combatPrepDraft).toEqual(combatPrepDefaults());
    expect(ui.current().patch).toEqual({ _manageEnergy: false });
    expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'previewCombatPrep', args);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(ui.current().busy).toBe(false);
    ui.dispose();
  });
  it.each(['durationSeconds', 'backgroundEnergyPercent', 'backgroundMagicPercent'] as const)('invalidates preview on %s edits including away-and-back', async key => {
    const { ui } = setup();
    await ui.refresh(); await ui.previewCombatPrep();
    const before = ui.current().combatPrepDraft[key];
    ui.editCombatPrep(key, '1'); ui.editCombatPrep(key, before);
    expect(ui.current().combatPrepPreview).toBeNull();
    expect(ui.current().combatPrepSelectedId).toBe('');
    ui.dispose();
  });
  it.each(['900', '3600', '7200', '86400'])('keeps budget shortcut %s local without preview, Start or settings writes', async seconds => {
    const { ui, backend } = setup();
    await ui.refresh(); await ui.previewCombatPrep();
    // Start from a different budget so every shortcut exercises invalidation.
    ui.editCombatPrep('durationSeconds', '60');
    ui.editCombatPrep('durationSeconds', seconds);
    await ui.refresh();
    expect(ui.current().combatPrepDraft.durationSeconds).toBe(seconds);
    expect(ui.current().combatPrepPreview).toBeNull();
    expect(ui.current().combatPrepSelectedId).toBe('');
    expect(ui.current().patch).toEqual({});
    expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'previewCombatPrep', args);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    ui.dispose();
  });
  it.each(['folder', 'session'])('invalidates preview on %s change without resetting local inputs', async change => {
    const { ui, backend, snapshot, replace } = setup();
    await ui.refresh(); ui.editCombatPrep('backgroundEnergyPercent', '4');
    backend.readCommandResult.mockResolvedValueOnce({ id: 'cmd', command: 'previewCombatPrep', success: true, data: { combatPrepPreview: { ...preview(), backgroundEnergyPercent: 4 } } });
    await ui.previewCombatPrep();
    ui.selectCombatPrep('casual');
    replace({ ...snapshot, runtime_dir: change === 'folder' ? 'A:/new' : snapshot.runtime_dir, runtime: { ...snapshot.runtime, startedAt: change === 'session' ? 'session-two' : 'session-one' } });
    if (change === 'folder') await ui.changeRuntimeDir('A:/new'); else await ui.refresh();
    expect(ui.current().combatPrepPreview).toBeNull();
    expect(ui.current().combatPrepSelectedId).toBe('');
    expect(ui.current().combatPrepDraft).toEqual({ ...combatPrepDefaults(), backgroundEnergyPercent: '4' });
    ui.dispose();
  });
  it('discards a preview response after options change during the request', async () => {
    const { ui, backend } = setup(); await ui.refresh();
    let resolve!: (result: JsonObject) => void;
    backend.readCommandResult.mockImplementationOnce(() => new Promise(yes => { resolve = yes; }));
    const pending = ui.previewCombatPrep();
    await vi.waitFor(() => expect(backend.readCommandResult).toHaveBeenCalledOnce());
    ui.editCombatPrep('durationSeconds', '3600');
    resolve({ id: 'cmd', command: 'previewCombatPrep', success: true, data: { combatPrepPreview: preview() } });
    await pending;
    expect(ui.current().combatPrepPreview).toBeNull();
    expect(ui.current().busy).toBe(false);
    ui.dispose();
  });
  it.each(['unsupported', 'stale', 'disconnected', 'unidentified'])('gates requests on %s backend', async reason => {
    const { ui, backend, snapshot } = setup();
    if (reason === 'unsupported') snapshot.runtime.supportedCommands = [];
    if (reason === 'stale') snapshot.runtime.updatedAt = '2000-01-01T00:00:00Z';
    if (reason === 'disconnected') snapshot.connected = false;
    if (reason === 'unidentified') delete snapshot.runtime.startedAt;
    await ui.refresh(); await ui.previewCombatPrep();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(ui.current().error).toMatch(/Disconnected|fresh injector session/);
    ui.dispose();
  });
  it('starts only the chosen token and releases busy while runtime remains active', async () => {
    const { ui, backend, snapshot, replace } = setup(); await ui.refresh(); await ui.previewCombatPrep();
    ui.selectCombatPrep('casual');
    replace({ ...snapshot, runtime: { ...snapshot.runtime, combatPrep: { schemaVersion: 1, active: true, runId: 'live-run', status: 'running' } } });
    // Runtime is published only after the explicit Start request.
    await ui.startCombatPrep();
    expect(backend.enqueueCommand).toHaveBeenLastCalledWith('A:/runtime', 'startCombatPrep', { previewId: 'preview-token', candidateId: 'casual' });
    expect(ui.current().combatPrepPreview).toBeNull();
    expect(ui.current().busy).toBe(false);
    expect(object(ui.current().snapshot?.runtime.combatPrep).active).toBe(true);
    ui.dispose();
  });
  it('uses runtime run ID for cancellation, not selected candidate', async () => {
    const { ui, backend, snapshot } = setup(); await ui.refresh(); await ui.previewCombatPrep();
    ui.selectCombatPrep('casual');
    snapshot.runtime = { ...snapshot.runtime, combatPrep: { schemaVersion: 1, active: true, status: 'paused', runId: 'actual-run' } };
    await ui.refresh(); await ui.cancelCombatPrep();
    expect(backend.enqueueCommand).toHaveBeenLastCalledWith('A:/runtime', 'cancelCombatPrep', { runId: 'actual-run' });
    await ui.cancelCombatPrep();
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(2);
    expect(ui.current().error).toContain('already submitted');
    ui.dispose();
  });
  it('rejects arbitrary start and cancel IDs before native IPC', async () => {
    const { ui, backend } = setup(); await ui.refresh(); await ui.previewCombatPrep();
    await ui.runCommand('startCombatPrep', { previewId: 'wrong', candidateId: 'full' });
    expect(ui.current().error).toContain('token or selected');
    await ui.runCommand('cancelCombatPrep', { runId: 'preview-token' });
    expect(ui.current().error).toContain('active run ID');
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(1);
    ui.dispose();
  });
  it('allows an explicit Cancel retry after a definite backend rejection without automatic replay', async () => {
    const { ui, backend, snapshot } = setup();
    snapshot.runtime.combatPrep = { schemaVersion: 1, active: true, status: 'running', runId: 'actual-run' };
    await ui.refresh();
    backend.readCommandResult.mockResolvedValueOnce({ id: 'cmd', command: 'cancelCombatPrep', success: false, message: 'Run changed; retry from fresh telemetry.' });
    await ui.cancelCombatPrep();
    expect(ui.current().error).toBe('Run changed; retry from fresh telemetry.');
    expect(ui.current().combatPrepUncertainRunId).toBe('');
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(1);
    await ui.cancelCombatPrep();
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(2);
    ui.dispose();
  });
  it('invalidates the old token when a preview refresh fails', async () => {
    const { ui, backend } = setup(); await ui.refresh(); await ui.previewCombatPrep();
    backend.enqueueCommand.mockRejectedValueOnce(new Error('Preview unavailable'));
    await ui.previewCombatPrep();
    expect(ui.current().error).toBe('Preview unavailable');
    expect(ui.current().combatPrepPreview).toBeNull();
    ui.dispose();
  });
  it('spends the preview on an uncertain transport outcome and never replays Start', async () => {
    const { ui, backend } = setup(); await ui.refresh(); await ui.previewCombatPrep();
    backend.enqueueCommand.mockRejectedValueOnce(new Error('Transport lost'));
    await ui.startCombatPrep();
    expect(ui.current().error).toContain('Outcome may be unknown');
    expect(ui.current().combatPrepPreview).toBeNull();
    expect(ui.current().busy).toBe(false);
    await ui.startCombatPrep(); await ui.refresh();
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(2);
    ui.dispose();
  });
  it('bounds pending result polling and does not retry an uncertain Start', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup(); await ui.refresh(); await ui.previewCombatPrep();
    backend.readCommandResult.mockResolvedValue(null);
    const pending = ui.startCombatPrep();
    await vi.advanceTimersByTimeAsync(12500); await pending;
    expect(ui.current().error).toContain('Outcome may be unknown');
    expect(ui.current().busy).toBe(false);
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(2);
    ui.dispose();
  });
});

describe('combat prep component SSR', () => {
  function renderSupport(runtime: JsonObject, value = preview()): string {
    const { ui, snapshot } = setup();
    const state = { ...ui.current(), combatPrepPreview: value, snapshot: { ...snapshot, runtime: { ...snapshot.runtime, combatPrep: { schemaVersion: 1, phase: 'Wandoos', ...runtime } } } };
    const before = api.stringifyJson(state);
    const body = render(CombatPrep, { props: { state, onedit: vi.fn(), onselect: vi.fn(), onpreview: vi.fn(), onstart: vi.fn(), oncancel: vi.fn() } }).body.replace(/<!--[\s\S]*?-->/g, '');
    expect(api.stringifyJson(state)).toBe(before);
    ui.dispose();
    return body;
  }
  it('distinguishes allocation cost from Wandoos gain and renders scheduled metadata', () => {
    const body = renderSupport({ owner: 'scheduled', stageIndex: 0, replanCount: 0, replanReason: 'AT boundary' }, { ...preview(), candidates: [{ ...candidate(), resourceReferenceProfileKey: 'frozen-profile', energyResourceFocusEquivalentSeconds: 0, magicResourceFocusEquivalentSeconds: 1800, energyResourceFocusCostKnown: true, magicResourceFocusCostKnown: false, energyFocusEquivalentSeconds: 600 }] });
    expect(body).toMatch(/Energy allocation opportunity cost<\/dt><dd[^>]*>0s/);
    expect(body).toMatch(/Magic allocation opportunity cost<\/dt><dd[^>]*>Unavailable/);
    expect(body).toContain('Automatic plan stage');
    expect(body).toMatch(/Energy full-focus equivalent \(Wandoos dump gain\)<\/dt><dd[^>]*>10m<\/dd>/);
    expect(body).toMatch(/Plan stage<\/dt><dd[^>]*>1/);
    const older = renderSupport({ stageIndex: -1, replanCount: null, automatic: true, lastReplanReason: 'Unshipped alias' });
    for (const label of ['Plan stage', 'Automatic plan stage']) expect(older).not.toContain(label);
    expect(older).not.toContain('Unshipped alias');
  });
  it('uses scheduled-owner metadata and fixed rebirth deadline from the runtime contract', () => {
    const body = renderSupport({ owner: 'scheduled', planName: 'saved-plan', stageIndex: 2, replanCount: 1, replanReason: 'phase-boundary', deadlineRebirthSeconds: 900, automatic: false, lastReplanReason: 'Not authoritative' });
    for (const text of ['Automatic plan stage', 'saved-plan', 'Fixed deadline (rebirth elapsed)', '15m']) expect(body).toContain(text);
    expect(body).not.toContain('Not authoritative');
    expect(body).not.toContain('Explicit planner run');
  });
  it.each([false, true, null])('treats no required AT gate, including Wish 190, as not applicable with targets reached %s', atTargetsReached => {
    const body = renderSupport({ phase: 'Wandoos', atGateRequired: false, atTargetsReached, actual: { energyAtLevel: '0', magicAtLevel: '0' } });
    expect(body).toMatch(/AT targets verified<\/dt><dd[^>]*>Not applicable \(no AT gate\)/);
    expect(body).not.toContain('Wandoos magic held at zero');
  });
  it.each<[JsonObject, string]>([
    [{ atGateRequired: true, atTargetsReached: true }, 'Yes'],
    [{ atGateRequired: true, atTargetsReached: false }, 'No'],
    [{ atGateRequired: true, atTargetsReached: null }, 'Unavailable'],
    [{ atTargetsReached: true }, 'Yes'],
    [{ atTargetsReached: false }, 'No']
  ])('shows required verification or shipped gate-less metadata without inventing success: %j', (runtime, expected) => {
    expect(renderSupport(runtime)).toMatch(new RegExp(`AT targets verified<\\/dt><dd[^>]*>${expected}<\\/dd>`));
  });
  it.each([true, false])('shows cleanup ownership rather than completion even with active %s and a restored support receipt', active => {
    const body = renderSupport({ active, cleanupPending: true, status: 'failed', owner: 'scheduled', runId: 'cleanup-run', supportRestoration: { status: 'restored' } });
    for (const text of ['Cleanup pending', 'combat prep still owns equipment and resources', 'Status: failed']) expect(body).toContain(text);
    expect(body).toMatch(/<button[^>]*disabled[^>]*>Start selected<\/button>/);
    expect(body).not.toContain('Support was restored at run end');
  });
  it('shows inactive reconciliation with its persistence reason instead of idle or restored completion', () => {
    const body = renderSupport({ active: false, status: 'idle', runId: '', cleanupPending: false, cleanupRecoveryRequired: true, cleanupRecoveryReason: 'Checkpoint write failed: access denied. Retrying automatically.', cleanupAttemptCount: 0, supportRestoration: { status: 'restored' } });
    expect(body).toContain('Reconciling: Checkpoint write failed: access denied. Retrying automatically.');
    expect(body).toMatch(/<button[^>]*disabled[^>]*>Start selected<\/button>/);
    expect(body).toMatch(/<button[^>]*disabled[^>]*>Cancel active run<\/button>/);
    for (const text of ['Status: idle', 'Planner ready', 'Support was restored at run end', 'Cleanup pending', 'acknowledge', 'Restart current stage', 'Set as current stage', 'Skip to selected stage']) expect(body).not.toContain(text);
  });
  it('omits implementation diagnostics without losing actual or projected stats', () => {
    const body = renderSupport({ active: false, runId: 'diagnostic-run', replanCount: 3, replanReason: 'diagnostic-replan', cleanupAttemptCount: 2, actual: { bossAttack: 123 }, projected: { bossAttack: 456 } }, { ...preview(), candidates: [{ ...candidate(), resourceReferenceProfileKey: 'diagnostic-profile' }] });
    for (const label of ['Resource reference profile', 'Run ID', 'Boundary replans', 'Last replan reason', 'Cleanup attempts', 'Support variant', 'Gear IDs']) expect(body).not.toContain(label);
    for (const value of ['diagnostic-run', 'diagnostic-replan', 'diagnostic-profile', 'wandoos-dedicated', 'evaluations /', '(ID ']) expect(body).not.toContain(value);
    expect(body).toMatch(/Actual boss attack<\/dt><dd[^>]*>123<\/dd>/);
    expect(body).toMatch(/Run projected boss attack<\/dt><dd[^>]*>456<\/dd>/);
  });
  it('renders exact planned/observed support and its actual blocked domain and reason', () => {
    const body = renderSupport({ plannedSupport: support(), actualSupport: { ...support('current'), beards: [{ id: 0, name: 'Observed beard' }], diggers: [{ id: 1, name: 'Observed digger', level: '00042', drain: 0 }], netGoldPerSecond: -1e100 }, supportStatus: 'applied', blockedDomain: 'support', blockedReason: 'Digger selection changed by user' });
    const planned = body.match(/<section[^>]*aria-label="Planned current-phase support"[^>]*>([\s\S]*?)<\/section>/)?.[1] ?? '';
    const actual = body.match(/<section[^>]*aria-label="Actual observed support"[^>]*>([\s\S]*?)<\/section>/)?.[1] ?? '';
    for (const text of ['Dedicated phase support', 'Wandoos beard', 'Wandoos digger', 'Level 9007199254740993', '1.250000e+100', '2.500000e+100', '50%', 'Wandoos beard multiplier', 'Wandoos digger multiplier']) expect(planned).toContain(text);
    for (const text of ['Current support', 'Observed beard', 'Observed digger', 'Level 00042', 'Drain 0', '-1.000000e+100']) expect(actual).toContain(text);
    expect(actual).not.toMatch(/<li\b[^>]*>Wandoos digger(?:\s*\/|\s*<\/li>)/);
    expect(body).not.toContain('(ID ');
    expect(body).toContain('Planned phase support');
    expect(body).toContain('support blocked');
    expect(body).toContain('Digger selection changed by user');
  });
  it('separates the planned GPS limit from observed usage instead of displaying the validation budget as policy', () => {
    const body = renderSupport({ plannedSupport: { ...support(), budgetFraction: .75 }, actualSupport: { ...support('current'), budgetFraction: 1 } });
    const planned = body.match(/<section[^>]*aria-label="Planned current-phase support"[^>]*>([\s\S]*?)<\/section>/)?.[1] ?? '';
    const actual = body.match(/<section[^>]*aria-label="Actual observed support"[^>]*>([\s\S]*?)<\/section>/)?.[1] ?? '';
    expect(planned).toMatch(/<dt>GPS budget limit<\/dt><dd[^>]*>75%<\/dd>/);
    expect(planned).not.toContain('Share of gross GPS in use');
    expect(actual).toMatch(/<dt>Share of gross GPS in use<\/dt><dd[^>]*>50%<\/dd>/);
    expect(actual).not.toContain('100%');
    expect(actual).not.toContain('<dt>GPS budget limit');
    for (const panel of [planned, actual]) for (const financial of ['2.500000e+100', '1.250000e+100']) expect(panel).toContain(financial);
    const phases = [...body.matchAll(/<section[^>]*aria-label="Planned phase support"[^>]*>([\s\S]*?)<\/section>/g)];
    expect(phases).toHaveLength(4);
    for (const phase of phases) {
      expect(phase[1]).toContain('GPS budget limit');
      expect(phase[1]).not.toContain('Share of gross GPS in use');
    }
  });
  it('shows actual terminal support without claiming a historical setup return', () => {
    const body = renderSupport({ active: false, status: 'completed', plannedSupport: support(), actualSupport: { ...support('current'), beards: [{ id: 2, name: 'Minute rotation beard' }] }, supportRestoration: { status: 'restored', reason: 'Verified at run end' } });
    expect(body).not.toContain('Support was restored at run end');
    expect(body).toContain('Minute rotation beard');
    expect(body).toContain('Wandoos beard');
    expect(body).not.toContain('Verified at run end');
    expect(body).not.toContain('Support restoration');
    expect(body).not.toContain('role="alert"');
  });
  it.each<JsonObject>([
    { active: true, supportRestoration: { status: 'restored' } },
    { supportRestoration: { status: 'restored' } },
    { active: false, supportRestoration: { status: 'skipped', reason: 'Changed by user' } },
    { active: false }
  ])('does not claim verified end-of-run restoration without an inactive restored receipt: %j', runtime => {
    expect(renderSupport(runtime)).not.toContain('Support was restored at run end');
  });
  it.each(['plannedSupport', 'actualSupport'])('never substitutes other telemetry for missing %s', key => {
    const runtime: JsonObject = { plannedSupport: support(), actualSupport: support('current') };
    delete runtime[key];
    const body = renderSupport(runtime, { ...preview(), candidates: [{ ...candidate(), phases: [{ label: 'Older phase' }] }] });
    const label = key === 'plannedSupport' ? 'Planned current-phase support' : 'Actual observed support';
    const missing = body.match(new RegExp(`<section[^>]*aria-label="${label}"[^>]*>([\\s\\S]*?)<\\/section>`))?.[1] ?? '';
    expect(missing.match(/<dd[^>]*>\s*Unavailable\s*<\/dd>/g)).toHaveLength(9);
    expect(missing).not.toContain('9007199254740993');
    const status = body.match(/<dl[^>]*aria-label="Setup status"[^>]*>([\s\S]*?)<\/dl>/)?.[1] ?? '';
    expect(status.match(/<dd[^>]*>Unavailable<\/dd>/g)).toHaveLength(1);
    const phase = body.match(/<section[^>]*aria-label="Planned phase support"[^>]*>([\s\S]*?)<\/section>/)?.[1] ?? '';
    expect(phase.match(/<dd[^>]*>\s*Unavailable\s*<\/dd>/g)).toHaveLength(9);
  });
  it('distinguishes explicitly empty support selections and zero financials from unavailable fields', () => {
    const body = renderSupport({ actualSupport: { policy: 'other', beards: [], diggers: [], grossGoldPerSecond: 0, drainPerSecond: 0, netGoldPerSecond: 0, budgetFraction: 0 } });
    const actual = body.match(/<section[^>]*aria-label="Actual observed support"[^>]*>([\s\S]*?)<\/section>/)?.[1] ?? '';
    expect(actual.match(/<dd[^>]*>\s*None\s*<\/dd>/g)).toHaveLength(2);
    expect(actual.match(/<dd[^>]*>0<\/dd>/g)).toHaveLength(3);
    expect(actual).not.toContain('0%');
    expect(actual.match(/<dd[^>]*>Unavailable<\/dd>/g)).toHaveLength(4);
  });
  it.each([
    { reason: undefined, expected: '' }, { reason: null, expected: '' },
    { reason: '', expected: '' }, { reason: ' \t ', expected: '' },
    { reason: 'Waiting for gear', expected: 'Waiting for gear' }
  ])('renders only a nonempty runtime reason: $reason', ({ reason, expected }) => {
    const { ui, snapshot } = setup();
    const state = { ...ui.current(), snapshot: { ...snapshot, runtime: { ...snapshot.runtime, combatPrep: {
      schemaVersion: 1, active: true, status: 'running', phase: 'Wandoos', candidateLabel: 'Full focus',
      ...(reason === undefined ? {} : { reason })
    } } } };
    const before = api.stringifyJson(state);
    const body = render(CombatPrep, { props: { state, onedit: () => {}, onselect: () => {}, onpreview: async () => {}, onstart: async () => {}, oncancel: async () => {} } }).body.replace(/<!--[\s\S]*?-->/g, '');
    const runtimeBody = body.match(/<section[^>]*aria-label="Combat prep runtime"[^>]*>([\s\S]*?)<\/section>/)?.[1] ?? '';
    const reasonMarkup = runtimeBody.match(/<p\b[^>]*role="status"[^>]*>[\s\S]*?<\/p>([\s\S]*?)<section\b[^>]*aria-label="Actual activity timing"/)?.[1];
    expect(reasonMarkup?.replace(/<[^>]+>/g, '').trim()).toBe(expected);
    expect(runtimeBody).not.toMatch(/<p\b[^>]*>\s*Unavailable\s*<\/p>/);
    expect(api.stringifyJson(state)).toBe(before);
    ui.dispose();
  });
  it('keeps phase details collapsed while retaining exact targets and read-only actual runtime', async () => {
    const { ui } = setup(); await ui.refresh(); await ui.previewCombatPrep();
    const state = ui.current();
    state.snapshot!.runtime.combatPrep = { schemaVersion: 1, active: true, status: 'running', candidateLabel: 'Full focus', phase: 'Verify AT', runId: 'actual-run', atTargetsReached: false, actual: { bossAttack: 123 }, warnings: ['Waiting for gear'] };
    const before = api.stringifyJson(state);
    const body = render(CombatPrep, { props: { state, onedit: () => {}, onselect: () => {}, onpreview: async () => {}, onstart: async () => {}, oncancel: async () => {} } }).body;
    for (const text of ['Full focus', 'Background casual', 'AT energy share', '50% AT-3 / 50% AT-4', 'XL', 'Owned hat', 'Owned ring', '9223372036854775807', '9007199254740993', '1.234568e+19', 'Projected boss attack', 'Actual boss attack', '123', 'Waiting for gear']) expect(body.toLowerCase()).toContain(text.toLowerCase());
    const phases = [...body.matchAll(/<details([^>]*)><summary>Phases, gear, support &amp; level targets<\/summary>/g)];
    expect(phases).toHaveLength(2);
    for (const phase of phases) expect(phase[1]).not.toMatch(/\bopen\b/);
    expect(body).toContain('Energy full-focus equivalent');
    expect(body).toContain('Unavailable');
    expect(body).not.toContain('type="number" value="123"');
    expect(api.stringifyJson(state)).toBe(before);
    ui.dispose();
  });
  it('renders scientific boss stats and whole-second times while leaving exact telemetry and preview tokens unchanged', () => {
    const { ui, snapshot } = setup();
    const huge = new LosslessNumber('1.234567890123456789e125');
    const tiny = new LosslessNumber('1.234567890123456789e-125');
    const seconds = new LosslessNumber('5.26096123456789123');
    const projected = { ...object(candidate().projected), bossAttack: huge, bossDefense: tiny };
    const state = { ...ui.current(), combatPrepPreview: { ...preview(), candidates: [{ ...candidate(), atSeconds: seconds, projected }] },
      snapshot: { ...snapshot, runtime: { ...snapshot.runtime, combatPrep: { schemaVersion: 1, active: true, status: 'running', phase: 'AT', elapsedSeconds: seconds, phaseRemainingSeconds: 59.5,
        actual: { bossAttack: huge, bossDefense: tiny, energyAtLevel: '9223372036854775807', magicAtAllocation: '9007199254740993', wandoosMagicAllocation: '0' } } } } };
    const before = api.stringifyJson(state);
    const body = render(CombatPrep, { props: { state, onedit: () => {}, onselect: () => {}, onpreview: async () => {}, onstart: async () => {}, oncancel: async () => {} } }).body.replace(/<!--[\s\S]*?-->/g, '');
    expect(body).toContain('1.234568e+125');
    expect(body).toContain('1.234568e-125');
    expect(body).toMatch(/<dd[^>]*>5s<\/dd>/);
    expect(body).toMatch(/<dd[^>]*>1m<\/dd>/);
    expect(body).toContain('9223372036854775807');
    expect(body).toContain('9007199254740993');
    expect(body).not.toContain(huge.value);
    expect(body).not.toContain(tiny.value);
    expect(body).not.toContain(seconds.value);
    expect(api.stringifyJson(state)).toBe(before);
    expect(huge.value).toBe('1.234567890123456789e125');
    expect(seconds.value).toBe('5.26096123456789123');
    ui.dispose();
  });
  it('labels missing telemetry unavailable without claiming idle or zero progress', () => {
    const { ui } = setup();
    const body = render(CombatPrep, { props: { state: ui.current(), onedit: () => {}, onselect: () => {}, onpreview: async () => {}, onstart: async () => {}, oncancel: async () => {} } }).body;
    expect(body).toMatch(/role="status">Disconnected; run status unavailable\.<\/p>/);
    expect(body).not.toContain('Status: idle');
    expect(body).not.toContain('0s');
    ui.dispose();
  });

  function renderObserved(actual?: JsonObject, phase = 'AT'): string {
    const { ui, snapshot } = setup();
    const state = { ...ui.current(), snapshot: { ...snapshot, runtime: { ...snapshot.runtime, combatPrep: { schemaVersion: 1, active: true, status: 'running', phase, ...(actual ? { actual } : {}) } } } };
    const before = api.stringifyJson(state);
    const body = render(CombatPrep, { props: { state, onedit: () => {}, onselect: () => {}, onpreview: async () => {}, onstart: async () => {}, oncancel: async () => {} } }).body.replace(/<!--[\s\S]*?-->/g, '');
    expect(api.stringifyJson(state)).toBe(before);
    ui.dispose();
    return body;
  }
  function observedReadings(body: string): Record<string, string> {
    const readings = body.match(/<dl[^>]*aria-label="Observed training readings"[^>]*>([\s\S]*?)<\/dl>/)?.[1] ?? '';
    return Object.fromEntries([...readings.matchAll(/<dt[^>]*>(.*?)<\/dt>\s*<dd[^>]*>(.*?)<\/dd>/g)].map(match => [match[1], match[2]]));
  }
  it('renders exact observed levels and allocations with compact gold, augment progress and finite lane rates in collapsed details', () => {
    const body = renderObserved({
      energyAtLevel: '9223372036854775807', magicAtLevel: '9007199254740993',
      energyAtAllocation: '1234567890123456789', magicAtAllocation: '987654321012345678',
      wandoosEnergyLevel: '100000000000000001', wandoosMagicLevel: '200000000000000003',
      wandoosEnergyAllocation: '0', wandoosMagicAllocation: '0',
      energyWandoosLevelsPerSecond: 1.25, magicWandoosLevelsPerSecond: 0,
      gold: api.parseJson('12345678901234567890.5'), augmentIndex: 3,
      augmentLevel: '300000000000000007', upgradeLevel: '400000000000000009',
      augmentEnergyAllocation: '333333333333333333', upgradeEnergyAllocation: '444444444444444444'
    });
    expect(body).toMatch(/<details[^>]*aria-label="Observed training details"[^>]*><summary>/);
    expect(body.match(/<details[^>]*aria-label="Observed training details"[^>]*>/)?.[0]).not.toMatch(/\bopen\b/);
    expect(body).toContain('Both AT-3 (energy AT) and AT-4 (magic AT) consume energy, not magic.');
    expect(observedReadings(body)).toEqual({
      'Energy AT level (AT-3)': '9223372036854775807', 'Magic AT level (AT-4)': '9007199254740993',
      'Energy to energy AT (AT-3)': '1234567890123456789', 'Energy to magic AT (AT-4)': '987654321012345678',
      'Wandoos energy level': '100000000000000001', 'Wandoos magic level': '200000000000000003',
      'Wandoos energy allocation': '0', 'Wandoos magic allocation': '0',
      'Observed gold': '1.234568e+19', 'Augment index (zero-based)': '3',
      'Augment level': '300000000000000007', 'Upgrade level': '400000000000000009',
      'Energy to augment': '333333333333333333', 'Energy to upgrade': '444444444444444444',
      'Wandoos energy levels / second': '1.25', 'Wandoos magic levels / second': '0'
    });
    expect(body).toContain('Wandoos magic held at zero');
    expect(body).not.toContain('role="alert"');
  });
  it('keeps missing observed telemetry unavailable and never claims Wandoos magic is zero', () => {
    const body = renderObserved();
    const readings = observedReadings(body);
    expect(Object.keys(readings)).toHaveLength(16);
    expect(Object.values(readings)).toEqual(Array(16).fill('Unavailable'));
    expect(body).toMatch(/<p[^>]*role="alert"[^>]*>Cannot verify Wandoos magic is zero during AT[^<]*allocation unavailable/);
    expect(body).not.toContain('Wandoos magic held at zero');
    expect(body).not.toContain('Wandoos magic is not zero during AT');
  });
  it.each(['1', '9007199254740993', `0.${'0'.repeat(400)}1`])('warns about an observed nonzero Wandoos magic allocation during AT: %s', allocation => {
    const body = renderObserved({ wandoosMagicAllocation: allocation });
    expect(body).toMatch(/<p[^>]*role="alert"[^>]*>Wandoos magic is not zero during AT/);
    expect(body).not.toContain('Wandoos magic held at zero');
    expect(observedReadings(body)['Wandoos magic allocation']).toBe(allocation);
  });
  it('does not label Wandoos-phase allocations as an AT zero hold or violation', () => {
    const body = renderObserved({ wandoosMagicAllocation: '123', energyWandoosLevelsPerSecond: Infinity, magicWandoosLevelsPerSecond: null }, 'Wandoos');
    expect(body).not.toContain('Wandoos magic held at zero');
    expect(body).not.toContain('role="alert"');
    expect(observedReadings(body)['Wandoos energy levels / second']).toBe('Unavailable');
    expect(observedReadings(body)['Wandoos magic levels / second']).toBe('Unavailable');
  });
});
