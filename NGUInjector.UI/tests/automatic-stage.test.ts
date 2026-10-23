import { describe, expect, it } from 'vitest';
import { render } from 'svelte/server';
import { parseJson, stringifyJson, type JsonObject, type JsonValue } from '../src/lib/api';
import { validatePlan } from '../src/lib/model';
import { automaticStageLabel, stageLabel, stagePresetIndex, stagePresets } from '../src/lib/stage-presets';
import { parsePlanProgress, stageHistoryDetails } from '../src/lib/plan-progress';
import StageEditor from '../src/lib/StageEditor.svelte';
import { scheduleTiming } from '../src/lib/stage-duration';
import { activityDuration, activityKinds, matchingStageActivityTiming, parseActivityTiming } from '../src/lib/activity-timing';
import { matchingPlanProgress } from '../src/lib/stage-bar';
import ActivityTiming from '../src/lib/ActivityTiming.svelte';

const stage = (): JsonObject => parseJson('{"Name":"Saved custom","DurationSeconds":300,"Percent":10,"CompletionMode":0,"EnergyFocus":1,"MagicFocus":1,"GearGoal":5,"EnergyPriorities":["NGU-8","TM"],"MagicPriorities":["RIT-7"],"FightOnEntry":true,"TargetAdvancedTrainingPower":"9223372036854775807","Future":9007199254740993}') as JsonObject;
const plan = (row: JsonObject): JsonObject => ({ Version: 14, __externalUiRevision: 'a'.repeat(64), Stages: [row] });

describe('automatic activity timing', () => {
  function fixture() {
    const value: JsonObject = { ...plan({ ...stage(), ExecutionMode: 1 }), Version: 17, StageLoopStartIndex: -1 };
    const runtime: JsonObject = { schemaVersion: 1, owner: 'scheduled', planName: 'test', stageIndex: 0, stageEntry: 'generation:2', runId: 'run-one', active: true, status: 'running',
      activityTimingScope: 'run', activityTimingSemantics: 'exclusive-phase-wall-time-including-setup',
      activityTimes: activityKinds.map((kind, index) => ({ kind, elapsedSeconds: [61, 125, 0, 0, 0][index], active: kind === 'Augments' })) };
    const progress: JsonObject = { available: true, status: 'running', planName: 'test', planVersion: 17, planRevision: 'a'.repeat(64), schedulerMode: 'stages', activeStageIndex: 0,
      stageEntry: 'generation:2', stageElapsedSeconds: 210, stageProgress: .7, repeating: false, loopStartIndex: -1, combatPrep: runtime,
      stages: [{ index: 0, executionMode: 1, completionMode: 0, timingKind: 'elapsed', durationSeconds: 300, configuredDurationSeconds: 300, flexWeight: 10, energyFocus: 1, magicFocus: 1, gearGoal: 5, configuredEnergyPriorities: ['NGU-8', 'TM'], configuredMagicPriorities: ['RIT-7'], state: 'current' }] };
    return { value, runtime, progress };
  }
  it('renders actual canonical rows, inactive zeros and live activity without mutating or issuing actions', () => {
    const { value, runtime, progress } = fixture();
    const before = stringifyJson({ value, progress });
    let actions = 0;
    const timing = matchingStageActivityTiming(runtime, matchingPlanProgress(value, 'test', progress, true, false));
    expect(timing?.current).toEqual({ kind: 'Augments', elapsedSeconds: 125, active: true });
    const body = render(ActivityTiming, { props: { timing } }).body;
    for (const text of ['Actual activity time', 'This run only', 'AT', 'Augments', 'Wandoos', 'Not visited', 'Now: Augments / 2m 5s', 'including phase setup', 'not a forecast or training credit', 'Parallel Wandoos and NGUs', 'Excludes entry preparation and cleanup']) expect(body).toContain(text);
    expect(body).not.toMatch(/replan|counter|in memory|storage|injector session|polling|offline|cumulative/i);
    expect(body).not.toContain('data-kind="NGU"');
    expect(body).not.toContain('data-kind="Background"');
    const editor = render(StageEditor, { props: { plan: value, name: 'test', progress, connected: true, onchange: () => actions++, onstageaction: async () => { actions++; } } }).body;
    expect(editor).toContain('Now: Augments / 2m 5s');
    expect(actions).toBe(0);
    expect(stringifyJson({ value, progress })).toBe(before);
  });
  it.each([3, 4])('includes optional activity %s only after a visit or while active', index => {
    const { runtime } = fixture();
    const rows = runtime.activityTimes as JsonObject[];
    rows[1].active = false;
    rows[index].active = true;
    const body = render(ActivityTiming, { props: { timing: parseActivityTiming(runtime) } }).body;
    expect(body).toContain(`Now: ${activityKinds[index]} / 0s`);
    expect(body).toContain(`data-kind="${activityKinds[index]}"`);
    rows[index].active = false;
    rows[index].elapsedSeconds = .5;
    expect(render(ActivityTiming, { props: { timing: parseActivityTiming(runtime) } }).body).toContain('&lt;1s');
    expect(activityDuration(.5)).toBe('<1s');
  });
  it('shows all three required activities for a recorded zero run, but not for missing telemetry', () => {
    const { runtime } = fixture();
    runtime.activityTimes = activityKinds.map(kind => ({ kind, elapsedSeconds: 0, active: false }));
    const body = render(ActivityTiming, { props: { timing: parseActivityTiming(runtime) } }).body;
    expect(body.match(/<dd[^>]*>Not visited<\/dd>/g)).toHaveLength(3);
    for (const kind of ['AT', 'Augments', 'Wandoos']) expect(body).toContain(`data-kind="${kind}"`);
    expect(body).not.toContain('Now:');
    delete runtime.activityTimes;
    const missing = render(ActivityTiming, { props: { timing: parseActivityTiming(runtime) } }).body;
    expect(missing).toContain('Activity timing not recorded');
    expect(missing).not.toContain('<dd');
  });
  it.each<JsonValue>([-1, NaN, Infinity, -Infinity, '12', null, true, {}, [], parseJson('1e400')])('rejects malformed elapsed seconds %j rather than fabricating zeros', elapsedSeconds => {
    const { runtime } = fixture();
    (runtime.activityTimes as JsonObject[])[0].elapsedSeconds = elapsedSeconds;
    expect(parseActivityTiming(runtime)).toBeNull();
  });
  it.each(['missing', 'duplicate', 'order', 'unknown', 'boolean', 'multi-active', 'schema', 'scope', 'semantics', 'run'])('rejects invalid timing contract: %s', kind => {
    const { runtime } = fixture();
    const rows = runtime.activityTimes as JsonObject[];
    if (kind === 'missing') rows.pop();
    if (kind === 'duplicate') rows[2].kind = 'AT';
    if (kind === 'order') [rows[0], rows[2]] = [rows[2], rows[0]];
    if (kind === 'unknown') rows[4].kind = 'Other';
    if (kind === 'boolean') rows[0].active = 0;
    if (kind === 'multi-active') rows[0].active = true;
    if (kind === 'schema') runtime.schemaVersion = 2;
    if (kind === 'scope') runtime.activityTimingScope = 'stage';
    if (kind === 'semantics') delete runtime.activityTimingSemantics;
    if (kind === 'run') runtime.runId = ' ';
    expect(parseActivityTiming(runtime)).toBeNull();
    expect(render(ActivityTiming, { props: { timing: parseActivityTiming(runtime) } }).body).toContain('Activity timing not recorded');
  });
  it.each(['manual', 'other-plan', 'index', 'entry', 'previous-loop', 'missing-entry', 'stale', 'dirty', 'revision', 'ordered-stage', 'recovery'])('never attaches mismatched %s telemetry to an automatic stage', mismatch => {
    const { value, runtime, progress } = fixture();
    if (mismatch === 'manual') runtime.owner = 'manual';
    if (mismatch === 'other-plan') runtime.planName = 'other';
    if (mismatch === 'index') runtime.stageIndex = 1;
    if (mismatch === 'entry') runtime.stageEntry = '';
    if (mismatch === 'previous-loop') progress.stageEntry = 'generation:3';
    if (mismatch === 'missing-entry') delete progress.stageEntry;
    if (mismatch === 'revision') progress.planRevision = 'b'.repeat(64);
    if (mismatch === 'ordered-stage') (progress.stages as JsonObject[])[0].configuredDurationSeconds = 400;
    if (mismatch === 'recovery') runtime.cleanupRecoveryRequired = true;
    const mapped = matchingPlanProgress(value, 'test', progress, mismatch !== 'stale', mismatch === 'dirty');
    expect(matchingStageActivityTiming(runtime, mapped)).toBeNull();
    const body = render(StageEditor, { props: { plan: value, name: 'test', progress, connected: mismatch !== 'stale', dirty: mismatch === 'dirty', onchange: () => {} } }).body;
    expect(body).not.toContain('Now:');
    expect(body).not.toContain('class="timing-rows"');
    expect(body).toContain('Activity timing not recorded');
  });
  it.each([{ cleanupPending: true }, { cleanupRecoveryRequired: true }, { status: 'completed', active: false }, { status: 'cancelled', active: false }, { status: 'failed', active: true }])('keeps recorded time but suppresses Now and active badges during %j', changes => {
    const { runtime } = fixture();
    Object.assign(runtime, changes);
    const timing = parseActivityTiming(runtime);
    expect(timing?.current).toBeNull();
    expect(timing?.rows[1].elapsedSeconds).toBe(125);
    const body = render(ActivityTiming, { props: { timing } }).body;
    expect(body).not.toContain('Now:');
    expect(body).not.toContain('active-badge');
    expect(body).toContain('2m 5s');
  });
  it('keeps replan counters authoritative, freezes paused views and rejects a last run after scheduler advancement', () => {
    const { value, runtime, progress } = fixture();
    Object.assign(runtime, { replanCount: 2, phaseElapsedSeconds: 0, elapsedSeconds: 300, phase: 'Wandoos' });
    const rows = runtime.activityTimes as JsonObject[];
    rows[1].active = false;
    rows[2].active = true;
    rows[2].elapsedSeconds = 45;
    const read = () => matchingStageActivityTiming(runtime, matchingPlanProgress(value, 'test', progress, true, false));
    expect(read()?.rows.map(row => row.elapsedSeconds)).toEqual([61, 125, 45, 0, 0]);
    expect(read()?.current?.kind).toBe('Wandoos');
    progress.status = 'paused';
    expect(read()?.current).toBeNull();
    expect(read()?.rows.every(row => !row.active)).toBe(true);
    progress.status = 'running';
    progress.stageEntry = 'generation:3';
    expect(read()).toBeNull();
    expect(parseActivityTiming(runtime)?.rows[1].elapsedSeconds).toBe(125);
  });
});

describe('automatic stage execution mode', () => {
  it.each([undefined, 0, 1])('preserves optional adventure weights in execution mode %s', ExecutionMode => {
    for (const weight of [undefined, 0, .125, 1000000]) {
      const row = { ...stage(), ...(ExecutionMode === undefined ? {} : { ExecutionMode }), ...(weight === undefined ? {} : { AdventureDynamicWeight: weight }) };
      const value = plan(row);
      const before = stringifyJson(value);
      validatePlan(value);
      const readback = parseJson(before) as JsonObject;
      validatePlan(readback);
      expect(stringifyJson(readback)).toBe(before);
      expect(stringifyJson(value)).toBe(before);
      expect((readback.Stages as JsonObject[])[0].AdventureDynamicWeight).toBe(weight);
    }
  });

  it.each<JsonValue>([null, -1, 1000000.01, '0', '1', true, false, {}, [], NaN, Infinity, -Infinity, parseJson('1e400')])('rejects malformed adventure weight %j in every mode', AdventureDynamicWeight => {
    for (const ExecutionMode of [undefined, 0, 1]) {
      expect(() => validatePlan(plan({ ...stage(), ...(ExecutionMode === undefined ? {} : { ExecutionMode }), AdventureDynamicWeight }))).toThrow('Stage 1 AdventureDynamicWeight');
    }
  });

  it.each([false, true])('renders the automatic decimal adventure input with parent disabled=%s', disabled => {
    const value = plan({ ...stage(), ExecutionMode: 1, AdventureDynamicWeight: .125 });
    const before = stringifyJson(value);
    const body = render(StageEditor, { props: { plan: value, disabled, onchange: () => {} } }).body;
    const input = body.match(/<input[^>]*aria-label="Adventure weight"[^>]*>/)?.[0];
    expect(input).toContain('type="number"');
    expect(input).toContain('inputmode="decimal"');
    expect(input).toContain('min="0"');
    expect(input).toContain('max="1000000"');
    expect(input).toContain('step="any"');
    expect(input).toContain('value="0.125"');
    expect(input?.includes('disabled')).toBe(disabled);
    expect(stringifyJson(value)).toBe(before);
    expect(render(StageEditor, { props: { plan: plan({ ...stage(), ExecutionMode: 0, AdventureDynamicWeight: .125 }), onchange: () => {} } }).body).not.toContain('aria-label="Adventure weight"');
    expect(render(StageEditor, { props: { plan: plan({ ...stage(), ExecutionMode: 1 }), onchange: () => {} } }).body).toMatch(/aria-label="Adventure weight"[^>]*value="0"/);
  });

  it.each([undefined, 0, 1])('validates and roundtrips shipped/new mode %s without rewriting saved settings', ExecutionMode => {
    const row = stage();
    if (ExecutionMode !== undefined) row.ExecutionMode = ExecutionMode;
    const value = plan(row);
    const before = stringifyJson(value);
    validatePlan(value);
    const readback = parseJson(before) as JsonObject;
    validatePlan(readback);
    expect(stringifyJson(readback)).toBe(before);
    expect(stringifyJson(value)).toBe(before);
    expect(stageLabel(row)).toBe(ExecutionMode === 1 ? automaticStageLabel : 'Time Machine');
    expect(stagePresetIndex(row)).toBe(ExecutionMode === 1 ? -1 : 0);
  });

  it.each<JsonValue>([null, -1, 2, .5, '1', 'AutoAugmentsWandoos', false, {}, [], NaN, Infinity])('rejects unknown explicit execution mode %j', ExecutionMode => {
    expect(() => validatePlan(plan({ ...stage(), ExecutionMode }))).toThrow('ExecutionMode');
    expect(stagePresetIndex({ ...stage(), ExecutionMode })).toBe(-1);
  });

  it('rejects target completion for automatic stages rather than silently deleting targets', () => {
    const row = { ...stage(), ExecutionMode: 1, CompletionMode: 1, TargetAdvancedTrainingToughness: '6000' };
    const before = stringifyJson(row);
    expect(() => validatePlan(plan(row))).toThrow('time budget');
    expect(stringifyJson(row)).toBe(before);
  });

  it('keeps the shared policy registry unchanged and distinguishes every standard tuple from automatic mode', () => {
    expect(stagePresets).toHaveLength(8);
    stagePresets.forEach((preset, index) => {
      expect(stagePresetIndex({ ...preset, ExecutionMode: 0 })).toBe(index);
      expect(stagePresetIndex({ ...preset, ExecutionMode: 1 })).toBe(-1);
      expect(stageLabel({ ...preset, ExecutionMode: 1 })).toBe(automaticStageLabel);
    });
  });

  it('hides irrelevant controls, explains total elapsed budget, and preserves the complete input', () => {
    const value = plan({ ...stage(), ExecutionMode: 1 });
    const before = stringifyJson(value);
    const body = render(StageEditor, { props: { plan: value, onchange: () => {} } }).body;
    for (const text of ['Stage mode', automaticStageLabel, 'Total budget', 'Preparation counts toward the budget. Maximum 24 hours per automatic stage.']) expect(body).toContain(text);
    expect(body).toContain('title="Results are not guaranteed optimal."');
    expect(body).not.toContain('unused but kept for switching back');
    for (const control of ['Focus preset', 'Energy focus', 'Magic focus', 'Energy priorities', 'Magic priorities']) expect(body).not.toContain(`aria-label="${control}"`);
    expect(body).toContain('aria-label="Fight on entry"');
    expect(body).toContain('Entry preparation consumes the total elapsed budget.');
    expect(stringifyJson(value)).toBe(before);
    const standard = render(StageEditor, { props: { plan: plan(stage()), onchange: () => {} } }).body;
    expect(standard).toContain('aria-label="Energy priorities"');
    expect(standard).toContain('aria-label="Duration"');
  });

  it('reports automatic runtime budgets rather than preserved standard allocation fields', () => {
    const data: JsonObject = { available: true, status: 'running', planName: 'test', planVersion: 17, schedulerMode: 'stages', activeStageIndex: 0, stageProgress: .1, stageElapsedSeconds: 30,
      stages: [{ index: 0, executionMode: 1, completionMode: 0, timingKind: 'elapsed', durationSeconds: 300, energyFocus: 1, magicFocus: 1, gearGoal: 5, state: 'current' }] };
    const view = parsePlanProgress(data, true);
    expect(view.stages[0]).toMatchObject({ name: automaticStageLabel, automatic: true, timingKind: 'elapsed', completionMode: 'Total elapsed budget', gear: 'Automatic phase gear' });
    expect(stageHistoryDetails(view.stages[0])).toContain('Total elapsed budget (includes AT, planning and setup)');
  });
  it.each([false, true])('keeps controls conservative until the backend clears cleanup with recovery marker %s', cleanupRecoveryRequired => {
    const value = { ...plan({ ...stage(), ExecutionMode: 1 }), Version: 17, StageLoopStartIndex: -1 };
    const progress: JsonObject = { available: true, status: 'running', planName: 'test', planVersion: 17, schedulerMode: 'stages', activeStageIndex: 0, stageProgress: 1, scheduleComplete: true, stageEntry: 'entry', repeating: false, loopStartIndex: -1,
      cleanupRecoveryRequired, reason: 'Checkpoint write failed: file locked. Retrying automatically.',
      combatPrep: { schemaVersion: 1, active: !cleanupRecoveryRequired, status: 'failed', cleanupPending: !cleanupRecoveryRequired, cleanupRecoveryRequired },
      stages: [{ index: 0, executionMode: 1, completionMode: 0, timingKind: 'elapsed', durationSeconds: 300, configuredDurationSeconds: 300, flexWeight: 10, energyFocus: 1, magicFocus: 1, gearGoal: 5, configuredEnergyPriorities: ['NGU-8', 'TM'], configuredMagicPriorities: ['RIT-7'], state: 'current' }] };
    const before = stringifyJson(progress);
    const body = render(StageEditor, { props: { plan: value, name: 'test', progress, connected: true, stageActionReason: '', onstageaction: async () => {}, onchange: () => {} } }).body;
    expect(body).toContain(cleanupRecoveryRequired ? 'Reconciling: Checkpoint write failed: file locked. Retrying automatically.' : 'Cleanup pending: combat prep still owns equipment and resources.');
    expect(body).not.toContain('acknowledge');
    expect(body).not.toContain('Sustaining');
    expect(body).not.toContain('Schedule complete');
    expect(body).not.toContain('Skip current stage');
    for (const label of ['Restart current stage', 'Set as current stage']) {
      const button = body.match(new RegExp(`<button([^>]*)>${label}<\\/button>`))?.[1];
      expect(button).toBeDefined();
      expect(button).toContain('disabled');
    }
    expect(stringifyJson(progress)).toBe(before);
  });
});

describe('automatic stage effective budget limit', () => {
  const automatic = { ...stage(), ExecutionMode: 1, DurationSeconds: 0, Percent: 1 };
  const standard = { ...stage(), ExecutionMode: 0, DurationSeconds: 0, Percent: 1 };

  it.each([1, 86400])('accepts fixed budget %s without imposing a 24-hour whole-plan limit', DurationSeconds => {
    const value = { ...plan(automatic), StageDurationSeconds: 345600, Stages: [{ ...automatic, DurationSeconds }, { ...automatic, DurationSeconds: 86400 }, { ...standard, DurationSeconds: 172800 }] };
    const before = stringifyJson(value);
    expect(validatePlan(value)).toBeUndefined();
    expect(stringifyJson(value)).toBe(before);
  });
  it('rejects a fixed automatic stage above 24 hours even when the plan horizon is shorter', () => {
    expect(() => validatePlan({ ...plan(automatic), StageDurationSeconds: 1, Stages: [{ ...automatic, DurationSeconds: 86401 }] })).toThrow('Stage 1: automatic Augments + Wandoos effective stage budget');
    expect(validatePlan({ ...plan(standard), StageDurationSeconds: 172800, Stages: [{ ...standard, DurationSeconds: 172800 }] })).toBeUndefined();
  });
  it.each([
    { horizon: 172800, fixed: 0, autoWeight: 1, standardWeight: 1, valid: true },
    { horizon: 172801, fixed: 0, autoWeight: 1, standardWeight: 1, valid: false },
    { horizon: 259200, fixed: 86400, autoWeight: 1, standardWeight: 1, valid: true },
    { horizon: 259201, fixed: 86400, autoWeight: 1, standardWeight: 1, valid: false },
    { horizon: 115200, fixed: 0, autoWeight: 3, standardWeight: 1, valid: true },
    { horizon: 115201, fixed: 0, autoWeight: 3, standardWeight: 1, valid: false },
    { horizon: 172800, fixed: 172801, autoWeight: 100, standardWeight: 1, valid: true },
    { horizon: 0, fixed: 0, autoWeight: 1, standardWeight: 1, valid: true }
  ])('uses fixed reservations and nominal flexible weights: $horizon / $fixed / $autoWeight:$standardWeight', ({ horizon, fixed, autoWeight, standardWeight, valid }) => {
    const value = { ...plan(automatic), StageDurationSeconds: horizon, Stages: [{ ...automatic, Percent: autoWeight }, { ...standard, Percent: standardWeight }, ...(fixed ? [{ ...standard, DurationSeconds: fixed }] : [])] };
    const before = stringifyJson(value);
    if (valid) expect(validatePlan(value)).toBeUndefined();
    else expect(() => validatePlan(value)).toThrow('86400 seconds (24 hours), including flexible allocation');
    expect(stringifyJson(value)).toBe(before);
  });
  it('rejects fractional nominal overflow even when integer schedule rounding displays 24 hours', () => {
    const value = { ...plan(automatic), StageDurationSeconds: 172801, Stages: [automatic, standard] };
    expect(scheduleTiming(value).seconds[0]).toBe(86400);
    expect(() => validatePlan(value)).toThrow('effective stage budget');
  });
  it.each([
    { TimeTriggerEnabled: true, TimeTriggerSeconds: 172801, StageDurationSeconds: 3600, valid: false },
    { TimeTriggerEnabled: true, TimeTriggerSeconds: 172800, StageDurationSeconds: 999999, valid: true },
    { TimeTriggerEnabled: false, TimeTriggerSeconds: 999999, StageDurationSeconds: 172800, valid: true },
    { TimeTriggerEnabled: true, TimeTriggerSeconds: 0, StageDurationSeconds: 172800, valid: true },
    { TimeTriggerEnabled: false, TimeTriggerSeconds: 3600, StageDurationSeconds: 172801, valid: false }
  ])('uses only an enabled positive time trigger before the fallback: %j', ({ valid, ...timing }) => {
    const value = { ...plan(automatic), ...timing, AutoRebirthEnabled: false, Stages: [automatic, standard] };
    if (valid) expect(validatePlan(value)).toBeUndefined();
    else expect(() => validatePlan(value)).toThrow('effective stage budget');
  });
  it('excludes target stages from fixed reservations and flexible weights', () => {
    const value = { ...plan(automatic), StageDurationSeconds: 172801, Stages: [automatic, standard, { ...standard, CompletionMode: 1, DurationSeconds: 1000000, Percent: 1000000, TargetAdvancedTrainingToughness: '6000' }] };
    expect(() => validatePlan(value)).toThrow('effective stage budget');
    value.StageDurationSeconds = 172800;
    expect(validatePlan(value)).toBeUndefined();
  });
});
