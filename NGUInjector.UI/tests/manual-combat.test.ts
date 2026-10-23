import { describe, expect, it, vi } from 'vitest';
import { render } from 'svelte/server';
import ManualTitanFights from '../src/lib/ManualTitanFights.svelte';
import { parseJson, stringifyJson } from '../src/lib/api';
import { validateSettings } from '../src/lib/model';
import { manualCombatLoadouts, titanZones } from '../src/lib/options';

describe('manual combat settings', () => {
  it.each([
    ['_manualAdventureLoadout', [-1, 0, 1, 2], [-2, 3]],
    ['_manualTitanLoadout', [-1, 0, 1, 2], [-2, 3]],
    ['_manualTitanMaxDeaths', [1, 3, 1000], [-1, 0, 1001]],
    ['_manualTitanZone', [6, 8, 11, 14, 16, 19, 23, 26, 30, 34, 38, 42], [-1, 0, 7, 15, 20, 43, 1000]]
  ] as const)('strictly validates %s when absent from legacy settings', (key, valid, invalid) => {
    for (const value of valid) expect(validateSettings({ [key]: value }, {})).toBeUndefined();
    for (const value of [...invalid, 1.5, '', '1', null, false, true, [], {}, NaN, Infinity, 2147483648, parseJson('1.0000000000000001')]) {
      expect(() => validateSettings({ [key]: value }, {})).toThrow(key);
    }
  });

  it.each(['_manualTitanEnabled', '_manualTitanRequireAT'])('requires boolean %s without relying on a saved value', key => {
    for (const value of [false, true]) expect(validateSettings({ [key]: value }, {})).toBeUndefined();
    for (const value of [null, 0, 1, 'true', [], {}]) expect(() => validateSettings({ [key]: value }, {})).toThrow(key);
  });

  it('exposes only supported titan IDs and the exact gear scores', () => {
    expect(titanZones.map(zone => zone.value)).toEqual([6, 8, 11, 14, 16, 19, 23, 26, 30, 34, 38, 42]);
    expect(manualCombatLoadouts).toEqual([
      { value: -1, label: 'Keep equipped' },
      { value: 0, label: 'sqrt(Power * Toughness) * Move Speed' },
      { value: 1, label: 'Power * Toughness * Move Speed' },
      { value: 2, label: '(Power + Toughness) * Move Speed' }
    ]);
  });
});

describe('manual titan card', () => {
  it('shows opt-in defaults and safety guidance without writing or enabling Manual Adventure', () => {
    const settings = { _manualAdventureEnabled: false, Future: 42 };
    const before = stringifyJson(settings);
    const onedit = vi.fn();
    const { body } = render(ManualTitanFights, { props: { settings, onedit } });
    expect(body).toMatch(/type="checkbox"(?![^>]*checked)[^>]*>/);
    expect(body).toContain('<option value="16" selected="">WALDERP</option>');
    expect(body).toContain('<option value="1" selected="">Power * Toughness * Move Speed</option>');
    expect(body).toMatch(/max="1000"[^>]*value="3"/);
    expect(body).toMatch(/aria-describedby="manual-titan-at-help"[^>]*checked/);
    expect(body).toMatch(/id="manual-titan-at-help">Set positive Power and Toughness targets in-game\./);
    expect(body.split('<details')[0]).toContain('Gear swaps recall energy, magic, and R3');
    const limits = body.match(/<details([^>]*)><summary>Attempt limits<\/summary>([\s\S]*?)<\/details>/);
    expect(limits).not.toBeNull();
    expect(limits![1]).not.toMatch(/\bopen\b/);
    for (const warning of ['per-spawn death limit', '10 minutes of active attempts', 'Resumes on the next spawn', 'Find Walderp in-game', 'resets the attempt and remaining deaths']) expect(limits![2]).toContain(warning);
    expect(body).toMatch(/role="status">Disconnected<\/p>/);
    expect(body).not.toMatch(/<button|arbitrary|weights/i);
    expect(stringifyJson(settings)).toBe(before);
    expect(onedit).not.toHaveBeenCalled();
  });

  it.each([true, false])('renders actual runtime state when active=%s, not the selected target or saved limit', active => {
    const { body } = render(ManualTitanFights, { props: {
      settings: { _manualTitanEnabled: false, _manualTitanZone: 6, _manualTitanMaxDeaths: 1000, _manualTitanRequireAT: false },
      connected: true, telemetry: { active, remainingDeaths: 2, zone: 16, status: 'Waiting for next spawn' }, onedit: vi.fn()
    } });
    expect(body).toContain(`${active ? 'Active' : 'Inactive'}: WALDERP / 2 deaths remaining / Waiting for next spawn`);
    expect(body).not.toMatch(/aria-describedby="manual-titan-at-help"[^>]*checked/);
  });

  it('does not invent idle state when connected telemetry is missing', () => {
    const { body } = render(ManualTitanFights, { props: { settings: {}, connected: true, onedit: vi.fn() } });
    expect(body).toContain('Manual titan status not reported.');
    expect(body).not.toContain('Inactive:');
  });

  it.each([true, false])('reports failed exact restoration separately from active combat when enabled=%s', enabled => {
    const { body } = render(ManualTitanFights, { props: {
      settings: { _manualTitanEnabled: enabled }, connected: true,
      telemetry: { active: true, recovering: true, recoveryError: 'Original helmet is unavailable', zone: 16, remainingDeaths: 2, status: 'Recovery pending' }, onedit: vi.fn()
    } });
    expect(body).toContain('Recovering gear: WALDERP / Combat blocked');
    expect(body).not.toContain('Active: WALDERP');
    expect(body).toMatch(/class="notice">All combat stays blocked until original gear is restored[^<]*even if manual titan fights are disabled/);
    expect(body).toMatch(/role="alert"[^>]*>[\s\S]*Exact gear restore failed:[\s\S]*Original helmet is unavailable/);
  });

  it('renders recovery without inventing an error or requiring active telemetry', () => {
    const { body } = render(ManualTitanFights, { props: {
      settings: {}, connected: true, telemetry: { recovering: true, zone: 16 }, onedit: vi.fn()
    } });
    expect(body).toContain('Recovering gear: WALDERP / Combat blocked');
    expect(body).toContain('All combat stays blocked until original gear is restored');
    expect(body).not.toContain('role="alert"');
    expect(body).not.toContain('Manual titan status not reported.');
  });

  it('hides stale runtime state when disconnected', () => {
    const { body } = render(ManualTitanFights, { props: { settings: {}, telemetry: { active: true, recovering: true, recoveryError: 'Original helmet is unavailable', remainingDeaths: 2, zone: 16, status: 'Fighting' }, onedit: vi.fn() } });
    expect(body).toContain('Disconnected');
    expect(body).not.toContain('Active: WALDERP');
    expect(body).not.toContain('Fighting');
    expect(body).not.toContain('Recovering gear:');
    expect(body).not.toContain('Original helmet is unavailable');
    expect(body).not.toContain('role="alert"');
  });
});
