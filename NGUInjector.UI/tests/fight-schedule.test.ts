import { describe, expect, it } from 'vitest';
import { normalizeFightSchedules, parseJson, requireCanonicalPlan, stringifyJson, type JsonObject, type NguiState } from '../src/lib/api';
import { assertFightScheduleReadback, canCommand, planCapabilitiesReady, reorderStages, validatePlan } from '../src/lib/model';
import { fightDurationSeconds } from '../src/lib/stage-duration';
import { matchingFightStatus, matchingPlanProgress } from '../src/lib/stage-bar';
import { applyStagePreset } from '../src/lib/stage-presets';

const schedule = (): JsonObject => ({ Id: 'stable-id_1', Anchor: 1, DelaySeconds: 180, IntervalSeconds: 180 });
const plan = (Stages: JsonObject[] = [{ FightSchedule: schedule() }]): JsonObject => ({ Version: 18, __externalUiRevision: 'a'.repeat(64), StageLoopStartIndex: -1, Stages });

describe('fight schedules', () => {
  it('migrates only absent fields and keeps IDs after reorder, preset changes and copy', () => {
    const original: JsonObject = { ...plan([{ FightOnEntry: true }, { FightOnEntry: true, FightSchedule: null }, { FightOnEntry: false }, {}]), Version: 17 };
    const migrated = normalizeFightSchedules(original);
    const rows = migrated.Stages as JsonObject[];
    expect(rows.map(row => row.FightSchedule)).toEqual([{ Id: 'legacy-entry-v1-0', Anchor: 0, DelaySeconds: 0, IntervalSeconds: 0 }, null, null, null]);
    expect(stringifyJson(migrated)).not.toContain('FightOnEntry');
    expect((original.Stages as JsonObject[])[0]).toEqual({ FightOnEntry: true });
    const moved = reorderStages(migrated, 0, 3);
    const copy = normalizeFightSchedules(parseJson(stringifyJson(moved)) as JsonObject);
    expect((copy.Stages as JsonObject[])[3].FightSchedule).toEqual(rows[0].FightSchedule);
    expect(applyStagePreset(rows[0], 0).FightSchedule).toEqual(rows[0].FightSchedule);
  });
  it('retains unknown schedule and surrounding numeric tokens exactly', () => {
    const value = parseJson('{"Version":17,"Future":1.234567890123456789,"Stages":[{"Future":9007199254740993,"FightSchedule":{"Id":"x","Anchor":0,"DelaySeconds":180,"IntervalSeconds":180,"Future":1e-400}}]}') as JsonObject;
    const normalized = normalizeFightSchedules(value);
    expect(stringifyJson(normalized)).toContain('"Future":1e-400');
    expect(stringifyJson(normalized)).toContain('9007199254740993');
    expect(stringifyJson(normalized)).toContain('1.234567890123456789');
    expect(normalizeFightSchedules(normalized)).toBe(normalized);
  });
  it.each(['Anchor', 'DelaySeconds', 'IntervalSeconds'])('strictly validates %s without coercion', field => {
    for (const value of [undefined, null, true, '0', -1, .5, 2147483648, Infinity, {}, []]) {
      const row = schedule(); if (value === undefined) delete row[field]; else row[field] = value;
      expect(() => validatePlan(plan([{ FightSchedule: row }]))).toThrow(field);
    }
    expect(() => validatePlan(plan([{ FightSchedule: { ...schedule(), Anchor: 2 } }]))).toThrow('Anchor');
    validatePlan(plan([{ FightSchedule: { ...schedule(), DelaySeconds: 2147483647, IntervalSeconds: 2147483647 } }]));
  });
  it.each(['', ' ', 'a.b', 'é', 'a'.repeat(129)])('rejects unsafe ID %s', Id => {
    expect(() => validatePlan(plan([{ FightSchedule: { ...schedule(), Id } }]))).toThrow('Id');
  });
  it('rejects duplicates, malformed legacy flags even with explicit null, and future versions', () => {
    expect(() => validatePlan(plan([{ FightSchedule: schedule() }, { FightSchedule: schedule() }]))).toThrow('unique');
    expect(() => normalizeFightSchedules(plan([{ FightOnEntry: 'true', FightSchedule: null }]))).toThrow('boolean');
    expect(() => normalizeFightSchedules({ ...plan(), Version: 19 })).toThrow('Version');
    expect(() => requireCanonicalPlan({ ...plan(), Version: 17 })).toThrow('version 18');
    expect(() => assertFightScheduleReadback(plan(), plan([{ FightSchedule: null }]))).toThrow('readback');
  });
  it('parses 3m delay and 3m repeat independently, with zero delay and explicit Off', () => {
    expect(fightDurationSeconds('3m', 0)).toBe(180);
    expect(fightDurationSeconds('3m', 0, true)).toBe(180);
    expect(fightDurationSeconds('0s', 180)).toBe(0);
    expect(fightDurationSeconds('3', 180, true)).toBe(180);
    validatePlan(plan([{ FightSchedule: null }]));
    validatePlan(plan([{ FightSchedule: { ...schedule(), IntervalSeconds: 0 } }]));
    for (const text of ['', '3.', '3.m', '-1s', '30%', '.5s', '2147483648s']) expect(() => fightDurationSeconds(text, 0)).toThrow();
    expect(() => fightDurationSeconds('0s', 180, true)).toThrow();
  });
  it('requires both capabilities and plan version 18 for all plan commands', () => {
    const state = { connected: true, runtime: { externalUiEnabled: true, status: 'running', protocolVersion: 3, updatedAt: new Date().toISOString(), stageTimingVersion: 1, fightScheduleVersion: 1, planVersion: 18, supportedCommands: ['readPlan', 'savePlan', 'createPlan', 'setPlanStage'] } } as unknown as NguiState;
    expect(planCapabilitiesReady(state)).toBe(true);
    for (const field of ['stageTimingVersion', 'fightScheduleVersion', 'planVersion']) for (const value of [null, '18', 0, ...(field === 'planVersion' ? [17] : [])]) {
      const old = { ...state, runtime: { ...state.runtime, [field]: value } };
      for (const command of ['readPlan', 'savePlan', 'createPlan', 'setPlanStage']) expect(canCommand(old, command)).toBe(false);
    }
  });
  it('renders only fresh matching schedule and entry telemetry, without estimating missing time', () => {
    const row = { FightSchedule: schedule(), CompletionMode: 0, DurationSeconds: 600, Percent: 100, EnergyFocus: 0, MagicFocus: 0, GearGoal: -1, EnergyPriorities: [], MagicPriorities: [] };
    const value = plan([row]);
    const progress: JsonObject = { available: true, status: 'running', planName: 'test', planVersion: 18, schedulerMode: 'stages', stageEntry: 'entry-1', activeStageIndex: 0, repeating: false, loopStartIndex: -1,
      stages: [{ index: 0, state: 'current', completionMode: 0, configuredDurationSeconds: 600, durationSeconds: 600, flexWeight: 100, timingKind: 'fixed', energyFocus: 0, magicFocus: 0, gearGoal: -1, configuredEnergyPriorities: [], configuredMagicPriorities: [] }],
      fightSchedule: { stageEntry: 'entry-1', scheduleId: 'stable-id_1', state: 'scheduled', nextFightAtSeconds: 180, remainingSeconds: 120, reason: '' } };
    const mapped = matchingPlanProgress(value, 'test', progress, true, false);
    expect(mapped).not.toBeNull();
    expect(matchingFightStatus(value, progress, mapped)).toBe('Next fight in 2m');
    const invalidTelemetry: JsonObject[] = [{ stageEntry: 'old' }, { scheduleId: 'other' }, { state: 'unknown' }, { remainingSeconds: null }, { nextFightAtSeconds: -1 }];
    for (const patch of invalidTelemetry) {
      expect(matchingFightStatus(value, { ...progress, fightSchedule: { ...(progress.fightSchedule as JsonObject), ...patch } }, mapped)).toBe('');
    }
    expect(matchingFightStatus(value, progress, matchingPlanProgress(value, 'test', progress, false, false))).toBe('');
    expect(matchingFightStatus(value, progress, matchingPlanProgress(value, 'test', progress, true, true))).toBe('');
    expect(matchingFightStatus(plan([{ ...row, FightSchedule: null }]), progress, mapped)).toBe('');
  });
});
