import { describe, expect, it, vi } from 'vitest';
import { render } from 'svelte/server';
import { parseJson, stringifyJson, stripNguOverrides, type JsonObject } from '../src/lib/api';
import NguWeights from '../src/lib/NguWeights.svelte';
import PlanEditor from '../src/lib/PlanEditor.svelte';
import { INT_MAX, validatePlan, validateSettings } from '../src/lib/model';
import { energyNguNames, magicNguNames } from '../src/lib/options';

const settings = (): JsonObject => ({
  _energyNguWeights: [0, 0, 0, 0, 1, 0, 1, 0, 0], _magicNguWeights: [1, 0, 0, 0, 0, 0, 0]
});
const plan = (): JsonObject => ({
  Version: 17, Name: 'selected', __externalUiRevision: 'a'.repeat(64),
  StageDurationSeconds: 1800, Stages: [{ Name: 'Fixed', Percent: 100, DurationSeconds: 60, DurationUnit: 's' }]
});

describe('global NGU weight editor', () => {
  it('renders backend settings with no selected plan or mount-time writes', () => {
    const source = settings(), before = stringifyJson(source), onedit = vi.fn();
    const { body } = render(NguWeights, { props: { settings: source, onedit } });
    expect(body).toContain('Global weights for all plans, presets, and Quick actions.');
    expect(body).not.toMatch(/autosave|save automatically|storage|polling/i);
    for (const [key, resource, names] of [['_energyNguWeights', 'Energy', energyNguNames], ['_magicNguWeights', 'Magic', magicNguNames]] as const) {
      names.forEach((name, index) => expect(body).toContain(`aria-label="${resource} ${name} weight" type="number" min="0" max="2147483647" step="1" value="${(source[key] as number[])[index]}"`));
    }
    expect(energyNguNames[5]).toBe('Power');
    expect(body).not.toContain('Power Alpha');
    expect(body).not.toContain('Plan:');
    expect(body.match(/<fieldset\b/g)).toHaveLength(2);
    expect(onedit).not.toHaveBeenCalled();
    expect(stringifyJson(source)).toBe(before);
  });

  it('keeps NGU controls out of the plan editor', () => {
    const source = plan(), onchange = vi.fn();
    const { body } = render(PlanEditor, { props: { plan: source, name: 'selected', dirty: false, onchange } });
    expect(body).not.toContain('aria-label="NGU weights"');
    expect(body).not.toMatch(/aria-label="(?:Energy|Magic) [^"]+ weight"/);
    expect(onchange).not.toHaveBeenCalled();
  });

  it.each(['_energyNguWeights', '_magicNguWeights'])('never invents missing %s defaults', key => {
    const source = settings(); delete source[key];
    const onedit = vi.fn();
    const { body } = render(NguWeights, { props: { settings: source, onedit } });
    expect(body).toContain('Global NGU weights unavailable. Update the injector and reconnect to load settings.');
    expect(body).not.toContain('<input');
    expect(source).not.toHaveProperty(key);
    expect(onedit).not.toHaveBeenCalled();
  });

  it('disables controls when the runtime does not advertise global weights', () => {
    const { body } = render(NguWeights, { props: { settings: settings(), weightsWritable: false, onedit: vi.fn() } });
    expect(body).toContain('Weight editing unavailable. Update the injector, reconnect, and reload.');
    expect(body.match(/<input\b[^>]*disabled/g)).toHaveLength(16);
  });

  it.each(['1e-400', '1.0000000000000001'])('does not round exact invalid weight %s', token => {
    for (const value of [token, parseJson(token)]) {
      const source = { ...settings(), _magicNguWeights: [value, 0, 0, 0, 0, 0, 0] };
      const before = stringifyJson(source), onedit = vi.fn();
      const { body } = render(NguWeights, { props: { settings: source, onedit } });
      expect(body).toMatch(/role="alert">_magicNguWeights\[0\] must be a finite integer/);
      expect(body).toContain(`step="1" value="${token}"`);
      expect(stringifyJson(source)).toBe(before);
      expect(onedit).not.toHaveBeenCalled();
    }
  });
});

describe('global NGU settings validation', () => {
  it.each(['_energyNguWeights', '_magicNguWeights'])('requires exactly the defined %s length', key => {
    for (const value of [undefined, null, [], [1], Array(key === '_energyNguWeights' ? 10 : 8).fill(0), 'bad']) {
      expect(() => validateSettings({ [key]: value } as JsonObject, {})).toThrow(`${key} must contain exactly`);
    }
    expect(() => validateSettings({ [key]: Array(key === '_energyNguWeights' ? 9 : 7) }, {})).toThrow(`${key}[0]`);
  });
  it.each(['', '1', null, -1, .5, INT_MAX + 1, Infinity, NaN, parseJson('1.0000000000000001'), parseJson('1e-400')])('rejects non-Int32 weight %s', invalid => {
    for (const [key, count] of [['_energyNguWeights', 9], ['_magicNguWeights', 7]] as const) {
      expect(() => validateSettings({ [key]: [invalid, ...Array(count - 1).fill(0)] }, {})).toThrow(`${key}[0]`);
    }
  });
  it('accepts zero and Int32 maximum without normalizing the settings', () => {
    const source = { _energyNguWeights: [INT_MAX, 0, 0, 0, 7, 0, 3, 0, 0], _magicNguWeights: Array(7).fill(0) };
    const before = stringifyJson(source);
    expect(validateSettings(source, {})).toBeUndefined();
    expect(stringifyJson(source)).toBe(before);
  });
  it.each([14, 15, 16, 17])('does not require NGU arrays on plan version %s', Version => {
    const source = { ...plan(), Version }, before = stringifyJson(source);
    expect(validatePlan(source)).toBeUndefined();
    expect(stringifyJson(source)).toBe(before);
  });
  it('strips retired plan, stage, and preset overrides without migrating them into settings', () => {
    const retired = { NguGoal: 1, EnergyNguWeights: [2], MagicNguWeights: null };
    const source = { ...plan(), ...retired, Stages: [{ Name: 'Stage', ...retired }], LoadoutPresets: [{ Id: 'old', ...retired, AdvancedTrainingWeights: [1, 1, 0, 0, 0] }], Future: { EnergyNguWeights: 'unknown nested data', exact: parseJson('9007199254740993') } };
    const before = stringifyJson(source);
    expect(stripNguOverrides(source)).toEqual({ ...plan(), Stages: [{ Name: 'Stage' }], LoadoutPresets: [{ Id: 'old', AdvancedTrainingWeights: [1, 1, 0, 0, 0] }], Future: source.Future });
    expect(stringifyJson(source)).toBe(before);
  });
  it('retains clean row identities while copying only branches with retired overrides', () => {
    const stage = Object.freeze({ Name: 'Selected stage', PresetId: '@active' });
    const clean = Object.freeze({ Stages: [stage] });
    Object.freeze(clean.Stages);
    expect(stripNguOverrides(clean)).toBe(clean);
    const retired = Object.freeze({ Name: 'Retired stage', EnergyNguWeights: [2] });
    const source = Object.freeze({ Stages: [stage, retired], GoalPresets: clean.Stages });
    Object.freeze(source.Stages);
    const result = stripNguOverrides(source);
    expect(result).not.toBe(source);
    expect(result.Stages).not.toBe(source.Stages);
    expect((result.Stages as JsonObject[])[0]).toBe(stage);
    expect((result.Stages as JsonObject[])[1]).toEqual({ Name: 'Retired stage' });
    expect(result.GoalPresets).toBe(clean.Stages);
    expect(retired.EnergyNguWeights).toEqual([2]);
    expect(stripNguOverrides(result)).toBe(result);
  });
});
