import { describe, expect, it } from 'vitest';
import { render } from 'svelte/server';
import { parseJson, stringifyJson, type JsonValue } from '../src/lib/api';
import RebirthHistory from '../src/lib/RebirthHistory.svelte';

function cells(body: string): string[] {
  return [...body.matchAll(/<td\b[^>]*>([\s\S]*?)<\/td>/g)]
    .map(match => match[1].replace(/<!--[^]*?-->/g, '').trim());
}

describe('Rebirth history', () => {
  it('renders a captioned read-only table with local timestamps and readable durations', () => {
    const { body } = render(RebirthHistory, { props: { history: [{ TimestampLocal: '2026-10-19 12:30:00', Reason: 'Time trigger', RebirthSeconds: 3661, BossId: 42, NumberBefore: 100, NumberAfter: 250, NumberMultiplier: 2.5 }] } });
    expect(body).toMatch(/<table\b[^>]*>\s*<caption[^>]*>Rebirth history<\/caption>/);
    expect([...body.matchAll(/<th\b[^>]*scope="col"[^>]*>(.*?)<\/th>/g)].map(match => match[1]))
      .toEqual(['Local timestamp', 'Reason', 'Duration', 'Boss', 'NUMBER before', 'NUMBER after', 'Gain', 'Details']);
    expect(body).toMatch(/<thead>\s*<tr>/);
    expect(body).toMatch(/<tbody>\s*(?:<!--[^]*?-->\s*)*<tr>/);
    expect(cells(body).slice(0, 7)).toEqual(['2026-10-19 12:30:00', 'Time trigger', '1h 1m 1s', '42', '100', '250', '2.5x']);
    expect(body).toMatch(/<div\b[^>]*role="region"[^>]*aria-label="Rebirth history table"[^>]*tabindex="0"/);
    expect(body).not.toMatch(/<(?:input|button|select|textarea)\b/);
  });

  it('preserves lossless API numeric values in the table cells', () => {
    const history = parseJson('[{"BossId":9007199254740993,"NumberBefore":9007199254740993,"NumberAfter":9007199254740995,"NumberMultiplier":1.0000000000000001}]');
    const { body } = render(RebirthHistory, { props: { history } });
    expect(cells(body).slice(3, 7)).toEqual(['9007199254740993', '9007199254740993', '9007199254740995', '1.0000000000000001x']);
    expect(body).not.toContain('9007199254740992');
  });

  it('keeps unknown fields in pretty JSON inside a closed row disclosure', () => {
    const history = parseJson('[{"Reason":"Manual","Future":{"counter":9007199254740993,"note":"<future & value>"}}]');
    const { body } = render(RebirthHistory, { props: { history } });
    const disclosures = [...body.matchAll(/<details\b([^>]*)>([\s\S]*?)<\/details>/g)];
    expect(disclosures).toHaveLength(1);
    expect(disclosures[0][1]).not.toMatch(/\bopen\b/);
    expect(disclosures[0][2]).toMatch(/^<summary[^>]*>Details<\/summary>\s*<pre\b/);
    expect(disclosures[0][2]).toContain('"Future": {\n    "counter": 9007199254740993,');
    expect(disclosures[0][2]).toContain('"note": "&lt;future &amp; value>"');
    expect(body.replace(/<details\b[^]*?<\/details>/g, '')).not.toContain('Future');
  });

  it('defaults to the empty state when no history prop is supplied', () => {
    const { body } = render(RebirthHistory);
    expect(body).toContain('No rebirths yet.');
    expect(body).not.toMatch(/<table\b/);
  });

  it.each(([undefined, null, [], {}, 'invalid', 42, true] satisfies (JsonValue | undefined)[]).map(history => ({ history })))('handles empty or malformed history: $history', ({ history }) => {
    const { body } = render(RebirthHistory, { props: { history } });
    expect(body).toContain('No rebirths yet.');
    expect(body).not.toMatch(/<table\b|<details\b/);
  });

  it.each(Array.of<JsonValue>(null, 'invalid', 42, [], {}, { RebirthSeconds: '3661', NumberMultiplier: null }).map(entry => ({ entry })))('uses safe fallbacks for malformed entries: $entry', ({ entry }) => {
    const { body } = render(RebirthHistory, { props: { history: [entry] } });
    expect(cells(body).slice(0, 7)).toEqual(['Unknown time', 'Rebirth', 'Unknown', '?', '?', '?', '?']);
    expect(body).not.toContain('?x');
  });

  it.each([-1, NaN, Infinity])('does not format an invalid duration: %s', RebirthSeconds => {
    const { body } = render(RebirthHistory, { props: { history: [{ RebirthSeconds }] } });
    expect(cells(body)[2]).toBe('Unknown');
  });

  it('retains zero values rather than replacing them with missing-value markers', () => {
    const { body } = render(RebirthHistory, { props: { history: [{ RebirthSeconds: 0, BossId: 0, NumberBefore: 0, NumberAfter: 0, NumberMultiplier: 0 }] } });
    expect(cells(body).slice(2, 7)).toEqual(['0h 0m 0s', '0', '0', '0', '0x']);
  });

  it('leaves entry order and all source data unchanged', () => {
    const history = parseJson('[{"TimestampLocal":"older","Future":{"counter":9007199254740993}},{"TimestampLocal":"newer","NumberBefore":9007199254740995},null]');
    const before = stringifyJson(history);
    const { body } = render(RebirthHistory, { props: { history } });
    expect(cells(body).filter((_, index) => index % 8 === 0)).toEqual(['older', 'newer', 'Unknown time']);
    expect(stringifyJson(history)).toBe(before);
  });
});
