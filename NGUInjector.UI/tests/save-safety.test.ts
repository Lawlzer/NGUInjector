import { describe, expect, it } from 'vitest';
import { render } from 'svelte/server';
import SaveSafety from '../src/lib/SaveSafety.svelte';

function report(overrides: Record<string, unknown> = {}) {
  return {
    version: 1, status: 'behind', message: 'Loaded save has less total playtime than a decoded save.',
    current: { totalPlaytimeSeconds: '360000', rebirth: '99', rebirthSeconds: '7200' },
    longest: { path: 'C:\\NGU saves\\longest-known.txt', sha256: 'a'.repeat(64), totalPlaytimeSeconds: '720000', rebirth: '98', rebirthSeconds: '3600' },
    testActive: false, scanComplete: true, errors: [], ...overrides
  };
}

function verifiedReport(overrides: Record<string, unknown> = {}) {
  return report({ status: 'verified', message: 'Loaded save verified.',
    current: { totalPlaytimeSeconds: '720000', rebirth: '100', rebirthSeconds: '0' }, ...overrides });
}

function banner(data: unknown, connected = true): string {
  return render(SaveSafety, { props: { data, connected } }).body;
}

function text(body: string): string {
  return body.replace(/<!--[^]*?-->/g, '').replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim();
}

describe('Save safety warning', () => {
  it.each([
    { totalPlaytimeSeconds: '360000', rebirth: '99', rebirthSeconds: '7200', expected: '100 h' },
    { totalPlaytimeSeconds: '1.8E+5', rebirth: '200', rebirthSeconds: '86400', expected: '50 h' }
  ])('renders an older-save fixture prominently using total hours: $totalPlaytimeSeconds', ({ expected, ...current }) => {
    const body = banner(report({ current }));
    expect(body).toMatch(/<aside\b[^>]*class="save-safety[^\"]*critical[^\"]*"[^>]*role="alert"[^>]*aria-label="Save safety"/);
    expect(text(body)).toContain(`Loaded save is behind Loaded save has less total playtime than a decoded save. Loaded save: ${expected} / Longest known: 200 h`);
    expect(text(body)).toContain('Longest decoded save: C:\\NGU saves\\longest-known.txt');
    expect(text(body)).toContain('Do not overwrite your saves. Check the longest known save before continuing.');
  });

  it('keeps recovery critical even while a test is active', () => {
    const body = banner(report({ status: 'recoveryRequired', testActive: true, message: 'Original save restoration could not be verified.' }));
    expect(body).toContain('role="alert"');
    expect(body).toMatch(/class="save-safety[^\"]*critical/);
    expect(text(body)).toContain('Save recovery required Original save restoration could not be verified.');
    expect(text(body)).toContain('Save testing is active.');
    expect(text(body)).toContain('Preserve your saves and resolve recovery before continuing.');
    expect(text(body)).not.toContain('Loaded save is behind');
  });

  it('does not downgrade a behind-save error during testing or a failed scan', () => {
    const body = banner(report({ testActive: true, scanComplete: false, errors: ['Could not decode broken-save.txt.'] }));
    expect(body).toMatch(/class="save-safety[^\"]*critical/);
    expect(text(body)).toContain('Loaded save is behind');
    expect(text(body)).toContain('Save testing is active. Save scan is incomplete. Could not decode broken-save.txt.');
  });

  it.each([
    { status: 'testing', testActive: false },
    { status: 'verified', testActive: true },
    { status: 'unknown', testActive: true }
  ])('warns during testing without claiming verified: %j', overrides => {
    const body = banner(verifiedReport({ ...overrides, message: 'Test save loaded.' }));
    expect(body).toContain('role="alert"');
    expect(body).not.toMatch(/class="save-safety[^\"]*critical/);
    expect(text(body)).toContain('Save testing active');
    expect(text(body)).toContain('Loaded save: 200 h / Longest known: 200 h');
    expect(text(body)).toContain('longest-known.txt');
    expect(text(body)).toContain('Keep test saves separate from your normal saves.');
    expect(text(body)).not.toContain('Loaded save verified.');
  });

  it('shows scan failures without inventing missing playtime', () => {
    const body = banner(report({ status: 'unknown', message: 'Save scan failed.', current: null, scanComplete: false,
      errors: ['Could not decode old.txt.', { path: 'C:\\NGU saves\\locked.txt', message: 'Access denied.' }, null] }));
    expect(body).toContain('role="alert"');
    expect(text(body)).toContain('Save safety unknown Save scan failed. Loaded save: Unavailable / Longest known: 200 h');
    expect(body).toContain('aria-label="Save scan errors"');
    expect(text(body)).toContain('Save scan is incomplete. Could not decode old.txt. C:\\NGU saves\\locked.txt: Access denied. Save scan error.');
    expect(text(body)).toContain('Check your saves and resolve any scan errors before continuing.');
    expect(text(body)).not.toContain('Loaded save: 0 h');
  });

  it('clears the alert only for a complete, fresh verified report', () => {
    expect(banner(verifiedReport())).not.toContain('role="alert"');
    expect(banner(verifiedReport({ current: { totalPlaytimeSeconds: '723600', rebirth: '101', rebirthSeconds: '1' } }))).not.toContain('role="alert"');
  });

  it.each([
    { version: 2 }, { version: undefined }, { status: 'future', message: 'Status unavailable.' }, { testActive: undefined }, { testActive: 'false' },
    { scanComplete: false }, { scanComplete: 'true' }, { errors: ['Scan failed.'] }, { errors: null }, { errors: undefined },
    { current: null }, { longest: null },
    { longest: { totalPlaytimeSeconds: '720000', rebirth: '98', rebirthSeconds: '3600', path: '', sha256: 'bad' } }
  ])('does not clear a warning for inconsistent verified telemetry: %j', overrides => {
    const body = banner(verifiedReport(overrides));
    expect(body).toContain('role="alert"');
    expect(text(body)).toContain('Save safety unknown');
    expect(text(body)).not.toContain('Loaded save verified.');
  });

  it.each(['current', 'longest'])('keeps a warning when %s rebirth seconds exceed total playtime', key => {
    const identity = { totalPlaytimeSeconds: '100', rebirth: '1', rebirthSeconds: '200' };
    const body = banner(verifiedReport({ [key]: key === 'longest' ? { ...report().longest, ...identity } : identity }));
    expect(body).toContain('role="alert"');
    expect(text(body)).toContain('Save safety unknown Save metadata is invalid or inconsistent.');
    expect(text(body)).not.toContain('Loaded save verified.');
  });

  it.each(['current', 'longest'])('accepts the %s rebirth clock at the total-playtime boundary', key => {
    const identity = { ...report()[key as 'current' | 'longest'], totalPlaytimeSeconds: '720000', rebirthSeconds: '720000' };
    expect(banner(verifiedReport({ [key]: identity }))).not.toContain('role="alert"');
  });

  it.each(['current', 'longest'])('rejects a %s rebirth count one above Int64 without rounding', key => {
    const base = verifiedReport();
    const body = banner(verifiedReport({ [key]: { ...base[key as 'current' | 'longest'], rebirth: '9223372036854775808' } }));
    expect(body).toContain('role="alert"');
    expect(text(body)).toContain('Save metadata is invalid or inconsistent.');
    expect(text(body)).not.toContain('Loaded save verified.');
  });

  it('accepts the exact maximum Int64 rebirth count and optional older-schema fields', () => {
    const data = verifiedReport();
    data.current.rebirth = '9223372036854775807';
    data.longest.rebirth = '9223372036854775807';
    expect(banner(data)).not.toContain('role="alert"');
    expect(banner({ ...data, ambiguousLongest: false, pendingFiles: 0, knownFiles: 1 })).not.toContain('role="alert"');
  });

  it.each(['verified', 'unknown'])('warns on ambiguous longest saves despite %s status', status => {
    const body = banner(verifiedReport({ status, ambiguousLongest: true, message: '' }));
    expect(body).toContain('role="alert"');
    expect(body).not.toMatch(/class="save-safety[^\"]*critical/);
    expect(text(body)).toContain('Save safety unknown Different saves tie for longest playtime. Review the saved branches.');
    expect(text(body)).toContain('longest-known.txt');
  });

  it('uses valid decoded metadata for a red behind warning even with verified status and ambiguity', () => {
    const body = banner(verifiedReport({ current: report().current, ambiguousLongest: true }));
    expect(body).toMatch(/class="save-safety[^\"]*critical/);
    expect(text(body)).toContain('Loaded save is behind Different saves tie for longest playtime.');
    expect(text(body)).toContain('Loaded save: 100 h / Longest known: 200 h');
    expect(text(body)).not.toContain('Loaded save verified.');
  });

  it('reports a false verified status as behind from decoded playtime alone', () => {
    const body = banner(verifiedReport({ current: report().current }));
    expect(body).toMatch(/class="save-safety[^\"]*critical/);
    expect(text(body)).toContain('Loaded save is behind Loaded playtime is lower than the longest decoded save.');
    expect(text(body)).toContain('Do not overwrite your saves.');
  });

  it('keeps recovery priority over the computed behind state and ambiguity', () => {
    const body = banner(report({ status: 'recoveryRequired', ambiguousLongest: true, message: 'Save restoration failed.' }));
    expect(body).toMatch(/class="save-safety[^\"]*critical/);
    expect(text(body)).toContain('Save recovery required Save restoration failed.');
    expect(text(body)).not.toContain('Loaded save is behind');
  });

  it.each([1, -1, .5, '0', null, false, {}, [], Number.MAX_SAFE_INTEGER + 1].map(pendingFiles => ({ pendingFiles })))('rejects conflicting or invalid pendingFiles: $pendingFiles', ({ pendingFiles }) => {
    const body = banner(verifiedReport({ pendingFiles }));
    expect(body).toContain('role="alert"');
    expect(text(body)).toContain('Save safety unknown Save scan counts are inconsistent or incomplete.');
    expect(text(body)).not.toContain('Loaded save verified.');
    if (pendingFiles === 1) expect(text(body)).toContain('Save scan is incomplete.');
  });

  it.each([0, -1, .5, '1', null, false, {}, [], Number.MAX_SAFE_INTEGER + 1].map(knownFiles => ({ knownFiles })))('rejects invalid knownFiles: $knownFiles', ({ knownFiles }) => {
    const body = banner(verifiedReport({ knownFiles }));
    expect(body).toContain('role="alert"');
    expect(text(body)).toContain('Save safety unknown Save scan counts are inconsistent or incomplete.');
    expect(text(body)).not.toContain('Loaded save verified.');
  });

  it.each([undefined, null, '', ' ', 'NaN', 'Infinity', '-1', '1e999', 720000, {}, []].map(totalPlaytimeSeconds => ({ totalPlaytimeSeconds })))('does not coerce malformed total playtime $totalPlaytimeSeconds into a verified result', ({ totalPlaytimeSeconds }) => {
    const body = banner(verifiedReport({ current: { totalPlaytimeSeconds, rebirth: '100', rebirthSeconds: '1' } }));
    expect(body).toContain('role="alert"');
    expect(text(body)).toContain('Loaded save: Unavailable');
    expect(text(body)).not.toMatch(/NaN h|Infinity h|Loaded save verified/);
  });

  it('keeps valid zero playtime distinct from unavailable telemetry', () => {
    const data = report({ status: 'unknown', current: { totalPlaytimeSeconds: '0', rebirth: '0', rebirthSeconds: '0' } });
    expect(text(banner(data))).toContain('Loaded save: 0 h / Longest known: 200 h');
  });

  it('does not reuse a verified claim after the connection goes stale', () => {
    const body = banner(verifiedReport(), false);
    expect(body).toContain('role="alert"');
    expect(text(body)).toContain('Save safety unknown Disconnected: showing last reported save safety; current safety is unknown.');
    expect(text(body)).not.toContain('Loaded save verified.');
  });

  it.each(['behind', 'recoveryRequired'])('retains the last reported critical %s warning while disconnected', status => {
    const body = banner(report({ status }), false);
    expect(body).toMatch(/class="save-safety[^\"]*critical/);
    expect(text(body)).toContain(status === 'behind' ? 'Loaded save is behind' : 'Save recovery required');
    expect(text(body)).toContain('current safety is unknown.');
    expect(text(body)).toContain('longest-known.txt');
  });

  it.each([undefined, null].map(data => ({ data })))('uses a visible status warning for a connected older backend with saveSafety $data', ({ data }) => {
    const body = banner(data);
    expect(body).toContain('role="status"');
    expect(body).not.toContain('role="alert"');
    expect(body).toMatch(/<aside\b[^>]*class="save-safety[^\"]*"/);
    expect(body).not.toMatch(/\bcritical\b|\bhidden[=>\s]/);
    expect(text(body)).toBe('Save safety unavailable Update and reconnect the injector to check the loaded save.');
    expect(body).not.toContain('Longest known');
  });

  it('does not fabricate a safety report before connection', () => {
    expect(render(SaveSafety).body).not.toMatch(/role="(?:alert|status)"/);
  });

  it.each([false, true, 42, 'invalid', [], {}].map(data => ({ data })))('handles malformed runtime.saveSafety $data as an alert', ({ data }) => {
    const body = banner(data);
    expect(body).toContain('role="alert"');
    expect(text(body)).toMatch(/^Save safety (?:unavailable|unknown)/);
    expect(text(body)).not.toContain('Loaded save verified.');
  });

  it('escapes backend messages, paths and scan errors as text', () => {
    const data = report({ message: '<script>alert("message")</script>',
      longest: { ...report().longest, path: 'C:\\<img src=x onerror=alert(1)>\\save.txt' }, errors: ['<button>Dismiss</button>'] });
    const body = banner(data);
    expect(body).toContain('&lt;script>');
    expect(body).toContain('&lt;img');
    expect(body).toContain('&lt;button>Dismiss&lt;/button>');
    expect(body).not.toMatch(/<(?:script|img|button)\b/);
  });

  it('renders read-only without dismissal, loading, or source mutation', () => {
    const data = report({ status: 'recoveryRequired', testActive: true, Future: { exact: '9007199254740993' } });
    const before = JSON.stringify(data);
    Object.freeze(data.current);
    Object.freeze(data.longest);
    Object.freeze(data.errors);
    Object.freeze(data);
    const body = banner(data);
    expect(body).toContain('role="alert"');
    expect(body).not.toMatch(/<(?:button|input|select|textarea|a|details)\b|\bhidden[=>\s]/);
    expect(JSON.stringify(data)).toBe(before);
  });
});
