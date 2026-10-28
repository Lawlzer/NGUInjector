import { describe, expect, it } from 'vitest';
import { render } from 'svelte/server';
import { stringifyJson, type JsonObject } from '../src/lib/api';
import { validatePlan } from '../src/lib/model';
import { newPreset, presetSupportsAtTargets } from '../src/lib/presets';
import { weightedStage } from '../src/lib/stage-presets';
import GlobalPresetSelect from '../src/lib/GlobalPresetSelect.svelte';
import StageEditor from '../src/lib/StageEditor.svelte';

const library = (): JsonObject => ({ Version: 1, Presets: [{ ...newPreset(), Id: 'balanced', Name: 'Shared balanced' }], __externalUiRevision: 'a'.repeat(64) });
const plan = (row = weightedStage()): JsonObject => ({ Version: 18, Name: 'test', __externalUiRevision: 'b'.repeat(64), ActivePresetId: 'balanced', Stages: [row] });

describe('global preset references', () => {
  it('requires both the AT objective and power/toughness ratios for mandatory targets', () => {
    const training = { ...newPreset(), Weights: [0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0] };
    expect(presetSupportsAtTargets(training)).toBe(true);
    expect(presetSupportsAtTargets(undefined)).toBe(false);
    expect(presetSupportsAtTargets(newPreset())).toBe(false);
    expect(presetSupportsAtTargets({ ...training, AdvancedTrainingWeights: [0, 0, 0, 1, 1] })).toBe(false);
    expect(presetSupportsAtTargets({ ...training, AdvancedTrainingWeights: [1, 0, 0, 0, 0] })).toBe(false);
    expect(presetSupportsAtTargets({ ...training, AdvancedTrainingWeights: [0, 1, 0, 0, 0] })).toBe(false);
  });
  it('does not mistake direct adventure priorities for mandatory Advanced Training targets', () => {
    for (const index of [10, 11]) {
      const direct = { ...newPreset(), Weights: Array.from({ length: 12 }, (_, i) => i === index ? 1 : 0) };
      expect(presetSupportsAtTargets(direct)).toBe(false);
      expect(presetSupportsAtTargets({ ...direct, Weights: direct.Weights.map((weight, i) => i === 9 ? 1 : weight) })).toBe(true);
    }
  });
  it('creates new weighted stages using plan inheritance rather than a legacy tuple', () => {
    const row = weightedStage();
    expect(row).toMatchObject({ PresetId: '@active', ExecutionMode: 0, CompletionMode: 0, DurationSeconds: 0, Percent: 100 });
    expect(row).not.toHaveProperty('EnergyFocus'); expect(row).not.toHaveProperty('GearGoal');
    expect(validatePlan(plan(row))).toBeUndefined();
  });
  it('keeps timing, targets and unknowns when constructing a weighted template', () => {
    const source: JsonObject = { Percent: 7, DurationSeconds: 420, FightOnEntry: true, TargetAdvancedTrainingPower: '9223372036854775807', Future: 'keep' };
    const before = stringifyJson(source);
    expect(weightedStage(source)).toMatchObject({ ...source, PresetId: '@active' });
    expect(stringifyJson(source)).toBe(before);
  });
  it.each(['@active', '', 'balanced'])('accepts explicit, inherited and persisted legacy stage reference %j', PresetId => {
    expect(validatePlan(plan(weightedStage({ PresetId })))).toBeUndefined();
  });
  it.each([undefined, ''])('rejects inherited references without an active preset: %j', ActivePresetId => {
    const value = plan();
    if (ActivePresetId === undefined) delete value.ActivePresetId; else value.ActivePresetId = ActivePresetId;
    expect(() => validatePlan(value)).toThrow("PresetId '@active' requires a nonempty plan.ActivePresetId");
    expect(validatePlan({ ...value, Stages: [weightedStage({ PresetId: 'balanced' })] })).toBeUndefined();
    expect(validatePlan({ ...value, Stages: [weightedStage({ PresetId: '' })] })).toBeUndefined();
  });
  it('disables inheritance when the plan has no saved active definition', () => {
    const body = render(GlobalPresetSelect, { props: { library: library(), value: '', inherit: true, onchange: () => {} } }).body;
    expect(body).toContain('value="@active" disabled=""');
    expect(body).toContain('Use plan active preset (not selected)');
  });
  it.each(['../bad', '@other', 'x'.repeat(65), false, null])('rejects malformed stage and plan references %j', reference => {
    expect(() => validatePlan(plan(weightedStage({ PresetId: reference })))).toThrow('PresetId');
    expect(() => validatePlan({ ...plan(), ActivePresetId: reference })).toThrow('ActivePresetId');
  });
  it('shows unresolved IDs without silently selecting a different global preset', () => {
    const body = render(GlobalPresetSelect, { props: { library: library(), value: 'deleted', onchange: () => {} } }).body;
    expect(body).toContain('Missing preset: deleted');
    expect(body).toContain('value="deleted" disabled="" selected=""');
    expect(body).toContain('Choose a saved preset to replace the missing one.');
    expect(body).toContain('Shared balanced');
  });
  it('offers inheritance without offering new legacy inline settings for a referenced stage', () => {
    const body = render(GlobalPresetSelect, { props: { library: library(), value: '@active', inherit: true, activePresetId: 'balanced', onchange: () => {} } }).body;
    expect(body).toContain('Use plan active preset: Shared balanced');
    expect(body).not.toContain('Existing settings');
    expect(body).toContain('value="" disabled=""');
  });
  it.each([true, false])('only exposes legacy inline editors for existing inline stages: legacy=%s', legacy => {
    const row = weightedStage({ PresetId: legacy ? '' : '@active' });
    const value = plan(row), before = stringifyJson(value);
    const body = render(StageEditor, { props: { plan: value, presetLibrary: library(), onchange: () => {} } }).body;
    expect(body.includes('aria-label="Energy focus"')).toBe(legacy);
    expect(body.includes('aria-label="Magic priorities"')).toBe(legacy);
    expect(body.includes('Existing settings')).toBe(legacy);
    expect(body).toContain('Shared balanced');
    expect(stringifyJson(value)).toBe(before);
  });
});
