import { describe, expect, it, vi } from 'vitest';
import { render } from 'svelte/server';
import { parseJson, stringifyJson, type JsonObject } from '../src/lib/api';
import NguWeights from '../src/lib/NguWeights.svelte';
import PlanEditor from '../src/lib/PlanEditor.svelte';
import { INT_MAX, validatePlan } from '../src/lib/model';
import { energyNguNames, magicNguNames } from '../src/lib/options';

const plan = (): JsonObject => ({
  Version: 16, Name: 'selected', __externalUiRevision: 'a'.repeat(64),
  EnergyNguWeights: [0, 0, 0, 0, 1, 0, 1, 0, 0], MagicNguWeights: [1, 0, 0, 0, 0, 0, 0],
  StageDurationSeconds: 1800, Stages: [{ Name: 'Fixed', Percent: 100, DurationSeconds: 60, DurationUnit: 's' }]
});

describe('NGU weight editor', () => {
  it('renders every labeled integer control and saved value without mutating or emitting edits', () => {
    const source = plan();
    const before = stringifyJson(source);
    const onchange = vi.fn();
    const { body } = render(NguWeights, { props: { plan: source, name: 'selected', activePlanName: 'active', dirty: false, onchange } });
    expect(body).toContain('aria-label="NGU weights"');
    expect(body).toMatch(/<h[23][^>]*>NGU weights<\/h[23]>/);
    for (const [resource, names, values] of [['Energy', energyNguNames, source.EnergyNguWeights], ['Magic', magicNguNames, source.MagicNguWeights]] as const) {
      names.forEach((name, index) => expect(body).toContain(`aria-label="${resource} ${name} weight" type="number" min="0" max="2147483647" step="1" value="${(values as number[])[index]}"`));
    }
    expect(body).toContain('0 disables a target. Positive weights are ratios, not percentages: 1:1');
    expect(body).toContain('All-zero weights skip generic NGU');
    expect(body).toContain('Editing plan');
    expect(body).toContain('Active plan: active. Edits apply to selected');
    expect(body).not.toContain('NGU goal');
    expect(onchange).not.toHaveBeenCalled();
    expect(stringifyJson(source)).toBe(before);
  });

  it('keeps the weights form out of the AFK plan editor without mutating or emitting edits', () => {
    const source = plan();
    const before = stringifyJson(source);
    const onchange = vi.fn();
    const { body } = render(PlanEditor, { props: { plan: source, name: 'selected', dirty: false, onchange } });
    expect(body).not.toContain('aria-label="NGU weights"');
    expect(body).not.toMatch(/aria-label="(?:Energy|Magic) [^"]+ weight"/);
    expect(onchange).not.toHaveBeenCalled();
    expect(stringifyJson(source)).toBe(before);
  });

  it.each([15, 16])('requires reload for legacy/missing arrays (v%s), never synthesizing defaults', version => {
    const source: JsonObject = { ...plan(), Version: version };
    delete source.EnergyNguWeights;
    const onchange = vi.fn();
    const { body } = render(NguWeights, { props: { plan: source, name: 'selected', dirty: false, onchange } });
    expect(body).toContain('reload this plan; the editor does not migrate legacy plans.');
    expect(body).not.toContain('aria-label="Energy Adventure Alpha weight"');
    expect(source.EnergyNguWeights).toBeUndefined();
    expect(onchange).not.toHaveBeenCalled();
  });

  it('disables weighted controls and explains missing runtime capability', () => {
    const { body } = render(NguWeights, { props: { plan: plan(), name: 'selected', dirty: false, weightsWritable: false, onchange: vi.fn() } });
    expect(body).toContain('advertising planVersion 16 or newer');
    expect(body).toMatch(/aria-label="Energy Adventure Alpha weight"[^>]*disabled/);
  });

  it.each(['1e-400', '1.0000000000000001'])('retains invalid exact weight token %s in SSR without rounding or emitting edits', token => {
    for (const value of [token, parseJson(token)]) {
      const source = { ...plan(), MagicNguWeights: [value, 0, 0, 0, 0, 0, 0] };
      const before = stringifyJson(source);
      const onchange = vi.fn();
      const { body } = render(NguWeights, { props: { plan: source, name: 'selected', dirty: true, onchange } });
      expect(body).toMatch(/role="alert">MagicNguWeights\[0\] must be a finite integer/);
      expect(body).toContain(`aria-label="Magic Yggdrasil weight" type="number" min="0" max="2147483647" step="1" value="${token}"`);
      expect(stringifyJson(source)).toBe(before);
      expect(onchange).not.toHaveBeenCalled();
    }
  });
});

describe('version 16 weight validation', () => {
  it.each(['EnergyNguWeights', 'MagicNguWeights'])('requires the exact %s array length', key => {
    for (const value of [undefined, null, [], [1], new Array(key === 'EnergyNguWeights' ? 10 : 8).fill(0), 'bad']) {
      expect(() => validatePlan({ ...plan(), [key]: value } as JsonObject)).toThrow(`${key} must contain exactly`);
    }
  });
  it.each(['', '1', null, -1, .5, INT_MAX + 1, Infinity, NaN])('rejects non-Int32 weight %s', invalid => {
    expect(() => validatePlan({ ...plan(), MagicNguWeights: [invalid, 0, 0, 0, 0, 0, 0] })).toThrow('MagicNguWeights[0]');
  });
  it('accepts all zero, arbitrary ratios and Int32 maximum without normalization', () => {
    const source = { ...plan(), EnergyNguWeights: [INT_MAX, 0, 0, 0, 7, 0, 3, 0, 0], MagicNguWeights: [0, 0, 0, 0, 0, 0, 0] };
    const before = stringifyJson(source);
    expect(() => validatePlan(source)).not.toThrow();
    expect(stringifyJson(source)).toBe(before);
  });
  it('does not require or inject weights when validating a legacy duration plan', () => {
    const source = plan(); source.Version = 15; source.NguGoal = 1;
    delete source.EnergyNguWeights; delete source.MagicNguWeights;
    const before = stringifyJson(source);
    expect(() => validatePlan(source)).not.toThrow();
    expect(stringifyJson(source)).toBe(before);
  });
  it.each([14, 15])('rejects either weight field on version %s plans with reload guidance', Version => {
    for (const key of ['EnergyNguWeights', 'MagicNguWeights']) {
      for (const value of [null, [], [1]]) {
        const source = plan(); source.Version = Version;
        delete source.EnergyNguWeights; delete source.MagicNguWeights;
        source[key] = value;
        const before = stringifyJson(source);
        expect(() => validatePlan(source)).toThrow('NGU weight fields require a version 16 plan. Connect to the updated injector and reload the plan.');
        expect(stringifyJson(source)).toBe(before);
      }
    }
  });
});
