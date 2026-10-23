import { describe, expect, it } from 'vitest';
import { parseJson, stringifyJson, type JsonObject } from '../src/lib/api';
import { validateSettings } from '../src/lib/model';
import { adventureModes } from '../src/lib/options';

describe('Max ITOPOD push Adventure mode', () => {
  it('appends mode 3 without renumbering existing modes', () => {
    expect(adventureModes).toEqual([
      { value: 0, label: 'Optimal ITOPOD' },
      { value: 1, label: 'Farm selected zone' },
      { value: 2, label: 'Farm boosts dynamically' },
      { value: 3, label: 'Max ITOPOD push' }
    ]);
  });

  it.each([
    ['_customAdventureMode', [0, 1, 2, 3], [-1, 4]],
    ['_customAdventureMaxItopodDeaths', [1, 3, 1000], [-1, 0, 1001, 2147483648]]
  ] as const)('validates %s even when absent from saved settings', (key, valid, invalid) => {
    for (const value of valid) expect(validateSettings({ [key]: value }, {})).toBeUndefined();
    for (const value of [...invalid, 1.5, '', '3', null, true, [], {}, NaN, Infinity, parseJson('1.0000000000000001')]) {
      expect(() => validateSettings({ [key]: value }, {})).toThrow(key);
    }
  });

  it('validates saved push settings without migrating unrelated or unknown fields', () => {
    const current: JsonObject = { _customAdventureMode: 1, _customAdventureZone: 12, _manualAdventureZone: 4, Future: { keep: 42 } };
    const before = stringifyJson(current);
    const patch = { _customAdventureMode: 3, _customAdventureMaxItopodDeaths: 7 };
    expect(validateSettings(patch, current)).toBeUndefined();
    expect(stringifyJson(current)).toBe(before);
    expect(patch).toEqual({ _customAdventureMode: 3, _customAdventureMaxItopodDeaths: 7 });
  });
});
