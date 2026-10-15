import { describe, expect, it } from 'vitest';
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
    expect(body).not.toContain('about 14h');
    expect(body).not.toContain('15% goal');
  });
  it('shows the saved goal and time using the optimal full-magic projection', () => {
    const { body } = render(BloodGoals, { props: { data, connected: true } });
    expect(body.replace(/<!--[\s\S]*?-->/g, '').replace(/<[^>]*>/g, '')).toContain('15% goal');
    expect(body).toContain('about 14h 57m 20s');
    expect(body).toContain('Goal reached');
    expect(body).toContain('53.84M');
    expect(body).toContain('53.84M blood remaining; 1K/s');
    expect(body).toContain('Estimate: all magic, current gear.');
    expect(body).toMatch(/<details><summary>Ritual details<\/summary>[\s\S]*Optimal ritual 3 with all 2,000,000 magic[\s\S]*<\/details>/);
    expect(body).not.toContain('<details open');
  });
  it('hides stale countdowns when disconnected', () => {
    const { body } = render(BloodGoals, { props: { data, connected: false } });
    expect(body).toContain('Connect to see estimates.');
    expect(body).not.toContain('about 14h');
    expect(body).not.toContain('53.84M');
  });
  it('labels estimates as saved goals while edits are pending', () => {
    const { body } = render(BloodGoals, { props: { data, connected: true, pending: true } });
    expect(body).toContain('Using saved goals. Save Settings to update estimates.');
  });
  it('does not show ritual details when no ritual information is supplied', () => {
    const { productionAssumption: _, ...spaghetti } = data.spaghetti;
    const { body } = render(BloodGoals, { props: { data: { spaghetti }, connected: true } });
    expect(body).toContain('53.84M blood remaining; 1K/s');
    expect(body).not.toContain('Ritual details');
  });
  it('shows an optimizer blocker instead of a countdown', () => {
    const reason = 'Blood Magic skipped: no ritual keeps gold positive at 100/s gross GPS.';
    const { body } = render(BloodGoals, { props: { data: { spaghetti: { ...data.spaghetti, bloodPerSecond: 0, estimatedSeconds: null, status: reason, productionAssumption: reason } }, connected: true } });
    expect(body).toContain(reason);
    expect(body).not.toContain('about 14h');
    expect(body).toContain('Estimate unavailable.');
    expect(body).not.toContain('unknown%');
  });
});
