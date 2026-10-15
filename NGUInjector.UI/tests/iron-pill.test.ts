import { describe, expect, it } from 'vitest';
import { render } from 'svelte/server';
import IronPillHistory from '../src/lib/IronPillHistory.svelte';
import IronPillRules from '../src/lib/IronPillRules.svelte';

describe('Iron Pill history', () => {
  it('shows the automatic-only empty state when history is absent', () => {
    const { body } = render(IronPillHistory, { props: { history: undefined } });
    expect(body).toContain('No automatic casts yet.');
    expect(body).toMatch(/<details><summary>About this history<\/summary>[\s\S]*Latest 10 successful automatic casts; manual casts and failures are excluded\.[\s\S]*<\/details>/);
    expect(body).not.toContain('<details open');
  });
  it('renders readable wait, pill worth, UTC timestamp, and rebirth duration', () => {
    const { body } = render(IronPillHistory, { props: { history: [{ CastAtUtc: '2026-10-14T12:30:00Z', WaitSeconds: 900, Worth: 1234.5, RebirthSeconds: 90061 }] } });
    expect(body).toContain('2026-10-14 12:30:00 UTC');
    expect(body).toContain('0h 15m 0s');
    expect(body).toContain('1,234.5');
    expect(body).toContain('25h 1m 1s');
    expect(body).toMatch(/<h3[^>]*>Recent casts<\/h3>/);
    expect(body).not.toContain('<input');
    expect(body).not.toContain('<button');
  });
  it('shows large and tiny worth without rounding them to zero', () => {
    const { body } = render(IronPillHistory, { props: { history: [1e8, 1e-8].map(Worth => ({ CastAtUtc: '2026-10-14T12:00:00Z', WaitSeconds: 0, Worth, RebirthSeconds: 3600 })) } });
    expect(body).toContain('1.000e+8');
    expect(body).toContain('1.000e-8');
  });
  it('limits to the newest ten casts without mutating the source', () => {
    const history = Array.from({ length: 12 }, (_, index) => ({ CastAtUtc: `2026-10-14T12:${String(index).padStart(2, '0')}:00Z`, WaitSeconds: index, Worth: index, RebirthSeconds: 3600 }));
    const { body } = render(IronPillHistory, { props: { history } });
    expect(body.match(/<time /g)).toHaveLength(10);
    expect(body).not.toContain('2026-10-14 12:00:00 UTC');
    expect(body).not.toContain('2026-10-14 12:01:00 UTC');
    expect(body.indexOf('12:11:00 UTC')).toBeLessThan(body.indexOf('12:02:00 UTC'));
    expect(history[0].WaitSeconds).toBe(0);
  });
  it('handles malformed history values without misleading numbers or crashing', () => {
    const { body } = render(IronPillHistory, { props: { history: [null, { CastAtUtc: 'invalid', WaitSeconds: -1, Worth: null, RebirthSeconds: 'bad' }] } });
    expect(body).toContain('Unknown timestamp');
    expect(body.match(/unknown/g)).toHaveLength(3);
    expect(body).not.toContain('NaN');
  });
});

describe('Iron Pill casting status', () => {
  it('shows the actual runtime blocker without enabling rebirth implicitly', () => {
    const { body } = render(IronPillRules, { props: { connected: true, data: { available: true, eligible: false, status: 'blocked', reason: 'Custom auto-rebirth is disabled.' } } });
    expect(body).toContain('Blocked: Custom auto-rebirth is disabled.');
    expect(body).toMatch(/<details><summary>Casting rules<\/summary>[\s\S]*Run a plan that allows rebirth\. Enable auto-rebirth and auto blood spells\.[\s\S]*<\/details>/);
    expect(body).not.toContain('<details open');
  });
  it('does not promise immediate casting when eligible', () => {
    const { body } = render(IronPillRules, { props: { connected: true, data: { available: true, eligible: true } } });
    expect(body).toContain('Eligible. Waiting for cooldown and a casting threshold.');
  });
  it('distinguishes missing eligibility exports from an evaluated blocker', () => {
    const { body } = render(IronPillRules, { props: { connected: true, data: {} } });
    expect(body).toContain('Iron Pill status unavailable.');
    expect(body).not.toContain('Eligible.');
    expect(body).not.toContain('Blocked:');
  });
  it('does not display stale eligibility while disconnected', () => {
    const { body } = render(IronPillRules, { props: { connected: false, data: { available: true, eligible: true } } });
    expect(body).toContain('Connect to see Iron Pill status.');
    expect(body).not.toContain('Eligible.');
  });
  it('defines actual worth as capped base Power and Toughness gains', () => {
    const { body } = render(IronPillRules);
    expect(body).toContain('base Power and Toughness gained each');
    expect(body).toContain('capped at 100 million');
  });
  it('explains that reservation yields to manual actions', () => {
    const { body } = render(IronPillRules);
    expect(body).toContain('Manual rebirth and boss push take priority over blood reservation');
  });
  it('describes threshold OR logic, disabled thresholds, actual eligibility, and reservation lookahead', () => {
    const { body } = render(IronPillRules);
    expect(body).toContain('either the saving time or minimum worth');
    expect(body).toContain('Zero disables a threshold. Both zero: cast as soon as ready');
    expect(body).toContain('1h of actual rebirth time and boss stage 100');
    expect(body).toContain('Planned duration does not count');
    expect(body).toContain('ready or within the saving-time cooldown window');
    expect(body).toContain('Worth-only mode uses a 900s lookahead');
    expect(body).not.toContain('15m before casting');
  });
});
