import { describe, expect, it, vi } from 'vitest';
import { render } from 'svelte/server';
import AutoExp from '../src/lib/AutoExp.svelte';
import { stringifyJson, type JsonObject } from '../src/lib/api';
import { autoExpRatioDefaults, validateSettings } from '../src/lib/model';

const fields = [
  ['_customExpEnergyRatio', 'Energy BASE cap (E:M) ratio', 5],
  ['_customExpMagicRatio', 'Magic BASE cap (E:M) ratio', 1],
  ['_customExpEnergyPowerRatio', 'Energy BASE Power ratio', 5],
  ['_customExpEnergyCapRatio', 'Energy BASE Cap ratio', 160000],
  ['_customExpEnergyBarsRatio', 'Energy BASE Bars ratio', 4],
  ['_customExpMagicPowerRatio', 'Magic BASE Power ratio', 5],
  ['_customExpMagicCapRatio', 'Magic BASE Cap ratio', 160000],
  ['_customExpMagicBarsRatio', 'Magic BASE Bars ratio', 4]
] as const;

describe('Auto use EXP settings', () => {
  it('shows off-by-default setup controls without migrating or saving on render', () => {
    const settings: JsonObject = { Future: { keep: 42 } };
    const onedit = vi.fn();
    const { body } = render(AutoExp, { props: { settings, onedit } });
    expect(autoExpRatioDefaults).toEqual(Object.fromEntries(fields.map(([key, , value]) => [key, value])));
    expect(body).toMatch(/type="checkbox" aria-label="Auto use EXP"[^>]*>/);
    expect(body.match(/<input[^>]*type="checkbox"[^>]*>/)?.[0]).not.toContain('checked');
    expect(body.match(/<input\b/g)).toHaveLength(9);
    for (const [, label, value] of fields) {
      expect(body).toContain(`aria-label="${label}" type="number" min="1" max="1000000000" step="1" value="${value}"`);
    }
    expect(body).not.toMatch(/\bdisabled\b|\binert\b/);
    expect(body).toMatch(/id="auto-exp-help">[^<]*base stats, not gear totals or EXP spending/);
    expect(body).toMatch(/<details[^>]*><summary[^>]*>Purchase rules<\/summary>/);
    expect(body).not.toMatch(/<details[^>]*\bopen\b/);
    const visible = body.split('<details')[0].replace(/<!--[^]*?-->/g, '').replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ');
    expect(visible.match(/Auto use EXP/g)).toHaveLength(1);
    expect(body.match(/<fieldset\b/g)).toHaveLength(3);
    expect(body.match(/<fieldset[^>]*aria-describedby="auto-exp-help"/g)).toHaveLength(3);
    expect(onedit).not.toHaveBeenCalled();
    expect(settings).toEqual({ Future: { keep: 42 } });
  });

  it('renders saved ratios independently while the toggle is off', () => {
    const settings = { _customAutoExpEnabled: false, ...Object.fromEntries(fields.map(([key], index) => [key, index + 11])) };
    const before = stringifyJson(settings);
    const { body } = render(AutoExp, { props: { settings, onedit: vi.fn() } });
    fields.forEach(([, label], index) => expect(body).toContain(`aria-label="${label}" type="number" min="1" max="1000000000" step="1" value="${index + 11}"`));
    expect(body).not.toMatch(/\bdisabled\b|\binert\b/);
    expect(stringifyJson(settings)).toBe(before);
  });

  it('preserves an enabled toggle and the disabled editing boundary', () => {
    const onedit = vi.fn();
    const { body } = render(AutoExp, { props: { settings: { _customAutoExpEnabled: true }, disabled: true, onedit } });
    expect(body).toMatch(/<article[^>]*\binert\b/);
    expect(body.match(/<input[^>]*type="checkbox"[^>]*>/)?.[0]).toContain('checked');
    expect(body.match(/<input\b/g)).toHaveLength(9);
    expect(onedit).not.toHaveBeenCalled();
  });

  it.each(fields)('validates the full range for %s with no existing setting', (key, _label, defaultValue) => {
    for (const value of [1, defaultValue, 1000000000]) {
      expect(validateSettings({ [key]: value }, {})).toBeUndefined();
    }
    for (const value of [0, -1, 1.5, 1000000001, '', '160000', '160k', null, false, [], {}, NaN, Infinity]) {
      expect(() => validateSettings({ [key]: value }, {})).toThrow(key);
    }
  });

  it('enforces a strict toggle boolean even for a missing or malformed legacy field', () => {
    for (const value of [false, true]) expect(validateSettings({ _customAutoExpEnabled: value }, {})).toBeUndefined();
    for (const value of [0, 1, '', 'true', null, [], {}]) {
      for (const current of [{}, { _customAutoExpEnabled: 'old' }] as JsonObject[]) {
        expect(() => validateSettings({ _customAutoExpEnabled: value }, current)).toThrow('_customAutoExpEnabled must be boolean');
      }
    }
  });
});
