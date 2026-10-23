import { afterEach, describe, expect, it, vi } from 'vitest';
import { render } from 'svelte/server';
import * as api from '../src/lib/api';
import { cloneObject, parseJson, stringifyJson, type JsonObject } from '../src/lib/api';
import { createController, planStageOverrideReason, type UiState } from '../src/lib/controller';
import { parsePlanProgress } from '../src/lib/plan-progress';
import { matchingPlanProgress } from '../src/lib/stage-bar';
import { stageLabel } from '../src/lib/stage-presets';
import StageEditor from '../src/lib/StageEditor.svelte';

function fixture(): { plan: JsonObject; data: JsonObject } {
  const common = { CompletionMode: 0, EnergyPriorities: [], MagicPriorities: [] };
  return {
    plan: {
      Name: 'Display name is not the filename', Version: 17, __externalUiRevision: 'a'.repeat(64),
      StageDurationSeconds: 600, StageLoopStartIndex: 1,
      Stages: [
        { ...common, Name: 'Same descriptor', Percent: 1, DurationSeconds: 120, EnergyFocus: 1, MagicFocus: 1, GearGoal: 5 },
        { ...common, Name: 'Same descriptor', Percent: 1, DurationSeconds: 0, EnergyFocus: 2, MagicFocus: 4, GearGoal: -1 },
        { ...common, Name: 'Same descriptor', Percent: 3, DurationSeconds: 0, EnergyFocus: 3, MagicFocus: 3, GearGoal: 12 }
      ]
    },
    data: {
      available: true, status: 'running', planName: 'selected-file', planVersion: 17,
      schedulerMode: 'stages', activeStageIndex: 1, stageProgress: .25,
      elapsedSeconds: 999999, stageElapsedSeconds: 999999, cycle: 4, repeating: true, loopStartIndex: 1,
      stages: [
        { index: 0, state: 'completed', appliedSeconds: 120, elapsedSeconds: 150, visits: 1, durationSeconds: 120, configuredDurationSeconds: 120, flexWeight: 1, timingKind: 'fixed', completionMode: 0, energyFocus: 1, magicFocus: 1, gearGoal: 5, configuredEnergyPriorities: [], configuredMagicPriorities: [], energyPriorities: ['TM'], magicPriorities: ['TM'] },
        { index: 1, state: 'current', appliedSeconds: 30, elapsedSeconds: 40, visits: 2, durationSeconds: 120, configuredDurationSeconds: 0, flexWeight: 1, timingKind: 'flexible', completionMode: 0, energyFocus: 2, magicFocus: 4, gearGoal: -1, configuredEnergyPriorities: [], configuredMagicPriorities: [] },
        { index: 2, state: 'pending', appliedSeconds: 0, elapsedSeconds: 0, visits: 0, durationSeconds: 360, configuredDurationSeconds: 0, flexWeight: 3, timingKind: 'flexible', completionMode: 0, energyFocus: 3, magicFocus: 3, gearGoal: 12, configuredEnergyPriorities: [], configuredMagicPriorities: [] }
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
    completionMode: 1, durationSeconds: 0, configuredDurationSeconds: 999, flexWeight: 999, timingKind: 'target',
    targetAdvancedTrainingPower: parseJson('9223372036854775806'), targetAdvancedTrainingToughness: 6000
  });
  // The remaining 600-second budget is split 1:3; the AT row is untimed.
  (data.stages as JsonObject[])[1].durationSeconds = 150;
  (data.stages as JsonObject[])[2].durationSeconds = 450;
  data.activeStageIndex = 0;
  (data.stages as JsonObject[]).forEach((row, i) => row.state = i === 0 ? 'current' : 'pending');
  data.stageProgress = null;
  return { plan, data };
}

const match = (plan: JsonObject, data: JsonObject) => matchingPlanProgress(plan, 'selected-file', data, true, false);

function weightedFixture(presetId = '@active'): { plan: JsonObject; data: JsonObject } {
  const { plan, data } = fixture();
  plan.ActivePresetId = data.activePresetId = 'ngu';
  (plan.Stages as JsonObject[])[0].PresetId = presetId;
  (data.stages as JsonObject[]).forEach(row => Object.assign(row, { presetId: '', resolvedPresetId: '', presetName: '' }));
  Object.assign((data.stages as JsonObject[])[0], { presetId, resolvedPresetId: presetId === '@active' ? 'ngu' : presetId, presetName: 'Runtime NGU name' });
  return { plan, data };
}

describe('weighted stage runtime binding', () => {
  it.each(['ngu', '@active'])('matches %s using configured and resolved identity, not ignored legacy fields or editor names', binding => {
    const { plan, data } = weightedFixture(binding);
    const configured = (plan.Stages as JsonObject[])[0], runtime = (data.stages as JsonObject[])[0];
    configured.Name = 'Editor Time Machine';
    runtime.name = 'Retained Time Machine';
    runtime.energyFocus = 7; runtime.magicFocus = 4; runtime.gearGoal = 9;
    runtime.configuredEnergyPriorities = ['AT']; runtime.configuredMagicPriorities = ['RIT'];
    const before = stringifyJson({ plan, data });
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    expect(match(plan, data)?.stages[0]).toMatchObject({ name: 'Runtime NGU name', energy: 'Global weighted preset', magic: 'Global weighted preset', gear: 'Global weighted preset' });
    expect(stringifyJson({ plan, data })).toBe(before);
    const runtimeBefore = stringifyJson(data);
    for (const key of ['EnergyFocus', 'MagicFocus', 'GearGoal', 'EnergyPriorities', 'MagicPriorities']) delete configured[key];
    expect(match(plan, data)?.stages[0].name).toBe('Runtime NGU name');
    expect(stringifyJson(data)).toBe(runtimeBefore);
  });
  it.each<JsonObject>([
    { presetId: 'ngu' }, { presetId: '' }, { presetId: null },
    { resolvedPresetId: 'different' }, { resolvedPresetId: '' }, { resolvedPresetId: null },
    { presetName: '' }, { presetName: null }, { presetName: 123 }
  ])('rejects a mismatched or incomplete weighted binding even with a matching revision: %j', patch => {
    const { plan, data } = weightedFixture();
    data.planRevision = plan.__externalUiRevision;
    Object.assign((data.stages as JsonObject[])[0], patch);
    expect(match(plan, data)).toBeNull();
  });
  it.each(['presetId', 'resolvedPresetId', 'presetName', 'activePresetId', 'all'])('requires new metadata for weighted stages: missing %s', field => {
    const { plan, data } = weightedFixture();
    const runtime = (data.stages as JsonObject[])[0];
    if (field === 'activePresetId' || field === 'all') delete data.activePresetId;
    if (field === 'all') for (const key of ['presetId', 'resolvedPresetId', 'presetName']) delete runtime[key];
    else if (field !== 'activePresetId') delete runtime[field];
    expect(match(plan, data)).toBeNull();
  });
  it.each(['other', '', null, 123, '@active'])('rejects a changed or malformed plan active identity %j', activePresetId => {
    const { plan, data } = weightedFixture();
    data.activePresetId = activePresetId;
    expect(match(plan, data)).toBeNull();
  });
  it('rejects a changed inherited target even when the runtime root and row agree with each other', () => {
    const { plan, data } = weightedFixture();
    data.activePresetId = 'other';
    (data.stages as JsonObject[])[0].resolvedPresetId = 'other';
    expect(match(plan, data)).toBeNull();
  });
  it('checks explicit references independently of the plan active selection', () => {
    const { plan, data } = weightedFixture('explicit');
    expect(match(plan, data)?.stages[0].name).toBe('Runtime NGU name');
    (data.stages as JsonObject[])[0].resolvedPresetId = 'ngu';
    expect(match(plan, data)).toBeNull();
  });
  it('allows an explicit weighted binding with an empty plan active selection', () => {
    const { plan, data } = weightedFixture('explicit');
    plan.ActivePresetId = data.activePresetId = '';
    expect(match(plan, data)?.stages[0].name).toBe('Runtime NGU name');
  });
  it('never maps an editor inline stage onto a runtime weighted stage with an identical legacy tuple', () => {
    const { plan, data } = weightedFixture('ngu');
    delete (plan.Stages as JsonObject[])[0].PresetId;
    expect(match(plan, data)).toBeNull();
  });
  it('keeps metadata-free matching only for legacy inline stages', () => {
    const { plan, data } = fixture();
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    data.activePresetId = plan.ActivePresetId = 'ngu';
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    (plan.Stages as JsonObject[])[0].PresetId = '@active';
    expect(match(plan, data)).toBeNull();
  });
  it.each<JsonObject>([
    { configuredDurationSeconds: 121 }, { flexWeight: 2 }, { timingKind: 'flexible' },
    { completionMode: 1 }, { durationSeconds: null }
  ])('continues checking weighted timing and completion metadata: %j', patch => {
    const { plan, data } = weightedFixture();
    Object.assign((data.stages as JsonObject[])[0], patch);
    expect(match(plan, data)).toBeNull();
  });
  it('rejects reordering weighted bindings even when legacy tuples and timing are identical', () => {
    const { plan, data } = weightedFixture('ngu');
    const configured = plan.Stages as JsonObject[], runtime = data.stages as JsonObject[];
    configured[1] = { ...configured[0], PresetId: 'other' };
    runtime[1] = { ...runtime[0], index: 1, state: 'current', presetId: 'other', resolvedPresetId: 'other' };
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    [runtime[0], runtime[1]] = [{ ...runtime[1], index: 0, state: 'completed' }, { ...runtime[0], index: 1, state: 'current' }];
    expect(match(plan, data)).toBeNull();
  });
});

describe('attributed scheduled preset blockers', () => {
  function blockedFixture() {
    const { plan, data } = weightedFixture();
    data.planRevision = plan.__externalUiRevision;
    data.activeStageIndex = 0;
    Object.assign((data.stages as JsonObject[])[0], { state: 'current', presetStatus: 'failed', presetReason: 'Use Preview + Apply first.' });
    (data.stages as JsonObject[])[1].state = 'pending';
    return { plan, data };
  }

  it.each(['failed', 'blocked', 'waiting'])('renders the matching current preset %s reason without actions', presetStatus => {
    const { plan, data } = blockedFixture();
    (data.stages as JsonObject[])[0].presetStatus = presetStatus;
    const before = stringifyJson({ plan, data }), onchange = vi.fn();
    const expected = `Preset ${presetStatus}: Use Preview + Apply first.`;
    const body = render(StageEditor, { props: { plan, name: 'selected-file', progress: data, connected: true, onchange } }).body;
    expect(body).toContain(`role="status">${expected}</span>`);
    expect(body).toContain(`aria-valuetext="${expected}"`);
    expect(onchange).not.toHaveBeenCalled();
    expect(stringifyJson({ plan, data })).toBe(before);
  });

  it.each(['offline', 'dirty', 'other plan', 'revision', 'binding', 'timing', 'paused', 'cleanup', 'recovery'])('does not leak a preset blocker through the %s gate', scenario => {
    const { plan, data } = blockedFixture();
    if (scenario === 'other plan') data.planName = 'other';
    if (scenario === 'revision') data.planRevision = 'b'.repeat(64);
    if (scenario === 'binding') (data.stages as JsonObject[])[0].resolvedPresetId = 'other';
    if (scenario === 'timing') (data.stages as JsonObject[])[0].configuredDurationSeconds = 121;
    if (scenario === 'paused') data.status = 'paused';
    if (scenario === 'cleanup') data.combatPrep = { schemaVersion: 1, cleanupPending: true };
    if (scenario === 'recovery') data.cleanupRecoveryRequired = true;
    const body = render(StageEditor, { props: { plan, name: 'selected-file', progress: data, connected: scenario !== 'offline', dirty: scenario === 'dirty', onchange: vi.fn() } }).body;
    expect(body).not.toContain('Use Preview + Apply first.');
  });
});

describe('editor stage-bar runtime mapping', () => {
  it.each([0, 1])('compares lower-camel adventure weight to configured weight in mode %s', executionMode => {
    const { plan, data } = fixture();
    const row = (plan.Stages as JsonObject[])[0], runtime = (data.stages as JsonObject[])[0];
    row.ExecutionMode = runtime.executionMode = executionMode;
    if (executionMode === 1) runtime.timingKind = 'elapsed';
    data.planRevision = plan.__externalUiRevision;
    for (const weight of [0, .125, 1000000]) {
      row.AdventureDynamicWeight = runtime.adventureDynamicWeight = weight;
      const before = stringifyJson({ plan, data });
      expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
      expect(stringifyJson({ plan, data })).toBe(before);
      runtime.adventureDynamicWeight = weight + .5;
      expect(match(plan, data)).toBeNull();
    }
    row.AdventureDynamicWeight = 0;
    delete runtime.adventureDynamicWeight;
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    delete row.AdventureDynamicWeight;
    runtime.adventureDynamicWeight = 0;
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    delete runtime.adventureDynamicWeight;
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    row.AdventureDynamicWeight = .125;
    runtime.AdventureDynamicWeight = .125;
    expect(match(plan, data)).toBeNull();
  });

  it.each([null, -1, 1000000.01, '0', true, false, {}, [], NaN, Infinity, -Infinity, parseJson('1e400')])('fails closed for malformed adventure weight %j even when both sides agree', weight => {
    for (const executionMode of [0, 1]) {
      const { plan, data } = fixture();
      const row = (plan.Stages as JsonObject[])[0], runtime = (data.stages as JsonObject[])[0];
      row.ExecutionMode = runtime.executionMode = executionMode;
      if (executionMode === 1) runtime.timingKind = 'elapsed';
      row.AdventureDynamicWeight = weight;
      runtime.adventureDynamicWeight = 0;
      expect(match(plan, data)).toBeNull();
      row.AdventureDynamicWeight = 0;
      runtime.adventureDynamicWeight = weight;
      expect(match(plan, data)).toBeNull();
      row.AdventureDynamicWeight = weight;
      expect(match(plan, data)).toBeNull();
    }
  });

  it('matches automatic elapsed-budget telemetry without confusing the saved standard tuple', () => {
    const { plan, data } = fixture();
    const row = (plan.Stages as JsonObject[])[1];
    const runtime = (data.stages as JsonObject[])[1];
    row.ExecutionMode = 1;
    expect(match(plan, data)).toBeNull();
    runtime.executionMode = 1;
    expect(match(plan, data)).toBeNull();
    runtime.timingKind = 'elapsed';
    expect(match(plan, data)?.stages[1]).toMatchObject({ automatic: true, timingKind: 'elapsed', completionMode: 'Total elapsed budget' });
    row.ExecutionMode = 0;
    expect(match(plan, data)).toBeNull();
  });
  it.each([null, -1, 2, '0', false])('does not default an explicit unknown runtime execution mode %j to standard', executionMode => {
    const { plan, data } = fixture();
    (data.stages as JsonObject[])[0].executionMode = executionMode;
    expect(match(plan, data)).toBeNull();
  });
  it('returns exactly the parsed position, not a position inferred from elapsed time', () => {
    const { plan, data } = fixture();
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    expect(match(plan, data)).toMatchObject({ activeStageIndex: 1, stageProgress: .25, cycle: 4 });
    expect(match(plan, data)?.stages.map(stage => stage.state)).toEqual(['completed', 'current', 'pending']);
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

  it('accepts version 17 and its runtime string representation', () => {
    const { plan, data } = fixture();
    for (const reported of [17, '17']) {
      data.planVersion = reported;
      expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    }
  });

  it.each([13, 14, 15, 16, 18, 999, null, 'future', 17.5])('rejects unsupported or invalid schema %j even when both sides report it', version => {
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
    { configuredDurationSeconds: 121 }, { flexWeight: 2 }, { timingKind: 'protected' }, { durationSeconds: '120' }, { durationSeconds: null },
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
    { EnergyFocus: '1' }, { EnergyPriorities: null }, { MagicPriorities: {} }, { Percent: '1' }, { Percent: true }
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
    (data.stages as JsonObject[])[1] = { ...(data.stages as JsonObject[])[0], index: 1, state: 'current' };
    expect(match(plan, data)?.stages.map(stage => stage.state)).toEqual(['completed', 'current', 'pending']);
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
    expect(match(plan, data)?.stages.map(stage => stage.state)).toEqual(status === 'disabled' ? ['unknown', 'unknown', 'unknown'] : ['completed', 'unknown', 'pending']);
  });

  it('does not invent progress from the effective duration', () => {
    const { plan, data } = fixture();
    data.stageProgress = '0.5';
    expect(match(plan, data)).toMatchObject({ activeStageIndex: 1, stageProgress: null });
    data.stageProgress = 1;
    expect(match(plan, data)).toMatchObject({ activeStageIndex: 1, stageProgress: 1 });
  });

  it.each<JsonObject>([
    { StageDurationSeconds: -1 }, { StageDurationSeconds: '600' },
    { Stages: [null] }, { Stages: [] }, { Stages: {} }
  ])('catches invalid plan timing or stages %j', patch => {
    const { plan, data } = fixture();
    Object.assign(plan, patch);
    expect(match(plan, data)).toBeNull();
  });

  it('compares raw weights and configured seconds even when effective estimates are identical', () => {
    const { plan, data } = fixture();
    (plan.Stages as JsonObject[])[2].Percent = 3000;
    expect(match(plan, data)).toBeNull();
    (data.stages as JsonObject[])[2].flexWeight = 3000;
    expect(match(plan, data)).not.toBeNull();
    (plan.Stages as JsonObject[])[2].DurationSeconds = 360;
    expect(match(plan, data)).toBeNull();
  });
});

describe('AT stage-bar mapping', () => {
  it.each([1, 'advancedTraining'])('accepts completion alias %s with exact normalized Int64 targets', completion => {
    const { plan, data } = atFixture();
    (data.stages as JsonObject[])[0].completionMode = completion;
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    expect(match(plan, data)).toMatchObject({ activeStageIndex: 0, stageProgress: null, schedulerMode: 'stages' });
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

  it('matches current AT telemetry with dynamic zero-second flexible assignments', () => {
    const { plan, data } = atFixture();
    plan.StageDurationSeconds = 0;
    (data.stages as JsonObject[]).slice(1).forEach(row => { row.durationSeconds = 0; });
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    (data.stages as JsonObject[])[2].flexWeight = 1;
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

describe('configured stage timing mapping', () => {
  function dynamicFixture() {
    const { plan, data } = fixture();
    data.protectedWorkComplete = false;
    data.protectedSecondsRemaining = 90;
    (data.stages as JsonObject[]).forEach((row, i) => Object.assign(row, {
      configuredDurationSeconds: [120, 0, 0][i], flexWeight: [1, 1, 3][i],
      timingKind: i === 0 ? 'fixed' : 'flexible', durationSeconds: [120, 0, 47][i]
    }));
    return { plan, data };
  }

  it('matches configured duration and raw weights, not dynamic assigned estimates', () => {
    const { plan, data } = dynamicFixture();
    data.planRevision = plan.__externalUiRevision;
    expect(match(plan, data)).toEqual(parsePlanProgress(data, true));
    expect(match(plan, data)?.stages[1]).toMatchObject({ durationSeconds: 0, configuredDurationSeconds: 0, flexWeight: 1, timingKind: 'flexible' });
    (data.stages as JsonObject[])[2].durationSeconds = 1;
    expect(match(plan, data)).not.toBeNull();
    plan.StageDurationSeconds = 0;
    expect(match(plan, data)).not.toBeNull();
  });

  it.each<JsonObject>([
    { configuredDurationSeconds: 121 }, { configuredDurationSeconds: '120' }, { configuredDurationSeconds: null },
    { flexWeight: 2 }, { flexWeight: '1' }, { flexWeight: null }, { timingKind: 'flexible' },
    { durationSeconds: -1 }, { durationSeconds: '120' }, { durationSeconds: null },
    { energyFocus: 0 }, { magicFocus: 0 }, { gearGoal: 0 },
    { configuredEnergyPriorities: ['TM'] }, { configuredMagicPriorities: ['TM'] }
  ])('rejects inconsistent configuration or invalid assignment: %j', patch => {
    const { plan, data } = dynamicFixture();
    Object.assign((data.stages as JsonObject[])[0], patch);
    expect(match(plan, data)).toBeNull();
  });

  it.each(['configuredDurationSeconds', 'flexWeight', 'timingKind', 'durationSeconds'])('always requires runtime metadata %s', key => {
    const { plan, data } = dynamicFixture();
    delete (data.stages as JsonObject[])[0][key];
    expect(match(plan, data)).toBeNull();
  });

  it('always checks revision and stage order without any timing opt-in', () => {
    const { plan, data } = dynamicFixture();
    expect(match(plan, data)).not.toBeNull();
    data.planRevision = 'b'.repeat(64);
    expect(match(plan, data)).toBeNull();
    delete data.planRevision;
    const rows = data.stages as JsonObject[];
    [rows[1], rows[2]] = [{ ...rows[2], index: 1 }, { ...rows[1], index: 2 }];
    expect(match(plan, data)).toBeNull();
  });

  it('matches mandatory AT targets exactly, including dormant duration metadata', () => {
    const { plan, data } = atFixture();
    (data.stages as JsonObject[]).forEach((row, i) => Object.assign(row, {
      configuredDurationSeconds: i === 0 ? 999 : 0, flexWeight: [999, 1, 3][i], timingKind: i === 0 ? 'target' : 'flexible'
    }));
    expect(match(plan, data)).not.toBeNull();
    (data.stages as JsonObject[])[0].configuredDurationSeconds = 0;
    expect(match(plan, data)).toBeNull();
    (data.stages as JsonObject[])[0].configuredDurationSeconds = 999;
    (data.stages as JsonObject[])[0].targetAdvancedTrainingPower = '9223372036854775805';
    expect(match(plan, data)).toBeNull();
  });
});

describe('manual stage commands', () => {
  const controllers: ReturnType<typeof createController>[] = [];
  afterEach(() => { controllers.splice(0).forEach(ui => ui.dispose()); });

  async function controllerFixture() {
    const { plan, data } = fixture();
    plan.StageSchedulerEnabled = true;
    Object.assign(data, { planRevision: plan.__externalUiRevision, stageEntry: 'entry-7' });
    const snapshot: api.NguiState = {
      connected: true, runtime_dir: 'A:/runtime', marker_enabled: true, protocol: {},
      settings: { _customPlanFile: 'selected-file', _useCustomRebirthPlan: true },
      runtime: { startedAt: 'session-a', updatedAt: new Date().toISOString(), runtimeRevision: 1, externalUiEnabled: true, status: 'running', protocolVersion: 3, stageOverridesV1: true, supportedCommands: ['readPlan', 'setPlanStage'], planProgress: data },
      plans: [], profiles: [], logs: [], command_results: []
    };
    const backend = { ...api,
      getState: vi.fn(async () => snapshot), storeRuntimeDir: vi.fn(),
      readPlan: vi.fn(async () => cloneObject(plan)),
      savePlan: vi.fn(async () => ({ success: true })), saveSettingsPatch: vi.fn(async () => ({ success: true })),
      enqueueCommand: vi.fn(async (_dir: string, _command: string, _args?: JsonObject) => 'manual-id'),
      readCommandResult: vi.fn(async (): Promise<JsonObject | null> => ({ id: 'manual-id', command: 'setPlanStage', success: true, message: 'Stage action accepted.' }))
    };
    const confirm = vi.fn(async (_message: string) => false);
    const ui = createController(backend, confirm, snapshot.runtime_dir);
    controllers.push(ui);
    await ui.refresh();
    await ui.loadPlan('selected-file');
    return { ui, backend, confirm, snapshot, plan, data };
  }

  async function noSelectionFixture(activeStageIndex: number | null = null) {
    const fixture = await controllerFixture();
    fixture.plan.StageDurationSeconds = 0;
    (fixture.plan.Stages as JsonObject[]).forEach(row => row.DurationSeconds = 0);
    Object.assign(fixture.data, { activeStageIndex, scheduleComplete: true, stageProgress: null, stageEntry: 'generation:0' });
    (fixture.data.stages as JsonObject[]).forEach(row => Object.assign(row, { configuredDurationSeconds: 0, durationSeconds: 0, timingKind: 'flexible', state: 'skipped', appliedSeconds: 0, elapsedSeconds: 0, visits: 0 }));
    await fixture.ui.loadPlan('selected-file');
    return fixture;
  }

  it.each([null, false, true, '1', 0, 2, 1])('gates reviewed plan stage actions on numeric lane capability %j', async capability => {
    const { ui, backend, snapshot, plan } = await controllerFixture();
    plan.ActivePresetId = 'reviewed';
    snapshot.runtime.globalPresetsVersion = 2;
    snapshot.runtime.globalNguWeightsVersion = 1;
    snapshot.runtime.resourceLanePrioritiesVersion = capability;
    vi.spyOn(backend, 'readPresetLibrary').mockResolvedValue({ Version: 1, __externalUiRevision: 'b'.repeat(64), Presets: [{ Id: 'reviewed', Name: 'Reviewed', ResourcePrioritiesReviewed: true, EnergyPriorities: [], MagicPriorities: [] }] });
    await ui.loadPlan('selected-file');
    await ui.setPlanStage('set', 0, 'entry-7');
    if (capability === 1) {
      expect(ui.current().error).toBe('');
      expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'setPlanStage', { name: 'selected-file', expectedRevision: plan.__externalUiRevision, expectedEntry: 'entry-7', action: 'set', stageIndex: 0 });
    } else {
      expect(ui.current().error).toContain('resourceLanePrioritiesVersion');
      expect(backend.enqueueCommand).not.toHaveBeenCalled();
    }
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
  });

  it.each(['restart', 'set', 'skip'] as const)('blocks %s during reconciliation without treating it as acknowledgement', async action => {
    const { ui, backend, snapshot, data } = await controllerFixture();
    const combatPrep = { schemaVersion: 1, active: false, status: 'failed', cleanupPending: false, cleanupRecoveryRequired: true, runId: '' };
    snapshot.runtime.combatPrep = combatPrep;
    Object.assign(data, { cleanupRecoveryRequired: true, combatPrep });
    expect(data.status).toBe('running');
    expect(planStageOverrideReason(ui.current())).toBe('Reconciling.');
    await ui.setPlanStage(action, 1, 'entry-7');
    expect(ui.current().error).toBe('Reconciling.');
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(data.cleanupRecoveryRequired).toBe(true);
    expect(data.stageEntry).toBe('entry-7');
    expect(backend.savePlan).not.toHaveBeenCalled(); expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
  });
  it('observes backend recovery clearing and successor funding without commands or settings writes', async () => {
    const { ui, backend, snapshot, data } = await controllerFixture();
    backend.getState.mockImplementation(async () => structuredClone(snapshot));
    const reason = 'Automatic recovery checkpoint could not be saved; check runtime directory permissions or file locks. Retrying automatically.';
    const combatPrep = { schemaVersion: 1, active: false, cleanupPending: false, cleanupRecoveryRequired: true, cleanupRecoveryReason: 'Retiring historical restoration ownership.', runId: '' };
    Object.assign(data, { cleanupRecoveryRequired: true, combatPrep, reason: 'Retiring historical restoration ownership.' });
    Object.assign(snapshot.runtime, { combatPrep, resourceAllocation: { state: 'Failed', owner: 'automatic-recovery', reason }, runtimeRevision: 2 });
    await ui.refresh();
    expect(planStageOverrideReason(ui.current())).toBe(`Reconciling: ${reason}`);
    expect(ui.current().snapshot?.runtime.planProgress).toMatchObject({ cleanupRecoveryRequired: true, activeStageIndex: 1, stageEntry: 'entry-7', cleanupRecoveryReason: reason });
    await ui.refresh();
    expect(data.cleanupRecoveryRequired).toBe(true);
    expect(data.reason).toBe('Retiring historical restoration ownership.');
    expect(data).not.toHaveProperty('cleanupRecoveryReason');
    expect(combatPrep.cleanupRecoveryRequired).toBe(true);

    combatPrep.cleanupRecoveryRequired = false;
    combatPrep.cleanupRecoveryReason = '';
    Object.assign(data, { cleanupRecoveryRequired: false, activeStageIndex: 2, stageEntry: 'entry-8', stageElapsedSeconds: 4, stageProgress: .1, reason: '' });
    (data.stages as JsonObject[])[1].state = 'completed';
    Object.assign((data.stages as JsonObject[])[2], { state: 'current', appliedSeconds: 4, visits: 1 });
    Object.assign(snapshot.runtime, { runtimeRevision: 3, resourceAllocation: { state: 'Applied', owner: 'stage:selected-file:2:entry-8', reason: '' }, nguAllocation: { available: true, strategy: 'weighted-ngu-buffered-cap', energyTargets: [{ index: 4, name: 'Adventure Alpha', weight: 1, allocated: '12345678901234567890' }], magicTargets: [{ index: 0, name: 'Yggdrasil', weight: 1, allocated: '9876543210' }] } });
    await ui.refresh();
    expect(planStageOverrideReason(ui.current())).toBe('');
    expect(match(ui.current().plan!, ui.current().snapshot!.runtime.planProgress as JsonObject)).toMatchObject({ activeStageIndex: 2, stageEntry: 'entry-8', stages: [{ state: 'completed' }, { state: 'completed' }, { state: 'current', appliedSeconds: 4 }] });
    expect(ui.current().snapshot?.runtime.resourceAllocation).toMatchObject({ state: 'Applied' });
    expect(ui.current().snapshot?.runtime.nguAllocation).toEqual(snapshot.runtime.nguAllocation);
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
  });
  it.each(['live run', 'cleanup pending', 'paused', 'stale entry', 'stale revision'])('does not bypass %s guards during reconciliation', async scenario => {
    const { ui, backend, snapshot, data } = await controllerFixture();
    Object.assign(data, { cleanupRecoveryRequired: true });
    snapshot.runtime.combatPrep = { schemaVersion: 1, active: scenario === 'live run', cleanupPending: scenario === 'cleanup pending', cleanupRecoveryRequired: true };
    if (scenario === 'paused') data.status = 'paused';
    if (scenario === 'stale entry') data.stageEntry = 'new-entry';
    if (scenario === 'stale revision') data.planRevision = 'b'.repeat(64);
    await ui.setPlanStage('restart', 1, 'entry-7');
    expect(ui.current().error).not.toBe('');
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(data.cleanupRecoveryRequired).toBe(true);
  });

  it.each(['skip', 'set', 'restart'] as const)('sends %s immediately with only the saved identity and exact current-entry token even when confirmation would return false', async action => {
    const { ui, backend, confirm, plan } = await controllerFixture();
    await ui.setPlanStage(action, action === 'set' ? 0 : 1, 'entry-7');
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'setPlanStage', {
      name: 'selected-file', expectedRevision: plan.__externalUiRevision, expectedEntry: 'entry-7', action, stageIndex: action === 'set' ? 0 : 1
    });
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(parsePlanProgress(ui.current().snapshot!.runtime.planProgress as JsonObject, true).activeStageIndex).toBe(1);
  });

  it.each(['Stage entry changed.', 'Saved plan revision changed.'])('does not retarget or move the reported stage when enqueue rejects stale intent: %s', async message => {
    const { ui, backend, confirm, plan } = await controllerFixture();
    backend.enqueueCommand.mockRejectedValue(new Error(message));
    await ui.setPlanStage('skip', 1, 'entry-7');
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'setPlanStage', {
      name: 'selected-file', expectedRevision: plan.__externalUiRevision, expectedEntry: 'entry-7', action: 'skip', stageIndex: 1
    });
    expect(backend.readCommandResult).not.toHaveBeenCalled();
    expect(ui.current().error).toContain(message);
    expect(ui.current().busy).toBe(false);
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(ui.current().snapshot!.runtime.planProgress).toMatchObject({ activeStageIndex: 1, stageEntry: 'entry-7' });
  });

  for (const terminalState of ['completed', 'skipped']) {
    it.each(['restart', 'set'] as const)('immediately sends %s on a finished ' + terminalState + ' holding row without optimistic re-entry', async action => {
      const { ui, backend, confirm, data, plan } = await controllerFixture();
      Object.assign(data, { activeStageIndex: 2, scheduleComplete: true });
      (data.stages as JsonObject[]).forEach((row, i) => row.state = i === 2 ? terminalState : 'completed');
      expect(planStageOverrideReason(ui.current())).toBe('');
      await ui.setPlanStage(action, 2, 'entry-7');
      expect(confirm).not.toHaveBeenCalled();
      expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'setPlanStage', {
        name: 'selected-file', expectedRevision: plan.__externalUiRevision, expectedEntry: 'entry-7', action, stageIndex: 2
      });
      const view = parsePlanProgress(ui.current().snapshot!.runtime.planProgress as JsonObject, true);
      expect(view).toMatchObject({ activeStageIndex: 2, scheduleComplete: true });
      expect(view.stages[2].state).toBe(terminalState);
      expect(backend.savePlan).not.toHaveBeenCalled();
      expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    });

    it.each(['entry', 'revision'])('rejects a changed %s before clicking a finished ' + terminalState + ' holding row', async change => {
      const { ui, backend, confirm, data } = await controllerFixture();
      Object.assign(data, { activeStageIndex: 2, scheduleComplete: true });
      (data.stages as JsonObject[]).forEach((row, i) => row.state = i === 2 ? terminalState : 'completed');
      if (change === 'entry') data.stageEntry = 'new-entry';
      else data.planRevision = 'b'.repeat(64);
      await ui.setPlanStage('restart', 2, 'entry-7');
      expect(confirm).not.toHaveBeenCalled();
      expect(backend.enqueueCommand).not.toHaveBeenCalled();
      expect(ui.current().error).toContain(change === 'entry' ? 'stage entry changed' : 'matching live stage telemetry');
      expect(parsePlanProgress(ui.current().snapshot!.runtime.planProgress as JsonObject, true).stages[2].state).toBe(terminalState);
    });
  }

  it.each<JsonObject>([
    { state: 'completed', scheduleComplete: false }, { state: 'skipped', scheduleComplete: false },
    { state: 'pending', scheduleComplete: true }, { state: 'unknown', scheduleComplete: true },
    { state: 'completed', scheduleComplete: true, status: 'paused' }
  ])('rejects an inconsistent or unavailable terminal position: %j', async patch => {
    const { ui, backend, confirm, data } = await controllerFixture();
    const { state, ...root } = patch;
    Object.assign(data, { activeStageIndex: 2, ...root });
    (data.stages as JsonObject[]).forEach((row, i) => row.state = i === 2 ? state : 'completed');
    await ui.setPlanStage('restart', 2, 'entry-7');
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(ui.current().error).toBe('Wait for matching live stage telemetry from the running plan.');
  });

  it.each([null, -1])('allows Set from a complete zero-budget flexible schedule with active index %s', async active => {
    const { ui, backend, confirm, plan } = await noSelectionFixture(active);
    expect(planStageOverrideReason(ui.current())).toBe('');
    await ui.setPlanStage('set', 2, 'generation:0');
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'setPlanStage', {
      name: 'selected-file', expectedRevision: plan.__externalUiRevision, expectedEntry: 'generation:0', action: 'set', stageIndex: 2
    });
    expect(parsePlanProgress(ui.current().snapshot!.runtime.planProgress as JsonObject, true)).toMatchObject({ activeStageIndex: null, scheduleComplete: true, stageEntry: 'generation:0' });
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
  });

  for (const active of [null, -1]) {
    it.each(['skip', 'restart'] as const)('rejects %s without confirmation when complete active index is ' + active, async action => {
      const { ui, backend, confirm } = await noSelectionFixture(active);
      expect(planStageOverrideReason(ui.current())).toBe('');
      await ui.setPlanStage(action, 2, 'generation:0');
      expect(ui.current().error).toBe('No current stage is selected. Set a stage as current first.');
      expect(confirm).not.toHaveBeenCalled();
      expect(backend.enqueueCommand).not.toHaveBeenCalled();
    });
  }

  it.each(['entry', 'revision', 'draft', 'unknown', 'offline'])('guards a no-selection terminal Set when %s changes before clicking', async change => {
    const { ui, backend, confirm, data, snapshot } = await noSelectionFixture();
    if (change === 'entry') data.stageEntry = 'generation:1';
    if (change === 'revision') data.planRevision = 'b'.repeat(64);
    if (change === 'draft') ui.planDraftDirty(true);
    if (change === 'unknown') (data.stages as JsonObject[])[0].state = 'unknown';
    if (change === 'offline') snapshot.connected = false;
    await ui.setPlanStage('set', 2, 'generation:0');
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(ui.current().error).not.toBe('');
    expect(parsePlanProgress(ui.current().snapshot!.runtime.planProgress as JsonObject, true).activeStageIndex).toBeNull();
  });

  it.each(['dirty', 'stale entry', 'stale revision', 'unknown row', 'rejected active index', 'incomplete'])('rejects unsafe no-selection Set without enqueueing: %s', async change => {
    const { ui, backend, confirm, data } = await noSelectionFixture();
    if (change === 'dirty') ui.planDraftDirty(true);
    if (change === 'stale entry') data.stageEntry = '';
    if (change === 'stale revision') data.planRevision = 'b'.repeat(64);
    if (change === 'unknown row') (data.stages as JsonObject[])[0].state = 'unknown';
    if (change === 'rejected active index') {
      data.activeStageIndex = 1;
      (data.stages as JsonObject[])[1].state = 'unknown';
    }
    if (change === 'incomplete') data.scheduleComplete = false;
    await ui.setPlanStage('set', 2, 'generation:0');
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
  });

  it.each(['plan', 'timing', 'JSON', 'settings'])('blocks a pending %s draft without confirmation or flushing other drafts', async kind => {
    const { ui, backend, confirm } = await controllerFixture();
    if (kind === 'timing') ui.planDraftDirty(true);
    if (kind === 'JSON') ui.advancedDirty(true);
    if (kind === 'settings') ui.setSetting('_manageEnergy', false);
    if (kind === 'plan') ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 0 });
    expect(planStageOverrideReason(ui.current())).toBe('Finish or discard pending settings, timing, and JSON edits first.');
    await ui.setPlanStage('skip', 1, 'entry-7');
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
  });

  it.each<[Partial<UiState>, string]>([
    [{ busy: true }, 'Another action is pending.'],
    [{ planName: 'inactive-file' }, 'Only the active running plan can change stages.'],
    [{ plan: null }, 'Load the saved plan before changing stages.'],
    [{ planBase: null }, 'Load the saved plan before changing stages.']
  ])('explains the unavailable editor gate %j', async (patch, reason) => {
    const { ui } = await controllerFixture();
    expect(planStageOverrideReason({ ...ui.current(), ...patch })).toBe(reason);
  });

  it.each<JsonObject>([
    { stageOverridesV1: false }, { stageOverridesV1: 'true' }, { supportedCommands: ['readPlan'] },
    { updatedAt: '2000-01-01T00:00:00Z' }, { status: 'unloaded' }, { settings: { customPlanFile: 'other', useCustomRebirthPlan: true } }, { settings: { customPlanFile: 'selected-file', useCustomRebirthPlan: false } }
  ])('fails closed for runtime capability or active-plan gates: %j', async patch => {
    const { ui, backend, confirm, snapshot } = await controllerFixture();
    Object.assign(snapshot.runtime, patch);
    await ui.setPlanStage('skip', 1, 'entry-7');
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
  });

  it.each<JsonObject>([
    { stageEntry: '' }, { stageEntry: null }, { planRevision: 'b'.repeat(64) }, { planRevision: null }, { status: 'paused' }, { status: 'interrupted' }, { available: false }
  ])('requires matching available telemetry and saved revision: %j', async patch => {
    const { ui, backend, confirm, data } = await controllerFixture();
    Object.assign(data, patch);
    await ui.setPlanStage('skip', 1, 'entry-7');
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
  });

  it.each([['skip', 0, 'entry-7'], ['restart', 1, 'old-entry'], ['set', -1, 'entry-7'], ['set', 3, 'entry-7'], ['set', 1.5, 'entry-7']] as const)('rejects invalid or stale intent %s/%s/%s', async (action, stageIndex, entry) => {
    const { ui, backend, confirm } = await controllerFixture();
    await ui.setPlanStage(action, stageIndex, entry);
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(ui.current().error).not.toBe('');
  });

  it.each(['entry', 'revision', 'draft', 'session', 'offline'])('rejects stale %s telemetry before clicking instead of retargeting the intent', async change => {
    const { ui, backend, confirm, snapshot, data } = await controllerFixture();
    if (change === 'entry') data.stageEntry = 'entry-8';
    if (change === 'revision') data.planRevision = 'b'.repeat(64);
    if (change === 'draft') ui.planDraftDirty(true);
    if (change === 'session') snapshot.runtime.startedAt = '';
    if (change === 'offline') snapshot.connected = false;
    await ui.setPlanStage('skip', 1, 'entry-7');
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(ui.current().error).not.toBe('');
  });

  it('keeps the current marker while pending, blocks duplicate actions, and consumes only backend telemetry', async () => {
    const { ui, backend, confirm, snapshot, data } = await controllerFixture();
    let finish!: (value: JsonObject) => void;
    backend.readCommandResult.mockReturnValue(new Promise(resolve => { finish = resolve; }));
    const action = ui.setPlanStage('skip', 1, 'entry-7');
    await vi.waitFor(() => expect(backend.readCommandResult).toHaveBeenCalledOnce());
    expect(ui.current().busy).toBe(true);
    expect(ui.current().snapshot!.runtime.planProgress).toMatchObject({ activeStageIndex: 1 });
    await ui.setPlanStage('restart', 1, 'entry-7');
    expect(backend.enqueueCommand).toHaveBeenCalledOnce();
    expect(confirm).not.toHaveBeenCalled();
    const next = cloneObject(data);
    next.activeStageIndex = 2;
    next.stageEntry = 'entry-8';
    (next.stages as JsonObject[])[1].state = 'skipped';
    (next.stages as JsonObject[])[2].state = 'current';
    finish({ id: 'manual-id', command: 'setPlanStage', success: true, data: { runtime: { ...snapshot.runtime, runtimeRevision: 2, planProgress: next } } });
    await action;
    expect(ui.current().busy).toBe(false);
    expect(ui.current().snapshot!.runtime.planProgress).toMatchObject({ activeStageIndex: 2, stageEntry: 'entry-8' });
    expect(parsePlanProgress(ui.current().snapshot!.runtime.planProgress as JsonObject, true).stages[1].state).toBe('skipped');
  });

  it('rejects a pending result from a changed injector session without moving the marker', async () => {
    const { ui, backend, confirm, snapshot, data } = await controllerFixture();
    let finish!: (value: JsonObject) => void;
    backend.readCommandResult.mockReturnValue(new Promise(resolve => { finish = resolve; }));
    const action = ui.setPlanStage('restart', 1, 'entry-7');
    await vi.waitFor(() => expect(backend.readCommandResult).toHaveBeenCalledOnce());
    snapshot.runtime.startedAt = 'session-b';
    const next = cloneObject(data);
    Object.assign(next, { activeStageIndex: 2, stageEntry: 'entry-8' });
    (next.stages as JsonObject[])[1].state = 'completed';
    (next.stages as JsonObject[])[2].state = 'current';
    finish({ id: 'manual-id', command: 'setPlanStage', success: true, data: { runtime: {
      ...snapshot.runtime, startedAt: 'session-a', runtimeRevision: 2, planProgress: next
    } } });
    await action;
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).toHaveBeenCalledOnce();
    expect(ui.current().error).toContain('Injector session changed');
    expect(ui.current().snapshot!.runtime.planProgress).toMatchObject({ activeStageIndex: 1, stageEntry: 'entry-7' });
    expect(ui.current().busy).toBe(false);
  });

  it.each(['Boss push is active.', 'Rebirth prep is active.', 'Stage entry changed.', 'Saved plan revision changed.'])('shows backend rejection without claiming a stage move: %s', async message => {
    const { ui, backend, confirm } = await controllerFixture();
    backend.readCommandResult.mockResolvedValue({ id: 'manual-id', command: 'setPlanStage', success: false, message });
    await ui.setPlanStage('set', 0, 'entry-7');
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).toHaveBeenCalledOnce();
    expect(ui.current().error).toContain(message);
    expect(ui.current().snapshot!.runtime.planProgress).toMatchObject({ activeStageIndex: 1, stageEntry: 'entry-7' });
    expect(ui.current().busy).toBe(false);
  });

  it.each([
    ['manualRebirth', 'Rebirth now? This resets the current run.'],
    ['unload', 'Disconnect and stop automation?'],
    ['probeCommands', 'Run the commands in probe.txt? They may change the game.']
  ])('still confirms unsafe %s and cancels it without enqueueing', async (command, warning) => {
    const { ui, backend, confirm, snapshot } = await controllerFixture();
    (snapshot.runtime.supportedCommands as string[]).push(command);
    await ui.runCommand(command);
    expect(confirm).toHaveBeenCalledExactlyOnceWith(warning);
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
  });
});
