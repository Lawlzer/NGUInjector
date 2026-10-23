import { describe, expect, it, vi } from 'vitest';
import { render } from 'svelte/server';
import EquipmentBoosts from '../src/lib/EquipmentBoosts.svelte';

const items = [
  { id: 100, name: 'Helmet', copies: 2, level: 100, fullyBoosted: false },
  { id: 101, name: 'Sword', copies: 1, level: 20, fullyBoosted: true }
];

describe('equipment boost priorities', () => {
  it('loads owned equipment without creating settings edits', () => {
    const onedit = vi.fn();
    const settings = { _customAutoBoostEnabled: true, _priorityBoosts: [999, 101], _boostBlacklist: [100] };
    const { body } = render(EquipmentBoosts, { props: { settings, telemetry: { available: true, items }, connected: true, onedit } });
    expect(body.match(/<li\b/g)).toHaveLength(2);
    expect(body.indexOf('<strong>Sword')).toBeLessThan(body.indexOf('<strong>Helmet'));
    expect(body).toContain('#100 / Level 100 / 2 copies / Needs boosts');
    expect(body).toContain('#101 / Level 20 / 1 copy / Fully boosted');
    expect(body).not.toMatch(/autosave|save automatically|storage|polling/i);
    expect(body).toMatch(/aria-label="Boost Sword"[^>]*checked/);
    expect(body).not.toMatch(/aria-label="Boost Helmet"[^>]*checked/);
    expect(body).toMatch(/<option value="1" selected="">2<\/option>/);
    expect(settings).toEqual({ _customAutoBoostEnabled: true, _priorityBoosts: [999, 101], _boostBlacklist: [100] });
    expect(onedit).not.toHaveBeenCalled();
  });

  it('offers default and a first priority when no priorities are saved', () => {
    const { body } = render(EquipmentBoosts, { props: { settings: {}, telemetry: { available: true, items }, connected: true, onedit: vi.fn() } });
    expect(body.match(/<option value="" selected="">Default<\/option>/g)).toHaveLength(2);
    expect(body.match(/<option value="0">1 \(first\)<\/option>/g)).toHaveLength(2);
    expect(body).toContain('Auto boost is off. Enable it in Adventure');
  });

  it('hides only fully boosted equipment at level 100 or higher without editing preferences', () => {
    const onedit = vi.fn();
    const settings = { _priorityBoosts: [102, 101], _boostBlacklist: [102, 103] };
    const { body } = render(EquipmentBoosts, { props: {
      settings, connected: true, onedit,
      telemetry: { available: true, items: [
        ...items,
        { id: 102, name: 'Completed Boots', copies: 2, level: 100, fullyBoosted: true },
        { id: 103, name: 'Overlevel Chest', copies: 1, level: 120, fullyBoosted: true },
        { id: 104, name: 'Almost Leveled', copies: 2, level: 99, fullyBoosted: true }
      ] }
    } });
    expect(body.match(/<li\b/g)).toHaveLength(3);
    expect(body).toContain('<strong>Helmet');
    expect(body).toContain('<strong>Sword');
    expect(body).toContain('<strong>Almost Leveled');
    expect(body).not.toContain('Completed Boots');
    expect(body).not.toContain('Overlevel Chest');
    expect(settings).toEqual({ _priorityBoosts: [102, 101], _boostBlacklist: [102, 103] });
    expect(onedit).not.toHaveBeenCalled();
  });

  it('shows the empty state when all received equipment is complete', () => {
    const { body } = render(EquipmentBoosts, { props: {
      settings: {}, connected: true, onedit: vi.fn(),
      telemetry: { available: true, items: [{ id: 100, name: 'Helmet', copies: 1, level: 100, fullyBoosted: true }] }
    } });
    expect(body).toContain('No equipment needs levels or boosts.');
    expect(body).not.toContain('<select');
    expect(body).not.toContain('<strong>Helmet');
  });

  it('shows an empty state for a readable list with no incomplete equipment', () => {
    const { body } = render(EquipmentBoosts, { props: { settings: {}, telemetry: { available: true, items: [] }, connected: true, onedit: vi.fn() } });
    expect(body).toContain('No equipment needs levels or boosts.');
    expect(body).not.toContain('<select');
  });

  it('does not display stale equipment while disconnected', () => {
    const { body } = render(EquipmentBoosts, { props: { settings: {}, telemetry: { available: true, items }, onedit: vi.fn() } });
    expect(body).toContain('Connect to load owned equipment.');
    expect(body).not.toContain('<strong>Helmet');
    expect(body).not.toContain('<select');
  });

  it.each([
    [{}, 'Equipment list unavailable. Update and reconnect the injector.'],
    [{ available: false, reason: 'Inventory is loading.', items }, 'Inventory is loading.']
  ])('reports unavailable telemetry without displaying partial rows', (telemetry, message) => {
    const { body } = render(EquipmentBoosts, { props: { settings: {}, telemetry, connected: true, onedit: vi.fn() } });
    expect(body).toContain(message);
    expect(body).not.toContain('<select');
  });

  it('disables controls when settings are not writable', () => {
    const { body } = render(EquipmentBoosts, { props: { settings: {}, telemetry: { available: true, items }, connected: true, disabled: true, onedit: vi.fn() } });
    expect(body.match(/<select[^>]*disabled/g)).toHaveLength(2);
    expect(body.match(/<input[^>]*disabled/g)).toHaveLength(2);
  });
});
