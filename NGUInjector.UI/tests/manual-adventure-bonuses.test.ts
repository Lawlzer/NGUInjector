import { describe, expect, it, vi } from 'vitest';
import { render } from 'svelte/server';
import ManualAdventureBonuses from '../src/lib/ManualAdventureBonuses.svelte';
import { parseJson, stringifyJson, type JsonObject } from '../src/lib/api';
import { manualAdventureWeightCounts, validateSettings } from '../src/lib/model';
import { beardOptions, diggerNames, presetNumberInput } from '../src/lib/presets';

describe('Manual Adventure additive bonuses', () => {
  it.each(Object.entries(manualAdventureWeightCounts))('validates exact finite double vector %s', (key, count) => {
    for (const value of [0, .125, 100, 1e6]) expect(validateSettings({ [key]: Array(count).fill(value) }, {})).toBeUndefined();
    for (const value of [null, false, 0, {}, [], Array(count - 1).fill(0), Array(count + 1).fill(0),
      ...[-1, 1000001, NaN, Infinity, '', '100', null, true, {}].map(weight => Array(count).fill(weight))]) {
      expect(() => validateSettings({ [key]: value }, {})).toThrow(key);
    }
    expect(presetNumberInput('+100')).toBe(100);
    expect(presetNumberInput('0.125')).toBe(.125);
    expect(presetNumberInput('')).toBe('');
  });

  it('renders a closed optional disclosure with zero defaults even while disabled, without writing', () => {
    const settings: JsonObject = { _manualAdventureEnabled: false, Future: parseJson('1.234567890123456789') };
    const before = stringifyJson(settings);
    const onedit = vi.fn();
    const { body } = render(ManualAdventureBonuses, { props: { settings, onedit } });
    expect(body).toContain('<summary>Manual adventure bonuses</summary>');
    expect(body).not.toMatch(/<details[^>]*\bopen\b/);
    expect(body.match(/<input\b/g)).toHaveLength(24);
    expect(body.match(/value="0"/g)).toHaveLength(24);
    for (const name of ['Push', 'Adventure power', 'Adventure toughness', 'Drop chance', 'Gold']) expect(body).toContain(`aria-label="Gear weights ${name} bonus"`);
    for (const beard of beardOptions) expect(body).toContain(`aria-label="Beard priorities ${beard.name} bonus"`);
    for (const name of diggerNames) expect(body).toContain(`aria-label="Digger priorities ${name} bonus"`);
    expect(body).not.toMatch(/autosave|takeover|recall energy|<p\b/i);
    expect(body).not.toMatch(/\bdisabled\b/);
    expect(onedit).not.toHaveBeenCalled();
    expect(stringifyJson(settings)).toBe(before);
  });

  it('keeps supplied values and reports invalid drafts instead of silently resetting them', () => {
    const settings: JsonObject = { _manualAdventureGearWeights: [100, .125, '', 0, 1e6] };
    const before = stringifyJson(settings);
    const { body } = render(ManualAdventureBonuses, { props: { settings, onedit: vi.fn() } });
    expect(body).toContain('value="100"');
    expect(body).toContain('value="0.125"');
    expect(body).toContain('value=""');
    expect(body).toContain('role="alert"');
    expect(body).toContain('_manualAdventureGearWeights[2]');
    expect(stringifyJson(settings)).toBe(before);
  });
});
