import { describe, expect, it, vi } from 'vitest';
import { render } from 'svelte/server';
import BloodGoals from '../src/lib/BloodGoals.svelte';

const data = {
  spaghetti: { goalPercent: 15, currentPercent: 10, requiredBlood: 163840000, remainingBlood: 53840000, bloodPerSecond: 1000, estimatedSeconds: 53840, status: 'Casting', productionAssumption: 'Optimal ritual 3 with all 2,000,000 magic' },
  counterfeit: { goalPercent: 100, currentPercent: 100, requiredBlood: 512000000, remainingBlood: 0, bloodPerSecond: 1000, estimatedSeconds: 0, status: 'Goal reached', productionAssumption: 'Optimal ritual 3 with all 2,000,000 magic' }
};

describe('blood goal display', () => {
  it('reports unavailable estimates without placeholder percentages', () => {
    const { body } = render(BloodGoals, { props: { data: {}, connected: true } });
    expect(body).toContain('Blood estimates unavailable.');
    expect(body).not.toContain('unknown%');
  });
  it('surfaces an explicit calculation failure without stale goal numbers', () => {
    const { body } = render(BloodGoals, { props: { data: { ...data, available: false, status: 'error', reason: 'Estimate calculation failed.' }, connected: true } });
    expect(body).toContain('Estimate calculation failed.');
    expect(body).not.toContain('~14h');
    expect(body).not.toContain('15%');
  });
  it('shows the saved goal and time using the optimal full-magic projection', () => {
    const { body } = render(BloodGoals, { props: { data, connected: true } });
    expect(body.replace(/<!--[\s\S]*?-->/g, '').replace(/<[^>]*>/g, '')).toContain('10% / 15%');
    expect(body).toContain('ETA ~14h 57m 20s');
    expect(body).toContain('title="ETA assumes all magic with current gear."');
    expect(body).toContain('Goal reached');
    expect(body).toContain('53.84M');
    expect(body).toContain('53.84M blood left');
    expect(body).not.toContain('Ritual details');
    expect(body).not.toContain('Optimal ritual');
    expect(body).not.toContain('Total blood needed');
    expect(body).not.toContain('1K/s');
    expect(body).not.toContain('0 blood left');
  });
  it('hides stale countdowns when disconnected', () => {
    const { body } = render(BloodGoals, { props: { data, connected: false } });
    expect(body).toContain('Connect to see estimates.');
    expect(body).not.toContain('~14h');
    expect(body).not.toContain('53.84M');
  });
  it.each([true, false])('keeps saved goals editable without estimates when connected=%s', connected => {
    const beginGoalEdit = vi.fn();
    const { body } = render(BloodGoals, { props: { data: { ...data, available: false }, connected, editable: true, beginGoalEdit, savedGoals: { spaghetti: 45, counterfeit: 500 } } });
    expect(body).toMatch(/aria-label="Edit Spaghetti goal \(%\)"[^>]*>45%/);
    expect(body).toMatch(/aria-label="Edit Counterfeit goal \(%\)"[^>]*>500%/);
    expect(body).not.toMatch(/<button\b[^>]*disabled/);
    expect(body).not.toContain('~14h');
    expect(body).not.toContain('53.84M');
    expect(beginGoalEdit).not.toHaveBeenCalled();
  });
  it('replaces stale estimates while edits are saving', () => {
    const { body } = render(BloodGoals, { props: { data, connected: true, pending: true } });
    expect(body).toContain('Updating estimate...');
    expect(body).not.toContain('~14h');
    expect(body).not.toContain('53.84M');
    expect(body).not.toContain('Save Settings');
  });
  it('does not show ritual details when no ritual information is supplied', () => {
    const { productionAssumption: _, ...spaghetti } = data.spaghetti;
    const { body } = render(BloodGoals, { props: { data: { spaghetti }, connected: true } });
    expect(body).toContain('53.84M blood left');
    expect(body).not.toContain('Ritual details');
  });
  it('shows an optimizer blocker instead of a countdown', () => {
    const reason = 'Blood Magic skipped: no ritual keeps gold positive at 100/s gross GPS.';
    const { body } = render(BloodGoals, { props: { data: { spaghetti: { ...data.spaghetti, bloodPerSecond: 0, estimatedSeconds: null, status: reason, productionAssumption: reason } }, connected: true } });
    expect(body).toContain(reason);
    expect(body).not.toContain('~14h');
    expect(body).toContain('Estimate unavailable.');
    expect(body).not.toContain('unknown%');
  });
  it('waits for runtime estimates to catch up with an edited goal', () => {
    const { body } = render(BloodGoals, { props: { data, connected: true, savedGoals: { spaghetti: 30 } } });
    expect(body).toContain('Updating estimate...');
    expect(body).not.toContain('~14h');
    expect(body).not.toContain('53.84M');
    expect(body).toContain('Goal reached');
  });
  it.each([true, false])('preserves inline goal controls with editable=%s', editable => {
    const beginGoalEdit = vi.fn();
    const { body } = render(BloodGoals, { props: { data, connected: true, editable, beginGoalEdit, savedGoals: { spaghetti: 30 } } });
    expect(body).toMatch(/aria-label="Edit Spaghetti goal \(%\)"[^>]*>30%/);
    expect(body).toContain('aria-label="Edit Counterfeit goal (%)"');
    expect(body.match(/<button\b[^>]*disabled/g) ?? []).toHaveLength(editable ? 0 : 2);
    expect(body).toContain('Updating estimate...');
    expect(beginGoalEdit).not.toHaveBeenCalled();
  });
  it.each(['Spell off / paused', 'Enough blood; awaiting cast', 'Disabled (0%)'])('keeps actionable status: %s', status => {
    const { body } = render(BloodGoals, { props: { data: { spaghetti: { ...data.spaghetti, status, estimatedSeconds: null } }, connected: true } });
    expect(body).toContain(status);
    expect(body).not.toContain('~14h');
  });
});
