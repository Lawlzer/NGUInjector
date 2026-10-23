import { describe, expect, it } from 'vitest';
import { render } from 'svelte/server';
import IronPillHistory from '../src/lib/IronPillHistory.svelte';
import IronPillRules from '../src/lib/IronPillRules.svelte';

function visibleStatus(body: string) {
  return body.replace(/<!--[^]*?-->/g, '').replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim();
}

describe('Iron Pill history', () => {
  it('shows the automatic-only empty state when history is absent', () => {
    const { body } = render(IronPillHistory, { props: { history: undefined } });
    expect(body).toContain('No automatic casts yet.');
    expect(body).not.toContain('<time');
    expect(body).not.toMatch(/<details\b/);
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
    expect(visibleStatus(body)).toBe('Blocked: Custom auto-rebirth is disabled.');
    expect(body).not.toContain('Casting rules');
    expect(body).not.toContain('<details');
  });
  it('omits routine eligible status without a casting rules section', () => {
    const { body } = render(IronPillRules, { props: { connected: true, data: { available: true, eligible: true } } });
    expect(visibleStatus(body)).toBe('');
    expect(body).not.toContain('role="status"');
    expect(body).not.toContain('Casting rules');
    expect(body).not.toContain('<details');
  });
  it('shows active reservation without describing a whole-stage takeover', () => {
    const { body } = render(IronPillRules, { props: { connected: true, data: { available: true, eligible: true, reservingBlood: true } } });
    expect(visibleStatus(body)).toBe('Saving blood for Iron Pill.');
    expect(body).toMatch(/role="status"[^>]*title="[^"]*except during Time Machine and Wandoos stages/);
    expect(body).not.toContain('Eligible. Waiting for cooldown');
    expect(body).not.toContain('focus Augments + Blood');
  });
  it('shows the remaining retry delay after restoring normal priorities', () => {
    const { body } = render(IronPillRules, { props: { connected: true, data: { available: true, eligible: true, reservingBlood: false, retryInSeconds: 899.1 } } });
    expect(visibleStatus(body)).toBe('Retry in 900s.');
    expect(body).toContain('title="Normal priorities restored."');
    expect(body).not.toContain('Eligible. Waiting for cooldown');
  });
  it('gives an eligibility blocker precedence over stale saving state', () => {
    const { body } = render(IronPillRules, { props: { connected: true, data: { available: true, eligible: false, reservingBlood: true, retryInSeconds: 900, reason: 'Boss push is running.' } } });
    expect(body).toContain('Blocked: Boss push is running.');
    expect(body).not.toContain('Saving blood for Iron Pill.');
    expect(visibleStatus(body)).not.toContain('Retry in');
  });
  it.each([0, -1, NaN, Infinity, '900'])('ignores unavailable or invalid retry delays: %s', retryInSeconds => {
    const { body } = render(IronPillRules, { props: { connected: true, data: { available: true, eligible: true, retryInSeconds } } });
    expect(visibleStatus(body)).toBe('');
    expect(body).not.toContain('role="status"');
  });
  it('distinguishes missing eligibility exports from an evaluated blocker', () => {
    const { body } = render(IronPillRules, { props: { connected: true, data: {} } });
    expect(body).toContain('Iron Pill status unavailable.');
    expect(body).not.toContain('Eligible.');
    expect(body).not.toContain('Blocked:');
  });
  it('does not display stale eligibility while disconnected', () => {
    const { body } = render(IronPillRules, { props: { connected: false, data: { available: true, eligible: true, reservingBlood: true, retryInSeconds: 900 } } });
    expect(body).toContain('Connect to see Iron Pill status.');
    expect(body).not.toContain('Eligible.');
    expect(body).not.toContain('Saving blood for Iron Pill.');
    expect(visibleStatus(body)).not.toContain('Retry in');
  });
  it('does not render casting rules while disconnected', () => {
    const { body } = render(IronPillRules);
    expect(visibleStatus(body)).toBe('Connect to see Iron Pill status.');
    expect(body).not.toContain('Casting rules');
    expect(body).not.toContain('<details');
    expect(body).not.toContain('<li');
  });
});
