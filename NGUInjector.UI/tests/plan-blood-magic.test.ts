import { describe, expect, it } from 'vitest';
import { LosslessNumber } from 'lossless-json';
import type { JsonObject } from '../src/lib/api';
import { validatePlan } from '../src/lib/model';

const plan: JsonObject = { Version: 14, __externalUiRevision: 'a'.repeat(64), Stages: [{ Percent: 100 }], Future: { keep: true } };

describe('optional plan Blood Magic validation', () => {
  it('does not materialize inherited fields or mutate unknown data', () => {
    const before = structuredClone(plan);
    validatePlan(plan);
    expect(plan).toEqual(before);
  });

  for (const key of ['AutoBloodSpellsEnabled', 'BloodSpaghettiGoalPercent', 'BloodCounterfeitGoalPercent', 'IronPillSaveSeconds', 'IronPillMinWorth']) {
    it(`${key} accepts null without substituting a default`, () => {
      const draft = { ...plan, [key]: null };
      validatePlan(draft);
      expect(draft[key]).toBeNull();
    });
  }

  for (const key of ['AutoBloodSpellsEnabled', 'AutoIronPillEnabled']) {
    it.each([false, true])(`${key} preserves %s`, value => {
      const draft = { ...plan, [key]: value };
      validatePlan(draft);
      expect(draft[key]).toBe(value);
    });
    it.each([0, 1, 'false', {}, []])(`${key} rejects %j`, value => {
      expect(() => validatePlan({ ...plan, [key]: value })).toThrow(key);
    });
  }
  it('does not make the existing non-nullable Iron Pill permission nullable', () => {
    expect(() => validatePlan({ ...plan, AutoIronPillEnabled: null })).toThrow('AutoIronPillEnabled');
  });

  for (const [key, max, integer] of [
    ['BloodSpaghettiGoalPercent', 100000, true], ['BloodCounterfeitGoalPercent', 100000, true],
    ['IronPillSaveSeconds', 604800, true], ['IronPillMinWorth', 1e8, false]
  ] as const) {
    it.each([0, max])(`${key} accepts boundary %s`, value => {
      const draft = { ...plan, [key]: value };
      validatePlan(draft);
      expect(draft[key]).toBe(value);
    });
    it.each([-1, max + 1, NaN, Infinity, -Infinity, '0', false, {}, []])(`${key} rejects %j`, value => {
      expect(() => validatePlan({ ...plan, [key]: value })).toThrow(key);
    });
    it(`${key} ${integer ? 'rejects' : 'accepts'} fractions`, () => {
      const validate = () => validatePlan({ ...plan, [key]: 0.25 });
      if (integer) expect(validate).toThrow(key);
      else expect(validate).not.toThrow();
    });
  }
  it('accepts lossless finite double tokens without rewriting them', () => {
    const worth = new LosslessNumber('0.1234567890123456789');
    const draft = { ...plan, IronPillMinWorth: worth };
    validatePlan(draft);
    expect(draft.IronPillMinWorth).toBe(worth);
  });
});
