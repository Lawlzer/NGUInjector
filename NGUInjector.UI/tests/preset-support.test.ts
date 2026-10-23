import { describe, expect, it } from 'vitest';
import definitions from '../../NGUInjector/Data/PresetDefinitions.json';
import activityPolicies from '../../NGUInjector/Data/ActivityPolicies.json';
import { cloneObject, parseJson, stringifyJson, type JsonObject, type JsonValue } from '../src/lib/api';
import { beardNames, defaultPresetBeards, newPreset, normalizePreset, normalizePresetLibrary, presetArgs, presetBeards, presetDiggerWeights, presetNumberInput, setPresetBeard, validatePreset } from '../src/lib/presets';

describe('shared preset support', () => {
  it('creates presets from shared defaults and the activity beard policy', () => {
    const preset = newPreset();
    expect(preset).toEqual({ AdventureDynamicWeight: 0, EnergyPriorities: null, MagicPriorities: null, ResourcePrioritiesReviewed: false, ...definitions.defaults, ...definitions.newPreset, Weights: [1, 1, ...Array(12).fill(0)], BeardPriority: activityPolicies.coreBeards, BeardRotation: activityPolicies.optionalBeards, Id: preset.Id });
    (preset.BeardPriority as number[]).reverse();
    (preset.Weights as number[])[0] = 9;
    expect(newPreset().BeardPriority).toEqual(activityPolicies.coreBeards);
    expect(newPreset().Weights).toEqual(definitions.defaults.Weights);
  });

  it('normalizes sparse saved definitions without opting into rotation', () => {
    const row = { Id: 'old', Name: 'Existing', Future: parseJson('9007199254740993') };
    const before = stringifyJson(row);
    const library = normalizePresetLibrary({ Version: 1, Presets: [row], __externalUiRevision: 'a'.repeat(64) });
    const saved = (library.Presets as JsonObject[])[0];
    expect(saved).toEqual({ AdventureDynamicWeight: 0, EnergyPriorities: null, MagicPriorities: null, ResourcePrioritiesReviewed: false, ...definitions.defaults, ...row });
    expect(newPreset(saved)).not.toHaveProperty('BeardRotation');
    expect(stringifyJson(row)).toBe(before);
  });

  it.each([undefined, null])('keeps legacy rotation %j distinct from explicit empty', BeardRotation => {
    const source: JsonObject = { ...newPreset(), BeardPriority: [3, 6] };
    if (BeardRotation === undefined) delete source.BeardRotation; else source.BeardRotation = BeardRotation;
    expect(presetBeards(source)).toEqual({ enabled: [3, 6], rng: [], legacy: true });
    expect(normalizePreset(source).BeardRotation).toBe(BeardRotation);
    expect((presetArgs(source).preset as JsonObject).BeardRotation).toBe(BeardRotation);
    expect(presetBeards({ ...source, BeardPriority: [], BeardRotation: [] })).toEqual({ enabled: [], rng: [], legacy: false });
  });

  it('moves a beard between exclusive pools without changing other priorities', () => {
    const source = newPreset();
    const before = stringifyJson(source);
    const rng = setPresetBeard(source, 3, 'rng');
    expect(rng.BeardPriority).toEqual([1, 5, 6]);
    expect(rng.BeardRotation).toEqual([0, 2, 4, 3]);
    const enabled = setPresetBeard(rng, 0, 'enabled');
    expect(enabled.BeardPriority).toEqual([1, 5, 6, 0]);
    expect(enabled.BeardRotation).toEqual([2, 4, 3]);
    const off = setPresetBeard(enabled, 6, 'off');
    expect(off.BeardPriority).toEqual([1, 5, 0]);
    expect(off.BeardRotation).toEqual([2, 4, 3]);
    expect(validatePreset(off)).toBeUndefined();
    expect(stringifyJson(source)).toBe(before);
  });

  it('converts legacy automatic beards only on an explicit edit', () => {
    const old = normalizePreset({ Id: 'legacy', Name: 'Legacy' });
    expect(presetBeards(old)).toEqual({ enabled: activityPolicies.coreBeards, rng: activityPolicies.optionalBeards, legacy: true });
    expect(old).not.toHaveProperty('BeardRotation');
    expect(setPresetBeard(old, 0, 'off')).toMatchObject({ BeardPriority: [1, 3, 5, 6], BeardRotation: [2, 4] });
    expect(defaultPresetBeards()).toEqual({ BeardPriority: activityPolicies.coreBeards, BeardRotation: activityPolicies.optionalBeards });
  });

  it.each<JsonValue>(['3', false, {}, [7], [-1], [0.5], [0, 0], [3]])('rejects invalid or overlapping RNG %j', BeardRotation => {
    expect(() => validatePreset({ ...newPreset(), BeardRotation })).toThrow();
  });

  it('accepts explicitly disabled beards without restoring defaults', () => {
    const source = { ...newPreset(), BeardPriority: [], BeardRotation: [] };
    expect(validatePreset(source)).toBeUndefined();
    expect(normalizePreset(source)).toMatchObject({ BeardPriority: [], BeardRotation: [] });
    expect(presetArgs(source).preset).toMatchObject({ BeardPriority: [], BeardRotation: [] });
    expect(beardNames([0, 3, 6])).toBe('Fu Manchu, NGU, Gold');
    expect(beardNames([])).toBe('');
    expect(beardNames(undefined)).toBe('Unknown');
  });

  it('populates diggers from normalized objective priorities', () => {
    expect(presetDiggerWeights(newPreset())).toEqual([0, 0, 0, 0, .5, .5, .1, .1, 0, 0, 0, 0]);
    expect(presetDiggerWeights({ ...newPreset(), Weights: [0, 0, 2, 1, 0, 1, 0, 0, 0, 0, 0, 0] })).toEqual([.25, 0, 0, .5, 0, 0, 0, 0, .25, 0, .5, 0]);
  });
  it.each([['builtin_energyCap', 12], ['builtin_magicCap', 13]] as const)('retains exclusive cap defaults and current support for %s', (id, index) => {
    const preset = normalizePreset({ ...definitions.defaults, AdventureDynamicWeight: 0, ...definitions.presets.find(row => row.Id === id)! });
    expect(preset).toMatchObject({ Id: id, AutoUpdate: true, SupportMode: 'current', AdventureDynamicWeight: 0, Weights: Array.from({ length: 14 }, (_, i) => i === index ? 1 : 0) });
    expect(validatePreset(preset)).toBeUndefined();
    expect(presetDiggerWeights(preset)).toEqual(Array(12).fill(0));
    expect(presetArgs(preset).preset).toMatchObject(preset);
  });

  it('requires resolved dynamic goals before materializing automatic diggers', () => {
    const preset = { ...newPreset(), AdventureDynamicWeight: 2, Weights: Array(12).fill(0) };
    expect(presetDiggerWeights(preset)).toEqual([]);
    const effective = [0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0];
    expect(presetDiggerWeights(preset, effective)).toEqual([0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0]);
    expect(presetDiggerWeights({ ...preset, DiggerWeights: Array(12).fill(3) })).toEqual(Array(12).fill(3));
    expect(preset.Weights).toEqual(Array(12).fill(0));
  });

  it.each(['MinimumPower', 'MinimumToughness'])('includes adventure funding for %s constraints', key => {
    const preset = { ...newPreset(), [key]: 1 };
    expect(presetDiggerWeights(preset)).toEqual([0, 0, 0, 1, .5, .5, .1, .1, 0, 0, 0, 0]);
  });

  it('keeps explicit digger weights independent of objective edits', () => {
    const preset = newPreset();
    const weights = presetDiggerWeights(preset);
    weights[11] = 2;
    const edited = { ...preset, DiggerWeights: weights, Weights: [0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0] };
    expect(presetDiggerWeights(edited)).toEqual([0, 0, 0, 0, .5, .5, .1, .1, 0, 0, 0, 2]);
    expect(presetDiggerWeights({ ...edited, DiggerWeights: [] })).toEqual([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0]);
    expect(preset.DiggerWeights).toEqual([]);
    expect(presetDiggerWeights({ ...edited, DiggerWeights: Array(12).fill(0) })).toEqual(Array(12).fill(0));
  });

  it('does not round unsafe input tokens into valid integers', () => {
    for (const token of ['1.0000000000000001', '1e-400', '2147483647.0000001']) {
      expect(presetNumberInput(token)).toBe(token);
      expect(() => validatePreset({ ...newPreset(), SwapSeconds: presetNumberInput(token) })).toThrow();
    }
  });

  it('compares known double formatting by value while retaining unknown exact tokens', () => {
    const source: JsonObject = { ...newPreset(), Weights: [1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0], DiggerWeights: Array(12).fill(1 / 3), MinimumPower: 1 / 3, Future: parseJson('0.33333333333333331') };
    const roundtrip = cloneObject(source);
    roundtrip.DiggerWeights = Array(12).fill(parseJson('0.33333333333333331'));
    roundtrip.MinimumPower = parseJson('0.33333333333333331');
    expect(normalizePreset(roundtrip)).toEqual({ ...source, Weights: [...source.Weights as number[], 0, 0] });
    expect(String(normalizePreset(roundtrip).Future)).toBe('0.33333333333333331');
    expect(String((roundtrip.DiggerWeights as JsonValue[])[0])).toBe('0.33333333333333331');
  });
});
