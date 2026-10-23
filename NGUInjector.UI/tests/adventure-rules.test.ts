import { describe, expect, it, vi } from 'vitest';
import { render } from 'svelte/server';
import AdventureRules from '../src/lib/AdventureRules.svelte';
import { parseJson, stringifyJson, type JsonObject, type JsonValue } from '../src/lib/api';
import { diffObject, freshEdits, validateSettings } from '../src/lib/model';

const rule = (fields: JsonObject = {}): JsonObject => ({ Enabled: true, MinPower: 0, MinToughness: 0, MinRegen: 0, Mode: 0, Zone: 0, ...fields });
const validate = (rules: JsonValue) => validateSettings({ _customAdventureRules: rules }, {});

describe('global adventure rule validation', () => {
  it('accepts empty defaults, every mode and zone boundary, and 32 ordered rules without mutation', () => {
    expect(validate([])).toBeUndefined();
    const rules = [0, 1, 2, 4].flatMap(Mode => [-1, 0, 43, 1000].flatMap(Zone => [false, true].map(Enabled => rule({ Mode, Zone, Enabled, MinPower: .125, MinToughness: 1e100, MinRegen: Number.MAX_VALUE }))));
    const before = stringifyJson(rules);
    expect(validate(rules)).toBeUndefined();
    expect(stringifyJson(rules)).toBe(before);
  });

  it.each([null, false, 0, '', {}, [null], [[]], Array.from({ length: 33 }, () => rule())].map(rules => ({ rules })))('rejects invalid rule containers: $rules', ({ rules }) => {
    expect(() => validate(rules)).toThrow('_customAdventureRules');
  });

  it('requires exactly the six case-sensitive keys, even on disabled rules', () => {
    for (const key of Object.keys(rule())) {
      const missing = rule({ Enabled: false });
      delete missing[key];
      expect(() => validate([missing])).toThrow('must contain exactly');
      expect(() => validate([{ ...missing, [key.toLowerCase()]: rule()[key] }])).toThrow('must contain exactly');
    }
    expect(() => validate([rule({ Future: 1 })])).toThrow('must contain exactly');
  });

  it('requires Enabled to be boolean', () => {
    for (const Enabled of [null, 0, 1, 'true', [], {}]) expect(() => validate([rule({ Enabled })])).toThrow('Enabled');
  });

  it.each(['MinPower', 'MinToughness', 'MinRegen'])('requires finite nonnegative numbers for %s', key => {
    for (const value of [-1, -.001, NaN, Infinity, -Infinity, '', '100', null, true, [], {}]) {
      expect(() => validate([rule({ [key]: value })])).toThrow(key);
    }
  });

  it('rejects unsupported modes and zones regardless of whether the mode uses a zone', () => {
    for (const Mode of [-1, 3, 5, 1.5, '1', null, true, NaN, Infinity, parseJson('1.0000000000000001')]) {
      expect(() => validate([rule({ Mode })])).toThrow('Mode');
    }
    for (const Zone of [-2, 44, 999, 1001, .5, '1000', null, true, NaN, Infinity]) {
      expect(() => validate([rule({ Zone })])).toThrow('Zone');
    }
  });

  it('roundtrips rules atomically while preserving unrelated unknown settings', () => {
    const current: JsonObject = { _customAdventureMode: 3, _customAdventureZone: 12, _manualAdventureEnabled: false, Future: parseJson('{"counter":9007199254740993}') };
    const draft = { ...current, _customAdventureRules: [rule({ Mode: 4, Zone: 43 })] };
    const patch = diffObject(current, draft);
    expect(validateSettings(patch, current)).toBeUndefined();
    expect(patch).toEqual({ _customAdventureRules: draft._customAdventureRules });
    expect(freshEdits(current, draft, { ...current, AddedLater: true })).toEqual({ ...draft, AddedLater: true });
    expect(stringifyJson(current.Future)).toBe('{"counter":9007199254740993}');
    expect(current).not.toHaveProperty('_customAdventureRules');
  });
});

describe('adventure rule editor rendering', () => {
  it('defaults to no rules without writing settings or requiring runtime support', () => {
    const settings = { Future: 42 };
    const onedit = vi.fn();
    const { body } = render(AdventureRules, { props: { settings, connected: true, onedit } });
    expect(body).toContain('0/32');
    expect(body).toContain('Live rule status unavailable.');
    expect(body).toMatch(/<button[^>]*>Add rule<\/button>/);
    expect(body).not.toContain('<fieldset');
    expect(settings).toEqual({ Future: 42 });
    expect(onedit).not.toHaveBeenCalled();
  });

  it('renders ordered saved targets and the backend match without predicting it', () => {
    const settings = { _customAdventureRules: [rule({ Mode: 4, Zone: 43, MinPower: 999 }), rule({ Mode: 2 })] };
    const { body } = render(AdventureRules, { props: { settings, connected: true, telemetry: { power: 1, toughness: 2, regen: 3, matchedRuleIndex: 0 }, onedit: vi.fn() } });
    expect(body.match(/<fieldset\b/g)).toHaveLength(2);
    expect(body.match(/<select\b/g)).toHaveLength(3);
    expect(body).toMatch(/Minimum power<input[^>]*min="0"[^>]*value="999"/);
    expect(body).toContain('<option value="43" selected="">');
    expect(body).toMatch(/aria-label="Move rule 1 up"[^>]*disabled/);
    expect(body).toMatch(/aria-label="Move rule 2 down"[^>]*disabled/);
    expect(body).toContain('First matching rule: 1');
    expect(body).toContain('Power 1 / Toughness 2 / Regen 3');
    expect(body).not.toContain('Max ITOPOD push</option>');
  });

  it('does not attribute saved match indices to a pending order', () => {
    const { body } = render(AdventureRules, { props: { settings: { _manualAdventureEnabled: true }, connected: true, pending: true, telemetry: { matchedRuleIndex: 0 }, onedit: vi.fn() } });
    expect(body).toMatch(/<p\b[^>]*class="[^"]*\bnotice\b[^"]*"[^>]*>Manual Adventure overrides these rules\.<\/p>/);
    expect(body).toContain('Rule changes pending.');
    expect(body).not.toMatch(/saving changes|saved rules|autosave/i);
    expect(body).not.toContain('First matching rule: 1');
  });

  it('shows no-match fallback and hides stale telemetry offline', () => {
    const props = { settings: {}, telemetry: { power: 1, matchedRuleIndex: -1 }, onedit: vi.fn() };
    expect(render(AdventureRules, { props: { ...props, connected: true } }).body).toContain('No matching rule; Adventure mode fallback.');
    const { body } = render(AdventureRules, { props });
    expect(body).toContain('Connect to see live stats');
    expect(body).not.toContain('Equipped stats:');
  });
});
