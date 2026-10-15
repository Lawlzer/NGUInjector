import { describe, expect, it } from 'vitest';
import { cloneObject, parseJson, stringifyJson, type JsonObject } from '../src/lib/api';
import { parsePlanProgress } from '../src/lib/plan-progress';
import { matchingPlanProgress } from '../src/lib/stage-bar';
import { stageLabel } from '../src/lib/stage-presets';

function fixture(): { plan: JsonObject; data: JsonObject } {
  const common = { CompletionMode: 0, EnergyPriorities: [], MagicPriorities: [] };
  return {
    plan: {
      Name: 'Display name is not the filename', Version: 16, __externalUiRevision: 'a'.repeat(64),
      StageDurationSeconds: 600, StageLoopStartIndex: 1,
      Stages: [
        { ...common, Name: 'Same descriptor', Percent: 1, DurationSeconds: 120, EnergyFocus: 1, MagicFocus: 1, GearGoal: 5 },
        { ...common, Name: 'Same descriptor', Percent: 1, DurationSeconds: 0, EnergyFocus: 2, MagicFocus: 4, GearGoal: -1 },
        { ...common, Name: 'Same descriptor', Percent: 3, DurationSeconds: 0, EnergyFocus: 3, MagicFocus: 3, GearGoal: 12 }
      ]
    },
    data: {
      available: true, status: 'running', planName: 'selected-file', planVersion: 16,
      schedulerMode: 'time', activeStageIndex: 1, stageProgress: .25,
      elapsedSeconds: 999999, stageElapsedSeconds: 999999, cycle: 4, repeating: true, loopStartIndex: 1,
      stages: [
        { index: 0, durationSeconds: 120, completionMode: 0, energyFocus: 1, magicFocus: 1, gearGoal: 5, configuredEnergyPriorities: [], configuredMagicPriorities: [], energyPriorities: ['TM'], magicPriorities: ['TM'] },
        { index: 1, durationSeconds: 120, completionMode: 0, energyFocus: 2, magicFocus: 4, gearGoal: -1, configuredEnergyPriorities: [], configuredMagicPriorities: [] },
        { index: 2, durationSeconds: 360, completionMode: 0, energyFocus: 3, magicFocus: 3, gearGoal: 12, configuredEnergyPriorities: [], configuredMagicPriorities: [] }
      ]
    }
  };
}

function atFixture(): { plan: JsonObject; data: JsonObject } {
  const { plan, data } = fixture();
  Object.assign((plan.Stages as JsonObject[])[0], {
    CompletionMode: 1, DurationSeconds: 999, Percent: 999,
    TargetAdvancedTrainingPower: '9223372036854775806', TargetAdvancedTrainingToughness: '006000'
  });
  Object.assign((data.stages as JsonObject[])[0], {
    completionMode: 1, durationSeconds: 0,
    targetAdvancedTrainingPower: parseJson('9223372036854775806'), targetAdvancedTrainingToughness: 6000
  });
  // The remaining 600-second budget is split 1:3; the AT row is untimed.
  (data.stages as JsonObject[])[1].durationSeconds = 150;
  (data.stages as JsonObject[])[2].durationSeconds = 450;
  data.schedulerMode = 'sequential';
  data.activeStageIndex = 0;
  data.stageProgress = null;
  return { plan, data };
}

const match = (plan: JsonObject, data: JsonObject) => matchingPlanProgress(plan, 'selected-file', data, true, false);

describe('editor stage-bar runtime mapping', () => {
  it('returns exactly the parsed position, not a position inferred from elapsed time', () => {
    const { plan, data } = fixture();
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    expect(match(plan, data)).toMatchObject({ activeStageIndex: 1, stageProgress: .25, cycle: 4 });
    expect(match(plan, data)?.stages.map(stage => stage.state)).toEqual(['completed', 'current', 'future']);
  });

  it.each([[false, false], [true, true], [false, true]])('rejects fresh=%s dirty=%s', (connected, dirty) => {
    const { plan, data } = fixture();
    expect(matchingPlanProgress(plan, 'selected-file', data, connected, dirty)).toBeNull();
  });

  it('accepts an equal supplied revision only with the full stage comparison', () => {
    const { plan, data } = fixture();
    data.planRevision = plan.__externalUiRevision;
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    (data.stages as JsonObject[])[2].gearGoal = 0;
    expect(match(plan, data)).toBeNull();
  });

  it.each(['b'.repeat(64), '', null, 123, {}])('fails closed for a mismatched or malformed supplied revision %j', revision => {
    const { plan, data } = fixture();
    data.planRevision = revision;
    expect(match(plan, data)).toBeNull();
  });

  it('rejects a supplied revision when the editor revision is absent', () => {
    const { plan, data } = fixture();
    data.planRevision = plan.__externalUiRevision;
    delete plan.__externalUiRevision;
    expect(match(plan, data)).toBeNull();
  });

  it('supports revisionless telemetry through complete comparison, not a whole-config claim', () => {
    const { plan, data } = fixture();
    delete plan.__externalUiRevision;
    plan.AutoActivateFruits = false;
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    (data.stages as JsonObject[])[0].energyFocus = 0;
    expect(match(plan, data)).toBeNull();
  });

  it('uses the selected Windows filename, not plan or stage display names', () => {
    const { plan, data } = fixture();
    expect(matchingPlanProgress(plan, 'SELECTED-FILE', data, true, false)).toEqual(parsePlanProgress(data, true));
    expect(matchingPlanProgress(plan, String(plan.Name), data, true, false)).toBeNull();
    expect(matchingPlanProgress(plan, '', data, true, false)).toBeNull();
    data.planName = 'other-file';
    expect(match(plan, data)).toBeNull();
  });

  it('does not use the parser fallback name as a filename', () => {
    const { plan, data } = fixture();
    delete data.planName;
    expect(matchingPlanProgress(plan, 'Active plan', data, true, false)).toBeNull();
  });

  it.each([14, 15, 16])('supports understood version %i and the parser string alias', version => {
    const { plan, data } = fixture();
    plan.Version = version;
    // These weights also resolve to 120/120/360 under v14's percent-only timing.
    for (const reported of [version, String(version)]) {
      data.planVersion = reported;
      expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    }
  });

  it.each([13, 17, 999, null, 'future', 16.5])('rejects unknown or invalid schema %j even when both sides report it', version => {
    const { plan, data } = fixture();
    plan.Version = version;
    data.planVersion = version;
    expect(match(plan, data)).toBeNull();
  });

  it('rejects different understood versions', () => {
    const { plan, data } = fixture();
    data.planVersion = 15;
    expect(match(plan, data)).toBeNull();
  });

  it.each<JsonObject>([
    { energyFocus: 0 }, { magicFocus: 0 }, { gearGoal: 4 },
    { completionMode: 1 }, { completionMode: 2 }, { completionMode: '0' }, { completionMode: 'Time' },
    { durationSeconds: 121 }, { durationSeconds: '120' }, { durationSeconds: null },
    { configuredEnergyPriorities: ['TM'] }, { configuredMagicPriorities: ['TM'] }
  ])('rejects a semantic runtime field mismatch %j', patch => {
    const { plan, data } = fixture();
    Object.assign((data.stages as JsonObject[])[0], patch);
    expect(match(plan, data)).toBeNull();
  });

  it.each(['energyFocus', 'magicFocus', 'gearGoal', 'completionMode', 'durationSeconds', 'configuredEnergyPriorities', 'configuredMagicPriorities'])('requires runtime comparison field %s', key => {
    const { plan, data } = fixture();
    delete (data.stages as JsonObject[])[0][key];
    expect(match(plan, data)).toBeNull();
  });

  it.each(['EnergyFocus', 'MagicFocus', 'GearGoal', 'CompletionMode', 'EnergyPriorities', 'MagicPriorities'])('requires configured comparison field %s', key => {
    const { plan, data } = fixture();
    delete (plan.Stages as JsonObject[])[0][key];
    expect(match(plan, data)).toBeNull();
  });

  it.each<JsonObject>([
    { CompletionMode: 2 }, { EnergyFocus: 8 }, { MagicFocus: -1 }, { GearGoal: 14 },
    { EnergyFocus: '1' }, { EnergyPriorities: null }, { MagicPriorities: {} }
  ])('rejects malformed configured fields %j', patch => {
    const { plan, data } = fixture();
    Object.assign((plan.Stages as JsonObject[])[0], patch);
    expect(match(plan, data)).toBeNull();
  });

  it.each([
    ['EnergyPriorities', 'configuredEnergyPriorities', ['AT-0', 'WAN', 'AT-0']],
    ['MagicPriorities', 'configuredMagicPriorities', ['RIT-0', 'WAN', 'RIT-0']]
  ] as const)('compares ordered configured overrides including duplicates for %s', (key, reported, tokens) => {
    const { plan, data } = fixture();
    (plan.Stages as JsonObject[])[0][key] = [...tokens];
    const row = (data.stages as JsonObject[])[0];
    row[reported] = [...tokens];
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    expect(match(plan, data)?.stages[0].name).toBe('Time Machine');
    expect(stageLabel((plan.Stages as JsonObject[])[0])).toBe('Time Machine');
    for (const priorities of [tokens.slice(1), [tokens[1], tokens[0], tokens[2]], [...tokens, tokens[0]], [], null, {}]) {
      row[reported] = priorities;
      expect(match(plan, data)).toBeNull();
      expect(parsePlanProgress(data, true).stages[0].name).toBe('Time Machine');
    }
  });

  it('rejects malformed priority tokens even when both sides agree', () => {
    const { plan, data } = fixture();
    (plan.Stages as JsonObject[])[0].EnergyPriorities = ['tm'];
    (data.stages as JsonObject[])[0].configuredEnergyPriorities = ['tm'];
    expect(match(plan, data)).toBeNull();
  });

  it('ignores changing effective priorities and legacy stage descriptors', () => {
    const { plan, data } = fixture();
    for (const row of data.stages as JsonObject[]) {
      row.name = 'Not the configured descriptor';
      row.energyPriorities = ['AT'];
      row.magicPriorities = ['RIT-0'];
    }
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
  });

  it('rejects a reordered sequence despite duplicate names and equal first durations', () => {
    const { plan, data } = fixture();
    const rows = data.stages as JsonObject[];
    [rows[0], rows[1]] = [{ ...rows[1], index: 0 }, { ...rows[0], index: 1 }];
    expect(match(plan, data)).toBeNull();
  });

  it('maps semantically duplicate stages by reported index, never by name', () => {
    const { plan, data } = fixture();
    (plan.Stages as JsonObject[])[1] = { ...(plan.Stages as JsonObject[])[0], Name: 'Different descriptor' };
    (data.stages as JsonObject[])[1] = { ...(data.stages as JsonObject[])[0], index: 1 };
    expect(match(plan, data)?.stages.map(stage => stage.state)).toEqual(['completed', 'current', 'future']);
  });

  it.each<JsonObject>([
    { stages: [] }, { stages: [{ index: 0 }] }, { stages: [null] }, { available: false },
    { status: 'future' }, { activeStageIndex: 3 }, { activeStageIndex: null },
    { repeating: false }, { repeating: 'true' }, { loopStartIndex: 0 }, { schedulerMode: 'sequential' }
  ])('rejects partial or inconsistent telemetry %j', patch => {
    const { plan, data } = fixture();
    Object.assign(data, patch);
    expect(match(plan, data)).toBeNull();
  });

  it('rejects extra or renumbered runtime stages', () => {
    const { plan, data } = fixture();
    const rows = data.stages as JsonObject[];
    rows.push({ ...rows[0], index: 3 });
    expect(match(plan, data)).toBeNull();
    rows.pop();
    rows[2].index = 1;
    expect(match(plan, data)).toBeNull();
  });

  it.each(['repeating', 'loopStartIndex', 'schedulerMode', 'planVersion'])('requires top-level comparison field %s', key => {
    const { plan, data } = fixture();
    delete data[key];
    expect(match(plan, data)).toBeNull();
  });

  it.each([-1, 0, 2])('matches an explicit loop boundary %i', loop => {
    const { plan, data } = fixture();
    plan.StageLoopStartIndex = loop;
    data.loopStartIndex = loop;
    data.repeating = loop >= 0;
    expect(match(plan, data)?.stages.map(stage => stage.loopStart)).toEqual([0, 1, 2].map(index => index === loop));
  });

  it('requires explicit valid configured loop settings', () => {
    const { plan, data } = fixture();
    for (const loop of [-2, 3, '1', null]) {
      plan.StageLoopStartIndex = loop;
      expect(match(plan, data)).toBeNull();
    }
    delete plan.StageLoopStartIndex;
    expect(match(plan, data)).toBeNull();
  });

  it('rejects a nonrepeating report with a fabricated loop boundary', () => {
    const { plan, data } = fixture();
    plan.StageLoopStartIndex = -1;
    data.repeating = false;
    data.loopStartIndex = null;
    expect(match(plan, data)).toBeNull();
  });

  it.each(['paused', 'interrupted', 'disabled'])('preserves parser semantics when %s with no reported position', status => {
    const { plan, data } = fixture();
    data.status = status;
    data.activeStageIndex = -1;
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    expect(match(plan, data)).toMatchObject({ activeStageIndex: null, stageProgress: null });
    expect(match(plan, data)?.stages.map(stage => stage.state)).toEqual(['unknown', 'unknown', 'unknown']);
  });

  it('does not invent progress from the effective duration', () => {
    const { plan, data } = fixture();
    data.stageProgress = '0.5';
    expect(match(plan, data)).toMatchObject({ activeStageIndex: 1, stageProgress: null });
    data.stageProgress = 1;
    expect(match(plan, data)).toMatchObject({ activeStageIndex: 1, stageProgress: 1 });
  });

  it.each<JsonObject>([
    { StageDurationSeconds: 120 }, { StageDurationSeconds: '600' },
    { Stages: [null] }, { Stages: [] }, { Stages: {} }
  ])('catches invalid plan timing or stages %j', patch => {
    const { plan, data } = fixture();
    Object.assign(plan, patch);
    expect(match(plan, data)).toBeNull();
  });

  it('compares effective integer seconds, not raw percentages or dormant reference budgets', () => {
    const { plan, data } = fixture();
    (plan.Stages as JsonObject[]).forEach(row => { row.Percent = Number(row.Percent) * 1000; });
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    (plan.Stages as JsonObject[]).forEach((row, i) => { row.DurationSeconds = [120, 120, 360][i]; });
    plan.StageDurationSeconds = 1;
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    (plan.Stages as JsonObject[])[2].DurationSeconds = 359;
    expect(match(plan, data)).toBeNull();
  });
});

describe('AT stage-bar mapping', () => {
  it.each([1, 'advancedTraining'])('accepts completion alias %s with exact normalized Int64 targets', completion => {
    const { plan, data } = atFixture();
    (data.stages as JsonObject[])[0].completionMode = completion;
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    expect(match(plan, data)).toMatchObject({ activeStageIndex: 0, stageProgress: null, schedulerMode: 'sequential' });
  });

  it('accepts the supported time completion alias', () => {
    const { plan, data } = fixture();
    (data.stages as JsonObject[]).forEach(row => { row.completionMode = 'time'; });
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
  });

  it.each<JsonObject>([
    { targetAdvancedTrainingPower: '9223372036854775805' }, { targetAdvancedTrainingToughness: '6001' },
    { targetAdvancedTrainingPower: 9223372036854775806 }, { targetAdvancedTrainingPower: '9223372036854775808' },
    { targetAdvancedTrainingPower: '1e3' }, { targetAdvancedTrainingPower: '-1' },
    { targetAdvancedTrainingPower: {} }, { targetAdvancedTrainingPower: null }, { targetAdvancedTrainingPower: '0' },
    { targetAdvancedTrainingToughness: 6000.5 }, { targetAdvancedTrainingToughness: ' 6000' },
    { durationSeconds: null }, { durationSeconds: 999 }, { durationSeconds: '0' }, { completionMode: 'AT targets' }
  ])('rejects mismatched or malformed AT fields %j', patch => {
    const { plan, data } = atFixture();
    Object.assign((data.stages as JsonObject[])[0], patch);
    expect(match(plan, data)).toBeNull();
  });

  it.each(['targetAdvancedTrainingPower', 'targetAdvancedTrainingToughness', 'durationSeconds'])('requires AT runtime field %s', key => {
    const { plan, data } = atFixture();
    delete (data.stages as JsonObject[])[0][key];
    expect(match(plan, data)).toBeNull();
  });

  it.each(['TargetAdvancedTrainingPower', 'TargetAdvancedTrainingToughness'])('requires configured AT field %s', key => {
    const { plan, data } = atFixture();
    delete (plan.Stages as JsonObject[])[0][key];
    expect(match(plan, data)).toBeNull();
  });

  it('rejects an AT plan reported as a time scheduler', () => {
    const { plan, data } = atFixture();
    data.schedulerMode = 'time';
    expect(match(plan, data)).toBeNull();
  });

  it('ignores dormant AT target fields on time stages', () => {
    const { plan, data } = fixture();
    (plan.Stages as JsonObject[])[0].TargetAdvancedTrainingPower = '123';
    (data.stages as JsonObject[])[0].targetAdvancedTrainingPower = '456';
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
  });

  it('uses legacy sequential midpoint-to-even timing', () => {
    const { plan, data } = atFixture();
    plan.Version = 14;
    data.planVersion = '14';
    plan.StageDurationSeconds = 5;
    (plan.Stages as JsonObject[]).slice(1).forEach(row => { row.Percent = 1; });
    (data.stages as JsonObject[]).slice(1).forEach(row => { row.durationSeconds = 2; });
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    (data.stages as JsonObject[])[2].durationSeconds = 3;
    expect(match(plan, data)).toBeNull();
  });

  it('does not mutate frozen inputs or round lossless target tokens', () => {
    const { plan, data } = atFixture();
    const before = stringifyJson({ plan, data });
    const freeze = (value: unknown): void => {
      if (value && typeof value === 'object') {
        Object.values(value).forEach(freeze);
        Object.freeze(value);
      }
    };
    freeze(plan);
    freeze(data);
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    expect(stringifyJson({ plan, data })).toBe(before);
    expect(String((data.stages as JsonObject[])[0].targetAdvancedTrainingPower)).toBe('9223372036854775806');
    const changed = cloneObject(data);
    (changed.stages as JsonObject[])[0].targetAdvancedTrainingPower = parseJson('9223372036854775807');
    expect(match(plan, changed)).toBeNull();
    expect(stringifyJson({ plan, data })).toBe(before);
  });
});
