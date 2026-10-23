import { describe, expect, it } from 'vitest';
import { parseJson, stringifyJson, type JsonObject } from '../src/lib/api';
import { scheduleTiming } from '../src/lib/stage-duration';
import { applyStagePreset, stageLabel, stagePresetIndex, stagePresets } from '../src/lib/stage-presets';

const allocations = [
  ['Time Machine', 1, 1, 5], ['Augments + Blood', 2, 4, 10], ['Wandoos', 3, 3, 12],
  ['Energy NGU', 5, 6, 2], ['Magic NGU', 0, 6, 3], ['All NGU', 5, 6, 4],
  ['AT Adventure', 7, 4, 11], ['AT Wandoos', 7, 3, 11]
] as const;

const priorityOrders = [
  [['TM', 'NGU', 'WAN', 'AUG', 'AT'], ['TM', 'RIT', 'NGU', 'WAN']],
  [['AUG', 'NGU', 'WAN', 'TM', 'AT'], ['RIT', 'NGU', 'WAN', 'TM']],
  [['WAN', 'NGU', 'TM', 'AUG', 'AT'], ['WAN', 'RIT', 'NGU', 'TM']],
  [['NGU', 'WAN', 'TM', 'AUG', 'AT'], ['NGU', 'RIT', 'WAN', 'TM']],
  [[], ['NGU', 'RIT', 'WAN', 'TM']],
  [['NGU', 'WAN', 'TM', 'AUG', 'AT'], ['NGU', 'RIT', 'WAN', 'TM']],
  [['AT', 'NGU', 'WAN', 'TM', 'AUG'], ['RIT', 'NGU', 'WAN', 'TM']],
  [['AT-3', 'AT-4', 'WAN', 'NGU', 'TM', 'AUG', 'AT'], ['RIT', 'NGU', 'TM']]
];

describe('stage presets', () => {
  it('keeps labels and indexes stable with unique focus/gear tuples', () => {
    expect(stagePresets.map(preset => [preset.label, preset.EnergyFocus, preset.MagicFocus, preset.GearGoal])).toEqual(allocations);
    expect(new Set(stagePresets.map(preset => `${preset.EnergyFocus}/${preset.MagicFocus}/${preset.GearGoal}`)).size).toBe(8);
  });

  it('defines explicit canonical priority orders for every preset', () => {
    expect(stagePresets.map(preset => [preset.EnergyPriorities, preset.MagicPriorities])).toEqual(priorityOrders);
  });

  it.each(allocations)('detects %s after application and JSON readback', (label, EnergyFocus, MagicFocus, GearGoal) => {
    const index = allocations.findIndex(entry => entry[0] === label);
    const applied = applyStagePreset({ Name: 'Old name', EnergyFocus: 0, MagicFocus: 0, GearGoal: -1 }, index);
    expect(applied).toEqual({
      Name: label, EnergyFocus, MagicFocus, GearGoal,
      EnergyPriorities: stagePresets[index].EnergyPriorities, MagicPriorities: stagePresets[index].MagicPriorities
    });
    const readback = parseJson(stringifyJson(applied)) as JsonObject;
    const before = stringifyJson(readback);
    expect(stagePresetIndex(readback)).toBe(index);
    expect(stageLabel(readback)).toBe(label);
    expect(stageLabel({ ...readback, Name: 'User name', CompletionMode: 1, Percent: .25 })).toBe(label);
    expect(stringifyJson(readback)).toBe(before);
  });

  it.each(allocations)('identifies %s without priority arrays', (label, EnergyFocus, MagicFocus, GearGoal) => {
    const row = { EnergyFocus, MagicFocus, GearGoal };
    const index = allocations.findIndex(entry => entry[0] === label);
    expect(stagePresetIndex(row)).toBe(index);
    expect(stagePresetIndex({ ...row, EnergyPriorities: [] })).toBe(index);
    expect(stagePresetIndex({ ...row, MagicPriorities: [] })).toBe(index);
    expect(stageLabel(row)).toBe(label);
    expect(row).toEqual({ EnergyFocus, MagicFocus, GearGoal });
  });

  it('recognizes the legacy Adventure Stats allocation by its new label', () => {
    expect(stageLabel({ Name: 'Adventure Stats', EnergyFocus: 7, MagicFocus: 4, GearGoal: 11 })).toBe('AT Adventure');
  });

  it.each<JsonObject>([
    { EnergyFocus: 0 }, { MagicFocus: 0 }, { GearGoal: -1 }, { EnergyFocus: '1' }
  ])('reports Custom for an unknown focus/gear tuple despite a preset name: %j', changes => {
    const row = { ...applyStagePreset({}, 0), ...changes };
    expect(stagePresetIndex(row)).toBe(-1);
    expect(stageLabel(row)).toBe('Custom');
  });

  it('does not infer an allocation from a saved name', () => {
    expect(stagePresetIndex({ Name: 'Time Machine' })).toBe(-1);
    expect(stageLabel({ Name: 'Time Machine' })).toBe('Custom');
  });

  it.each(allocations)('keeps %s identity independent of edited priority orders', label => {
    const index = allocations.findIndex(entry => entry[0] === label);
    const row = applyStagePreset({}, index);
    for (const key of ['EnergyPriorities', 'MagicPriorities'] as const) {
      const priorities = row[key] as string[];
      for (const changed of [[], priorities.slice(1), [...priorities, 'WAN'], [...priorities].reverse(), ['TM', 'TM'], 'TM', null, {}]) {
        expect(stagePresetIndex({ ...row, [key]: changed })).toBe(index);
        expect(stageLabel({ ...row, [key]: changed })).toBe(label);
      }
      delete row[key];
      expect(stagePresetIndex(row)).toBe(index);
    }
  });

  it('puts Wandoos AT before energy spillover without Wandoos magic during AT', () => {
    const row = applyStagePreset({ EnergyPriorities: ['TM'], MagicPriorities: ['RIT'] }, 7);
    expect(row.EnergyPriorities).toEqual(['AT-3', 'AT-4', 'WAN', 'NGU', 'TM', 'AUG', 'AT']);
    expect(row.MagicPriorities).toEqual(['RIT', 'NGU', 'TM']);
    expect([row.EnergyFocus, row.MagicFocus, row.GearGoal]).toEqual([7, 3, 11]);
  });

  it.each(allocations)('replaces previous priority overrides with canonical orders when selecting %s', label => {
    const index = allocations.findIndex(entry => entry[0] === label);
    const source = { EnergyPriorities: ['TM', 'TM'], MagicPriorities: ['RIT-0'] };
    const before = stringifyJson(source);
    const next = applyStagePreset(source, index);
    expect([next.EnergyPriorities, next.MagicPriorities]).toEqual(priorityOrders[index]);
    expect(stagePresetIndex(next)).toBe(index);
    expect(stringifyJson(source)).toBe(before);
  });

  it.each(allocations)('clones priority arrays for each %s application', label => {
    const index = allocations.findIndex(entry => entry[0] === label);
    const source: JsonObject = { EnergyPriorities: ['TM'], MagicPriorities: ['NGU'] };
    const before = stringifyJson(source);
    const presetsBefore = stringifyJson(stagePresets);
    Object.freeze(source.EnergyPriorities);
    Object.freeze(source.MagicPriorities);
    Object.freeze(source);
    const first = applyStagePreset(source, index);
    const second = applyStagePreset(source, index);
    expect(first).not.toBe(source);
    for (const key of ['EnergyPriorities', 'MagicPriorities'] as const) {
      expect(first[key]).not.toBe(source[key]);
      expect(first[key]).not.toBe(stagePresets[index][key]);
      expect(first[key]).not.toBe(second[key]);
      (first[key] as string[]).push('TM');
      expect(second[key]).toEqual(stagePresets[index][key]);
    }
    expect(stringifyJson(source)).toBe(before);
    expect(stringifyJson(stagePresets)).toBe(presetsBefore);
  });

  it.each(allocations)('preserves lossless unknown fields, timing, completion and targets when selecting %s', label => {
    const source = parseJson('{"Name":"Old","Percent":0.123456789012345678901,"DurationSeconds":321,"DurationUnit":"min","CompletionMode":1,"TargetAdvancedTrainingPower":"9223372036854775807","TargetAdvancedTrainingToughness":"9007199254740993","FightOnEntry":true,"BlackBarLookaheadSeconds":17,"Future":9007199254740995,"Nested":{"values":[9223372036854775807,null,true]}}') as JsonObject;
    const before = stringifyJson(source);
    const next = applyStagePreset(source, allocations.findIndex(entry => entry[0] === label));
    for (const key of Object.keys(source).filter(key => key !== 'Name')) {
      expect(stringifyJson(next[key])).toBe(stringifyJson(source[key]));
    }
    expect(next.Name).toBe(label);
    expect(stringifyJson(source)).toBe(before);
  });

  it.each([0, 1])('leaves effective schedule timing unchanged for completion mode %s', CompletionMode => {
    const row: JsonObject = {
      Name: 'Before', Percent: .25, DurationSeconds: 120, DurationUnit: 's', CompletionMode,
      TargetAdvancedTrainingPower: '9223372036854775807', TargetAdvancedTrainingToughness: '9007199254740993'
    };
    const plan: JsonObject = { Version: 15, StageDurationSeconds: 600, StageLoopStartIndex: 1, Stages: [row, { Percent: .75 }] };
    const timing = scheduleTiming(plan);
    const before = stringifyJson(plan);
    stagePresets.forEach((_, index) => {
      const next = applyStagePreset(row, index);
      expect(next.CompletionMode).toBe(CompletionMode);
      expect(next.DurationSeconds).toBe(120);
      expect(next.DurationUnit).toBe('s');
      expect(next.Percent).toBe(.25);
      expect(scheduleTiming({ ...plan, Stages: [next, (plan.Stages as JsonObject[])[1]] })).toEqual(timing);
    });
    expect(stringifyJson(plan)).toBe(before);
  });

  it('does not add timing, completion or target defaults to sparse rows', () => {
    expect(applyStagePreset({}, 6)).toEqual({
      Name: 'AT Adventure', EnergyFocus: 7, MagicFocus: 4, GearGoal: 11,
      EnergyPriorities: ['AT', 'NGU', 'WAN', 'TM', 'AUG'], MagicPriorities: ['RIT', 'NGU', 'WAN', 'TM']
    });
  });

  it.each([-1, stagePresets.length, .5, NaN, Infinity, -Infinity])('rejects invalid preset index %s without mutating the source', index => {
    const source: JsonObject = { Name: 'Keep', Percent: .5, EnergyPriorities: ['TM'] };
    const before = stringifyJson(source);
    expect(() => applyStagePreset(source, index)).toThrow('Invalid stage preset index');
    expect(stringifyJson(source)).toBe(before);
  });
});
